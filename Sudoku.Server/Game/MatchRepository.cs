using System;
using System.Collections.Concurrent;

namespace Sudoku.Server.Game
{
    // Persistence boundary. A SQLite implementation can replace this without changing MatchManager.
    internal interface IMatchRepository
    {
        void SaveMatch(Match match);
        void SaveMove(Guid matchId, string playerId, string moveId, MoveResult result, DateTime occurredAtUtc);
    }

    internal sealed class InMemoryMatchRepository : IMatchRepository
    {
        private readonly ConcurrentDictionary<Guid, Match> _matches = new ConcurrentDictionary<Guid, Match>();

        public void SaveMatch(Match match) { _matches[match.MatchId] = match; }
        public void SaveMove(Guid matchId, string playerId, string moveId, MoveResult result, DateTime occurredAtUtc) { }
    }
}
