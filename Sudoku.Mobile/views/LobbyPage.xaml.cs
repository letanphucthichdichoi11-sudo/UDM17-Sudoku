using Sudoku.Mobile.Models;
using Sudoku.Mobile.Network;
using Sudoku.Mobile.Services;
using Sudoku.Mobile.ViewModels;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Views;

public partial class LobbyPage : ContentPage
{
    private readonly LobbyViewModel _viewModel;
    private readonly IDispatcherTimer _refreshTimer;
    private OnlinePlayerEntry? _challengeTarget;
    private SudokuDifficultyLevel _difficulty = SudokuDifficultyLevel.Medium;
    private MatchDurationMinutes _duration = MatchDurationMinutes.Five;
    private bool _quickMatchBusy;
    private bool _watchBusy;
    private bool _challengeBusy;
    private bool _navigatingToRoom;
    private bool _developmentQuickMatchStarted;

    public LobbyPage()
    {
        InitializeComponent();
        _viewModel = new LobbyViewModel(new LobbyService(TcpGameClient.Shared));
        BindingContext = _viewModel;
        _viewModel.ChallengeAccepted += OnChallengeAccepted;
        _viewModel.ChallengeOutcome += OnChallengeOutcome;
        _refreshTimer = Dispatcher.CreateTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += async (_, _) =>
        {
            if (!IsVisible || !TcpGameClient.Shared.IsConnected) return;
            try { await _viewModel.RefreshAsync(); }
            catch (Exception ex) { Console.WriteLine($"[LOBBY REFRESH] {ex.Message}"); }
        };
        UpdateChoiceStyles();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _navigatingToRoom = false;
        try
        {
            await _viewModel.RefreshAsync();
            DevelopmentLaunchOptions.MarkLobbyReady();
            _refreshTimer.Start();
            if (!_developmentQuickMatchStarted && DevelopmentLaunchOptions.AutoQuickMatch)
            {
                _developmentQuickMatchStarted = true;
                OnQuickMatchClicked(QuickMatchButton, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lobby unavailable", ex.Message, "OK");
        }
    }

    protected override void OnDisappearing()
    {
        _refreshTimer.Stop();
        base.OnDisappearing();
    }

    private void OnLobbySizeChanged(object? sender, EventArgs e)
    {
        if (ActionCardsGrid == null || CreateCard == null || JoinCard == null) return;
        bool stacked = Width < 650;
        ActionCardsGrid.ColumnDefinitions.Clear();
        ActionCardsGrid.RowDefinitions.Clear();
        ActionCardsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (stacked) ActionCardsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        else ActionCardsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (stacked) ActionCardsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetColumn(CreateCard, 0); Grid.SetRow(CreateCard, 0);
        Grid.SetColumn(JoinCard, stacked ? 0 : 1);
        Grid.SetRow(JoinCard, stacked ? 1 : 0);
    }

    private async void CreateRoom_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync(nameof(CreateRoomPage));

    private async void JoinRoomCode_Clicked(object? sender, EventArgs e)
    {
        string? code = await DisplayPromptAsync("Join Room", "Enter a room ID:", "JOIN", "CANCEL");
        if (String.IsNullOrWhiteSpace(code)) return;
        if (!Guid.TryParse(code.Trim(), out Guid id))
        {
            await DisplayAlertAsync("Invalid room", "Enter a valid room ID.", "OK");
            return;
        }
        LobbyRoom? room = _viewModel.Rooms.FirstOrDefault(item => item.RoomId == id.ToString());
        if (room == null || !room.IsWaiting)
        {
            await DisplayAlertAsync("Room unavailable", "The room is not waiting for a player.", "OK");
            return;
        }
        await JoinRoomAsync(room);
    }

    private async void JoinRoom_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: LobbyRoom room })
            await JoinRoomAsync(room);
    }

    private async Task JoinRoomAsync(LobbyRoom room)
    {
        try
        {
            string playerId = TcpGameClient.Shared.PlayerId ?? throw new InvalidOperationException("Not connected to Game Server.");
            LobbyRoom joined = await _viewModel.JoinRoomAsync(room.RoomId, playerId)
                ?? throw new InvalidOperationException("Could not join this room.");
            RoomSession.CurrentRoomId = Guid.Parse(joined.RoomId);
            await Shell.Current.GoToAsync(nameof(RoomPreparationPage));
        }
        catch (Exception ex) { await DisplayAlertAsync("Join Room failed", ex.Message, "OK"); }
    }

    private async void OnQuickMatchClicked(object? sender, EventArgs e)
    {
        if (_quickMatchBusy) return;
        _quickMatchBusy = true;
        QuickMatchButton.IsEnabled = false;
        QuickMatchButton.Text = "SEARCHING...";
        try
        {
            if (!TcpGameClient.Shared.IsConnected) throw new InvalidOperationException("Not connected to Game Server.");
            string playerId = TcpGameClient.Shared.PlayerId ?? throw new InvalidOperationException("Player ID is unavailable.");
            string playerName = Preferences.Default.Get("Username", playerId);
            MatchStatusResponse match = await _viewModel.QuickMatchAsync(playerId, playerName)
                ?? throw new InvalidOperationException("No opponent found or the match could not start.");
            MatchSession.CurrentMatch = match;
            await Shell.Current.GoToAsync(nameof(SudokuPage));
        }
        catch (Exception ex) { await DisplayAlertAsync("Quick Match", ex.Message, "OK"); }
        finally
        {
            _quickMatchBusy = false;
            QuickMatchButton.IsEnabled = true;
            QuickMatchButton.Text = "FIND OPPONENT  →";
        }
    }

    private async void WatchRoom_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: LobbyRoom { ActiveMatchId: Guid id } })
            await WatchMatchAsync(id);
    }

    private async void WatchPlayer_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: OnlinePlayerEntry { MatchId: Guid id } })
            await WatchMatchAsync(id);
    }

    private async Task WatchMatchAsync(Guid matchId)
    {
        if (_watchBusy) return;
        _watchBusy = true;
        try
        {
            await SpectatorService.Shared.JoinAsync(matchId);
            await Shell.Current.GoToAsync(nameof(SpectatorPage));
        }
        catch (Exception ex) { await DisplayAlertAsync("Cannot watch match", ex.Message, "OK"); }
        finally { _watchBusy = false; }
    }

    private void ChallengePlayer_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: OnlinePlayerEntry player } || !player.CanChallenge) return;
        _challengeTarget = player;
        _difficulty = SudokuDifficultyLevel.Medium;
        _duration = MatchDurationMinutes.Five;
        ChallengeTitle.Text = $"CHALLENGE {player.PlayerName.ToUpperInvariant()}";
        UpdateChoiceStyles();
        ChallengeModal.IsVisible = true;
    }

    private void Difficulty_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string value } &&
            Enum.TryParse(value, out SudokuDifficultyLevel difficulty))
        {
            _difficulty = difficulty;
            UpdateChoiceStyles();
        }
    }

    private void Duration_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string value } &&
            Enum.TryParse(value, out MatchDurationMinutes duration))
        {
            _duration = duration;
            UpdateChoiceStyles();
        }
    }

    private void UpdateChoiceStyles()
    {
        if (EasyButton == null) return;
        StyleChoice(EasyButton, _difficulty == SudokuDifficultyLevel.Easy);
        StyleChoice(MediumButton, _difficulty == SudokuDifficultyLevel.Medium);
        StyleChoice(HardButton, _difficulty == SudokuDifficultyLevel.Hard);
        StyleChoice(FiveButton, _duration == MatchDurationMinutes.Five);
        StyleChoice(TenButton, _duration == MatchDurationMinutes.Ten);
        StyleChoice(FifteenButton, _duration == MatchDurationMinutes.Fifteen);
    }

    private static void StyleChoice(Button button, bool selected)
    {
        button.BackgroundColor = Color.FromArgb(selected ? "#7C3AED" : "#F5F0FE");
        button.TextColor = Color.FromArgb(selected ? "#FFFFFF" : "#6D28D9");
    }

    private void CancelChallengeModal_Clicked(object? sender, EventArgs e)
    {
        ChallengeModal.IsVisible = false;
        _challengeTarget = null;
    }

    private async void SendChallenge_Clicked(object? sender, EventArgs e)
    {
        if (_challengeBusy || _challengeTarget == null) return;
        _challengeBusy = true;
        SendChallengeButton.IsEnabled = false;
        try
        {
            await _viewModel.SendChallengeAsync(_challengeTarget.PlayerId, _difficulty, _duration);
            ChallengeModal.IsVisible = false;
            _challengeTarget = null;
        }
        catch (Exception ex) { await DisplayAlertAsync("Challenge failed", ex.Message, "OK"); }
        finally { _challengeBusy = false; SendChallengeButton.IsEnabled = true; }
    }

    private async void CancelSent_Clicked(object? sender, EventArgs e)
    {
        try { await _viewModel.CancelSentAsync(); }
        catch (Exception ex) { await DisplayAlertAsync("Cannot cancel challenge", ex.Message, "OK"); }
    }

    private void ToggleChallenges_Clicked(object? sender, EventArgs e) => _viewModel.ToggleChallenges();
    private void DismissToast_Clicked(object? sender, EventArgs e) => _viewModel.DismissIncomingToast();

    private async void AcceptChallenge_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: PendingChallengeEntry entry })
            await RespondToChallengeAsync(entry.Challenge.ChallengeId, true);
    }
    private async void DeclineChallenge_Clicked(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: PendingChallengeEntry entry })
            await RespondToChallengeAsync(entry.Challenge.ChallengeId, false);
    }
    private async void AcceptToast_Clicked(object? sender, EventArgs e)
    {
        if (_viewModel.IncomingChallenge is { } challenge)
            await RespondToChallengeAsync(challenge.ChallengeId, true);
    }
    private async void DeclineToast_Clicked(object? sender, EventArgs e)
    {
        if (_viewModel.IncomingChallenge is { } challenge)
            await RespondToChallengeAsync(challenge.ChallengeId, false);
    }

    private async Task RespondToChallengeAsync(Guid challengeId, bool accept)
    {
        if (_challengeBusy) return;
        _challengeBusy = true;
        try
        {
            if (accept) await _viewModel.AcceptAsync(challengeId);
            else await _viewModel.DeclineAsync(challengeId);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Challenge unavailable", ex.Message, "OK");
            try { await _viewModel.RefreshAsync(); } catch { }
        }
        finally { _challengeBusy = false; }
    }

    private void OnChallengeAccepted(object? sender, ChallengeDto challenge)
    {
        string? playerId = TcpGameClient.Shared.PlayerId;
        if (challenge.RoomId.HasValue &&
            (challenge.ChallengerPlayerId == playerId || challenge.TargetPlayerId == playerId))
            _ = NavigateToChallengeRoomAsync(challenge.RoomId.Value);
    }

    private async Task NavigateToChallengeRoomAsync(Guid roomId)
    {
        if (_navigatingToRoom || !IsVisible) return;
        _navigatingToRoom = true;
        RoomSession.CurrentRoomId = roomId;
        try { await Shell.Current.GoToAsync(nameof(RoomPreparationPage)); }
        catch (Exception ex)
        {
            _navigatingToRoom = false;
            await DisplayAlertAsync("Cannot open challenge room", ex.Message, "OK");
        }
    }

    private void OnChallengeOutcome(object? sender, ChallengeDto challenge)
    {
        if (!IsVisible || challenge.ChallengerPlayerId != TcpGameClient.Shared.PlayerId ||
            challenge.Status == ChallengeStatus.Cancelled) return;
        _ = DisplayAlertAsync("Challenge", $"Challenge {challenge.Status.ToString().ToLowerInvariant()}.", "OK");
    }
}
