using System.Collections.Concurrent;
using System.Net.Sockets;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Network;

public sealed class TcpGameClient : IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<Message>> _pending = new();
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _connectionCancellation;
    private string? _playerId;
    private string? _playerName;
    private string? _sessionToken;
    private bool _intentionalDisconnect;
    private bool _reconnecting;

    public static TcpGameClient Shared { get; } = new();
    public bool IsConnected => _client?.Connected == true && _stream != null;
    public string? PlayerId => _playerId;

    public event EventHandler<Message>? EventReceived;
    public event EventHandler? ConnectionLost;
    public event EventHandler? Reconnected;

    private TcpGameClient() { }

    public async Task ConnectAsync(string playerId, string playerName)
    {
        await _connectionLock.WaitAsync();
        try
        {
            if (IsConnected) return;
            _intentionalDisconnect = false;
            _playerId = playerId;
            _playerName = playerName;
            await OpenSocketAsync();
            Message response = await SendRequestAsync(
                MessageType.Handshake,
                new HandshakeRequest { PlayerId = playerId, PlayerName = playerName });
            HandshakeResponse handshake = response.ReadPayload<HandshakeResponse>();
            _sessionToken = handshake.SessionToken;
            _ = HeartbeatLoopAsync(_connectionCancellation!.Token);
        }
        finally { _connectionLock.Release(); }
    }

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        MessageType type,
        TRequest payload)
    {
        Message response = await SendRequestAsync(type, payload);
        return response.ReadPayload<TResponse>();
    }

    private async Task<Message> SendRequestAsync<TRequest>(
        MessageType type,
        TRequest payload)
    {
        if (!IsConnected || _stream == null)
            throw new InvalidOperationException("TCP connection is not available.");

        Message request = Message.Create(type, payload);
        var completion = new TaskCompletionSource<Message>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(request.MessageId, completion))
            throw new InvalidOperationException("Duplicate request id.");

        try
        {
            await SendAsync(request);
            Task finished = await Task.WhenAny(
                completion.Task,
                Task.Delay(RequestTimeout));
            if (finished != completion.Task)
                throw new TimeoutException("Server response timed out.");

            Message response = await completion.Task;
            if (response.Type == MessageType.Error || response.Error != null)
                throw new InvalidOperationException(
                    response.Error?.Code + ": " + response.Error?.Message);
            return response;
        }
        finally { _pending.TryRemove(request.MessageId, out _); }
    }

    private async Task OpenSocketAsync()
    {
        CleanupSocket();
        _client = new TcpClient { NoDelay = true };
#if ANDROID
        const string host = "10.0.2.2";
#else
        const string host = "127.0.0.1";
#endif
        await _client.ConnectAsync(host, 5000);
        _stream = _client.GetStream();
        _connectionCancellation = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_connectionCancellation.Token);
    }

    private async Task SendAsync(Message message)
    {
        await _writeLock.WaitAsync();
        try
        {
            if (_stream == null) throw new IOException("TCP stream is closed.");
            await PacketCodec.WriteMessageAsync(
                _stream, message,
                _connectionCancellation?.Token ?? CancellationToken.None);
        }
        finally { _writeLock.Release(); }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream != null)
            {
                Message message = await PacketCodec.ReadMessageAsync(_stream, cancellationToken);
                TaskCompletionSource<Message>? completion = null;
                if (message.CorrelationId.HasValue)
                    _pending.TryGetValue(message.CorrelationId.Value, out completion);
                if (completion != null) completion.TrySetResult(message);
                else EventReceived?.Invoke(this, message);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            foreach (TaskCompletionSource<Message> pending in _pending.Values)
                pending.TrySetException(exception);
            CleanupSocket();
            if (!_intentionalDisconnect)
            {
                ConnectionLost?.Invoke(this, EventArgs.Empty);
                _ = ReconnectLoopAsync();
            }
        }
    }

    private async Task ReconnectLoopAsync()
    {
        if (_reconnecting || String.IsNullOrWhiteSpace(_sessionToken)) return;
        _reconnecting = true;
        try
        {
            int[] delays = { 1, 2, 5, 5, 5 };
            foreach (int delay in delays)
            {
                if (_intentionalDisconnect) return;
                await Task.Delay(TimeSpan.FromSeconds(delay));
                try
                {
                    await _connectionLock.WaitAsync();
                    try
                    {
                        await OpenSocketAsync();
                        await SendRequestAsync(
                            MessageType.Reconnect,
                            new ReconnectRequest { SessionToken = _sessionToken });
                    }
                    finally { _connectionLock.Release(); }
                    Reconnected?.Invoke(this, EventArgs.Empty);
                    _ = HeartbeatLoopAsync(_connectionCancellation!.Token);
                    return;
                }
                catch { CleanupSocket(); }
            }
        }
        finally { _reconnecting = false; }
    }

    private void CleanupSocket()
    {
        try { _connectionCancellation?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _stream = null;
        _client = null;
        _connectionCancellation?.Dispose();
        _connectionCancellation = null;
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                await SendRequestAsync(MessageType.Heartbeat, new EmptyPayload());
            }
        }
        catch { }
    }

    public ValueTask DisposeAsync()
    {
        _intentionalDisconnect = true;
        CleanupSocket();
        _connectionLock.Dispose();
        _writeLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
