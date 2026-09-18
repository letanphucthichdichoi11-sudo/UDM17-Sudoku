using System.Diagnostics;
using Sudoku.Mobile.Services;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile;

public partial class SpectatorPage : ContentPage
{
    private readonly SpectatorService _service = SpectatorService.Shared;
    private readonly IDispatcherTimer _timer;
    private readonly Label[,] _cellsA = new Label[9, 9];
    private readonly Label[,] _cellsB = new Label[9, 9];
    private SpectatorMatchState? _state;
    private long _stateReceivedAt;
    private bool _leaving;

    public SpectatorPage()
    {
        InitializeComponent();
        BuildBoard(BoardAGrid, _cellsA);
        BuildBoard(BoardBGrid, _cellsB);
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => UpdateTimer();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _service.Updated += OnUpdated;
        if (_service.Current is { } current) Apply(current);
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        _service.Updated -= OnUpdated;
        base.OnDisappearing();
    }

    private void OnUpdated(object? sender, SpectatorMatchState state) =>
        MainThread.BeginInvokeOnMainThread(() => Apply(state));

    private static void BuildBoard(Grid grid, Label[,] cells)
    {
        for (int i = 0; i < 9; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }
        for (int row = 0; row < 9; row++)
        for (int col = 0; col < 9; col++)
        {
            var label = new Label
            {
                BackgroundColor = (row / 3 + col / 3) % 2 == 0 ? Colors.White : Color.FromArgb("#F5F0FE"),
                TextColor = Color.FromArgb("#3B0764"),
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            cells[row, col] = label;
            grid.Add(label, col, row);
        }
    }

    private void Apply(SpectatorMatchState state)
    {
        _state = state;
        _stateReceivedAt = Stopwatch.GetTimestamp();
        TitleLabel.Text = $"{state.PlayerAName} vs {state.PlayerBName}";
        StatusLabel.Text = $"{state.Difficulty} • {state.State} • Read-only spectator mode";
        PlayerALabel.Text = state.PlayerAName;
        PlayerBLabel.Text = state.PlayerBName;
        ProgressALabel.Text = $"Progress: {GetPercent(state.CorrectCountA, state.PuzzleA)}% • Mistakes: {state.ErrorCountA}";
        ProgressBLabel.Text = $"Progress: {GetPercent(state.CorrectCountB, state.PuzzleB)}% • Mistakes: {state.ErrorCountB}";
        RenderBoard(_cellsA, state.BoardA, state.PuzzleA);
        RenderBoard(_cellsB, state.BoardB, state.PuzzleB);
        UpdateTimer();
    }

    private static int GetPercent(int correct, int[] puzzle)
    {
        int total = puzzle?.Count(value => value == 0) ?? 0;
        return total == 0 ? 0 : Math.Clamp((int)Math.Round(100d * correct / total), 0, 100);
    }

    private static void RenderBoard(Label[,] labels, int[] board, int[] puzzle)
    {
        if (board?.Length != 81 || puzzle?.Length != 81) return;
        for (int row = 0; row < 9; row++)
        for (int col = 0; col < 9; col++)
        {
            int index = row * 9 + col;
            labels[row, col].Text = board[index] == 0 ? string.Empty : board[index].ToString();
            labels[row, col].TextColor = puzzle[index] != 0
                ? Color.FromArgb("#1F2937") : Color.FromArgb("#7C3AED");
        }
    }

    private void UpdateTimer()
    {
        if (_state?.EndsAtUtc is not DateTime endsAt) { TimerLabel.Text = "--:--"; return; }
        DateTime estimatedServerNow = _state.ServerUtcNow + Stopwatch.GetElapsedTime(_stateReceivedAt);
        TimeSpan left = endsAt - estimatedServerNow;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        TimerLabel.Text = $"{(int)left.TotalMinutes:00}:{left.Seconds:00}";
    }

    private async void OnLeaveClicked(object? sender, EventArgs e)
    {
        if (_leaving) return;
        _leaving = true;
        try
        {
            await _service.LeaveAsync();
            await Shell.Current.GoToAsync("//LobbyPage");
        }
        catch (Exception ex) { await DisplayAlertAsync("Cannot leave spectator mode", ex.Message, "OK"); }
        finally { _leaving = false; }
    }
}
