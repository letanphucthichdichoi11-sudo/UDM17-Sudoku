using System;
using System.Linq;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Game
{
    internal sealed class MatchCoordinator
    {
        private readonly SudokuGenerator _sudokuGenerator;
        private readonly RoomManager _roomManager;
        private readonly MatchManager _matchManager;
        private readonly object _startLock = new object();

        public MatchCoordinator(
            SudokuGenerator sudokuGenerator,
            RoomManager roomManager,
            MatchManager matchManager)
        {
            _sudokuGenerator = sudokuGenerator ??
                throw new ArgumentNullException("sudokuGenerator");
            _roomManager = roomManager ??
                throw new ArgumentNullException("roomManager");
            _matchManager = matchManager ??
                throw new ArgumentNullException("matchManager");
        }

        public Match StartMatch(
            string roomId,
            SudokuDifficulty difficulty,
            TimeSpan timeLimit)
        {
            if (String.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("Room id is required.", "roomId");

            if (timeLimit <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("timeLimit");

            Guid parsedRoomId;
            if (!Guid.TryParse(roomId, out parsedRoomId))
            {
                throw new ArgumentException(
                    "Room id must be a valid GUID.",
                    "roomId");
            }

            lock (_startLock)
            {
                Room room = _roomManager.GetRoom(roomId);
                if (room == null)
                    throw new InvalidOperationException("Room was not found.");

                if (room.Players == null || room.Players.Count != 2)
                {
                    throw new InvalidOperationException(
                        "A match requires exactly two players.");
                }

                if (room.Players.Any(player =>
                    player == null ||
                    String.IsNullOrWhiteSpace(player.PlayerId)))
                {
                    throw new InvalidOperationException(
                        "Every room player must have a valid id.");
                }

                if (room.Players[0].PlayerId == room.Players[1].PlayerId)
                {
                    throw new InvalidOperationException(
                        "A match requires two different players.");
                }

                bool hasActiveMatch = _matchManager
                    .GetActiveMatchSummaries(DateTime.UtcNow)
                    .Any(match => match.RoomId == parsedRoomId);

                if (hasActiveMatch)
                {
                    throw new InvalidOperationException(
                        "The room already has an active match.");
                }

                GeneratedSudoku sudoku =
                    _sudokuGenerator.GeneratePuzzle(difficulty);

                return _matchManager.StartMatch(
                    parsedRoomId,
                    Guid.NewGuid(),
                    room.Players[0].PlayerId,
                    room.Players[1].PlayerId,
                    sudoku.Puzzle,
                    sudoku.Solution,
                    timeLimit);
            }
        }
    }
}
