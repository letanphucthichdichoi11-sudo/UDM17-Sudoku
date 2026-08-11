using System;
using System.Collections.Generic;

namespace Sudoku.Server.Game
{
    internal enum SudokuDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    internal sealed class GeneratedSudoku
    {
        public int[,] Puzzle { get; private set; }
        public int[,] Solution { get; private set; }
        public SudokuDifficulty Difficulty { get; private set; }
        public int EmptyCells { get; private set; }
        public int Seed { get; private set; }

        public GeneratedSudoku(
            int[,] puzzle,
            int[,] solution,
            SudokuDifficulty difficulty,
            int emptyCells,
            int seed)
        {
            Puzzle = CloneGrid(puzzle);
            Solution = CloneGrid(solution);
            Difficulty = difficulty;
            EmptyCells = emptyCells;
            Seed = seed;
        }

        private static int[,] CloneGrid(int[,] grid)
        {
            var copy = new int[9, 9];
            Array.Copy(grid, copy, grid.Length);
            return copy;
        }
    }

    internal sealed class SudokuGenerator
    {
        private const int Size = 9;
        private const int BoxSize = 3;
        private const int MaximumGenerationAttempts = 10;

        private readonly object _generationLock = new object();

        public GeneratedSudoku GeneratePuzzle(
            SudokuDifficulty difficulty,
            int? seed = null)
        {
            int targetEmptyCells = GetTargetEmptyCells(difficulty);
            int actualSeed = seed ?? CreateSeed();

            lock (_generationLock)
            {
                var random = new Random(actualSeed);

                for (int attempt = 0;
                    attempt < MaximumGenerationAttempts;
                    attempt++)
                {
                    int[,] solution = GenerateSolution(random);
                    int[,] puzzle = CloneGrid(solution);

                    int removed = RemoveCellsWithUniqueSolution(
                        puzzle,
                        targetEmptyCells,
                        random);

                    if (removed == targetEmptyCells)
                    {
                        ValidateGeneratedPuzzle(
                            puzzle,
                            solution,
                            targetEmptyCells);

                        return new GeneratedSudoku(
                            puzzle,
                            solution,
                            difficulty,
                            removed,
                            actualSeed);
                    }
                }
            }

            throw new InvalidOperationException(
                "Could not generate a uniquely solvable Sudoku " +
                "for difficulty " + difficulty +
                " after " + MaximumGenerationAttempts + " attempts.");
        }

        public int[,] GenerateSolution(int? seed = null)
        {
            int actualSeed = seed ?? CreateSeed();

            lock (_generationLock)
            {
                return GenerateSolution(new Random(actualSeed));
            }
        }

        internal static int CountSolutions(int[,] board, int limit = 2)
        {
            ValidateGridDimensions(board, "board");

            if (limit < 1)
                throw new ArgumentOutOfRangeException("limit");

            int[,] workingBoard = CloneGrid(board);
            if (!HasValidValuesAndClues(workingBoard))
                return 0;

            return CountSolutionsRecursive(workingBoard, limit);
        }

        private static int[,] GenerateSolution(Random random)
        {
            int[,] board = new int[Size, Size];

            if (!FillBoard(board, random))
            {
                throw new InvalidOperationException(
                    "Could not generate a complete Sudoku solution.");
            }

            if (!IsCompleteSolution(board))
            {
                throw new InvalidOperationException(
                    "Generated Sudoku solution is invalid.");
            }

            return board;
        }

        private static bool FillBoard(int[,] board, Random random)
        {
            int row;
            int col;

            if (!FindBestEmptyCell(board, out row, out col))
                return true;

            int[] numbers = CreateShuffledNumbers(random);

            foreach (int number in numbers)
            {
                if (!IsValidPlacement(board, row, col, number))
                    continue;

                board[row, col] = number;

                if (FillBoard(board, random))
                    return true;

                board[row, col] = 0;
            }

            return false;
        }

        private static int RemoveCellsWithUniqueSolution(
            int[,] board,
            int targetEmptyCells,
            Random random)
        {
            var positions = new List<int>();
            for (int index = 0; index < Size * Size; index++)
                positions.Add(index);

            Shuffle(positions, random);

            int removed = 0;
            foreach (int position in positions)
            {
                if (removed >= targetEmptyCells)
                    break;

                int row = position / Size;
                int col = position % Size;
                int previousValue = board[row, col];

                board[row, col] = 0;

                if (CountSolutions(board, 2) == 1)
                {
                    removed++;
                }
                else
                {
                    board[row, col] = previousValue;
                }
            }

            return removed;
        }

        private static int CountSolutionsRecursive(int[,] board, int limit)
        {
            int row;
            int col;

            if (!FindBestEmptyCell(board, out row, out col))
                return 1;

            int count = 0;
            for (int number = 1; number <= Size; number++)
            {
                if (!IsValidPlacement(board, row, col, number))
                    continue;

                board[row, col] = number;
                count += CountSolutionsRecursive(board, limit - count);
                board[row, col] = 0;

                if (count >= limit)
                    return count;
            }

            return count;
        }

        private static bool FindBestEmptyCell(
            int[,] board,
            out int bestRow,
            out int bestCol)
        {
            bestRow = -1;
            bestCol = -1;
            int fewestCandidates = Size + 1;

            for (int row = 0; row < Size; row++)
            {
                for (int col = 0; col < Size; col++)
                {
                    if (board[row, col] != 0)
                        continue;

                    int candidates = 0;
                    for (int number = 1; number <= Size; number++)
                    {
                        if (IsValidPlacement(board, row, col, number))
                            candidates++;
                    }

                    if (candidates < fewestCandidates)
                    {
                        fewestCandidates = candidates;
                        bestRow = row;
                        bestCol = col;

                        if (candidates <= 1)
                            return true;
                    }
                }
            }

            return bestRow >= 0;
        }

        private static bool IsValidPlacement(
            int[,] board,
            int row,
            int col,
            int number)
        {
            for (int index = 0; index < Size; index++)
            {
                if (board[row, index] == number ||
                    board[index, col] == number)
                {
                    return false;
                }
            }

            int startRow = row - row % BoxSize;
            int startCol = col - col % BoxSize;

            for (int rowOffset = 0; rowOffset < BoxSize; rowOffset++)
            {
                for (int colOffset = 0; colOffset < BoxSize; colOffset++)
                {
                    if (board[startRow + rowOffset, startCol + colOffset]
                        == number)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool HasValidValuesAndClues(int[,] board)
        {
            for (int row = 0; row < Size; row++)
            {
                for (int col = 0; col < Size; col++)
                {
                    int value = board[row, col];
                    if (value < 0 || value > Size)
                        return false;

                    if (value == 0)
                        continue;

                    board[row, col] = 0;
                    bool isValid = IsValidPlacement(board, row, col, value);
                    board[row, col] = value;

                    if (!isValid)
                        return false;
                }
            }

            return true;
        }

        private static bool IsCompleteSolution(int[,] board)
        {
            if (!HasValidValuesAndClues(board))
                return false;

            for (int row = 0; row < Size; row++)
            for (int col = 0; col < Size; col++)
            {
                if (board[row, col] == 0)
                    return false;
            }

            return true;
        }

        private static void ValidateGeneratedPuzzle(
            int[,] puzzle,
            int[,] solution,
            int expectedEmptyCells)
        {
            if (!IsCompleteSolution(solution))
                throw new InvalidOperationException("Solution grid is invalid.");

            int emptyCells = 0;
            for (int row = 0; row < Size; row++)
            for (int col = 0; col < Size; col++)
            {
                int value = puzzle[row, col];
                if (value == 0)
                {
                    emptyCells++;
                }
                else if (value != solution[row, col])
                {
                    throw new InvalidOperationException(
                        "Puzzle clues do not match the solution.");
                }
            }

            if (emptyCells != expectedEmptyCells)
                throw new InvalidOperationException("Unexpected empty cell count.");

            if (CountSolutions(puzzle, 2) != 1)
                throw new InvalidOperationException(
                    "Generated puzzle does not have exactly one solution.");
        }

        private static int GetTargetEmptyCells(SudokuDifficulty difficulty)
        {
            switch (difficulty)
            {
                case SudokuDifficulty.Easy:
                    return 40;
                case SudokuDifficulty.Medium:
                    return 48;
                case SudokuDifficulty.Hard:
                    return 54;
                default:
                    throw new ArgumentOutOfRangeException("difficulty");
            }
        }

        private static int[] CreateShuffledNumbers(Random random)
        {
            int[] numbers = { 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            for (int index = numbers.Length - 1; index > 0; index--)
            {
                int otherIndex = random.Next(index + 1);
                int temporary = numbers[index];
                numbers[index] = numbers[otherIndex];
                numbers[otherIndex] = temporary;
            }

            return numbers;
        }

        private static void Shuffle(List<int> values, Random random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int otherIndex = random.Next(index + 1);
                int temporary = values[index];
                values[index] = values[otherIndex];
                values[otherIndex] = temporary;
            }
        }

        private static int[,] CloneGrid(int[,] grid)
        {
            ValidateGridDimensions(grid, "grid");
            var copy = new int[Size, Size];
            Array.Copy(grid, copy, grid.Length);
            return copy;
        }

        private static void ValidateGridDimensions(int[,] grid, string name)
        {
            if (grid == null ||
                grid.GetLength(0) != Size ||
                grid.GetLength(1) != Size)
            {
                throw new ArgumentException(
                    "Sudoku grid must be a 9x9 matrix.",
                    name);
            }
        }

        private static int CreateSeed()
        {
            return Guid.NewGuid().GetHashCode();
        }
    }
}
