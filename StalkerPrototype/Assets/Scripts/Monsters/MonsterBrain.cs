using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public enum MonsterState { Wander, Stalk, Spotted, Hunt, Search, Hide }

/// <summary>
/// THE MODULAR MONSTER. One brain shared by every monster.
/// What makes monsters different:
///   - MonsterProfile (the data file): toolkit settings, chances, speeds, senses
///   - MonsterRule    (a component):   its quirk (can it move? when does it hunt?)
///
/// Every frame: pick a target -> sense -> ask the rule -> run the current state.
///
///   WANDER (drifts toward players, or GUARDS A GEM: lurks around a corner / hides in a locker by it) --sees you--> STALK
///   STALK --you look at it--> SPOTTED (stare & hide / freeze / flee / vanish / chase) --> STALK
///   STALK --rule says hunt--> HUNT          STALK --dice roll--> FAKE HUNT or HIDE (ambush from a locker)
///   lost you --> SEARCH (checks hiding spots near where it lost you) --> WANDER
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class MonsterBrain : MonoBehaviour
{
    /// <summary>Every monster currently in the game (used by the HUD and the logger).</summary>
    public static readonly List<MonsterBrain> All = new List<MonsterBrain>();

    /// <summary>Fires whenever any monster changes state: (monster, from, to). The logger listens to this.</summary>
    public static event System.Action<MonsterBrain, MonsterState, MonsterState> StateChanged;

    [Tooltip("This monster's settings. Create one: Project window > right-click > Create > Stalker > Monster Profile.")]
    public MonsterProfile profile;
    [Tooltip("Height of its eyes above its feet (metres).")]
    public float eyeHeight = 1.85f;

    // ---------- What the monster currently knows (read by rules, HUD and logger) ----------
    public MonsterState State { get; private set; } = MonsterState.Wander;
    public PlayerAIState Target { get; private set; }
    public bool CanSeeTarget { get; private set; }
    public bool IsWatched { get; private set; }          // is ANY player looking at me?
    public bool CanMoveNow { get; private set; } = true; // what my rule says this frame
    public bool IsFakeHunt { get; private set; }
    public bool IsHiddenInSpot { get; private set; }
    public SpottedReaction CurrentReaction { get; private set; }
    public float DistanceToTarget { get; private set; } = 999f;
    /// <summary>Seconds since ANY player last saw ANY part of me. The hiding rule is built on this.</summary>
    public float UnseenFor { get; private set; }
    /// <summary>"Hidden" = no player has seen any part of me for long enough (see the profile).</summary>
    public bool IsFullyHidden => UnseenFor >= profile.unseenSecondsToCountAsHidden;
    public float TimeInState => Time.time - stateEnteredAt;
    public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight;
    /// <summary>The "stare meter": fills while watched (faster if you walk toward it), empties when unseen.</summary>
    public float WatchPressure { get; private set; }
    /// <summary>True while it's banging on the door of the hiding spot it SAW you get into.</summary>
    public bool IsBanging { get; private set; }
    public HidingSpot BangingOn { get; private set; }
    /// <summary>The gem it's guarding right now (lurking near it, or hiding in a locker by it). null = not guarding.</summary>
    public Gem GuardingGem => guardGem != null ? guardGem : hideGuardGem;
    /// <summary>True once it has reached its guard spot and is waiting there.</summary>
    public bool IsWaitingAtGem => guardGem != null && guardUntil > 0f;
    /// <summary>The short warning beat (roar) before a hunt actually starts moving.</summary>
    public bool IsWindingUp => State == MonsterState.Hunt && Time.time < huntStartsAt;
    /// <summary>Standing at a hiding spot, opening it to look inside (not banging).</summary>
    public bool IsCheckingSpot => State == MonsterState.Search && openingUntil > 0f && !IsBanging;
    /// <summary>Holding still after slipping out of sight.</summary>
    public bool IsLurking => Time.time < lurkUntil;
    /// <summary>Peeking around a corner in front of a player because nobody has seen it for a while.</summary>
    public bool IsPeeking => peeking;
    /// <summary>Leaning out from behind a corner near you, watching you.</summary>
    public bool IsLeaningOut => leaning && !leanReturning;
    /// <summary>Standing at the end of a hallway, luring you closer. Get too close and it charges.</summary>
    public bool IsBaiting => baiting;
    /// <summary>Nobody has seen it for a long time: it stopped showing off and is closing in, fast.</summary>
    public bool IsBeingIgnored => profile.useRing && UnseenFor >= profile.ignoredAfterSeconds;
    /// <summary>Ducked around a corner after being spotted, waiting for whoever follows.</summary>
    public bool IsWaitingAroundCorner => Time.time < cornerWaitUntil;
    /// <summary>In the middle of a lunge (wind-up, then the catch check).</summary>
    public bool IsAttacking => attackPhase != AttackPhase.None;
    /// <summary>Lost you: looking around where it last saw you.</summary>
    public bool IsLookingAround => State == MonsterState.Search && lookingAround;
    /// <summary>THE RING: how far it's keeping from its target right now. Shrinks while unseen; attacks when small.</summary>
    public float Ring => Target != null && rings.TryGetValue(Target, out float r) ? r : profile.ringStartRadius;
    /// <summary>The most the ring can grow back to. Drops a little each time you spot it (pressure builds), resets after a hunt.</summary>
    public float RingMax => Target != null && ringMaxes.TryGetValue(Target, out float m) ? m : profile.ringStartRadius;
    /// <summary>The Brute: worn out after a chase, standing there catching its breath.</summary>
    public bool IsResting => Time.time < restUntil;
    /// <summary>Is it moving right now (and how fast)? Read by MonsterFootsteps.</summary>
    public float CurrentSpeed => agent != null && agent.enabled ? agent.velocity.magnitude : 0f;

    // ---------- Learned values (copies of the profile's chances, so the file isn't changed) ----------
    public BehaviourChance Boldness { get; private set; }
    public BehaviourChance FakeHuntChance { get; private set; }
    public BehaviourChance HideAmbushChance { get; private set; }
    public BehaviourChance CheckSpotsChance { get; private set; }
    public BehaviourChance FreezeBreakAttackChance { get; private set; }
    public BehaviourChance OpenAfterBangingChance { get; private set; }
    public BehaviourChance GuardGemChance { get; private set; }

    NavMeshAgent agent;
    NavMeshPath scratchPath;      // reused when measuring walking distances
    float normalAcceleration;
    MonsterRule rule;
    Renderer[] renderers;
    Collider[] colliders;

    Vector3 spawnPosition, lastKnownPosition;
    float stateEnteredAt, lastSeenTime = -999f, lastHeardTime = -999f, previousDistance;
    float searchAllowedAt, bangUntil, huntStartsAt;
    HidingSpot spotISawYouEnter;                         // remembered for the whole search (for banging)
    float watchedFor, closeUnseenFor;
    float wanderPauseUntil = -1f, nextRepathTime, nextScareCheck, scareAllowedAt, huntAllowedAt, huntEndsAt, hideAllowedAt;
    float stareUntil, freezeUntil, freezeStartDistance, reactionEndsAt, nextHidePick, lurkUntil;
    bool reactionStarted;

    HidingSpot sawPlayerHideIn;                          // "I SAW you get in there"
    HidingSpot hideSpot;                                 // the spot I'm hiding in / heading to
    float hideEndsAt;
    readonly List<HidingSpot> spotsToCheck = new List<HidingSpot>();
    int spotIndex;
    float openingUntil = -1f;

    Gem guardGem;                 // the gem I'm lurking near (Wander state)
    Gem hideGuardGem;             // the gem whose locker I'm hiding in / heading to (Hide state)
    Vector3 guardPoint;
    float guardUntil = -1f, guardAllowedAt;
    bool hidWhileWandering;       // hid to guard a gem (-> back to Wander after), not to ambush someone I was stalking

    bool peeking;                 // peeking around a corner in front of the player
    bool peekSteppedOut;          // false = still getting to the hidden side of the corner; true = leaning out
    Vector3 peekPoint, peekDuckPoint;
    bool baiting, baitSteppedOut; // standing at the end of a hallway, luring you closer
    Vector3 baitPoint, baitDuckPoint;
    float baitStandUntil = -1f, baitEndsAt;
    Vector3 lastSpottedPos;       // where it was last spotted (so "still there" stays rare)
    float lastSpottedTime = -999f;
    int spottedHereCount, staysAllowedHere = 1;
    bool leaning, leanReturning;  // leaning out from behind a corner to watch you
    Vector3 leanPoint, leanHome;
    float leanHoldUntil = -1f, nextLeanTime;
    bool lockerPeek;              // this hide is a PEEK out of a locker (not an ambush / gem guard)
    bool lockerPeekDone;          // the door has shut again (spotted, or nobody noticed)
    float lockerPeekOpenAt, lockerPeekCloseAt, lockerWatchedFor;
    Material eyeMaterial;         // for the eyes in the locker gap
    bool hasEscapeDuck;           // spotted right next to a corner: just step back behind it
    Vector3 escapeDuckPoint;
    float peekEndsAt, peekStandUntil = -1f, peekAllowedAt;

    // THE RING: one per player (co-op!). It goes after whoever's ring is smallest = whoever isn't watching their back.
    readonly Dictionary<PlayerAIState, float> rings = new Dictionary<PlayerAIState, float>();
    readonly Dictionary<PlayerAIState, float> ringMaxes = new Dictionary<PlayerAIState, float>();   // shrinks per sighting
    float nextRingPick, lastTrackedTime = -999f, nextTrackTime;
    float cornerWaitUntil, attackPhaseEndsAt, recoverUntil, huntLastSawAt, resumeStalkAt;
    float lastTeleportTime = -999f, stuckSince = -1f;
    float graceUntil;             // right after a hunt: no stare meter, no stand-and-stare attack
    float lastFlashTime = -999f;  // the flash budget
    float restUntil = -1f;        // the Brute: catching its breath after a chase
    float retreatStartedAt, retreatStartDistance;   // backing away after being spotted: since when, from how far
    bool checkCoverDistance;      // just picked where to retreat to: is it too far (then teleport instead)?
    Vector3 walkAwayFrom;         // walked off (no teleport): avoid this spot for a while
    float walkAwayUntil;
    bool retreatAfterHunt;        // a hunt ran out next to you: back off instead of standing there
    bool leavingAfterHunt;        // ...and once out of sight, always really leave (teleport), never stay
    float nextSneakCheck, exposedSince = -1f;
    bool pathAheadExposed;
    bool ringSpotUnreachable;     // couldn't find ANY reachable spot on the ring (stranded?) -> teleport out
    bool lookingAround;           // searching: looking around where it lost you (before any locker checks)
    float lookAroundUntil = -1f, nextLookTurn;
    Vector3 lookDirection = Vector3.forward;
    enum AttackPhase { None, WindUp, Lunge }
    AttackPhase attackPhase;

    // =====================================================================
    // SETUP
    // =====================================================================
    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        rule = GetComponent<MonsterRule>();
        if (rule == null) rule = gameObject.AddComponent<MonsterRule>();   // no rule = default rule
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
        foreach (var r in renderers)
            if (r.name == "Eye") { eyeMaterial = r.sharedMaterial; break; }

        if (profile == null)
        {
            Debug.LogError($"{name} has no MonsterProfile assigned!", this);
            enabled = false;
            return;
        }
        Boldness = Copy(profile.boldness);
        FakeHuntChance = Copy(profile.fakeHuntChance);
        HideAmbushChance = Copy(profile.hideAmbushChance);
        CheckSpotsChance = Copy(profile.checkHidingSpotsChance);
        FreezeBreakAttackChance = Copy(profile.freezeBreakAttackChance);
        OpenAfterBangingChance = Copy(profile.openAfterBangingChance);
        GuardGemChance = Copy(profile.guardGemChance);
        scratchPath = new NavMeshPath();
        normalAcceleration = agent.acceleration;
    }

    static BehaviourChance Copy(BehaviourChance c)
    {
        var copy = c.Clone();
        copy.Reset();
        return copy;
    }

    /// <summary>LEARNING: nudge a chance, unless learning is switched off in the profile.</summary>
    void Learn(BehaviourChance chance, float amount)
    {
        if (profile.learningEnabled) chance.Nudge(amount, profile.adaptability);
    }

    void OnEnable()
    {
        All.Add(this);
        GameObjective.GemCollected += OnGemCollected;   // "listen" for gem pickups
    }

    void OnDisable()
    {
        All.Remove(this);
        GameObjective.GemCollected -= OnGemCollected;
    }

    void Start()
    {
        // The NavMesh is built when the game starts, so the agent is turned on here, after that.
        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            transform.position = hit.position;
        spawnPosition = transform.position;
        agent.enabled = true;

        scareAllowedAt = Time.time + profile.scareCooldown;
        hideAllowedAt = Time.time + profile.hideCooldown;
        huntAllowedAt = Time.time + 5f;
        Enter(MonsterState.Wander);
    }

    // =====================================================================
    // EVERY FRAME
    // =====================================================================
    void Update()
    {
        // You won: everything stops.
        if (GameObjective.Instance != null && GameObjective.Instance.Won)
        {
            if (agent.enabled && agent.isOnNavMesh) agent.isStopped = true;
            return;
        }

        PickTarget();
        if (Target == null) return;

        // Hiding inside a locker: the agent is switched off, so this has its own little update.
        if (IsHiddenInSpot) { UpdateHiddenInSpot(); return; }

        if (!agent.enabled || !agent.isOnNavMesh) return;
        Sense();
        UpdateRing();

        // The RULE gets the final say on movement, every frame, before anything else.
        CanMoveNow = rule.CanMove(this);
        agent.isStopped = !CanMoveNow;
        agent.updateRotation = CanMoveNow;

        // Normal acceleration unless escaping (UpdateSpotted turns it up while dashing to cover)
        if (State != MonsterState.Spotted) agent.acceleration = normalAcceleration;

        // WATCHED TOO LONG: the Stalker's loop is "spot it, let it go". Stare at it or follow it and it attacks.
        bool canBeProvoked = State == MonsterState.Stalk || State == MonsterState.Spotted || State == MonsterState.Hide;
        if (profile.attackWhenWatchedTooLong && canBeProvoked && WatchPressure >= profile.watchedTooLongSeconds && CanMoveNow)
        {
            WatchPressure = 0f;
            StartHunt(false);
        }

        // While engaged with a player: did they hide in front of me, or did I lose them?
        bool engaged = State == MonsterState.Stalk || State == MonsterState.Spotted || State == MonsterState.Hunt;
        if (engaged)
        {
            if (sawPlayerHideIn != null) { StartSearch(); }
            else if (PlayerGotAway())
            {
                bool farAway = DistanceToTarget > profile.loseInterestDistance;
                bool maySearch = Time.time >= searchAllowedAt && HidingSpot.All.Count > 0;
                if (!farAway && maySearch && CheckSpotsChance.Roll()) StartSearch();
                else Enter(MonsterState.Wander);
            }
        }

        switch (State)
        {
            case MonsterState.Wander: UpdateWander(); break;
            case MonsterState.Stalk: UpdateStalk(); break;
            case MonsterState.Spotted: UpdateSpotted(); break;
            case MonsterState.Hunt: UpdateHunt(); break;
            case MonsterState.Search: UpdateSearch(); break;
            case MonsterState.Hide: UpdateGoingToHide(); break;
        }
    }

    // =====================================================================
    // SENSES
    // =====================================================================
    void PickTarget()
    {
        // During a hunt or an attack, stay locked on whoever it's after.
        bool lockedOn = Target != null && (State == MonsterState.Hunt || State == MonsterState.Search || IsHiddenInSpot);

        if (!lockedOn)
        {
            // With the ring: go after the player whose ring is SMALLEST (the one not watching their back).
            // Hiding players count as "huge ring" (it doesn't know where they are). Ties -> the closest one.
            PlayerAIState best = null;
            float bestScore = float.MaxValue;
            foreach (var p in PlayerAIState.All)
            {
                if (!rings.ContainsKey(p)) rings[p] = profile.ringStartRadius;
                float d = Vector3.Distance(transform.position, p.transform.position);
                float score = profile.useRing ? rings[p] * 10f + d * 0.01f : d;
                if (p.IsHiding) score += 10000f;
                if (score < bestScore) { bestScore = score; best = p; }
            }
            Target = best;
        }
        if (Target != null) DistanceToTarget = Vector3.Distance(transform.position, Target.transform.position);
    }

    // ---------- THE RING ----------
    void UpdateRing()
    {
        if (!profile.useRing || Target == null) return;
        float r = Ring;

        // Shrinks only while it's actually stalking, in position (near the ring), and nobody can see it.
        bool inPosition = DistanceToTarget <= r + 4f;
        // IGNORED (nobody has seen me for a long time): no more showing off, I just come for you, faster.
        bool ignored = IsBeingIgnored;
        bool stalkingQuietly = State == MonsterState.Stalk && !IsWatched && (!peeking || ignored) && !Target.IsHiding;
        if (stalkingQuietly && inPosition)
        {
            float boldFactor = Mathf.Lerp(0.6f, 1.6f, Boldness.current);   // LEARNED boldness = faster closing in
            r -= profile.ringShrinkPerSecond * boldFactor * (ignored ? profile.ignoredShrinkMultiplier : 1f) * Time.deltaTime;
        }
        rings[Target] = Mathf.Clamp(r, profile.ringAttackRadius, RingMax);
    }

    /// <summary>
    /// Spotted! The ring's MAXIMUM drops a little (pressure builds over the match), then the ring is pushed back
    /// a little (your reward for spotting it), but never past the new maximum.
    /// </summary>
    void OnSpottedRing()
    {
        if (Target == null) return;
        ringMaxes[Target] = Mathf.Max(profile.ringMaxLowest, RingMax - profile.ringMaxDropPerSpot);
        rings[Target] = Mathf.Clamp(Ring + profile.ringPushBackWhenSpotted, profile.ringAttackRadius, RingMax);
    }

    /// <summary>Start the ring over (after a hunt, a catch, or losing you): new size, and the maximum back to full.</summary>
    void SetRing(float metres)
    {
        if (Target == null) return;
        ringMaxes[Target] = profile.ringStartRadius;
        rings[Target] = Mathf.Clamp(metres, profile.ringAttackRadius, profile.ringStartRadius);
    }

    void Sense()
    {
        bool couldSeeBefore = Time.time - lastSeenTime < 0.25f;   // saw them a split second ago

        // SIGHT: in range? inside my view cone? nothing blocking? (a player inside a locker is blocked by the locker)
        Vector3 toTarget = Target.EyePosition - EyePosition;
        bool inRange = toTarget.magnitude <= profile.sightRange;
        bool inCone = Vector3.Angle(transform.forward, toTarget) <= profile.sightAngle * 0.5f;
        CanSeeTarget = !Target.IsHiding && inRange && inCone && HasLineOfSight(Target.EyePosition, Target.transform);
        if (CanSeeTarget)
        {
            lastKnownPosition = Target.transform.position;
            lastSeenTime = Time.time;
        }

        // Did I just WATCH them climb into a hiding spot? Only if I saw them a split second ago
        // AND I can actually see that spot's door from here (not around a corner).
        if (Target.IsHiding && couldSeeBefore && sawPlayerHideIn == null && State != MonsterState.Search)
        {
            HidingSpot spot = Target.currentHidingSpot;
            Vector3 door = spot.ExitPosition + Vector3.up * 1.2f;
            if (Vector3.Distance(EyePosition, door) <= profile.sightRange && HasLineOfSight(door, spot.transform))
                sawPlayerHideIn = spot;
        }

        // HEARING: louder noise carries further (sprinting = full hearing range).
        // Hearing is ROUGH: the further away, the less exactly it knows where the sound came from.
        if (!Target.IsHiding && Target.noiseLevel > 0f && DistanceToTarget <= profile.hearingRange * Target.noiseLevel)
        {
            if (CanSeeTarget) lastHeardTime = Time.time;              // (sight already gave the exact position)
            else if (Time.time - lastHeardTime > 0.5f)               // a new rough guess twice a second
            {
                Vector2 error = Random.insideUnitCircle * DistanceToTarget * 0.25f;
                lastKnownPosition = Target.transform.position + new Vector3(error.x, 0f, error.y);
                lastHeardTime = Time.time;
            }
        }

        // TRACKING: a stalker always knows ROUGHLY where you are (an old, fuzzy guess), unless you're hiding.
        // Paused during a hunt and a search: then it only has its eyes and ears (that's how you escape).
        if (profile.alwaysTracksPlayers && !Target.IsHiding && !CanSeeTarget && Time.time >= nextTrackTime &&
            State != MonsterState.Hunt && State != MonsterState.Search)
        {
            nextTrackTime = Time.time + profile.trackingUpdateInterval;
            float fuzz = profile.useRing ? Mathf.Min(profile.trackingFuzz, Ring * 0.25f) : profile.trackingFuzz;
            Vector2 error = Random.insideUnitCircle * fuzz;
            lastKnownPosition = Target.transform.position + new Vector3(error.x, 0f, error.y);
            lastTrackedTime = Time.time;
        }

        // AM I BEING WATCHED by any player?
        IsWatched = false;
        foreach (var p in PlayerAIState.All)
        {
            if (p.CanSee(this)) { IsWatched = true; break; }
        }
        watchedFor = IsWatched ? watchedFor + Time.deltaTime : 0f;
        UnseenFor = IsWatched ? 0f : UnseenFor + Time.deltaTime;

        // THE STARE METER: fills while watched (faster if the player is walking toward me), empties when unseen.
        // It punishes YOU CHOOSING TO STARE, not me taking my time to leave. So it only fills while I'm stalking,
        // hiding, baiting, or standing-and-staring (caught in the open). NOT while I'm backing away after being
        // spotted, not during a hunt, not while wandering, and not during the grace period right after a hunt.
        bool playerApproaching = (previousDistance - DistanceToTarget) / Mathf.Max(Time.deltaTime, 0.0001f) > 1f;
        previousDistance = DistanceToTarget;
        bool leaving = State == MonsterState.Spotted && CurrentReaction != SpottedReaction.StandAndStare;
        bool meterCounts = (State == MonsterState.Stalk || State == MonsterState.Hide ||
                            State == MonsterState.Spotted) && !leaving && Time.time >= graceUntil;
        if (IsWatched && meterCounts)
            WatchPressure += Time.deltaTime * (playerApproaching ? profile.followPressureMultiplier : 1f);
        else
            WatchPressure = Mathf.Max(0f, WatchPressure - Time.deltaTime * profile.pressureDecayPerSecond);
    }

    bool HasLineOfSight(Vector3 to, Transform targetRoot)
    {
        if (Physics.Linecast(EyePosition, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.transform.IsChildOf(targetRoot);   // the first thing we hit is the player = we see them
        return true;
    }

    bool PlayerGotAway()
    {
        float sinceContact = Time.time - Mathf.Max(lastSeenTime, Mathf.Max(lastHeardTime, lastTrackedTime));
        // A tracking stalker never "loses interest" because of distance: it knows roughly where you are anyway.
        bool tracking = profile.alwaysTracksPlayers && !Target.IsHiding;
        bool tooFar = !tracking && DistanceToTarget > profile.loseInterestDistance;
        return tooFar || sinceContact > profile.memorySeconds;
    }

    /// <summary>Would ANY player see ANY part of me if I stood at 'feet'? (Right now, where they're looking.)</summary>
    bool AnyPlayerCanSee(Vector3 feet)
    {
        foreach (var p in PlayerAIState.All)
            if (p.CanSeeBodyAt(feet)) return true;
        return false;
    }

    /// <summary>
    /// Is 'feet' REAL cover, i.e. a wall between it and every player? (Even if they turn around, they can't see me.)
    /// "Off the edge of their screen" is NOT real cover: one small turn and they see me again.
    /// </summary>
    bool IsRealCover(Vector3 feet)
    {
        foreach (var p in PlayerAIState.All)
            if (p.HasLineOfSightToBodyAt(feet, p.maxViewDistance)) return false;
        return true;
    }

    /// <summary>
    /// Turn my own colliders off while testing other spots, otherwise my own body would
    /// "block the view" and a spot behind me would wrongly look hidden.
    /// </summary>
    void SetCollidersEnabled(bool on)
    {
        foreach (var c in colliders) c.enabled = on;
    }

    // =====================================================================
    // STATES
    // =====================================================================
    void Enter(MonsterState next)
    {
        MonsterState previous = State;
        State = next;
        stateEnteredAt = Time.time;
        closeUnseenFor = 0f;
        if (next != MonsterState.Hunt) IsFakeHunt = false;
        if (next != MonsterState.Search) { IsBanging = false; BangingOn = null; }
        if (next != MonsterState.Wander) guardGem = null;
        // Spotted while leaning out of a peek: the way back is the peek's duck point (one step behind the corner).
        // Spotted while leaning out to watch: the way back is where it leaned out from.
        if (next == MonsterState.Spotted)
        {
            hasEscapeDuck = (peeking && peekSteppedOut) || leaning;
            escapeDuckPoint = leaning ? leanHome : peekDuckPoint;
        }
        if (next != MonsterState.Stalk) { leaning = false; baiting = false; }
        if (next != MonsterState.Stalk && peeking) StopPeeking();
        if (next != MonsterState.Stalk) cornerWaitUntil = 0f;
        if (next != MonsterState.Hunt) { attackPhase = AttackPhase.None; recoverUntil = 0f; }
        if (next != MonsterState.Hide) hideGuardGem = null;
        if (next == MonsterState.Wander)
        {
            wanderPauseUntil = -1f;
            sawPlayerHideIn = null;
            agent.ResetPath();
            resumeStalkAt = Time.time + profile.breatherAfterLosingYou;   // a breather before a tracking stalker comes back
        }

        // A hunt just ended: forget that you were looking at me during it (otherwise I'd be "spotted" instantly).
        if (previous == MonsterState.Hunt) watchedFor = 0f;
        // An encounter just ended (I slipped away): the stare meter starts fresh. No leftovers piling up.
        if (previous == MonsterState.Spotted && next == MonsterState.Stalk) WatchPressure = 0f;

        StateChanged?.Invoke(this, previous, next);

        if (next == MonsterState.Spotted) OnSpotted();
    }

    // ---------- WANDER: roam (usually drifting toward the players) until it sees or hears someone ----------
    void UpdateWander()
    {
        agent.speed = profile.wanderSpeed;
        agent.stoppingDistance = 0.5f;

        // Worn out after a chase (the Brute): stands there, heaving, glaring at you. Your chance to get away.
        if (IsResting)
        {
            agent.isStopped = true;
            if (CanMoveNow && CanSeeTarget) FaceTarget();
            return;
        }

        if (CanSeeTarget)
        {
            if (profile.followStyle == FollowStyle.None) { if (Time.time >= huntAllowedAt) StartHunt(false); }
            else Enter(MonsterState.Stalk);
            return;
        }

        // A tracking stalker doesn't need to see you: after a short breather, it starts circling you again.
        // (Not while you're inside a hiding spot: then it really doesn't know where you are.)
        bool canTrack = profile.alwaysTracksPlayers && profile.followStyle != FollowStyle.None && !Target.IsHiding;
        if (canTrack && Time.time >= resumeStalkAt && guardGem == null)
        {
            Enter(MonsterState.Stalk);
            return;
        }

        // Heard something just now? Go check it out.
        if (Time.time - lastHeardTime < 0.1f)
        {
            agent.SetDestination(lastKnownPosition);
            wanderPauseUntil = -1f;
            guardGem = null;   // (stops guarding a gem to go investigate)
            return;
        }

        if (profile.wanderStyle == WanderStyle.StayStill) return;

        // Guarding a gem? That has its own little routine.
        if (guardGem != null) { UpdateGuarding(); return; }

        // Arrived? Pause a moment, then pick a new spot (or go guard a gem).
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f)
        {
            if (wanderPauseUntil < 0f)
                wanderPauseUntil = Time.time + Random.Range(profile.wanderPause.x, profile.wanderPause.y);
            else if (Time.time >= wanderPauseUntil)
            {
                if (TryStartGuarding()) return;
                if (PickWanderPoint(out Vector3 p))
                {
                    agent.SetDestination(p);
                    wanderPauseUntil = -1f;
                }
            }
        }
    }

    // ---------- GUARD A GEM: the players have to come here eventually, so wait for them ----------
    bool TryStartGuarding()
    {
        if (!profile.guardGems || Gem.All.Count == 0 || Time.time < guardAllowedAt) return false;
        if (!GuardGemChance.Roll()) return false;

        Gem gem = PickGemToGuard();
        if (gem == null) return false;   // no gems near the players right now

        // Option A: climb into a locker right by the gem, and burst out when someone grabs it.
        if (profile.canHide && Random.value < profile.guardFromLockerChance)
        {
            HidingSpot spot = FreeSpotNear(gem.Position, profile.guardRadius);
            if (spot != null)
            {
                hideSpot = spot;
                hidWhileWandering = true;
                Enter(MonsterState.Hide);
                hideGuardGem = gem;   // (set after Enter, which clears it)
                return true;
            }
        }

        // Option B: lurk just around a corner from the gem, out of its sight.
        if (!FindLurkSpotNear(gem.Position, out guardPoint)) return false;
        guardGem = gem;
        guardUntil = -1f;   // the waiting timer starts once I get there
        agent.SetDestination(guardPoint);
        return true;
    }

    void UpdateGuarding()
    {
        // Someone took the gem already: nothing left to guard.
        if (!guardGem.isActiveAndEnabled) { StopGuarding(); return; }

        if (guardUntil < 0f)
        {
            // Sneak over there
            agent.speed = profile.stalkSpeed;
            agent.stoppingDistance = 0.3f;
            if (!agent.pathPending && agent.remainingDistance <= 0.6f)
                guardUntil = Time.time + Random.Range(profile.guardSeconds.x, profile.guardSeconds.y);
            return;
        }

        // Wait, facing the gem. (It sees you if you walk into view -> Stalk, handled at the top of UpdateWander.)
        if (CanMoveNow) FaceTowards(guardGem.Position);
        if (Time.time >= guardUntil)
        {
            Learn(GuardGemChance, -0.05f);   // LEARNING: nobody came, guarding was a waste of time
            StopGuarding();
        }
    }

    void StopGuarding()
    {
        guardGem = null;
        guardAllowedAt = Time.time + 8f;   // don't go straight back to another gem
        wanderPauseUntil = -1f;
        agent.ResetPath();
    }

    /// <summary>
    /// Which gem? The one nearest the players (where they're probably heading next), but only if it's near them:
    /// never a gem on the other side of the map. Skips gems they're about to grab anyway.
    /// (Like drifting, this "knows" where players are, for now.) Returns null if there's no good gem.
    /// </summary>
    Gem PickGemToGuard()
    {
        Gem best = null;
        float bestDistance = profile.guardOnlyGemsWithin;
        foreach (var g in Gem.All)
        {
            float d = Vector3.Distance(g.Position, Target.transform.position);
            if (d < 8f) continue;   // too late to set up there
            if (d <= bestDistance) { bestDistance = d; best = g; }
        }
        return best;
    }

    /// <summary>The free locker closest to 'point' (within 'radius') whose door no player can see right now.</summary>
    HidingSpot FreeSpotNear(Vector3 point, float radius)
    {
        HidingSpot best = null;
        float bestDistance = radius;
        SetCollidersEnabled(false);
        foreach (var s in HidingSpot.All)
        {
            if (!s.IsFree) continue;
            float d = Vector3.Distance(s.ExitPosition, point);
            if (d > bestDistance || AnyPlayerCanSee(s.ExitPosition)) continue;
            bestDistance = d;
            best = s;
        }
        SetCollidersEnabled(true);
        return best;
    }

    /// <summary>
    /// A spot a few metres from the gem, but around a corner, so you can't see it while you walk up to the gem.
    /// Must be a short WALK from the gem (not "close" but on the other side of a wall).
    /// </summary>
    bool FindLurkSpotNear(Vector3 gem, out Vector3 best)
    {
        best = gem;
        float bestScore = float.MaxValue;
        bool foundHidden = false;
        if (!NavMesh.SamplePosition(gem, out NavMeshHit gemHit, 3f, NavMesh.AllAreas)) return false;

        SetCollidersEnabled(false);
        float[] rings = { 4f, 6f, 8f, 10f };
        for (int r = 0; r < rings.Length; r++)
        {
            if (rings[r] > profile.guardRadius) break;
            for (int d = 0; d < 8; d++)
            {
                float angle = d * 45f + Random.Range(-20f, 20f);
                Vector3 candidate = gem + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * rings[r];
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                if (AnyPlayerCanSee(hit.position)) continue;

                if (!NavMesh.CalculatePath(gemHit.position, hit.position, NavMesh.AllAreas, scratchPath)) continue;
                if (scratchPath.status != NavMeshPathStatus.PathComplete) continue;
                float walk = PathLength(scratchPath);
                if (walk > profile.guardRadius * 1.5f || walk < 3f) continue;

                // Hidden from the gem = a wall is in the way between the gem and where my head would be.
                bool hiddenFromGem = Physics.Linecast(gem, hit.position + Vector3.up * 1.6f, ~0, QueryTriggerInteraction.Ignore);
                if (foundHidden && !hiddenFromGem) continue;   // once I have a hidden option, only hidden ones count

                float score = Mathf.Abs(walk - 6f);   // about 6 m of walking from the gem feels right
                if (hiddenFromGem && !foundHidden) { foundHidden = true; bestScore = float.MaxValue; }
                if (score < bestScore) { bestScore = score; best = hit.position; }
            }
        }
        SetCollidersEnabled(true);
        return bestScore < float.MaxValue;
    }

    /// <summary>
    /// A player picked up a gem somewhere. (GameObjective announces it; every monster hears about it.)
    /// </summary>
    void OnGemCollected(Gem gem, PlayerAIState player)
    {
        if (profile == null || agent == null) return;

        // Hiding in a locker by that gem? BURST OUT. This is the payoff of guarding.
        if (IsHiddenInSpot)
        {
            if (hideGuardGem == gem || Vector3.Distance(hideSpot.ExitPosition, gem.Position) <= profile.guardRadius)
            {
                LeaveSpot();
                Learn(GuardGemChance, +0.1f);   // LEARNING: guarding gems works
                StartHunt(false);
                huntStartsAt = Time.time + 0.4f;   // a tiny beat, the door flies open...
                huntEndsAt = Time.time + 6f;       // ...then a short, explosive chase
            }
            return;
        }

        if (!agent.enabled || !agent.isOnNavMesh) return;

        float distance = Vector3.Distance(transform.position, gem.Position);

        // Lurking by that gem, or just very close to it? CHARGE.
        bool guardingThisGem = guardGem == gem;
        if ((guardingThisGem || distance <= profile.guardRadius) && State != MonsterState.Hunt)
        {
            if (guardingThisGem) Learn(GuardGemChance, +0.1f);   // LEARNING: guarding gems works
            StartHunt(false);
            return;
        }

        if (distance > profile.gemPickupHearingRange)
        {
            // Too far to hear it. LEARNING: they're collecting gems while I'm away, so guard them more.
            Learn(GuardGemChance, +0.1f);
            return;
        }

        // Heard it: go and look (same as hearing a footstep, see UpdateWander).
        lastKnownPosition = player.transform.position;
        lastHeardTime = Time.time;
    }

    bool PickWanderPoint(out Vector3 point)
    {
        // DriftTowardPlayers: usually head for the players' ROUGH area, so it slowly closes in.
        // (Later, the particle filter from the design doc replaces this "cheat" with a real guess.)
        // (Not toward a player hiding in a locker: it doesn't know where they are.)
        if (profile.wanderStyle == WanderStyle.DriftTowardPlayers && !Target.IsHiding && Random.value < profile.driftChance)
            return RandomNavPoint(Target.transform.position, profile.driftFuzziness, out point);

        return RandomNavPoint(transform.position, profile.wanderRadius, out point);
    }

    // ---------- STALK: circle the player on THE RING, out of sight; peek; scare; attack when the ring is small ----------
    void UpdateStalk()
    {
        // A monster that doesn't stalk (the Brute) never stays here: it just goes back to roaming.
        if (profile.followStyle == FollowStyle.None) { Enter(MonsterState.Wander); return; }

        agent.speed = profile.stalkSpeed;

        // 1) Ducked around a corner after being spotted: wait there. Follow me too closely, and I get you.
        //    (Spot me from further away and it's a normal "spotted" below: I slip away again.)
        if (IsWaitingAroundCorner)
        {
            agent.isStopped = true;
            if (CanMoveNow) FaceTarget();
            if (DistanceToTarget <= profile.cornerAmbushDistance && CanMoveNow)
            {
                cornerWaitUntil = 0f;
                StartHunt(false);   // (it still roars first: a split second to react)
                return;
            }
            if (watchedFor < profile.secondsWatchedBeforeSpotted) return;
            cornerWaitUntil = 0f;
        }

        // 1b) Baiting you from down the hallway: it WANTS to be seen, so being watched doesn't make it leave.
        if (baiting)
        {
            if (UpdateBait()) return;
            watchedFor = 0f;   // (don't count the bait as "being spotted" the moment it ends)
        }

        // 2) Has a player been looking at me? -> Spotted (and the ring is pushed back)
        if (watchedFor >= profile.secondsWatchedBeforeSpotted)
        {
            if (!peeking) Learn(Boldness, -0.3f);   // LEARNING: I got caught stalking, so be more careful
            OnSpottedRing();                         // (no boldness lesson if I WANTED to be seen)
            Enter(MonsterState.Spotted);
            return;
        }

        // 3) Does my rule say "hunt now"? (Default rule with the ring: the ring has shrunk all the way.)
        if (Time.time >= huntAllowedAt && rule.WantsToHunt(this))
        {
            StartHunt(false);
            return;
        }

        // 4) Nobody has seen me in a while? Peek around a corner in front of them.
        //    (But if they've IGNORED me for a long time, I stop showing off and just close in. See UpdateRing.)
        if (peeking)
        {
            if (IsBeingIgnored) StopPeeking();
            else if (UpdatePeeking()) return;
        }
        else if (profile.peekWhenUnseen && UnseenFor >= profile.peekAfterUnseen && !IsBeingIgnored &&
                 Time.time >= peekAllowedAt && !IsLurking &&
                 ((Random.value < profile.lockerPeekChance && TryStartLockerPeek()) || TryStartPeeking()))
            return;

        // 5) Every few seconds: maybe a scare (fake hunt), or (rarely) go hide in a locker ahead of you.
        if (Time.time >= nextScareCheck)
        {
            nextScareCheck = Time.time + profile.scareCheckInterval;
            float bold = Boldness.current;
            // "ANGRY": a small chance it just attacks (only when it's close-ish: within its ring)
            bool nearYou = DistanceToTarget <= CurrentStalkDistance() + 4f;
            if (nearYou && Time.time >= huntAllowedAt &&
                Random.value < profile.realHuntChance + bold * profile.boldnessHuntBonus) { StartHunt(false); return; }
            if (Time.time >= scareAllowedAt && FakeHuntChance.Roll()) { StartHunt(true); return; }
            float hideChance = HideAmbushChance.current * (1f - bold * profile.boldnessHideReduction);
            if (profile.canHide && Time.time >= hideAllowedAt && Random.value < hideChance && TryStartHiding()) return;
        }

        // 6) LEARNING: staying close without being seen makes me bolder
        if (!IsWatched && DistanceToTarget < CurrentStalkDistance() + 2f)
        {
            closeUnseenFor += Time.deltaTime;
            if (closeUnseenFor > 5f)
            {
                Learn(Boldness, +0.1f);
                closeUnseenFor = 0f;
            }
        }

        // 7) Just slipped out of sight? Lurk where I am for a bit, so they lose track of me.
        if (Time.time < lurkUntil)
        {
            if (CanMoveNow) FaceTarget();
            return;
        }

        // 7b) WATCHING YOU: waiting near you behind a corner? Now and then lean out to watch you, then lean back.
        if (leaning) { UpdateLeaning(); return; }
        bool settledAtSpot = !agent.pathPending && agent.remainingDistance < 0.6f;
        if (profile.leanOutToWatch && settledAtSpot && Time.time >= nextLeanTime && IsFullyHidden && !IsBeingIgnored &&
            TryStartLeaning())
            return;

        // 8) Move to my spot (on the ring, or behind the player). Re-plan a couple of times a second.
        if (Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + (profile.useRing ? 0.5f : 0.25f);
            agent.SetDestination(StalkDestination());
        }

        // SNEAKY: never stroll across a player's view. If the next bit of my route is visible, wait for them to
        // look away... and if they don't, teleport instead (so I'm still always around).
        if (profile.sneakyMovement && !IsWatched)
        {
            if (Time.time >= nextSneakCheck)
            {
                nextSneakCheck = Time.time + 0.25f;
                pathAheadExposed = PathAheadIsExposed();
            }
            if (pathAheadExposed)
            {
                agent.isStopped = true;
                stuckSince = -1f;   // (waiting on purpose isn't "stuck")
                if (CanMoveNow) FaceTarget();
                if (exposedSince < 0f) exposedSince = Time.time;
                if (Time.time - exposedSince > profile.waitForClearPathSeconds && IsFullyHidden && VanishSomewhere(flash: false))
                {
                    exposedSince = -1f;
                    pathAheadExposed = false;
                    nextRepathTime = 0f;
                }
                return;
            }
        }
        exposedSince = -1f;

        // Long way round, stuck, or stranded? Teleport near you instead (only while nobody can see me).
        if (ShouldTeleportToRing() && VanishSomewhere(flash: false))   // behind the scenes: no flash
        {
            stuckSince = -1f;
            ringSpotUnreachable = false;
            nextRepathTime = 0f;
            return;
        }

        bool farFromSpot = !agent.pathPending && agent.remainingDistance > 8f;
        agent.speed = farFromSpot ? profile.catchUpSpeed : profile.stalkSpeed;
        if (CanMoveNow && !agent.pathPending && agent.remainingDistance < 0.6f) FaceTarget();
    }

    /// <summary>How far back it stalks. With the ring: the ring. Without: bolder = closer.</summary>
    public float CurrentStalkDistance() => profile.useRing
        ? Ring
        : Mathf.Lerp(profile.maxStalkDistance, profile.minStalkDistance, Boldness.current);

    Vector3 StalkDestination()
    {
        Vector3 playerPos = CanSeeTarget ? Target.transform.position : lastKnownPosition;

        if (profile.followStyle == FollowStyle.CreepWhenUnseen)
        {
            agent.stoppingDistance = 1f;
            return playerPos;   // walk straight at them (the rule decides when it may move)
        }

        agent.stoppingDistance = 0.5f;
        if (profile.useRing) return BestRingSpot(playerPos);

        // StalkFromBehind (ring off): aim for a point BEHIND the player's back.
        Vector3 behind = playerPos - Target.FlatLookDirection * CurrentStalkDistance();
        if (NavMesh.SamplePosition(behind, out NavMeshHit hit, 4f, NavMesh.AllAreas)) return hit.position;
        return transform.position;   // no good spot, so stay put
    }

    /// <summary>
    /// THE RING: test 16 spots in a circle around the player (radius = the ring) and pick the best one.
    /// Lower score = better:
    ///   + a big penalty if ANY player could see me standing there (stay hidden!)
    ///   + a penalty the more it's in FRONT of the player (behind them is best)
    ///   + how far off the ring it is (walls push spots around)
    ///   + half the walking distance (don't run laps around them)
    /// </summary>
    Vector3 BestRingSpot(Vector3 playerPos)
    {
        Vector3 best = transform.position;
        float ring = Ring;
        Vector3 look = Target.FlatLookDirection;
        // The ring has (almost) closed: time to come for you. It moves where it could SEE you (and you could see it
        // if you turned around), instead of waiting behind a wall and rounding the corner too fast to react.
        bool finalApproach = ring <= profile.ringAttackRadius + 1f;

        SetCollidersEnabled(false);
        for (int i = 0; i < 16; i++)
        {
            float angle = i * 22.5f + Random.Range(-8f, 8f);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            if (!NavMesh.SamplePosition(playerPos + dir * ring, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, scratchPath)) continue;
            if (scratchPath.status != NavMeshPathStatus.PathComplete) continue;

            float score = PathLength(scratchPath) * 0.5f;
            Vector3 fromPlayer = hit.position - playerPos;
            fromPlayer.y = 0f;
            score += Mathf.Abs(fromPlayer.magnitude - ring);
            score += (1f + Vector3.Dot(look, fromPlayer.normalized)) * 10f;  // 0 directly behind, 20 in front (no stumbling into it)
            if (AnyPlayerCanSee(hit.position)) score += 40f;                  // on someone's screen right now: terrible
            else if (finalApproach) { if (!ClearLineToTarget(hit.position)) score += 25f; }   // closing in: must SEE you
            else if (!IsRealCover(hit.position)) score += 20f;               // one turn and they would see me: avoid
            if (Time.time < walkAwayUntil && Vector3.Distance(hit.position, walkAwayFrom) < 10f) score += 30f;   // walked off: not back here
            spotCandidates.Add((hit.position, score));
        }
        ringSpotUnreachable = spotCandidates.Count == 0;   // nowhere reachable at all: probably stranded
        if (!ringSpotUnreachable) PickBestCornerSpot(out best);   // NEVER STAND IN THE OPEN: prefer spots at a corner
        SetCollidersEnabled(true);
        return best;
    }

    // Reused list of (spot, score) while choosing places to stand (no new memory every time).
    readonly List<(Vector3 spot, float score)> spotCandidates = new List<(Vector3 spot, float score)>();

    /// <summary>
    /// From the scored candidates (lower = better): the best one that is AT A CORNER. If none of the top few are,
    /// the best one anyway. Clears the list. (Call with my colliders switched off.)
    /// </summary>
    bool PickBestCornerSpot(out Vector3 best)
    {
        best = transform.position;
        if (spotCandidates.Count == 0) return false;
        spotCandidates.Sort((a, b) => a.score.CompareTo(b.score));
        best = spotCandidates[0].spot;
        int toCheck = Mathf.Min(5, spotCandidates.Count);   // corner checks are expensive: only the best few
        for (int i = 0; i < toCheck; i++)
        {
            if (!IsAtACorner(spotCandidates[i].spot)) continue;
            best = spotCandidates[i].spot;
            break;
        }
        spotCandidates.Clear();
        return true;
    }

    /// <summary>
    /// "At a corner" = right up against a wall, with a fully hidden spot one step away.
    /// Standing here, it can always just step back out of sight. Never stuck in the open.
    /// </summary>
    bool IsAtACorner(Vector3 feet)
    {
        if (!NavMesh.FindClosestEdge(feet, out NavMeshHit edge, NavMesh.AllAreas) || edge.distance > 1.5f) return false;
        return FindDuckPoint(feet, out _);
    }

    /// <summary>
    /// Would walking the next couple of metres of my route put me in a player's view?
    /// Checks the next corner of the path and a point ~2.5 m ahead.
    /// </summary>
    bool PathAheadIsExposed()
    {
        if (agent.pathPending || !agent.hasPath || agent.remainingDistance < 0.6f) return false;
        Vector3 next = agent.steeringTarget;   // the next corner of the path
        Vector3 toNext = next - transform.position;
        toNext.y = 0f;
        Vector3 soon = transform.position + toNext.normalized * Mathf.Min(2.5f, toNext.magnitude);

        SetCollidersEnabled(false);
        bool exposed = AnyPlayerCanSee(soon) || AnyPlayerCanSee(next);
        SetCollidersEnabled(true);
        return exposed;
    }

    /// <summary>
    /// Teleport to the ring instead of walking when: the walk is long, I'm stuck, or I can't reach the ring at all.
    /// Only when nobody can see me, and not too often.
    /// </summary>
    bool ShouldTeleportToRing()
    {
        if (!profile.canTeleport || !profile.useRing || !IsFullyHidden) return false;
        if (Time.time - lastTeleportTime < profile.teleportCooldown) return false;

        bool longWalk = !agent.pathPending && agent.remainingDistance > profile.teleportIfWalkFartherThan &&
                        DistanceToTarget > profile.quietTeleportOnlyFartherThan;   // only when far from you

        // Stuck: trying to move but not getting anywhere for 3 seconds
        bool tryingToMove = !agent.pathPending && agent.remainingDistance > 1.5f;
        if (tryingToMove && agent.velocity.magnitude < 0.2f) { if (stuckSince < 0f) stuckSince = Time.time; }
        else stuckSince = -1f;
        bool stuck = stuckSince > 0f && Time.time - stuckSince > 3f;

        return longWalk || stuck || ringSpotUnreachable;
    }

    // ---------- PEEK: let them know something's there ----------
    // If nobody has seen me for a while, I peek around a CORNER in front of the player: only part of me shows
    // (a shoulder, my head). Every peek spot has a DUCK POINT one step back that's fully behind the wall, so:
    //   1) I get to the duck point first (hidden; teleporting there if it's far and nobody can see me),
    //   2) then I slowly lean out to the peek spot,
    //   3) and when spotted, I just step back to the duck point. No long awkward run across a hallway.
    // (Later, with real animations, this is where the "head poking around the corner" pose plays.)
    bool TryStartPeeking()
    {
        if (!FindPeekSpot(out peekPoint, out peekDuckPoint))
        {
            peekAllowedAt = Time.time + 4f;   // nowhere good right now; try again soon
            return false;
        }
        peeking = true;
        peekSteppedOut = false;
        peekEndsAt = Time.time + 25f;     // getting there + waiting, at most
        peekStandUntil = -1f;
        GoToPeekDuckPoint();
        return true;
    }

    /// <summary>Head for the hidden side of the corner. Teleport there if it's a long or exposed walk (and nobody sees me).</summary>
    void GoToPeekDuckPoint()
    {
        peekSteppedOut = false;
        bool farWalk = Vector3.Distance(transform.position, peekDuckPoint) > 8f;
        if (profile.canTeleport && farWalk && IsFullyHidden)
        {
            // (behind the scenes: no flash. The flash is for teleports you're MEANT to notice.)
            agent.Warp(peekDuckPoint);
            agent.ResetPath();
            lastTeleportTime = Time.time;
        }
        else agent.SetDestination(peekDuckPoint);
    }

    /// <summary>Returns true while still peeking (it handles movement), false once it gives up.</summary>
    bool UpdatePeeking()
    {
        agent.stoppingDistance = 0.15f;
        bool arrived = !agent.pathPending && agent.remainingDistance < 0.35f;

        if (!peekSteppedOut)
        {
            // Phase 1: getting to the hidden side of the corner
            agent.speed = profile.stalkSpeed;
            if (arrived)
            {
                peekSteppedOut = true;
                agent.SetDestination(peekPoint);   // phase 2: lean out
            }
        }
        else
        {
            agent.speed = 1.2f;   // leaning out slowly
            if (arrived)
            {
                if (peekStandUntil < 0f) peekStandUntil = Time.time + profile.peekMaxSeconds;
                if (CanMoveNow) FaceTarget();   // just... looking at you. From around the corner.
            }
        }

        // The player moved somewhere else: find a new corner in front of them (once a second at most).
        if (Time.time >= nextRingPick)
        {
            nextRingPick = Time.time + 1f;
            SetCollidersEnabled(false);   // (my own body would block the check)
            int seen = MostVisiblePoints(peekPoint);
            SetCollidersEnabled(true);
            if ((seen == 0 || seen > profile.peekMaxVisiblePoints) && FindPeekSpot(out Vector3 newSpot, out Vector3 newDuck))
            {
                peekPoint = newSpot;
                peekDuckPoint = newDuck;
                peekStandUntil = -1f;
                GoToPeekDuckPoint();
            }
        }

        bool gaveUp = Time.time >= peekEndsAt || (peekStandUntil > 0f && Time.time >= peekStandUntil);
        if (gaveUp) { StopPeeking(); return false; }
        return true;
    }

    void StopPeeking()
    {
        peeking = false;
        peekStandUntil = -1f;
        peekAllowedAt = Time.time + 12f;
    }

    /// <summary>
    /// A peek spot in front of the player, inside the ring, where they'd see only a FEW of my 9 body points
    /// (an edge of me past a corner), AND with a duck point one step away that's fully behind a wall.
    /// The shortest walk (to the duck point) wins.
    /// </summary>
    bool FindPeekSpot(out Vector3 best, out Vector3 bestDuck)
    {
        best = bestDuck = transform.position;
        float bestWalk = float.MaxValue;
        Vector3 look = Target.FlatLookDirection;
        float far = Mathf.Max(profile.ringAttackRadius + 4f, profile.useRing ? Ring : 16f);
        float near = Mathf.Max(profile.ringAttackRadius + 2f, far * 0.5f);

        SetCollidersEnabled(false);
        for (int i = 0; i < 30; i++)
        {
            Vector3 candidate = Target.transform.position +
                                Quaternion.Euler(0f, Random.Range(-45f, 45f), 0f) * look * Random.Range(near, far);
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            int seen = MostVisiblePoints(hit.position);
            if (seen < 1 || seen > profile.peekMaxVisiblePoints) continue;   // must be PARTLY visible: a peek
            if (!FindDuckPoint(hit.position, out Vector3 duck)) continue;     // must be right at a corner

            if (!NavMesh.CalculatePath(transform.position, duck, NavMesh.AllAreas, scratchPath)) continue;
            if (scratchPath.status != NavMeshPathStatus.PathComplete) continue;
            float walk = PathLength(scratchPath);
            if (walk < bestWalk) { bestWalk = walk; best = hit.position; bestDuck = duck; }
        }
        SetCollidersEnabled(true);
        return bestWalk < float.MaxValue;
    }

    /// <summary>
    /// A point one step away (up to ~1.5 m, straight walkable line) that is REAL cover: behind a wall from every
    /// player. If it exists, 'feet' is right at a corner. (Call with my colliders switched off.)
    /// </summary>
    bool FindDuckPoint(Vector3 feet, out Vector3 duck, float maxStep = 1.5f)
    {
        duck = feet;
        for (float step = 0.8f; step <= maxStep + 0.01f; step += 0.7f)
        {
            for (int d = 0; d < 8; d++)
            {
                Vector3 c = feet + Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward * step;
                if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 0.6f, NavMesh.AllAreas)) continue;
                if (NavMesh.Raycast(feet, hit.position, out _, NavMesh.AllAreas)) continue;   // something in the way
                if (!IsRealCover(hit.position)) continue;
                duck = hit.position;
                return true;
            }
        }
        return false;
    }

    // ---------- LEAN OUT: it's always watching you ----------
    // Waiting behind a corner near you, it leans out a step or two so that only an edge of it sticks out,
    // watches you for a few seconds, and leans back. If you happen to look: a head around the corner.
    bool TryStartLeaning()
    {
        nextLeanTime = Time.time + Random.Range(profile.leanOutEvery.x, profile.leanOutEvery.y);
        if (!FindLeanSpot(out leanPoint)) return false;
        leaning = true;
        leanReturning = false;
        leanHome = transform.position;
        leanHoldUntil = -1f;
        agent.SetDestination(leanPoint);
        return true;
    }

    void UpdateLeaning()
    {
        agent.speed = 1.2f;                 // slowly
        agent.stoppingDistance = 0.15f;
        agent.updateRotation = false;       // keep facing you the whole time
        if (CanMoveNow) FaceTarget();
        bool arrived = !agent.pathPending && agent.remainingDistance < 0.3f;

        if (!leanReturning)
        {
            if (arrived && leanHoldUntil < 0f)
                leanHoldUntil = Time.time + Random.Range(profile.leanOutHoldSeconds.x, profile.leanOutHoldSeconds.y);
            bool doneWatching = leanHoldUntil > 0f && Time.time >= leanHoldUntil;
            bool youWalkedOff = Vector3.Distance(Target.transform.position, leanHome) > Ring + 8f;
            if (doneWatching || youWalkedOff)
            {
                leanReturning = true;
                agent.SetDestination(leanHome);   // lean back behind the corner
            }
        }
        else if (arrived) leaning = false;
    }

    /// <summary>A spot 1-3 m from me (straight walkable line) where only an EDGE of me would be in a player's line of sight.</summary>
    bool FindLeanSpot(out Vector3 spot)
    {
        spot = transform.position;
        SetCollidersEnabled(false);
        float[] steps = { 1f, 2f, 3f };
        foreach (float step in steps)
        {
            for (int d = 0; d < 8; d++)
            {
                Vector3 c = transform.position + Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward * step;
                if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 0.6f, NavMesh.AllAreas)) continue;
                if (NavMesh.Raycast(transform.position, hit.position, out _, NavMesh.AllAreas)) continue;
                int seen = 0;
                foreach (var p in PlayerAIState.All) seen = Mathf.Max(seen, p.CountLineOfSightPoints(hit.position, p.maxViewDistance));
                if (seen < 1 || seen > 4) continue;   // just an edge: a head, a shoulder
                spot = hit.position;
                SetCollidersEnabled(true);
                return true;
            }
        }
        SetCollidersEnabled(true);
        return false;
    }

    /// <summary>The most body points (0-9) any player could see if I stood at 'feet'.</summary>
    int MostVisiblePoints(Vector3 feet)
    {
        int most = 0;
        foreach (var p in PlayerAIState.All) most = Mathf.Max(most, p.CountVisiblePoints(feet));
        return most;
    }

    // ---------- SPOTTED: a player saw me ----------
    // THE TRICKSTER: it loves catching YOU off guard, but hates being caught off guard.
    //   Spotted at a CORNER (it saw you first): it holds your gaze, then backs away behind the corner. Then it
    //     either really leaves (teleport, or BAIT you down a hallway), or (rarely) stays right there.
    //   Spotted in the OPEN (you caught it off guard): it stands dead still and stares. Look away: it's gone.
    //     Keep staring (stare meter) or walk up to it: it attacks.
    // It always FACES you. It never turns its back on you.
    void OnSpotted()
    {
        // After a hunt that ran out next to you: just back off, right away. No staring, no attacking.
        if (retreatAfterHunt)
        {
            retreatAfterHunt = false;
            CurrentReaction = SpottedReaction.StareThenHide;
            stareUntil = Time.time;
            reactionStarted = false;
            hasEscapeDuck = false;
            return;
        }

        CurrentReaction = profile.spottedReactions.Pick();
        stareUntil = Time.time + Random.Range(profile.stareSeconds.x, profile.stareSeconds.y);
        freezeUntil = Time.time + Random.Range(profile.maxFreezeSeconds.x, profile.maxFreezeSeconds.y);
        freezeStartDistance = DistanceToTarget;
        reactionStarted = false;

        // Remember where I've been spotted, so "still there... still there..." stays rare.
        bool samePlace = Time.time - lastSpottedTime < 30f && Vector3.Distance(transform.position, lastSpottedPos) < 6f;
        spottedHereCount = samePlace ? spottedHereCount + 1 : 1;
        if (!samePlace)
        {
            staysAllowedHere = 1;
            while (staysAllowedHere < 4 && Random.value < profile.extraStayChance) staysAllowedHere++;
        }
        lastSpottedPos = transform.position;
        lastSpottedTime = Time.time;

        // Right next to a corner? Then escaping is just one step back behind it, not a long run.
        SetCollidersEnabled(false);
        if (!hasEscapeDuck) hasEscapeDuck = FindDuckPoint(transform.position, out escapeDuckPoint);
        bool cornerNearby = hasEscapeDuck || FindDuckPoint(transform.position, out _, profile.openNoCornerWithin);
        SetCollidersEnabled(true);

        if (CurrentReaction == SpottedReaction.Vanish) { VanishSomewhere(); Enter(MonsterState.Stalk); return; }
        if (CurrentReaction == SpottedReaction.Chase) { StartHunt(false); return; }

        // CAUGHT OFF GUARD in the open: no running. It stops and stares.
        if (profile.standAndStareWhenCaughtInOpen && !cornerNearby) CurrentReaction = SpottedReaction.StandAndStare;
    }

    void UpdateSpotted()
    {
        if (CurrentReaction == SpottedReaction.StandAndStare)
        {
            agent.isStopped = true;
            if (CanMoveNow) FaceTarget();
            // You looked away (even briefly): it's gone, with a purple flash.
            if (UnseenFor >= profile.standAndStareVanishAfterUnseen)
            {
                if (!VanishSomewhere()) lurkUntil = Time.time + 1f;
                Enter(MonsterState.Stalk);
                return;
            }
            // You walked up to it: big mistake. (Staring too long is handled by the stare meter.)
            // (Not during the breather right after a hunt.)
            if (CanMoveNow && DistanceToTarget <= profile.standAndStareAttackDistance && Time.time >= graceUntil)
                StartHunt(false);
            return;
        }

        if (CurrentReaction == SpottedReaction.Freeze)
        {
            // Stand still and stare. Look away and it goes back to stalking...
            agent.isStopped = true;
            if (CanMoveNow) FaceTarget();
            if (IsFullyHidden) { Enter(MonsterState.Stalk); return; }

            // ...walk toward it and it SNAPS: attack, or get away.
            bool approached = profile.freezeBreakDistance > 0f &&
                              (DistanceToTarget <= profile.freezeBreakDistance ||
                               DistanceToTarget <= freezeStartDistance - profile.freezeBreakApproach);
            if (approached && CanMoveNow)
            {
                if (FreezeBreakAttackChance.Roll()) { StartHunt(false); return; }
                CurrentReaction = SpottedReaction.Flee;
                reactionStarted = true;
                reactionEndsAt = Time.time + 20f;
                nextHidePick = 0f;
                return;
            }

            // Safety net: if neither of you moves for a long time, it slips away to hide.
            if (Time.time >= freezeUntil && CanMoveNow) { CurrentReaction = SpottedReaction.StareThenHide; stareUntil = Time.time; }
            return;
        }

        // StareThenHide / Flee: first it holds your gaze (the "it saw me see it" moment)...
        if (!reactionStarted)
        {
            agent.isStopped = true;
            if (CanMoveNow) FaceTarget();
            if (Time.time < stareUntil) return;

            // ...then it goes.
            reactionStarted = true;
            retreatStartedAt = Time.time;
            retreatStartDistance = DistanceToTarget;
            reactionEndsAt = Time.time + 20f;
            nextHidePick = 0f;
        }

        // Out of sight. Now: really LEAVE (bait you down a hallway, or teleport near you), or (rarely) STAY right
        // there (wait around the corner / lurk). Staying is only allowed a limited number of times per spot, and
        // never when it's only off the edge of your screen (one small turn and you'd see it again).
        if (IsFullyHidden)
        {
            SetCollidersEnabled(false);   // (my own body would count as "a wall in the way")
            bool realCover = IsRealCover(transform.position);
            SetCollidersEnabled(true);

            bool mayStay = realCover && spottedHereCount <= staysAllowedHere && !leavingAfterHunt;
            bool mustTeleport = leavingAfterHunt;   // after a hunt it always poofs out
            leavingAfterHunt = false;
            ChooseWhatToDoAfterSlippingAway(mayStay, mustTeleport);
            Enter(MonsterState.Stalk);
            return;
        }

        // Moving away: ALWAYS facing you. Slowly backing off if cover is close; fast (still facing you) if it's far.
        agent.isStopped = !CanMoveNow;
        agent.stoppingDistance = 0.2f;
        agent.updateRotation = false;
        if (CanMoveNow) FaceTarget();
        bool coverIsClose = !agent.pathPending && agent.remainingDistance <= profile.dashIfCoverFartherThan;
        if (coverIsClose && CurrentReaction != SpottedReaction.Flee)
            agent.speed = profile.retreatSpeed;
        else
        {
            agent.speed = profile.escapeSpeed * (CurrentReaction == SpottedReaction.Flee ? 1.1f : 1f);
            agent.acceleration = profile.escapeAcceleration;
        }

        // Re-plan if: we just started, we arrived and are still seen, or the player can now see where we're going.
        bool arrived = !agent.pathPending && agent.remainingDistance < 0.8f;
        bool needNewSpot = nextHidePick == 0f || arrived || AnyPlayerCanSee(agent.destination);
        if (needNewSpot && Time.time >= nextHidePick)
        {
            nextHidePick = Time.time + 0.5f;
            Vector3 goal;
            if (hasEscapeDuck && CurrentReaction != SpottedReaction.Flee)
            {
                goal = escapeDuckPoint;   // one step back behind the corner
                hasEscapeDuck = false;    // (if that somehow isn't enough, re-plan normally next time)
            }
            else if (CurrentReaction == SpottedReaction.Flee || !TryFindQuickestCover(out goal)) goal = FleePoint();
            agent.SetDestination(goal);
            checkCoverDistance = true;
        }

        // COVER TOO FAR: instead of slowly backing off for ages, it teleports away right now (even if you're watching).
        if (checkCoverDistance && !agent.pathPending && profile.canTeleport)
        {
            checkCoverDistance = false;
            if (agent.remainingDistance > profile.teleportIfCoverFartherThan && VanishSomewhere())
            {
                Enter(MonsterState.Stalk);
                return;
            }
        }

        // DON'T FOLLOW HIM: still watched a while after it started backing away (e.g. circling a pillar with it).
        // Kept up with it? It attacks. Only watched from where you were? It teleports away.
        if (IsWatched && Time.time - retreatStartedAt > profile.maxRetreatSeconds)
        {
            bool youFollowed = DistanceToTarget <= retreatStartDistance + 1.5f;
            if (youFollowed && CanMoveNow && Time.time >= graceUntil) { StartHunt(false); return; }
            if (VanishSomewhere()) { Enter(MonsterState.Stalk); return; }
        }

        // Couldn't get out of sight for a long time: last resort, vanish.
        if (Time.time >= reactionEndsAt)
        {
            VanishSomewhere();
            Enter(MonsterState.Stalk);
        }
    }

    // ---------- AFTER SLIPPING AWAY: the mix ----------
    // Once out of sight after being spotted, it picks ONE of these (by the weights in the profile):
    //   TELEPORT AWAY  - purple flash, gone              (you KNOW it moved... or do you?)
    //   WALK OFF       - no flash, it just moves away    (is it still around here?)
    //   BAIT           - reappears far down the hallway  (how did it get there?)
    //   STAY           - waits right there               (oh no, it's still there)
    // FLASH BUDGET: signature teleports are limited (one every ~20 s) so the flash stays special.
    // If one happened recently, it walks off instead. Never knowing which one you got is the point.
    void ChooseWhatToDoAfterSlippingAway(bool mayStay, bool mustTeleport)
    {
        if (mustTeleport && VanishSomewhere()) return;

        float teleport = profile.canTeleport ? profile.teleportAwayWeight : 0f;
        float walk = profile.walkAwayWeight;
        float bait = profile.canBait && profile.canTeleport ? profile.baitWeight : 0f;
        float stay = mayStay ? profile.stayWeight : 0f;
        float total = teleport + walk + bait + stay;
        if (total <= 0f) { WalkAway(); return; }

        float r = Random.value * total;
        if ((r -= teleport) < 0f) { if (!(FlashBudgetReady && VanishSomewhere())) WalkAway(); return; }
        if ((r -= walk) < 0f) { WalkAway(); return; }
        if ((r -= bait) < 0f) { if (!(FlashBudgetReady && TryStartBait())) WalkAway(); return; }
        cornerWaitUntil = Time.time + profile.cornerWaitSeconds;   // STAY: waiting around the corner
    }

    /// <summary>Walk off (no teleport): for a while, avoid standing anywhere near the spot where it was just seen.</summary>
    void WalkAway()
    {
        walkAwayFrom = transform.position;
        walkAwayUntil = Time.time + 20f;
        lurkUntil = 0f;
        nextRepathTime = 0f;
    }

    bool FlashBudgetReady => Time.time - lastFlashTime >= profile.minSecondsBetweenFlashes;

    // ---------- BAIT: the trickster lures you closer ----------
    // It reappears FAR down the hallway ahead of you (it teleports to the hidden side of the corner at the end,
    // then steps out). It just stands there, watching. Walk toward it and, once you're close enough, it CHARGES.
    // Stare at it too long and the stare meter gets you too. Ignore it and it eventually slips away.
    bool TryStartBait()
    {
        if (!profile.canTeleport || !FindBaitSpot(out baitPoint, out baitDuckPoint)) return false;
        FlashWhereIWas();
        agent.Warp(baitDuckPoint);
        agent.ResetPath();
        lastTeleportTime = Time.time;
        baiting = true;
        baitSteppedOut = false;
        baitStandUntil = -1f;
        baitEndsAt = Time.time + profile.baitMaxSeconds + 10f;
        return true;
    }

    /// <summary>Returns true while still baiting (it handles everything), false once it's over.</summary>
    bool UpdateBait()
    {
        agent.stoppingDistance = 0.15f;
        agent.updateRotation = false;
        agent.speed = 1.2f;   // stepping out slowly
        if (CanMoveNow) FaceTarget();

        if (!baitSteppedOut)
        {
            // Hidden around the corner at the end of the hallway: step out.
            baitSteppedOut = true;
            agent.SetDestination(baitPoint);
            return true;
        }

        bool arrived = !agent.pathPending && agent.remainingDistance < 0.35f;
        if (arrived && baitStandUntil < 0f) baitStandUntil = Time.time + profile.baitMaxSeconds;

        // Took the bait: came too close. CHARGE.
        if (CanMoveNow && DistanceToTarget <= profile.baitAttackDistance)
        {
            baiting = false;
            StartHunt(false);
            return true;
        }

        // Didn't take the bait: when nobody's looking, slip back around the corner and carry on stalking.
        bool waitedLongEnough = (baitStandUntil > 0f && Time.time >= baitStandUntil) || Time.time >= baitEndsAt;
        if (waitedLongEnough && IsFullyHidden)
        {
            baiting = false;
            agent.SetDestination(baitDuckPoint);
            lurkUntil = Time.time + 1.5f;
            return false;
        }
        return true;
    }

    /// <summary>
    /// A spot far down the way the player is facing (roughly in a straight line = a hallway) that they'd clearly see
    /// if they looked, with a corner right next to it to hide behind first. The corner must be out of sight now.
    /// </summary>
    bool FindBaitSpot(out Vector3 spot, out Vector3 duck)
    {
        spot = duck = transform.position;
        if (!NavMesh.SamplePosition(Target.transform.position, out NavMeshHit targetHit, 2f, NavMesh.AllAreas)) return false;
        Vector3 targetPos = targetHit.position;
        Vector3 look = Target.FlatLookDirection;
        float bestScore = float.MaxValue;

        SetCollidersEnabled(false);
        for (int i = 0; i < 30; i++)
        {
            float distance = Random.Range(profile.baitDistance.x, profile.baitDistance.y);
            float angle = Random.Range(-25f, 25f);
            Vector3 candidate = targetPos + Quaternion.Euler(0f, angle, 0f) * look * distance;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(hit.position.y - targetPos.y) > 1.5f) continue;

            // Roughly a straight walk from the player = down a hallway, not around the block
            if (!NavMesh.CalculatePath(targetPos, hit.position, NavMesh.AllAreas, scratchPath)) continue;
            if (scratchPath.status != NavMeshPathStatus.PathComplete) continue;
            float walk = PathLength(scratchPath);
            float straight = Vector3.Distance(targetPos, hit.position);
            if (walk > straight * 1.3f) continue;

            // Clearly visible down the hall (if you look), with a hidden corner right beside it
            int visible = 0;
            foreach (var p in PlayerAIState.All) visible = Mathf.Max(visible, p.CountLineOfSightPoints(hit.position, p.maxViewDistance));
            if (visible < 5) continue;
            if (!FindDuckPoint(hit.position, out Vector3 d)) continue;

            float score = Mathf.Abs(angle);   // the more in line with where you're facing, the better
            if (score < bestScore) { bestScore = score; spot = hit.position; duck = d; }
        }
        SetCollidersEnabled(true);
        return bestScore < float.MaxValue;
    }

    /// <summary>
    /// Find the cover spot (where no player could see ANY part of me) that I can reach with the SHORTEST WALK.
    /// Uses real NavMesh paths, so it won't pick a spot that's close in a straight line but behind a wall.
    /// </summary>
    bool TryFindQuickestCover(out Vector3 best)
    {
        best = transform.position;
        float bestScore = float.MaxValue;
        Vector3 toPlayer = Target.transform.position - transform.position;
        toPlayer.y = 0f;

        SetCollidersEnabled(false);
        // Check spots in rings around me: 8 directions × 6 distances (+ a little randomness).
        float[] rings = { 2f, 3.5f, 5f, 7f, 10f, 14f };
        for (int r = 0; r < rings.Length; r++)
        {
            for (int d = 0; d < 8; d++)
            {
                float angle = d * 45f + Random.Range(-15f, 15f);
                Vector3 candidate = transform.position + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * rings[r];
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) continue;
                if (AnyPlayerCanSee(hit.position)) continue;

                // How far do I actually have to WALK to get there?
                if (!NavMesh.CalculatePath(transform.position, hit.position, NavMesh.AllAreas, scratchPath)) continue;
                if (scratchPath.status != NavMeshPathStatus.PathComplete) continue;
                float walk = PathLength(scratchPath);
                if (walk > profile.maxCoverPathLength) continue;

                // Never escape by running right past a player (if I'm already close, at least never get closer).
                float closest = PathClosestApproachToPlayers(scratchPath);
                float keepAway = Mathf.Min(profile.escapeKeepAwayFromPlayers, DistanceToTarget - 0.5f);
                if (closest < keepAway) continue;

                // REAL cover (a wall in the way) is much better than "just off the edge of your screen".
                float score = walk;
                if (!IsRealCover(hit.position)) score += 12f;

                // Small penalty if the first step of the route heads toward the player.
                if (scratchPath.corners.Length > 1)
                {
                    Vector3 firstStep = scratchPath.corners[1] - transform.position;
                    firstStep.y = 0f;
                    if (Vector3.Dot(firstStep.normalized, toPlayer.normalized) > 0.5f) score += 4f;
                }
                if (score < bestScore) { bestScore = score; best = hit.position; }
            }
        }
        SetCollidersEnabled(true);
        return bestScore < float.MaxValue;
    }

    static float PathLength(NavMeshPath path)
    {
        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return length;
    }

    /// <summary>The closest any player gets to this route (metres, measured along the floor).</summary>
    static float PathClosestApproachToPlayers(NavMeshPath path)
    {
        Vector3[] corners = path.corners;
        float closest = float.MaxValue;
        foreach (var p in PlayerAIState.All)
        {
            Vector2 player = new Vector2(p.transform.position.x, p.transform.position.z);
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 a = new Vector2(corners[i - 1].x, corners[i - 1].z);
                Vector2 b = new Vector2(corners[i].x, corners[i].z);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector2.Dot(player - a, ab) / ab.sqrMagnitude) : 0f;
                closest = Mathf.Min(closest, Vector2.Distance(player, a + ab * t));
            }
        }
        return closest;
    }

    // ---------- HUNT: the real attack (or a fake one that's just a scare) ----------
    void StartHunt(bool fake)
    {
        huntEndsAt = Time.time + (fake ? Random.Range(profile.fakeHuntDuration.x, profile.fakeHuntDuration.y)
                                       : profile.huntDuration);
        huntStartsAt = Time.time + profile.huntWindupSeconds;   // the roar: a warning before it charges
        huntLastSawAt = Time.time;                               // a hunt starts because it knows where you are
        if (Target != null) lastKnownPosition = Target.transform.position;
        WatchPressure = 0f;
        Enter(MonsterState.Hunt);
        IsFakeHunt = fake;
    }

    void UpdateHunt()
    {
        // Startled (e.g. you burst out of a locker while it was banging): a short beat before it charges.
        if (Time.time < huntStartsAt)
        {
            agent.isStopped = true;
            if (CanMoveNow) FaceTarget();
            return;
        }
        agent.stoppingDistance = 0.2f;

        if (IsFakeHunt)
        {
            // A fake hunt never touches you: it vanishes when close or when time is up.
            ChaseTarget(profile.huntSpeed);
            if (DistanceToTarget < 2.5f || Time.time >= huntEndsAt)
            {
                VanishSomewhere();
                scareAllowedAt = Time.time + profile.scareCooldown;
                Enter(MonsterState.Stalk);
            }
            return;
        }

        // THE ATTACK: stop (wind-up) -> lunge at where you were -> caught if it gets close enough.
        if (attackPhase == AttackPhase.WindUp)
        {
            agent.isStopped = true;
            if (CanMoveNow) FaceTarget();
            if (Time.time >= attackPhaseEndsAt)
            {
                // Commit to a direction: where you are now, plus a bit past you.
                Vector3 toYou = Target.transform.position - transform.position;
                toYou.y = 0f;
                Vector3 lungeTo = Target.transform.position + toYou.normalized * 1.5f;
                if (NavMesh.SamplePosition(lungeTo, out NavMeshHit hit, 2f, NavMesh.AllAreas)) lungeTo = hit.position;
                agent.isStopped = false;
                agent.SetDestination(lungeTo);
                attackPhase = AttackPhase.Lunge;
                attackPhaseEndsAt = Time.time + profile.lungeSeconds;
            }
            return;
        }
        if (attackPhase == AttackPhase.Lunge)
        {
            agent.speed = profile.lungeSpeed;
            agent.acceleration = 80f;
            if (!Target.IsHiding && DistanceToTarget <= profile.catchDistance) { Catch(Target); return; }
            if (Time.time >= attackPhaseEndsAt)
            {
                attackPhase = AttackPhase.None;                              // missed!
                recoverUntil = Time.time + profile.attackRecoverSeconds;
            }
            return;
        }

        // Missed a lunge a moment ago: slow and clumsy for a bit.
        bool recovering = Time.time < recoverUntil;
        ChaseTarget(recovering ? profile.huntSpeed * 0.3f : profile.huntSpeed);

        if (!recovering && CanMoveNow && CanSeeTarget && !Target.IsHiding && DistanceToTarget <= profile.attackRange)
        {
            attackPhase = AttackPhase.WindUp;
            attackPhaseEndsAt = Time.time + profile.attackWindupSeconds;
            return;
        }

        // Lost you (couldn't see you for a while), or the hunt ran out: back off and start the ring over.
        if (CanSeeTarget) huntLastSawAt = Time.time;
        bool lostSight = Time.time - huntLastSawAt > profile.huntLoseSightSeconds;
        if (lostSight) EndHunt(lostYou: true);
        else if (Time.time >= huntEndsAt) EndHunt(lostYou: false);
    }

    void ChaseTarget(float speed)
    {
        agent.speed = speed;
        if (Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + 0.1f;
            agent.SetDestination(CanSeeTarget ? Target.transform.position : lastKnownPosition);
        }
    }

    /// <summary>
    /// The hunt is over. Lost you (you broke line of sight): go look around where it last saw you,
    /// then wander for a while (your breather). Ran out of time but can still see you: back off to the ring.
    /// </summary>
    void EndHunt(bool lostYou)
    {
        // THE BRUTE: no teleports, no ring. Lost you: look around where it last saw you, then roam.
        // Chased you until it ran out of steam: stop dead and catch its breath (your window to get away).
        if (profile.tiresOut)
        {
            WatchPressure = 0f;
            if (lostYou)
            {
                huntAllowedAt = Time.time + profile.huntCooldown;
                StartSearch(lookAroundFirst: true);
                return;
            }
            restUntil = Time.time + profile.restSeconds;
            huntAllowedAt = restUntil;   // the moment it's rested, it charges again if it can see you
            Enter(MonsterState.Wander);
            return;
        }

        huntAllowedAt = Time.time + profile.huntCooldown;
        SetRing(profile.ringAfterHunt);
        WatchPressure = 0f;
        graceUntil = Time.time + profile.graceAfterHunt;   // a breather: no stare meter, no stand-and-stare attack
        if (lostYou)
        {
            // You got away. Hiding in a locker? Then it searches for you. Otherwise: POOF, a purple flash where
            // it was, and it's gone (somewhere around you, the ring starting over). "The hunt is over"... or is it?
            if (!Target.IsHiding && IsFullyHidden && VanishSomewhere())
            {
                Enter(MonsterState.Stalk);
                return;
            }
            StartSearch(lookAroundFirst: true);
            return;
        }
        // Ran out of time right next to you? Don't just stand there: back off (facing you), then teleport away.
        if (DistanceToTarget < 10f)
        {
            retreatAfterHunt = true;
            leavingAfterHunt = true;
            Enter(MonsterState.Spotted);
            return;
        }
        if (IsFullyHidden && VanishSomewhere()) { Enter(MonsterState.Stalk); return; }
        lurkUntil = Time.time + 2f;   // catch its breath (and give you yours)
        Enter(MonsterState.Stalk);
    }

    void Catch(PlayerAIState victim)
    {
        victim.OnCaught(this);
        Learn(Boldness, +0.2f);   // LEARNING: that worked
        agent.Warp(spawnPosition);
        huntAllowedAt = Time.time + profile.huntCooldown;
        sawPlayerHideIn = null;
        spotISawYouEnter = null;
        SetRing(profile.ringStartRadius);   // start the whole ring over
        graceUntil = Time.time + profile.graceAfterHunt;
        Enter(MonsterState.Wander);
    }

    // ---------- SEARCH: lost you, so check the hiding spots near where it last saw/heard you ----------
    /// <param name="lookAroundFirst">True after losing you in a hunt: first walk to where it last saw you and look
    /// around; only then decide (by chance) whether to check hiding spots.</param>
    void StartSearch(bool lookAroundFirst = false)
    {
        lookingAround = lookAroundFirst;
        lookAroundUntil = -1f;
        spotsToCheck.Clear();
        foreach (var s in HidingSpot.All)
        {
            if (s.OccupyingMonster != null) continue;
            if (Vector3.Distance(s.ExitPosition, lastKnownPosition) <= profile.searchRadius) spotsToCheck.Add(s);
        }

        // Which ones first? Closest to where it lost you, spots you've used a lot (LEARNING FROM HABITS),
        // plus a random guess, so the one right next to where you vanished isn't ALWAYS checked first.
        var scores = new Dictionary<HidingSpot, float>();
        foreach (var s in spotsToCheck) scores[s] = SearchScore(s) + Random.Range(0f, profile.searchGuessRandomness);
        spotsToCheck.Sort((a, b) => scores[a].CompareTo(scores[b]));
        if (spotsToCheck.Count > profile.maxSpotsToCheck)
            spotsToCheck.RemoveRange(profile.maxSpotsToCheck, spotsToCheck.Count - profile.maxSpotsToCheck);

        // If I SAW you get in, that spot goes first.
        spotISawYouEnter = sawPlayerHideIn;
        if (sawPlayerHideIn != null)
        {
            spotsToCheck.Remove(sawPlayerHideIn);
            spotsToCheck.Insert(0, sawPlayerHideIn);
            sawPlayerHideIn = null;
        }

        spotIndex = 0;
        openingUntil = -1f;
        Enter(MonsterState.Search);
    }

    // Lost you: walk to where it last saw/heard you, then turn this way and that for a few seconds.
    void UpdateLookAround()
    {
        agent.speed = profile.stalkSpeed * 1.2f;

        if (lookAroundUntil < 0f)
        {
            // Walking there
            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + 0.25f;
                agent.SetDestination(lastKnownPosition);
            }
            bool arrived = !agent.pathPending && agent.remainingDistance < 1f;
            if (arrived || TimeInState > 8f)
            {
                lookAroundUntil = Time.time + profile.investigateSeconds;
                nextLookTurn = 0f;
                agent.ResetPath();
            }
            return;
        }

        // Looking around: a new direction every second
        if (Time.time >= nextLookTurn)
        {
            nextLookTurn = Time.time + 1f;
            lookDirection = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
        }
        if (CanMoveNow) FaceTowards(transform.position + lookDirection);
        if (Time.time < lookAroundUntil) return;

        // Done looking. Check the hiding spots nearby (by chance), or give up and wander (your breather).
        lookingAround = false;
        bool checkSpots = spotsToCheck.Count > 0 && Time.time >= searchAllowedAt && CheckSpotsChance.Roll();
        if (!checkSpots) Enter(MonsterState.Wander);
    }

    float SearchScore(HidingSpot s) =>
        Vector3.Distance(s.ExitPosition, lastKnownPosition) - profile.hidingHabitWeight * s.playerUseCount;

    void UpdateSearch()
    {
        agent.speed = profile.stalkSpeed;
        agent.stoppingDistance = 0.3f;

        if (CanSeeTarget)
        {
            // Found you again. A stalker goes back to stalking; a monster that doesn't stalk (the Brute) charges.
            if (profile.followStyle != FollowStyle.None) Enter(MonsterState.Stalk);
            else if (Time.time >= huntAllowedAt) StartHunt(false);
            else Enter(MonsterState.Wander);
            return;
        }
        if (lookingAround) { UpdateLookAround(); return; }
        if (spotIndex >= spotsToCheck.Count || TimeInState > 45f)
        {
            Learn(CheckSpotsChance, -0.05f);   // LEARNING: searched for nothing, so search less
            SetRing(profile.ringStartRadius);
            Enter(MonsterState.Wander);
            return;
        }

        HidingSpot spot = spotsToCheck[spotIndex];
        if (openingUntil < 0f)
        {
            // Walk up to the spot (stand a little back from the door, so a player can still burst out)...
            if (Time.time >= nextRepathTime)
            {
                nextRepathTime = Time.time + 0.25f;
                agent.SetDestination(spot.ExitPosition + spot.transform.forward * 1.2f);
            }
            if (!agent.pathPending && agent.remainingDistance < 0.6f)
            {
                // If I SAW you get in: bang on the door first, as a warning. Otherwise just open it.
                bool bang = profile.bangOnDoorWhenSawHide && spot == spotISawYouEnter && spot.OccupyingPlayer != null;
                if (bang)
                {
                    IsBanging = true;
                    BangingOn = spot;
                    bangUntil = Time.time + Random.Range(profile.bangSeconds.x, profile.bangSeconds.y);
                    openingUntil = bangUntil;
                }
                else openingUntil = Time.time + 1.2f;
            }
            return;
        }

        FaceTowards(spot.transform.position);

        if (IsBanging)
        {
            // *BANG* *BANG* (later: real sounds + the door shaking)
            if (spot.OccupyingPlayer == null)
            {
                // They burst out while I was banging! A short startled beat, then chase.
                StartHunt(false);
                huntStartsAt = Time.time + 0.8f;
                return;
            }
            if (Time.time < bangUntil) return;

            IsBanging = false;
            BangingOn = null;
            if (!OpenAfterBangingChance.Roll())
            {
                // Gave up... for now. It lurks nearby and won't search hiding spots again for a while.
                Learn(OpenAfterBangingChance, +0.1f);   // LEARNING: you WERE in there, so open it next time
                searchAllowedAt = Time.time + profile.searchCooldownAfterGivingUp;
                spotISawYouEnter = null;
                lurkUntil = Time.time + Random.Range(profile.lurkAfterHidingSeconds.x, profile.lurkAfterHidingSeconds.y);
                Enter(MonsterState.Stalk);
                return;
            }
            // ...otherwise fall through and open it.
        }
        else if (Time.time < openingUntil) return;   // "opening" it (later: door animation + sound)

        PlayerAIState found = spot.OccupyingPlayer;
        if (found != null)
        {
            Learn(CheckSpotsChance, +0.15f);   // LEARNING: you rely on hiding spots, so check them more
            Catch(found);                      // FOUND YOU
            return;
        }
        spotIndex++;
        openingUntil = -1f;
    }

    // ---------- HIDE: climb into a free hiding spot near the player and wait to ambush ----------
    // Picks the best spot to ambush from. Lower score = better:
    //   short walk for me + close to you
    //   + penalty if you've ALREADY been there (you're exploring, you won't come back)
    //   + penalty if it's BEHIND you (ahead = where you're going)
    //   - bonus if it's next to a gem (you'll HAVE to come there)
    bool TryStartHiding()
    {
        HidingSpot best = null;
        Gem bestGem = null;
        float bestScore = float.MaxValue;
        Vector3 look = Target.FlatLookDirection;

        SetCollidersEnabled(false);
        foreach (var s in HidingSpot.All)
        {
            if (!s.IsFree) continue;
            Vector3 toSpot = s.ExitPosition - Target.transform.position;
            toSpot.y = 0f;
            float toPlayer = toSpot.magnitude;
            if (toPlayer < 5f || toPlayer > 22f) continue;     // near you, but not right in front of you
            if (AnyPlayerCanSee(s.ExitPosition)) continue;     // don't get in while you're watching

            float score = Vector3.Distance(transform.position, s.ExitPosition) + toPlayer;
            if (ExplorationMap.IsExplored(s.ExitPosition)) score += profile.exploredSpotPenalty;
            if (Vector3.Dot(toSpot / Mathf.Max(toPlayer, 0.01f), look) < 0f) score += profile.behindPlayerPenalty;
            Gem gem = GemNear(s.ExitPosition, profile.guardRadius);
            if (gem != null) score -= profile.lockerByGemBonus;

            if (score < bestScore) { bestScore = score; best = s; bestGem = gem; }
        }
        SetCollidersEnabled(true);
        if (best == null) return false;

        hideSpot = best;
        hideAllowedAt = Time.time + profile.hideCooldown;
        hidWhileWandering = false;
        Enter(MonsterState.Hide);
        hideGuardGem = bestGem;   // if it's by a gem: grabbing that gem makes me burst out
        return true;
    }

    /// <summary>The nearest gem still on the map within 'radius' of 'point' (or null).</summary>
    static Gem GemNear(Vector3 point, float radius)
    {
        Gem best = null;
        float bestDistance = radius;
        foreach (var g in Gem.All)
        {
            float d = Vector3.Distance(g.Position, point);
            if (d <= bestDistance) { bestDistance = d; best = g; }
        }
        return best;
    }

    void UpdateGoingToHide()
    {
        agent.speed = profile.stalkSpeed * 1.3f;
        agent.stoppingDistance = 0.3f;

        if (watchedFor >= profile.secondsWatchedBeforeSpotted) { Enter(MonsterState.Spotted); return; }
        float giveUpAfter = hidWhileWandering ? 45f : 25f;   // a gem can be a long walk away
        if (!hideSpot.IsFree || TimeInState > giveUpAfter) { Enter(StateAfterHiding()); return; }

        if (Time.time >= nextRepathTime)
        {
            nextRepathTime = Time.time + 0.25f;
            agent.SetDestination(hideSpot.ExitPosition);
        }
        // THE HIDING RULE: only climb in once nobody has seen any part of me for a moment.
        if (!agent.pathPending && agent.remainingDistance < 0.6f && IsFullyHidden) GetInSpot();
    }

    void GetInSpot()
    {
        if (!hideSpot.TryEnter(gameObject)) { Enter(StateAfterHiding()); return; }
        IsHiddenInSpot = true;
        lockerPeek = false;   // (TryStartLockerPeek turns this on right after, for a locker peek)
        agent.enabled = false;
        SetVisible(false);
        transform.position = hideSpot.InsidePosition;
        hideEndsAt = Time.time + (hidWhileWandering
            ? Random.Range(profile.guardSeconds.x, profile.guardSeconds.y)   // guarding a gem: wait as long as guarding
            : profile.hideMaxSeconds);
        CanSeeTarget = false;
        IsWatched = false;
    }

    // ---------- PEEK FROM A LOCKER: the door creaks open... and it's watching you ----------
    // A locker in front of you, door facing you, that you could see from where you are. It slips in unseen
    // (no flash: it's already inside before you notice anything), waits a moment, then the door creaks open.
    bool TryStartLockerPeek()
    {
        if (!profile.peekFromLockers || !profile.canTeleport || HidingSpot.All.Count == 0 || !IsFullyHidden) return false;

        HidingSpot best = null;
        float bestScore = float.MaxValue;
        Vector3 look = Target.FlatLookDirection;
        float far = Mathf.Clamp(profile.useRing ? Ring : 16f, 10f, 22f);

        SetCollidersEnabled(false);
        foreach (var s in HidingSpot.All)
        {
            if (!s.IsFree) continue;
            Vector3 toSpot = s.ExitPosition - Target.transform.position;
            toSpot.y = 0f;
            float d = toSpot.magnitude;
            if (d < 6f || d > far) continue;                                     // in front of you, not on top of you
            float angle = Vector3.Angle(look, toSpot);
            if (angle > 70f) continue;                                           // roughly where you're looking/going
            if (Vector3.Dot(s.transform.forward, -toSpot / d) < 0.3f) continue;  // its door faces you
            bool seenFromSomewhere = false;                                      // you COULD see the door from where you are
            foreach (var p in PlayerAIState.All)
                if (!Physics.Linecast(p.EyePosition, s.PeekEyePoint, ~0, QueryTriggerInteraction.Ignore)) { seenFromSomewhere = true; break; }
            if (!seenFromSomewhere) continue;

            float score = Mathf.Abs(d - far * 0.75f) + angle * 0.05f;
            if (score < bestScore) { bestScore = score; best = s; }
        }
        SetCollidersEnabled(true);
        if (best == null) { peekAllowedAt = Time.time + 4f; return false; }

        hideSpot = best;
        hidWhileWandering = false;
        Enter(MonsterState.Hide);
        GetInSpot();                       // slips straight in (it's a teleporter: no walk across the room)
        if (!IsHiddenInSpot) return false;
        lockerPeek = true;
        lockerPeekDone = false;
        lockerWatchedFor = 0f;
        lockerPeekOpenAt = Time.time + Random.Range(profile.lockerPeekDelay.x, profile.lockerPeekDelay.y);
        lockerPeekCloseAt = lockerPeekOpenAt + profile.peekMaxSeconds;
        hideEndsAt = lockerPeekCloseAt + 3f;
        lastTeleportTime = Time.time;
        peekAllowedAt = Time.time + 12f;
        return true;
    }

    /// <summary>Peeking out of a locker right now (door open, eyes in the gap)?</summary>
    public bool IsLockerPeeking => IsHiddenInSpot && lockerPeek && !lockerPeekDone && Time.time >= lockerPeekOpenAt;

    void UpdateHiddenInSpot()
    {
        // Burst out when a (non-hiding) player walks right past. The Trickster: usually just a SCARE
        // (screech, lunge, gone in a flash); sometimes a real attack.
        bool playerClose = !Target.IsHiding &&
                           Vector3.Distance(hideSpot.ExitPosition, Target.transform.position) <= profile.ambushDistance;
        if (playerClose)
        {
            bool guarding = hidWhileWandering;
            bool real = Random.value < profile.burstOutRealAttackChance;
            hideSpot.BurstOpen();
            LeaveSpot();
            Learn(guarding ? GuardGemChance : HideAmbushChance, +0.1f);   // LEARNING: hiding got me close
            StartHunt(!real);
            huntStartsAt = Time.time + 0.35f;                       // a quick screech, not a long roar
            if (real) huntEndsAt = Time.time + 4f;                  // short, explosive chase
            return;
        }

        // THE TELLS: the door sits a crack open with one eye in the gap. Peeking: it creaks wider, both eyes.
        if (lockerPeek && !lockerPeekDone && Time.time >= lockerPeekOpenAt)
        {
            hideSpot.ShowPeek(eyeMaterial);

            // Did you spot the eyes? The door eases shut... but now you KNOW it's in there.
            bool seen = false;
            foreach (var p in PlayerAIState.All)
                if (!p.IsHiding && p.CanSeePoint(hideSpot.PeekEyePoint)) { seen = true; break; }
            lockerWatchedFor = seen ? lockerWatchedFor + Time.deltaTime : 0f;
            if (lockerWatchedFor >= profile.secondsWatchedBeforeSpotted)
            {
                OnSpottedRing();                    // same as spotting it anywhere: the ring is pushed back a little
                lockerPeekDone = true;
                hideEndsAt = Time.time + Random.Range(profile.stayInLockerAfterSpotted.x, profile.stayInLockerAfterSpotted.y);
            }
            else if (Time.time >= lockerPeekCloseAt)
            {
                lockerPeekDone = true;              // nobody noticed: close up and slip away soon
                hideEndsAt = Time.time + 3f;
            }
        }
        else hideSpot.ShowAjar(eyeMaterial);

        // Hiding by a gem that someone already took (and I didn't hear it)? Nothing left to wait for.
        bool gemGone = hideGuardGem != null && !hideGuardGem.isActiveAndEnabled;
        // Ambushing someone who went a different way? Pointless, go find them again.
        bool playersWentElsewhere = !hidWhileWandering && DistanceToTarget > profile.leaveHidingIfPlayersFartherThan;

        // Got bored? Slip out when nobody can see the door.
        if ((gemGone || playersWentElsewhere || Time.time >= hideEndsAt) && !AnyPlayerCanSee(hideSpot.ExitPosition))
        {
            bool guarding = hidWhileWandering;
            bool wasLockerPeek = lockerPeek;
            LeaveSpot();
            if (!wasLockerPeek)
                Learn(guarding ? GuardGemChance : HideAmbushChance, guarding ? -0.05f : -0.1f);   // LEARNING: waste of time
            Enter(StateAfterHiding());
            if (wasLockerPeek && profile.canTeleport) VanishSomewhere(flash: false);   // gone, quietly, back to the ring
        }
    }

    /// <summary>Hid to ambush a player I was stalking -> back to stalking. Hid to guard a gem -> back to wandering.</summary>
    MonsterState StateAfterHiding() => hidWhileWandering ? MonsterState.Wander : MonsterState.Stalk;

    void LeaveSpot()
    {
        hideSpot.Exit(gameObject);
        IsHiddenInSpot = false;
        transform.position = hideSpot.ExitPosition;
        transform.rotation = Quaternion.LookRotation(hideSpot.transform.forward);
        SetVisible(true);
        agent.enabled = true;
        agent.Warp(hideSpot.ExitPosition);
        nextScareCheck = Time.time + profile.scareCheckInterval;   // no instant scare the moment it gets out
    }

    /// <summary>A player opened the hiding spot I'm in. Gotcha.</summary>
    public void AmbushedBy(PlayerAIState player)
    {
        if (!IsHiddenInSpot) return;
        LeaveSpot();
        Learn(HideAmbushChance, +0.2f);       // LEARNING: best outcome possible
        Catch(player);
    }

    // =====================================================================
    // HELPERS
    // =====================================================================
    void SetVisible(bool visible)
    {
        foreach (var r in renderers) r.enabled = visible;
        foreach (var c in colliders) c.enabled = visible;
    }

    void FaceTarget() => FaceTowards(Target.transform.position);

    void FaceTowards(Vector3 point)
    {
        Vector3 d = point - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 6f * Time.deltaTime);
    }

    Vector3 FleePoint()
    {
        Vector3 away = (transform.position - Target.transform.position).normalized;
        if (NavMesh.SamplePosition(transform.position + away * 10f, out NavMeshHit hit, 6f, NavMesh.AllAreas))
            return hit.position;
        return RandomNavPoint(transform.position, 10f, out Vector3 p) ? p : transform.position;
    }

    /// <summary>
    /// TELEPORT somewhere else NEAR the target (about ring distance), out of sight. Used after it slips out of view,
    /// at the end of a fake hunt, and to keep up with you on the ring. Returns false if it found nowhere good.
    /// </summary>
    /// <param name="flash">true = a SIGNATURE teleport (purple flash, meant to be noticed).
    /// false = BEHIND THE SCENES (catching up, getting unstuck): no flash, you never know it happened.</param>
    bool VanishSomewhere(bool flash = true)
    {
        if (!profile.canTeleport) return false;
        // Land as close as possible to the RING (the ring remembers how close it had got), never right on top of you.
        float wanted = Mathf.Max(profile.useRing ? Ring : 18f, profile.ringAttackRadius + 3f);
        if (!FindTeleportSpot(wanted, out Vector3 spot)) return false;
        if (flash) FlashWhereIWas();
        agent.Warp(spot);
        agent.ResetPath();
        Vector3 toTarget = Target.transform.position - spot;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(toTarget);   // facing you
        lastTeleportTime = Time.time;
        lurkUntil = Time.time + profile.settleAfterTeleportSeconds;   // a beat before it moves again
        return true;
    }

    /// <summary>
    /// The hidden spot CLOSEST to 'wanted' metres from the target (preferably behind them) that is:
    ///   - on the same walkable area as the target (reachable from where THEY stand: never on top of a wall, or cut off)
    ///   - REAL cover from every player (a wall in the way), or at least off everyone's screen if no real cover exists
    /// </summary>
    bool FindTeleportSpot(float wanted, out Vector3 best)
    {
        best = transform.position;
        if (!NavMesh.SamplePosition(Target.transform.position, out NavMeshHit targetHit, 2f, NavMesh.AllAreas)) return false;
        Vector3 targetPos = targetHit.position;
        Vector3 look = Target.FlatLookDirection;

        SetCollidersEnabled(false);
        for (int i = 0; i < 36; i++)
        {
            // Mostly behind the player (the half they're not looking at), sometimes anywhere; around the ring distance
            float angle = i < 24 ? 180f + Random.Range(-110f, 110f) : Random.Range(0f, 360f);
            float distance = Mathf.Max(profile.ringAttackRadius + 2f, wanted + Random.Range(-4f, 4f));
            Vector3 candidate = targetPos + Quaternion.Euler(0f, angle, 0f) * look * distance;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;
            if (Mathf.Abs(hit.position.y - targetPos.y) > 1.5f) continue;     // same floor level as the player
            if (AnyPlayerCanSee(hit.position)) continue;                       // never pop in on someone's screen

            // Reachable from the PLAYER, and not a huge detour away (that wouldn't be "near")
            if (!NavMesh.CalculatePath(targetPos, hit.position, NavMesh.AllAreas, scratchPath)) continue;
            if (scratchPath.status != NavMeshPathStatus.PathComplete) continue;
            float walk = PathLength(scratchPath);
            if (walk > wanted * 2f + 6f) continue;

            Vector3 flat = hit.position - targetPos;
            flat.y = 0f;
            float score = Mathf.Abs(flat.magnitude - wanted) * 1.5f;          // closest to the ring wins...
            if (!IsRealCover(hit.position)) score += 20f;                      // ...behind a real wall if at all possible
            score += walk * 0.05f;
            spotCandidates.Add((hit.position, score));
        }
        bool found = PickBestCornerSpot(out best);   // ...and at a corner (never land in the open)
        SetCollidersEnabled(true);
        return found;
    }

    /// <summary>From my eyes at 'feet', is there a clear line to the target's eyes (no wall in the way)?</summary>
    bool ClearLineToTarget(Vector3 feet)
    {
        Vector3 eye = feet + Vector3.up * eyeHeight;
        if (Physics.Linecast(eye, Target.EyePosition, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.transform.IsChildOf(Target.transform);   // the first thing in the way is the player = clear
        return true;
    }

    /// <summary>The teleport flash: a burst of (purple) light where I'm standing, right before I vanish.
    /// I'm out of sight when I teleport, so you don't see ME go: you see the glow around the corner.</summary>
    void FlashWhereIWas()
    {
        lastFlashTime = Time.time;   // (counts toward the flash budget even if the flash is switched off)
        if (!profile.teleportFlash) return;
        LightFlash.Spawn(transform.position + Vector3.up * 1.5f, profile.teleportFlashColor,
                         profile.teleportFlashRange, profile.teleportFlashIntensity, profile.teleportFlashSeconds);
    }

    static bool RandomNavPoint(Vector3 center, float radius, out Vector3 result)
    {
        for (int i = 0; i < 10; i++)
        {
            Vector3 random = center + Random.insideUnitSphere * radius;
            random.y = center.y;
            if (NavMesh.SamplePosition(random, out NavMeshHit hit, 3f, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }
        result = center;
        return false;
    }

    // Draw the sight cone (yellow) and hearing range (blue) in the Scene view when selected.
    void OnDrawGizmosSelected()
    {
        if (profile == null) return;
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        float half = profile.sightAngle * 0.5f;

        Gizmos.color = new Color(1f, 0.9f, 0f, 0.6f);
        Vector3 prev = eye + Quaternion.Euler(0f, -half, 0f) * transform.forward * profile.sightRange;
        Gizmos.DrawLine(eye, prev);
        for (int i = 1; i <= 24; i++)
        {
            float a = Mathf.Lerp(-half, half, i / 24f);
            Vector3 p = eye + Quaternion.Euler(0f, a, 0f) * transform.forward * profile.sightRange;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
        Gizmos.DrawLine(eye, prev);

        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, profile.hearingRange);
    }
}
