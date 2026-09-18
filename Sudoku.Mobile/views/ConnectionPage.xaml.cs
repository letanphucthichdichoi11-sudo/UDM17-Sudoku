using Sudoku.Mobile.ViewModels;

namespace Sudoku.Mobile.Views;

public partial class ConnectionPage : ContentPage
{
    private readonly ConnectionViewModel _viewModel;

    public ConnectionPage()
    {
        InitializeComponent();

        _viewModel = new ConnectionViewModel();

        BindingContext = _viewModel;
    }

    private async void CheckConnection_Clicked(
        object sender,
        EventArgs e)
    {
        await _viewModel.CheckConnectionAsync();
    }
}