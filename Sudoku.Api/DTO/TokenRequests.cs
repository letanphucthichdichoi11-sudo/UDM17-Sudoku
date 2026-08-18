namespace Sudoku.Api.DTO
{
    public class RefreshTokenRequest
    {
        public string RefreshToken { get; set; }
    }

    public class LogoutRequest
    {
        public string RefreshToken { get; set; }
    }
}