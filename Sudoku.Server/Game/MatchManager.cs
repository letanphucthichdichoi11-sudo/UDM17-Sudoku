using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Sudoku.Server.Game
{
    internal sealed class MatchManager
    {
        private static readonly TimeSpan DisconnectGracePeriod = TimeSpan.FromMinutes(3);
        private readonly ConcurrentDictionary<Guid, Match> _matches = new ConcurrentDictionary<Guid, Match>();
        private readonly IMatchRepository _repository;

        public event EventHandler<MatchEventArgs> MatchStarted;
        public event EventHandler<MatchMoveEventArgs> PlayerProgressChanged;
        public event EventHandler<MatchMoveEventArgs> SpectatorBoardChanged;
        public event EventHandler<MatchEventArgs> PlayerConnectionChanged;
        public event EventHandler<MatchEventArgs> MatchFinished;
        public event EventHandler<MatchEventArgs> MatchAborted;
        public event EventHandler<MatchEventArgs> MatchArchived;

        public MatchManager() : this(new InMemoryMatchRepository()) { }
        public MatchManager(IMatchRepository repository) { _repository = repository ?? throw new ArgumentNullException("repository"); }

        public Match StartMatch(Guid roomId, Guid matchId, string playerAId, string playerBId, int[,] originalPuzzle, int[,] solutionGrid, TimeSpan timeLimit)
        {
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
                ServerStartTimestamp = DateTime.UtcNow,
                State = MatchState.Ongoing,
                ConnectionA = ConnectionStatus.Connected,
                ConnectionB = ConnectionStatus.Connected
            };
            match.BoardA = new PlayerBoardState(match.OriginalPuzzle);
            match.BoardB = new PlayerBoardState(match.OriginalPuzzle);
            if (!_matches.TryAdd(matchId, match)) throw new InvalidOperationException("A match with this matchId already exists.");
            _repository.SaveMatch(match);
            OnMatchStarted(match);
            return match;
        }

        public MoveResult SubmitMove(Guid matchId, string playerId, string moveId, int row, int col, int value)
        {
            Match match;
            if (!_matches.TryGetValue(matchId, out match)) return Reject(MoveErrorCode.MatchNotFound);
            if (String.IsNullOrWhiteSpace(moveId)) return Reject(MoveErrorCode.InvalidValue);
            MoveResult result;
            lock (match.SyncRoot)
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
                _repository.SaveMove(matchId, playerId, moveId, result, DateTime.UtcNow);
                _repository.SaveMatch(match);
            }
            if (result.Accepted || result.ErrorCode == MoveErrorCode.IncorrectValue) OnPlayerProgressChanged(match, playerId, result);
            if (result.BoardChanged) OnSpectatorBoardChanged(match, playerId, result);
            if (match.Result != null && match.Result.Reason == MatchFinishReason.Completed) ArchiveFinishedMatch(match);
            return result;
        }

        public SpectatorMatchSnapshot JoinSpectator(Guid matchId, string spectatorId)
        {
            Match match = GetRequiredMatch(matchId);
            lock (match.SyncRoot)
            {
                if (match.State != MatchState.Ongoing) throw new InvalidOperationException("Match is not ongoing.");
                if (String.IsNullOrWhiteSpace(spectatorId) || match.IsPlayer(spectatorId)) throw new ArgumentException("Spectator id is not valid.");
                match.SpectatorIds.Add(spectatorId);
                return CreateSpectatorSnapshot(match, DateTime.UtcNow);
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
            Match match = GetRequiredMatch(matchId);
            lock (match.SyncRoot)
            {
                if (!match.IsPlayer(playerId)) throw new UnauthorizedAccessException();
                var own = match.GetBoard(playerId);
                var opponent = match.GetBoard(match.GetOpponent(playerId));
                return new PlayerMatchSnapshot { MatchId = matchId, OriginalPuzzle = MatchGrid.Clone(match.OriginalPuzzle), OwnBoard = MatchGrid.Clone(own.CurrentValues), OwnCorrectCount = own.CorrectCount, OwnErrorCount = own.ErrorCount, OpponentCorrectCount = opponent.CorrectCount, OpponentErrorCount = opponent.ErrorCount, TimeLeft = GetTimeLeft(match, DateTime.UtcNow) };
            }
        }

        public IList<ActiveMatchSummary> GetActiveMatchSummaries(DateTime nowUtc)
        {
            var summaries = new List<ActiveMatchSummary>();
            foreach (var match in _matches.Values)
            {
                lock (match.SyncRoot)
                {
                    if (match.State != MatchState.Ongoing) continue;
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
                        TimeLeft = GetTimeLeft(match, nowUtc)
                    });
                }
            }
            return summaries;
        }

        public void HandleDisconnect(Guid matchId, string playerId, DateTime nowUtc)
        {
            Match match = GetRequiredMatch(matchId);
            lock (match.SyncRoot)
            {
                if (match.State != MatchState.Ongoing || !match.IsPlayer(playerId)) return;
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
                if (match.State != MatchState.Ongoing || !match.IsPlayer(playerId)) return;
                SetConnection(match, playerId, ConnectionStatus.Connected, nowUtc);
                _repository.SaveMatch(match);
            }
            OnPlayerConnectionChanged(match);
        }

        public void ProcessDueTimers(DateTime nowUtc)
        {
            foreach (var match in _matches.Values.Where(m => m.State == MatchState.Ongoing).ToList())
            {
                var finalized = false;
                lock (match.SyncRoot)
                {
                    if (match.State != MatchState.Ongoing) continue;
                    if (nowUtc - match.ServerStartTimestamp >= match.TimeLimit)
                    {
                        Finish(match, DetermineWinner(match), MatchFinishReason.TimeUp, nowUtc);
                        finalized = true;
                    }
                    else if (HasExceededGrace(match.ConnectionA, match.DisconnectedAtA, nowUtc) && HasExceededGrace(match.ConnectionB, match.DisconnectedAtB, nowUtc))
                    {
                        Abort(match, MatchFinishReason.BothDisconnected, nowUtc);
                        finalized = true;
                    }
                    else if (HasExceededGrace(match.ConnectionA, match.DisconnectedAtA, nowUtc))
                    {
                        Finish(match, match.PlayerBId, MatchFinishReason.TechnicalWinDisconnect, nowUtc);
                        finalized = true;
                    }
                    else if (HasExceededGrace(match.ConnectionB, match.DisconnectedAtB, nowUtc))
                    {
                        Finish(match, match.PlayerAId, MatchFinishReason.TechnicalWinDisconnect, nowUtc);
                        finalized = true;
                    }
                    if (finalized) _repository.SaveMatch(match);
                }
                if (finalized) ArchiveFinishedMatch(match);
            }
        }

        private static MoveResult ApplyMove(Match match, PlayerBoardState board, int row, int col, int value)
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
            if (board.CorrectCount == board.TotalEmptyCells) Finish(match, match.PlayerAId == null ? null : (ReferenceEquals(board, match.BoardA) ? match.PlayerAId : match.PlayerBId), MatchFinishReason.Completed, DateTime.UtcNow);
            return result;
        }

        private static MoveResult Success(PlayerBoardState board, int row, int col, int value, bool changed)
        {
            return new MoveResult { Accepted = true, IsCorrect = true, ErrorCode = MoveErrorCode.None, CorrectCount = board.CorrectCount, ErrorCount = board.ErrorCount, BoardChanged = changed, Row = row, Column = col, Value = value };
        }

        private static MoveResult Reject(MoveErrorCode error) { return new MoveResult { ErrorCode = error }; }
        private static bool HasExceededGrace(ConnectionStatus status, DateTime? disconnectedAt, DateTime now) { return status == ConnectionStatus.Disconnected && disconnectedAt.HasValue && now - disconnectedAt.Value >= DisconnectGracePeriod; }
        private static TimeSpan GetTimeLeft(Match match, DateTime now) { var left = match.TimeLimit - (now - match.ServerStartTimestamp); return left < TimeSpan.Zero ? TimeSpan.Zero : left; }
        private static string DetermineWinner(Match match) { if (match.BoardA.CorrectCount != match.BoardB.CorrectCount) return match.BoardA.CorrectCount > match.BoardB.CorrectCount ? match.PlayerAId : match.PlayerBId; if (match.BoardA.ErrorCount != match.BoardB.ErrorCount) return match.BoardA.ErrorCount < match.BoardB.ErrorCount ? match.PlayerAId : match.PlayerBId; return null; }
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
        private static SpectatorMatchSnapshot CreateSpectatorSnapshot(Match match, DateTime now) { return new SpectatorMatchSnapshot { MatchId = match.MatchId, OriginalPuzzle = MatchGrid.Clone(match.OriginalPuzzle), BoardA = MatchGrid.Clone(match.BoardA.CurrentValues), BoardB = MatchGrid.Clone(match.BoardB.CurrentValues), CorrectCountA = match.BoardA.CorrectCount, CorrectCountB = match.BoardB.CorrectCount, ErrorCountA = match.BoardA.ErrorCount, ErrorCountB = match.BoardB.ErrorCount, TimeLeft = GetTimeLeft(match, now) }; }
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

            ValidateGrid(puzzle, "puzzle");
            ValidateGrid(solution, "solution");

            bool hasEmptyCell = false;
            for (int row = 0; row < 9; row++)
            for (int col = 0; col < 9; col++)
            {
                int puzzleValue = puzzle[row, col];
                int solutionValue = solution[row, col];

                if (puzzleValue < 0 || puzzleValue > 9 ||
                    solutionValue < 1 || solutionValue > 9)
                {
                    throw new ArgumentException(
                        "Puzzle or solution contains an invalid value.");
                }

                if (puzzleValue == 0)
                {
                    hasEmptyCell = true;
                }
                else if (puzzleValue != solutionValue)
                {
                    throw new ArgumentException(
                        "Puzzle clues must match the solution.");
                }
            }

            if (!hasEmptyCell)
                throw new ArgumentException("Puzzle must contain an empty cell.");

            if (!IsValidCompleteGrid(solution))
                throw new ArgumentException("Solution grid is invalid.");
        }

        private static void ValidateGrid(int[,] grid, string parameterName)
        {
            if (grid == null ||
                grid.GetLength(0) != 9 ||
                grid.GetLength(1) != 9)
            {
                throw new ArgumentException(
                    "Sudoku grid must be a 9x9 matrix.",
                    parameterName);
            }
        }

        private static bool IsValidCompleteGrid(int[,] grid)
        {
            for (int index = 0; index < 9; index++)
            {
                var rowValues = new bool[10];
                var columnValues = new bool[10];

                for (int offset = 0; offset < 9; offset++)
                {
                    int rowValue = grid[index, offset];
                    int columnValue = grid[offset, index];

                    if (rowValues[rowValue] || columnValues[columnValue])
                        return false;

                    rowValues[rowValue] = true;
                    columnValues[columnValue] = true;
                }
            }

            for (int boxRow = 0; boxRow < 3; boxRow++)
            for (int boxCol = 0; boxCol < 3; boxCol++)
            {
                var boxValues = new bool[10];
                for (int rowOffset = 0; rowOffset < 3; rowOffset++)
                for (int colOffset = 0; colOffset < 3; colOffset++)
                {
                    int value = grid[
                        boxRow * 3 + rowOffset,
                        boxCol * 3 + colOffset];

                    if (boxValues[value])
                        return false;

                    boxValues[value] = true;
                }
            }

            return true;
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
