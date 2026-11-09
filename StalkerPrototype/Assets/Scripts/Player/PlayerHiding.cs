using UnityEngine;

/// <summary>
/// Look at a hiding spot (locker) and press E to get in. Press E again to get out.
/// If a MONSTER is already inside... bad luck.
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

    void Awake()
    {
        ai = GetComponent<PlayerAIState>();
        controller = GetComponent<CharacterController>();
        movement = GetComponent<FirstPersonController>();
    }

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

        if (ai.IsHiding) { ExitSpot(); return; }
        if (LookedAtSpot == null) return;

        MonsterBrain monsterInside = LookedAtSpot.OccupyingMonster;
        if (monsterInside != null)
        {
            monsterInside.AmbushedBy(ai);   // surprise!
            return;
        }
        EnterSpot(LookedAtSpot);
    }

    void EnterSpot(HidingSpot spot)
    {
        if (!spot.TryEnter(gameObject)) return;
        ai.currentHidingSpot = spot;
        controller.enabled = false;       // must be off to teleport, and so we don't collide with the locker
        movement.movementLocked = true;   // can still look around, can't walk
        transform.SetPositionAndRotation(spot.InsidePosition, Quaternion.LookRotation(spot.transform.forward));
    }

    public void ExitSpot()
    {
        HidingSpot spot = ai.currentHidingSpot;
        if (spot == null) return;
        spot.Exit(gameObject);
        ai.currentHidingSpot = null;
        transform.position = spot.ExitPosition;
        controller.enabled = true;
        movement.movementLocked = false;
    }
}
