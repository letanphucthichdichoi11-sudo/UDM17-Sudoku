namespace Sudoku.Mobile.Models;

public class LobbyPlayer
{
    public string PlayerId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public int Level { get; set; }

    public int Rating { get; set; }

    public bool IsReady { get; set; }

    public bool IsOnline { get; set; }
}