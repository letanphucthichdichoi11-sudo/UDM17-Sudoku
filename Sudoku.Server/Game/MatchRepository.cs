using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Game
{
    // Persistence boundary.
    // Hiện tại dùng InMemory để lưu lịch sử trong thời gian Server đang chạy.
    // Sau này nếu cần SQLite có thể thay implementation này mà không ảnh hưởng MatchManager.
    internal interface IMatchRepository
    {
        void SaveMatch(Match match);

        void SaveMove(
            Guid matchId,
            string playerId,
            string moveId,
            MoveResult result,
            DateTime occurredAtUtc);

        IList<Match> GetFinishedMatches();
    }

    internal sealed class InMemoryMatchRepository : IMatchRepository
    {
        private readonly ConcurrentDictionary<Guid, Match> _matches =
            new ConcurrentDictionary<Guid, Match>();

        public void SaveMatch(Match match)
        {
            if (match == null)
                return;

            _matches[match.MatchId] = match;
        }

        public void SaveMove(
            Guid matchId,
            string playerId,
            string moveId,
            MoveResult result,
            DateTime occurredAtUtc)
        {
            // Move đã được cập nhật trực tiếp vào Match.
            // Repository hiện tại chỉ cần giữ Match snapshot.
        }

        public IList<Match> GetFinishedMatches()
        {
            return _matches.Values
                .Where(match =>
                    match.State == MatchState.Finished ||
                    match.State == MatchState.Aborted ||
                    match.State == MatchState.Archived)
                .OrderByDescending(match =>
                    match.Result != null
                        ? match.Result.FinishedAtUtc
                        : DateTime.MinValue)
                .ToList();
        }
    }
}