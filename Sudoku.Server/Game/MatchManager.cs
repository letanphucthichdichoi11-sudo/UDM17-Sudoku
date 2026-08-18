using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Game
{
    internal sealed class MatchManager : IMatchManager
    {
        private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan PreparingTimeout = TimeSpan.FromSeconds(60);
        private readonly ConcurrentDictionary<Guid, Match> _matches = new ConcurrentDictionary<Guid, Match>();
        private readonly IMatchRepository _repository;
        private readonly IClock _clock;

        public event EventHandler<MatchEventArgs> MatchStarted;
        public event EventHandler<MatchMoveEventArgs> PlayerProgressChanged;
        public event EventHandler<MatchMoveEventArgs> SpectatorBoardChanged;
        public event EventHandler<MatchEventArgs> PlayerConnectionChanged;
        public event EventHandler<MatchEventArgs> MatchFinished;
        public event EventHandler<MatchEventArgs> MatchAborted;
        public event EventHandler<MatchEventArgs> MatchArchived;

        public MatchManager() : this(new InMemoryMatchRepository(), new SystemClock()) { }
        public MatchManager(IMatchRepository repository) : this(repository, new SystemClock()) { }
        public MatchManager(IMatchRepository repository, IClock clock)
        {
            _repository = repository ?? throw new ArgumentNullException("repository");
            _clock = clock ?? throw new ArgumentNullException("clock");
        }

        public Match StartMatch(Guid roomId, Guid matchId, string playerAId, string playerBId, int[,] originalPuzzle, int[,] solutionGrid, MatchDurationMinutes duration)
        {
            TimeSpan timeLimit = ToTimeLimit(duration);
            ValidateStartArguments(roomId, matchId, playerAId, playerBId, originalPuzzle, solutionGrid, timeLimit);
            var match = new Match
            {
                RoomId = roomId,
                MatchId = matchId,
                PlayerAId = playerAId,
                PlayerBId = playerBId,
                OriginalPuzzle = MatchGrid.Clone(originalPuzzle),
                SolutionGrid = MatchGrid.Clone(solutionGrid),
                TimeLimit = timeLimit,
                Duration = duration,
                PreparingEndsAtUtc = _clock.UtcNow.Add(PreparingTimeout),
                State = MatchState.Preparing,
                ConnectionA = ConnectionStatus.Connected,
                ConnectionB = ConnectionStatus.Connected
            };
            match.BoardA = new PlayerBoardState(match.OriginalPuzzle);
            match.BoardB = new PlayerBoardState(match.OriginalPuzzle);
            if (!_matches.TryAdd(matchId, match)) throw new InvalidOperationException("A match with this matchId already exists.");
            _repository.SaveMatch(match);
            return match;
        }

        public Match StartMatch(Guid roomId, Guid matchId, string playerAId, string playerBId, int[,] originalPuzzle, int[,] solutionGrid, TimeSpan timeLimit)
        {
            return StartMatch(roomId, matchId, playerAId, playerBId,
                originalPuzzle, solutionGrid, ToDuration(timeLimit));
        }

        public bool MarkPlayerReady(Guid matchId, string playerId)
        {
            ProcessDueTimers(_clock.UtcNow);
            Match match = GetRequiredMatch(matchId);
            bool started = false;
            lock (match.SyncRoot)
            {
                if (!match.IsPlayer(playerId))
                    throw new UnauthorizedAccessException("Player does not belong to this match.");
                if (match.State != MatchState.Preparing)
                    return false;

                if (playerId == match.PlayerAId) match.PlayerAReady = true;
                else match.PlayerBReady = true;

                if (match.PlayerAReady && match.PlayerBReady)
                {
                    DateTime now = _clock.UtcNow;
                    match.StartedAtUtc = now;
                    match.EndsAtUtc = now.Add(match.TimeLimit);
                    match.State = MatchState.Ongoing;
                    started = true;
                }
                _repository.SaveMatch(match);
            }
            if (started) OnMatchStarted(match);
            return started;
        }

        public MoveResult SubmitMove(Guid matchId, string playerId, string moveId, int row, int col, int value)
        {
            Match match;
            if (!_matches.TryGetValue(matchId, out match)) return Reject(MoveErrorCode.MatchNotFound);
            if (String.IsNullOrWhiteSpace(moveId)) return Reject(MoveErrorCode.InvalidValue);
            MoveResult result;
            bool expired = false;
            lock (match.SyncRoot)
            {
                if (TryFinishExpired(match, _clock.UtcNow))
                {
                    _repository.SaveMatch(match);
                    result = Reject(MoveErrorCode.MatchExpired);
                    expired = true;
                }
                else
                {
                if (match.State != MatchState.Ongoing) return Reject(MoveErrorCode.MatchNotOngoing);
                if (!match.IsPlayer(playerId)) return Reject(MoveErrorCode.NotAPlayer);
                if (match.ProcessedMoves.TryGetValue(moveId, out result)) return result;
                if (row < 0 || row > 8 || col < 0 || col > 8) result = Reject(MoveErrorCode.OutOfRange);
                else if (value < 0 || value > 9) result = Reject(MoveErrorCode.InvalidValue);
                else
                {
                    var board = match.GetBoard(playerId);
                    if (board.IsGivenCell[row, col]) result = Reject(MoveErrorCode.GivenCellLocked);
                    else result = ApplyMove(match, board, row, col, value);
                }
                match.ProcessedMoves[moveId] = result;
                _repository.SaveMove(matchId, playerId, moveId, result, _clock.UtcNow);
                _repository.SaveMatch(match);
                }
            }
            if (expired) { ArchiveFinishedMatch(match); return result; }
            if (result.Accepted || result.ErrorCode == MoveErrorCode.IncorrectValue) OnPlayerProgressChanged(match, playerId, result);
            if (result.BoardChanged) OnSpectatorBoardChanged(match, playerId, result);
            if (match.Result != null && match.Result.Reason == MatchFinishReason.Completed) ArchiveFinishedMatch(match);
            return result;
        }

        public SpectatorMatchSnapshot JoinSpectator(Guid matchId, string spectatorId)
        {
            ProcessDueTimers(_clock.UtcNow);
            Match match = GetRequiredMatch(matchId);
            lock (match.SyncRoot)
            {
                if (match.State != MatchState.Ongoing) throw new InvalidOperationException("Match is not ongoing.");
                if (String.IsNullOrWhiteSpace(spectatorId) || match.IsPlayer(spectatorId)) throw new ArgumentException("Spectator id is not valid.");
                match.SpectatorIds.Add(spectatorId);
                return CreateSpectatorSnapshot(match, _clock.UtcNow);
            }
        }

        public void LeaveSpectator(Guid matchId, string spectatorId)
        {
            Match match;
            if (!_matches.TryGetValue(matchId, out match)) return;
            lock (match.SyncRoot) match.SpectatorIds.Remove(spectatorId);
        }

        public PlayerMatchSnapshot GetPlayerSnapshot(Guid matchId, string playerId)
        {
            ProcessDueTimers(_clock.UtcNow);
            Match match = GetRequiredMatch(matchId);
            lock (match.SyncRoot)
            {
                if (!match.IsPlayer(playerId)) throw new UnauthorizedAccessException();
                var own = match.GetBoard(playerId);
                var opponent = match.GetBoard(match.GetOpponent(playerId));
                DateTime now = _clock.UtcNow;
                return new PlayerMatchSnapshot { MatchId = matchId, OriginalPuzzle = MatchGrid.Clone(match.OriginalPuzzle), OwnBoard = MatchGrid.Clone(own.CurrentValues), OwnCorrectCount = own.CorrectCount, OwnErrorCount = own.ErrorCount, OpponentCorrectCount = opponent.CorrectCount, OpponentErrorCount = opponent.ErrorCount, TimeLeft = GetTimeLeft(match, now), ServerUtcNow = now, StartedAtUtc = match.StartedAtUtc, EndsAtUtc = match.EndsAtUtc, State = match.State };
            }
        }

        public MatchStatusResponse GetPlayerStatus(Guid matchId, string playerId)
        {
            PlayerMatchSnapshot snapshot = GetPlayerSnapshot(matchId, playerId);
            Match match = GetRequiredMatch(matchId);
            lock (match.SyncRoot)
            {
                return new MatchStatusResponse
                {
                    MatchId = match.MatchId,
                    RoomId = match.RoomId,
                    Puzzle = MatchGrid.Flatten(snapshot.OriginalPuzzle),
                    OwnBoard = MatchGrid.Flatten(snapshot.OwnBoard),
                    State = (MatchLifecycleState)match.State,
                    Duration = match.Duration,
                    PlayerAReady = match.PlayerAReady,
                    PlayerBReady = match.PlayerBReady,
                    ServerUtcNow = snapshot.ServerUtcNow,
                    PreparingEndsAtUtc = match.PreparingEndsAtUtc,
                    StartedAtUtc = snapshot.StartedAtUtc,
                    EndsAtUtc = snapshot.EndsAtUtc,
                    TimeLeft = snapshot.TimeLeft,
                    OwnCorrectCount = snapshot.OwnCorrectCount,
                    OwnErrorCount = snapshot.OwnErrorCount,
                    OpponentCorrectCount = snapshot.OpponentCorrectCount,
                    OpponentErrorCount = snapshot.OpponentErrorCount,
                    FinishReason = MapFinishReason(match.Result),
                    WinnerPlayerId = match.Result == null ? null : match.Result.WinnerPlayerId
                };
            }
        }

        public IList<ActiveMatchSummary> GetActiveMatchSummaries(DateTime nowUtc)
        {
            ProcessDueTimers(nowUtc);
            var summaries = new List<ActiveMatchSummary>();
            foreach (var match in _matches.Values)
            {
                lock (match.SyncRoot)
                {
                    if (match.State != MatchState.Preparing && match.State != MatchState.Ongoing) continue;
                    summaries.Add(new ActiveMatchSummary
                    {
                        RoomId = match.RoomId,
                        MatchId = match.MatchId,
                        PlayerAId = match.PlayerAId,
                        PlayerBId = match.PlayerBId,
                        CorrectCountA = match.BoardA.CorrectCount,
                        CorrectCountB = match.BoardB.CorrectCount,
                        ErrorCountA = match.BoardA.ErrorCount,
                        ErrorCountB = match.BoardB.ErrorCount,
                        SpectatorCount = match.SpectatorIds.Count,
                        TimeLeft = GetTimeLeft(match, nowUtc), ServerUtcNow = nowUtc, EndsAtUtc = match.EndsAtUtc
                    });
                }
            }
            return summaries;
        }

        public void HandleDisconnect(Guid matchId, string playerId, DateTime nowUtc)
        {
            ProcessDueTimers(nowUtc);
            Match match = GetRequiredMatch(matchId);
            lock (match.SyncRoot)
            {
                if ((match.State != MatchState.Preparing && match.State != MatchState.Ongoing) ||
                    !match.IsPlayer(playerId)) return;
                SetConnection(match, playerId, ConnectionStatus.Disconnected, nowUtc);
                _repository.SaveMatch(match);
            }
            OnPlayerConnectionChanged(match);
        }

        public void HandleReconnect(Guid matchId, string playerId, DateTime nowUtc)
        {
            // A reconnect that arrives after the grace deadline must not revive an expired match.
            ProcessDueTimers(nowUtc);
            Match match = GetRequiredMatch(matchId);
            lock (match.SyncRoot)
            {
                if ((match.State != MatchState.Preparing && match.State != MatchState.Ongoing) ||
                    !match.IsPlayer(playerId)) return;
                SetConnection(match, playerId, ConnectionStatus.Connected, nowUtc);
                _repository.SaveMatch(match);
            }
            OnPlayerConnectionChanged(match);
        }

        public void ProcessDueTimers(DateTime nowUtc)
        {
            foreach (var match in _matches.Values.Where(m =>
                m.State == MatchState.Preparing || m.State == MatchState.Ongoing).ToList())
            {
                var finalized = false;
                lock (match.SyncRoot)
                {
                    if (match.State == MatchState.Preparing)
                    {
                        if (nowUtc < match.PreparingEndsAtUtc) continue;
                        Abort(match, MatchFinishReason.PreparingTimeout, nowUtc);
                        finalized = true;
                        _repository.SaveMatch(match);
                    }
                    else if (match.State != MatchState.Ongoing) continue;
                    else
                    {
                    DateTime? disconnectDeadlineA = GetDisconnectDeadline(
                        match.ConnectionA, match.DisconnectedAtA);
                    DateTime? disconnectDeadlineB = GetDisconnectDeadline(
                        match.ConnectionB, match.DisconnectedAtB);
                    DateTime? earliestDisconnectDeadline = Min(
                        disconnectDeadlineA,
                        disconnectDeadlineB);

                    if (match.EndsAtUtc.HasValue &&
                        nowUtc >= match.EndsAtUtc.Value &&
                        (!earliestDisconnectDeadline.HasValue ||
                         match.EndsAtUtc.Value <= earliestDisconnectDeadline.Value))
                    {
                        Finish(match, DetermineWinner(match), MatchFinishReason.TimeUp, nowUtc);
                        finalized = true;
                    }
                    else if (disconnectDeadlineA.HasValue &&
                             disconnectDeadlineB.HasValue &&
                             disconnectDeadlineA.Value == disconnectDeadlineB.Value &&
                             nowUtc >= disconnectDeadlineA.Value &&
                             nowUtc >= disconnectDeadlineB.Value)
                    {
                        Abort(match, MatchFinishReason.BothDisconnected, nowUtc);
                        finalized = true;
                    }
                    else if (disconnectDeadlineA.HasValue &&
                             nowUtc >= disconnectDeadlineA.Value &&
                             (!disconnectDeadlineB.HasValue ||
                              disconnectDeadlineA.Value <= disconnectDeadlineB.Value))
                    {
                        Finish(match, match.PlayerBId, MatchFinishReason.TechnicalWinDisconnect, nowUtc);
                        finalized = true;
                    }
                    else if (disconnectDeadlineB.HasValue &&
                             nowUtc >= disconnectDeadlineB.Value)
                    {
                        Finish(match, match.PlayerAId, MatchFinishReason.TechnicalWinDisconnect, nowUtc);
                        finalized = true;
                    }
                    if (finalized) _repository.SaveMatch(match);
                    }
                }
                if (finalized) ArchiveFinishedMatch(match);
            }
        }

        private MoveResult ApplyMove(Match match, PlayerBoardState board, int row, int col, int value)
        {
            var oldValue = board.CurrentValues[row, col];
            var oldWasCorrect = oldValue != 0 && oldValue == match.SolutionGrid[row, col];
            if (value == 0)
            {
                board.CurrentValues[row, col] = 0;
                if (oldWasCorrect) board.CorrectCount--;
                return Success(board, row, col, value, true);
            }
            if (value != match.SolutionGrid[row, col])
            {
                board.ErrorCount++;
                return new MoveResult { Accepted = false, IsCorrect = false, ErrorCode = MoveErrorCode.IncorrectValue, CorrectCount = board.CorrectCount, ErrorCount = board.ErrorCount, Row = row, Column = col, Value = value };
            }
            board.CurrentValues[row, col] = value;
            if (!oldWasCorrect) board.CorrectCount++;
            var result = Success(board, row, col, value, true);
            if (board.CorrectCount == board.TotalEmptyCells) Finish(match, match.PlayerAId == null ? null : (ReferenceEquals(board, match.BoardA) ? match.PlayerAId : match.PlayerBId), MatchFinishReason.Completed, _clock.UtcNow);
            return result;
        }

        private static MoveResult Success(PlayerBoardState board, int row, int col, int value, bool changed)
        {
            return new MoveResult { Accepted = true, IsCorrect = true, ErrorCode = MoveErrorCode.None, CorrectCount = board.CorrectCount, ErrorCount = board.ErrorCount, BoardChanged = changed, Row = row, Column = col, Value = value };
        }

        private static MoveResult Reject(MoveErrorCode error) { return new MoveResult { ErrorCode = error }; }
        private static DateTime? GetDisconnectDeadline(ConnectionStatus status, DateTime? disconnectedAt) { return status == ConnectionStatus.Disconnected && disconnectedAt.HasValue ? disconnectedAt.Value.Add(DisconnectGracePeriod) : (DateTime?)null; }
        private static DateTime? Min(DateTime? first, DateTime? second) { if (!first.HasValue) return second; if (!second.HasValue) return first; return first.Value <= second.Value ? first : second; }
        private static TimeSpan GetTimeLeft(Match match, DateTime now) { if (!match.EndsAtUtc.HasValue) return match.TimeLimit; var left = match.EndsAtUtc.Value - now; return left < TimeSpan.Zero ? TimeSpan.Zero : left; }
        private static bool TryFinishExpired(Match match, DateTime now) { if (match.State != MatchState.Ongoing || !match.EndsAtUtc.HasValue || now < match.EndsAtUtc.Value) return false; Finish(match, DetermineWinner(match), MatchFinishReason.TimeUp, now); return true; }
        private static string DetermineWinner(Match match) { if (match.BoardA.CorrectCount != match.BoardB.CorrectCount) return match.BoardA.CorrectCount > match.BoardB.CorrectCount ? match.PlayerAId : match.PlayerBId; if (match.BoardA.ErrorCount != match.BoardB.ErrorCount) return match.BoardA.ErrorCount < match.BoardB.ErrorCount ? match.PlayerAId : match.PlayerBId; return null; }
        private static MatchFinishReasonCode? MapFinishReason(MatchResult result)
        {
            if (result == null) return null;
            switch (result.Reason)
            {
                case MatchFinishReason.Completed: return MatchFinishReasonCode.Completed;
                case MatchFinishReason.TimeUp: return MatchFinishReasonCode.TimeUp;
                case MatchFinishReason.TechnicalWinDisconnect: return MatchFinishReasonCode.TechnicalWinDisconnect;
                case MatchFinishReason.BothDisconnected: return MatchFinishReasonCode.BothDisconnected;
                case MatchFinishReason.PreparingTimeout: return MatchFinishReasonCode.PreparingTimeout;
                default: return null;
            }
        }
        private static void Finish(Match match, string winner, MatchFinishReason reason, DateTime now) { match.State = MatchState.Finished; match.Result = new MatchResult { WinnerPlayerId = winner, Reason = reason, FinishedAtUtc = now }; }
        private static void Abort(Match match, MatchFinishReason reason, DateTime now) { match.State = MatchState.Aborted; match.Result = new MatchResult { Reason = reason, FinishedAtUtc = now }; }
        private static void SetConnection(Match match, string playerId, ConnectionStatus status, DateTime now)
        {
            if (playerId == match.PlayerAId)
            {
                if (status == ConnectionStatus.Disconnected && match.ConnectionA == ConnectionStatus.Connected) match.DisconnectedAtA = now;
                if (status == ConnectionStatus.Connected) match.DisconnectedAtA = null;
                match.ConnectionA = status;
            }
            else
            {
                if (status == ConnectionStatus.Disconnected && match.ConnectionB == ConnectionStatus.Connected) match.DisconnectedAtB = now;
                if (status == ConnectionStatus.Connected) match.DisconnectedAtB = null;
                match.ConnectionB = status;
            }
        }
        private Match GetRequiredMatch(Guid id) { Match match; if (!_matches.TryGetValue(id, out match)) throw new KeyNotFoundException("Match not found."); return match; }
        private static SpectatorMatchSnapshot CreateSpectatorSnapshot(Match match, DateTime now) { return new SpectatorMatchSnapshot { MatchId = match.MatchId, OriginalPuzzle = MatchGrid.Clone(match.OriginalPuzzle), BoardA = MatchGrid.Clone(match.BoardA.CurrentValues), BoardB = MatchGrid.Clone(match.BoardB.CurrentValues), CorrectCountA = match.BoardA.CorrectCount, CorrectCountB = match.BoardB.CorrectCount, ErrorCountA = match.BoardA.ErrorCount, ErrorCountB = match.BoardB.ErrorCount, TimeLeft = GetTimeLeft(match, now), ServerUtcNow = now, StartedAtUtc = match.StartedAtUtc, EndsAtUtc = match.EndsAtUtc }; }
        private void ArchiveFinishedMatch(Match match) { lock (match.SyncRoot) { if (match.State == MatchState.Archived) return; var aborted = match.State == MatchState.Aborted; match.State = MatchState.Archived; _repository.SaveMatch(match); if (aborted) OnMatchAborted(match); else OnMatchFinished(match); } OnMatchArchived(match); }
        private static void ValidateStartArguments(
            Guid roomId,
            Guid matchId,
            string playerAId,
            string playerBId,
            int[,] puzzle,
            int[,] solution,
            TimeSpan timeLimit)
        {
            if (roomId == Guid.Empty ||
                matchId == Guid.Empty ||
                String.IsNullOrWhiteSpace(playerAId) ||
                String.IsNullOrWhiteSpace(playerBId) ||
                playerAId == playerBId ||
                timeLimit <= TimeSpan.Zero)
            {
                throw new ArgumentException("StartMatch arguments are invalid.");
            }

            SudokuGridValidator.ValidatePuzzleAndSolution(puzzle, solution);
        }
        private static TimeSpan ToTimeLimit(MatchDurationMinutes duration)
        {
            if (duration != MatchDurationMinutes.Five &&
                duration != MatchDurationMinutes.Ten &&
                duration != MatchDurationMinutes.Fifteen)
                throw new ArgumentOutOfRangeException("duration");
            return TimeSpan.FromMinutes((int)duration);
        }
        private static MatchDurationMinutes ToDuration(TimeSpan timeLimit)
        {
            if (timeLimit == TimeSpan.FromMinutes(5)) return MatchDurationMinutes.Five;
            if (timeLimit == TimeSpan.FromMinutes(10)) return MatchDurationMinutes.Ten;
            if (timeLimit == TimeSpan.FromMinutes(15)) return MatchDurationMinutes.Fifteen;
            throw new ArgumentOutOfRangeException("timeLimit", "Match duration must be 5, 10 or 15 minutes.");
        }
        private void OnMatchStarted(Match m) { var h = MatchStarted; if (h != null) h(this, new MatchEventArgs(m)); }
        private void OnPlayerProgressChanged(Match m, string p, MoveResult r) { var h = PlayerProgressChanged; if (h != null) h(this, new MatchMoveEventArgs(m, p, r)); }
        private void OnSpectatorBoardChanged(Match m, string p, MoveResult r) { var h = SpectatorBoardChanged; if (h != null) h(this, new MatchMoveEventArgs(m, p, r)); }
        private void OnPlayerConnectionChanged(Match m) { var h = PlayerConnectionChanged; if (h != null) h(this, new MatchEventArgs(m)); }
        private void OnMatchFinished(Match m) { var h = MatchFinished; if (h != null) h(this, new MatchEventArgs(m)); }
        private void OnMatchAborted(Match m) { var h = MatchAborted; if (h != null) h(this, new MatchEventArgs(m)); }
        private void OnMatchArchived(Match m) { var h = MatchArchived; if (h != null) h(this, new MatchEventArgs(m)); }
    }
}
