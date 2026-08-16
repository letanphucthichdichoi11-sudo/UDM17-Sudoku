namespace Sudoku.Mobile.Views;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
    }

    private async void FindOpponent_Clicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(FindingOpponentPage));
    }
}