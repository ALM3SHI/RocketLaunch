namespace PresenceServer.Models;

/// <summary>
/// Tracks one currently-connected launcher instance: who they are (their
/// stable, self-chosen FriendCode) and which live SignalR connection maps
/// to them right now. ConnectionId changes every time the app reconnects;
/// FriendCode never does - that's what your friends actually add to their list.
/// </summary>
public sealed class OnlineFriend
{
    public required string FriendCode { get; init; }
    public required string DisplayName { get; init; }
    public required string ConnectionId { get; set; }
}
