using Sudoku.Server.Game;

namespace Sudoku.Mobile;

public partial class SudokuPage : ContentPage
{
    private int[,] _puzzle = new int[9, 9];

private Button? _selectedCell;

    public SudokuPage()
    {
        InitializeComponent();

        GenerateSudoku();
    }

    private void GenerateSudoku()
    {
        SudokuGenerator generator = new SudokuGenerator();

        // Tạo đề Sudoku.
        // 40 ô sẽ được xóa thành ô trống.
        _puzzle = generator.GeneratePuzzle(40);

        CreateSudokuBoard();
    }

    private void CreateSudokuBoard()
    {
        SudokuGrid.Children.Clear();

        for (int row = 0; row < 9; row++)
        {
            for (int col = 0; col < 9; col++)
            {
                int value = _puzzle[row, col];

                Button cell = new Button
                {
                    FontSize = 20,
                    Padding = 0,
                    CornerRadius = 0,
                    BorderWidth = 0
                };

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, col);

                // ==========================
                // Ô CÓ SỐ SẴN
                // ==========================

                if (value != 0)
                {
                    cell.Text = value.ToString();

                    cell.IsEnabled = false;

                    cell.BackgroundColor =
                        Color.FromArgb("#E5E7EB");

                    cell.TextColor =
                        Color.FromArgb("#111827");
                }

                // ==========================
                // Ô TRỐNG
                // ==========================

                else
                {
                    cell.Text = "";

                    cell.IsEnabled = true;

                    cell.BackgroundColor = Colors.White;

                    cell.TextColor =
                        Color.FromArgb("#2563EB");

                    cell.Clicked += Cell_Clicked;
                }

                SudokuGrid.Add(cell);
            }
        }
    }

    // ==========================
    // CHỌN Ô
    // ==========================

    private void Cell_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button cell)
            return;

        // Bỏ highlight ô trước đó
        if (_selectedCell != null)
        {
            _selectedCell.BackgroundColor = Colors.White;
        }

        _selectedCell = cell;

        // Highlight ô đang chọn
        _selectedCell.BackgroundColor =
            Color.FromArgb("#DBEAFE");
    }

    // ==========================
    // NHẬP SỐ
    // ==========================

    private void Number_Clicked(object? sender, EventArgs e)
    {
        if (_selectedCell == null)
        {
            DisplayAlert(
                "Thông báo",
                "Hãy chọn một ô trống.",
                "OK");

            return;
        }

        if (sender is not Button numberButton)
            return;

        _selectedCell.Text = numberButton.Text;

        _selectedCell.TextColor =
            Color.FromArgb("#2563EB");

        _selectedCell.BackgroundColor =
            Color.FromArgb("#DBEAFE");
    }

    // ==========================
    // XÓA SỐ
    // ==========================

    private void Clear_Clicked(object? sender, EventArgs e)
    {
        if (_selectedCell == null)
            return;

        _selectedCell.Text = "";

        _selectedCell.BackgroundColor = Colors.White;
    }

    // ==========================
    // SUBMIT
    // ==========================

    private async void SubmitButton_Clicked(
        object? sender,
        EventArgs e)
    {
        await DisplayAlert(
            "Sudoku",
            "Đã gửi bài.",
            "OK");
    }


}
