using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using PresenceServer.Models;

namespace PresenceServer.Hubs;

/// <summary>
/// Phase 1 of the RocketLaunch plan: presence + invites only. No game
/// networking happens here - this hub's only job is "who's online" and
/// "relay this invite to that friend". The ZeroTier plumbing (Phase 2)
/// will ride inside the payload this hub forwards, once it exists.
///
/// Deliberately no database yet: state lives in memory, so a server
/// restart clears who's online (which is fine - clients re-register on
/// reconnect) but does NOT clear anyone's friend list (that lives
/// client-side, per the plan).
/// </summary>
public sealed class PresenceHub : Hub
{
    // FriendCode -> who's connected as that code right now.
    private static readonly ConcurrentDictionary<string, OnlineFriend> OnlineByCode = new();

    // ConnectionId -> FriendCode, so OnDisconnectedAsync can clean up
    // without a linear scan of OnlineByCode.
    private static readonly ConcurrentDictionary<string, string> CodeByConnection = new();

    /// <summary>
    /// Called once, right after the WPF client connects. Claims this
    /// FriendCode as "online" for the lifetime of the connection.
    /// </summary>
    public Task Register(string friendCode, string displayName)
    {
        var entry = new OnlineFriend
        {
            FriendCode = friendCode,
            DisplayName = displayName,
            ConnectionId = Context.ConnectionId
        };

        OnlineByCode[friendCode] = entry;
        CodeByConnection[Context.ConnectionId] = friendCode;

        return Task.CompletedTask;
    }

    /// <summary>
    /// The WPF client polls this per friend-in-list every few seconds
    /// (see PresenceClient.cs). Simple and correct beats clever for an
    /// MVP - a push-based "watch list" can replace this later without
    /// changing the client's public API.
    /// </summary>
    public Task<bool> CheckStatus(string friendCode)
    {
        return Task.FromResult(OnlineByCode.ContainsKey(friendCode));
    }

    /// <summary>
    /// Returns the friend's display name if they're online, null if offline.
    /// Used by the client's CheckStatusDetailedAsync so it can update the
    /// display name the first time a friend comes online.
    /// </summary>
    public Task<string?> CheckStatusDetailed(string friendCode)
    {
        if (OnlineByCode.TryGetValue(friendCode, out var entry))
            return Task.FromResult<string?>(entry.DisplayName);
        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Forwards an invite to a specific friend if - and only if - they're
    /// currently online. sessionPayload is opaque to the server on purpose:
    /// in Phase 1 it's just a placeholder string, but Phase 2 will put the
    /// ZeroTier network ID / join token in there, and this hub never needs
    /// to change to carry it.
    /// </summary>
    public async Task<bool> SendInvite(string toFriendCode, string fromDisplayName, string sessionPayload)
    {
        if (!OnlineByCode.TryGetValue(toFriendCode, out var target))
        {
            return false; // friend is offline - let the caller show that in the UI
        }

        await Clients.Client(target.ConnectionId)
            .SendAsync("ReceiveInvite", fromDisplayName, sessionPayload);

        return true;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (CodeByConnection.TryRemove(Context.ConnectionId, out var friendCode))
        {
            OnlineByCode.TryRemove(friendCode, out _);
        }

        return base.OnDisconnectedAsync(exception);
    }
}
