using Sudoku.Mobile.Models;
using Sudoku.Mobile.Network;
using Sudoku.Shared.Network;

namespace Sudoku.Mobile.Services;

public sealed class LobbyService
{
    private readonly TcpGameClient _client;

    public event EventHandler<List<LobbyRoom>>? RoomsUpdated;

    public LobbyService(TcpGameClient client)
    {
        _client = client;
        _client.EventReceived += OnEventReceived;
    }

    public async Task<List<LobbyRoom>> GetRoomsAsync()
    {
        RoomListResponse response = await _client.RequestAsync<EmptyPayload, RoomListResponse>(
            MessageType.ListRooms, new EmptyPayload());
        return response.Rooms.Select(MapRoom).ToList();
    }

    public async Task<LobbyRoom?> CreateRoomAsync(string playerId, string roomName)
    {
        LobbyRoomDto room = await _client.RequestAsync<CreateRoomRequest, LobbyRoomDto>(
            MessageType.CreateRoom,
            new CreateRoomRequest { RoomName = roomName });
        return MapRoom(room);
    }

    public async Task<LobbyRoom?> JoinRoomAsync(string roomId, string playerId)
    {
        LobbyRoomDto room = await _client.RequestAsync<RoomRequest, LobbyRoomDto>(
            MessageType.JoinRoom,
            new RoomRequest { RoomId = Guid.Parse(roomId) });
        return MapRoom(room);
    }

    public async Task<bool> LeaveRoomAsync(string roomId, string playerId)
    {
        await _client.RequestAsync<RoomRequest, EmptyPayload>(
            MessageType.LeaveRoom,
            new RoomRequest { RoomId = Guid.Parse(roomId) });
        return true;
    }

    public Task<bool> CheckServerAsync()
    {
        return Task.FromResult(_client.IsConnected);
    }

    private void OnEventReceived(object? sender, Message message)
    {
        if (message.Type != MessageType.RoomUpdated) return;
        RoomListResponse response = message.ReadPayload<RoomListResponse>();
        RoomsUpdated?.Invoke(this, response.Rooms.Select(MapRoom).ToList());
    }

    private static LobbyRoom MapRoom(LobbyRoomDto room)
    {
        return new LobbyRoom
        {
            RoomId = room.RoomId.ToString(),
            RoomName = room.RoomName,
            Player1 = room.Players.Count > 0 ? MapPlayer(room.Players[0]) : null,
            Player2 = room.Players.Count > 1 ? MapPlayer(room.Players[1]) : null,
            HasActiveMatch = room.HasActiveMatch
        };
    }

    private static LobbyPlayer MapPlayer(LobbyPlayerDto player)
    {
        return new LobbyPlayer
        {
            PlayerId = player.PlayerId,
            Username = player.PlayerName,
            IsOnline = true
        };
    }
}
