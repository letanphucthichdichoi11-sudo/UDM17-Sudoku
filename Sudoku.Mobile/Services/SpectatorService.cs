using Sudoku.Mobile.Network;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Services;

public sealed class SpectatorService
{
    public static SpectatorService Shared { get; } = new(TcpGameClient.Shared);

    private readonly TcpGameClient _client;
    private readonly SemaphoreSlim _joinLock = new(1, 1);
    private readonly object _sync = new();
    private SpectatorMatchState? _current;
    private SpectatorMatchState? _duringJoin;
    private Guid? _joining;

    public event EventHandler<SpectatorMatchState>? Updated;
    public SpectatorMatchState? Current { get { lock (_sync) return _current; } }

    private SpectatorService(TcpGameClient client)
    {
        _client = client;
        _client.EventReceived += OnEventReceived;
    }

    public async Task<SpectatorMatchState> JoinAsync(Guid matchId)
    {
        if (matchId == Guid.Empty) throw new ArgumentException("Select an active match.");
        await _joinLock.WaitAsync();
        try
        {
            lock (_sync)
            {
                _joining = matchId;
                _duringJoin = null;
            }
            SpectatorMatchState response = await _client.RequestAsync<MatchIdRequest, SpectatorMatchState>(
                MessageType.JoinSpectator, new MatchIdRequest { MatchId = matchId });
            SpectatorMatchState selected;
            lock (_sync)
            {
                selected = _duringJoin != null && _duringJoin.Version > response.Version
                    ? _duringJoin : response;
                _current = selected;
                _joining = null;
                _duringJoin = null;
            }
            Updated?.Invoke(this, selected);
            return selected;
        }
        catch
        {
            lock (_sync) { _joining = null; _duringJoin = null; }
            throw;
        }
        finally { _joinLock.Release(); }
    }

    public async Task LeaveAsync()
    {
        SpectatorMatchState? match = Current;
        if (match == null) return;
        await _client.RequestAsync<MatchIdRequest, EmptyPayload>(MessageType.LeaveSpectator,
            new MatchIdRequest { MatchId = match.MatchId });
        lock (_sync) if (_current?.MatchId == match.MatchId) _current = null;
    }

    private void OnEventReceived(object? sender, Message message)
    {
        if (message.Type != MessageType.SpectatorMatchUpdated) return;
        SpectatorMatchState incoming;
        try { incoming = message.ReadPayload<SpectatorMatchState>(); }
        catch { return; }
        bool notify = false;
        lock (_sync)
        {
            if (_joining == incoming.MatchId)
            {
                if (_duringJoin == null || incoming.Version > _duringJoin.Version)
                    _duringJoin = incoming;
                return;
            }
            if (_current?.MatchId == incoming.MatchId && incoming.Version > _current.Version)
            {
                _current = incoming;
                notify = true;
            }
        }
        if (notify) Updated?.Invoke(this, incoming);
    }
}
