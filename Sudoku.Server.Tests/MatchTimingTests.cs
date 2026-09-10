using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sudoku.Server.Game;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Tests
{
    [TestClass]
    public class MatchTimingTests
    {
        [DataTestMethod]
        [DataRow(5)]
        [DataRow(10)]
        [DataRow(15)]
        public void StartMatch_AcceptsConfiguredDurations(int minutes)
        {
            TestContext context = CreateMatch((MatchDurationMinutes)minutes);

            Assert.AreEqual(MatchState.Preparing, context.Match.State);
            Assert.AreEqual(TimeSpan.FromMinutes(minutes), context.Match.TimeLimit);
            Assert.IsNull(context.Match.StartedAtUtc);
            Assert.IsNull(context.Match.EndsAtUtc);
        }

        [TestMethod]
        public void StartMatch_RejectsDurationOutsideConfiguredPresets()
        {
            GeneratedSudoku sudoku = GenerateSudoku();
            var manager = new MatchManager();

            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                manager.StartMatch(
                    Guid.NewGuid(), Guid.NewGuid(), "a", "b",
                    sudoku.Puzzle, sudoku.Solution,
                    TimeSpan.FromMinutes(7)));
        }

        [TestMethod]
        public void Ready_StartsOnlyAfterBothPlayersAndIsIdempotent()
        {
            TestContext context = CreateMatch(MatchDurationMinutes.Five);
            int startedEvents = 0;
            context.Manager.MatchStarted += (sender, args) => startedEvents++;

            Assert.IsFalse(context.Manager.MarkPlayerReady(context.Match.MatchId, "a"));
            Assert.AreEqual(MatchState.Preparing, context.Match.State);
            Assert.IsNull(context.Match.StartedAtUtc);

            Assert.IsTrue(context.Manager.MarkPlayerReady(context.Match.MatchId, "b"));
            Assert.AreEqual(MatchState.Ongoing, context.Match.State);
            Assert.AreEqual(context.Clock.UtcNow, context.Match.StartedAtUtc);
            Assert.AreEqual(context.Clock.UtcNow.AddMinutes(5), context.Match.EndsAtUtc);
            Assert.AreEqual(1, startedEvents);

            Assert.IsFalse(context.Manager.MarkPlayerReady(context.Match.MatchId, "a"));
            Assert.AreEqual(1, startedEvents);
        }

        [TestMethod]
        public void Ready_FromNonPlayerIsRejected()
        {
            TestContext context = CreateMatch(MatchDurationMinutes.Five);
            Assert.ThrowsException<UnauthorizedAccessException>(() =>
                context.Manager.MarkPlayerReady(context.Match.MatchId, "spectator"));
        }

        [TestMethod]
        public void SubmitMove_BeforeReadyIsRejected()
        {
            TestContext context = CreateMatch(MatchDurationMinutes.Five);
            int row;
            int column;
            FindEmptyCell(context.Match.OriginalPuzzle, out row, out column);

            MoveResult result = context.Manager.SubmitMove(
                context.Match.MatchId, "a", "before-ready",
                row, column, context.Match.SolutionGrid[row, column]);

            Assert.AreEqual(MoveErrorCode.MatchNotOngoing, result.ErrorCode);
        }

        [TestMethod]
        public void PreparingTimeout_AtSixtySecondsAbortsAndPublishesOnce()
        {
            TestContext context = CreateMatch(MatchDurationMinutes.Five);
            int abortedEvents = 0;
            int archivedEvents = 0;
            context.Manager.MatchAborted += (sender, args) => abortedEvents++;
            context.Manager.MatchArchived += (sender, args) => archivedEvents++;

            Assert.AreEqual(context.Clock.UtcNow.AddSeconds(60), context.Match.PreparingEndsAtUtc);
            context.Manager.ProcessDueTimers(context.Match.PreparingEndsAtUtc.AddTicks(-1));
            Assert.AreEqual(MatchState.Preparing, context.Match.State);

            context.Manager.ProcessDueTimers(context.Match.PreparingEndsAtUtc);
            context.Manager.ProcessDueTimers(context.Match.PreparingEndsAtUtc.AddSeconds(1));

            Assert.AreEqual(MatchState.Archived, context.Match.State);
            Assert.AreEqual(MatchFinishReason.PreparingTimeout, context.Match.Result.Reason);
            Assert.AreEqual(1, abortedEvents);
            Assert.AreEqual(1, archivedEvents);
        }

        [TestMethod]
        public void ReadyAtPreparingDeadline_DoesNotStartExpiredMatch()
        {
            TestContext context = CreateMatch(MatchDurationMinutes.Five);
            context.Manager.MarkPlayerReady(context.Match.MatchId, "a");
            context.Clock.UtcNow = context.Match.PreparingEndsAtUtc;

            Assert.IsFalse(context.Manager.MarkPlayerReady(context.Match.MatchId, "b"));
            Assert.AreEqual(MatchState.Archived, context.Match.State);
            Assert.AreEqual(MatchFinishReason.PreparingTimeout, context.Match.Result.Reason);
        }

        [TestMethod]
        public void PreparingReconnect_DoesNotExtendDeadline()
        {
            TestContext context = CreateMatch(MatchDurationMinutes.Five);
            DateTime originalDeadline = context.Match.PreparingEndsAtUtc;
            context.Manager.HandleDisconnect(context.Match.MatchId, "a", context.Clock.UtcNow.AddSeconds(10));
            context.Manager.HandleReconnect(context.Match.MatchId, "a", context.Clock.UtcNow.AddSeconds(20));

            Assert.AreEqual(ConnectionStatus.Connected, context.Match.ConnectionA);
            Assert.AreEqual(originalDeadline, context.Match.PreparingEndsAtUtc);
            context.Manager.ProcessDueTimers(originalDeadline);
            Assert.AreEqual(MatchFinishReason.PreparingTimeout, context.Match.Result.Reason);
        }

        [TestMethod]
        public void PlayerStatus_ContainsSeparateCurrentBoardsForBothPlayers()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Five);
            int row;
            int column;
            FindEmptyCell(context.Match.OriginalPuzzle, out row, out column);
            context.Manager.SubmitMove(context.Match.MatchId, "a", "restore-move",
                row, column, context.Match.SolutionGrid[row, column]);

            MatchStatusResponse playerA = context.Manager.GetPlayerStatus(context.Match.MatchId, "a");
            MatchStatusResponse playerB = context.Manager.GetPlayerStatus(context.Match.MatchId, "b");

            Assert.AreEqual(context.Match.SolutionGrid[row, column], playerA.OwnBoard[row * 9 + column]);
            Assert.AreEqual(0, playerB.OwnBoard[row * 9 + column]);
            Assert.AreEqual(0, playerA.OpponentBoard[row * 9 + column]);
            Assert.AreEqual(context.Match.SolutionGrid[row, column], playerB.OpponentBoard[row * 9 + column]);
        }

        [TestMethod]
        public void Deadline_AtExactEndFinishesMatchAndRejectsMove()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Five);
            int finishedEvents = 0;
            context.Manager.MatchFinished += (sender, args) => finishedEvents++;
            int row;
            int column;
            FindEmptyCell(context.Match.OriginalPuzzle, out row, out column);

            context.Clock.UtcNow = context.Match.EndsAtUtc.Value.AddTicks(-1);
            MoveResult beforeDeadline = context.Manager.SubmitMove(
                context.Match.MatchId, "a", "before-deadline",
                row, column, context.Match.SolutionGrid[row, column]);
            Assert.IsTrue(beforeDeadline.Accepted);

            context.Clock.UtcNow = context.Match.EndsAtUtc.Value;
            MoveResult atDeadline = context.Manager.SubmitMove(
                context.Match.MatchId, "b", "at-deadline",
                row, column, context.Match.SolutionGrid[row, column]);

            Assert.AreEqual(MoveErrorCode.MatchExpired, atDeadline.ErrorCode);
            Assert.AreEqual(MatchState.Archived, context.Match.State);
            Assert.AreEqual(MatchFinishReason.TimeUp, context.Match.Result.Reason);
            Assert.AreEqual("a", context.Match.Result.WinnerPlayerId);
            Assert.AreEqual(1, finishedEvents);
        }

        [TestMethod]
        public void TimeUp_UsesErrorsAsTieBreakerThenAllowsDraw()
        {
            TestContext errorContext = CreateStartedMatch(MatchDurationMinutes.Five);
            errorContext.Match.BoardA.ErrorCount = 2;
            errorContext.Match.BoardB.ErrorCount = 1;
            errorContext.Clock.UtcNow = errorContext.Match.EndsAtUtc.Value;
            errorContext.Manager.ProcessDueTimers(errorContext.Clock.UtcNow);
            Assert.AreEqual("b", errorContext.Match.Result.WinnerPlayerId);

            TestContext drawContext = CreateStartedMatch(MatchDurationMinutes.Five);
            drawContext.Clock.UtcNow = drawContext.Match.EndsAtUtc.Value;
            drawContext.Manager.ProcessDueTimers(drawContext.Clock.UtcNow);
            Assert.IsNull(drawContext.Match.Result.WinnerPlayerId);
        }

        [TestMethod]
        public void CompletedMatch_ReturnsSameWinnerAndFrozenTimeToBothPlayers()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Five);
            context.Clock.UtcNow = context.Match.StartedAtUtc.Value
                .AddMinutes(4)
                .AddSeconds(32);

            int moveNumber = 0;
            for (int row = 0; row < 9; row++)
            for (int column = 0; column < 9; column++)
            {
                if (context.Match.OriginalPuzzle[row, column] != 0)
                    continue;

                MoveResult move = context.Manager.SubmitMove(
                    context.Match.MatchId,
                    "a",
                    "complete-" + moveNumber++,
                    row,
                    column,
                    context.Match.SolutionGrid[row, column]);
                Assert.IsTrue(move.Accepted);
            }

            MatchStatusResponse playerA = context.Manager.GetPlayerStatus(
                context.Match.MatchId, "a");
            MatchStatusResponse playerB = context.Manager.GetPlayerStatus(
                context.Match.MatchId, "b");

            Assert.AreEqual(MatchLifecycleState.Archived, playerA.State);
            Assert.AreEqual("a", playerA.WinnerPlayerId);
            Assert.AreEqual(playerA.WinnerPlayerId, playerB.WinnerPlayerId);
            Assert.AreEqual(context.Clock.UtcNow, playerA.FinishedAtUtc);
            Assert.AreEqual(playerA.FinishedAtUtc, playerB.FinishedAtUtc);
            Assert.AreEqual(
                TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(32)),
                playerA.FinishedAtUtc.Value - playerA.StartedAtUtc.Value);
        }

        [TestMethod]
        public void Disconnect_DoesNotPauseClockAndDeadlineWinsWhenEarlier()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Five);
            DateTime disconnectedAt = context.Clock.UtcNow.AddMinutes(4);
            context.Manager.HandleDisconnect(context.Match.MatchId, "a", disconnectedAt);

            context.Clock.UtcNow = context.Match.EndsAtUtc.Value;
            context.Manager.ProcessDueTimers(context.Clock.UtcNow);

            Assert.AreEqual(MatchFinishReason.TimeUp, context.Match.Result.Reason);
        }

        [TestMethod]
        public void DisconnectGracePeriod_RemainsThreeMinutes()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Fifteen);
            DateTime disconnectedAt = context.Clock.UtcNow;
            context.Manager.HandleDisconnect(context.Match.MatchId, "a", disconnectedAt);

            context.Manager.ProcessDueTimers(disconnectedAt.AddMinutes(3).AddTicks(-1));
            Assert.AreEqual(MatchState.Ongoing, context.Match.State);

            context.Manager.ProcessDueTimers(disconnectedAt.AddMinutes(3));
            Assert.AreEqual(MatchFinishReason.TechnicalWinDisconnect, context.Match.Result.Reason);
            Assert.AreEqual("b", context.Match.Result.WinnerPlayerId);
        }

        [TestMethod]
        public void DisconnectGraceDeadline_WinsWhenItOccursBeforeMatchDeadline()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Fifteen);
            DateTime disconnectedAt = context.Clock.UtcNow;
            context.Manager.HandleDisconnect(context.Match.MatchId, "a", disconnectedAt);

            context.Clock.UtcNow = context.Match.EndsAtUtc.Value.AddMinutes(1);
            context.Manager.ProcessDueTimers(context.Clock.UtcNow);

            Assert.AreEqual(
                MatchFinishReason.TechnicalWinDisconnect,
                context.Match.Result.Reason);
            Assert.AreEqual("b", context.Match.Result.WinnerPlayerId);
        }

        [TestMethod]
        public void BothPlayersDisconnectedAtSameTime_AbortsAfterThreeMinutes()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Fifteen);
            DateTime disconnectedAt = context.Clock.UtcNow;
            context.Manager.HandleDisconnect(context.Match.MatchId, "a", disconnectedAt);
            context.Manager.HandleDisconnect(context.Match.MatchId, "b", disconnectedAt);

            context.Manager.ProcessDueTimers(disconnectedAt.AddMinutes(3));

            Assert.AreEqual(MatchFinishReason.BothDisconnected, context.Match.Result.Reason);
            Assert.AreEqual(MatchState.Archived, context.Match.State);
        }

        [TestMethod]
        public async Task ConcurrentTimerChecks_FinishAndPublishOnlyOnce()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Five);
            context.Clock.UtcNow = context.Match.EndsAtUtc.Value;
            int finishedEvents = 0;
            int archivedEvents = 0;
            context.Manager.MatchFinished += (sender, args) => Interlocked.Increment(ref finishedEvents);
            context.Manager.MatchArchived += (sender, args) => Interlocked.Increment(ref archivedEvents);

            Task[] checks = Enumerable.Range(0, 8)
                .Select(index => Task.Run(() =>
                    context.Manager.ProcessDueTimers(context.Clock.UtcNow)))
                .ToArray();
            await Task.WhenAll(checks);

            Assert.AreEqual(1, finishedEvents);
            Assert.AreEqual(1, archivedEvents);
        }

        [TestMethod]
        public void Snapshot_ReturnsServerTimeDeadlineAndTimeLeft()
        {
            TestContext context = CreateStartedMatch(MatchDurationMinutes.Ten);
            context.Clock.UtcNow = context.Clock.UtcNow.AddMinutes(2);

            PlayerMatchSnapshot snapshot = context.Manager.GetPlayerSnapshot(
                context.Match.MatchId, "a");

            Assert.AreEqual(context.Clock.UtcNow, snapshot.ServerUtcNow);
            Assert.AreEqual(context.Match.StartedAtUtc, snapshot.StartedAtUtc);
            Assert.AreEqual(context.Match.EndsAtUtc, snapshot.EndsAtUtc);
            Assert.AreEqual(TimeSpan.FromMinutes(8), snapshot.TimeLeft);

            MatchStatusResponse response = context.Manager.GetPlayerStatus(
                context.Match.MatchId, "a");
            Assert.AreEqual(MatchLifecycleState.Ongoing, response.State);
            Assert.AreEqual(MatchDurationMinutes.Ten, response.Duration);
            Assert.AreEqual(TimeSpan.FromMinutes(8), response.TimeLeft);
            Assert.IsNull(typeof(MatchStatusResponse).GetProperty("Solution"));
            Assert.IsNull(typeof(MatchStatusResponse).GetProperty("SolutionGrid"));
        }

        private static TestContext CreateStartedMatch(MatchDurationMinutes duration)
        {
            TestContext context = CreateMatch(duration);
            context.Manager.MarkPlayerReady(context.Match.MatchId, "a");
            context.Manager.MarkPlayerReady(context.Match.MatchId, "b");
            return context;
        }

        private static TestContext CreateMatch(MatchDurationMinutes duration)
        {
            GeneratedSudoku sudoku = GenerateSudoku();
            var clock = new FakeClock { UtcNow = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc) };
            var manager = new MatchManager(new InMemoryMatchRepository(), clock);
            Match match = manager.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "a", "b",
                sudoku.Puzzle, sudoku.Solution, duration);
            return new TestContext(manager, match, clock);
        }

        private static GeneratedSudoku GenerateSudoku()
        {
            return new SudokuGenerator().GeneratePuzzle(SudokuDifficulty.Easy, 321);
        }

        private static void FindEmptyCell(int[,] puzzle, out int row, out int column)
        {
            for (row = 0; row < 9; row++)
            for (column = 0; column < 9; column++)
                if (puzzle[row, column] == 0) return;
            throw new AssertFailedException("Puzzle has no empty cell.");
        }

        private sealed class FakeClock : IClock
        {
            public DateTime UtcNow { get; set; }
        }

        private sealed class TestContext
        {
            public MatchManager Manager { get; private set; }
            public Match Match { get; private set; }
            public FakeClock Clock { get; private set; }

            public TestContext(MatchManager manager, Match match, FakeClock clock)
            {
                Manager = manager;
                Match = match;
                Clock = clock;
            }
        }
    }
}
