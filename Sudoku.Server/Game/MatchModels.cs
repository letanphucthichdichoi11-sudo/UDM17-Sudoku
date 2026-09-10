using System;
using System.Collections.Generic;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Game
{
    internal enum MatchState { Preparing, Ongoing, Finished, Aborted, Archived }
    internal enum ConnectionStatus { Connected, Disconnected }
    internal enum MatchFinishReason { Completed, TimeUp, TechnicalWinDisconnect, BothDisconnected, ServerRestart, PreparingTimeout }
    internal enum MoveErrorCode { None, MatchNotFound, MatchNotOngoing, MatchExpired, NotAPlayer, DuplicateMoveNotFound, OutOfRange, GivenCellLocked, InvalidValue, IncorrectValue }

    internal sealed class PlayerBoardState
    {
        public int[,] CurrentValues { get; private set; }
        public bool[,] IsGivenCell { get; private set; }
        public int CorrectCount { get; set; }
        public int ErrorCount { get; set; }
        public int TotalEmptyCells { get; private set; }

        public PlayerBoardState(int[,] puzzle)
        {
            CurrentValues = MatchGrid.Clone(puzzle);
            IsGivenCell = new bool[9, 9];
            for (var row = 0; row < 9; row++)
            for (var col = 0; col < 9; col++)
            {
                IsGivenCell[row, col] = puzzle[row, col] != 0;
                if (!IsGivenCell[row, col]) TotalEmptyCells++;
            }
        }
    }

    internal sealed class Match
    {
        public Guid MatchId { get; set; }
        public Guid RoomId { get; set; }
        public string PlayerAId { get; set; }
        public string PlayerBId { get; set; }
        public int[,] OriginalPuzzle { get; set; }
        public int[,] SolutionGrid { get; set; }
        public int[,] OriginalPuzzleB { get; set; }
        public int[,] SolutionGridB { get; set; }
        public PlayerBoardState BoardA { get; set; }
        public PlayerBoardState BoardB { get; set; }
        public TimeSpan TimeLimit { get; set; }
        public MatchDurationMinutes Duration { get; set; }
        public DateTime PreparingEndsAtUtc { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }
        public bool PlayerAReady { get; set; }
        public bool PlayerBReady { get; set; }
        public MatchState State { get; set; }
        public ConnectionStatus ConnectionA { get; set; }
        public ConnectionStatus ConnectionB { get; set; }
        public DateTime? DisconnectedAtA { get; set; }
        public DateTime? DisconnectedAtB { get; set; }
        public MatchResult Result { get; set; }
        public HashSet<string> SpectatorIds { get; private set; }
        public Dictionary<string, MoveResult> ProcessedMoves { get; private set; }
        public object SyncRoot { get; private set; }

        public Match()
        {
            SpectatorIds = new HashSet<string>(StringComparer.Ordinal);
            ProcessedMoves = new Dictionary<string, MoveResult>(StringComparer.Ordinal);
            SyncRoot = new object();
        }

        public bool IsPlayer(string playerId) { return playerId == PlayerAId || playerId == PlayerBId; }
        public PlayerBoardState GetBoard(string playerId) { return playerId == PlayerAId ? BoardA : BoardB; }
        public int[,] GetPuzzle(string playerId) { return playerId == PlayerAId ? OriginalPuzzle : OriginalPuzzleB; }
        public int[,] GetSolution(PlayerBoardState board) { return ReferenceEquals(board, BoardA) ? SolutionGrid : SolutionGridB; }
        public string GetOpponent(string playerId) { return playerId == PlayerAId ? PlayerBId : PlayerAId; }
    }

    internal sealed class MatchResult
    {
        public string WinnerPlayerId { get; set; }
        public MatchFinishReason Reason { get; set; }
        public DateTime FinishedAtUtc { get; set; }
    }

    internal sealed class MoveResult
    {
        public bool Accepted { get; set; }
        public bool IsCorrect { get; set; }
        public MoveErrorCode ErrorCode { get; set; }
        public int CorrectCount { get; set; }
        public int ErrorCount { get; set; }
        public bool BoardChanged { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public int Value { get; set; }
    }

    internal sealed class PlayerMatchSnapshot
    {
        public Guid MatchId { get; set; }
        public int[,] OriginalPuzzle { get; set; }
        public int[,] OwnBoard { get; set; }
        public int[,] OpponentBoard { get; set; }
        public int OwnCorrectCount { get; set; }
        public int OwnErrorCount { get; set; }
        public int OpponentCorrectCount { get; set; }
        public int OpponentErrorCount { get; set; }
        public TimeSpan TimeLeft { get; set; }
        public DateTime ServerUtcNow { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }
        public MatchState State { get; set; }
    }

    internal sealed class SpectatorMatchSnapshot
    {
        public Guid MatchId { get; set; }
        public int[,] OriginalPuzzle { get; set; }
        public int[,] BoardA { get; set; }
        public int[,] BoardB { get; set; }
        public int CorrectCountA { get; set; }
        public int CorrectCountB { get; set; }
        public int ErrorCountA { get; set; }
        public int ErrorCountB { get; set; }
        public TimeSpan TimeLeft { get; set; }
        public DateTime ServerUtcNow { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? EndsAtUtc { get; set; }
    }

    internal sealed class ActiveMatchSummary
    {
        public Guid RoomId { get; set; }
        public Guid MatchId { get; set; }
        public string PlayerAId { get; set; }
        public string PlayerBId { get; set; }
        public int CorrectCountA { get; set; }
        public int CorrectCountB { get; set; }
        public int ErrorCountA { get; set; }
        public int ErrorCountB { get; set; }
        public int SpectatorCount { get; set; }
        public TimeSpan TimeLeft { get; set; }
        public DateTime ServerUtcNow { get; set; }
        public DateTime? EndsAtUtc { get; set; }
    }

    internal class MatchEventArgs : EventArgs
    {
        public Match Match { get; private set; }
        public MatchEventArgs(Match match) { Match = match; }
    }

    internal sealed class MatchMoveEventArgs : MatchEventArgs
    {
        public string PlayerId { get; private set; }
        public MoveResult Move { get; private set; }
        public MatchMoveEventArgs(Match match, string playerId, MoveResult move) : base(match) { PlayerId = playerId; Move = move; }
    }

    internal static class MatchGrid
    {
        public static int[,] Clone(int[,] grid)
        {
            var copy = new int[9, 9];
            Array.Copy(grid, copy, grid.Length);
            return copy;
        }

        public static int[] Flatten(int[,] grid)
        {
            var values = new int[81];
            for (int row = 0; row < 9; row++)
            for (int column = 0; column < 9; column++)
                values[row * 9 + column] = grid[row, column];
            return values;
        }
    }
}
