using Sudoku.Mobile.Services;
using Sudoku.Mobile.ViewModels;

namespace Sudoku.Mobile.Views;

public partial class LobbyPage : ContentPage
{
    private readonly LobbyViewModel _viewModel;

    public LobbyPage()
    {
        InitializeComponent();

        var apiClient = new Network.ApiClient();

        var lobbyService = new LobbyService(apiClient);

        _viewModel = new LobbyViewModel(lobbyService);

        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.LoadRoomsAsync();
    }

    private async void CreateRoom_Clicked(
        object? sender,
        EventArgs e)
    {
        string? roomName = await DisplayPromptAsync(
            "Create Room",
            "Nhập tên phòng:");

        if (string.IsNullOrWhiteSpace(roomName))
            return;

        // Tạm thời dùng Player ID mẫu.
        // Sau này sẽ lấy từ tài khoản đăng nhập.
        string playerId = "player-001";

        var room = await _viewModel.CreateRoomAsync(
            playerId,
            roomName);

        if (room == null)
        {
            await DisplayAlertAsync(
                "Lỗi",
                "Không thể tạo phòng.",
                "OK");

            return;
        }

        await DisplayAlertAsync(
            "Thành công",
            $"Đã tạo phòng: {roomName}",
            "OK");
    }

    private async void JoinRoom_Clicked(
        object? sender,
        EventArgs e)
    {
        if (sender is not Button button)
            return;

        if (button.CommandParameter is not Models.LobbyRoom room)
            return;

        string playerId = "player-001";

        var result = await _viewModel.JoinRoomAsync(
            room.RoomId,
            playerId);

        if (result == null)
        {
            await DisplayAlertAsync(
                "Lỗi",
                "Không thể tham gia phòng.",
                "OK");

            return;
        }

        await DisplayAlertAsync(
            "Thành công",
            $"Đã tham gia phòng {room.RoomName}",
            "OK");
    }
}