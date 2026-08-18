namespace Sudoku.Api.DTO
{
    public class LoginRequest
    {
        public string UsernameOrEmail { get; set; }
        public string Password { get; set; }
        public string DeviceInfo { get; set; } = "Mobile App";
    }
}