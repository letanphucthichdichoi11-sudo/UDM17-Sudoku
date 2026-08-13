using Sudoku.Mobile.Models;
using Sudoku.Mobile.Network;

namespace Sudoku.Mobile.Services;

public class LobbyService
{
    private readonly ApiClient _apiClient;

    public LobbyService(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    // ============================
    // LẤY DANH SÁCH ROOM
    // ============================

    public async Task<List<LobbyRoom>> GetRoomsAsync()
    {
        var rooms = await _apiClient.GetAsync<List<LobbyRoom>>(
            "api/lobby/rooms");

        return rooms ?? new List<LobbyRoom>();
    }


    // ============================
    // TẠO ROOM
    // ============================

    public async Task<LobbyRoom?> CreateRoomAsync(
        string playerId,
        string roomName)
    {
        var request = new CreateRoomRequest
        {
            PlayerId = playerId,
            RoomName = roomName
        };

        return await _apiClient.PostAsync<CreateRoomRequest, LobbyRoom>(
            "api/lobby/rooms",
            request);
    }


    // ============================
    // JOIN ROOM
    // ============================

    public async Task<LobbyRoom?> JoinRoomAsync(
        string roomId,
        string playerId)
    {
        var request = new JoinRoomRequest
        {
            PlayerId = playerId
        };

        return await _apiClient.PostAsync<JoinRoomRequest, LobbyRoom>(
            $"api/lobby/rooms/{roomId}/join",
            request);
    }


    // ============================
    // LEAVE ROOM
    // ============================

    public async Task<bool> LeaveRoomAsync(
        string roomId,
        string playerId)
    {
        var request = new LeaveRoomRequest
        {
            PlayerId = playerId
        };

        var result = await _apiClient.PostAsync<LeaveRoomRequest, ApiResult>(
            $"api/lobby/rooms/{roomId}/leave",
            request);

        return result?.Success == true;
    }


    // ============================
    // KIỂM TRA SERVER
    // ============================

    public async Task<bool> CheckServerAsync()
    {
        return await _apiClient.CheckConnectionAsync();
    }
}


// ========================================
// REQUEST MODELS
// ========================================

public class CreateRoomRequest
{
    public string PlayerId { get; set; } = string.Empty;

    public string RoomName { get; set; } = string.Empty;
}


public class JoinRoomRequest
{
    public string PlayerId { get; set; } = string.Empty;
}


public class LeaveRoomRequest
{
    public string PlayerId { get; set; } = string.Empty;
}


public class ApiResult
{
    public bool Success { get; set; }

    public string? Message { get; set; }
}