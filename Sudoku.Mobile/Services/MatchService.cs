using Sudoku.Mobile.Network;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Services;

public sealed class MatchService : IDisposable
{
    private readonly TcpGameClient _client;
    private Guid? _activeMatchId;
    private bool _disposed;

    public event EventHandler<MatchStatusResponse>? MatchPrepared;
    public event EventHandler<MatchStatusResponse>? MatchStarted;
    public event EventHandler<MatchStatusResponse>? MatchUpdated;
    public event EventHandler<MatchStatusResponse>? MatchFinished;
    public event EventHandler<MoveResultResponse>? OpponentProgressUpdated;

    public MatchService(TcpGameClient client)
    {
        _client = client;
        _client.EventReceived += OnEventReceived;
        _client.Reconnected += OnReconnected;
    }

    public Task<MatchStatusResponse> StartMatchAsync(
        Guid roomId,
        SudokuDifficultyLevel difficulty,
        MatchDurationMinutes duration)
    {
        return StartAndTrackMatchAsync(
            roomId, difficulty, duration);
    }

    private async Task<MatchStatusResponse> StartAndTrackMatchAsync(
        Guid roomId,
        SudokuDifficultyLevel difficulty,
        MatchDurationMinutes duration)
    {
        MatchStatusResponse response = await _client.RequestAsync<StartMatchRequest, MatchStatusResponse>(
            MessageType.StartMatch,
            new StartMatchRequest
            {
                RoomId = roomId.ToString(),
                Difficulty = difficulty,
                Duration = duration
            });
        _activeMatchId = response.MatchId;
        return response;
    }

    public Task<MatchStatusResponse> ReadyAsync(Guid matchId)
    {
        return _client.RequestAsync<MatchIdRequest, MatchStatusResponse>(
            MessageType.PlayerReady,
            new MatchIdRequest { MatchId = matchId });
    }

    public Task<MatchStatusResponse> GetStatusAsync(Guid matchId)
    {
        return _client.RequestAsync<MatchIdRequest, MatchStatusResponse>(
            MessageType.GetMatchStatus,
            new MatchIdRequest { MatchId = matchId });
    }

    public Task<MoveResultResponse> SubmitMoveAsync(
        Guid matchId,
        int row,
        int column,
        int value)
    {
        return _client.RequestAsync<SubmitMoveRequest, MoveResultResponse>(
            MessageType.SubmitMove,
            new SubmitMoveRequest
            {
                MatchId = matchId,
                MoveId = Guid.NewGuid().ToString("N"),
                Row = row,
                Column = column,
                Value = value
            });
    }

    private void OnEventReceived(object? sender, Message message)
    {
        switch (message.Type)
        {
            case MessageType.MatchPrepared:
                MatchStatusResponse prepared = message.ReadPayload<MatchStatusResponse>();
                _activeMatchId = prepared.MatchId;
                MatchPrepared?.Invoke(this, prepared);
                break;
            case MessageType.MatchStarted:
                MatchStatusResponse started = message.ReadPayload<MatchStatusResponse>();
                _activeMatchId = started.MatchId;
                MatchStarted?.Invoke(this, started);
                break;
            case MessageType.MatchStatusUpdated:
                MatchUpdated?.Invoke(this, message.ReadPayload<MatchStatusResponse>());
                break;
            case MessageType.MatchFinished:
                MatchStatusResponse finished = message.ReadPayload<MatchStatusResponse>();
                MatchFinished?.Invoke(this, finished);
                _activeMatchId = null;
                break;
            case MessageType.OpponentProgressUpdated:
                OpponentProgressUpdated?.Invoke(this, message.ReadPayload<MoveResultResponse>());
                break;
        }
    }

    private async void OnReconnected(object? sender, EventArgs args)
    {
        if (!_activeMatchId.HasValue) return;
        try
        {
            MatchStatusResponse status = await GetStatusAsync(_activeMatchId.Value);
            if (status.State == MatchLifecycleState.Finished ||
                status.State == MatchLifecycleState.Aborted ||
                status.State == MatchLifecycleState.Archived)
            {
                MatchFinished?.Invoke(this, status);
                _activeMatchId = null;
            }
            else
            {
                MatchUpdated?.Invoke(this, status);
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine("Could not restore active match after reconnect: " + exception.Message);
        }
    }

    public void TrackMatch(Guid matchId)
    {
        _activeMatchId = matchId;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _client.EventReceived -= OnEventReceived;
        _client.Reconnected -= OnReconnected;
    }
}
