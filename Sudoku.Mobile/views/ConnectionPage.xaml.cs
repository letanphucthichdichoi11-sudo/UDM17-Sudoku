namespace Sudoku.Mobile.Views;

public partial class ConnectionPage : ContentPage
{
    public ConnectionPage()
    {
        InitializeComponent();
    }

    private async void CheckConnection_Clicked(object? sender, EventArgs e)
    {
        StatusLabel.Text = "Checking server connection...";

        await Task.Delay(1000);

        StatusLabel.Text = "Connection restored";
    }
}