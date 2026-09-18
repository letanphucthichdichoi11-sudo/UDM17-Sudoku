using System;

namespace Sudoku.Shared.Models
{
    public class Player
    {
        public string PlayerId { get; set; }

        public string PlayerName { get; set; }

        public Player(string playerId, string playerName)
        {
            PlayerId = playerId;
            PlayerName = playerName;
        }
    }
}