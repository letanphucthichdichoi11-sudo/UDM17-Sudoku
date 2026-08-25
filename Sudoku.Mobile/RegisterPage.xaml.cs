namespace Sudoku.Mobile;

using Sudoku.Mobile.Network;
using Sudoku.Mobile.Models;

public partial class RegisterPage : ContentPage
{
    private readonly ApiClient _apiClient = new ApiClient();
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
            await DisplayAlertAsync("Lỗi", "Vui lòng nhập đầy đủ thông tin để đăng ký nhé!", "OK");
            return;
        }

        // Hiển thị một vòng xoay loading ở đây nếu bạn muốn giao diện mượt hơn

        // 2. Gói dữ liệu để gửi xuống Backend
        var request = new RegisterRequest
        {
            Username = UsernameEntry.Text,
            Email = EmailEntry.Text,
            Password = PasswordEntry.Text
        };

        // 3. Bắn dữ liệu xuống API /api/auth/register
        var response = await _apiClient.PostAsync<RegisterRequest, BaseAuthResponse>("api/auth/register", request);

        // 4. Xử lý kết quả Backend trả về
        if (response != null && response.Success)
        {
            RegisterFormLayout.IsVisible = false;
            OtpFormLayout.IsVisible = true;

            await DisplayAlertAsync("Thành công", "Mã OTP đã được gửi đến email của bạn!", "OK");
        }
        else
        {
            await DisplayAlertAsync("Đăng ký thất bại", response?.Message ?? "Không thể kết nối đến máy chủ", "OK");
        }
    }
    private async void OnLoginTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
    private async void OnVerifyOtpClicked(object sender, EventArgs e)
    {
        // 1. Gom 6 số OTP từ 6 ô nhập liệu thành 1 chuỗi
        string otpCode = $"{Otp1.Text}{Otp2.Text}{Otp3.Text}{Otp4.Text}{Otp5.Text}{Otp6.Text}";

        if (otpCode.Length < 6)
        {
            await DisplayAlert("Thiếu thông tin", "Vui lòng nhập đủ 6 số OTP!", "OK");
            return;
        }

        // 2. Gom dữ liệu gửi đi (Lấy lại chính Email mà người dùng vừa nhập ở form trước)
        var verifyData = new VerifyOtpRequest
        {
            Email = EmailEntry.Text,
            Otp = otpCode
        };

        // 3. Gọi Backend xác thực
        var apiClient = new ApiClient();
        var response = await apiClient.PostAsync<VerifyOtpRequest, BaseAuthResponse>("api/auth/verify-otp", verifyData);

        // 4. Xử lý kết quả trả về
        if (response != null && response.Success)
        {
            await DisplayAlertAsync("Thành công", "Xác thực tài khoản thành công!", "Tuyệt vời");

            // Đóng trang hiện tại, lùi về trang Login
            await Shell.Current.GoToAsync("..");
        }
        else
        {
            string errorMsg = response != null ? response.Message : "Lỗi kết nối máy chủ";
            await DisplayAlertAsync("Xác thực thất bại", errorMsg, "Thử lại");
        }
    }
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
        border.Stroke = Color.FromArgb("#06B6D4"); 
        border.StrokeThickness = 2; 
    }
}