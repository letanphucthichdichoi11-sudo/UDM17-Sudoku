using System;

namespace Sudoku.Server.Game
{
    internal class SudokuGenerator
    {
        private const int Size = 9;
        private const int BoxSize = 3;

        private readonly Random _random;

        public SudokuGenerator()
        {
            _random = new Random();
        }

        // Tạo một bảng Sudoku hoàn chỉnh
        public int[,] GenerateSolution()
        {
            int[,] board = new int[Size, Size];

            FillBoard(board);

            return board;
        }

        // Tạo đề Sudoku bằng cách xóa một số ô
        public int[,] GeneratePuzzle(int emptyCells = 40)
        {
            int[,] board = GenerateSolution();

            if (emptyCells < 0)
                emptyCells = 0;

            if (emptyCells > 64)
                emptyCells = 64;

            RemoveCells(board, emptyCells);

            return board;
        }

        // Dùng Backtracking để điền Sudoku
        private bool FillBoard(int[,] board)
        {
            int row;
            int col;

            if (!FindEmptyCell(board, out row, out col))
                return true;

            int[] numbers = CreateShuffledNumbers();

            foreach (int number in numbers)
            {
                if (IsValid(board, row, col, number))
                {
                    board[row, col] = number;

                    if (FillBoard(board))
                        return true;

                    board[row, col] = 0;
                }
            }

            return false;
        }

        // Tìm ô trống đầu tiên
        private bool FindEmptyCell(
            int[,] board,
            out int row,
            out int col)
        {
            for (row = 0; row < Size; row++)
            {
                for (col = 0; col < Size; col++)
                {
                    if (board[row, col] == 0)
                        return true;
                }
            }

            row = -1;
            col = -1;

            return false;
        }

        // Kiểm tra số có hợp lệ tại vị trí đó không
        private bool IsValid(
            int[,] board,
            int row,
            int col,
            int number)
        {
            // Kiểm tra hàng
            for (int x = 0; x < Size; x++)
            {
                if (board[row, x] == number)
                    return false;
            }

            // Kiểm tra cột
            for (int x = 0; x < Size; x++)
            {
                if (board[x, col] == number)
                    return false;
            }

            // Kiểm tra ô 3x3
            int startRow = row - row % BoxSize;
            int startCol = col - col % BoxSize;

            for (int r = 0; r < BoxSize; r++)
            {
                for (int c = 0; c < BoxSize; c++)
                {
                    if (board[startRow + r, startCol + c] == number)
                        return false;
                }
            }

            return true;
        }

        // Tạo danh sách số 1-9 theo thứ tự ngẫu nhiên
        private int[] CreateShuffledNumbers()
        {
            int[] numbers =
            {
                1, 2, 3, 4, 5,
                6, 7, 8, 9
            };

            for (int i = numbers.Length - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);

                int temp = numbers[i];
                numbers[i] = numbers[j];
                numbers[j] = temp;
            }

            return numbers;
        }

        // Xóa số ô để tạo đề
        private void RemoveCells(
            int[,] board,
            int emptyCells)
        {
            int removed = 0;

            while (removed < emptyCells)
            {
                int row = _random.Next(Size);
                int col = _random.Next(Size);

                if (board[row, col] != 0)
                {
                    board[row, col] = 0;
                    removed++;
                }
            }
        }
    }
}