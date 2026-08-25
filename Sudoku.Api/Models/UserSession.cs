namespace Sudoku.Api.Models
{
    public class UserSession
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }

        public string RefreshToken { get; set; }
        public string DeviceInfo { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool IsRevoked { get; set; } = false;
    }
}