namespace Sudoku.Mobile.Models;

public class ConnectionStatus
{
    public bool IsConnected { get; set; }

    public string Status { get; set; } = "Disconnected";

    public string Message { get; set; } = "";
}