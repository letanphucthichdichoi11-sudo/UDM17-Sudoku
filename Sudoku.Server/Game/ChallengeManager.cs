using System;
using System.Collections.Generic;
using System.Linq;
using Sudoku.Server.Network;
using Sudoku.Shared.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Server.Game
{
    // All room membership changes and challenge transitions use SyncRoot in the dispatcher.
    internal sealed class ChallengeManager
    {
        private readonly Dictionary<Guid, ChallengeDto> _challenges = new Dictionary<Guid, ChallengeDto>();
        private readonly RoomManager _rooms;
        private readonly MatchManager _matches;
        private readonly SessionManager _sessions;
        private readonly IClock _clock;

        public object SyncRoot { get; } = new object();

        public ChallengeManager(RoomManager rooms, MatchManager matches, SessionManager sessions, IClock clock)
        {
            _rooms = rooms;
            _matches = matches;
            _sessions = sessions;
            _clock = clock;
        }

        public ChallengeDto[] ListFor(string playerId)
        {
            lock (SyncRoot)
            {
                ExpirePending();
                return _challenges.Values.Where(challenge =>
                    challenge.ChallengerPlayerId == playerId || challenge.TargetPlayerId == playerId)
                    .OrderByDescending(challenge => challenge.CreatedAtUtc).ToArray();
            }
        }

        public ChallengeDto Send(ClientSession challenger, SendChallengeRequest request)
        {
            lock (SyncRoot)
            {
                ExpirePending();
                if (request == null || String.IsNullOrWhiteSpace(request.TargetPlayerId))
                    throw new ArgumentException("Target player is required.");
                if (!Enum.IsDefined(typeof(SudokuDifficultyLevel), request.Difficulty) ||
                    !Enum.IsDefined(typeof(MatchDurationMinutes), request.Duration))
                    throw new ArgumentException("Challenge settings are invalid.");
                if (request.TargetPlayerId == challenger.PlayerId)
                    throw new ArgumentException("You cannot challenge yourself.");
                ClientSession target = _sessions.GetOnline(request.TargetPlayerId);
                if (target == null) throw new InvalidOperationException("Target player is offline.");
                if (!IsAvailable(challenger.PlayerId) || !IsAvailable(target.PlayerId))
                    throw new InvalidOperationException("Both players must be available in Lobby.");
                if (_challenges.Values.Any(challenge => challenge.Status == ChallengeStatus.Pending &&
                    challenge.ChallengerPlayerId == challenger.PlayerId &&
                    challenge.TargetPlayerId == target.PlayerId))
                    throw new InvalidOperationException("A challenge is already pending for this player.");

                var now = _clock.UtcNow;
                var created = new ChallengeDto
                {
                    ChallengeId = Guid.NewGuid(),
                    ChallengerPlayerId = challenger.PlayerId,
                    ChallengerName = challenger.PlayerName,
                    TargetPlayerId = target.PlayerId,
                    TargetName = target.PlayerName,
                    Difficulty = request.Difficulty,
                    Duration = request.Duration,
                    Status = ChallengeStatus.Pending,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddMinutes(2)
                };
                _challenges.Add(created.ChallengeId, created);
                return created;
            }
        }

        public ChallengeDto[] Accept(string targetPlayerId, Guid challengeId)
        {
            lock (SyncRoot)
            {
                ExpirePending();
                ChallengeDto challenge = GetPending(challengeId);
                if (challenge.TargetPlayerId != targetPlayerId)
                    throw new UnauthorizedAccessException("Only the intended receiver may accept.");
                if (!IsAvailable(challenge.ChallengerPlayerId) || !IsAvailable(challenge.TargetPlayerId))
                {
                    challenge.Status = ChallengeStatus.Invalidated;
                    throw new InvalidOperationException("A player is no longer available.");
                }
                ClientSession challenger = _sessions.GetOnline(challenge.ChallengerPlayerId);
                ClientSession target = _sessions.GetOnline(challenge.TargetPlayerId);
                Room room = _rooms.CreateRoom(
                    $"Duel - {challenger.PlayerName} vs {target.PlayerName}",
                    new Player(challenger.PlayerId, challenger.PlayerName), challenge.Difficulty);
                room.ChallengeDuration = challenge.Duration;
                if (!_rooms.JoinRoom(room.RoomId, new Player(target.PlayerId, target.PlayerName)))
                {
                    _rooms.RemoveRoom(room.RoomId);
                    throw new InvalidOperationException("Could not prepare the challenge room.");
                }
                challenge.RoomId = room.RoomId;
                challenge.Status = ChallengeStatus.Accepted;
                return new[] { challenge }.Concat(
                    InvalidateForPlayers(challenge.ChallengerPlayerId, challenge.TargetPlayerId)).ToArray();
            }
        }

        public ChallengeDto Decline(string targetPlayerId, Guid challengeId)
        {
            lock (SyncRoot)
            {
                ExpirePending();
                ChallengeDto challenge = GetPending(challengeId);
                if (challenge.TargetPlayerId != targetPlayerId)
                    throw new UnauthorizedAccessException("Only the intended receiver may decline.");
                challenge.Status = ChallengeStatus.Declined;
                return challenge;
            }
        }

        public ChallengeDto Cancel(string challengerPlayerId, Guid challengeId)
        {
            lock (SyncRoot)
            {
                ExpirePending();
                ChallengeDto challenge = GetPending(challengeId);
                if (challenge.ChallengerPlayerId != challengerPlayerId)
                    throw new UnauthorizedAccessException("Only the challenger may cancel.");
                challenge.Status = ChallengeStatus.Cancelled;
                return challenge;
            }
        }

        public ChallengeDto[] InvalidateForPlayers(params string[] playerIds)
        {
            lock (SyncRoot)
            {
                var set = new HashSet<string>(playerIds, StringComparer.Ordinal);
                ChallengeDto[] changed = _challenges.Values.Where(challenge =>
                    challenge.Status == ChallengeStatus.Pending &&
                    (set.Contains(challenge.ChallengerPlayerId) || set.Contains(challenge.TargetPlayerId)))
                    .ToArray();
                foreach (ChallengeDto challenge in changed)
                    challenge.Status = ChallengeStatus.Invalidated;
                return changed;
            }
        }

        private ChallengeDto GetPending(Guid challengeId)
        {
            ChallengeDto challenge;
            if (challengeId == Guid.Empty || !_challenges.TryGetValue(challengeId, out challenge) ||
                challenge.Status != ChallengeStatus.Pending)
                throw new InvalidOperationException("Challenge is not pending or does not exist.");
            return challenge;
        }

        private bool IsAvailable(string playerId)
        {
            if (_sessions.GetOnline(playerId) == null) return false;
            if (_matches.IsSpectating(playerId)) return false;
            if (_rooms.GetAllRooms().Any(room => room.Players.Any(player => player.PlayerId == playerId)))
                return false;
            return !_matches.GetActiveMatchSummaries(_clock.UtcNow).Any(match =>
                match.PlayerAId == playerId || match.PlayerBId == playerId);
        }

        private void ExpirePending()
        {
            foreach (ChallengeDto challenge in _challenges.Values)
                if (challenge.Status == ChallengeStatus.Pending && challenge.ExpiresAtUtc <= _clock.UtcNow)
                    challenge.Status = ChallengeStatus.Expired;
        }
    }
}
