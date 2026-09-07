using System.Collections.ObjectModel;
using Sudoku.Mobile.Models;
using Sudoku.Mobile.Services;
using Sudoku.Shared.Models;

namespace Sudoku.Mobile.ViewModels;

public class LobbyViewModel
{
    private readonly LobbyService _lobbyService;

    public ObservableCollection<LobbyRoom> Rooms { get; } = new();

    public bool IsLoading { get; private set; }

    public LobbyViewModel(LobbyService lobbyService)
    {
        _lobbyService = lobbyService;
        _lobbyService.RoomsUpdated += OnRoomsUpdated;
    }

    private void OnRoomsUpdated(
        object? sender,
        List<LobbyRoom> rooms)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Rooms.Clear();

            foreach (LobbyRoom room in rooms)
            {
                Rooms.Add(room);
            }
        });
    }

    public async Task LoadRoomsAsync()
    {
        try
        {
            IsLoading = true;

            var rooms = await _lobbyService.GetRoomsAsync();

            Rooms.Clear();

            foreach (var room in rooms)
            {
                Rooms.Add(room);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Load Rooms Error: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<LobbyRoom?> CreateRoomAsync(
        string playerId,
        string roomName)
    {
        try
        {
            var room = await _lobbyService.CreateRoomAsync(
                playerId,
                roomName);

            if (room != null)
            {
                Rooms.Add(room);
            }

            return room;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Create Room Error: {ex.Message}");

            return null;
        }
    }

    public async Task<LobbyRoom?> JoinRoomAsync(
        string roomId,
        string playerId)
    {
        try
        {
            return await _lobbyService.JoinRoomAsync(
                roomId,
                playerId);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Join Room Error: {ex.Message}");

            return null;
        }
    }

    public async Task<bool> LeaveRoomAsync(
        string roomId,
        string playerId)
    {
        try
        {
            return await _lobbyService.LeaveRoomAsync(
                roomId,
                playerId);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Leave Room Error: {ex.Message}");

            return false;
        }
    }

    // ============================
    // QUICK MATCH
    // ============================

    public async Task<MatchStatusResponse?> QuickMatchAsync(
        string playerId,
        string playerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _lobbyService.QuickMatchAsync(
                playerId,
                playerName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"Quick Match Error: {ex.Message}");

            return null;
        }
    }
}