namespace Sudoku.Api.DTO
{
    public class VerifyEmailRequest
    {
        public string Email { get; set; }
        public string OtpCode { get; set; }
    }
}