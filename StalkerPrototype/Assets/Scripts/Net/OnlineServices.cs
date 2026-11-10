using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Signs in to Unity's online services (needed for join codes and voice chat). Anonymous: no account, no password.
/// The project must be linked to a Unity Cloud project (Edit > Project Settings > Services).
///
/// Each copy of the game signs in as a DIFFERENT anonymous player, including Multiplayer Play Mode's extra
/// players on the same PC (otherwise Player 2 would be "the same person" as Player 1 and couldn't join).
/// </summary>
public static class OnlineServices
{
    public static bool IsSignedIn =>
        UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;

    public static async Task SignInAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync(new InitializationOptions().SetProfile(ProfileName()));
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    /// <summary>A name for this copy's saved sign-in (letters, numbers, - and _ only; 30 characters max).</summary>
    static string ProfileName()
    {
#if UNITY_EDITOR
        // Main editor = "editor". Multiplayer Play Mode players live in Library/VP/<folder>: use the folder name.
        string path = Application.dataPath.Replace('\\', '/');
        int vp = path.IndexOf("/Library/VP/");
        if (vp < 0) return "editor";
        string folder = path.Substring(vp + "/Library/VP/".Length).Split('/')[0];
        return Clean("vp_" + folder);
#else
        // A built game: a fresh anonymous player each time it's started (so two copies on one PC still work)
        return "player_" + System.Diagnostics.Process.GetCurrentProcess().Id;
#endif
    }

    static string Clean(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') sb.Append(c);
        string r = sb.ToString();
        return r.Length > 30 ? r.Substring(0, 30) : r;
    }
}
