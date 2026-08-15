namespace Sudoku.Mobile;

public partial class ForgotPasswordPage : ContentPage
{
    public ForgotPasswordPage()
    {
        InitializeComponent();
    }

    private void OnSendOtpClicked(object sender, EventArgs e)
    {
        // Gửi API lấy mã OTP ở đây
    }

    private void OnResetPasswordClicked(object sender, EventArgs e)
    {
        // Gọi API đặt lại mật khẩu ở đây
    }

    // 1. Tự động nhảy sang ô tiếp theo khi gõ chữ
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
            else if (entry == Otp6) NewPasswordEntry.Focus(); // Ô cuối nhảy thẳng xuống New Password
        }
    }

    // 2. Khi con trỏ trỏ vào ô nào -> Viền ô đó chuyển màu Tím
    private void OnOtpFocused(object sender, FocusEventArgs e)
    {
        var entry = sender as Entry;
        if (entry?.Parent is Border border)
        {
            border.Stroke = Color.FromArgb("#8B5CF6");
            border.StrokeThickness = 3;
        }
    }

    // 3. Khi con trỏ rời đi -> Viền trả về Xanh lợt
    private void OnOtpUnfocused(object sender, FocusEventArgs e)
    {
        var entry = sender as Entry;
        if (entry?.Parent is Border border)
        {
            border.Stroke = Color.FromArgb("#06B6D4");
            border.StrokeThickness = 2;
        }
    }

    // 4. Bấm chữ Log In lùi về trang trước
    private async void OnLoginTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}