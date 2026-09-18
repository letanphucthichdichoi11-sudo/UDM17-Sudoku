using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Sudoku.Mobile.Network;
using Sudoku.Shared.Models;

namespace Sudoku.Mobile.Models;

public sealed class DuelResultData
{
    public Guid MatchId { get; set; }
    public string CurrentPlayerId { get; set; } = String.Empty;
    public string WinnerPlayerId { get; set; } = String.Empty;
    public string OpponentName { get; set; } = "Rival";
    public bool IsVictory { get; set; }
    public TimeSpan CompletionTime { get; set; }
}

public static class DuelResultSession
{
    public static DuelResultData? Current { get; private set; }

    public static DuelResultData Create(
        MatchStatusResponse match,
        string currentPlayerId,
        string opponentName)
    {
        DateTime finishedAtUtc = match.FinishedAtUtc ?? match.ServerUtcNow;
        TimeSpan completionTime = match.StartedAtUtc.HasValue
            ? finishedAtUtc - match.StartedAtUtc.Value
            : TimeSpan.FromMinutes((int)match.Duration) - match.TimeLeft;
        if (completionTime < TimeSpan.Zero)
            completionTime = TimeSpan.Zero;

        var result = new DuelResultData
        {
            MatchId = match.MatchId,
            CurrentPlayerId = currentPlayerId,
            WinnerPlayerId = match.WinnerPlayerId ?? String.Empty,
            OpponentName = String.IsNullOrWhiteSpace(opponentName) ? "Rival" : opponentName,
            IsVictory = String.Equals(
                match.WinnerPlayerId,
                currentPlayerId,
                StringComparison.Ordinal),
            CompletionTime = completionTime
        };

        Current = result;
        string resultPath = GetResultPath(currentPlayerId);
        Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
        File.WriteAllText(resultPath, JsonSerializer.Serialize(result));
        return result;
    }

    public static DuelResultData? GetCurrent(string? playerId = null)
    {
        if (Current != null)
            return Current;

        playerId ??= TcpGameClient.Shared.PlayerId;
        if (String.IsNullOrWhiteSpace(playerId))
            return null;

        string resultPath = GetResultPath(playerId);
        if (!File.Exists(resultPath))
            return null;

        try
        {
            string json = File.ReadAllText(resultPath);
            Current = JsonSerializer.Deserialize<DuelResultData>(json);
        }
        catch (JsonException)
        {
            File.Delete(resultPath);
        }
        return Current;
    }

    public static void Clear()
    {
        string? playerId = Current?.CurrentPlayerId ?? TcpGameClient.Shared.PlayerId;
        if (!String.IsNullOrWhiteSpace(playerId))
        {
            string resultPath = GetResultPath(playerId);
            if (File.Exists(resultPath))
                File.Delete(resultPath);
        }
        Current = null;
    }

    private static string GetResultPath(string playerId)
    {
        byte[] playerHash = SHA256.HashData(Encoding.UTF8.GetBytes(playerId));
        return Path.Combine(
            FileSystem.AppDataDirectory,
            "duel-results",
            Convert.ToHexString(playerHash) + ".json");
    }
}
