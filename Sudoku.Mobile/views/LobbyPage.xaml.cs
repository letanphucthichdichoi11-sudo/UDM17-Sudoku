using Sudoku.Mobile.Network;
using Sudoku.Mobile.Services;
using Sudoku.Mobile.ViewModels;
using Sudoku.Mobile.views;
using Sudoku.Shared.Models;

namespace Sudoku.Mobile.Views;

public partial class LobbyPage : ContentPage
{
    private readonly LobbyViewModel _viewModel;

    public LobbyPage()
    {
        InitializeComponent();

        var lobbyService =
            new LobbyService(TcpGameClient.Shared);

        _viewModel =
            new LobbyViewModel(lobbyService);

        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _viewModel.LoadRoomsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[LOBBY] Load rooms error: {ex}");
        }
    }

    // =========================================================
    // CREATE ROOM
    // =========================================================

    private async void CreateRoom_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            string? roomName =
                await DisplayPromptAsync(
                    "Create Room",
                    "Nhập tên phòng:");

            if (string.IsNullOrWhiteSpace(roomName))
                return;

            string playerId =
                TcpGameClient.Shared.PlayerId
                ?? throw new InvalidOperationException(
                    "TCP chưa có PlayerId.");

            var room =
                await _viewModel.CreateRoomAsync(
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
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[CREATE ROOM ERROR] {ex}");

            await DisplayAlertAsync(
                "Lỗi",
                ex.Message,
                "OK");
        }
    }

    // =========================================================
    // JOIN ROOM
    // =========================================================

    private async void JoinRoom_Clicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            if (sender is not Button button)
                return;

            if (button.CommandParameter
                is not Models.LobbyRoom room)
                return;

            string playerId =
                TcpGameClient.Shared.PlayerId
                ?? throw new InvalidOperationException(
                    "TCP chưa có PlayerId.");

            var result =
                await _viewModel.JoinRoomAsync(
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
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[JOIN ROOM ERROR] {ex}");

            await DisplayAlertAsync(
                "Lỗi",
                ex.Message,
                "OK");
        }
    }

    // =========================================================
    // QUICK MATCH
    // =========================================================

    private async void OnQuickMatchClicked(
        object? sender,
        EventArgs e)
    {
        Button? button =
            sender as Button;

        try
        {
            if (button != null)
            {
                button.IsEnabled = false;
                button.Text = "SEARCHING...";
            }

            // -------------------------------------------------
            // KIỂM TRA TCP
            // -------------------------------------------------

            if (!TcpGameClient.Shared.IsConnected)
            {
                await DisplayAlertAsync(
                    "Lỗi kết nối",
                    "Chưa kết nối đến Game Server.",
                    "OK");

                return;
            }

            // -------------------------------------------------
            // LẤY PLAYER ID
            // -------------------------------------------------

            string playerId =
                TcpGameClient.Shared.PlayerId
                ?? throw new InvalidOperationException(
                    "TCP chưa có PlayerId.");

            string playerName =
                Preferences.Default.Get(
                    "Username",
                    playerId);

            Console.WriteLine(
                "======================================");

            Console.WriteLine(
                "[QUICK MATCH] START");

            Console.WriteLine(
                $"[QUICK MATCH] PlayerId: {playerId}");

            Console.WriteLine(
                $"[QUICK MATCH] PlayerName: {playerName}");

            // -------------------------------------------------
            // TÌM TRẬN
            // -------------------------------------------------

            MatchStatusResponse? match =
                await _viewModel.QuickMatchAsync(
                    playerId,
                    playerName);

            // -------------------------------------------------
            // KHÔNG TÌM THẤY
            // -------------------------------------------------

            if (match == null)
            {
                Console.WriteLine(
                    "[QUICK MATCH] Match = NULL");

                await DisplayAlertAsync(
                    "Không tìm thấy đối thủ",
                    "Hiện tại chưa có đối thủ hoặc " +
                    "không thể bắt đầu trận đấu.",
                    "OK");

                return;
            }

            // -------------------------------------------------
            // KIỂM TRA MATCH
            // -------------------------------------------------

            Console.WriteLine(
                $"[QUICK MATCH] MatchId: {match.MatchId}");

            Console.WriteLine(
                $"[QUICK MATCH] State: {match.State}");

            Console.WriteLine(
                $"[QUICK MATCH] Puzzle length: " +
                $"{match.Puzzle?.Length ?? 0}");

            // -------------------------------------------------
            // LƯU MATCH
            // -------------------------------------------------

            MatchSession.CurrentMatch =
                match;

            Console.WriteLine(
                "[QUICK MATCH] Match saved.");

            // -------------------------------------------------
            // CHUYỂN SANG SUDOKU
            // -------------------------------------------------

            await Shell.Current.GoToAsync(
                nameof(SudokuPage));

            Console.WriteLine(
                "[QUICK MATCH] Navigate SudokuPage.");

            Console.WriteLine(
                "======================================");
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "======================================");

            Console.WriteLine(
                "[QUICK MATCH ERROR]");

            Console.WriteLine(
                ex.ToString());

            Console.WriteLine(
                "======================================");

            await DisplayAlertAsync(
                "Lỗi Quick Match",
                ex.Message,
                "OK");
        }
        finally
        {
            if (button != null)
            {
                button.IsEnabled = true;
                button.Text =
                    "FIND OPPONENT  →";
            }
        }
    }
}