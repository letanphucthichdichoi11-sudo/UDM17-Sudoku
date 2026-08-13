using System.Text.RegularExpressions;
using Sudoku.Mobile.Network;
using Sudoku.Mobile.Models;

namespace Sudoku.Mobile;

public partial class RegisterPage : ContentPage
{
    private readonly ApiClient _apiClient;

    public RegisterPage()
    {
        InitializeComponent();
        _apiClient = new ApiClient();
    }

    // 1. Validate Password Realtime
    private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
    {
        string password = e.NewTextValue ?? string.Empty;
        int score = 0;

        if (password.Length >= 8) score++;
        if (Regex.IsMatch(password, @"[a-z]") && Regex.IsMatch(password, @"[A-Z]")) score++;
        if (Regex.IsMatch(password, @"[0-9]")) score++;
        if (Regex.IsMatch(password, @"[\W]")) score++; // Ký tự đặc biệt

        switch (score)
        {
            case 0:
            case 1:
                PasswordStrengthBar.Progress = 0.25;
                PasswordStrengthBar.ProgressColor = Colors.Red;
                PasswordStrengthLabel.Text = "Yếu";
                PasswordStrengthLabel.TextColor = Colors.Red;
                break;
            case 2:
                PasswordStrengthBar.Progress = 0.50;
                PasswordStrengthBar.ProgressColor = Colors.Orange;
                PasswordStrengthLabel.Text = "Trung bình";
                PasswordStrengthLabel.TextColor = Colors.Orange;
                break;
            case 3:
                PasswordStrengthBar.Progress = 0.75;
                PasswordStrengthBar.ProgressColor = Colors.YellowGreen;
                PasswordStrengthLabel.Text = "Khá";
                PasswordStrengthLabel.TextColor = Colors.YellowGreen;
                break;
            case 4:
                PasswordStrengthBar.Progress = 1.0;
                PasswordStrengthBar.ProgressColor = Colors.Green;
                PasswordStrengthLabel.Text = "Mạnh";
                PasswordStrengthLabel.TextColor = Colors.Green;
                break;
        }

        if (string.IsNullOrEmpty(password))
        {
            PasswordStrengthBar.Progress = 0;
            PasswordStrengthLabel.Text = "Nhập mật khẩu...";
            PasswordStrengthLabel.TextColor = Colors.Gray;
        }
    }

    // 2. Gửi Yêu Cầu Đăng Ký
    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        string username = UsernameEntry.Text?.Trim();
        string email = EmailEntry.Text?.Trim();
        string password = PasswordEntry.Text;
        string confirm = ConfirmPasswordEntry.Text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            await DisplayAlert("Lỗi", "Vui lòng điền đầy đủ thông tin.", "OK");
            return;
        }

        if (password != confirm)
        {
            await DisplayAlert("Lỗi", "Mật khẩu xác nhận không khớp.", "OK");
            return;
        }

        RegisterBtn.IsEnabled = false;
        RegisterBtn.Text = "Đang xử lý...";

        var request = new RegisterRequest { Username = username, Email = email, Password = password };
        var response = await _apiClient.PostAsync<RegisterRequest, BaseAuthResponse>("api/auth/register", request);

        if (response != null && response.Success)
        {
            // Ẩn form nhập liệu, hiện form OTP
            RegisterFormLayout.IsVisible = false;
            OtpFormLayout.IsVisible = true;
            await DisplayAlert("Thành công", "Mã OTP đã được gửi đến email của bạn.", "OK");
        }
        else
        {
            await DisplayAlert("Lỗi", response?.Message ?? "Không thể kết nối đến máy chủ.", "OK");
        }

        RegisterBtn.IsEnabled = true;
        RegisterBtn.Text = "Đăng ký & Nhận OTP";
    }

    // 3. Xác Nhận OTP
    private async void OnVerifyOtpClicked(object sender, EventArgs e)
    {
        string email = EmailEntry.Text?.Trim();
        string otp = OtpEntry.Text?.Trim();

        if (string.IsNullOrEmpty(otp) || otp.Length != 6)
        {
            await DisplayAlert("Lỗi", "Vui lòng nhập đúng 6 số OTP.", "OK");
            return;
        }

        VerifyOtpBtn.IsEnabled = false;

        var request = new VerifyOtpRequest { Email = email, Otp = otp };
        var response = await _apiClient.PostAsync<VerifyOtpRequest, BaseAuthResponse>("api/auth/verify-otp", request);

        if (response != null && response.Success)
        {
            await DisplayAlert("Thành công", "Xác thực thành công. Bạn có thể đăng nhập ngay bây giờ.", "OK");
            // Quay về màn hình Login
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            await DisplayAlert("Lỗi", response?.Message ?? "Mã OTP không hợp lệ.", "OK");
            VerifyOtpBtn.IsEnabled = true;
        }
    }
}