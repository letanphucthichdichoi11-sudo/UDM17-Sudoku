namespace Sudoku.Mobile.Views;

public partial class OpponentFoundPage : ContentPage
{
    public OpponentFoundPage()
    {
        InitializeComponent();
    }

    private async void StartBattle_Clicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(
            nameof(SudokuBattlePage));
    }
}