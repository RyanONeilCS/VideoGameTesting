using System.Collections.Generic;
using UnityEngine;

public enum MoveState { Still, Crouch, Walk, Sprint }

/// <summary>
/// Everything the monsters are allowed to know about this player, in one place.
/// (This is the "PlayerAIState" from the design doc, section 9. Add more values here over time.)
/// </summary>
public class PlayerAIState : MonoBehaviour
{
    /// <summary>Every player currently in the game. Monsters loop over this list.</summary>
    public static readonly List<PlayerAIState> All = new List<PlayerAIState>();

    [Tooltip("The player's first-person camera (their eyes).")]
    public Camera playerCamera;

    [Tooltip("Players can't see monsters further away than this (metres).")]
    public float maxViewDistance = 60f;

    [Header("Filled in automatically while playing")]
    public MoveState moveState;
    [Range(0f, 1f)] public float noiseLevel;
    public int timesCaught;
    [HideInInspector] public float lastCaughtTime = -999f;

    /// <summary>The hiding spot I'm inside right now (null = not hiding).</summary>
    public HidingSpot currentHidingSpot;
    public bool IsHiding => currentHidingSpot != null;

    Vector3 spawnPosition;
    Quaternion spawnRotation;

    public Vector3 EyePosition => playerCamera.transform.position;
    public Vector3 LookDirection => playerCamera.transform.forward;

    /// <summary>Look direction flattened onto the floor (ignores looking up/down).</summary>
    public Vector3 FlatLookDirection
    {
        get
        {
            Vector3 d = LookDirection;
            d.y = 0f;
            return d.sqrMagnitude < 0.0001f ? transform.forward : d.normalized;
        }
    }

    void Awake()
    {
        if (playerCamera == null) playerCamera = GetComponentInChildren<Camera>();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update() => ExplorationMap.Record(transform.position);   // "I've been here"

    // A monster-sized body (3 m tall) is tested at 9 points: feet / middle / head  ×  left edge / centre / right edge.
    // PLUS its FACE: the glowing eyes stick out in front of the body (toward you, since it always faces you).
    // (Without the face point, its eyes could peek past a corner while the game thought it was hidden.)
    static readonly float[] sampleHeights = { 0.4f, 1.5f, 2.6f };
    const float BodyHalfWidth = 0.55f;   // the capsule's full radius
    const float FaceHeight = 2.55f, FaceForward = 0.6f;

    /// <summary>"Am I being watched?" from the monster's point of view (design doc 6.3).</summary>
    public bool CanSee(MonsterBrain monster) => CanSeeBodyAt(monster.transform.position, monster.transform);

    /// <summary>Where its face (eyes) would be if it stood at 'feet' facing me.</summary>
    Vector3 FacePointAt(Vector3 feet)
    {
        Vector3 toMe = EyePosition - feet;
        toMe.y = 0f;
        return feet + Vector3.up * FaceHeight + (toMe.sqrMagnitude > 0.0001f ? toMe.normalized * FaceForward : Vector3.zero);
    }

    /// <summary>
    /// Could I see ANY part of a monster-sized body standing at 'feet' (including its eyes)?
    /// Even a shoulder or its eyes sticking out from behind a corner counts as seen.
    /// If 'owner' is given, hitting that object counts as seeing it.
    /// </summary>
    public bool CanSeeBodyAt(Vector3 feet, Transform owner = null)
    {
        Vector3 side = Vector3.Cross(Vector3.up, feet - EyePosition);   // sideways, as seen from my eyes
        side.y = 0f;
        side = side.sqrMagnitude > 0.0001f ? side.normalized * BodyHalfWidth : Vector3.zero;

        if (CanSeePoint(FacePointAt(feet), owner)) return true;
        foreach (float h in sampleHeights)
            for (int s = -1; s <= 1; s++)
                if (CanSeePoint(feet + Vector3.up * h + side * s, owner)) return true;
        return false;
    }

    /// <summary>
    /// Could I see ANY part of a monster-sized body at 'feet' if I TURNED to look? (Ignores where I'm looking.)
    /// False = a wall is truly in the way. That's real cover, not just "off the edge of my screen".
    /// </summary>
    public bool HasLineOfSightToBodyAt(Vector3 feet, float maxDistance)
    {
        if (Vector3.Distance(EyePosition, feet) > maxDistance) return false;
        Vector3 side = Vector3.Cross(Vector3.up, feet - EyePosition);
        side.y = 0f;
        side = side.sqrMagnitude > 0.0001f ? side.normalized * BodyHalfWidth : Vector3.zero;

        if (!Physics.Linecast(EyePosition, FacePointAt(feet), ~0, QueryTriggerInteraction.Ignore)) return true;   // its eyes
        foreach (float h in sampleHeights)
            for (int s = -1; s <= 1; s++)
                if (!Physics.Linecast(EyePosition, feet + Vector3.up * h + side * s, ~0, QueryTriggerInteraction.Ignore))
                    return true;
        return false;
    }

    /// <summary>
    /// How many of the 9 body points at 'feet' I COULD see if I turned to look (0-9). Ignores where I'm looking.
    /// 1-4 = only an edge of it would stick out past a corner: a good "leaning out to watch you" spot.
    /// </summary>
    public int CountLineOfSightPoints(Vector3 feet, float maxDistance)
    {
        if (Vector3.Distance(EyePosition, feet) > maxDistance) return 0;
        Vector3 side = Vector3.Cross(Vector3.up, feet - EyePosition);
        side.y = 0f;
        side = side.sqrMagnitude > 0.0001f ? side.normalized * BodyHalfWidth : Vector3.zero;

        int count = 0;
        foreach (float h in sampleHeights)
            for (int s = -1; s <= 1; s++)
                if (!Physics.Linecast(EyePosition, feet + Vector3.up * h + side * s, ~0, QueryTriggerInteraction.Ignore))
                    count++;
        return count;
    }

    /// <summary>
    /// HOW MUCH of a monster-sized body at 'feet' could I see? 0 to 9 sample points.
    /// 9 = standing in the open. 1-4 = just an edge poking out from behind a corner (a PEEK). 0 = hidden.
    /// </summary>
    public int CountVisiblePoints(Vector3 feet, Transform owner = null)
    {
        Vector3 side = Vector3.Cross(Vector3.up, feet - EyePosition);
        side.y = 0f;
        side = side.sqrMagnitude > 0.0001f ? side.normalized * BodyHalfWidth : Vector3.zero;

        int count = 0;
        foreach (float h in sampleHeights)
            for (int s = -1; s <= 1; s++)
                if (CanSeePoint(feet + Vector3.up * h + side * s, owner)) count++;
        return count;
    }

    /// <summary>
    /// Is this point on my screen, and is nothing (like a wall) in the way?
    /// If 'owner' is given, hitting that object counts as seeing it.
    /// </summary>
    public bool CanSeePoint(Vector3 point, Transform owner = null)
    {
        // 1) Is it inside the camera's view? Viewport coords go from 0 to 1 across the screen.
        Vector3 vp = playerCamera.WorldToViewportPoint(point);
        bool onScreen = vp.z > 0f && vp.z < maxViewDistance && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
        if (!onScreen) return false;

        // 2) Is anything blocking the view? Shoot an invisible line (a "linecast") from our eyes to the point.
        if (Physics.Linecast(EyePosition, point, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            return owner != null && hit.transform.IsChildOf(owner);   // we hit the monster itself = we see it

        return true;   // nothing in the way
    }

    /// <summary>Called by a monster when it catches this player. For now: teleport back to the start.</summary>
    public void OnCaught(MonsterBrain by)
    {
        timesCaught++;
        lastCaughtTime = Time.time;
        Debug.Log($"Caught by {by.name}! (total: {timesCaught})");

        // Get out of any hiding spot first
        var hiding = GetComponent<PlayerHiding>();
        if (hiding != null) hiding.ExitSpot();

        // A CharacterController must be switched off before we teleport it.
        var controller = GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        if (controller != null) controller.enabled = true;
    }
}
