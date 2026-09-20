using Microsoft.AspNetCore.SignalR.Client;

namespace LauncherApp.Services;

/// <summary>
/// Thin wrapper around the SignalR HubConnection so the rest of the app
/// talks to "PresenceClient" and never touches SignalR types directly.
/// That's what makes it painless to swap the transport later (e.g. once
/// there's a real hosted server with auth) without touching MainWindow.
/// </summary>
public sealed class PresenceClient : IAsyncDisposable
{
    // Priority order for the server URL:
    //   1. ROCKETLAUNCH_SERVER environment variable  (set by user / installer)
    //   2. localhost:5000                             (local development fallback)
    //
    // Once you deploy PresenceServer to Render, set this env var to your
    // Render URL, e.g.: ROCKETLAUNCH_SERVER=https://rocketlaunch-presence.onrender.com
    private static readonly string ServerUrl =
        (Environment.GetEnvironmentVariable("ROCKETLAUNCH_SERVER")
         ?? "http://localhost:5000")
        .TrimEnd('/') + "/presenceHub";


    private readonly HubConnection _connection;

    /// <summary>Raised when a friend invites you in. Args: (fromDisplayName, sessionPayload).</summary>
    public event Action<string, string>? InviteReceived;

    public PresenceClient()
    {
        _connection = new HubConnectionBuilder()
            .WithUrl(ServerUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<string, string>("ReceiveInvite", (fromDisplayName, sessionPayload) =>
        {
            InviteReceived?.Invoke(fromDisplayName, sessionPayload);
        });
    }

    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    public async Task ConnectAsync(string myFriendCode, string myDisplayName)
    {
        if (_connection.State == HubConnectionState.Disconnected)
        {
            await _connection.StartAsync();
            await _connection.InvokeAsync("Register", myFriendCode, myDisplayName);
        }
    }

    public async Task<bool> CheckStatusAsync(string friendCode)
    {
        if (!IsConnected) return false;
        return await _connection.InvokeAsync<bool>("CheckStatus", friendCode);
    }

    /// <summary>
    /// Returns (isOnline, displayName). DisplayName is the friend's own
    /// self-reported name if they're online, or friendCode if offline/unknown.
    /// </summary>
    public async Task<(bool IsOnline, string DisplayName)> CheckStatusDetailedAsync(string friendCode)
    {
        if (!IsConnected) return (false, friendCode);
        var result = await _connection.InvokeAsync<string?>("CheckStatusDetailed", friendCode);
        if (result is null) return (false, friendCode);
        return (true, result);
    }

    /// <returns>true if the friend was online and the invite was delivered.</returns>
    public async Task<bool> SendInviteAsync(string toFriendCode, string myDisplayName, string sessionPayload)
    {
        if (!IsConnected) return false;
        return await _connection.InvokeAsync<bool>("SendInvite", toFriendCode, myDisplayName, sessionPayload);
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
