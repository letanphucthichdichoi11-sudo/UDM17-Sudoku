namespace Sudoku.Mobile.Views;

public partial class FindingOpponentPage : ContentPage
{
    private int _seconds = 12;
    private IDispatcherTimer? _timer;

    public FindingOpponentPage()
    {
        InitializeComponent();

        StartSearching();
    }

    private void StartSearching()
    {
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private async void Timer_Tick(object? sender, EventArgs e)
    {
        _seconds--;

        TimerLabel.Text = $"00:{_seconds:00}";

        if (_seconds <= 0)
        {
            _timer?.Stop();

            await Shell.Current.GoToAsync(
                nameof(OpponentFoundPage));
        }
    }

    private async void Cancel_Clicked(object? sender, EventArgs e)
    {
        _timer?.Stop();

        await Shell.Current.GoToAsync("..");
    }

    private async void Back_Clicked(object? sender, EventArgs e)
    {
        _timer?.Stop();

        await Shell.Current.GoToAsync("..");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _timer?.Stop();
    }
}