using System;

namespace Sudoku.Shared.Models
{
    public static class SudokuBoardCoordinates
    {
        public const int Size = 9;
        public const int CellCount = Size * Size;

        public static int ToIndex(int row, int column)
        {
            ValidateCoordinate(row, "row");
            ValidateCoordinate(column, "column");
            return row * Size + column;
        }

        public static void FromIndex(int index, out int row, out int column)
        {
            if (index < 0 || index >= CellCount)
                throw new ArgumentOutOfRangeException("index");

            row = index / Size;
            column = index % Size;
        }

        public static bool IsValidFlatBoard(int[] board)
        {
            return board != null && board.Length == CellCount;
        }

        private static void ValidateCoordinate(int value, string name)
        {
            if (value < 0 || value >= Size)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
