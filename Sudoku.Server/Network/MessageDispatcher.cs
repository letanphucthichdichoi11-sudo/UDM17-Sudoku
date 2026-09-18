using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sudoku.Server.Game;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Server.Network
{
    internal sealed class ClientConnectionContext
    {
        public ClientSession Session { get; set; }
        public Func<Message, Task> SendAsync { get; set; }
        public Action Close { get; set; }
        public Guid ConnectionId { get; set; }
    }

    internal sealed class MessageDispatcher
    {
        private readonly GameApplicationServices _games;
        private readonly SessionManager _sessions;
        private readonly IClock _clock;
        private readonly ChallengeManager _challenges;

        public MessageDispatcher(
            GameApplicationServices games,
            SessionManager sessions,
            IClock clock)
        {
            _games = games;
            _sessions = sessions;
            _clock = clock;
            _challenges = new ChallengeManager(games.Rooms, games.Matches, sessions, clock);

            _games.Matches.MatchStarted += OnMatchStarted;
            _games.Matches.PlayerProgressChanged += OnProgressChanged;
            _games.Matches.SpectatorBoardChanged += OnSpectatorBoardChanged;
            _games.Matches.PlayerConnectionChanged += OnPlayerConnectionChanged;
            _games.Matches.MatchFinished += OnMatchFinished;
            _games.Matches.MatchAborted += OnMatchFinished;
        }

        public async Task<Message> DispatchAsync(
            Message request,
            ClientConnectionContext context)
        {
            if (request == null)
                return Error(
                    null,
                    ProtocolErrorCode.InvalidPacket,
                    "Message is required.");

            if (request.ProtocolVersion != Message.CurrentProtocolVersion)
                return Error(
                    request,
                    ProtocolErrorCode.UnsupportedProtocol,
                    "Protocol version is not supported.");

            try
            {
                if (request.Type == MessageType.Handshake)
                {
                    Message response = HandleHandshake(request, context);
                    await BroadcastOnlinePlayersAsync();
                    return response;
                }

                if (request.Type == MessageType.Reconnect)
                {
                    Message response = HandleReconnect(request, context);
                    await BroadcastOnlinePlayersAsync();
                    return response;
                }

                if (context.Session == null)
                    return Error(
                        request,
                        ProtocolErrorCode.Unauthorized,
                        "Handshake is required.");

                switch (request.Type)
                {
                    case MessageType.Heartbeat:
                        return Response(
                            request,
                            MessageType.HeartbeatAck,
                            new EmptyPayload());

                    case MessageType.ListRooms:
                        return Response(
                            request,
                            MessageType.ListRooms,
                            CreateRoomList());

                    case MessageType.ListOnlinePlayers:
                        return Response(
                            request,
                            MessageType.ListOnlinePlayers,
                            CreateOnlinePlayerList());

                    case MessageType.ListChallenges:
                        return Response(
                            request,
                            MessageType.ListChallenges,
                            new ChallengeListResponse
                            {
                                Challenges = _challenges
                                    .ListFor(context.Session.PlayerId)
                                    .ToList()
                            });

                    case MessageType.SendChallenge:
                        return await SendChallengeAsync(
                            request,
                            context.Session);

                    case MessageType.AcceptChallenge:
                        return await AcceptChallengeAsync(
                            request,
                            context.Session);

                    case MessageType.DeclineChallenge:
                        return await DeclineChallengeAsync(
                            request,
                            context.Session);

                    case MessageType.CancelChallenge:
                        return await CancelChallengeAsync(
                            request,
                            context.Session);

                    case MessageType.JoinSpectator:
                        {
                            Message response =
                                JoinSpectator(
                                    request,
                                    context.Session);

                            await BroadcastOnlinePlayersAsync();

                            return response;
                        }

                    case MessageType.LeaveSpectator:
                        {
                            Message response =
                                LeaveSpectator(
                                    request,
                                    context.Session);

                            await BroadcastOnlinePlayersAsync();

                            return response;
                        }

                    case MessageType.CreateRoom:
                        return await CreateRoomAsync(
                            request,
                            context.Session);

                    case MessageType.JoinRoom:
                        return await JoinRoomAsync(
                            request,
                            context.Session);

                    case MessageType.LeaveRoom:
                        return await LeaveRoomAsync(
                            request,
                            context.Session);

                    case MessageType.StartMatch:
                        return await StartMatchAsync(
                            request,
                            context.Session);

                    case MessageType.PlayerReady:
                        return PlayerReady(
                            request,
                            context.Session);

                    case MessageType.GetMatchStatus:
                        return GetMatchStatus(
                            request,
                            context.Session);

                    case MessageType.SubmitMove:
                        return SubmitMove(
                            request,
                            context.Session);

                    // ============================
                    // MATCH HISTORY
                    // ============================
                    case MessageType.GetMatchHistory:
                        return GetMatchHistory(
                            request,
                            context.Session);

                    default:
                        return Error(
                            request,
                            ProtocolErrorCode.InvalidPayload,
                            "Message type is not a client command.");
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                return Error(
                    request,
                    ProtocolErrorCode.Unauthorized,
                    exception.Message);
            }
            catch (KeyNotFoundException)
            {
                return Error(
                    request,
                    ProtocolErrorCode.MatchNotFound,
                    "Match was not found.");
            }
            catch (ArgumentException exception)
            {
                return Error(
                    request,
                    ProtocolErrorCode.InvalidPayload,
                    exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Error(
                    request,
                    ProtocolErrorCode.InvalidPayload,
                    exception.Message);
            }
            catch
            {
                return Error(
                    request,
                    ProtocolErrorCode.InternalError,
                    "The server could not process the request.");
            }
        }

        public void HandleDisconnected(
            ClientSession session,
            Guid connectionId)
        {
            if (session == null ||
                session.ConnectionId != connectionId)
                return;

            _sessions.MarkDisconnected(
                session,
                connectionId);

            _games.Matches.LeaveSpectatorFromAll(
                session.PlayerId);

            ActiveMatchSummary[] active =
                _games.Matches
                    .GetActiveMatchSummaries(_clock.UtcNow)
                    .ToArray();

            foreach (ActiveMatchSummary match in active)
            {
                if (match.PlayerAId == session.PlayerId ||
                    match.PlayerBId == session.PlayerId)
                {
                    _games.Matches.HandleDisconnect(
                        match.MatchId,
                        session.PlayerId,
                        _clock.UtcNow);
                }
            }

            foreach (Room room in _games.Rooms.GetAllRooms())
            {
                if (room.Players.Any(
                        player => player.PlayerId == session.PlayerId) &&
                    !active.Any(
                        match => match.RoomId == room.RoomId))
                {
                    _games.Rooms.LeaveRoom(
                        room.RoomId,
                        session.PlayerId);
                }
            }

            ChallengeDto[] invalidated =
                _challenges.InvalidateForPlayers(
                    session.PlayerId);

            _ = PushChallengesAsync(invalidated);
            _ = BroadcastRoomsAsync();
            _ = BroadcastOnlinePlayersAsync();
        }

        private Message HandleHandshake(
            Message request,
            ClientConnectionContext context)
        {
            if (context.Session != null)
                throw new InvalidOperationException(
                    "Connection is already authenticated.");

            HandshakeRequest payload =
                request.ReadPayload<HandshakeRequest>();

            context.Session = _sessions.Create(
                payload.PlayerId,
                payload.PlayerName,
                context.SendAsync,
                context.Close,
                context.ConnectionId);

            return Response(
                request,
                MessageType.HandshakeAccepted,
                new HandshakeResponse
                {
                    PlayerId = context.Session.PlayerId,
                    SessionToken = context.Session.Token
                });
        }

        private Message HandleReconnect(
            Message request,
            ClientConnectionContext context)
        {
            ReconnectRequest payload =
                request.ReadPayload<ReconnectRequest>();

            context.Session = _sessions.Reconnect(
                payload.SessionToken,
                context.SendAsync,
                context.Close,
                context.ConnectionId);

            foreach (ActiveMatchSummary match in
                _games.Matches.GetActiveMatchSummaries(
                    _clock.UtcNow))
            {
                if (match.PlayerAId == context.Session.PlayerId ||
                    match.PlayerBId == context.Session.PlayerId)
                {
                    _games.Matches.HandleReconnect(
                        match.MatchId,
                        context.Session.PlayerId,
                        _clock.UtcNow);
                }
            }

            return Response(
                request,
                MessageType.ReconnectAccepted,
                new HandshakeResponse
                {
                    PlayerId = context.Session.PlayerId,
                    SessionToken = context.Session.Token
                });
        }

        private async Task<Message> CreateRoomAsync(
            Message request,
            ClientSession session)
        {
            CreateRoomRequest payload =
                request.ReadPayload<CreateRoomRequest>();

            Room room;
            ChallengeDto[] invalidated;

            lock (_challenges.SyncRoot)
            {
                if (FindRoomForPlayer(session.PlayerId) != null)
                {
                    return Error(
                        request,
                        ProtocolErrorCode.InvalidPayload,
                        "Player is already in a room.");
                }

                room = _games.Rooms.CreateRoom(
                    payload.RoomName,
                    new Player(
                        session.PlayerId,
                        session.PlayerName),
                    payload.Difficulty);

                invalidated =
                    _challenges.InvalidateForPlayers(
                        session.PlayerId);
            }

            await PushChallengesAsync(invalidated);
            await BroadcastRoomsAsync();
            await BroadcastOnlinePlayersAsync();

            return Response(
                request,
                MessageType.CreateRoom,
                MapRoom(room));
        }

        private async Task<Message> JoinRoomAsync(
            Message request,
            ClientSession session)
        {
            RoomRequest payload =
                request.ReadPayload<RoomRequest>();

            bool joined;
            ChallengeDto[] invalidated;

            lock (_challenges.SyncRoot)
            {
                if (FindRoomForPlayer(session.PlayerId) != null)
                {
                    return Error(
                        request,
                        ProtocolErrorCode.InvalidPayload,
                        "Player is already in a room.");
                }

                Room existing =
                    _games.Rooms.GetRoom(
                        payload.RoomId);

                if (existing != null &&
                    _games.Matches
                        .GetActiveMatchSummaries(
                            _clock.UtcNow)
                        .Any(match =>
                            match.RoomId == payload.RoomId))
                {
                    return Error(
                        request,
                        ProtocolErrorCode.RoomFull,
                        "Room already has an active match.");
                }

                joined =
                    _games.Rooms.JoinRoom(
                        payload.RoomId,
                        new Player(
                            session.PlayerId,
                            session.PlayerName));

                invalidated =
                    joined
                        ? _challenges.InvalidateForPlayers(
                            session.PlayerId)
                        : new ChallengeDto[0];
            }

            if (!joined)
            {
                return Error(
                    request,
                    ProtocolErrorCode.RoomFull,
                    "Room does not exist, is full, or already contains the player.");
            }

            await PushChallengesAsync(invalidated);
            await BroadcastRoomsAsync();
            await BroadcastOnlinePlayersAsync();

            return Response(
                request,
                MessageType.JoinRoom,
                MapRoom(
                    _games.Rooms.GetRoom(
                        payload.RoomId)));
        }

        private async Task<Message> LeaveRoomAsync(
            Message request,
            ClientSession session)
        {
            RoomRequest payload =
                request.ReadPayload<RoomRequest>();

            bool left;

            lock (_challenges.SyncRoot)
            {
                left =
                    _games.Rooms.LeaveRoom(
                        payload.RoomId,
                        session.PlayerId);
            }

            if (!left)
            {
                return Error(
                    request,
                    ProtocolErrorCode.RoomNotFound,
                    "Player or room was not found.");
            }

            await BroadcastRoomsAsync();
            await BroadcastOnlinePlayersAsync();

            return Response(
                request,
                MessageType.LeaveRoom,
                new EmptyPayload());
        }

        private async Task<Message> StartMatchAsync(
            Message request,
            ClientSession session)
        {
            StartMatchRequest payload =
                request.ReadPayload<StartMatchRequest>();

            Guid roomId;

            if (!Guid.TryParse(
                    payload.RoomId,
                    out roomId))
            {
                throw new ArgumentException(
                    "Room id is invalid.");
            }

            Match match;

            lock (_challenges.SyncRoot)
            {
                Room room =
                    _games.Rooms.GetRoom(roomId);

                if (room == null ||
                    !room.Players.Any(
                        player =>
                            player.PlayerId ==
                            session.PlayerId))
                {
                    throw new UnauthorizedAccessException(
                        "Player does not belong to the room.");
                }

                if (room.ChallengeDuration.HasValue &&
                    room.ChallengeDuration.Value !=
                    payload.Duration)
                {
                    throw new ArgumentException(
                        "Challenge duration does not match the accepted invitation.");
                }

                match =
                    _games.MatchCoordinator.StartMatch(
                        payload);
            }

            await BroadcastRoomsAsync();
            await BroadcastOnlinePlayersAsync();

            string opponentId =
                match.GetOpponent(
                    session.PlayerId);

            try
            {
                await _sessions.SendToPlayerAsync(
                    opponentId,
                    Message.Create(
                        MessageType.MatchPrepared,
                        _games.Matches.GetPlayerStatus(
                            match.MatchId,
                            opponentId)));
            }
            catch
            {
                // The preparing timeout will clean up
                // a match whose opponent cannot be reached.
            }

            return Response(
                request,
                MessageType.MatchPrepared,
                _games.Matches.GetPlayerStatus(
                    match.MatchId,
                    session.PlayerId));
        }

        private Message PlayerReady(
            Message request,
            ClientSession session)
        {
            MatchIdRequest payload =
                request.ReadPayload<MatchIdRequest>();

            _games.Matches.MarkPlayerReady(
                payload.MatchId,
                session.PlayerId);

            return Response(
                request,
                MessageType.MatchStatusUpdated,
                _games.Matches.GetPlayerStatus(
                    payload.MatchId,
                    session.PlayerId));
        }

        private Message GetMatchStatus(
            Message request,
            ClientSession session)
        {
            MatchIdRequest payload =
                request.ReadPayload<MatchIdRequest>();

            return Response(
                request,
                MessageType.MatchStatusUpdated,
                _games.Matches.GetPlayerStatus(
                    payload.MatchId,
                    session.PlayerId));
        }

        private Message SubmitMove(
            Message request,
            ClientSession session)
        {
            SubmitMoveRequest payload =
                request.ReadPayload<SubmitMoveRequest>();

            MoveResult result =
                _games.Matches.SubmitMove(
                    payload.MatchId,
                    session.PlayerId,
                    payload.MoveId,
                    payload.Row,
                    payload.Column,
                    payload.Value);

            return Response(
                request,
                MessageType.MoveResult,
                MapMove(result));
        }

        // ============================================================
        // MATCH HISTORY
        // ============================================================

        private Message GetMatchHistory(
            Message request,
            ClientSession session)
        {
            IList<Match> matches =
                _games.Matches.GetMatchHistory();

            List<MatchHistoryItem> history =
                new List<MatchHistoryItem>();

            foreach (Match match in matches)
            {
                if (match == null)
                    continue;

                if (!match.IsPlayer(
                        session.PlayerId))
                    continue;

                bool isPlayerA =
                    match.PlayerAId ==
                    session.PlayerId;

                string opponentId =
                    isPlayerA
                        ? match.PlayerBId
                        : match.PlayerAId;

                string opponentName =
                    _sessions.GetOnline(
                        opponentId)?.PlayerName
                    ?? opponentId;

                int ownCorrectCount =
                    isPlayerA
                        ? match.BoardA.CorrectCount
                        : match.BoardB.CorrectCount;

                int ownErrorCount =
                    isPlayerA
                        ? match.BoardA.ErrorCount
                        : match.BoardB.ErrorCount;

                int opponentCorrectCount =
                    isPlayerA
                        ? match.BoardB.CorrectCount
                        : match.BoardA.CorrectCount;

                int opponentErrorCount =
                    isPlayerA
                        ? match.BoardB.ErrorCount
                        : match.BoardA.ErrorCount;

                MatchFinishReasonCode? finishReason =
                    MapHistoryFinishReason(
                        match.Result);

                history.Add(
                    new MatchHistoryItem
                    {
                        MatchId =
                            match.MatchId,

                        OpponentName =
                            opponentName,

                        StartedAtUtc =
                            match.StartedAtUtc,

                        FinishedAtUtc =
                            match.Result == null
                                ? (DateTime?)null
                                : match.Result.FinishedAtUtc,

                        Duration =
                            match.Duration,

                        OwnCorrectCount =
                            ownCorrectCount,

                        OwnErrorCount =
                            ownErrorCount,

                        OpponentCorrectCount =
                            opponentCorrectCount,

                        OpponentErrorCount =
                            opponentErrorCount,

                        FinishReason =
                            finishReason,

                        WinnerPlayerId =
                            match.Result == null
                                ? null
                                : match.Result.WinnerPlayerId
                    });
            }

            history =
                history
                    .OrderByDescending(
                        item =>
                            item.FinishedAtUtc ??
                            item.StartedAtUtc ??
                            DateTime.MinValue)
                    .ToList();

            return Response(
                request,
                MessageType.MatchHistoryUpdated,
                new MatchHistoryResponse
                {
                    Matches = history
                });
        }

        private static MatchFinishReasonCode?
            MapHistoryFinishReason(
                MatchResult result)
        {
            if (result == null)
                return null;

            switch (result.Reason)
            {
                case MatchFinishReason.Completed:
                    return MatchFinishReasonCode.Completed;

                case MatchFinishReason.TimeUp:
                    return MatchFinishReasonCode.TimeUp;

                case MatchFinishReason.TechnicalWinDisconnect:
                    return MatchFinishReasonCode.TechnicalWinDisconnect;

                case MatchFinishReason.BothDisconnected:
                    return MatchFinishReasonCode.BothDisconnected;

                case MatchFinishReason.PreparingTimeout:
                    return MatchFinishReasonCode.PreparingTimeout;

                default:
                    return null;
            }
        }

        private RoomListResponse CreateRoomList()
        {
            return new RoomListResponse
            {
                Rooms =
                    _games.Rooms
                        .GetAllRooms()
                        .Select(MapRoom)
                        .ToList()
            };
        }

        private OnlinePlayerListResponse
            CreateOnlinePlayerList()
        {
            ActiveMatchSummary[] matches =
                _games.Matches
                    .GetActiveMatchSummaries(
                        _clock.UtcNow)
                    .ToArray();

            Room[] rooms =
                _games.Rooms
                    .GetAllRooms()
                    .ToArray();

            var response =
                new OnlinePlayerListResponse();

            foreach (ClientSession session in
                _sessions.GetOnlineSessions())
            {
                if (_games.Matches.IsSpectating(
                        session.PlayerId))
                    continue;

                Room room =
                    rooms.FirstOrDefault(
                        candidate =>
                            candidate.Players.Any(
                                player =>
                                    player.PlayerId ==
                                    session.PlayerId));

                ActiveMatchSummary match =
                    matches.FirstOrDefault(
                        candidate =>
                            candidate.PlayerAId ==
                                session.PlayerId ||
                            candidate.PlayerBId ==
                                session.PlayerId);

                bool ongoing =
                    match != null &&
                    match.EndsAtUtc.HasValue;

                string opponentId =
                    match == null
                        ? null
                        : match.PlayerAId ==
                            session.PlayerId
                            ? match.PlayerBId
                            : match.PlayerAId;

                response.Players.Add(
                    new OnlinePlayerDto
                    {
                        PlayerId =
                            session.PlayerId,

                        PlayerName =
                            session.PlayerName,

                        State =
                            ongoing
                                ? LobbyPlayerState.InMatch
                                : room != null
                                    ? LobbyPlayerState.InRoom
                                    : LobbyPlayerState.Available,

                        RoomName =
                            room?.RoomName,

                        MatchId =
                            ongoing
                                ? match.MatchId
                                : (Guid?)null,

                        OpponentName =
                            opponentId == null
                                ? null
                                : _sessions.GetOnline(
                                      opponentId)
                                  ?.PlayerName
                                  ??
                                  room?.Players
                                      .FirstOrDefault(
                                          player =>
                                              player.PlayerId ==
                                              opponentId)
                                      ?.PlayerName,

                        Difficulty =
                            room?.Difficulty
                    });
            }

            return response;
        }

        private Room FindRoomForPlayer(
            string playerId)
        {
            return _games.Rooms
                .GetAllRooms()
                .FirstOrDefault(
                    room =>
                        room.Players.Any(
                            player =>
                                player.PlayerId ==
                                playerId));
        }

        private Task BroadcastRoomsAsync()
        {
            return _sessions.BroadcastAsync(
                Message.Create(
                    MessageType.RoomUpdated,
                    CreateRoomList()));
        }

        private Task BroadcastOnlinePlayersAsync()
        {
            return _sessions.BroadcastAsync(
                Message.Create(
                    MessageType.OnlinePlayersUpdated,
                    CreateOnlinePlayerList()));
        }

        private async void OnMatchStarted(
            object sender,
            MatchEventArgs args)
        {
            await PushStatusAsync(
                args.Match,
                MessageType.MatchStarted);

            await BroadcastRoomsAsync();
            await BroadcastOnlinePlayersAsync();
        }

        private async void OnProgressChanged(
            object sender,
            MatchMoveEventArgs args)
        {
            string opponent =
                args.Match.GetOpponent(
                    args.PlayerId);

            await _sessions.SendToPlayerAsync(
                opponent,
                Message.Create(
                    MessageType.OpponentProgressUpdated,
                    MapMove(args.Move)));
        }

        private async void OnSpectatorBoardChanged(
            object sender,
            MatchMoveEventArgs args)
        {
            await PushSpectatorStatusAsync(
                args.Match);
        }

        private async void OnMatchFinished(
            object sender,
            MatchEventArgs args)
        {
            await PushStatusAsync(
                args.Match,
                MessageType.MatchFinished);

            await PushSpectatorStatusAsync(
                args.Match);

            _games.Rooms.RemoveRoom(
                args.Match.RoomId);

            await BroadcastRoomsAsync();
            await BroadcastOnlinePlayersAsync();
        }

        private async Task PushSpectatorStatusAsync(
            Match match)
        {
            string[] subscribers;

            lock (match.SyncRoot)
            {
                subscribers =
                    match.SpectatorIds.ToArray();
            }

            if (subscribers.Length == 0)
                return;

            SpectatorMatchState state =
                MapSpectator(
                    _games.Matches.GetSpectatorSnapshot(
                        match.MatchId));

            foreach (string spectatorId in subscribers)
            {
                try
                {
                    await _sessions.SendToPlayerAsync(
                        spectatorId,
                        Message.Create(
                            MessageType.SpectatorMatchUpdated,
                            state));
                }
                catch
                {
                    // A disconnected spectator must not
                    // affect the match.
                }
            }
        }

        private async void OnPlayerConnectionChanged(
            object sender,
            MatchEventArgs args)
        {
            Match match = args.Match;

            if (match.ConnectionA ==
                ConnectionStatus.Disconnected)
            {
                await PushPlayerStatusAsync(
                    match,
                    match.PlayerBId,
                    MessageType.PlayerDisconnected);
            }

            if (match.ConnectionB ==
                ConnectionStatus.Disconnected)
            {
                await PushPlayerStatusAsync(
                    match,
                    match.PlayerAId,
                    MessageType.PlayerDisconnected);
            }

            if (match.ConnectionA ==
                    ConnectionStatus.Connected &&
                match.ConnectionB ==
                    ConnectionStatus.Connected)
            {
                await PushStatusAsync(
                    match,
                    MessageType.MatchStatusUpdated);
            }
        }

        private Task PushStatusAsync(
            Match match,
            MessageType type)
        {
            return Task.WhenAll(
                PushPlayerStatusAsync(
                    match,
                    match.PlayerAId,
                    type),

                PushPlayerStatusAsync(
                    match,
                    match.PlayerBId,
                    type));
        }

        private async Task PushPlayerStatusAsync(
            Match match,
            string playerId,
            MessageType type)
        {
            try
            {
                await _sessions.SendToPlayerAsync(
                    playerId,
                    Message.Create(
                        type,
                        _games.Matches.GetPlayerStatus(
                            match.MatchId,
                            playerId)));
            }
            catch
            {
                // A disconnected client must not prevent
                // state finalization or room cleanup.
            }
        }

        private LobbyRoomDto MapRoom(
            Room room)
        {
            ActiveMatchSummary active =
                _games.Matches
                    .GetActiveMatchSummaries(
                        _clock.UtcNow)
                    .FirstOrDefault(
                        match =>
                            match.RoomId ==
                            room.RoomId);

            return new LobbyRoomDto
            {
                RoomId =
                    room.RoomId,

                RoomName =
                    room.RoomName,

                Players =
                    room.Players
                        .Select(
                            player =>
                                new LobbyPlayerDto
                                {
                                    PlayerId =
                                        player.PlayerId,

                                    PlayerName =
                                        player.PlayerName
                                })
                        .ToList(),

                HasActiveMatch =
                    active != null,

                ActiveMatchId =
                    active?.MatchId,

                MatchState =
                    active == null
                        ? (MatchLifecycleState?)null
                        : active.EndsAtUtc.HasValue
                            ? MatchLifecycleState.Ongoing
                            : MatchLifecycleState.Preparing,

                Duration =
                    room.ChallengeDuration,

                IsChallengeRoom =
                    room.ChallengeDuration.HasValue,

                Difficulty =
                    room.Difficulty
            };
        }

        private SpectatorMatchState MapSpectator(
            SpectatorMatchSnapshot snapshot)
        {
            Room room =
                _games.Rooms.GetRoom(
                    snapshot.RoomId);

            return new SpectatorMatchState
            {
                MatchId =
                    snapshot.MatchId,

                RoomId =
                    snapshot.RoomId,

                PlayerAId =
                    snapshot.PlayerAId,

                PlayerBId =
                    snapshot.PlayerBId,

                PlayerAName =
                    room?.Players
                        .FirstOrDefault(
                            player =>
                                player.PlayerId ==
                                snapshot.PlayerAId)
                        ?.PlayerName
                    ??
                    _sessions.GetOnline(
                        snapshot.PlayerAId)
                        ?.PlayerName
                    ??
                    snapshot.PlayerAId,

                PlayerBName =
                    room?.Players
                        .FirstOrDefault(
                            player =>
                                player.PlayerId ==
                                snapshot.PlayerBId)
                        ?.PlayerName
                    ??
                    _sessions.GetOnline(
                        snapshot.PlayerBId)
                        ?.PlayerName
                    ??
                    snapshot.PlayerBId,

                PuzzleA =
                    MatchGrid.Flatten(
                        snapshot.OriginalPuzzle),

                PuzzleB =
                    MatchGrid.Flatten(
                        snapshot.OriginalPuzzleB),

                BoardA =
                    MatchGrid.Flatten(
                        snapshot.BoardA),

                BoardB =
                    MatchGrid.Flatten(
                        snapshot.BoardB),

                CorrectCountA =
                    snapshot.CorrectCountA,

                CorrectCountB =
                    snapshot.CorrectCountB,

                ErrorCountA =
                    snapshot.ErrorCountA,

                ErrorCountB =
                    snapshot.ErrorCountB,

                Difficulty =
                    room?.Difficulty ??
                    SudokuDifficultyLevel.Medium,

                Duration =
                    snapshot.Duration,

                State =
                    (MatchLifecycleState)
                    snapshot.State,

                ServerUtcNow =
                    snapshot.ServerUtcNow,

                EndsAtUtc =
                    snapshot.EndsAtUtc,

                Version =
                    snapshot.Version
            };
        }

        private async Task<Message>
            SendChallengeAsync(
                Message request,
                ClientSession session)
        {
            ChallengeDto challenge =
                _challenges.Send(
                    session,
                    request.ReadPayload<
                        SendChallengeRequest>());

            await _sessions.SendToPlayerAsync(
                challenge.TargetPlayerId,
                Message.Create(
                    MessageType.ChallengeReceived,
                    challenge));

            return Response(
                request,
                MessageType.SendChallenge,
                challenge);
        }

        private async Task<Message>
            AcceptChallengeAsync(
                Message request,
                ClientSession session)
        {
            ChallengeIdRequest payload =
                request.ReadPayload<
                    ChallengeIdRequest>();

            ChallengeDto[] changed =
                _challenges.Accept(
                    session.PlayerId,
                    payload.ChallengeId);

            await PushChallengesAsync(
                changed);

            await BroadcastRoomsAsync();
            await BroadcastOnlinePlayersAsync();

            return Response(
                request,
                MessageType.AcceptChallenge,
                changed[0]);
        }

        private async Task<Message>
            DeclineChallengeAsync(
                Message request,
                ClientSession session)
        {
            ChallengeIdRequest payload =
                request.ReadPayload<
                    ChallengeIdRequest>();

            ChallengeDto challenge =
                _challenges.Decline(
                    session.PlayerId,
                    payload.ChallengeId);

            await PushChallengesAsync(
                new[] { challenge });

            return Response(
                request,
                MessageType.DeclineChallenge,
                challenge);
        }

        private async Task<Message>
            CancelChallengeAsync(
                Message request,
                ClientSession session)
        {
            ChallengeIdRequest payload =
                request.ReadPayload<
                    ChallengeIdRequest>();

            ChallengeDto challenge =
                _challenges.Cancel(
                    session.PlayerId,
                    payload.ChallengeId);

            await PushChallengesAsync(
                new[] { challenge });

            return Response(
                request,
                MessageType.CancelChallenge,
                challenge);
        }

        private Message JoinSpectator(
            Message request,
            ClientSession session)
        {
            MatchIdRequest payload =
                request.ReadPayload<
                    MatchIdRequest>();

            if (FindRoomForPlayer(
                    session.PlayerId) != null ||
                _games.Matches
                    .GetActiveMatchSummaries(
                        _clock.UtcNow)
                    .Any(
                        match =>
                            match.PlayerAId ==
                                session.PlayerId ||
                            match.PlayerBId ==
                                session.PlayerId))
            {
                throw new UnauthorizedAccessException(
                    "Players in a room or match cannot spectate.");
            }

            _games.Matches.LeaveSpectatorFromAll(
                session.PlayerId);

            SpectatorMatchSnapshot snapshot =
                _games.Matches.JoinSpectator(
                    payload.MatchId,
                    session.PlayerId);

            return Response(
                request,
                MessageType.JoinSpectator,
                MapSpectator(snapshot));
        }

        private Message LeaveSpectator(
            Message request,
            ClientSession session)
        {
            MatchIdRequest payload =
                request.ReadPayload<
                    MatchIdRequest>();

            _games.Matches.LeaveSpectator(
                payload.MatchId,
                session.PlayerId);

            return Response(
                request,
                MessageType.LeaveSpectator,
                new EmptyPayload());
        }

        private async Task PushChallengesAsync(
            IEnumerable<ChallengeDto> challenges)
        {
            foreach (ChallengeDto challenge in
                challenges)
            {
                Message update =
                    Message.Create(
                        MessageType.ChallengeUpdated,
                        challenge);

                await _sessions.SendToPlayerAsync(
                    challenge.ChallengerPlayerId,
                    update);

                await _sessions.SendToPlayerAsync(
                    challenge.TargetPlayerId,
                    update);
            }
        }

        private static MoveResultResponse MapMove(
            MoveResult result)
        {
            return new MoveResultResponse
            {
                Accepted =
                    result.Accepted,

                IsCorrect =
                    result.IsCorrect,

                ErrorCode =
                    result.ErrorCode.ToString(),

                CorrectCount =
                    result.CorrectCount,

                ErrorCount =
                    result.ErrorCount,

                BoardChanged =
                    result.BoardChanged,

                Row =
                    result.Row,

                Column =
                    result.Column,

                Value =
                    result.Value
            };
        }

        private static Message Response<T>(
            Message request,
            MessageType type,
            T payload)
        {
            Message response =
                Message.Create(
                    type,
                    payload);

            response.CorrelationId =
                request.MessageId;

            return response;
        }

        private static Message Error(
            Message request,
            ProtocolErrorCode code,
            string text)
        {
            Message response =
                Message.Create(
                    MessageType.Error,
                    new EmptyPayload());

            response.CorrelationId =
                request == null
                    ? null
                    : (Guid?)request.MessageId;

            response.Error =
                new ProtocolError
                {
                    Code = code,
                    Message = text
                };

            return response;
        }
    }
}