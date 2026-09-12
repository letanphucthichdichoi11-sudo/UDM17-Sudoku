using Sudoku.Mobile.Network;
using Sudoku.Mobile.Models;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile;

public partial class LoginPage : ContentPage
{
    private readonly ApiClient _apiClient;

    private int _failedAttempts = 0;
    private DateTime _lockoutEndTime = DateTime.MinValue;
    private bool _isLoggingIn;
    private bool _developmentLoginStarted;

    public LoginPage()
    {
        InitializeComponent();
        _apiClient = new ApiClient();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_developmentLoginStarted ||
            !DevelopmentLaunchOptions.TryGetLogin(
                out string username,
                out string password))
            return;

        _developmentLoginStarted = true;
        UsernameEntry.Text = username;
        PasswordEntry.Text = password;
        OnLoginClicked(LoginBtn, EventArgs.Empty);
    }

    private async void OnLoginClicked(object sender, EventArgs e)
    {
        if (_isLoggingIn)
            return;

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

        _isLoggingIn = true;
        LoginBtn.IsEnabled = false;
        LoginBtn.Text = "Đang kết nối...";

        try
        {
            var request = new LoginRequest
            {
                UsernameOrEmail = username,
                Password = password
            };
            ApiResult<LoginResponse> result =
                await _apiClient.PostDetailedAsync<LoginRequest, LoginResponse>(
                    "api/auth/login",
                    request);

            if (!result.IsSuccess)
            {
                await DisplayAlertAsync(
                    "Đăng nhập thất bại",
                    GetLoginErrorMessage(result.ErrorKind),
                    "Thử lại");
                return;
            }

            LoginResponse response = result.Value!;
            if (!response.Success)
            {
                _failedAttempts++;
                if (_failedAttempts >= 5)
                {
                    _lockoutEndTime = DateTime.Now.AddMinutes(1);
                    await DisplayAlertAsync(
                        "Cảnh báo",
                        "Sai thông tin quá 5 lần. Vui lòng chờ 1 phút để thử lại.",
                        "OK");
                }
                else
                {
                    string message = response.Message?.Contains(
                        "Sai tài khoản",
                        StringComparison.OrdinalIgnoreCase) == true
                        ? "Thông tin đăng nhập không chính xác."
                        : response.Message ?? "Thông tin đăng nhập không chính xác.";
                    await DisplayAlertAsync(
                        "Đăng nhập thất bại",
                        message,
                        "Thử lại");
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(response.AccessToken))
            {
                Console.WriteLine("[LOGIN] Success response has no access token.");
                await DisplayAlertAsync(
                    "Đăng nhập thất bại",
                    "Đã xảy ra lỗi khi đăng nhập.",
                    "Thử lại");
                return;
            }

            _failedAttempts = 0;
            await SecureStorage.Default.SetAsync(
                "AccessToken",
                response.AccessToken);
            if (!string.IsNullOrEmpty(response.RefreshToken))
                await SecureStorage.Default.SetAsync(
                    "RefreshToken",
                    response.RefreshToken);

            string normalizedUsername = username.ToLowerInvariant();
            string playerIdKey = "sudoku-player-id-" + normalizedUsername;
            string? playerId = Preferences.Default.Get<string?>(playerIdKey, null);
            if (string.IsNullOrWhiteSpace(playerId))
            {
                playerId = "player-" + Guid.NewGuid().ToString("N");
                Preferences.Default.Set(playerIdKey, playerId);
            }

            Preferences.Default.Set("Username", username);

            if (DevelopmentLaunchOptions.OpenSavedResult &&
                DuelResultSession.GetCurrent(playerId) is { } savedResult)
            {
                string resultRoute = savedResult.IsVictory
                    ? nameof(VictoryResultPage)
                    : nameof(DefeatResultPage);
                await Shell.Current.GoToAsync(resultRoute);
                return;
            }

            try
            {
                await TcpGameClient.Shared.ConnectAsync(playerId, username);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGIN] TCP connection failed: {ex}");
                await DisplayAlertAsync(
                    "Lỗi kết nối Game Server",
                    "Đăng nhập thành công nhưng không thể kết nối Game Server. " +
                    "Hãy kiểm tra Sudoku.Server đang chạy ở cổng 5000.",
                    "OK");
                return;
            }

            await Shell.Current.GoToAsync("//LobbyPage");
            if (DevelopmentLaunchOptions.OpenCreateRoom)
                await Shell.Current.GoToAsync(nameof(CreateRoomPage));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LOGIN] Unexpected error: {ex}");
            await DisplayAlertAsync(
                "Đăng nhập thất bại",
                "Đã xảy ra lỗi khi đăng nhập.",
                "Thử lại");
        }
        finally
        {
            _isLoggingIn = false;
            LoginBtn.IsEnabled = true;
            LoginBtn.Text = "Đăng nhập";
        }
    }

    private static string GetLoginErrorMessage(ApiErrorKind errorKind)
    {
        return errorKind switch
        {
            ApiErrorKind.Connection =>
                "Không thể kết nối đến máy chủ. Vui lòng kiểm tra lại kết nối.",
            ApiErrorKind.Timeout =>
                "Máy chủ phản hồi quá lâu. Vui lòng thử lại.",
            ApiErrorKind.Unauthorized =>
                "Thông tin đăng nhập không chính xác.",
            ApiErrorKind.Forbidden =>
                "Tài khoản không có quyền truy cập.",
            ApiErrorKind.Server =>
                "Máy chủ đang gặp lỗi. Vui lòng thử lại sau.",
            _ => "Đã xảy ra lỗi khi đăng nhập."
        };
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
