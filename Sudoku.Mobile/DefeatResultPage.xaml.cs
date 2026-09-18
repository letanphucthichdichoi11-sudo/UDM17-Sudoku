using Sudoku.Mobile.Models;

namespace Sudoku.Mobile;

public partial class DefeatResultPage : ContentPage
{
    public DefeatResultPage()
    {
        InitializeComponent();
        BindResult();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BindResult();
        _ = CaptureDevelopmentScreenshotAsync();
    }

    private void BindResult()
    {
        DuelResultData? result = DuelResultSession.GetCurrent();
        OpponentNameLabel.Text = result?.OpponentName ?? "Rival";
        CompletionTimeLabel.Text = FormatTime(result?.CompletionTime ?? TimeSpan.Zero);
    }

    private async Task CaptureDevelopmentScreenshotAsync()
    {
        await Task.Delay(700);
        await DevelopmentLaunchOptions.CaptureScreenshotAsync("defeat");
        DevelopmentLaunchOptions.MarkResultReady("defeat", DuelResultSession.GetCurrent());
        if (DevelopmentLaunchOptions.AutoHomeFromResult)
            await NavigateHomeAsync();
    }

    private async void OnHomeClicked(object? sender, EventArgs e)
    {
        await NavigateHomeAsync();
    }

    private static async Task NavigateHomeAsync()
    {
        Guid matchId = DuelResultSession.GetCurrent()?.MatchId ?? Guid.Empty;
        DuelResultSession.Clear();
        MatchSession.CurrentMatch = null;
        await Shell.Current.GoToAsync("//LobbyPage");
        DevelopmentLaunchOptions.MarkHomeReady(matchId);
    }

    private static string FormatTime(TimeSpan value) =>
        $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
}
