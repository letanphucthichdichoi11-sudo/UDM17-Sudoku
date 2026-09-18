using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Sudoku.Shared.Network;

namespace Sudoku.Server.Network
{
    internal sealed class ClientHandler : IDisposable
    {
        private const int MaximumRequestsPerWindow = 100;
        private static readonly TimeSpan RateWindow = TimeSpan.FromSeconds(10);

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly MessageDispatcher _dispatcher;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly ConcurrentDictionary<Guid, Message> _responseCache =
            new ConcurrentDictionary<Guid, Message>();
        private readonly ClientConnectionContext _context;
        private DateTime _windowStartedUtc = DateTime.UtcNow;
        private int _windowRequests;
        private bool _disposed;

        public Guid ConnectionId { get; private set; }

        public ClientHandler(TcpClient client, MessageDispatcher dispatcher)
        {
            _client = client ?? throw new ArgumentNullException("client");
            _dispatcher = dispatcher ?? throw new ArgumentNullException("dispatcher");
            _stream = client.GetStream();
            ConnectionId = Guid.NewGuid();
            _context = new ClientConnectionContext
            {
                ConnectionId = ConnectionId,
                SendAsync = SendAsync,
                Close = Dispose
            };
        }

        public async Task HandleAsync()
        {
            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    Message request = await PacketCodec.ReadMessageAsync(
                        _stream, _cancellation.Token);
                    if (!AllowRequest())
                    {
                        await SendAsync(CreateError(
                            request, ProtocolErrorCode.InvalidPacket,
                            "Request rate limit exceeded."));
                        continue;
                    }

                    Message cached;
                    if (_responseCache.TryGetValue(request.MessageId, out cached))
                    {
                        await SendAsync(cached);
                        continue;
                    }

                    Message response = await _dispatcher.DispatchAsync(request, _context);
                    _responseCache[request.MessageId] = response;
                    TrimCache();
                    await SendAsync(response);
                    if (request.ProtocolVersion != Message.CurrentProtocolVersion)
                        break;
                }
            }
            catch (OperationCanceledException) { }
            catch (EndOfStreamException) { }
            catch (IOException) { }
            catch (SocketException) { }
            catch (InvalidDataException)
            {
                try
                {
                    await SendAsync(CreateError(
                        null, ProtocolErrorCode.InvalidPacket,
                        "Packet framing or JSON is invalid."));
                }
                catch { }
            }
            finally
            {
                _dispatcher.HandleDisconnected(_context.Session, ConnectionId);
                Dispose();
            }
        }

        public async Task SendAsync(Message message)
        {
            await _writeLock.WaitAsync(_cancellation.Token);
            try
            {
                await PacketCodec.WriteMessageAsync(
                    _stream, message, _cancellation.Token);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private bool AllowRequest()
        {
            DateTime now = DateTime.UtcNow;
            if (now - _windowStartedUtc >= RateWindow)
            {
                _windowStartedUtc = now;
                _windowRequests = 0;
            }
            return Interlocked.Increment(ref _windowRequests) <= MaximumRequestsPerWindow;
        }

        private void TrimCache()
        {
            if (_responseCache.Count <= 256) return;
            foreach (Guid key in _responseCache.Keys)
            {
                Message ignored;
                _responseCache.TryRemove(key, out ignored);
                if (_responseCache.Count <= 128) break;
            }
        }

        private static Message CreateError(
            Message request,
            ProtocolErrorCode code,
            string text)
        {
            Message response = Message.Create(MessageType.Error, new EmptyPayload());
            response.CorrelationId = request == null ? null : (Guid?)request.MessageId;
            response.Error = new ProtocolError { Code = code, Message = text };
            return response;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cancellation.Cancel();
            try { _stream.Close(); } catch { }
            try { _client.Close(); } catch { }
            _writeLock.Dispose();
            _cancellation.Dispose();
        }
    }
}
