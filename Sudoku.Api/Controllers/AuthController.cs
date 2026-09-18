using System;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sudoku.Api.Data;
using Sudoku.Api.Models;
using Sudoku.Api.Services; 

namespace Sudoku.Api.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;

        // Bơm cả Cầu nối Database và Dịch vụ Email vào Controller
        public AuthController(AppDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            // Tìm user trong DB
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == request.UsernameOrEmail || u.Username == request.UsernameOrEmail);

            if (user == null)
            {
                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    Message = "Sai tài khoản hoặc mật khẩu."
                });
            }

            // Kiểm tra trạng thái tài khoản
            if (user.Status != "Active")
            {
                return StatusCode(403, new LoginResponse
                {
                    Success = false,
                    Message = "Tài khoản chưa được kích hoạt. Vui lòng xác thực OTP!"
                });
            }

            // Xác thực mật khẩu
            bool isPasswordCorrect = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
            if (!isPasswordCorrect)
            {
                return Unauthorized(new LoginResponse
                {
                    Success = false,
                    Message = "Sai tài khoản hoặc mật khẩu."
                });
            }

            // Tạo Token
            string accessToken = GenerateJwtToken(user);
            string refreshToken = Guid.NewGuid().ToString() + "-refresh";

            // Lưu phiên đăng nhập
            var session = new UserSession
            {
                UserId = user.Id,
                RefreshToken = refreshToken,
                DeviceInfo = "Mobile App",
                ExpiryDate = DateTime.UtcNow.AddDays(30),
                IsRevoked = false
            };
            _context.UserSessions.Add(session);
            await _context.SaveChangesAsync();

            return Ok(new LoginResponse
            {
                Success = true,
                AccessToken = accessToken,
                RefreshToken = refreshToken
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            // 1. Kiểm tra Email/Username trùng lặp
            var existingEmail = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (existingEmail != null)
                return Ok(new BaseAuthResponse { Success = false, Message = "Email đã tồn tại." });

            var existingUsername = await _context.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
            if (existingUsername != null)
                return Ok(new BaseAuthResponse { Success = false, Message = "Username đã tồn tại." });

            // 2. Băm mật khẩu
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // 3. Tạo User mới (Unverified)
            var newUser = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = passwordHash,
                Status = "Unverified"
            };
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            // 4. Tạo mã OTP
            string otp = new Random().Next(100000, 999999).ToString();
            var newOtp = new OtpCode
            {
                UserId = newUser.Id,
                Code = otp,
                Type = "Register",
                ExpiryDate = DateTime.UtcNow.AddMinutes(15),
                IsUsed = false
            };
            _context.OtpCodes.Add(newOtp);
            await _context.SaveChangesAsync();

            // 5. Gửi Email 
            string subject = "Mã xác nhận đăng ký Sudoku";
            string body = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; text-align: center; border: 1px solid #ddd; border-radius: 10px;'>
                    <h2 style='color: #4CAF50;'>Chào mừng bạn đến với Sudoku!</h2>
                    <p>Mã OTP xác nhận tài khoản của bạn là:</p>
                    <h1 style='color: #d9534f; letter-spacing: 5px; font-size: 36px; background-color: #f9f9f9; padding: 10px; border-radius: 5px; display: inline-block;'>{otp}</h1>
                    <p style='color: #888;'>Mã này sẽ hết hạn trong vòng 15 phút. Vui lòng không chia sẻ cho bất kỳ ai.</p>
                </div>";

            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendEmailAsync(newUser.Email, subject, body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LỖI GỬI MAIL NGẦM]: {ex.Message}");
                }
            });

            return Ok(new BaseAuthResponse { Success = true, Message = "Đã gửi OTP qua Email của bạn." });
        }

        [HttpPost("verify-otp")]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null)
                return Ok(new BaseAuthResponse { Success = false, Message = "Không tìm thấy tài khoản." });

            var savedOtp = await _context.OtpCodes
                .Where(o => o.UserId == user.Id && o.Type == "Register" && !o.IsUsed)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync();

            if (savedOtp != null && savedOtp.Code == request.Otp)
            {
                if (savedOtp.ExpiryDate < DateTime.UtcNow)
                    return Ok(new BaseAuthResponse { Success = false, Message = "OTP đã hết hạn." });

                savedOtp.IsUsed = true;
                user.Status = "Active";
                await _context.SaveChangesAsync();

                return Ok(new BaseAuthResponse { Success = true, Message = "Xác thực thành công. Bạn có thể đăng nhập!" });
            }
            return Ok(new BaseAuthResponse { Success = false, Message = "OTP không hợp lệ." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null)
                return Ok(new BaseAuthResponse { Success = false, Message = "Email không tồn tại trong hệ thống." });

            string otp = new Random().Next(100000, 999999).ToString();

            var newOtp = new OtpCode
            {
                UserId = user.Id,
                Code = otp,
                Type = "ForgotPassword",
                ExpiryDate = DateTime.UtcNow.AddMinutes(15),
                IsUsed = false
            };
            _context.OtpCodes.Add(newOtp);
            await _context.SaveChangesAsync();

            // Gửi Email khôi phục mật khẩu
            string subject = "Yêu cầu khôi phục mật khẩu Sudoku";
            string body = $@"
                <div style='font-family: Arial, sans-serif; padding: 20px; text-align: center; border: 1px solid #ddd; border-radius: 10px;'>
                    <h2 style='color: #f39c12;'>Khôi phục mật khẩu</h2>
                    <p>Bạn vừa yêu cầu đặt lại mật khẩu. Mã OTP của bạn là:</p>
                    <h1 style='color: #d9534f; letter-spacing: 5px; font-size: 36px; background-color: #f9f9f9; padding: 10px; border-radius: 5px; display: inline-block;'>{otp}</h1>
                    <p style='color: #888;'>Mã này sẽ hết hạn trong vòng 15 phút. Nếu bạn không yêu cầu, vui lòng bỏ qua email này.</p>
                </div>";

            _ = Task.Run(async () =>
            {
                try
                {
                    await _emailService.SendEmailAsync(user.Email, subject, body);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LỖI GỬI MAIL NGẦM]: {ex.Message}");
                }
            });

            return Ok(new BaseAuthResponse { Success = true, Message = "Đã gửi OTP khôi phục qua email." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null)
                return Ok(new BaseAuthResponse { Success = false, Message = "Không tìm thấy tài khoản." });

            var savedOtp = await _context.OtpCodes
                .Where(o => o.UserId == user.Id && o.Type == "ForgotPassword" && !o.IsUsed)
                .OrderByDescending(o => o.Id)
                .FirstOrDefaultAsync();

            if (savedOtp != null && savedOtp.Code == request.Otp)
            {
                if (savedOtp.ExpiryDate < DateTime.UtcNow)
                    return Ok(new BaseAuthResponse { Success = false, Message = "OTP đã hết hạn." });

                // Băm mật khẩu mới
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                savedOtp.IsUsed = true;
                await _context.SaveChangesAsync();

                return Ok(new BaseAuthResponse { Success = true, Message = "Đổi mật khẩu thành công." });
            }
            return Ok(new BaseAuthResponse { Success = false, Message = "OTP sai hoặc đã hết hạn." });
        }

        // Cỗ máy chế tạo chìa khóa JWT
        private string GenerateJwtToken(User user)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("Day_la_mot_chuoi_bi_mat_rat_dai_va_phuc_tap_khong_ai_doan_duoc_123!@#"));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: "SudokuApp",
                audience: "SudokuUsers",
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    // ========================================================
    // CÁC CLASS MODEL DÙNG ĐỂ GIAO TIẾP VỚI APP
    // ========================================================
    public class LoginRequest { public string UsernameOrEmail { get; set; } public string Password { get; set; } }
    public class LoginResponse { public bool Success { get; set; } public string Message { get; set; } public string AccessToken { get; set; } public string RefreshToken { get; set; } }
    public class RegisterRequest { public string Username { get; set; } public string Email { get; set; } public string Password { get; set; } }
    public class VerifyOtpRequest { public string Email { get; set; } public string Otp { get; set; } }
    public class BaseAuthResponse { public bool Success { get; set; } public string Message { get; set; } }
    public class ForgotPasswordRequest { public string Email { get; set; } }
    public class ResetPasswordRequest { public string Email { get; set; } public string Otp { get; set; } public string NewPassword { get; set; } }
}
