using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Linq;
using System.Threading.Tasks;
using Sudoku.Shared.Network;

namespace Sudoku.Server.Network
{
    internal sealed class ClientSession
    {
        public string PlayerId { get; set; }
        public string PlayerName { get; set; }
        public string Token { get; set; }
        public Func<Message, Task> SendAsync { get; set; }
        public Action CloseConnection { get; set; }
        public Guid ConnectionId { get; set; }
        public bool IsOnline { get; set; }
    }

    internal sealed class SessionManager
    {
        private readonly ConcurrentDictionary<string, ClientSession> _byToken =
            new ConcurrentDictionary<string, ClientSession>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ClientSession> _byPlayer =
            new ConcurrentDictionary<string, ClientSession>(StringComparer.Ordinal);
        private readonly object _sync = new object();

        public ClientSession GetOnline(string playerId)
        {
            ClientSession session;
            return !String.IsNullOrWhiteSpace(playerId) &&
                   _byPlayer.TryGetValue(playerId, out session) && session.IsOnline
                ? session : null;
        }

        public ClientSession[] GetOnlineSessions()
        {
            return _byPlayer.Values.Where(session => session.IsOnline).ToArray();
        }

        public ClientSession Create(
            string playerId,
            string playerName,
            Func<Message, Task> sendAsync,
            Action closeConnection,
            Guid connectionId)
        {
            if (String.IsNullOrWhiteSpace(playerId) || playerId.Length > 100)
                throw new ArgumentException("Player id is invalid.");
            if (String.IsNullOrWhiteSpace(playerName) || playerName.Length > 100)
                throw new ArgumentException("Player name is invalid.");
            lock (_sync)
            {
                playerId = playerId.Trim();
                ClientSession old;
                if (_byPlayer.TryGetValue(playerId, out old))
                {
                    if (old.IsOnline)
                        throw new InvalidOperationException("Player already has an active session.");
                    ClientSession ignored;
                    _byToken.TryRemove(old.Token, out ignored);
                }
                var session = new ClientSession
                {
                    PlayerId = playerId,
                    PlayerName = playerName.Trim(),
                    Token = CreateToken(),
                    SendAsync = sendAsync,
                    CloseConnection = closeConnection,
                    ConnectionId = connectionId,
                    IsOnline = true
                };
                _byPlayer[playerId] = session;
                _byToken[session.Token] = session;
                return session;
            }
        }

        public ClientSession Reconnect(
            string token,
            Func<Message, Task> sendAsync,
            Action closeConnection,
            Guid connectionId)
        {
            ClientSession session;
            Action oldClose;
            lock (_sync)
            {
                if (String.IsNullOrWhiteSpace(token) || !_byToken.TryGetValue(token, out session))
                    throw new UnauthorizedAccessException("Reconnect token is invalid.");
                oldClose = session.CloseConnection;
                session.SendAsync = sendAsync;
                session.CloseConnection = closeConnection;
                session.ConnectionId = connectionId;
                session.IsOnline = true;
            }
            if (oldClose != null && oldClose != closeConnection) oldClose();
            return session;
        }

        public void MarkDisconnected(ClientSession session, Guid connectionId)
        {
            if (session == null) return;
            lock (_sync)
            {
                if (session.ConnectionId == connectionId)
                    session.IsOnline = false;
            }
        }

        public Task SendToPlayerAsync(string playerId, Message message)
        {
            ClientSession session;
            return _byPlayer.TryGetValue(playerId, out session) && session.IsOnline && session.SendAsync != null
                ? session.SendAsync(message)
                : Task.FromResult(0);
        }

        public Task BroadcastAsync(Message message)
        {
            return Task.WhenAll(System.Linq.Enumerable.Select(
                GetOnlineSessions(),
                session => TrySendBroadcastAsync(session, message)));
        }

        private static async Task TrySendBroadcastAsync(ClientSession session, Message message)
        {
            try { await session.SendAsync(message); }
            catch
            {
                // A stale peer must not prevent the new client's handshake response.
                // Its own connection handler will mark it offline.
            }
        }

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
    }
}
