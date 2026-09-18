using System;
using Sudoku.Mobile.Network;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Views;

public partial class HistoryPage : ContentPage
{
    private readonly TcpGameClient _client;

    public HistoryPage()
    {
        InitializeComponent();

        _client = TcpGameClient.Shared;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await LoadHistoryAsync();
    }

    private async void Refresh_Clicked(
        object sender,
        EventArgs e)
    {
        await LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            if (!_client.IsConnected)
            {
                await DisplayAlert(
                    "Connection",
                    "Game Server connection is not available.",
                    "OK");

                return;
            }

            MatchHistoryResponse response =
                await _client.RequestAsync<
                    EmptyPayload,
                    MatchHistoryResponse>(
                        MessageType.GetMatchHistory,
                        new EmptyPayload());

            HistoryCollection.ItemsSource =
                response?.Matches ??
                new List<MatchHistoryItem>();
        }
        catch (TimeoutException)
        {
            await DisplayAlert(
                "History",
                "Server response timed out.",
                "OK");
        }
        catch (Exception exception)
        {
            await DisplayAlert(
                "History",
                "Could not load match history.\n\n" +
                exception.Message,
                "OK");
        }
    }
}