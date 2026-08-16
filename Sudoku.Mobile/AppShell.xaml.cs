using Sudoku.Mobile.Views;

namespace Sudoku.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(
            nameof(FindingOpponentPage),
            typeof(Views.FindingOpponentPage));

        Routing.RegisterRoute(
            nameof(OpponentFoundPage),
            typeof(Views.OpponentFoundPage));

        Routing.RegisterRoute(
            nameof(SudokuBattlePage),
            typeof(Views.SudokuBattlePage));
    }
}