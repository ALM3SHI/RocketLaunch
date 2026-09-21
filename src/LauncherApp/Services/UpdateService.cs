using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace LauncherApp.Services;

/// <summary>
/// Checks GitHub Releases for a newer version and downloads the installer
/// if one is available.  Entirely optional — if GitHub is unreachable the
/// launcher starts normally.
///
/// Convention: the GitHub release tag must be a plain version string like
/// "1.2.0" and the release must contain an asset named
/// "RocketLaunch-Setup.exe".
/// </summary>
public sealed class UpdateService
{
    // Change these two strings to your own GitHub repo once you push.
    private const string GitHubOwner = "ALM3SHI";
    private const string GitHubRepo  = "RocketLaunch";

    // Bump this with every release.
    public const string CurrentVersion = "1.0.0";

    private readonly HttpClient _http;

    public UpdateService()
    {
        _http = new HttpClient();
        // GitHub API requires a User-Agent header.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"RocketLaunch/{CurrentVersion}");
    }

    public sealed record UpdateInfo(string Version, string DownloadUrl);

    /// <summary>
    /// Returns update info if a newer version exists on GitHub, otherwise null.
    /// Never throws — any error is silently swallowed so startup isn't blocked.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
            var json = await _http.GetStringAsync(url);
            var node = JsonNode.Parse(json)!;

            var tag     = node["tag_name"]?.GetValue<string>() ?? string.Empty;
            var version = tag.TrimStart('v');

            if (!IsNewer(version, CurrentVersion)) return null;

            // Find the setup installer asset.
            var assets = node["assets"]?.AsArray();
            if (assets is null) return null;

            foreach (var asset in assets)
            {
                var name = asset!["name"]?.GetValue<string>() ?? string.Empty;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var dlUrl = asset["browser_download_url"]?.GetValue<string>();
                    if (dlUrl is not null)
                        return new UpdateInfo(version, dlUrl);
                }
            }
        }
        catch
        {
            // Network error, rate limit, repo doesn't exist yet — ignore.
        }

        return null;
    }

    /// <summary>
    /// Downloads the installer to a temp file and runs it silently.
    /// The installer handles replacing the running app.
    /// </summary>
    public async Task DownloadAndInstallAsync(UpdateInfo info, IProgress<int>? progress = null)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "RocketLaunch-Setup.exe");

        using var response = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var file   = File.Create(tempPath);

        var buffer = new byte[65536];
        long downloaded = 0;
        int  read;

        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            downloaded += read;
            if (total > 0)
                progress?.Report((int)(downloaded * 100 / total));
        }

        // Launch the installer then exit — the installer will restart the app.
        Process.Start(new ProcessStartInfo
        {
            FileName        = tempPath,
            UseShellExecute = true
        });

        System.Windows.Application.Current.Shutdown();
    }

    // Simple semver-like comparison: "1.2.0" > "1.0.0"
    private static bool IsNewer(string remote, string local)
    {
        if (!Version.TryParse(remote, out var r)) return false;
        if (!Version.TryParse(local,  out var l)) return false;
        return r > l;
    }
}
