using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Sends a monster from the HOST to everyone else.
///   Position/rotation: the NetworkTransform on the same object (smoothed).
///   Teleports: snapped, never smoothed (otherwise you'd see the Trickster SLIDE through the walls to its new spot).
///   What it's doing: MonsterBrain.GetView() -> a small packet, ~30 times a second -> ApplyView() on the copies.
///   Teleport flashes: a one-off message.
/// </summary>
[RequireComponent(typeof(MonsterBrain), typeof(NetworkObject), typeof(NetworkTransform))]
public class MonsterNet : NetworkBehaviour
{
    const ulong None = ulong.MaxValue;
    const float TeleportIfMovedMoreThan = 2.5f;   // metres in one frame: no monster walks that fast

    /// <summary>The View, as plain numbers (players/monsters as network IDs, lockers/gems as list positions).</summary>
    public struct NetView : INetworkSerializable, IEquatable<NetView>
    {
        public byte state, reaction;
        public int flags;
        public float speed, distance, unseenFor, watchPressure, ring, ringMax, bluff;
        public ulong target, disguisedAs;
        public short bangingOn, gem;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref state); s.SerializeValue(ref reaction); s.SerializeValue(ref flags);
            s.SerializeValue(ref speed); s.SerializeValue(ref distance); s.SerializeValue(ref unseenFor);
            s.SerializeValue(ref watchPressure); s.SerializeValue(ref ring); s.SerializeValue(ref ringMax);
            s.SerializeValue(ref bluff); s.SerializeValue(ref target); s.SerializeValue(ref disguisedAs);
            s.SerializeValue(ref bangingOn); s.SerializeValue(ref gem);
        }

        public bool Equals(NetView o) =>
            state == o.state && reaction == o.reaction && flags == o.flags && speed == o.speed &&
            distance == o.distance && unseenFor == o.unseenFor && watchPressure == o.watchPressure &&
            ring == o.ring && ringMax == o.ringMax && bluff == o.bluff && target == o.target &&
            disguisedAs == o.disguisedAs && bangingOn == o.bangingOn && gem == o.gem;
    }

    readonly NetworkVariable<NetView> view = new NetworkVariable<NetView>();

    MonsterBrain brain;
    NetworkTransform netTransform;
    Vector3 lastPosition;

    void Awake()
    {
        brain = GetComponent<MonsterBrain>();
        netTransform = GetComponent<NetworkTransform>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            brain.Flashed += OnFlashed;
            lastPosition = transform.position;
        }
        else
        {
            brain.BecomeRemoteCopy();
            view.OnValueChanged += (_, now) => Apply(now);
            Apply(view.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) brain.Flashed -= OnFlashed;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer) return;
        view.Value = ToNet(brain.GetView());
    }

    void LateUpdate()
    {
        if (!IsSpawned || !IsServer) return;
        // A big jump in one frame = a teleport (or getting into/out of a locker): SNAP there on everyone's screen
        if ((transform.position - lastPosition).sqrMagnitude > TeleportIfMovedMoreThan * TeleportIfMovedMoreThan)
            netTransform.Teleport(transform.position, transform.rotation, transform.localScale);
        lastPosition = transform.position;
    }

    void OnFlashed(Vector3 at) => FlashRpc(at);

    [Rpc(SendTo.NotServer)]
    void FlashRpc(Vector3 at) => brain.PlayFlashAt(at);

    // ---------- View <-> network numbers ----------
    static NetView ToNet(MonsterBrain.View v) => new NetView
    {
        state = (byte)v.state, reaction = (byte)v.reaction, flags = (int)v.flags,
        speed = v.speed, distance = v.distance, unseenFor = v.unseenFor, watchPressure = v.watchPressure,
        ring = v.ring, ringMax = v.ringMax, bluff = v.bluff,
        target = IdOf(v.target), disguisedAs = IdOf(v.disguisedAs),
        bangingOn = (short)NetGame.IndexOf(v.bangingOn), gem = (short)NetGame.IndexOf(v.guardingGem),
    };

    void Apply(NetView n)
    {
        brain.ApplyView(new MonsterBrain.View
        {
            state = (MonsterState)n.state, reaction = (SpottedReaction)n.reaction, flags = (MonsterBrain.ViewFlags)n.flags,
            speed = n.speed, distance = n.distance, unseenFor = n.unseenFor, watchPressure = n.watchPressure,
            ring = n.ring, ringMax = n.ringMax, bluff = n.bluff,
            target = Find<PlayerAIState>(n.target), disguisedAs = Find<MonsterBrain>(n.disguisedAs),
            bangingOn = NetGame.SpotAt(n.bangingOn), guardingGem = NetGame.GemAt(n.gem),
        });
    }

    static ulong IdOf(Component c) =>
        c != null && c.TryGetComponent(out NetworkObject no) && no.IsSpawned ? no.NetworkObjectId : None;

    T Find<T>(ulong id) where T : Component
    {
        if (id == None || NetworkManager == null) return null;
        return NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject no) ? no.GetComponent<T>() : null;
    }
}
