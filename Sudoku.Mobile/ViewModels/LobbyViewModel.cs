using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Sudoku.Mobile.Models;
using Sudoku.Mobile.Services;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.ViewModels;

public sealed class LobbyViewModel : INotifyPropertyChanged
{
    private readonly LobbyService _service;
    private bool _isLoading;
    private bool _challengesExpanded;
    private bool _toastDismissed;
    private ChallengeDto? _incomingChallenge;
    private ChallengeDto? _sentChallenge;
    private string? _loadError;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ChallengeDto>? ChallengeAccepted;
    public event EventHandler<ChallengeDto>? ChallengeOutcome;

    public ObservableCollection<LobbyRoom> Rooms { get; } = new();
    public ObservableCollection<OnlinePlayerEntry> OnlinePlayers { get; } = new();
    public ObservableCollection<PendingChallengeEntry> PendingChallenges { get; } = new();

    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }
    public string? LoadError { get => _loadError; private set { if (Set(ref _loadError, value)) Notify(nameof(HasLoadError)); } }
    public bool HasLoadError => !String.IsNullOrWhiteSpace(LoadError);
    public bool ChallengesExpanded
    {
        get => _challengesExpanded;
        private set => Set(ref _challengesExpanded, value);
    }
    public ChallengeDto? IncomingChallenge
    {
        get => _incomingChallenge;
        private set
        {
            if (Set(ref _incomingChallenge, value)) Notify(nameof(HasIncomingToast));
        }
    }
    public ChallengeDto? SentChallenge
    {
        get => _sentChallenge;
        private set
        {
            if (Set(ref _sentChallenge, value)) Notify(nameof(HasSentChallenge));
        }
    }
    public bool HasIncomingToast => IncomingChallenge?.Status == ChallengeStatus.Pending && !_toastDismissed;
    public bool HasSentChallenge => SentChallenge?.Status == ChallengeStatus.Pending;
    public string SentDetail => SentChallenge == null ? string.Empty :
        $"Waiting for {SentChallenge.TargetName} to accept...\n{SentChallenge.Difficulty} difficulty • {(int)SentChallenge.Duration} min";
    public string IncomingDetail => IncomingChallenge == null ? string.Empty :
        $"{IncomingChallenge.ChallengerName} challenged you\n{IncomingChallenge.Difficulty} difficulty • {(int)IncomingChallenge.Duration} min duel";
    public string ChallengeBadge => $"CHALLENGES ({PendingChallenges.Count})";
    public string OnlineCount => $"● {OnlinePlayers.Count} ONLINE";
    public bool HasNoRooms => Rooms.Count == 0;
    public bool HasNoOnlinePlayers => OnlinePlayers.Count == 0;

    public LobbyViewModel(LobbyService service)
    {
        _service = service;
        _service.RoomsUpdated += (_, rooms) => MainThread.BeginInvokeOnMainThread(() =>
        {
            Replace(Rooms, rooms);
            Notify(nameof(HasNoRooms));
        });
        _service.OnlinePlayersUpdated += (_, players) => MainThread.BeginInvokeOnMainThread(() =>
        {
            Replace(OnlinePlayers, players);
            Notify(nameof(OnlineCount));
            Notify(nameof(HasNoOnlinePlayers));
        });
        _service.ChallengeReceived += (_, challenge) => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (challenge.TargetPlayerId != Sudoku.Mobile.Network.TcpGameClient.Shared.PlayerId) return;
            UpsertChallenge(challenge);
            _toastDismissed = false;
            IncomingChallenge = challenge;
            Notify(nameof(IncomingDetail));
        });
        _service.ChallengeUpdated += (_, challenge) => MainThread.BeginInvokeOnMainThread(() =>
        {
            UpsertChallenge(challenge);
            if (SentChallenge?.ChallengeId == challenge.ChallengeId)
            {
                SentChallenge = challenge.Status == ChallengeStatus.Pending ? challenge : null;
                Notify(nameof(SentDetail));
            }
            if (IncomingChallenge?.ChallengeId == challenge.ChallengeId && challenge.Status != ChallengeStatus.Pending)
                IncomingChallenge = null;
            if (challenge.Status == ChallengeStatus.Accepted)
                ChallengeAccepted?.Invoke(this, challenge);
            else if (challenge.Status != ChallengeStatus.Pending)
                ChallengeOutcome?.Invoke(this, challenge);
        });
    }

    public async Task RefreshAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        try
        {
            Replace(Rooms, await _service.GetRoomsAsync());
            Notify(nameof(HasNoRooms));
            Replace(OnlinePlayers, await _service.GetOnlinePlayersAsync());
            Notify(nameof(OnlineCount));
            Notify(nameof(HasNoOnlinePlayers));
            ApplyChallenges(await _service.GetChallengesAsync());
            LoadError = null;
        }
        catch (Exception ex)
        {
            LoadError = $"Could not refresh Lobby: {ex.Message}";
            throw;
        }
        finally { IsLoading = false; }
    }

    public Task LoadRoomsAsync() => RefreshAsync();

    public Task<LobbyRoom?> CreateRoomAsync(string playerId, string roomName) =>
        _service.CreateRoomAsync(playerId, roomName);
    public Task<LobbyRoom?> JoinRoomAsync(string roomId, string playerId) =>
        _service.JoinRoomAsync(roomId, playerId);
    public Task<bool> LeaveRoomAsync(string roomId, string playerId) =>
        _service.LeaveRoomAsync(roomId, playerId);
    public Task<MatchStatusResponse?> QuickMatchAsync(string playerId, string playerName,
        CancellationToken cancellationToken = default) =>
        _service.QuickMatchAsync(playerId, playerName, cancellationToken);

    public async Task SendChallengeAsync(string playerId, SudokuDifficultyLevel difficulty,
        MatchDurationMinutes duration)
    {
        ChallengeDto sent = await _service.SendChallengeAsync(playerId, difficulty, duration);
        SentChallenge = sent;
        Notify(nameof(SentDetail));
    }

    public async Task AcceptAsync(Guid id) => ApplyChallengeResult(await _service.AcceptChallengeAsync(id));
    public async Task DeclineAsync(Guid id) => ApplyChallengeResult(await _service.DeclineChallengeAsync(id));
    public async Task CancelSentAsync()
    {
        if (SentChallenge == null) return;
        ApplyChallengeResult(await _service.CancelChallengeAsync(SentChallenge.ChallengeId));
    }

    public void ToggleChallenges() => ChallengesExpanded = !ChallengesExpanded;
    public void DismissIncomingToast()
    {
        _toastDismissed = true;
        Notify(nameof(HasIncomingToast));
    }

    private void ApplyChallengeResult(ChallengeDto challenge)
    {
        UpsertChallenge(challenge);
        if (IncomingChallenge?.ChallengeId == challenge.ChallengeId)
            IncomingChallenge = null;
        if (SentChallenge?.ChallengeId == challenge.ChallengeId)
            SentChallenge = null;
    }

    private void ApplyChallenges(List<ChallengeDto> challenges)
    {
        string? playerId = Sudoku.Mobile.Network.TcpGameClient.Shared.PlayerId;
        Replace(PendingChallenges, challenges
            .Where(challenge => challenge.TargetPlayerId == playerId && challenge.Status == ChallengeStatus.Pending)
            .Select(challenge => new PendingChallengeEntry { Challenge = challenge }).ToList());
        ChallengeDto? sent = challenges.LastOrDefault(challenge =>
            challenge.ChallengerPlayerId == playerId && challenge.Status == ChallengeStatus.Pending);
        if (sent?.ChallengeId != SentChallenge?.ChallengeId)
        {
            SentChallenge = sent;
            Notify(nameof(SentDetail));
        }
        Notify(nameof(ChallengeBadge));
    }

    private void UpsertChallenge(ChallengeDto challenge)
    {
        string? playerId = Sudoku.Mobile.Network.TcpGameClient.Shared.PlayerId;
        PendingChallengeEntry? existing = PendingChallenges.FirstOrDefault(item =>
            item.Challenge.ChallengeId == challenge.ChallengeId);
        if (existing != null) PendingChallenges.Remove(existing);
        if (challenge.TargetPlayerId == playerId && challenge.Status == ChallengeStatus.Pending)
            PendingChallenges.Insert(0, new PendingChallengeEntry { Challenge = challenge });
        Notify(nameof(ChallengeBadge));
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items) target.Add(item);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
    private void Notify(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
