using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sudoku.Server.Game;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Tests
{
    [TestClass]
    public class MatchCoordinatorTests
    {
        [TestMethod]
        public void StartMatch_WithReadyRoom_CreatesMatchWithGeneratedPuzzle()
        {
            RoomManager rooms;
            MatchManager matches;
            MatchCoordinator coordinator = CreateCoordinator(
                out rooms,
                out matches);
            Room room = CreateReadyRoom(rooms);

            Match match = coordinator.StartMatch(
                room.RoomId.ToString(),
                SudokuDifficulty.Easy,
                TimeSpan.FromMinutes(10));

            Assert.AreEqual(room.RoomId, match.RoomId);
            Assert.AreEqual(room.Players[0].PlayerId, match.PlayerAId);
            Assert.AreEqual(room.Players[1].PlayerId, match.PlayerBId);
            Assert.AreEqual(1, SudokuGenerator.CountSolutions(
                match.OriginalPuzzle,
                2));
            Assert.AreEqual(1, matches.GetActiveMatchSummaries(
                DateTime.UtcNow).Count);

            Assert.IsFalse(matches.MarkPlayerReady(
                match.MatchId,
                match.PlayerAId));
            Assert.IsTrue(matches.MarkPlayerReady(
                match.MatchId,
                match.PlayerBId));

            int emptyRow = -1;
            int emptyCol = -1;
            for (int row = 0; row < 9 && emptyRow < 0; row++)
            for (int col = 0; col < 9; col++)
            {
                if (match.OriginalPuzzle[row, col] == 0)
                {
                    emptyRow = row;
                    emptyCol = col;
                    break;
                }
            }

            int correctValue = match.SolutionGrid[emptyRow, emptyCol];
            int incorrectValue = correctValue == 9 ? 1 : correctValue + 1;
            MoveResult incorrectMove = matches.SubmitMove(
                match.MatchId,
                match.PlayerAId,
                "move-incorrect",
                emptyRow,
                emptyCol,
                incorrectValue);
            Assert.IsTrue(incorrectMove.Accepted);
            Assert.IsFalse(incorrectMove.IsCorrect);
            Assert.AreEqual(MoveErrorCode.IncorrectValue, incorrectMove.ErrorCode);
            Assert.AreEqual(incorrectValue,
                matches.GetPlayerSnapshot(match.MatchId, match.PlayerAId)
                    .OwnBoard[emptyRow, emptyCol]);

            MoveResult correctMove = matches.SubmitMove(
                match.MatchId,
                match.PlayerAId,
                "move-correct",
                emptyRow,
                emptyCol,
                correctValue);
            Assert.IsTrue(correctMove.Accepted);
            Assert.IsTrue(correctMove.IsCorrect);
        }

        [TestMethod]
        public void StartMatch_WhenRoomAlreadyHasActiveMatch_IsRejected()
        {
            RoomManager rooms;
            MatchManager matches;
            MatchCoordinator coordinator = CreateCoordinator(
                out rooms,
                out matches);
            Room room = CreateReadyRoom(rooms);

            coordinator.StartMatch(
                room.RoomId.ToString(),
                SudokuDifficulty.Easy,
                TimeSpan.FromMinutes(10));

            Assert.ThrowsException<InvalidOperationException>(() =>
                coordinator.StartMatch(
                    room.RoomId.ToString(),
                    SudokuDifficulty.Easy,
                    TimeSpan.FromMinutes(10)));
        }

        [TestMethod]
        public void StartMatch_WhenRoomHasOnlyOnePlayer_IsRejected()
        {
            RoomManager rooms;
            MatchManager matches;
            MatchCoordinator coordinator = CreateCoordinator(
                out rooms,
                out matches);
            Room room = rooms.CreateRoom(
                "Waiting room",
                new Player("player-a", "A"));

            Assert.ThrowsException<InvalidOperationException>(() =>
                coordinator.StartMatch(
                    room.RoomId.ToString(),
                    SudokuDifficulty.Easy,
                    TimeSpan.FromMinutes(10)));
        }

        [TestMethod]
        public void StartMatch_WithInvalidRoomId_IsRejected()
        {
            RoomManager rooms;
            MatchManager matches;
            MatchCoordinator coordinator = CreateCoordinator(
                out rooms,
                out matches);

            Assert.ThrowsException<ArgumentException>(() =>
                coordinator.StartMatch(
                    "not-a-guid",
                    SudokuDifficulty.Easy,
                    TimeSpan.FromMinutes(10)));
        }

        [TestMethod]
        public void StartMatch_WhenRoomDoesNotExist_IsRejected()
        {
            RoomManager rooms;
            MatchManager matches;
            MatchCoordinator coordinator = CreateCoordinator(
                out rooms,
                out matches);

            Assert.ThrowsException<InvalidOperationException>(() =>
                coordinator.StartMatch(
                    Guid.NewGuid().ToString(),
                    SudokuDifficulty.Easy,
                    TimeSpan.FromMinutes(10)));
        }

        [TestMethod]
        public void StartMatch_WithInvalidTimeLimit_IsRejected()
        {
            RoomManager rooms;
            MatchManager matches;
            MatchCoordinator coordinator = CreateCoordinator(
                out rooms,
                out matches);
            Room room = CreateReadyRoom(rooms);

            Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
                coordinator.StartMatch(
                    room.RoomId.ToString(),
                    SudokuDifficulty.Easy,
                    TimeSpan.Zero));
        }

        [TestMethod]
        public void MatchManager_WhenPuzzleClueDoesNotMatchSolution_IsRejected()
        {
            var generator = new SudokuGenerator();
            GeneratedSudoku sudoku = generator.GeneratePuzzle(
                SudokuDifficulty.Easy,
                101);
            int row = 0;
            int col = 0;
            while (sudoku.Puzzle[row, col] == 0)
            {
                col++;
                if (col == 9)
                {
                    col = 0;
                    row++;
                }
            }
            sudoku.Puzzle[row, col] =
                sudoku.Puzzle[row, col] == 9 ? 1 :
                sudoku.Puzzle[row, col] + 1;

            var matchManager = new MatchManager();

            Assert.ThrowsException<ArgumentException>(() =>
                matchManager.StartMatch(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "player-a",
                    "player-b",
                    sudoku.Puzzle,
                    sudoku.Solution,
                    TimeSpan.FromMinutes(10)));
        }

        [TestMethod]
        public void StartMatch_WhenGeneratorFails_DoesNotCreateMatch()
        {
            var rooms = new RoomManager();
            var matches = new MatchManager();
            Room room = CreateReadyRoom(rooms);
            var coordinator = new MatchCoordinator(
                new FailingSudokuGenerator(),
                rooms,
                matches);

            Assert.ThrowsException<InvalidOperationException>(() =>
                coordinator.StartMatch(
                    room.RoomId.ToString(),
                    SudokuDifficulty.Easy,
                    TimeSpan.FromMinutes(10)));
            Assert.AreEqual(
                0,
                matches.GetActiveMatchSummaries(DateTime.UtcNow).Count);
        }

        [TestMethod]
        public void MatchManager_WithInvalidIdentifiers_IsRejected()
        {
            GeneratedSudoku sudoku = CreateGeneratedSudoku();
            var matches = new MatchManager();

            Assert.ThrowsException<ArgumentException>(() => matches.StartMatch(
                Guid.Empty, Guid.NewGuid(), "player-a", "player-b",
                sudoku.Puzzle, sudoku.Solution, TimeSpan.FromMinutes(10)));
            Assert.ThrowsException<ArgumentException>(() => matches.StartMatch(
                Guid.NewGuid(), Guid.Empty, "player-a", "player-b",
                sudoku.Puzzle, sudoku.Solution, TimeSpan.FromMinutes(10)));
            Assert.ThrowsException<ArgumentException>(() => matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), " ", "player-b",
                sudoku.Puzzle, sudoku.Solution, TimeSpan.FromMinutes(10)));
            Assert.ThrowsException<ArgumentException>(() => matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "same", "same",
                sudoku.Puzzle, sudoku.Solution, TimeSpan.FromMinutes(10)));
        }

        [TestMethod]
        public void MatchManager_WithInvalidGrids_IsRejected()
        {
            GeneratedSudoku sudoku = CreateGeneratedSudoku();
            var matches = new MatchManager();

            Assert.ThrowsException<ArgumentException>(() => matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "player-a", "player-b",
                new int[8, 9], sudoku.Solution, TimeSpan.FromMinutes(10)));

            int[,] invalidPuzzle = MatchGrid.Clone(sudoku.Puzzle);
            invalidPuzzle[0, 0] = 10;
            Assert.ThrowsException<ArgumentException>(() => matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "player-a", "player-b",
                invalidPuzzle, sudoku.Solution, TimeSpan.FromMinutes(10)));

            int[,] invalidSolution = MatchGrid.Clone(sudoku.Solution);
            invalidSolution[0, 0] = 0;
            Assert.ThrowsException<ArgumentException>(() => matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "player-a", "player-b",
                sudoku.Puzzle, invalidSolution, TimeSpan.FromMinutes(10)));

            Assert.ThrowsException<ArgumentException>(() => matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "player-a", "player-b",
                sudoku.Solution, sudoku.Solution, TimeSpan.FromMinutes(10)));
        }

        [TestMethod]
        public void MatchManager_ClonesSolutionBeforeKeepingItInternally()
        {
            GeneratedSudoku sudoku = CreateGeneratedSudoku();
            int[,] suppliedSolution = MatchGrid.Clone(sudoku.Solution);
            var matches = new MatchManager();
            Match match = matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "player-a", "player-b",
                sudoku.Puzzle, suppliedSolution, TimeSpan.FromMinutes(10));

            int originalValue = match.SolutionGrid[0, 0];
            suppliedSolution[0, 0] = originalValue == 9 ? 1 : originalValue + 1;

            Assert.AreEqual(originalValue, match.SolutionGrid[0, 0]);
        }

        [TestMethod]
        public void PlayerMove_ChangesOnlyOwnBoardAtTheExactCoordinate()
        {
            GeneratedSudoku sudoku = CreateGeneratedSudoku();
            var matches = new MatchManager();
            Match match = matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "player-a", "player-b",
                sudoku.Puzzle, sudoku.Solution, TimeSpan.FromMinutes(10));
            matches.MarkPlayerReady(match.MatchId, "player-a");
            matches.MarkPlayerReady(match.MatchId, "player-b");
            FindEmptyCell(sudoku.Puzzle, out int row, out int column);

            int value = sudoku.Solution[row, column];
            MoveResult result = matches.SubmitMove(
                match.MatchId,
                "player-a",
                "exact-coordinate",
                row,
                column,
                value);

            Assert.IsTrue(result.Accepted);
            Assert.AreEqual(value, match.BoardA.CurrentValues[row, column]);
            Assert.AreEqual(0, match.BoardB.CurrentValues[row, column]);

            int differences = 0;
            for (int currentRow = 0; currentRow < 9; currentRow++)
            for (int currentColumn = 0; currentColumn < 9; currentColumn++)
            {
                if (match.BoardA.CurrentValues[currentRow, currentColumn] !=
                    sudoku.Puzzle[currentRow, currentColumn])
                {
                    differences++;
                }
            }
            Assert.AreEqual(1, differences);
        }

        [TestMethod]
        public void DuplicateMoveId_IsAppliedOnceAndSnapshotRestoresBoard()
        {
            GeneratedSudoku sudoku = CreateGeneratedSudoku();
            var matches = new MatchManager();
            Match match = matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "player-a", "player-b",
                sudoku.Puzzle, sudoku.Solution, TimeSpan.FromMinutes(10));
            matches.MarkPlayerReady(match.MatchId, "player-a");
            matches.MarkPlayerReady(match.MatchId, "player-b");
            FindEmptyCell(sudoku.Puzzle, out int row, out int column);

            int value = sudoku.Solution[row, column];
            MoveResult first = matches.SubmitMove(
                match.MatchId, "player-a", "same-move-id", row, column, value);
            MoveResult duplicate = matches.SubmitMove(
                match.MatchId, "player-a", "same-move-id", row, column, value);
            PlayerMatchSnapshot restored = matches.GetPlayerSnapshot(
                match.MatchId,
                "player-a");

            Assert.IsTrue(first.Accepted);
            Assert.AreSame(first, duplicate);
            Assert.AreEqual(1, restored.OwnCorrectCount);
            Assert.AreEqual(value, restored.OwnBoard[row, column]);
            Assert.AreEqual(1, match.ProcessedMoves.Count);
        }

        [TestMethod]
        public void GivenCell_CannotBeModifiedByEitherPlayer()
        {
            GeneratedSudoku sudoku = CreateGeneratedSudoku();
            var matches = new MatchManager();
            Match match = matches.StartMatch(
                Guid.NewGuid(), Guid.NewGuid(), "player-a", "player-b",
                sudoku.Puzzle, sudoku.Solution, TimeSpan.FromMinutes(10));
            matches.MarkPlayerReady(match.MatchId, "player-a");
            matches.MarkPlayerReady(match.MatchId, "player-b");

            int row = 0;
            int column = 0;
            while (sudoku.Puzzle[row, column] == 0)
            {
                column++;
                if (column == 9)
                {
                    row++;
                    column = 0;
                }
            }

            MoveResult playerA = matches.SubmitMove(
                match.MatchId, "player-a", "given-a", row, column, 0);
            MoveResult playerB = matches.SubmitMove(
                match.MatchId, "player-b", "given-b", row, column, 0);

            Assert.AreEqual(MoveErrorCode.GivenCellLocked, playerA.ErrorCode);
            Assert.AreEqual(MoveErrorCode.GivenCellLocked, playerB.ErrorCode);
            Assert.AreEqual(sudoku.Puzzle[row, column],
                match.BoardA.CurrentValues[row, column]);
            Assert.AreEqual(sudoku.Puzzle[row, column],
                match.BoardB.CurrentValues[row, column]);
        }

        private static MatchCoordinator CreateCoordinator(
            out RoomManager rooms,
            out MatchManager matches)
        {
            rooms = new RoomManager();
            matches = new MatchManager();
            return new MatchCoordinator(
                new SudokuGenerator(),
                rooms,
                matches);
        }

        private static Room CreateReadyRoom(RoomManager rooms)
        {
            Room room = rooms.CreateRoom(
                "Ready room",
                new Player("player-a", "A"));
            Assert.IsTrue(rooms.JoinRoom(
                room.RoomId,
                new Player("player-b", "B")));
            return room;
        }

        private static GeneratedSudoku CreateGeneratedSudoku()
        {
            return new SudokuGenerator().GeneratePuzzle(
                SudokuDifficulty.Easy,
                20260812);
        }

        private static void FindEmptyCell(
            int[,] puzzle,
            out int row,
            out int column)
        {
            for (row = 0; row < 9; row++)
            for (column = 0; column < 9; column++)
            {
                if (puzzle[row, column] == 0)
                    return;
            }

            throw new InvalidOperationException("Test puzzle has no empty cell.");
        }

        private sealed class FailingSudokuGenerator : ISudokuGenerator
        {
            public GeneratedSudoku GeneratePuzzle(
                SudokuDifficulty difficulty,
                int? seed = null)
            {
                throw new InvalidOperationException("Generation failed.");
            }

            public int[,] GenerateSolution(int? seed = null)
            {
                throw new InvalidOperationException("Generation failed.");
            }
        }
    }
}
