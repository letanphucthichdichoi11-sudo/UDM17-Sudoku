using System;
using System.Linq;
using Sudoku.Shared.Models;

namespace Sudoku.Server.Game
{
    internal sealed class MatchCoordinator
    {
        private readonly ISudokuGenerator _sudokuGenerator;
        private readonly IRoomManager _roomManager;
        private readonly IMatchManager _matchManager;
        private readonly IClock _clock;
        private readonly object _startLock = new object();

        public MatchCoordinator(
            ISudokuGenerator sudokuGenerator,
            IRoomManager roomManager,
            IMatchManager matchManager)
            : this(sudokuGenerator, roomManager, matchManager, new SystemClock())
        {
        }

        public MatchCoordinator(
            ISudokuGenerator sudokuGenerator,
            IRoomManager roomManager,
            IMatchManager matchManager,
            IClock clock)
        {
            _sudokuGenerator = sudokuGenerator ??
                throw new ArgumentNullException("sudokuGenerator");
            _roomManager = roomManager ??
                throw new ArgumentNullException("roomManager");
            _matchManager = matchManager ??
                throw new ArgumentNullException("matchManager");
            _clock = clock ?? throw new ArgumentNullException("clock");
        }

        public Match StartMatch(
            string roomId,
            SudokuDifficulty difficulty,
            TimeSpan timeLimit)
        {
            MatchDurationMinutes duration = ParseDuration(timeLimit);
            return StartMatch(roomId, difficulty, duration);
        }

        public Match StartMatch(
            string roomId,
            SudokuDifficulty difficulty,
            MatchDurationMinutes duration)
        {
            if (String.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("Room id is required.", "roomId");

            ValidateDuration(duration);

            Guid parsedRoomId;
            if (!Guid.TryParse(roomId, out parsedRoomId))
            {
                throw new ArgumentException(
                    "Room id must be a valid GUID.",
                    "roomId");
            }

            lock (_startLock)
            {
                Room room = _roomManager.GetRoom(parsedRoomId);
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
                    .GetActiveMatchSummaries(_clock.UtcNow)
                    .Any(match => match.RoomId == parsedRoomId);

                if (hasActiveMatch)
                {
                    throw new InvalidOperationException(
                        "The room already has an active match.");
                }

                SudokuDifficulty roomDifficulty = (SudokuDifficulty)room.Difficulty;
                GeneratedSudoku sudoku =
                    _sudokuGenerator.GeneratePuzzle(roomDifficulty);
                GeneratedSudoku sudokuB;
                do
                {
                    sudokuB = _sudokuGenerator.GeneratePuzzle(roomDifficulty);
                }
                while (MatchGrid.Flatten(sudoku.Puzzle).SequenceEqual(MatchGrid.Flatten(sudokuB.Puzzle)));

                return _matchManager.StartMatch(
                    parsedRoomId,
                    Guid.NewGuid(),
                    room.Players[0].PlayerId,
                    room.Players[1].PlayerId,
                    sudoku.Puzzle,
                    sudoku.Solution,
                    sudokuB.Puzzle,
                    sudokuB.Solution,
                    duration);
            }
        }

        public Match StartMatch(StartMatchRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            return StartMatch(
                request.RoomId,
                (SudokuDifficulty)request.Difficulty,
                request.Duration);
        }

        private static MatchDurationMinutes ParseDuration(TimeSpan timeLimit)
        {
            if (timeLimit == TimeSpan.FromMinutes(5)) return MatchDurationMinutes.Five;
            if (timeLimit == TimeSpan.FromMinutes(10)) return MatchDurationMinutes.Ten;
            if (timeLimit == TimeSpan.FromMinutes(15)) return MatchDurationMinutes.Fifteen;
            throw new ArgumentOutOfRangeException("timeLimit", "Match duration must be 5, 10 or 15 minutes.");
        }

        private static void ValidateDuration(MatchDurationMinutes duration)
        {
            if (duration != MatchDurationMinutes.Five &&
                duration != MatchDurationMinutes.Ten &&
                duration != MatchDurationMinutes.Fifteen)
                throw new ArgumentOutOfRangeException("duration");
        }
    }
}
