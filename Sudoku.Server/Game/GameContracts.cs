using System;
using System.Collections.Generic;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Game
{
    internal interface IClock
    {
        DateTime UtcNow { get; }
    }

    internal sealed class SystemClock : IClock
    {
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
    }

    internal interface ISudokuGenerator
    {
        GeneratedSudoku GeneratePuzzle(
            SudokuDifficulty difficulty,
            int? seed = null);

        int[,] GenerateSolution(int? seed = null);
    }

    internal interface IRoomManager
    {
        Room GetRoom(Guid roomId);
    }

    internal interface IMatchManager
    {
        Match StartMatch(
            Guid roomId,
            Guid matchId,
            string playerAId,
            string playerBId,
            int[,] originalPuzzle,
            int[,] solutionGrid,
            Sudoku.Shared.Models.MatchDurationMinutes duration);

        bool MarkPlayerReady(Guid matchId, string playerId);

        MatchStatusResponse GetPlayerStatus(Guid matchId, string playerId);

        IList<ActiveMatchSummary> GetActiveMatchSummaries(DateTime nowUtc);
    }
}
