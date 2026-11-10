using Unity.Netcode;

/// <summary>
/// "Am I the one running the game world?" One question the rest of the code asks.
///
/// MULTIPLAYER IN ONE PARAGRAPH: one player HOSTS (their game is the server AND a player). The host runs the
/// real game: monster brains, gem pickups, who's in which locker, who got caught. Everyone else (CLIENTS)
/// moves their own player and sees copies of everything else. "Solo" is just hosting with nobody joining.
/// </summary>
public static class NetRole
{
    /// <summary>True on the host (and when no network is running at all). False on joined players.</summary>
    public static bool IsServer
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return nm == null || !nm.IsListening || nm.IsServer;
        }
    }
}
