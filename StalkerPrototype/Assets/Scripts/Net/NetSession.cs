using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The start menu and the connection.
///   SOLO: you host a match nobody else can join.
///   ONLINE (over the internet): Host gives you a JOIN CODE (like "K7QX2M"); friends type it in to join.
///     This goes through Unity's Relay servers, so nobody has to mess with IP addresses or routers.
///     Needs the project linked to Unity Cloud (Edit > Project Settings > Services).
///   LOCAL NETWORK: host on your PC, friends on the same network join with your IP address (same PC: 127.0.0.1).
/// When a match starts, the HOST spawns the monsters (MonsterSpawnPoint) and the shared match state (NetGame).
/// Players are spawned automatically (one each) from the Player prefab. In Host/Join matches you also get
/// proximity voice chat (ProximityVoice).
///
/// TESTING WITH 2 PLAYERS ON ONE PC: Multiplayer Play Mode (Window > Play Mode). Host in the MAIN window first,
/// then join from Player 2's window (with the code, or 127.0.0.1).
/// </summary>
[RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
public class NetSession : MonoBehaviour
{
    public enum Mode { Menu, Solo, Online, LocalNetwork }

    /// <summary>What kind of match we're in.</summary>
    public static Mode CurrentMode { get; private set; } = Mode.Menu;

    [Tooltip("The shared match state (lockers, gems, score). Spawned by the host.")]
    public GameObject netGamePrefab;
    [Tooltip("The camera you see in the menu (switched off when your player appears).")]
    public GameObject menuCamera;
    public ushort port = 7777;
    [Tooltip("Most players in an online match.")]
    public int maxPlayers = 4;

    static NetSession instance;
    static ISession session;          // the online match (null = not online)
    static string pendingStatus;
    static bool leaving;

    NetworkManager net;
    UnityTransport transport;
    string address = "127.0.0.1", codeToJoin = "", status = "", disconnectMessage;
    bool busy, matchSpawned;
    float copiedAt = -99f;
    GUIStyle title, label, button, field, code;
    readonly List<Rect> inGameButtons = new List<Rect>();

    void Awake()
    {
        instance = this;
        leaving = false;
        CurrentMode = Mode.Menu;
        net = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();

        // Every computer must know the same list of things that can be spawned
        if (netGamePrefab != null) net.AddNetworkPrefab(netGamePrefab);
        foreach (var point in FindObjectsByType<MonsterSpawnPoint>(FindObjectsInactive.Include))
            if (point.prefab != null) net.AddNetworkPrefab(point.prefab);

        net.OnClientDisconnectCallback += OnDisconnected;
        FreeMouse();
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(pendingStatus)) { status = pendingStatus; pendingStatus = null; }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (net != null) net.OnClientDisconnectCallback -= OnDisconnected;
    }

    // =====================================================================
    // START / JOIN
    // =====================================================================
    void StartSolo()
    {
        CurrentMode = Mode.Solo;
        transport.SetConnectionData("127.0.0.1", port, "127.0.0.1");   // only this computer can connect
        if (!net.StartHost()) Failed("Couldn't start (is another copy already hosting on this PC?)");
    }

    void HostLocal()
    {
        CurrentMode = Mode.LocalNetwork;
        transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");     // anyone who can reach this PC can join
        if (!net.StartHost()) Failed($"Couldn't host on port {port} (already in use?)");
    }

    void JoinLocal()
    {
        CurrentMode = Mode.LocalNetwork;
        transport.SetConnectionData(address.Trim(), port);
        if (net.StartClient()) status = $"Connecting to {address}...";
        else Failed("Couldn't start connecting.");
    }

    /// <summary>ONLINE: make a match on Unity's servers and get a join code. (This also starts hosting.)</summary>
    async void HostOnline()
    {
        busy = true;
        CurrentMode = Mode.Online;
        try
        {
            status = "Signing in...";
            await OnlineServices.SignInAsync();
            status = "Creating the match...";
            var options = new SessionOptions { MaxPlayers = maxPlayers, IsPrivate = true }.WithRelayNetwork();
            session = await MultiplayerService.Instance.CreateSessionAsync(options);
            status = "";
        }
        catch (Exception e) { OnlineFailed("Couldn't host online", e); }
        busy = false;
    }

    /// <summary>ONLINE: join a friend's match with their code. (This also connects.)</summary>
    async void JoinOnline()
    {
        string joinCode = codeToJoin.Trim().ToUpperInvariant();
        if (joinCode.Length == 0) { status = "Type the host's join code first."; return; }
        busy = true;
        CurrentMode = Mode.Online;
        try
        {
            status = "Signing in...";
            await OnlineServices.SignInAsync();
            status = $"Joining {joinCode}...";
            session = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode);
            status = "";
        }
        catch (Exception e) { OnlineFailed($"Couldn't join {joinCode}", e); }
        busy = false;
    }

    void Failed(string message)
    {
        status = message;
        CurrentMode = Mode.Menu;
    }

    void OnlineFailed(string what, Exception e)
    {
        Debug.LogWarning($"{what}: {e}");
        Failed($"{what}: {e.Message}\n(Is the project linked to Unity Cloud? Edit > Project Settings > Services)");
        session = null;
        if (net.IsListening) net.Shutdown();
    }

    /// <summary>HOST: put the match state and the monsters into the world (everyone else gets copies).</summary>
    void SpawnMatch()
    {
        matchSpawned = true;
        if (netGamePrefab != null) Instantiate(netGamePrefab).GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
        foreach (var point in FindObjectsByType<MonsterSpawnPoint>(FindObjectsInactive.Exclude))
        {
            if (point.prefab == null) continue;
            var monster = Instantiate(point.prefab, point.transform.position, point.transform.rotation);
            monster.name = point.prefab.name;
            monster.GetComponent<NetworkObject>().Spawn(destroyWithScene: true);
        }
    }

    /// <summary>Your own player just appeared: hide the menu camera.</summary>
    public static void OnLocalPlayerReady()
    {
        if (instance != null && instance.menuCamera != null) instance.menuCamera.SetActive(false);
    }

    // =====================================================================
    // LEAVE
    // =====================================================================
    void OnDisconnected(ulong clientId)
    {
        // Only care about OUR connection ending (the host left, or we couldn't connect)
        if (net.IsServer || clientId != net.LocalClientId) return;
        disconnectMessage = string.IsNullOrEmpty(net.DisconnectReason)
            ? "Disconnected: the host ended the match (or couldn't be reached)." : net.DisconnectReason;
    }

    void Update()
    {
        // Hosting has started (any kind): spawn the monsters once
        if (!matchSpawned && net.IsListening && net.IsServer) SpawnMatch();

        // (Leave on the next frame, not from inside the network's own callback)
        if (disconnectMessage != null) { string m = disconnectMessage; disconnectMessage = null; LeaveMatch(m); }
    }

    /// <summary>Leave the match and go back to the menu (if you're the host, everyone goes back).</summary>
    public static async void LeaveMatch(string message = "")
    {
        if (leaving) return;
        leaving = true;
        pendingStatus = message;

        // Online: leave the match on Unity's servers too (the host closes it for everyone)
        ISession s = session;
        session = null;
        if (s != null)
        {
            try
            {
                if (s.IsHost && s is IHostSession hosted) await hosted.DeleteAsync();
                else await s.LeaveAsync();
            }
            catch (Exception e) { Debug.LogWarning("Leaving the online match: " + e.Message); }
        }

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            if (nm.IsListening) nm.Shutdown();
            Destroy(nm.gameObject);   // the reloaded scene brings a fresh one
        }
        CurrentMode = Mode.Menu;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static void FreeMouse()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>Is the mouse over an in-game button (Leave, Copy code)? Then a click shouldn't grab the mouse.</summary>
    public static bool IsPointerOverButtons()
    {
        if (instance == null) return false;
        Vector2 mouse = InputHelper.MouseGuiPosition();
        foreach (Rect r in instance.inGameButtons)
            if (r.Contains(mouse)) return true;
        return false;
    }

    // =====================================================================
    // MENU (and the small in-game panel)
    // =====================================================================
    void OnGUI()
    {
        if (title == null)
        {
            title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title.normal.textColor = new Color(0.85f, 0.1f, 0.1f);
            label = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            label.normal.textColor = Color.white;
            button = new GUIStyle(GUI.skin.button) { fontSize = 20 };
            field = new GUIStyle(GUI.skin.textField) { fontSize = 20, alignment = TextAnchor.MiddleCenter };
            code = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            code.normal.textColor = new Color(1f, 0.85f, 0.3f);
        }

        float w = Screen.width, h = Screen.height;
        if (Event.current.type == EventType.Layout) inGameButtons.Clear();

        if (net.IsListening) { InGamePanel(w, h); return; }
        if (busy) { Centered(w, h, status); return; }
        Menu(w, h);
    }

    void InGamePanel(float w, float h)
    {
        // Still connecting
        if (net.IsClient && !net.IsConnectedClient)
        {
            Centered(w, h, status.Length > 0 ? status : "Connecting...");
            if (GUI.Button(new Rect(w / 2f - 80f, h / 2f + 30f, 160f, 40f), "Cancel", button)) LeaveMatch();
            return;
        }

        float y = 40f;
        string who = CurrentMode == Mode.Solo ? "SOLO"
                   : net.IsHost ? $"HOST  •  {net.ConnectedClientsIds.Count} player(s)" : "JOINED";
        GUI.Label(new Rect(w - 360f, y, 350f, 24f), who, new GUIStyle(label) { alignment = TextAnchor.MiddleRight });
        y += 26f;

        // The join code (host, online): tell your friends this
        if (session != null && session.IsHost && !string.IsNullOrEmpty(session.Code))
        {
            GUI.Label(new Rect(w - 360f, y, 350f, 30f), $"JOIN CODE:  {session.Code}", code);
            y += 32f;
        }
        if (CurrentMode != Mode.Solo && !string.IsNullOrEmpty(ProximityVoice.HudText))
        {
            GUI.Label(new Rect(w - 360f, y, 350f, 24f), ProximityVoice.HudText, new GUIStyle(label) { alignment = TextAnchor.MiddleRight });
            y += 26f;
        }

        // Mouse free (Esc): buttons
        if (Cursor.lockState == CursorLockMode.Locked) return;
        if (session != null && session.IsHost && !string.IsNullOrEmpty(session.Code))
        {
            var copyRect = new Rect(w - 170f, y, 160f, 34f);
            inGameButtons.Add(copyRect);
            if (GUI.Button(copyRect, Time.unscaledTime - copiedAt < 1.5f ? "Copied!" : "Copy code", button))
            {
                GUIUtility.systemCopyBuffer = session.Code;
                copiedAt = Time.unscaledTime;
            }
            y += 40f;
        }
        var leaveRect = new Rect(w - 170f, y, 160f, 34f);
        inGameButtons.Add(leaveRect);
        if (GUI.Button(leaveRect, net.IsHost ? "End match" : "Leave match", button)) LeaveMatch();
    }

    void Menu(float w, float h)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = w / 2f - 160f, y = h * 0.12f;
        GUI.Label(new Rect(0f, y, w, 60f), "STALKER PROTOTYPE", title);
        y += 80f;
        if (GUI.Button(new Rect(x, y, 320f, 44f), "Solo", button)) StartSolo();

        // Online (join codes)
        y += 70f;
        GUI.Label(new Rect(0f, y, w, 24f), "ONLINE  (friends anywhere, with a join code)", label);
        y += 28f;
        if (GUI.Button(new Rect(x, y, 320f, 44f), "Host online", button)) HostOnline();
        y += 54f;
        codeToJoin = GUI.TextField(new Rect(x, y, 150f, 44f), codeToJoin, 12, field);
        if (GUI.Button(new Rect(x + 160f, y, 160f, 44f), "Join code", button)) JoinOnline();

        // Local network (IP address)
        y += 76f;
        GUI.Label(new Rect(0f, y, w, 24f), "LOCAL NETWORK  (same Wi-Fi, or same PC = 127.0.0.1)", label);
        y += 28f;
        if (GUI.Button(new Rect(x, y, 320f, 44f), "Host on this network", button)) HostLocal();
        y += 54f;
        address = GUI.TextField(new Rect(x, y, 150f, 44f), address, field);
        if (GUI.Button(new Rect(x + 160f, y, 160f, 44f), "Join address", button)) JoinLocal();

        y += 66f;
        if (!string.IsNullOrEmpty(status)) GUI.Label(new Rect(w / 2f - 320f, y, 640f, 80f), status, label);
    }

    void Centered(float w, float h, string text) => GUI.Label(new Rect(0f, h / 2f - 20f, w, 40f), text, label);
}
