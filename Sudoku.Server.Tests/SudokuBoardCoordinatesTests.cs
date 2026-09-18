using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Tests
{
    [TestClass]
    public class SudokuBoardCoordinatesTests
    {
        [TestMethod]
        public void BoardDimensions_AreNineByNineWithEightyOneCells()
        {
            Assert.AreEqual(9, SudokuBoardCoordinates.Size);
            Assert.AreEqual(81, SudokuBoardCoordinates.CellCount);
            Assert.IsTrue(SudokuBoardCoordinates.IsValidFlatBoard(new int[81]));
            Assert.IsFalse(SudokuBoardCoordinates.IsValidFlatBoard(new int[80]));
            Assert.IsFalse(SudokuBoardCoordinates.IsValidFlatBoard(new int[82]));
        }

        [DataTestMethod]
        [DataRow(0, 0, 0)]
        [DataRow(0, 8, 8)]
        [DataRow(1, 0, 9)]
        [DataRow(4, 4, 40)]
        [DataRow(8, 0, 72)]
        [DataRow(8, 8, 80)]
        public void CoordinateMapping_RoundTripsCorrectly(
            int row,
            int column,
            int expectedIndex)
        {
            int index = SudokuBoardCoordinates.ToIndex(row, column);
            Assert.AreEqual(expectedIndex, index);

            SudokuBoardCoordinates.FromIndex(
                index,
                out int mappedRow,
                out int mappedColumn);
            Assert.AreEqual(row, mappedRow);
            Assert.AreEqual(column, mappedColumn);
        }

        [TestMethod]
        public void CoordinateMapping_RejectsOutOfRangeValues()
        {
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => SudokuBoardCoordinates.ToIndex(-1, 0));
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => SudokuBoardCoordinates.ToIndex(0, 9));
            Assert.ThrowsException<ArgumentOutOfRangeException>(
                () => SudokuBoardCoordinates.FromIndex(81, out _, out _));
        }

        [TestMethod]
        public void OneMove_ChangesExactlyOneCell()
        {
            var board = new int[SudokuBoardCoordinates.CellCount];
            board[SudokuBoardCoordinates.ToIndex(4, 4)] = 7;

            int changedCells = 0;
            foreach (int value in board)
            {
                if (value != 0)
                    changedCells++;
            }

            Assert.AreEqual(1, changedCells);
            Assert.AreEqual(7, board[40]);
        }
    }
}
