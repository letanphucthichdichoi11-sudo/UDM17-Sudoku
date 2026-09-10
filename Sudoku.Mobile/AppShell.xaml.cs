namespace Sudoku.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Đăng ký Route điều hướng
        Routing.RegisterRoute("RegisterPage", typeof(RegisterPage));
        Routing.RegisterRoute("ForgotPasswordPage", typeof(ForgotPasswordPage));
        Routing.RegisterRoute(nameof(SudokuPage), typeof(SudokuPage));
        Routing.RegisterRoute(nameof(VictoryResultPage), typeof(VictoryResultPage));
        Routing.RegisterRoute(nameof(DefeatResultPage), typeof(DefeatResultPage));
        Routing.RegisterRoute(nameof(CreateRoomPage), typeof(CreateRoomPage));
    }
}
