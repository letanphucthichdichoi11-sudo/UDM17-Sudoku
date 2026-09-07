using System.Collections.Concurrent;
using Sudoku.Mobile.Models;
using Sudoku.Mobile.Network;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Services;

public sealed class LobbyService
{
    private readonly TcpGameClient _client;

    private readonly ConcurrentDictionary<
        Guid,
        TaskCompletionSource<MatchStatusResponse>>
        _preparedMatches = new();

    public event EventHandler<List<LobbyRoom>>? RoomsUpdated;

    public LobbyService(TcpGameClient client)
    {
        _client = client;
        _client.EventReceived += OnEventReceived;
    }

    // =========================================================
    // ROOMS
    // =========================================================

    public async Task<List<LobbyRoom>> GetRoomsAsync()
    {
        RoomListResponse response =
            await _client.RequestAsync<
                EmptyPayload,
                RoomListResponse>(
                    MessageType.ListRooms,
                    new EmptyPayload());

        return response.Rooms
            .Select(MapRoom)
            .ToList();
    }

    public async Task<LobbyRoom?> CreateRoomAsync(
        string playerId,
        string roomName)
    {
        LobbyRoomDto room =
            await _client.RequestAsync<
                CreateRoomRequest,
                LobbyRoomDto>(
                    MessageType.CreateRoom,
                    new CreateRoomRequest
                    {
                        RoomName = roomName
                    });

        return MapRoom(room);
    }

    public async Task<LobbyRoom?> JoinRoomAsync(
        string roomId,
        string playerId)
    {
        LobbyRoomDto room =
            await _client.RequestAsync<
                RoomRequest,
                LobbyRoomDto>(
                    MessageType.JoinRoom,
                    new RoomRequest
                    {
                        RoomId = Guid.Parse(roomId)
                    });

        return MapRoom(room);
    }

    public async Task<bool> LeaveRoomAsync(
        string roomId,
        string playerId)
    {
        await _client.RequestAsync<
            RoomRequest,
            EmptyPayload>(
                MessageType.LeaveRoom,
                new RoomRequest
                {
                    RoomId = Guid.Parse(roomId)
                });

        return true;
    }

    // =========================================================
    // QUICK MATCH
    // =========================================================

    public async Task<MatchStatusResponse?> QuickMatchAsync(
        string playerId,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Console.WriteLine(
                $"[QUICK MATCH] Player: {playerName} / {playerId}");

            LobbyRoom? room =
                await FindAvailableRoomAsync(
                    playerId,
                    cancellationToken);

            // Không có phòng → tạo phòng mới
            if (room == null)
            {
                Console.WriteLine(
                    "[QUICK MATCH] Không có phòng. Tạo phòng mới.");

                room = await CreateRoomAsync(
                    playerId,
                    $"Quick Match - {playerName}");

                if (room == null)
                {
                    Console.WriteLine(
                        "[QUICK MATCH] Tạo phòng thất bại.");

                    return null;
                }

                Console.WriteLine(
                    $"[QUICK MATCH] Room created: {room.RoomId}");
            }
            else
            {
                Console.WriteLine(
                    $"[QUICK MATCH] Found room: {room.RoomId}");

                room = await JoinRoomAsync(
                    room.RoomId,
                    playerId);

                if (room == null)
                {
                    Console.WriteLine(
                        "[QUICK MATCH] Join room thất bại.");

                    return null;
                }
            }

            Guid roomId =
                Guid.Parse(room.RoomId);

            // Tạo waiter TRƯỚC khi chờ,
            // tránh trường hợp MatchPrepared đến quá sớm.
            TaskCompletionSource<MatchStatusResponse>
                preparedSource =
                    _preparedMatches.GetOrAdd(
                        roomId,
                        _ => new TaskCompletionSource<MatchStatusResponse>(
                            TaskCreationOptions.RunContinuationsAsynchronously));

            // =================================================
            // CHỜ ĐỦ 2 PLAYER
            // =================================================

            room =
                await WaitForTwoPlayersAsync(
                    roomId,
                    playerId,
                    cancellationToken);

            if (room == null)
            {
                Console.WriteLine(
                    "[QUICK MATCH] Không đủ 2 player.");

                return null;
            }

            Console.WriteLine(
                $"[QUICK MATCH] Room ready: " +
                $"{room.Player1?.Username} vs {room.Player2?.Username}");

            // =================================================
            // CHỈ PLAYER 1 ĐƯỢC START MATCH
            // =================================================

            MatchStatusResponse? match;

            bool isPlayerOne =
                room.Player1?.PlayerId == playerId;

            if (isPlayerOne)
            {
                Console.WriteLine(
                    "[QUICK MATCH] Tôi là Player 1 → StartMatch.");

                match =
                    await StartMatchAsync(
                        room.RoomId,
                        cancellationToken);
            }
            else
            {
                Console.WriteLine(
                    "[QUICK MATCH] Tôi là Player 2 → chờ MatchPrepared.");

                match =
                    await WaitForPreparedMatchAsync(
                        preparedSource,
                        cancellationToken);
            }

            if (match == null)
            {
                Console.WriteLine(
                    "[QUICK MATCH] Không nhận được MatchPrepared.");

                return null;
            }

            Console.WriteLine(
                $"[MATCH] MatchId = {match.MatchId}");

            // =================================================
            // PLAYER READY
            // =================================================

            MatchStatusResponse readyStatus =
                await PlayerReadyAsync(
                    match.MatchId,
                    cancellationToken);

            Console.WriteLine(
                $"[MATCH] Ready: " +
                $"A={readyStatus.PlayerAReady}, " +
                $"B={readyStatus.PlayerBReady}");

            // =================================================
            // CHỜ MATCH STARTED
            // =================================================

            MatchStatusResponse? startedMatch =
                await WaitForMatchStartedAsync(
                    match.MatchId,
                    cancellationToken);

            if (startedMatch == null)
            {
                Console.WriteLine(
                    "[MATCH] Match không bắt đầu.");

                return null;
            }

            Console.WriteLine(
                "[MATCH] MATCH STARTED!");

            _preparedMatches.TryRemove(
                roomId,
                out _);

            return startedMatch;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine(
                "[QUICK MATCH] Cancelled.");

            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[QUICK MATCH ERROR] {ex}");

            return null;
        }
    }

    // =========================================================
    // FIND ROOM
    // =========================================================

    private async Task<LobbyRoom?> FindAvailableRoomAsync(
        string playerId,
        CancellationToken cancellationToken)
    {
        List<LobbyRoom> rooms =
            await GetRoomsAsync();

        foreach (LobbyRoom room in rooms)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (room.HasActiveMatch)
                continue;

            if (room.Player1 == null)
                continue;

            if (room.Player2 != null)
                continue;

            if (room.Player1.PlayerId == playerId)
                continue;

            return room;
        }

        return null;
    }

    // =========================================================
    // WAIT TWO PLAYERS
    // =========================================================

    private async Task<LobbyRoom?> WaitForTwoPlayersAsync(
        Guid roomId,
        string playerId,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 60;

        for (int i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<LobbyRoom> rooms =
                await GetRoomsAsync();

            LobbyRoom? room =
                rooms.FirstOrDefault(
                    x => x.RoomId == roomId.ToString());

            if (room != null &&
                room.Player1 != null &&
                room.Player2 != null &&
                room.Player1.PlayerId !=
                room.Player2.PlayerId)
            {
                Console.WriteLine(
                    "[QUICK MATCH] Đã đủ 2 player.");

                return room;
            }

            Console.WriteLine(
                $"[QUICK MATCH] Waiting... {i + 1}/{maxAttempts}");

            await Task.Delay(
                1000,
                cancellationToken);
        }

        return null;
    }

    // =========================================================
    // START MATCH
    // =========================================================

    private async Task<MatchStatusResponse?>
        StartMatchAsync(
            string roomId,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        StartMatchRequest request =
            new StartMatchRequest
            {
                RoomId = roomId,
                Difficulty =
                    SudokuDifficultyLevel.Medium,
                Duration =
                    MatchDurationMinutes.Five
            };

        MatchStatusResponse match =
            await _client.RequestAsync<
                StartMatchRequest,
                MatchStatusResponse>(
                    MessageType.StartMatch,
                    request);

        return match;
    }

    // =========================================================
    // WAIT MATCH PREPARED
    // =========================================================

    private async Task<MatchStatusResponse?>
        WaitForPreparedMatchAsync(
            TaskCompletionSource<MatchStatusResponse>
                source,
            CancellationToken cancellationToken)
    {
        Task timeoutTask =
            Task.Delay(
                TimeSpan.FromSeconds(60),
                cancellationToken);

        Task completed =
            await Task.WhenAny(
                source.Task,
                timeoutTask);

        if (completed != source.Task)
            return null;

        return await source.Task;
    }

    // =========================================================
    // PLAYER READY
    // =========================================================

    private async Task<MatchStatusResponse>
        PlayerReadyAsync(
            Guid matchId,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        MatchStatusResponse response =
            await _client.RequestAsync<
                MatchIdRequest,
                MatchStatusResponse>(
                    MessageType.PlayerReady,
                    new MatchIdRequest
                    {
                        MatchId = matchId
                    });

        return response;
    }

    // =========================================================
    // WAIT MATCH STARTED
    // =========================================================

    private async Task<MatchStatusResponse?>
        WaitForMatchStartedAsync(
            Guid matchId,
            CancellationToken cancellationToken)
    {
        const int maxAttempts = 30;

        for (int i = 0; i < maxAttempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                MatchStatusResponse status =
                    await _client.RequestAsync<
                        MatchIdRequest,
                        MatchStatusResponse>(
                            MessageType.GetMatchStatus,
                            new MatchIdRequest
                            {
                                MatchId = matchId
                            });

                if (status.State ==
                    MatchLifecycleState.Ongoing)
                {
                    return status;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[MATCH STATUS] {ex.Message}");
            }

            await Task.Delay(
                500,
                cancellationToken);
        }

        return null;
    }

    // =========================================================
    // SERVER EVENTS
    // =========================================================

    private void OnEventReceived(
        object? sender,
        Message message)
    {
        try
        {
            if (message.Type ==
                MessageType.RoomUpdated)
            {
                RoomListResponse response =
                    message.ReadPayload<RoomListResponse>();

                RoomsUpdated?.Invoke(
                    this,
                    response.Rooms
                        .Select(MapRoom)
                        .ToList());

                return;
            }

            if (message.Type ==
                MessageType.MatchPrepared)
            {
                MatchStatusResponse status =
                    message.ReadPayload<MatchStatusResponse>();

                Console.WriteLine(
                    $"[EVENT] MatchPrepared: {status.MatchId}");

                TaskCompletionSource<MatchStatusResponse> source =
                    _preparedMatches.GetOrAdd(
                        status.RoomId,
                        _ => new TaskCompletionSource<MatchStatusResponse>(
                            TaskCreationOptions.RunContinuationsAsynchronously));

                source.TrySetResult(status);

                return;
            }

            if (message.Type ==
                MessageType.MatchStarted)
            {
                MatchStatusResponse status =
                    message.ReadPayload<MatchStatusResponse>();

                Console.WriteLine(
                    $"[EVENT] MatchStarted: {status.MatchId}");

                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[EVENT ERROR] {ex.Message}");
        }
    }

    // =========================================================
    // CONNECTION
    // =========================================================

    public Task<bool> CheckServerAsync()
    {
        return Task.FromResult(
            _client.IsConnected);
    }

    // =========================================================
    // MAPPING
    // =========================================================

    private static LobbyRoom MapRoom(
        LobbyRoomDto room)
    {
        return new LobbyRoom
        {
            RoomId =
                room.RoomId.ToString(),

            RoomName =
                room.RoomName,

            Player1 =
                room.Players.Count > 0
                    ? MapPlayer(room.Players[0])
                    : null,

            Player2 =
                room.Players.Count > 1
                    ? MapPlayer(room.Players[1])
                    : null,

            HasActiveMatch =
                room.HasActiveMatch
        };
    }

    private static LobbyPlayer MapPlayer(
        LobbyPlayerDto player)
    {
        return new LobbyPlayer
        {
            PlayerId =
                player.PlayerId,

            Username =
                player.PlayerName,

            IsOnline = true
        };
    }
}