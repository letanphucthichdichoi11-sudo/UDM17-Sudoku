using System;

namespace Sudoku.Shared.Models
{
    public class GameState
    {
        // ID của trận đấu
        public Guid MatchId { get; set; }

        // Bàn Sudoku hiện tại (Dùng int[][] thay cho int[,] để hỗ trợ JSON)
        // 0 = ô chưa có số
        public int[][] Board { get; set; }

        // Người chơi đang thực hiện lượt
        public Guid CurrentPlayerId { get; set; }

        // Trạng thái trận đấu
        public GameStatus Status { get; set; }

        // Khởi tạo GameState mặc định
        public GameState()
        {
            MatchId = Guid.NewGuid();
            CurrentPlayerId = Guid.Empty;
            Status = GameStatus.Waiting;

            // Khởi tạo mảng Jagged 9x9 chuẩn
            Board = new int[9][];
            for (int i = 0; i < 9; i++)
            {
                Board[i] = new int[9];
            }
        }
    }

    public enum GameStatus
    {
        Waiting,
        Playing,
        Finished,
        Aborted
    }
}
