using System;
using System.Collections.Generic;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Game
{
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
            TimeSpan timeLimit);

        IList<ActiveMatchSummary> GetActiveMatchSummaries(DateTime nowUtc);
    }
}
