namespace Sudoku.Mobile;
using Sudoku.Mobile.Network;
using Sudoku.Mobile.Models;

public partial class ForgotPasswordPage : ContentPage
{
    public ForgotPasswordPage()
    {
        InitializeComponent();
    }

    private async void OnSendOtpClicked(object sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(EmailEntry.Text))
        {
            await DisplayAlertAsync("Thiếu thông tin", "Vui lòng nhập Email để nhận mã cứu hộ!", "OK");
            return;
        }

        // Hiệu ứng chờ
        SendOtpBtn.IsEnabled = false;
        SendOtpBtn.Text = "Đang gửi...";

        var request = new ForgotPasswordRequest { Email = EmailEntry.Text };
        var apiClient = new ApiClient();

        // Gọi API xin OTP khôi phục
        var response = await apiClient.PostAsync<ForgotPasswordRequest, BaseAuthResponse>("api/auth/forgot-password", request);

        if (response != null && response.Success)
        {
            await DisplayAlertAsync("Thành công", "Mã OTP đã được gửi đến email của bạn.", "OK");
            Otp1.Focus(); // Tự động trỏ chuột vào ô OTP đầu tiên cho tiện
        }
        else
        {
            string msg = response?.Message ?? "Không thể kết nối đến máy chủ.";
            await DisplayAlertAsync("Lỗi", msg, "OK");
        }

        // Trả lại trạng thái nút
        SendOtpBtn.IsEnabled = true;
        SendOtpBtn.Text = "Send OTP";
    }

    private async void OnResetPasswordClicked(object sender, EventArgs e)
    {
        // Gom 6 số OTP lại
        string otpCode = $"{Otp1.Text}{Otp2.Text}{Otp3.Text}{Otp4.Text}{Otp5.Text}{Otp6.Text}";

        if (string.IsNullOrWhiteSpace(EmailEntry.Text) || otpCode.Length < 6 || string.IsNullOrWhiteSpace(NewPasswordEntry.Text))
        {
            await DisplayAlertAsync("Thiếu thông tin", "Vui lòng nhập đủ Email, 6 số OTP và Mật khẩu mới!", "OK");
            return;
        }

        ResetPasswordBtn.IsEnabled = false;
        ResetPasswordBtn.Text = "Đang xử lý...";

        var request = new ResetPasswordRequest
        {
            Email = EmailEntry.Text,
            Otp = otpCode,
            NewPassword = NewPasswordEntry.Text
        };

        var apiClient = new ApiClient();
        var response = await apiClient.PostAsync<ResetPasswordRequest, BaseAuthResponse>("api/auth/reset-password", request);

        if (response != null && response.Success)
        {
            await DisplayAlertAsync("Thành công", "Đổi mật khẩu thành công! Hãy đăng nhập lại bằng mật khẩu mới nhé.", "Tuyệt vời");
            await Shell.Current.GoToAsync(".."); // Lùi về trang Đăng nhập
        }
        else
        {
            string msg = response?.Message ?? "Lỗi kết nối hoặc OTP không hợp lệ.";
            await DisplayAlertAsync("Thất bại", msg, "Thử lại");
        }

        ResetPasswordBtn.IsEnabled = true;
        ResetPasswordBtn.Text = "Reset Password";
    }

    private void OnOtpTextChanged(object sender, TextChangedEventArgs e)
    {
        var entry = sender as Entry;

        if (!string.IsNullOrEmpty(e.NewTextValue))
        {
            if (entry == Otp1) Otp2.Focus();
            else if (entry == Otp2) Otp3.Focus();
            else if (entry == Otp3) Otp4.Focus();
            else if (entry == Otp4) Otp5.Focus();
            else if (entry == Otp5) Otp6.Focus();
            else if (entry == Otp6) NewPasswordEntry.Focus(); 
        }
    }

    private void OnOtpFocused(object sender, FocusEventArgs e)
    {
        var entry = sender as Entry;
        if (entry?.Parent is Border border)
        {
            border.Stroke = Color.FromArgb("#8B5CF6");
            border.StrokeThickness = 3;
        }
    }

    private void OnOtpUnfocused(object sender, FocusEventArgs e)
    {
        var entry = sender as Entry;
        if (entry?.Parent is Border border)
        {
            border.Stroke = Color.FromArgb("#06B6D4");
            border.StrokeThickness = 2;
        }
    }

    private async void OnLoginTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}