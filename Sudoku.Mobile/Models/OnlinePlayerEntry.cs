using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Models;

public sealed class OnlinePlayerEntry
{
    public string PlayerId { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public LobbyPlayerState State { get; init; }
    public string? RoomName { get; init; }
    public Guid? MatchId { get; init; }
    public string? OpponentName { get; init; }
    public Sudoku.Shared.Models.SudokuDifficultyLevel? Difficulty { get; init; }

    public bool CanChallenge => State == LobbyPlayerState.Available;
    public bool CanWatch => State == LobbyPlayerState.InMatch && MatchId.HasValue;
    public bool IsInRoom => State == LobbyPlayerState.InRoom;
    public string Status => State switch
    {
        LobbyPlayerState.Available => "AVAILABLE",
        LobbyPlayerState.InRoom => "IN ROOM",
        _ => "IN MATCH"
    };
    public string Detail => State switch
    {
        LobbyPlayerState.Available => "Ready for 1v1 duel • In Lobby",
        LobbyPlayerState.InRoom => $"Preparing match in {RoomName}",
        _ => $"Playing vs {OpponentName} • {Difficulty}"
    };
}
