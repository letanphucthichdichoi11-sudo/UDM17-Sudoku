using System;
using Microsoft.Maui.Controls;

namespace Sudoku.Mobile
{
    public partial class SudokuPage : ContentPage
    {
        private readonly int[,] _puzzle = new int[9, 9];
        private readonly int[,] _solution = new int[9, 9];

        private Button? _selectedCell;

        public SudokuPage()
        {
            InitializeComponent();

            GenerateSudoku();
        }

        private void GenerateSudoku()
        {
            GenerateSolvedBoard();

            // Copy lời giải sang puzzle
            Array.Copy(_solution, _puzzle, _solution.Length);

            // Xóa khoảng 40 ô để tạo đề
            Random random = new Random();

            int removed = 0;

            while (removed < 40)
            {
                int row = random.Next(0, 9);
                int col = random.Next(0, 9);

                if (_puzzle[row, col] != 0)
                {
                    _puzzle[row, col] = 0;
                    removed++;
                }
            }

            UpdateBoard();
        }

        private void GenerateSolvedBoard()
        {
            // Xóa bảng
            for (int row = 0; row < 9; row++)
            {
                for (int col = 0; col < 9; col++)
                {
                    _solution[row, col] = 0;
                }
            }

            FillBoard(_solution);
        }

        private bool FillBoard(int[,] board)
        {
            for (int row = 0; row < 9; row++)
            {
                for (int col = 0; col < 9; col++)
                {
                    if (board[row, col] != 0)
                        continue;

                    int[] numbers =
                    {
                        1, 2, 3, 4, 5,
                        6, 7, 8, 9
                    };

                    Shuffle(numbers);

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
            }

            return true;
        }

        private bool IsValid(
            int[,] board,
            int row,
            int col,
            int number)
        {
            // Kiểm tra hàng
            for (int c = 0; c < 9; c++)
            {
                if (board[row, c] == number)
                    return false;
            }

            // Kiểm tra cột
            for (int r = 0; r < 9; r++)
            {
                if (board[r, col] == number)
                    return false;
            }

            // Kiểm tra ô 3x3
            int startRow = row / 3 * 3;
            int startCol = col / 3 * 3;

            for (int r = startRow; r < startRow + 3; r++)
            {
                for (int c = startCol; c < startCol + 3; c++)
                {
                    if (board[r, c] == number)
                        return false;
                }
            }

            return true;
        }

        private void Shuffle(int[] numbers)
        {
            Random random = new Random();

            for (int i = numbers.Length - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);

                int temp = numbers[i];
                numbers[i] = numbers[j];
                numbers[j] = temp;
            }
        }

        private void UpdateBoard()
        {
            for (int row = 0; row < 9; row++)
            {
                for (int col = 0; col < 9; col++)
                {
                    Button button = GetCellButton(row, col);

                    int value = _puzzle[row, col];

                    if (value == 0)
                    {
                        button.Text = "";
                    }
                    else
                    {
                        button.Text = value.ToString();
                    }

                    // Ô đề bài không được sửa
                    button.IsEnabled = value == 0;
                }
            }
        }

        private Button GetCellButton(int row, int col)
        {
            return (Button)SudokuGrid.Children[
                row * 9 + col
            ];
        }

        private void OnCellClicked(object sender, EventArgs e)
        {
            if (sender is not Button button)
                return;

            _selectedCell = button;
        }

        private void OnNumberClicked(object sender, EventArgs e)
        {
            if (_selectedCell == null)
                return;

            if (sender is not Button button)
                return;

            _selectedCell.Text = button.Text;
        }

        private void OnClearClicked(object sender, EventArgs e)
        {
            if (_selectedCell == null)
                return;

            _selectedCell.Text = "";
        }

        private async void OnNewGameClicked(object sender, EventArgs e)
        {
            GenerateSudoku();

            await DisplayAlert(
                "Sudoku",
                "Đã tạo ván mới!",
                "OK"
            );
        }
    }
}