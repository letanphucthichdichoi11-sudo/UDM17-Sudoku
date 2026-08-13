namespace Sudoku.Mobile.Models;

public class LobbyRoom
{
    public string RoomId { get; set; } = string.Empty;

    public string RoomName { get; set; } = string.Empty;

    public LobbyPlayer? Player1 { get; set; }

    public LobbyPlayer? Player2 { get; set; }

    public bool HasActiveMatch { get; set; }

    public int PlayerCount
    {
        get
        {
            int count = 0;

            if (Player1 != null)
                count++;

            if (Player2 != null)
                count++;

            return count;
        }
    }

    public bool IsFull => PlayerCount >= 2;

    public string PlayerStatus => $"{PlayerCount}/2 PLAYERS";

    public string MatchStatus =>
        HasActiveMatch ? "MATCH ACTIVE" : "WAITING";
}