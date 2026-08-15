using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
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
    }

    internal sealed class SessionManager
    {
        private readonly ConcurrentDictionary<string, ClientSession> _byToken =
            new ConcurrentDictionary<string, ClientSession>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ClientSession> _byPlayer =
            new ConcurrentDictionary<string, ClientSession>(StringComparer.Ordinal);

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
            if (_byPlayer.ContainsKey(playerId))
                throw new InvalidOperationException("Player already has an active session.");

            var session = new ClientSession
            {
                PlayerId = playerId.Trim(),
                PlayerName = playerName.Trim(),
                Token = CreateToken(),
                SendAsync = sendAsync,
                CloseConnection = closeConnection,
                ConnectionId = connectionId
            };
            if (!_byPlayer.TryAdd(session.PlayerId, session) ||
                !_byToken.TryAdd(session.Token, session))
                throw new InvalidOperationException("Could not create player session.");
            return session;
        }

        public ClientSession Reconnect(
            string token,
            Func<Message, Task> sendAsync,
            Action closeConnection,
            Guid connectionId)
        {
            ClientSession session;
            if (String.IsNullOrWhiteSpace(token) || !_byToken.TryGetValue(token, out session))
                throw new UnauthorizedAccessException("Reconnect token is invalid.");

            Action oldClose = session.CloseConnection;
            session.SendAsync = sendAsync;
            session.CloseConnection = closeConnection;
            session.ConnectionId = connectionId;
            if (oldClose != null && oldClose != closeConnection) oldClose();
            return session;
        }

        public Task SendToPlayerAsync(string playerId, Message message)
        {
            ClientSession session;
            return _byPlayer.TryGetValue(playerId, out session) && session.SendAsync != null
                ? session.SendAsync(message)
                : Task.FromResult(0);
        }

        public Task BroadcastAsync(Message message)
        {
            return Task.WhenAll(System.Linq.Enumerable.Select(
                _byPlayer.Values,
                session => session.SendAsync(message)));
        }

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
    }
}
