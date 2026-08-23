using Sudoku.Mobile.Network;
using Sudoku.Mobile.Models;

namespace Sudoku.Mobile;

public partial class LoginPage : ContentPage
{
    private readonly ApiClient _apiClient;

    private int _failedAttempts = 0;
    private DateTime _lockoutEndTime = DateTime.MinValue;

    public LoginPage()
    {
        InitializeComponent();
        _apiClient = new ApiClient();
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        if (DateTime.Now < _lockoutEndTime)
        {
            int remain = (int)(_lockoutEndTime - DateTime.Now).TotalSeconds;
            await DisplayAlertAsync("Khóa tạm thời", $"Bạn đã nhập sai quá nhiều. Thử lại sau {remain} giây.", "OK");
            return;
        }

        string username = UsernameEntry.Text?.Trim();
        string password = PasswordEntry.Text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            await DisplayAlertAsync("Lỗi", "Vui lòng nhập đầy đủ Email/Username và Mật khẩu.", "OK");
            return;
        }

        LoginBtn.IsEnabled = false;
        LoginBtn.Text = "Đang kết nối...";

        var request = new LoginRequest { UsernameOrEmail = username, Password = password };
        var response = await _apiClient.PostAsync<LoginRequest, LoginResponse>("api/auth/login", request);

        if (response != null && response.Success)
        {
            _failedAttempts = 0;
            await SecureStorage.Default.SetAsync("AccessToken", response.AccessToken);
            if (!string.IsNullOrEmpty(response.RefreshToken))
            {
                await SecureStorage.Default.SetAsync("RefreshToken", response.RefreshToken);
            }
            await Shell.Current.GoToAsync("//LobbyPage");
        }
        else
        {
            _failedAttempts++;
            if (_failedAttempts >= 5)
            {
                _lockoutEndTime = DateTime.Now.AddMinutes(1);
                await DisplayAlertAsync("Cảnh báo", "Sai thông tin quá 5 lần. Vui lòng chờ 1 phút để thử lại.", "OK");
            }
            else
            {
                string errorMsg = response?.Message ?? "Không thể kết nối đến máy chủ. Vui lòng kiểm tra lại mạng!";
                await DisplayAlertAsync("Đăng nhập thất bại", errorMsg, "Thử lại");
            }
        }

        LoginBtn.IsEnabled = true;
        LoginBtn.Text = "Đăng nhập";
    }

    private async void OnRegisterTapped(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("RegisterPage");
    }

    private async void OnForgotPasswordTapped(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("ForgotPasswordPage");
    }
}