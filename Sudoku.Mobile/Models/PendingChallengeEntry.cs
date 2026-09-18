using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Models;

public sealed class PendingChallengeEntry
{
    public ChallengeDto Challenge { get; init; } = new();
    public string ChallengerName => Challenge.ChallengerName;
    public string Detail => $"{Challenge.Difficulty} difficulty • {(int)Challenge.Duration} min timer";
}
