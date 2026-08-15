namespace Sudoku.Mobile;

public partial class RegisterPage : ContentPage
{
    public RegisterPage()
    {
        InitializeComponent();
    }


    private void OnPasswordTextChanged(object sender, TextChangedEventArgs e)
    {
        string password = e.NewTextValue ?? "";

        // 1. Chấm điểm mật khẩu
        int score = 0;
        if (password.Length >= 4)
            score++;

        if (password.Length >= 8 && System.Text.RegularExpressions.Regex.IsMatch(password, "[0-9]"))
            score++;

        if (password.Length >= 8 && System.Text.RegularExpressions.Regex.IsMatch(password, "[^a-zA-Z0-9]"))
            score++;

        if (score >= 1)
        {
            ImgNovice.Source = "novice_active_state.png";
            LblNovice.TextColor = Colors.White;
        }
        else
        {
            ImgNovice.Source = "novice_empty_state.png";
            LblNovice.TextColor = Color.FromArgb("#EF4444");
        }

        if (score >= 2)
        {
            ImgPro.Source = "pro_active_state.png";
            LblPro.TextColor = Colors.White;
        }
        else
        {
            ImgPro.Source = "pro_empty_state.png";
            LblPro.TextColor = Color.FromArgb("#EAB308");
        }

        if (score >= 3)
        {
            ImgMaster.Source = "master_active_state.png";
            LblMaster.TextColor = Colors.White;
        }
        else
        {
            ImgMaster.Source = "master_empty_state.png";
            LblMaster.TextColor = Color.FromArgb("#10B981");
        }
    }

    private async void OnRegisterClicked(object sender, EventArgs e)
    {
        // 1. Kiểm tra xem người dùng đã nhập đủ thông tin chưa
        if (string.IsNullOrWhiteSpace(UsernameEntry.Text) ||
            string.IsNullOrWhiteSpace(EmailEntry.Text) ||
            string.IsNullOrWhiteSpace(PasswordEntry.Text))
        {
            await DisplayAlert("Lỗi", "Vui lòng nhập đầy đủ thông tin để đăng ký nhé!", "OK");
            return;
        }

        // 2. Chỗ này sau này bạn sẽ viết code kết nối SQL hoặc gọi API Backend ở đây
        // (Tạm thời chúng ta giả lập app đang tải dữ liệu mất 1 giây)
        await Task.Delay(1000);

        // 3. Ảo thuật chuyển giao diện: Ẩn form Đăng ký, Hiện form OTP
        RegisterFormLayout.IsVisible = false;
        OtpFormLayout.IsVisible = true;
    }
    private async void OnLoginTapped(object sender, TappedEventArgs e)
    {
        // Lệnh ".." có nghĩa là đóng trang hiện tại lại và quay lùi về trang trước đó (Trang Login)
        await Shell.Current.GoToAsync("..");
    }
    private void OnVerifyOtpClicked(object sender, EventArgs e) { }
    private void OnResendOtpTapped(object sender, TappedEventArgs e) { }
    // 1. Tự động nhảy sang ô tiếp theo khi gõ chữ
    private void OnOtpTextChanged(object sender, TextChangedEventArgs e)
    {
        var entry = sender as Entry;

        // Nếu ô vừa gõ có chứa 1 con số, lập tức Focus sang ô kế tiếp
        if (!string.IsNullOrEmpty(e.NewTextValue))
        {
            if (entry == Otp1) Otp2.Focus();
            else if (entry == Otp2) Otp3.Focus();
            else if (entry == Otp3) Otp4.Focus();
            else if (entry == Otp4) Otp5.Focus();
            else if (entry == Otp5) Otp6.Focus();
        }
    }

    // 2. Khi con trỏ trỏ vào ô nào -> Viền ô đó chuyển màu Tím
    private void OnOtpFocused(object sender, FocusEventArgs e)
    {
        var entry = sender as Entry;
        var border = entry.Parent as Border;
        border.Stroke = Color.FromArgb("#8B5CF6"); // Màu tím
        border.StrokeThickness = 3; // Viền dày lên
    }

    // 3. Khi con trỏ rời đi -> Viền trả về Xanh lợt
    private void OnOtpUnfocused(object sender, FocusEventArgs e)
    {
        var entry = sender as Entry;
        var border = entry.Parent as Border;
        border.Stroke = Color.FromArgb("#06B6D4"); // Màu xanh cyan
        border.StrokeThickness = 2; // Viền mỏng lại
    }
}