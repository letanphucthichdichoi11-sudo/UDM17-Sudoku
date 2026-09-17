namespace Sudoku.Mobile.Models;

public class LobbyRoom
{
    public string RoomId { get; set; } = string.Empty;

    public string RoomName { get; set; } = string.Empty;

    public LobbyPlayer? Player1 { get; set; }

    public LobbyPlayer? Player2 { get; set; }

    public bool HasActiveMatch { get; set; }
    public Sudoku.Shared.Models.SudokuDifficultyLevel Difficulty { get; set; }
    public Guid? ActiveMatchId { get; set; }
    public Sudoku.Shared.Models.MatchLifecycleState? MatchState { get; set; }
    public Sudoku.Shared.Models.MatchDurationMinutes? Duration { get; set; }
    public bool IsChallengeRoom { get; set; }

    public int PlayerCount
    {
        get
        {
            int count = 0;

            if (Player1 != null)
                count++;

            if (Player2 != null)
                count++;

            return count;
        }
    }

    public bool IsFull => PlayerCount >= 2;

    public string PlayerStatus => $"{PlayerCount}/2 PLAYERS";

    public bool IsOngoing => MatchState == Sudoku.Shared.Models.MatchLifecycleState.Ongoing && ActiveMatchId.HasValue;
    public bool IsWaiting => !HasActiveMatch && !IsFull;
    public bool IsPreparing => !IsOngoing && !IsWaiting;
    public string MatchStatus => IsOngoing ? "LIVE" : IsPreparing ? "PREPARING" : "WAITING";
    public string Detail => IsOngoing
        ? $"{Player1?.Username} vs {Player2?.Username} • {Difficulty} • ONGOING"
        : $"{Difficulty} difficulty • {PlayerCount}/2 Players";
}
