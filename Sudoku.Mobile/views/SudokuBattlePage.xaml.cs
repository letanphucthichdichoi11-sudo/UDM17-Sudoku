using System.Timers;

namespace Sudoku.Mobile.Views;

public partial class SudokuBattlePage : ContentPage
{
    private readonly System.Timers.Timer _timer;

    private int _minutes = 4;
    private int _seconds = 32;

    private Button? _selectedCell;

    private readonly int[,] _puzzle =
    {
        { 5, 3, 0, 6, 0, 0, 9, 0, 0 },
        { 8, 0, 0, 1, 9, 5, 0, 3, 0 },
        { 7, 0, 2, 0, 0, 6, 0, 0, 7 },

        { 0, 6, 0, 0, 2, 5, 0, 5, 0 },
        { 8, 0, 2, 6, 1, 0, 0, 7, 9 },
        { 0, 3, 5, 0, 0, 5, 3, 0, 3 },

        { 4, 0, 9, 0, 0, 3, 3, 6, 0 },
        { 0, 0, 0, 0, 0, 0, 0, 0, 0 },
        { 0, 0, 0, 0, 0, 0, 0, 0, 0 }
    };

    public SudokuBattlePage()
    {
        InitializeComponent();

        CreateSudokuBoard();

        _timer = new System.Timers.Timer(1000);
        _timer.Elapsed += Timer_Elapsed;
        _timer.AutoReset = true;
        _timer.Start();
    }

    private void CreateSudokuBoard()
    {
        SudokuGrid.RowDefinitions.Clear();
        SudokuGrid.ColumnDefinitions.Clear();
        SudokuGrid.Children.Clear();

        for (int i = 0; i < 9; i++)
        {
            SudokuGrid.RowDefinitions.Add(
                new RowDefinition(GridLength.Star));

            SudokuGrid.ColumnDefinitions.Add(
                new ColumnDefinition(GridLength.Star));
        }

        for (int row = 0; row < 9; row++)
        {
            for (int col = 0; col < 9; col++)
            {
                int value = _puzzle[row, col];

                var cell = new Button
                {
                    Text = value == 0 ? string.Empty : value.ToString(),
                    FontSize = 19,
                    FontAttributes = FontAttributes.Bold,
                    Padding = 0,
                    Margin = new Thickness(
                        col % 3 == 0 ? 2 : 0.5,
                        row % 3 == 0 ? 2 : 0.5,
                        col == 8 ? 2 : 0.5,
                        row == 8 ? 2 : 0.5),
                    CornerRadius = 0
                };

                if (value != 0)
                {
                    cell.BackgroundColor = Color.FromArgb("#FFF1D2");
                    cell.TextColor = Color.FromArgb("#3B2117");
                }
                else
                {
                    cell.BackgroundColor = Color.FromArgb("#FFE4BA");
                    cell.TextColor = Color.FromArgb("#F04A20");

                    cell.Clicked += Cell_Clicked;
                }

                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, col);

                SudokuGrid.Children.Add(cell);
            }
        }
    }

    private void Cell_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button cell)
            return;

        _selectedCell = cell;

        cell.BackgroundColor = Color.FromArgb("#FF8A25");
        cell.TextColor = Colors.White;
    }

    private void Number_Clicked(object? sender, EventArgs e)
    {
        if (_selectedCell == null)
            return;

        if (sender is not Button numberButton)
            return;

        _selectedCell.Text = numberButton.Text;

        _selectedCell.BackgroundColor = Color.FromArgb("#FFB52B");
        _selectedCell.TextColor = Color.FromArgb("#4A1E13");
    }

    private void Erase_Clicked(object? sender, EventArgs e)
    {
        if (_selectedCell == null)
            return;

        _selectedCell.Text = string.Empty;
        _selectedCell.BackgroundColor = Color.FromArgb("#FFE4BA");
        _selectedCell.TextColor = Color.FromArgb("#F04A20");
    }

    private async void Undo_Clicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync(
            "UNDO",
            "Đã hoàn tác nước đi gần nhất.",
            "OK");
    }

    private async void Hint_Clicked(object? sender, EventArgs e)
    {
        await DisplayAlertAsync(
            "HINT",
            "💡 Gợi ý: hãy kiểm tra hàng và cột của ô đang chọn.",
            "OK");
    }

    private async void Back_Clicked(object? sender, EventArgs e)
    {
        _timer.Stop();

        if (Navigation.NavigationStack.Count > 1)
        {
            await Navigation.PopAsync();
        }
    }

    private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        if (_seconds > 0)
        {
            _seconds--;
        }
        else if (_minutes > 0)
        {
            _minutes--;
            _seconds = 59;
        }
        else
        {
            _timer.Stop();

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await DisplayAlertAsync(
                    "TIME'S UP!",
                    "The battle has ended.",
                    "OK");
            });

            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            TimerLabel.Text = $"{_minutes:00}:{_seconds:00}";

            if (_minutes == 0 && _seconds <= 30)
            {
                TimerLabel.TextColor = Color.FromArgb("#FF3E2F");
            }
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _timer.Stop();
    }
}
