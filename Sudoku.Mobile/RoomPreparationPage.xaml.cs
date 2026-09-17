using Sudoku.Mobile.Models;
using Sudoku.Mobile.Network;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile;

public partial class RoomPreparationPage : ContentPage
{
    private readonly TcpGameClient _client = TcpGameClient.Shared;
    private LobbyRoomDto? _room;
    private MatchStatusResponse? _match;
    private bool _busy;
    private bool _navigating;

    public RoomPreparationPage() => InitializeComponent();

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _client.EventReceived += OnEventReceived;
        try { await RefreshAsync(); }
        catch (Exception ex) { await DisplayAlertAsync("Room unavailable", ex.Message, "OK"); }
    }

    protected override void OnDisappearing()
    {
        _client.EventReceived -= OnEventReceived;
        base.OnDisappearing();
    }

    private async Task RefreshAsync()
    {
        Guid roomId = RoomSession.CurrentRoomId ?? throw new InvalidOperationException("No room selected.");
        RoomListResponse rooms = await _client.RequestAsync<EmptyPayload, RoomListResponse>(
            MessageType.ListRooms, new EmptyPayload());
        _room = rooms.Rooms.FirstOrDefault(room => room.RoomId == roomId)
            ?? throw new InvalidOperationException("Room no longer exists.");
        RoomNameLabel.Text = _room.RoomName;
        SettingsLabel.Text = $"{_room.Difficulty} difficulty • {(int)(_room.Duration ?? MatchDurationMinutes.Five)} min timer";
        PlayersLabel.Text = String.Join("  vs  ", _room.Players.Select(player => player.PlayerName));
        bool owner = _room.Players.FirstOrDefault()?.PlayerId == _client.PlayerId;
        StartButton.IsVisible = owner && _room.Players.Count == 2 && !_room.HasActiveMatch;
        if (_room.ActiveMatchId.HasValue)
        {
            _match = await _client.RequestAsync<MatchIdRequest, MatchStatusResponse>(
                MessageType.GetMatchStatus, new MatchIdRequest { MatchId = _room.ActiveMatchId.Value });
            ApplyMatch(_match);
        }
        else StatusLabel.Text = _room.Players.Count == 2
            ? owner ? "Both players joined. Start the match when ready." : "Waiting for room owner to start the match."
            : "Waiting for another player to join.";
    }

    private void OnEventReceived(object? sender, Message message)
    {
        if (message.Type == MessageType.RoomUpdated)
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await RefreshAsync(); } catch (Exception ex) { Console.WriteLine($"[ROOM] {ex}"); }
            });
        else if (message.Type is MessageType.MatchPrepared or MessageType.MatchStarted or MessageType.MatchStatusUpdated)
        {
            MatchStatusResponse status;
            try { status = message.ReadPayload<MatchStatusResponse>(); }
            catch { return; }
            if (status.RoomId == RoomSession.CurrentRoomId)
                MainThread.BeginInvokeOnMainThread(() => ApplyMatch(status));
        }
    }

    private void ApplyMatch(MatchStatusResponse status)
    {
        _match = status;
        MatchSession.CurrentMatch = status;
        StartButton.IsVisible = false;
        bool ownReady = _room?.Players.FirstOrDefault()?.PlayerId == _client.PlayerId
            ? status.PlayerAReady : status.PlayerBReady;
        ReadyButton.IsVisible = status.State == MatchLifecycleState.Preparing && !ownReady;
        StatusLabel.Text = status.State == MatchLifecycleState.Preparing
            ? $"Match prepared. Player A ready: {status.PlayerAReady}; Player B ready: {status.PlayerBReady}."
            : status.State == MatchLifecycleState.Ongoing ? "Match started!" : status.State.ToString();
        if (status.State == MatchLifecycleState.Ongoing && !_navigating)
            _ = NavigateToGameAsync();
    }

    private async Task NavigateToGameAsync()
    {
        _navigating = true;
        try { await Shell.Current.GoToAsync(nameof(SudokuPage)); }
        catch { _navigating = false; throw; }
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (_busy || _room == null) return;
        _busy = true;
        StartButton.IsEnabled = false;
        try
        {
            MatchStatusResponse prepared = await _client.RequestAsync<StartMatchRequest, MatchStatusResponse>(
                MessageType.StartMatch, new StartMatchRequest
                {
                    RoomId = _room.RoomId.ToString(),
                    Difficulty = _room.Difficulty,
                    Duration = _room.Duration ?? MatchDurationMinutes.Five
                });
            ApplyMatch(prepared);
        }
        catch (Exception ex) { await DisplayAlertAsync("Cannot start match", ex.Message, "OK"); }
        finally { _busy = false; StartButton.IsEnabled = true; }
    }

    private async void OnReadyClicked(object? sender, EventArgs e)
    {
        if (_busy || _match == null) return;
        _busy = true;
        ReadyButton.IsEnabled = false;
        try
        {
            MatchStatusResponse status = await _client.RequestAsync<MatchIdRequest, MatchStatusResponse>(
                MessageType.PlayerReady, new MatchIdRequest { MatchId = _match.MatchId });
            ApplyMatch(status);
        }
        catch (Exception ex) { await DisplayAlertAsync("Cannot ready", ex.Message, "OK"); }
        finally { _busy = false; ReadyButton.IsEnabled = true; }
    }

    private async void OnLeaveClicked(object? sender, EventArgs e)
    {
        if (_busy || _room == null) return;
        _busy = true;
        try
        {
            await _client.RequestAsync<RoomRequest, EmptyPayload>(MessageType.LeaveRoom,
                new RoomRequest { RoomId = _room.RoomId });
            RoomSession.CurrentRoomId = null;
            await Shell.Current.GoToAsync("//LobbyPage");
        }
        catch (Exception ex) { await DisplayAlertAsync("Cannot leave room", ex.Message, "OK"); }
        finally { _busy = false; }
    }
}
