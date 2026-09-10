using Sudoku.Mobile.Network;
using Sudoku.Mobile.Services;
using Sudoku.Shared.Models;

namespace Sudoku.Mobile;

public partial class CreateRoomPage : ContentPage
{
    private readonly LobbyService _service = new(TcpGameClient.Shared);
    private SudokuDifficultyLevel _difficulty = SudokuDifficultyLevel.Hard;
    private bool _creating;

    public CreateRoomPage()
    {
        InitializeComponent();
        UpdateDifficultyVisuals();
        UpdateNameState();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Task.Delay(700);
        await DevelopmentLaunchOptions.CaptureScreenshotAsync("create-room");
    }

    private void OnRoomNameChanged(object? sender, TextChangedEventArgs e) => UpdateNameState();

    private void OnDifficultyClicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string value } && Enum.TryParse(value, out SudokuDifficultyLevel parsed))
        {
            _difficulty = parsed;
            UpdateDifficultyVisuals();
        }
    }

    private void UpdateDifficultyVisuals()
    {
        DifficultyBadge.Text = _difficulty.ToString().ToUpperInvariant();
        StyleDifficulty(EasyButton, _difficulty == SudokuDifficultyLevel.Easy);
        StyleDifficulty(MediumButton, _difficulty == SudokuDifficultyLevel.Medium);
        StyleDifficulty(HardButton, _difficulty == SudokuDifficultyLevel.Hard);
    }

    private static void StyleDifficulty(Button button, bool selected)
    {
        button.BackgroundColor = selected ? Color.FromArgb("#7C3AED") : Colors.Transparent;
        button.TextColor = selected ? Colors.White : Color.FromArgb("#475569");
        button.CornerRadius = 12;
        button.FontFamily = "Fredoka";
        button.FontAttributes = FontAttributes.Bold;
    }

    private void UpdateNameState()
    {
        int length = RoomNameEntry.Text?.Length ?? 0;
        CharacterCountLabel.Text = $"{length}/28";
        CreateButton.IsEnabled = !String.IsNullOrWhiteSpace(RoomNameEntry.Text) && !_creating;
        CreateButton.Opacity = CreateButton.IsEnabled ? 1 : .4;
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        if (_creating || String.IsNullOrWhiteSpace(RoomNameEntry.Text)) return;
        _creating = true; UpdateNameState();
        try
        {
            string playerId = TcpGameClient.Shared.PlayerId ?? throw new InvalidOperationException("TCP chưa có PlayerId.");
            await _service.CreateRoomAsync(playerId, RoomNameEntry.Text.Trim(), _difficulty);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex) { await DisplayAlertAsync("Lỗi", ex.Message, "OK"); }
        finally { _creating = false; UpdateNameState(); }
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
