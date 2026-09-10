using System;

namespace Sudoku.Shared.Models
{
    public enum MatchDurationMinutes
    {
        Five = 5,
        Ten = 10,
        Fifteen = 15
    }

    public enum SudokuDifficultyLevel
    {
        Easy,
        Medium,
        Hard
    }

    public enum MatchLifecycleState
    {
        Preparing,
        Ongoing,
        Finished,
        Aborted,
        Archived
    }

    public enum MatchFinishReasonCode
    {
        Completed,
        TimeUp,
        TechnicalWinDisconnect,
        BothDisconnected,
        PreparingTimeout
    }

    public sealed class StartMatchRequest
    {
        public string RoomId { get; set; }
        public SudokuDifficultyLevel Difficulty { get; set; }
        public MatchDurationMinutes Duration { get; set; }
    }

    public sealed class MatchReadyRequest
    {
        public Guid MatchId { get; set; }
        public string PlayerId { get; set; }
    }

    public sealed class MatchStatusResponse
    {
        public Guid MatchId { get; set; }
        public Guid RoomId { get; set; }
        public int[] Puzzle { get; set; }
        public int[] OwnBoard { get; set; }
        public int[] OpponentBoard { get; set; }
        public string OpponentName { get; set; }
        public MatchLifecycleState State { get; set; }
        public MatchDurationMinutes Duration { get; set; }
        public bool PlayerAReady { get; set; }
        public bool PlayerBReady { get; set; }
        public DateTime ServerUtcNow { get; set; }
        public DateTime? PreparingEndsAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        public TimeSpan TimeLeft { get; set; }
        public int OwnCorrectCount { get; set; }
        public int OwnErrorCount { get; set; }
        public int OpponentCorrectCount { get; set; }
        public int OpponentErrorCount { get; set; }
        public MatchFinishReasonCode? FinishReason { get; set; }
        public string WinnerPlayerId { get; set; }
    }
}
