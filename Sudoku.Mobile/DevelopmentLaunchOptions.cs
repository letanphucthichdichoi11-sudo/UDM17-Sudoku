using Sudoku.Mobile.Models;

namespace Sudoku.Mobile;

internal static class DevelopmentLaunchOptions
{
#if DEBUG
    private const string DevelopmentPassword = "SudokuTest!2026";
    private static readonly string? Player = GetArgumentValue("--dev-player=");

    internal static bool AutoQuickMatch =>
        Environment.GetCommandLineArgs().Any(
            argument => string.Equals(
                argument,
                "--dev-quick-match",
                StringComparison.OrdinalIgnoreCase));

    internal static bool RunBoardProbe =>
        Environment.GetCommandLineArgs().Any(
            argument => string.Equals(
                argument,
                "--dev-board-probe",
                StringComparison.OrdinalIgnoreCase));

    internal static bool ForceWin => HasArgument("--dev-force-win");
    internal static bool AutoHomeFromResult => HasArgument("--dev-result-home");
    internal static bool OpenSavedResult => HasArgument("--dev-open-last-result");
    internal static bool OpenCreateRoom => HasArgument("--dev-open-create-room");

    internal static bool TryGetLogin(out string username, out string password)
    {
        username = Player ?? string.Empty;
        password = DevelopmentPassword;

        return username is "test_player_a" or "test_player_b";
    }

    internal static void MarkBoardReady(Guid matchId)
    {
        if (Player == null)
            return;

        string directory = Path.Combine(
            Path.GetTempPath(),
            "sudoku-dev-launch");
        Directory.CreateDirectory(directory);

        string markerPath = Path.Combine(
            directory,
            $"{Player}.ready.txt");
        File.WriteAllText(
            markerPath,
            $"player={Player}{Environment.NewLine}" +
            $"matchId={matchId}{Environment.NewLine}" +
            $"cellCount=81{Environment.NewLine}" +
            $"readyAt={DateTimeOffset.Now:O}{Environment.NewLine}");
    }

    internal static void MarkLobbyReady()
    {
        if (Player == null)
            return;

        string directory = Path.Combine(Path.GetTempPath(), "sudoku-dev-launch");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{Player}.lobby.txt"),
            $"player={Player}{Environment.NewLine}" +
            $"route=//LobbyPage{Environment.NewLine}" +
            $"readyAt={DateTimeOffset.Now:O}{Environment.NewLine}");
    }

    internal static async Task CaptureScreenshotAsync(string? suffix = null)
    {
        if (Player == null)
            return;

        try
        {
            Microsoft.Maui.Media.IScreenshotResult screenshot =
                await Microsoft.Maui.Media.Screenshot.Default.CaptureAsync();
            await using Stream source = await screenshot.OpenReadAsync(
                Microsoft.Maui.Media.ScreenshotFormat.Png,
                100);
            string directory = Path.Combine(
                Path.GetTempPath(),
                "sudoku-dev-launch");
            Directory.CreateDirectory(directory);
            string fileName = String.IsNullOrWhiteSpace(suffix)
                ? $"{Player}.screen.png"
                : $"{Player}.{suffix}.png";
            await using FileStream destination = File.Create(
                Path.Combine(directory, fileName));
            await source.CopyToAsync(destination);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[DEV SCREENSHOT] {exception}");
        }
    }

    internal static void MarkBoardProbe(IEnumerable<string> results)
    {
        if (Player == null)
            return;

        string directory = Path.Combine(
            Path.GetTempPath(),
            "sudoku-dev-launch");
        Directory.CreateDirectory(directory);
        File.WriteAllLines(
            Path.Combine(directory, $"{Player}.board-probe.txt"),
            results);
    }

    internal static void MarkResultReady(string resultName, DuelResultData? result)
    {
        if (Player == null || result == null)
            return;

        string directory = Path.Combine(Path.GetTempPath(), "sudoku-dev-launch");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{Player}.{resultName}.txt"),
            $"player={Player}{Environment.NewLine}" +
            $"matchId={result.MatchId}{Environment.NewLine}" +
            $"result={resultName}{Environment.NewLine}" +
            $"winnerPlayerId={result.WinnerPlayerId}{Environment.NewLine}" +
            $"opponent={result.OpponentName}{Environment.NewLine}" +
            $"completionTime={result.CompletionTime:c}{Environment.NewLine}" +
            $"readyAt={DateTimeOffset.Now:O}{Environment.NewLine}");
    }

    internal static void MarkHomeReady(Guid matchId)
    {
        if (Player == null)
            return;

        string directory = Path.Combine(Path.GetTempPath(), "sudoku-dev-launch");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{Player}.home.txt"),
            $"player={Player}{Environment.NewLine}" +
            $"previousMatchId={matchId}{Environment.NewLine}" +
            $"route=//LobbyPage{Environment.NewLine}" +
            $"readyAt={DateTimeOffset.Now:O}{Environment.NewLine}");
    }

    private static bool HasArgument(string expected) =>
        Environment.GetCommandLineArgs().Any(argument =>
            String.Equals(argument, expected, StringComparison.OrdinalIgnoreCase));

    private static string? GetArgumentValue(string prefix)
    {
        string? argument = Environment.GetCommandLineArgs()
            .FirstOrDefault(value => value.StartsWith(
                prefix,
                StringComparison.OrdinalIgnoreCase));

        return argument?[prefix.Length..].Trim().ToLowerInvariant();
    }
#else
    internal static bool AutoQuickMatch => false;
    internal static bool RunBoardProbe => false;
    internal static bool ForceWin => false;
    internal static bool AutoHomeFromResult => false;
    internal static bool OpenSavedResult => false;
    internal static bool OpenCreateRoom => false;

    internal static bool TryGetLogin(out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;
        return false;
    }

    internal static void MarkBoardReady(Guid matchId)
    {
    }
    internal static void MarkLobbyReady() { }

    internal static Task CaptureScreenshotAsync(string? suffix = null) => Task.CompletedTask;
    internal static void MarkBoardProbe(IEnumerable<string> results) { }
    internal static void MarkResultReady(string resultName, DuelResultData? result) { }
    internal static void MarkHomeReady(Guid matchId) { }
#endif
}
