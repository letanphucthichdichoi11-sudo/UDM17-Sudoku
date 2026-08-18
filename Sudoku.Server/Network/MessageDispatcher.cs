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

        public MessageDispatcher(
            GameApplicationServices games,
            SessionManager sessions,
            IClock clock)
        {
            _games = games;
            _sessions = sessions;
            _clock = clock;
            _games.Matches.MatchStarted += OnMatchStarted;
            _games.Matches.PlayerProgressChanged += OnProgressChanged;
            _games.Matches.PlayerConnectionChanged += OnPlayerConnectionChanged;
            _games.Matches.MatchFinished += OnMatchFinished;
            _games.Matches.MatchAborted += OnMatchFinished;
        }

        public async Task<Message> DispatchAsync(
            Message request,
            ClientConnectionContext context)
        {
            if (request == null) return Error(null, ProtocolErrorCode.InvalidPacket, "Message is required.");
            if (request.ProtocolVersion != Message.CurrentProtocolVersion)
                return Error(request, ProtocolErrorCode.UnsupportedProtocol, "Protocol version is not supported.");

            try
            {
                if (request.Type == MessageType.Handshake)
                    return HandleHandshake(request, context);
                if (request.Type == MessageType.Reconnect)
                    return HandleReconnect(request, context);
                if (context.Session == null)
                    return Error(request, ProtocolErrorCode.Unauthorized, "Handshake is required.");

                switch (request.Type)
                {
                    case MessageType.Heartbeat:
                        return Response(request, MessageType.HeartbeatAck, new EmptyPayload());
                    case MessageType.ListRooms:
                        return Response(request, MessageType.ListRooms, CreateRoomList());
                    case MessageType.CreateRoom:
                        return await CreateRoomAsync(request, context.Session);
                    case MessageType.JoinRoom:
                        return await JoinRoomAsync(request, context.Session);
                    case MessageType.LeaveRoom:
                        return await LeaveRoomAsync(request, context.Session);
                    case MessageType.StartMatch:
                        return await StartMatchAsync(request, context.Session);
                    case MessageType.PlayerReady:
                        return PlayerReady(request, context.Session);
                    case MessageType.GetMatchStatus:
                        return GetMatchStatus(request, context.Session);
                    case MessageType.SubmitMove:
                        return SubmitMove(request, context.Session);
                    default:
                        return Error(request, ProtocolErrorCode.InvalidPayload, "Message type is not a client command.");
                }
            }
            catch (UnauthorizedAccessException exception)
            {
                return Error(request, ProtocolErrorCode.Unauthorized, exception.Message);
            }
            catch (KeyNotFoundException)
            {
                return Error(request, ProtocolErrorCode.MatchNotFound, "Match was not found.");
            }
            catch (ArgumentException exception)
            {
                return Error(request, ProtocolErrorCode.InvalidPayload, exception.Message);
            }
            catch (InvalidOperationException exception)
            {
                return Error(request, ProtocolErrorCode.InvalidPayload, exception.Message);
            }
            catch
            {
                return Error(request, ProtocolErrorCode.InternalError, "The server could not process the request.");
            }
        }

        public void HandleDisconnected(ClientSession session, Guid connectionId)
        {
            if (session == null || session.ConnectionId != connectionId) return;
            foreach (ActiveMatchSummary match in _games.Matches.GetActiveMatchSummaries(_clock.UtcNow))
                if (match.PlayerAId == session.PlayerId || match.PlayerBId == session.PlayerId)
                    _games.Matches.HandleDisconnect(match.MatchId, session.PlayerId, _clock.UtcNow);
        }

        private Message HandleHandshake(Message request, ClientConnectionContext context)
        {
            if (context.Session != null) throw new InvalidOperationException("Connection is already authenticated.");
            HandshakeRequest payload = request.ReadPayload<HandshakeRequest>();
            context.Session = _sessions.Create(
                payload.PlayerId, payload.PlayerName, context.SendAsync, context.Close,
                context.ConnectionId);
            return Response(request, MessageType.HandshakeAccepted, new HandshakeResponse
            {
                PlayerId = context.Session.PlayerId,
                SessionToken = context.Session.Token
            });
        }

        private Message HandleReconnect(Message request, ClientConnectionContext context)
        {
            ReconnectRequest payload = request.ReadPayload<ReconnectRequest>();
            context.Session = _sessions.Reconnect(
                payload.SessionToken, context.SendAsync, context.Close,
                context.ConnectionId);
            foreach (ActiveMatchSummary match in _games.Matches.GetActiveMatchSummaries(_clock.UtcNow))
                if (match.PlayerAId == context.Session.PlayerId || match.PlayerBId == context.Session.PlayerId)
                    _games.Matches.HandleReconnect(match.MatchId, context.Session.PlayerId, _clock.UtcNow);
            return Response(request, MessageType.ReconnectAccepted, new HandshakeResponse
            {
                PlayerId = context.Session.PlayerId,
                SessionToken = context.Session.Token
            });
        }

        private async Task<Message> CreateRoomAsync(Message request, ClientSession session)
        {
            CreateRoomRequest payload = request.ReadPayload<CreateRoomRequest>();
            Room room = _games.Rooms.CreateRoom(payload.RoomName, new Player(session.PlayerId, session.PlayerName));
            await BroadcastRoomsAsync();
            return Response(request, MessageType.CreateRoom, MapRoom(room));
        }

        private async Task<Message> JoinRoomAsync(Message request, ClientSession session)
        {
            RoomRequest payload = request.ReadPayload<RoomRequest>();
            if (!_games.Rooms.JoinRoom(payload.RoomId, new Player(session.PlayerId, session.PlayerName)))
                return Error(request, ProtocolErrorCode.RoomFull, "Room does not exist, is full, or already contains the player.");
            await BroadcastRoomsAsync();
            return Response(request, MessageType.JoinRoom, MapRoom(_games.Rooms.GetRoom(payload.RoomId)));
        }

        private async Task<Message> LeaveRoomAsync(Message request, ClientSession session)
        {
            RoomRequest payload = request.ReadPayload<RoomRequest>();
            if (!_games.Rooms.LeaveRoom(payload.RoomId, session.PlayerId))
                return Error(request, ProtocolErrorCode.RoomNotFound, "Player or room was not found.");
            await BroadcastRoomsAsync();
            return Response(request, MessageType.LeaveRoom, new EmptyPayload());
        }

        private async Task<Message> StartMatchAsync(Message request, ClientSession session)
        {
            StartMatchRequest payload = request.ReadPayload<StartMatchRequest>();
            Guid roomId;
            if (!Guid.TryParse(payload.RoomId, out roomId)) throw new ArgumentException("Room id is invalid.");
            Room room = _games.Rooms.GetRoom(roomId);
            if (room == null || !room.Players.Any(player => player.PlayerId == session.PlayerId))
                throw new UnauthorizedAccessException("Player does not belong to the room.");
            Match match = _games.MatchCoordinator.StartMatch(payload);
            string opponentId = match.GetOpponent(session.PlayerId);
            try
            {
                await _sessions.SendToPlayerAsync(opponentId,
                    Message.Create(MessageType.MatchPrepared,
                        _games.Matches.GetPlayerStatus(match.MatchId, opponentId)));
            }
            catch
            {
                // The preparing timeout will clean up a match whose opponent cannot be reached.
            }
            return Response(request, MessageType.MatchPrepared,
                _games.Matches.GetPlayerStatus(match.MatchId, session.PlayerId));
        }

        private Message PlayerReady(Message request, ClientSession session)
        {
            MatchIdRequest payload = request.ReadPayload<MatchIdRequest>();
            _games.Matches.MarkPlayerReady(payload.MatchId, session.PlayerId);
            return Response(request, MessageType.MatchStatusUpdated,
                _games.Matches.GetPlayerStatus(payload.MatchId, session.PlayerId));
        }

        private Message GetMatchStatus(Message request, ClientSession session)
        {
            MatchIdRequest payload = request.ReadPayload<MatchIdRequest>();
            return Response(request, MessageType.MatchStatusUpdated,
                _games.Matches.GetPlayerStatus(payload.MatchId, session.PlayerId));
        }

        private Message SubmitMove(Message request, ClientSession session)
        {
            SubmitMoveRequest payload = request.ReadPayload<SubmitMoveRequest>();
            MoveResult result = _games.Matches.SubmitMove(
                payload.MatchId, session.PlayerId, payload.MoveId,
                payload.Row, payload.Column, payload.Value);
            return Response(request, MessageType.MoveResult, MapMove(result));
        }

        private RoomListResponse CreateRoomList()
        {
            return new RoomListResponse { Rooms = _games.Rooms.GetAllRooms().Select(MapRoom).ToList() };
        }

        private Task BroadcastRoomsAsync()
        {
            return _sessions.BroadcastAsync(Message.Create(MessageType.RoomUpdated, CreateRoomList()));
        }

        private async void OnMatchStarted(object sender, MatchEventArgs args)
        {
            await PushStatusAsync(args.Match, MessageType.MatchStarted);
        }

        private async void OnProgressChanged(object sender, MatchMoveEventArgs args)
        {
            string opponent = args.Match.GetOpponent(args.PlayerId);
            await _sessions.SendToPlayerAsync(opponent,
                Message.Create(MessageType.OpponentProgressUpdated, MapMove(args.Move)));
        }

        private async void OnMatchFinished(object sender, MatchEventArgs args)
        {
            await PushStatusAsync(args.Match, MessageType.MatchFinished);
            if (args.Match.Result != null &&
                args.Match.Result.Reason == MatchFinishReason.PreparingTimeout)
            {
                if (args.Match.ConnectionA == ConnectionStatus.Disconnected)
                    _games.Rooms.LeaveRoom(args.Match.RoomId, args.Match.PlayerAId);
                if (args.Match.ConnectionB == ConnectionStatus.Disconnected)
                    _games.Rooms.LeaveRoom(args.Match.RoomId, args.Match.PlayerBId);
                await BroadcastRoomsAsync();
            }
        }

        private async void OnPlayerConnectionChanged(object sender, MatchEventArgs args)
        {
            Match match = args.Match;
            if (match.ConnectionA == ConnectionStatus.Disconnected)
                await PushPlayerStatusAsync(match, match.PlayerBId, MessageType.PlayerDisconnected);
            if (match.ConnectionB == ConnectionStatus.Disconnected)
                await PushPlayerStatusAsync(match, match.PlayerAId, MessageType.PlayerDisconnected);
            if (match.ConnectionA == ConnectionStatus.Connected &&
                match.ConnectionB == ConnectionStatus.Connected)
                await PushStatusAsync(match, MessageType.MatchStatusUpdated);
        }

        private Task PushStatusAsync(Match match, MessageType type)
        {
            return Task.WhenAll(
                PushPlayerStatusAsync(match, match.PlayerAId, type),
                PushPlayerStatusAsync(match, match.PlayerBId, type));
        }

        private async Task PushPlayerStatusAsync(Match match, string playerId, MessageType type)
        {
            try
            {
                await _sessions.SendToPlayerAsync(playerId,
                    Message.Create(type, _games.Matches.GetPlayerStatus(match.MatchId, playerId)));
            }
            catch
            {
                // A disconnected client must not prevent state finalization or room cleanup.
            }
        }

        private LobbyRoomDto MapRoom(Room room)
        {
            bool hasActiveMatch = _games.Matches
                .GetActiveMatchSummaries(_clock.UtcNow)
                .Any(match => match.RoomId == room.RoomId);
            return new LobbyRoomDto
            {
                RoomId = room.RoomId,
                RoomName = room.RoomName,
                Players = room.Players.Select(player => new LobbyPlayerDto
                {
                    PlayerId = player.PlayerId,
                    PlayerName = player.PlayerName
                }).ToList(),
                HasActiveMatch = hasActiveMatch
            };
        }

        private static MoveResultResponse MapMove(MoveResult result)
        {
            return new MoveResultResponse
            {
                Accepted = result.Accepted,
                IsCorrect = result.IsCorrect,
                ErrorCode = result.ErrorCode.ToString(),
                CorrectCount = result.CorrectCount,
                ErrorCount = result.ErrorCount,
                BoardChanged = result.BoardChanged,
                Row = result.Row,
                Column = result.Column,
                Value = result.Value
            };
        }

        private static Message Response<T>(Message request, MessageType type, T payload)
        {
            Message response = Message.Create(type, payload);
            response.CorrelationId = request.MessageId;
            return response;
        }

        private static Message Error(Message request, ProtocolErrorCode code, string text)
        {
            Message response = Message.Create(MessageType.Error, new EmptyPayload());
            response.CorrelationId = request == null ? null : (Guid?)request.MessageId;
            response.Error = new ProtocolError { Code = code, Message = text };
            return response;
        }
    }
}
