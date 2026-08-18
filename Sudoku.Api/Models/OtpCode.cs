namespace Sudoku.Api.Models
{
    public class OtpCode
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User User { get; set; }

        public string Code { get; set; }
        public string Type { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool IsUsed { get; set; } = false;
    }
}   