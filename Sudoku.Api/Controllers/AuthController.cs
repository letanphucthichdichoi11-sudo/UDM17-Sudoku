using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace Sudoku.Api.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        // Bộ nhớ tạm để test API
        private static readonly Dictionary<string, string> _users = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _otps = new Dictionary<string, string>();

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            if (_users.TryGetValue(request.UsernameOrEmail, out var pass) && pass == request.Password)
            {
                // Trả về Token để vào Dashboard
                return Ok(new LoginResponse
                {
                    Success = true,
                    AccessToken = Guid.NewGuid().ToString(),
                    RefreshToken = "dummy-refresh-token"
                });
            }
            return Ok(new LoginResponse { Success = false, Message = "Sai tài khoản hoặc mật khẩu." });
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] RegisterRequest request)
        {
            if (_users.ContainsKey(request.Email))
                return Ok(new BaseAuthResponse { Success = false, Message = "Email đã tồn tại." });

            string otp = new Random().Next(100000, 999999).ToString();
            _otps[request.Email] = otp;

            // In OTP ra cửa sổ Console (màn hình đen) của Server để bạn lấy mã
            Console.WriteLine($"\n[HỆ THỐNG] OTP Đăng ký của {request.Email} là: {otp}\n");

            _users[request.Email + "_temp"] = request.Password;
            return Ok(new BaseAuthResponse { Success = true, Message = "Đã gửi OTP." });
        }

        [HttpPost("verify-otp")]
        public IActionResult VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            if (_otps.TryGetValue(request.Email, out var savedOtp) && savedOtp == request.Otp)
            {
                _users[request.Email] = _users[request.Email + "_temp"];
                _users.Remove(request.Email + "_temp");
                _otps.Remove(request.Email);
                return Ok(new BaseAuthResponse { Success = true, Message = "Xác thực thành công." });
            }
            return Ok(new BaseAuthResponse { Success = false, Message = "OTP không hợp lệ hoặc đã hết hạn." });
        }

        [HttpPost("forgot-password")]
        public IActionResult ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            if (!_users.ContainsKey(request.Email))
                return Ok(new BaseAuthResponse { Success = false, Message = "Email không tồn tại trong hệ thống." });

            string otp = new Random().Next(100000, 999999).ToString();
            _otps[request.Email] = otp;
            Console.WriteLine($"\n[HỆ THỐNG] OTP Khôi phục của {request.Email} là: {otp}\n");

            return Ok(new BaseAuthResponse { Success = true, Message = "Đã gửi OTP khôi phục." });
        }

        [HttpPost("reset-password")]
        public IActionResult ResetPassword([FromBody] ResetPasswordRequest request)
        {
            if (_otps.TryGetValue(request.Email, out var savedOtp) && savedOtp == request.Otp)
            {
                _users[request.Email] = request.NewPassword;
                _otps.Remove(request.Email);
                return Ok(new BaseAuthResponse { Success = true, Message = "Đổi mật khẩu thành công." });
            }
            return Ok(new BaseAuthResponse { Success = false, Message = "OTP sai hoặc hết hạn." });
        }
    }

    // ==========================================
    // CÁC CLASS MODEL DÙNG ĐỂ GIAO TIẾP VỚI APP
    // ==========================================
    public class LoginRequest { public string UsernameOrEmail { get; set; } public string Password { get; set; } }
    public class LoginResponse { public bool Success { get; set; } public string Message { get; set; } public string AccessToken { get; set; } public string RefreshToken { get; set; } }
    public class RegisterRequest { public string Username { get; set; } public string Email { get; set; } public string Password { get; set; } }
    public class VerifyOtpRequest { public string Email { get; set; } public string Otp { get; set; } }
    public class BaseAuthResponse { public bool Success { get; set; } public string Message { get; set; } }
    public class ForgotPasswordRequest { public string Email { get; set; } }
    public class ResetPasswordRequest { public string Email { get; set; } public string Otp { get; set; } public string NewPassword { get; set; } }
}