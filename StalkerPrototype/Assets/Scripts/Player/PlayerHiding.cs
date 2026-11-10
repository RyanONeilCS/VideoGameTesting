using UnityEngine;

/// <summary>
/// Look at a hiding spot (locker) and press E to get in. Press E again to get out.
/// If a MONSTER is already inside... bad luck.
///
/// Multiplayer: pressing E ASKS THE HOST (PlayerNet), because the host knows who's in which locker. When the host
/// says yes, EnterSpotLocally moves you in. (On your own you're the host, so the answer is instant.)
/// </summary>
[RequireComponent(typeof(PlayerAIState), typeof(CharacterController), typeof(FirstPersonController))]
public class PlayerHiding : MonoBehaviour
{
    [Tooltip("How far away you can reach a hiding spot (metres).")]
    public float reach = 2.5f;

    /// <summary>The hiding spot you're looking at right now (for the "[E] Hide" prompt).</summary>
    public HidingSpot LookedAtSpot { get; private set; }

    PlayerAIState ai;
    CharacterController controller;
    FirstPersonController movement;
    PlayerNet net;
    HidingSpot inside;   // the locker I'm physically in, on this computer

    void Awake()
    {
        ai = GetComponent<PlayerAIState>();
        controller = GetComponent<CharacterController>();
        movement = GetComponent<FirstPersonController>();
        net = GetComponent<PlayerNet>();
    }

    bool Networked => net != null && net.IsSpawned;

    void Update()
    {
        // What am I looking at?
        LookedAtSpot = null;
        if (!ai.IsHiding)
        {
            Transform cam = ai.playerCamera.transform;
            if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
                LookedAtSpot = hit.collider.GetComponentInParent<HidingSpot>();
        }

        if (!InputHelper.InteractPressed()) return;

        if (Networked)
        {
            if (ai.IsHiding) net.RequestExit();
            else if (LookedAtSpot != null) net.RequestInteract(LookedAtSpot);
            return;
        }

        // ----- Not networked: decide right here -----
        if (ai.IsHiding) { ExitSpot(); return; }
        if (LookedAtSpot == null) return;
        MonsterBrain monsterInside = LookedAtSpot.OccupyingMonster;
        if (monsterInside != null) { monsterInside.AmbushedBy(ai); return; }   // surprise!
        if (!LookedAtSpot.TryEnter(gameObject)) return;
        ai.currentHidingSpot = LookedAtSpot;
        EnterSpotLocally(LookedAtSpot);
    }

    /// <summary>Climb in (the host already said yes): locked in place, looking out through the door.</summary>
    public void EnterSpotLocally(HidingSpot spot)
    {
        inside = spot;
        ai.currentHidingSpot = spot;
        controller.enabled = false;       // so we don't collide with the locker
        movement.movementLocked = true;   // can still look around, can't walk
        Place(spot.InsidePosition, Quaternion.LookRotation(spot.transform.forward));
    }

    /// <summary>Climb out. moveOut = step out in front of the door (false = something else moves you, e.g. caught).</summary>
    public void LeaveSpotLocally(bool moveOut)
    {
        if (inside == null) return;   // not in one
        HidingSpot spot = inside;
        inside = null;
        ai.currentHidingSpot = null;
        movement.movementLocked = false;
        if (moveOut) Place(spot.ExitPosition, transform.rotation);
        controller.enabled = true;
    }

    /// <summary>Not networked: get out and free the locker.</summary>
    public void ExitSpot()
    {
        if (ai.currentHidingSpot != null) ai.currentHidingSpot.Exit(gameObject);
        LeaveSpotLocally(moveOut: true);
    }

    void Place(Vector3 position, Quaternion rotation)
    {
        if (Networked) net.MoveTo(position, rotation);
        else transform.SetPositionAndRotation(position, rotation);
    }
}
