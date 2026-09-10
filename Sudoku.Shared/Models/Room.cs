using System;
using System.Collections.Generic;

namespace Sudoku.Shared.Models
{
    public class Room
    {
        public Guid RoomId { get; set; }

        public string RoomName { get; set; }

        public int MaxPlayers { get; set; }

        public List<Player> Players { get; set; }
        public SudokuDifficultyLevel Difficulty { get; set; }

        public Room(
            Guid roomId,
            string roomName,
            int maxPlayers = 2,
            SudokuDifficultyLevel difficulty = SudokuDifficultyLevel.Medium)
        {
            RoomId = roomId;
            RoomName = roomName;
            MaxPlayers = maxPlayers;
            Difficulty = difficulty;
            Players = new List<Player>();
        }
    }
}
