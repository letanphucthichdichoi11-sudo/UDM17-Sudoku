namespace Sudoku.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Đăng ký Route điều hướng
        Routing.RegisterRoute("RegisterPage", typeof(RegisterPage));
        Routing.RegisterRoute("ForgotPasswordPage", typeof(ForgotPasswordPage));
    }
}