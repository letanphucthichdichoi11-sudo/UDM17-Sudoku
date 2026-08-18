using Sudoku.Mobile.Network;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Services;

public sealed class MatchService
{
    private readonly TcpGameClient _client;

    public event EventHandler<MatchStatusResponse>? MatchStarted;
    public event EventHandler<MatchStatusResponse>? MatchUpdated;
    public event EventHandler<MatchStatusResponse>? MatchFinished;
    public event EventHandler<MoveResultResponse>? OpponentProgressUpdated;

    public MatchService(TcpGameClient client)
    {
        _client = client;
        _client.EventReceived += OnEventReceived;
    }

    public Task<MatchStatusResponse> StartMatchAsync(
        Guid roomId,
        SudokuDifficultyLevel difficulty,
        MatchDurationMinutes duration)
    {
        return _client.RequestAsync<StartMatchRequest, MatchStatusResponse>(
            MessageType.StartMatch,
            new StartMatchRequest
            {
                RoomId = roomId.ToString(),
                Difficulty = difficulty,
                Duration = duration
            });
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
            case MessageType.MatchStarted:
                MatchStarted?.Invoke(this, message.ReadPayload<MatchStatusResponse>());
                break;
            case MessageType.MatchStatusUpdated:
                MatchUpdated?.Invoke(this, message.ReadPayload<MatchStatusResponse>());
                break;
            case MessageType.MatchFinished:
                MatchFinished?.Invoke(this, message.ReadPayload<MatchStatusResponse>());
                break;
            case MessageType.OpponentProgressUpdated:
                OpponentProgressUpdated?.Invoke(this, message.ReadPayload<MoveResultResponse>());
                break;
        }
    }
}
