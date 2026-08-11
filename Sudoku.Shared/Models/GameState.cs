using System;
        
namespace Sudoku.Shared.Models
{
    public class GameState
    {
        // ID của trận đấu
        public Guid MatchId { get; set; }

        // Bàn Sudoku hiện tại
        // 0 = ô chưa có số
        public int[,] Board { get; set; }

        // Người chơi đang thực hiện lượt
        public Guid CurrentPlayerId { get; set; }

        // Trạng thái trận đấu
        public GameStatus Status { get; set; }

        // Khởi tạo GameState mặc định
        public GameState()
        {
            MatchId = Guid.NewGuid();
            Board = new int[9, 9];
            CurrentPlayerId = Guid.Empty;
            Status = GameStatus.Waiting;
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