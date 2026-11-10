using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// One player, over the network.
///
/// YOUR player: you move it yourself (instant, no lag), and its position is sent to everyone (NetworkTransform,
/// "Owner" mode). This script also sends what the monsters need to know about you: where you're LOOKING (up/down;
/// left/right is the body's rotation), how high your eyes are (crouching), and how much NOISE you make.
///
/// EVERYONE ELSE'S player (a copy on your screen): no camera, no controls, no HUD. You see their body and their
/// flashlight. On the HOST these copies are what the monsters look at, listen to and chase.
///
/// The HOST decides about lockers and catches: pressing E asks the host "can I get in?", and the host answers.
/// </summary>
[RequireComponent(typeof(PlayerAIState), typeof(NetworkObject), typeof(NetworkTransform))]
public class PlayerNet : NetworkBehaviour
{
    /// <summary>The player on THIS computer (null until you've joined).</summary>
    public static PlayerNet Local { get; private set; }

    // Written by you, read by everyone (the host's monsters use these)
    readonly NetworkVariable<float> pitch = new NetworkVariable<float>(0f, writePerm: NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> eyeHeight = new NetworkVariable<float>(1.6f, writePerm: NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<byte> moveState = new NetworkVariable<byte>(0, writePerm: NetworkVariableWritePermission.Owner);
    readonly NetworkVariable<float> noise = new NetworkVariable<float>(0f, writePerm: NetworkVariableWritePermission.Owner);

    // Written by the host
    readonly NetworkVariable<int> hidingIn = new NetworkVariable<int>(-1);   // locker number (NetGame), -1 = not hiding
    readonly NetworkVariable<int> timesCaught = new NetworkVariable<int>();

    PlayerAIState ai;
    PlayerHiding hiding;
    CharacterController controller;
    NetworkTransform netTransform;
    Transform cam;
    bool placedAtSpawn;

    void Awake()
    {
        ai = GetComponent<PlayerAIState>();
        hiding = GetComponent<PlayerHiding>();
        controller = GetComponent<CharacterController>();
        netTransform = GetComponent<NetworkTransform>();
        cam = ai.playerCamera != null ? ai.playerCamera.transform : GetComponentInChildren<Camera>().transform;
    }

    public override void OnNetworkSpawn()
    {
        name = IsOwner ? "Player (you)" : $"Player {OwnerClientId + 1}";
        hidingIn.OnValueChanged += (_, now) => OnHidingChanged(now);
        timesCaught.OnValueChanged += (_, now) => ai.timesCaught = now;

        if (IsOwner) BecomeMine();
        else BecomeSomeoneElses();
        OnHidingChanged(hidingIn.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (Local == this) Local = null;
    }

    /// <summary>This is me: switch on my camera, ears, controls and HUD.</summary>
    void BecomeMine()
    {
        Local = this;
        ai.playerCamera.enabled = true;
        SetEnabled<AudioListener>(cam, true);
        SetEnabled<FilmGrain>(cam, true);
        SetEnabled<FirstPersonController>(transform, true);
        SetEnabled<PlayerHiding>(transform, true);
        SetEnabled<PlayerHUD>(transform, true);
        SetBodyShadowOnly(true);   // don't block my own view with my own body
        if (NetSession.CurrentMode != NetSession.Mode.Solo) gameObject.AddComponent<ProximityVoice>();   // talk to nearby teammates
        NetSession.OnLocalPlayerReady();
    }

    /// <summary>Someone else: just a body with a flashlight. No camera, no ears, no controls.</summary>
    void BecomeSomeoneElses()
    {
        ai.playerCamera.enabled = false;    // (still used for "can this player see the monster?" on the host)
        var listener = cam.GetComponent<AudioListener>();
        if (listener != null) Destroy(listener);   // only ONE pair of ears per computer: yours
        SetEnabled<FilmGrain>(cam, false);
        SetEnabled<FirstPersonController>(transform, false);
        SetEnabled<PlayerHiding>(transform, false);
        SetEnabled<PlayerHUD>(transform, false);
        SetBodyShadowOnly(false);
    }

    static void SetEnabled<T>(Transform on, bool value) where T : Behaviour
    {
        var c = on.GetComponent<T>();
        if (c != null) c.enabled = value;
    }

    void SetBodyShadowOnly(bool shadowOnly)
    {
        var body = transform.Find("Body");
        if (body != null && body.TryGetComponent(out Renderer r))
            r.shadowCastingMode = shadowOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
    }

    void Update()
    {
        if (!IsSpawned) return;

        if (IsOwner)
        {
            // First frame: walk to my spot in the start room
            if (!placedAtSpawn)
            {
                placedAtSpawn = true;
                Vector3 start = PlayerSpawnPoint.PositionFor(OwnerClientId);
                Quaternion facing = transform.rotation;
                ai.SetSpawn(start, facing);
                MoveTo(start, facing);
            }

            // Tell everyone where I'm looking and how loud I am (rounded a little, so we don't send tiny changes)
            float p = cam.localEulerAngles.x;
            if (p > 180f) p -= 360f;
            pitch.Value = Mathf.Round(p * 2f) / 2f;
            eyeHeight.Value = Mathf.Round(cam.localPosition.y * 20f) / 20f;
            moveState.Value = (byte)ai.moveState;
            noise.Value = ai.noiseLevel;
        }
        else
        {
            // Someone else's copy: aim their head (and flashlight) and pass on their noise to the monsters
            cam.localRotation = Quaternion.Euler(pitch.Value, 0f, 0f);
            cam.localPosition = new Vector3(0f, eyeHeight.Value, 0f);
            ai.moveState = (MoveState)moveState.Value;
            ai.noiseLevel = noise.Value;
        }
    }

    /// <summary>Jump straight there (no sliding on other screens). Only on your own player.</summary>
    public void MoveTo(Vector3 position, Quaternion rotation)
    {
        bool wasOn = controller.enabled;
        controller.enabled = false;   // a CharacterController must be off to be moved directly
        if (IsSpawned && netTransform.CanCommitToTransform) netTransform.Teleport(position, rotation, transform.localScale);
        else transform.SetPositionAndRotation(position, rotation);
        controller.enabled = wasOn;
    }

    // =====================================================================
    // LOCKERS: ask the host
    // =====================================================================
    public void RequestInteract(HidingSpot spot) => InteractRpc(NetGame.IndexOf(spot));
    public void RequestExit() => ExitRpc();

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void InteractRpc(int lockerNumber)
    {
        HidingSpot spot = NetGame.SpotAt(lockerNumber);
        if (spot == null || ai.IsHiding) return;
        if (Vector3.Distance(spot.ExitPosition, transform.position) > 4f) return;   // too far (a lag sanity check)

        MonsterBrain monsterInside = spot.OccupyingMonster;
        if (monsterInside != null) { monsterInside.AmbushedBy(ai); return; }   // surprise!

        if (!spot.TryEnter(gameObject)) return;
        ai.currentHidingSpot = spot;
        hidingIn.Value = lockerNumber;   // -> OnHidingChanged on every computer
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    void ExitRpc() => ServerLeaveSpot();

    void ServerLeaveSpot()
    {
        HidingSpot spot = ai.currentHidingSpot;
        if (spot != null) spot.Exit(gameObject);
        ai.currentHidingSpot = null;
        hidingIn.Value = -1;
    }

    void OnHidingChanged(int lockerNumber)
    {
        HidingSpot spot = NetGame.SpotAt(lockerNumber);
        if (!IsServer) ai.currentHidingSpot = spot;   // (the host already set it)

        if (IsOwner)
        {
            if (spot != null) hiding.EnterSpotLocally(spot);
            else hiding.LeaveSpotLocally(moveOut: true);
        }
        else
        {
            controller.enabled = spot == null;   // someone else's body: nothing to bump into while they're in a locker
        }
    }

    // =====================================================================
    // CAUGHT (the host's monster got you)
    // =====================================================================
    /// <summary>HOST: a monster caught this player.</summary>
    public void ServerCaught()
    {
        timesCaught.Value = ai.timesCaught;
        ServerLeaveSpot();
        CaughtRpc();
    }

    [Rpc(SendTo.Owner)]
    void CaughtRpc()
    {
        ai.lastCaughtTime = Time.time;            // "CAUGHT!" on your screen
        hiding.LeaveSpotLocally(moveOut: false);  // out of the locker (if you were in one)...
        MoveTo(ai.SpawnPosition, ai.SpawnRotation);   // ...and back to the start
    }
}
