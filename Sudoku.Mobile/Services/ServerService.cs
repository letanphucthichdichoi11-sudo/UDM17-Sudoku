using Sudoku.Mobile.Network;

namespace Sudoku.Mobile.Services;

public sealed class ServerService
{
    private readonly TcpGameClient _client = TcpGameClient.Shared;

    public async Task<bool> ConnectAsync(string playerId, string playerName)
    {
        try
        {
            await _client.ConnectAsync(playerId, playerName);
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine("TCP connection error: " + exception.Message);
            return false;
        }
    }
}
