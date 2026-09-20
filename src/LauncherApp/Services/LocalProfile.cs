using System.IO;
using System.Text.Json;
using LauncherApp.Models;

namespace LauncherApp.Services;

/// <summary>
/// Everything that makes this "your" launcher rather than a fresh install:
/// your own FriendCode (generated once, then stable forever) and your
/// saved friend list. Deliberately file-based, not a database - there's
/// exactly one user per install, so a JSON file in %AppData% is simpler
/// and just as reliable.
/// </summary>
public sealed class LocalProfile
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RocketLaunch");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "profile.json");

    public string MyFriendCode { get; private set; } = string.Empty;
    public string MyDisplayName { get; set; } = Environment.UserName;
    public List<Friend> Friends { get; private set; } = new();

    // Phase 2: ZeroTier settings
    public string ZeroTierApiToken { get; set; } = string.Empty;
    public string LastNetworkId { get; set; } = string.Empty;

    public static LocalProfile LoadOrCreate()
    {
        var profile = new LocalProfile();

        if (File.Exists(ConfigPath))
        {
            var json = File.ReadAllText(ConfigPath);
            var saved = JsonSerializer.Deserialize<SavedProfile>(json);

            if (saved is not null && !string.IsNullOrWhiteSpace(saved.MyFriendCode))
            {
                profile.MyFriendCode = saved.MyFriendCode;
                profile.MyDisplayName = saved.MyDisplayName;
                profile.Friends = saved.Friends ?? new List<Friend>();
                profile.ZeroTierApiToken = saved.ZeroTierApiToken ?? string.Empty;
                profile.LastNetworkId = saved.LastNetworkId ?? string.Empty;

                // IsOnline is a live value, never a saved one - always start
                // every friend as offline until the first poll says otherwise.
                foreach (var friend in profile.Friends)
                {
                    friend.IsOnline = false;
                }

                return profile;
            }
        }

        // First run: mint a new, stable FriendCode and persist it immediately
        // so it survives even if the user closes the app before doing anything else.
        profile.MyFriendCode = GenerateFriendCode();
        profile.Save();
        return profile;
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDir);

        var saved = new SavedProfile
        {
            MyFriendCode = MyFriendCode,
            MyDisplayName = MyDisplayName,
            Friends = Friends,
            ZeroTierApiToken = ZeroTierApiToken,
            LastNetworkId = LastNetworkId
        };

        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// 8 uppercase hex characters (e.g. "A3F9-21BC") - short enough to
    /// read over voice chat, long enough that nobody collides with your
    /// friend's code by accident.
    /// </summary>
    private static string GenerateFriendCode()
    {
        var raw = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        return $"{raw[..4]}-{raw[4..]}";
    }

    private sealed class SavedProfile
    {
        public string MyFriendCode { get; set; } = string.Empty;
        public string MyDisplayName { get; set; } = string.Empty;
        public List<Friend>? Friends { get; set; }
        public string? ZeroTierApiToken { get; set; }
        public string? LastNetworkId { get; set; }
    }
}
