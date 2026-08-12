using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sudoku.Server.Game;
using System.Linq;
using System.Threading.Tasks;

namespace Sudoku.Server.Tests
{
    [TestClass]
    public class SudokuGeneratorTests
    {
        [TestMethod]
        public void GenerateSolution_WithSameSeed_ReturnsSameValidGrid()
        {
            var generator = new SudokuGenerator();

            int[,] first = generator.GenerateSolution(12345);
            int[,] second = generator.GenerateSolution(12345);

            AssertGridsEqual(first, second);
            AssertValidSolution(first);
        }

        [DataTestMethod]
        [DataRow((int)SudokuDifficulty.Easy, 40)]
        [DataRow((int)SudokuDifficulty.Medium, 48)]
        [DataRow((int)SudokuDifficulty.Hard, 54)]
        public void GeneratePuzzle_ReturnsUniquePuzzleMatchingSolution(
            int difficultyValue,
            int expectedEmptyCells)
        {
            var generator = new SudokuGenerator();
            var difficulty = (SudokuDifficulty)difficultyValue;

            GeneratedSudoku generated =
                generator.GeneratePuzzle(difficulty, 20260811);

            Assert.AreEqual(difficulty, generated.Difficulty);
            Assert.AreEqual(expectedEmptyCells, generated.EmptyCells);
            Assert.AreEqual(20260811, generated.Seed);
            AssertValidSolution(generated.Solution);
            AssertPuzzleMatchesSolution(
                generated.Puzzle,
                generated.Solution,
                expectedEmptyCells);
            Assert.AreEqual(
                1,
                SudokuGenerator.CountSolutions(generated.Puzzle, 2));
        }

        [TestMethod]
        public void GeneratePuzzle_WithSameSeed_ReturnsSamePuzzleAndSolution()
        {
            var generator = new SudokuGenerator();

            GeneratedSudoku first = generator.GeneratePuzzle(
                SudokuDifficulty.Medium,
                77);
            GeneratedSudoku second = generator.GeneratePuzzle(
                SudokuDifficulty.Medium,
                77);

            AssertGridsEqual(first.Puzzle, second.Puzzle);
            AssertGridsEqual(first.Solution, second.Solution);
        }

        [TestMethod]
        public void GeneratedSudoku_PuzzleAndSolutionAreIndependent()
        {
            var generator = new SudokuGenerator();
            GeneratedSudoku generated = generator.GeneratePuzzle(
                SudokuDifficulty.Easy,
                9001);

            int originalSolutionValue = generated.Solution[0, 0];
            generated.Puzzle[0, 0] = 0;

            Assert.AreEqual(originalSolutionValue, generated.Solution[0, 0]);
        }

        [TestMethod]
        public async Task GeneratePuzzle_ConcurrentCalls_ReturnIndependentValidPuzzles()
        {
            var generator = new SudokuGenerator();
            Task<GeneratedSudoku>[] tasks = Enumerable.Range(1, 4)
                .Select(seed => Task.Run(() => generator.GeneratePuzzle(
                    SudokuDifficulty.Easy,
                    seed)))
                .ToArray();

            GeneratedSudoku[] results = await Task.WhenAll(tasks);

            foreach (GeneratedSudoku generated in results)
            {
                Assert.AreEqual(40, generated.EmptyCells);
                Assert.AreEqual(
                    1,
                    SudokuGenerator.CountSolutions(generated.Puzzle, 2));
                AssertPuzzleMatchesSolution(
                    generated.Puzzle,
                    generated.Solution,
                    40);
            }
        }

        private static void AssertPuzzleMatchesSolution(
            int[,] puzzle,
            int[,] solution,
            int expectedEmptyCells)
        {
            int emptyCells = 0;
            for (int row = 0; row < 9; row++)
            for (int col = 0; col < 9; col++)
            {
                if (puzzle[row, col] == 0)
                    emptyCells++;
                else
                    Assert.AreEqual(solution[row, col], puzzle[row, col]);
            }

            Assert.AreEqual(expectedEmptyCells, emptyCells);
        }

        private static void AssertValidSolution(int[,] grid)
        {
            Assert.AreEqual(9, grid.GetLength(0));
            Assert.AreEqual(9, grid.GetLength(1));

            for (int index = 0; index < 9; index++)
            {
                var rowSeen = new bool[10];
                var columnSeen = new bool[10];
                for (int offset = 0; offset < 9; offset++)
                {
                    int rowValue = grid[index, offset];
                    int columnValue = grid[offset, index];
                    Assert.IsTrue(rowValue >= 1 && rowValue <= 9);
                    Assert.IsTrue(columnValue >= 1 && columnValue <= 9);
                    Assert.IsFalse(rowSeen[rowValue]);
                    Assert.IsFalse(columnSeen[columnValue]);
                    rowSeen[rowValue] = true;
                    columnSeen[columnValue] = true;
                }
            }

            for (int boxRow = 0; boxRow < 3; boxRow++)
            for (int boxCol = 0; boxCol < 3; boxCol++)
            {
                var seen = new bool[10];
                for (int rowOffset = 0; rowOffset < 3; rowOffset++)
                for (int colOffset = 0; colOffset < 3; colOffset++)
                {
                    int value = grid[
                        boxRow * 3 + rowOffset,
                        boxCol * 3 + colOffset];
                    Assert.IsFalse(seen[value]);
                    seen[value] = true;
                }
            }
        }

        private static void AssertGridsEqual(int[,] expected, int[,] actual)
        {
            for (int row = 0; row < 9; row++)
            for (int col = 0; col < 9; col++)
                Assert.AreEqual(expected[row, col], actual[row, col]);
        }
    }
}
