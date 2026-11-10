using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The shared state of the match that isn't a player or a monster: LOCKERS (who's inside, door open/shut, eyes),
/// GEMS (which are taken) and the SCORE (gems collected, won). The host spawns one of these when the match starts.
///
/// Lockers and gems are part of the map, which is the same scene file on every computer. So instead of making
/// each one a network object, they're numbered (sorted by position, so everyone gets the same numbers) and this
/// sends a list: locker #7 = "monster X inside, door ajar".
/// </summary>
public class NetGame : NetworkBehaviour
{
    public static NetGame Instance { get; private set; }

    public struct LockerState : INetworkSerializable, IEquatable<LockerState>
    {
        public ulong occupant;   // network ID of who's inside (ulong.MaxValue = empty)
        public byte tell;        // HidingSpot.TellCode

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref occupant);
            s.SerializeValue(ref tell);
        }
        public bool Equals(LockerState o) => occupant == o.occupant && tell == o.tell;
    }

    readonly NetworkList<LockerState> lockers = new NetworkList<LockerState>();
    readonly NetworkVariable<ulong> gemsTaken = new NetworkVariable<ulong>();   // one bit per gem (up to 64)
    readonly NetworkVariable<int> collected = new NetworkVariable<int>();
    readonly NetworkVariable<bool> won = new NetworkVariable<bool>();
    readonly NetworkVariable<float> winTime = new NetworkVariable<float>();
    readonly NetworkVariable<ulong> voiceChannel = new NetworkVariable<ulong>();   // picked by the host

    /// <summary>This match's voice channel number (0 = not picked yet). Everyone in the match joins the same one.</summary>
    public ulong VoiceChannel => voiceChannel.Value;

    // Everyone numbers the map's lockers and gems the same way (sorted by position)
    static readonly List<HidingSpot> spots = new List<HidingSpot>();
    static readonly List<Gem> gems = new List<Gem>();

    public static int IndexOf(HidingSpot s) => s == null ? -1 : spots.IndexOf(s);
    public static int IndexOf(Gem g) => g == null ? -1 : gems.IndexOf(g);
    public static HidingSpot SpotAt(int i) => i >= 0 && i < spots.Count ? spots[i] : null;
    public static Gem GemAt(int i) => i >= 0 && i < gems.Count ? gems[i] : null;

    void Awake()
    {
        Instance = this;
        NumberTheMap();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    static void NumberTheMap()
    {
        spots.Clear();
        spots.AddRange(FindObjectsByType<HidingSpot>(FindObjectsInactive.Include));
        spots.Sort((a, b) => Compare(a.transform.position, b.transform.position));
        gems.Clear();
        gems.AddRange(FindObjectsByType<Gem>(FindObjectsInactive.Include));
        gems.Sort((a, b) => Compare(a.transform.position, b.transform.position));
    }

    static int Compare(Vector3 a, Vector3 b)
    {
        int x = Mathf.RoundToInt(a.x * 100f).CompareTo(Mathf.RoundToInt(b.x * 100f));
        return x != 0 ? x : Mathf.RoundToInt(a.z * 100f).CompareTo(Mathf.RoundToInt(b.z * 100f));
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            for (int i = 0; i < spots.Count; i++) lockers.Add(Read(spots[i]));
            // A random channel number for this match's voice chat (so different matches never share one)
            voiceChannel.Value = ((ulong)(uint)System.DateTime.UtcNow.Ticks << 20) ^ (ulong)UnityEngine.Random.Range(1, int.MaxValue) | 1UL;
        }
        else
        {
            lockers.OnListChanged += _ => ApplyLockers();
            gemsTaken.OnValueChanged += (_, __) => ApplyGems();
            collected.OnValueChanged += (_, __) => ApplyScore();
            won.OnValueChanged += (_, __) => ApplyScore();
            winTime.OnValueChanged += (_, __) => ApplyScore();
            ApplyLockers();
            ApplyGems();
            ApplyScore();
        }
    }

    // ---------- HOST: watch the real lockers/gems/score and send any changes ----------
    void Update()
    {
        if (!IsSpawned || !IsServer) return;

        for (int i = 0; i < spots.Count && i < lockers.Count; i++)
        {
            LockerState now = Read(spots[i]);
            if (!now.Equals(lockers[i])) lockers[i] = now;
        }

        ulong taken = 0;
        for (int i = 0; i < gems.Count && i < 64; i++)
            if (!gems[i].gameObject.activeSelf) taken |= 1UL << i;
        gemsTaken.Value = taken;

        var goal = GameObjective.Instance;
        if (goal != null)
        {
            collected.Value = goal.Collected;
            won.Value = goal.Won;
            winTime.Value = goal.Won ? goal.MatchTime : 0f;
        }
    }

    static LockerState Read(HidingSpot s)
    {
        ulong occupant = ulong.MaxValue;
        if (s.Occupant != null && s.Occupant.TryGetComponent(out NetworkObject no) && no.IsSpawned) occupant = no.NetworkObjectId;
        return new LockerState { occupant = occupant, tell = s.TellCode };
    }

    // ---------- EVERYONE ELSE: copy them ----------
    void ApplyLockers()
    {
        for (int i = 0; i < spots.Count && i < lockers.Count; i++)
        {
            LockerState st = lockers[i];
            GameObject occupant = null;
            if (st.occupant != ulong.MaxValue && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(st.occupant, out NetworkObject no))
                occupant = no.gameObject;
            spots[i].ApplyRemote(occupant, st.tell);
        }
    }

    void ApplyGems()
    {
        for (int i = 0; i < gems.Count && i < 64; i++)
        {
            bool taken = (gemsTaken.Value & (1UL << i)) != 0;
            if (taken && gems[i].gameObject.activeSelf) gems[i].gameObject.SetActive(false);
        }
    }

    void ApplyScore()
    {
        if (GameObjective.Instance != null) GameObjective.Instance.ApplyRemote(collected.Value, won.Value, winTime.Value);
    }
}
