namespace Sudoku.Api.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Email { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; }
        public string Status { get; set; } = "Unverified";
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }

        public ICollection<UserSession> Sessions { get; set; }
        public ICollection<OtpCode> OtpCodes { get; set; }
    }
}