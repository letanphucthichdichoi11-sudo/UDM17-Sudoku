using System;

namespace Sudoku.Shared.Models
{
    public class Player
    {
        public string PlayerId { get; set; }

        public string PlayerName { get; set; }

        // Trạng thái sẵn sàng trong phòng chờ
        public bool IsReady { get; set; }

        // Constructor rỗng (Bắt buộc phải có để Deserialize JSON)
        public Player()
        {
            PlayerId = string.Empty;
            PlayerName = string.Empty;
            IsReady = false;
        }

        // Constructor có tham số để khởi tạo nhanh
        public Player(string playerId, string playerName)
        {
            PlayerId = playerId;
            PlayerName = playerName;
            IsReady = false;
        }
    }
}
