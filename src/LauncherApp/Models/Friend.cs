namespace LauncherApp.Models;

/// <summary>
/// One entry in your locally-saved friend list. IsOnline is never
/// persisted - it's refreshed every poll cycle from the presence server
/// and reset to false on load, so you never show a stale "online" from
/// last session.
/// </summary>
public sealed class Friend
{
    public required string FriendCode { get; set; }
    public required string DisplayName { get; set; }
    public bool IsOnline { get; set; }
}
