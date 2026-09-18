namespace Sudoku.Mobile.Models;

public class LoginRequest
{
    public string UsernameOrEmail { get; set; }
    public string Password { get; set; }
}

public class LoginResponse
{
    public bool Success { get; set; }
    public string Message { get; set; }
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
}

public class RegisterRequest
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
}


public class BaseAuthResponse
{
    public bool Success { get; set; }
    public string Message { get; set; }
}

public class ForgotPasswordRequest
{
    public string Email { get; set; }
}

public class ResetPasswordRequest
{
    public string Email { get; set; }
    public string Otp { get; set; }
    public string NewPassword { get; set; }
}
public class VerifyOtpRequest
{
    public string Email { get; set; }
    public string Otp { get; set; }
}