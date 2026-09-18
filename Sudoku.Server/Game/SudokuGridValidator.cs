using System;

namespace Sudoku.Server.Game
{
    internal static class SudokuGridValidator
    {
        private const int Size = 9;

        public static void ValidatePuzzleAndSolution(
            int[,] puzzle,
            int[,] solution)
        {
            ValidateDimensions(puzzle, "puzzle");
            ValidateDimensions(solution, "solution");

            bool hasEmptyCell = false;
            for (int row = 0; row < Size; row++)
            for (int column = 0; column < Size; column++)
            {
                int puzzleValue = puzzle[row, column];
                int solutionValue = solution[row, column];

                if (puzzleValue < 0 || puzzleValue > Size)
                    throw new ArgumentException(
                        "Puzzle values must be between 0 and 9.",
                        "puzzle");

                if (solutionValue < 1 || solutionValue > Size)
                    throw new ArgumentException(
                        "Solution values must be between 1 and 9.",
                        "solution");

                if (puzzleValue == 0)
                    hasEmptyCell = true;
                else if (puzzleValue != solutionValue)
                    throw new ArgumentException(
                        "Puzzle clues must match the solution.",
                        "puzzle");
            }

            if (!hasEmptyCell)
                throw new ArgumentException(
                    "Puzzle must contain at least one empty cell.",
                    "puzzle");

            if (!IsValidCompleteSolution(solution))
                throw new ArgumentException(
                    "Solution must be valid in every row, column and 3x3 box.",
                    "solution");
        }

        public static void ValidateDimensions(int[,] grid, string parameterName)
        {
            if (grid == null ||
                grid.GetLength(0) != Size ||
                grid.GetLength(1) != Size)
            {
                throw new ArgumentException(
                    "Sudoku grid must be a 9x9 matrix.",
                    parameterName);
            }
        }

        public static bool IsValidCompleteSolution(int[,] grid)
        {
            if (grid == null ||
                grid.GetLength(0) != Size ||
                grid.GetLength(1) != Size)
                return false;

            for (int index = 0; index < Size; index++)
            {
                var rowValues = new bool[Size + 1];
                var columnValues = new bool[Size + 1];

                for (int offset = 0; offset < Size; offset++)
                {
                    int rowValue = grid[index, offset];
                    int columnValue = grid[offset, index];

                    if (rowValue < 1 || rowValue > Size ||
                        columnValue < 1 || columnValue > Size ||
                        rowValues[rowValue] ||
                        columnValues[columnValue])
                        return false;

                    rowValues[rowValue] = true;
                    columnValues[columnValue] = true;
                }
            }

            for (int boxRow = 0; boxRow < 3; boxRow++)
            for (int boxColumn = 0; boxColumn < 3; boxColumn++)
            {
                var values = new bool[Size + 1];
                for (int rowOffset = 0; rowOffset < 3; rowOffset++)
                for (int columnOffset = 0; columnOffset < 3; columnOffset++)
                {
                    int value = grid[
                        boxRow * 3 + rowOffset,
                        boxColumn * 3 + columnOffset];

                    if (value < 1 || value > Size || values[value])
                        return false;

                    values[value] = true;
                }
            }

            return true;
        }
    }
}
