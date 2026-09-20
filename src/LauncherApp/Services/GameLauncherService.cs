using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace LauncherApp.Services;

/// <summary>
/// Finds and launches Rocket League whether it was installed via Epic Games
/// or Steam.  Does not depend on BakkesMod or any injected plugin.
///
/// The launch strategy:
///   - Epic: launch via com.epicgames.launcher protocol URI so Epic handles
///     account validation, then poll until the game process appears.
///   - Steam: launch via steam:// URI (steam://rungameid/252950).
///
/// After launch we expose the PID so the UI can show "Game running" status.
/// </summary>
public sealed class GameLauncherService
{
    // Rocket League's Steam App ID.
    private const string SteamAppId = "252950";

    // Epic Games Store game ID / namespace.
    private const string EpicAppId = "Sugar";           // product slug
    private const string EpicNamespace = "9773aa1aa54f4f89b5838944d35f13f";

    public enum InstallSource { Unknown, Steam, Epic }

    public InstallSource Source { get; private set; } = InstallSource.Unknown;

    /// <summary>
    /// Detects where Rocket League is installed and returns the source, or
    /// InstallSource.Unknown if it can't be found.
    /// </summary>
    public InstallSource DetectInstall()
    {
        if (TryFindSteamExecutable(out _))
        {
            Source = InstallSource.Steam;
            return Source;
        }

        if (TryFindEpicExecutable(out _))
        {
            Source = InstallSource.Epic;
            return Source;
        }

        Source = InstallSource.Unknown;
        return Source;
    }

    /// <summary>
    /// Launches Rocket League and returns the Process object (or null if
    /// launch failed).  Does not wait for the game to fully load.
    /// </summary>
    public Process? Launch()
    {
        // Auto-detect if not done yet.
        if (Source == InstallSource.Unknown)
            DetectInstall();

        return Source switch
        {
            InstallSource.Steam => LaunchViaSteam(),
            InstallSource.Epic  => LaunchViaEpic(),
            _                   => null
        };
    }

    // -----------------------------------------------------------------------
    // Steam path
    // -----------------------------------------------------------------------

    private static bool TryFindSteamExecutable(out string path)
    {
        // Check the Steam registry key for the install path.
        const string steamKey = @"SOFTWARE\WOW6432Node\Valve\Steam";
        using var key = Registry.LocalMachine.OpenSubKey(steamKey);

        var steamPath = key?.GetValue("InstallPath") as string;
        if (steamPath is null)
        {
            // 64-bit Steam on 64-bit OS sometimes uses the non-WOW6432 path.
            using var key64 = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam");
            steamPath = key64?.GetValue("InstallPath") as string;
        }

        if (steamPath is null) { path = string.Empty; return false; }

        // Common library location for Rocket League via Steam.
        var candidate = Path.Combine(steamPath, "steamapps", "common",
            "rocketleague", "Binaries", "Win64", "RocketLeague.exe");

        if (File.Exists(candidate)) { path = candidate; return true; }

        path = string.Empty;
        return false;
    }

    private static Process? LaunchViaSteam()
    {
        // steam://rungameid/<appId> hands off to Steam which does DRM validation.
        Process.Start(new ProcessStartInfo
        {
            FileName = $"steam://rungameid/{SteamAppId}",
            UseShellExecute = true
        });

        // Steam starts an intermediate launcher; poll for the actual game process.
        return WaitForGameProcess("RocketLeague", TimeSpan.FromSeconds(60));
    }

    // -----------------------------------------------------------------------
    // Epic path
    // -----------------------------------------------------------------------

    private static bool TryFindEpicExecutable(out string path)
    {
        // Epic stores manifests in %ProgramData%\Epic\EpicGamesLauncher\Data\Manifests
        var manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestDir)) { path = string.Empty; return false; }

        foreach (var file in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("RocketLeague", StringComparison.OrdinalIgnoreCase)) continue;

            // Very small JSON parse: find "InstallLocation" value.
            var startMarker = "\"InstallLocation\": \"";
            var start = text.IndexOf(startMarker, StringComparison.Ordinal);
            if (start < 0) continue;

            start += startMarker.Length;
            var end = text.IndexOf('"', start);
            if (end < 0) continue;

            var installPath = text[start..end].Replace("\\\\", "\\");
            var exe = Path.Combine(installPath, "Binaries", "Win64", "RocketLeague.exe");

            if (File.Exists(exe)) { path = exe; return true; }
        }

        path = string.Empty;
        return false;
    }

    private static Process? LaunchViaEpic()
    {
        // com.epicgames.launcher://apps/<namespace>%3A<appId> opens the game
        // via the Epic Launcher which handles entitlement / EAC.
        var uri = $"com.epicgames.launcher://apps/{EpicNamespace}%3A{EpicAppId}?action=launch&silent=true";

        Process.Start(new ProcessStartInfo
        {
            FileName = uri,
            UseShellExecute = true
        });

        return WaitForGameProcess("RocketLeague", TimeSpan.FromSeconds(90));
    }

    // -----------------------------------------------------------------------
    // Shared helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Polls Process.GetProcessesByName every 2s until the named process
    /// appears or the timeout expires.
    /// </summary>
    private static Process? WaitForGameProcess(string processName, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var procs = Process.GetProcessesByName(processName);
            if (procs.Length > 0)
                return procs[0];

            Thread.Sleep(2000);
        }

        return null;
    }
}
