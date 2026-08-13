using Sudoku.Mobile.Network;
using Sudoku.Mobile.Models;

namespace Sudoku.Mobile;

public partial class ForgotPasswordPage : ContentPage
{
    private readonly ApiClient _apiClient;

    public ForgotPasswordPage()
    {
        InitializeComponent();
        _apiClient = new ApiClient();
    }

    private async void OnSendOtpClicked(object sender, EventArgs e)
    {
        string email = EmailEntry.Text?.Trim();

        if (string.IsNullOrEmpty(email))
        {
            await DisplayAlert("Lỗi", "Vui lòng nhập Email.", "OK");
            return;
        }

        SendOtpBtn.IsEnabled = false;
        SendOtpBtn.Text = "Đang gửi...";

        var request = new ForgotPasswordRequest { Email = email };
        var response = await _apiClient.PostAsync<ForgotPasswordRequest, BaseAuthResponse>("api/auth/forgot-password", request);

        if (response != null && response.Success)
        {
            Step1Layout.IsVisible = false;
            Step2Layout.IsVisible = true;
            await DisplayAlert("Thành công", "Mã OTP đã được gửi về Email của bạn.", "OK");
        }
        else
        {
            await DisplayAlert("Lỗi", response?.Message ?? "Không thể gửi yêu cầu.", "OK");
        }

        SendOtpBtn.IsEnabled = true;
        SendOtpBtn.Text = "Gửi mã xác minh";
    }

    private async void OnResetPasswordClicked(object sender, EventArgs e)
    {
        string email = EmailEntry.Text?.Trim();
        string otp = OtpEntry.Text?.Trim();
        string newPassword = NewPasswordEntry.Text;
        string confirmPassword = ConfirmPasswordEntry.Text;

        if (string.IsNullOrEmpty(otp) || string.IsNullOrEmpty(newPassword))
        {
            await DisplayAlert("Lỗi", "Vui lòng điền đầy đủ OTP và mật khẩu mới.", "OK");
            return;
        }

        if (newPassword != confirmPassword)
        {
            await DisplayAlert("Lỗi", "Mật khẩu xác nhận không khớp.", "OK");
            return;
        }

        ResetBtn.IsEnabled = false;

        var request = new ResetPasswordRequest
        {
            Email = email,
            Otp = otp,
            NewPassword = newPassword
        };

        var response = await _apiClient.PostAsync<ResetPasswordRequest, BaseAuthResponse>("api/auth/reset-password", request);

        if (response != null && response.Success)
        {
            await DisplayAlert("Thành công", "Đổi mật khẩu thành công. Vui lòng đăng nhập lại.", "OK");
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            await DisplayAlert("Lỗi", response?.Message ?? "Mã OTP không hợp lệ hoặc đã hết hạn.", "OK");
            ResetBtn.IsEnabled = true;
        }
    }
}