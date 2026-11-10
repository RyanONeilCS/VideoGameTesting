using UnityEngine;

// ---- The options for each toolkit part (design doc section 5) ----
public enum WanderStyle { DriftTowardPlayers, Roam, StayStill }
public enum FollowStyle { StalkFromBehind, CreepWhenUnseen, None }
// (StandAndStare is never picked by chance: it happens when you catch it in the open, see "CAUGHT OFF GUARD".)
public enum SpottedReaction { StareThenHide, Freeze, Flee, Vanish, Chase, StandAndStare }

/// <summary>
/// A chance (0 to 1) that the monster can LEARN, but only inside its limits (design doc section 8).
/// </summary>
[System.Serializable]
public class BehaviourChance
{
    [Range(0f, 1f)] public float start = 0.3f;   // value at the start of every match
    [Range(0f, 1f)] public float min = 0.1f;     // learning can't push it below this...
    [Range(0f, 1f)] public float max = 0.6f;     // ...or above this
    public bool locked;                          // true = learning never changes it

    [System.NonSerialized] public float current; // the live value during play

    public BehaviourChance() { }
    public BehaviourChance(float start, float min, float max, bool locked = false)
    {
        this.start = start; this.min = min; this.max = max; this.locked = locked;
    }

    public void Reset() => current = Mathf.Clamp(start, min, max);

    /// <summary>Learning: move the value a bit, scaled by adaptability, but never outside the limits.</summary>
    public void Nudge(float amount, float adaptability)
    {
        if (locked) return;
        current = Mathf.Clamp(current + amount * adaptability, min, max);
    }

    /// <summary>Roll the dice: true with probability 'current'.</summary>
    public bool Roll() => Random.value < current;

    /// <summary>Each monster gets its own copy so learning doesn't change the saved file.</summary>
    public BehaviourChance Clone() => (BehaviourChance)MemberwiseClone();
}

/// <summary>
/// How likely each reaction is when a player spots the monster. Every monster CAN do every reaction;
/// the weights decide how often. (They don't need to add up to 1. Bigger = more likely.)
/// </summary>
[System.Serializable]
public class SpottedReactionWeights
{
    [Tooltip("Stares back for a moment, then slips out of sight (around a corner).")]
    [Range(0f, 1f)] public float stareThenHide = 0.75f;
    [Tooltip("Stands perfectly still, staring, until you look away... or walk toward it (then it snaps).")]
    [Range(0f, 1f)] public float freeze = 0.1f;
    [Tooltip("Turns and runs.")]
    [Range(0f, 1f)] public float flee = 0.1f;
    [Tooltip("Teleports somewhere you can't see.")]
    [Range(0f, 1f)] public float vanish = 0f;
    [Tooltip("Charges at you (a real hunt). Keep this rare!")]
    [Range(0f, 1f)] public float chase = 0.05f;

    /// <summary>Weighted random pick (a "roulette wheel").</summary>
    public SpottedReaction Pick()
    {
        float total = stareThenHide + freeze + flee + vanish + chase;
        if (total <= 0f) return SpottedReaction.Freeze;
        float r = Random.value * total;
        if ((r -= stareThenHide) < 0f) return SpottedReaction.StareThenHide;
        if ((r -= freeze) < 0f) return SpottedReaction.Freeze;
        if ((r -= flee) < 0f) return SpottedReaction.Flee;
        if ((r -= vanish) < 0f) return SpottedReaction.Vanish;
        return SpottedReaction.Chase;
    }
}

/// <summary>
/// ALL the settings for one kind of monster: its "toolkit" (design doc section 5).
/// This is a ScriptableObject: a data file you edit in the Inspector, no code needed.
/// Make a new one: right-click in the Project window > Create > Stalker > Monster Profile.
/// </summary>
[CreateAssetMenu(menuName = "Stalker/Monster Profile", fileName = "NewMonsterProfile")]
public class MonsterProfile : ScriptableObject
{
    public string displayName = "Monster";

    [Header("WANDER (no target)")]
    public WanderStyle wanderStyle = WanderStyle.DriftTowardPlayers;
    public float wanderSpeed = 2.2f;
    public float wanderRadius = 15f;
    [Tooltip("Random pause (min, max seconds) after reaching each wander point.")]
    public Vector2 wanderPause = new Vector2(1f, 3f);
    [Tooltip("DriftTowardPlayers: chance that each new wander point is in the players' rough area.")]
    [Range(0f, 1f)] public float driftChance = 0.7f;
    [Tooltip("DriftTowardPlayers: how rough 'rough area' is (metres). Bigger = vaguer.")]
    public float driftFuzziness = 15f;

    [Header("FOLLOW / STALK")]
    public FollowStyle followStyle = FollowStyle.StalkFromBehind;
    public float stalkSpeed = 3.2f;
    [Tooltip("How close it stalks when fully bold / fully careful.")]
    public float minStalkDistance = 4f, maxStalkDistance = 12f;
    [Tooltip("LEARNED: 0 = careful, 1 = bold. With the ring: bolder = the ring shrinks faster.")]
    public BehaviourChance boldness = new BehaviourChance(0.5f, 0.2f, 0.9f);

    [Header("THE RING (stalking pressure)")]
    [Tooltip("ON: it circles each player at a distance (the ring), staying out of sight. The ring slowly SHRINKS while " +
             "nobody spots it, and GROWS when someone does. When it's small enough, it attacks. " +
             "Keep looking around and it never gets close. Get careless and it's right behind you.")]
    public bool useRing = true;
    [Tooltip("Ring size at the start (metres): about as far as players can see in the dark.")]
    public float ringStartRadius = 24f;
    [Tooltip("When the ring shrinks to this (metres), it's right behind you: it attacks.")]
    public float ringAttackRadius = 4f;
    [Tooltip("Metres the ring shrinks per second while it's in position and unseen (×0.6 when careful, ×1.6 when bold).")]
    public float ringShrinkPerSecond = 0.35f;
    [Tooltip("Metres the ring grows each time a player spots it (pushes it back a little).")]
    public float ringPushBackWhenSpotted = 3f;
    [Tooltip("PRESSURE BUILDS: each time it's spotted, the ring's MAXIMUM also drops this much. Spot it quickly and " +
             "you can work it back a bit, but over a match it keeps creeping closer. Resets after a hunt.")]
    public float ringMaxDropPerSpot = 1.5f;
    [Tooltip("The ring's maximum never drops below this (metres).")]
    public float ringMaxLowest = 10f;
    [Tooltip("IGNORED: if NO player has seen it for this long (seconds), it stops trying to be noticed (no peeks or " +
             "leaning out) and just comes for you. Ignoring it is never safe.")]
    public float ignoredAfterSeconds = 25f;
    [Tooltip("While ignored, the ring shrinks this many times faster.")]
    public float ignoredShrinkMultiplier = 2f;
    [Tooltip("Ring size after a hunt ends (it backs off and starts over).")]
    public float ringAfterHunt = 20f;
    [Tooltip("Speed (m/s) when far from its spot on the ring (catching up). Normal stalking uses Stalk Speed.")]
    public float catchUpSpeed = 4.5f;

    [Header("TELEPORTING (always somewhere around you)")]
    [Tooltip("Can it teleport? Only ever when NO player can see it, and only to places behind a wall from every player.")]
    public bool canTeleport = true;
    [Tooltip("BEHIND THE SCENES (no flash): if it's far from you AND its spot on the ring is more than this many metres " +
             "of walking away, it quietly teleports closer instead of taking the long way round. You never know.")]
    public float teleportIfWalkFartherThan = 25f;
    [Tooltip("...only when it's at least this far from the player (metres).")]
    public float quietTeleportOnlyFartherThan = 20f;
    [Tooltip("Minimum seconds between behind-the-scenes teleports.")]
    public float teleportCooldown = 6f;
    [Tooltip("FLASH BUDGET: at most one SIGNATURE teleport (the purple flash) this often (seconds), so the flash stays " +
             "special. When it's not allowed yet, it walks off instead. (Hunt endings and look-away vanishes always flash.)")]
    public float minSecondsBetweenFlashes = 20f;
    [Tooltip("A flash of light where it WAS when it teleports (you see the glow around the corner, not the teleport).")]
    public bool teleportFlash = true;
    public Color teleportFlashColor = new Color(0.6f, 0.15f, 1f);   // purple
    [Tooltip("How far the flash's light reaches (metres).")]
    public float teleportFlashRange = 11f;
    public float teleportFlashIntensity = 10f;
    [Tooltip("How long the flash takes to fade out (seconds).")]
    public float teleportFlashSeconds = 0.4f;
    [Tooltip("After a teleport it holds still this long (seconds), facing you, before moving again.")]
    public float settleAfterTeleportSeconds = 1f;
    [Tooltip("FAKE TELEPORT: the chance a purple flash (after slipping away) is a lie. The flash goes off, but it never left: " +
             "it waits right there around the corner. Go check where it \"left\" from, and it gets you (you followed it).")]
    [Range(0f, 1f)] public float fakeTeleportChance = 0.17f;
    [Tooltip("After a fake teleport: how long it waits around the corner for you (seconds).")]
    public float fakeTeleportWaitSeconds = 10f;
    [Tooltip("SNEAKY: it won't walk across a player's view. If the next bit of its route is visible, it waits...")]
    public bool sneakyMovement = true;
    [Tooltip("...for up to this long (seconds), then teleports instead (so it's still always around).")]
    public float waitForClearPathSeconds = 2f;

    [Header("TRACKING (a stalker always knows ROUGHLY where you are)")]
    [Tooltip("ON: it always knows roughly where players are (unless they're hiding in a locker). " +
             "So you keep running into it, instead of never seeing it if you didn't go near its start.")]
    public bool alwaysTracksPlayers = true;
    [Tooltip("How often (seconds) its rough guess of where you are updates. Older guess = easier to slip away.")]
    public float trackingUpdateInterval = 2f;
    [Tooltip("How rough the guess is (metres of random error).")]
    public float trackingFuzz = 5f;

    [Header("AFTER LOSING YOU (you escaped)")]
    [Tooltip("Lost you during a hunt: it goes to where it last saw you and looks around for this long (seconds)...")]
    public float investigateSeconds = 4f;
    [Tooltip("...then (unless it decides to check lockers) it wanders for this long (seconds) before tracking you again, " +
             "slowly drifting your way. Your breather.")]
    public float breatherAfterLosingYou = 12f;

    [Header("HIDE & HIDING SPOTS")]
    [Tooltip("Can this monster climb into hiding spots to ambush players?")]
    public bool canHide = true;
    [Tooltip("LEARNED: chance (each scare check) that it goes to hide in a hiding spot near you. " +
             "Keep it LOW: the players should be the ones hiding, not the monster.")]
    public BehaviourChance hideAmbushChance = new BehaviourChance(0.08f, 0.03f, 0.2f);
    [Tooltip("Longest it waits in a hiding spot (seconds).")]
    public float hideMaxSeconds = 15f;
    public float hideCooldown = 40f;
    [Tooltip("Gets out early if every player is further than this from its hiding spot (they went another way).")]
    public float leaveHidingIfPlayersFartherThan = 28f;
    [Tooltip("Picking a hiding spot: spots in places players have ALREADY been count as this many metres further away " +
             "(they're exploring, so they probably won't come back).")]
    public float exploredSpotPenalty = 12f;
    [Tooltip("Picking a hiding spot: spots BEHIND the player count as this many metres further away (ahead = where they're going).")]
    public float behindPlayerPenalty = 8f;
    [Tooltip("Bursts out when a player comes this close to its hiding spot.")]
    public float ambushDistance = 3f;
    [Tooltip("Someone walks right past the locker it's in and it bursts out: the chance it's a REAL attack. Otherwise it's a scare (roar, lunge, gone in a flash).")]
    [Range(0f, 1f)] public float burstOutRealAttackChance = 0.25f;
    [Tooltip("LEARNED: chance it checks hiding spots after losing you. Goes UP each time it finds someone in one " +
             "(you rely on lockers too much), DOWN each time it searches for nothing.")]
    public BehaviourChance checkHidingSpotsChance = new BehaviourChance(0.3f, 0.15f, 0.7f);
    [Tooltip("Checks hiding spots within this distance of where it last saw/heard you.")]
    public float searchRadius = 12f;
    public int maxSpotsToCheck = 2;
    [Tooltip("Randomness when choosing which spots to check (metres). Bigger = less likely to pick the obvious closest one.")]
    public float searchGuessRandomness = 8f;
    [Tooltip("LEARNING FROM HABITS: each time players used a spot, it counts as this many metres closer (checked sooner).")]
    public float hidingHabitWeight = 3f;
    [Tooltip("If it SAW you get into a hiding spot: bang on the door first (a warning) instead of opening it right away.")]
    public bool bangOnDoorWhenSawHide = true;
    [Tooltip("How long it bangs (min, max seconds). Get out now... or stay and hope.")]
    public Vector2 bangSeconds = new Vector2(2.5f, 4.5f);
    [Tooltip("LEARNED: after banging, chance it opens the door. Otherwise it gives up and goes away (for now).")]
    public BehaviourChance openAfterBangingChance = new BehaviourChance(0.6f, 0.3f, 0.9f);
    [Tooltip("After giving up on a door, it won't search hiding spots again for this long (seconds).")]
    public float searchCooldownAfterGivingUp = 20f;

    [Header("GUARD THE GEMS (the objective)")]
    [Tooltip("Players HAVE to go to the gems, so a smart monster waits near them instead of only chasing.")]
    public bool guardGems = true;
    [Tooltip("LEARNED: each time it picks a new wander point, chance it goes to guard a gem instead. Keep it low.")]
    public BehaviourChance guardGemChance = new BehaviourChance(0.1f, 0.05f, 0.25f);
    [Tooltip("Only guards gems within this many metres of a player (the gem they're about to go for), never far-away ones.")]
    public float guardOnlyGemsWithin = 30f;
    [Tooltip("When it hides to ambush (while stalking), a hiding spot next to a gem counts as this many metres closer. " +
             "So it often picks one by a gem, but it can hide anywhere.")]
    public float lockerByGemBonus = 6f;
    [Tooltip("When guarding: chance it hides IN a locker by the gem (bursts out when you grab it). " +
             "Otherwise it lurks just out of sight of the gem, around a corner.")]
    [Range(0f, 1f)] public float guardFromLockerChance = 0.5f;
    [Tooltip("Looks for lockers / lurking spots within this many metres of the gem.")]
    public float guardRadius = 10f;
    [Tooltip("How long it waits by a gem (min, max seconds) before giving up and wandering again.")]
    public Vector2 guardSeconds = new Vector2(15f, 30f);
    [Tooltip("Picking up a gem makes a sound. If it's within this many metres, it hears it and comes to look.")]
    public float gemPickupHearingRange = 25f;

    [Header("SCARE (fake hunt = a hunt that can't hurt you)")]
    [Tooltip("Chance of a fake hunt each time it checks (every 'Scare Check Interval').")]
    public BehaviourChance fakeHuntChance = new BehaviourChance(0.06f, 0.02f, 0.15f);
    public float scareCheckInterval = 6f;
    public float scareCooldown = 40f;
    public Vector2 fakeHuntDuration = new Vector2(2f, 4f);
    [Tooltip("\"ANGRY\": chance (each scare check, while it's near you) that it just attacks, ring or not. " +
             "Keep it small: players need to trust the rules MOST of the time.")]
    [Range(0f, 1f)] public float realHuntChance = 0.01f;
    [Tooltip("...plus this much more at boldness 1.")]
    [Range(0f, 1f)] public float boldnessHuntBonus = 0f;
    [Tooltip("Bolder = hides less: at boldness 1 the hide chance is multiplied by (1 - this).")]
    [Range(0f, 1f)] public float boldnessHideReduction = 0.4f;

    [Header("BAIT (the trickster: lure them closer...)")]
    [Tooltip("After slipping away, it sometimes relocates FAR down the hallway ahead of you, steps out at the end, and waits. " +
             "Come too close and it charges.")]
    public bool canBait = true;
    [Tooltip("How far down the hallway (min, max metres). (How OFTEN it baits: Bait Weight, under AFTER SLIPPING AWAY.)")]
    public Vector2 baitDistance = new Vector2(18f, 30f);
    [Tooltip("Come closer than this while it's baiting you, and it charges.")]
    public float baitAttackDistance = 10f;
    [Tooltip("If you don't take the bait, it gives up after this long (seconds) and slips away.")]
    public float baitMaxSeconds = 12f;

    [Header("PEEKING (let them know something's there)")]
    [Tooltip("If no player has seen it for a while, it peeks around a corner in front of them: only PART of it " +
             "is visible (a shoulder, its head). When they notice, it slips away.")]
    public bool peekWhenUnseen = true;
    [Tooltip("Seconds without being seen before it peeks.")]
    public float peekAfterUnseen = 12f;
    [Tooltip("WATCHING YOU: while it waits behind a corner near you, it now and then leans out to watch you " +
             "(only an edge of it sticks out), then leans back. Look at the right moment and you catch it.")]
    public bool leanOutToWatch = true;
    [Tooltip("How often it leans out (min, max seconds between).")]
    public Vector2 leanOutEvery = new Vector2(3f, 7f);
    [Tooltip("How long it stays leaned out, watching (min, max seconds).")]
    public Vector2 leanOutHoldSeconds = new Vector2(2f, 4f);
    [Tooltip("A peek spot shows between 1 and this many of the 9 body points (9 = fully in the open).")]
    [Range(1, 8)] public int peekMaxVisiblePoints = 3;
    [Tooltip("Gives up peeking after this long (seconds) if nobody notices.")]
    public float peekMaxSeconds = 10f;
    [Tooltip("PEEK FROM LOCKERS: sometimes it peeks out of a locker instead of from a corner. It slips in unseen, " +
             "the door silently eases open, and its eyes watch you through the gap. Spot it and the door eases shut: you KNOW " +
             "it's in there. Walk past: it bursts out. Open it: you followed it.")]
    public bool peekFromLockers = true;
    [Tooltip("When it's time to peek, the chance it uses a locker (if there's one in front of you) instead of a corner.")]
    [Range(0f, 1f)] public float lockerPeekChance = 0.35f;
    [Tooltip("After slipping into the locker, it waits this long (seconds) before the door opens.")]
    public Vector2 lockerPeekDelay = new Vector2(1.5f, 3f);
    [Tooltip("Spotted in a locker: it stays in there (door ajar, one eye) this long before slipping away.")]
    public Vector2 stayInLockerAfterSpotted = new Vector2(8f, 15f);

    [Header("HUNT (the real attack)")]
    [Tooltip("Player walk = 3.5, sprint = 6. Keep it BETWEEN those: walking gets you caught, sprinting gets you away (while stamina lasts).")]
    public float huntSpeed = 5.2f;
    [Tooltip("A short warning (it stops and roars) before every hunt starts moving, in seconds. 0 = instant. " +
             "Fair warning = scarier AND fairer.")]
    public float huntWindupSeconds = 0.6f;
    public float huntDuration = 12f;
    public float huntCooldown = 20f;
    [Tooltip("The hunt ends if it can't SEE you for this long (break line of sight and it loses you).")]
    public float huntLoseSightSeconds = 2.5f;
    [Tooltip("Default rule (ring OFF): hunts when this close after stalking long enough.")]
    public float huntTriggerDistance = 6f;
    public float minStalkSecondsBeforeHunt = 10f;
    [Tooltip("THE ATTACK: when this close (metres), it stops, winds up, then LUNGES at where you are.")]
    public float attackRange = 3.5f;
    [Tooltip("The wind-up (seconds): it stops for a moment. That's your cue to sidestep.")]
    public float attackWindupSeconds = 0.35f;
    [Tooltip("The lunge: this fast (m/s)...")]
    public float lungeSpeed = 10f;
    [Tooltip("...for this long (seconds). It commits to a direction, so sidestepping makes it miss.")]
    public float lungeSeconds = 0.35f;
    [Tooltip("During the lunge, you're caught if it gets this close.")]
    public float catchDistance = 1.3f;
    [Tooltip("After a missed lunge it's slow for this long (seconds): your chance to get away.")]
    public float attackRecoverSeconds = 0.8f;

    [Header("DISGUISE (the Trickster copies another monster in the match)")]
    [Tooltip("On: now and then, out of sight, it turns into a copy of another monster in the match (same body, same behaviour)... " +
             "with a TELL. Copying the Brute: it's completely SILENT (the real one is always loud).")]
    public bool canDisguise = false;
    [Tooltip("Seconds between disguises (random in this range). The first one comes no earlier than the minimum, either.")]
    public Vector2 disguiseEvery = new Vector2(90f, 150f);
    [Tooltip("Longest it stays disguised (seconds). Then it changes back out of sight.")]
    public float disguiseMaxSeconds = 60f;
    [Tooltip("Disguised and it reaches you: the chance it's a REAL catch. Otherwise a purple flash in your face, and it's gone.")]
    [Range(0f, 1f)] public float disguiseRealCatchChance = 0.25f;
    [Tooltip("CALL ITS BLUFF: watch it charge at you WITHOUT sprinting away for this long, and it's exposed (flash, gone, ring pushed back).")]
    public float bluffCallSeconds = 1.5f;
    [Tooltip("...and only counts while it's this close (metres).")]
    public float bluffCallDistance = 14f;
    [Tooltip("Called its bluff: the ring is pushed back this much extra (your reward).")]
    public float bluffRingPushBack = 6f;
    [Tooltip("Can the Trickster copy THIS monster? (Each copyable monster needs a tell: the Brute's is silence.)")]
    public bool canBeImpersonated = false;
    [Tooltip("What gives the copy away (shown in the debug panel; design note). The Brute: \"silent: no footsteps, no roar\".")]
    public string disguiseTell = "";

    [Header("TIRES OUT (the Brute: charges, then has to catch its breath)")]
    [Tooltip("On: when a chase runs out (Hunt Duration above), it stops dead and rests instead of going back to stalking.")]
    public bool tiresOut = false;
    [Tooltip("How long it stands there catching its breath after a chase. Your window to get away.")]
    public float restSeconds = 6f;

    [Header("WHEN SPOTTED")]
    public SpottedReactionWeights spottedReactions = new SpottedReactionWeights();
    [Tooltip("How long a player must look at it before it counts as 'spotted'.")]
    public float secondsWatchedBeforeSpotted = 0.6f;
    [Tooltip("For Stare-Then-Hide / Flee: how long it holds eye contact first (min, max seconds). " +
             "A predator doesn't bolt the instant it's seen.")]
    public Vector2 stareSeconds = new Vector2(1f, 2f);
    [Tooltip("MENACE, NOT FEAR: it backs away at this speed, still FACING you, into cover. (Prey runs; predators back off.)")]
    public float retreatSpeed = 2.5f;
    [Tooltip("Only if the nearest cover is further than this (metres) does it turn and dash (Escape Speed).")]
    public float dashIfCoverFartherThan = 8f;
    [Tooltip("If the nearest cover is further than this (metres of walking), it TELEPORTS away right after the stare, " +
             "even if you're watching, instead of slowly backing off for ages. (Needs Can Teleport.)")]
    public float teleportIfCoverFartherThan = 5f;
    [Tooltip("DON'T FOLLOW HIM: if you're still watching it this long (seconds) after it started backing away, and you " +
             "kept up with it, it attacks. If you only watched (didn't follow), it teleports away instead.")]
    public float maxRetreatSeconds = 3f;
    [Tooltip("Escape routes never pass closer than this to a player (no running right past you to hide).")]
    public float escapeKeepAwayFromPlayers = 3f;

    [Header("WHEN SPOTTED: ESCAPING")]
    [Tooltip("How fast it dashes to cover once it decides to go (player sprint is 6).")]
    public float escapeSpeed = 9f;
    [Tooltip("How quickly it reaches full speed / turns while escaping. High = snappy, no slow wide turns.")]
    public float escapeAcceleration = 60f;
    [Tooltip("Only considers cover it can reach within this many metres of WALKING (real path, not straight line).")]
    public float maxCoverPathLength = 25f;
    [Tooltip("After slipping out of sight, it holds still this long (min, max) before stalking again.")]
    public Vector2 lurkAfterHidingSeconds = new Vector2(2f, 5f);
    [Header("WHEN SPOTTED: AFTER SLIPPING AWAY (the mix)")]
    [Tooltip("Once out of sight after being spotted, it picks ONE of these by weight (they don't need to add up to 1). " +
             "TELEPORT AWAY: a purple flash, and it's gone (only if the flash budget allows; otherwise it walks off).")]
    [Range(0f, 1f)] public float teleportAwayWeight = 0.35f;
    [Tooltip("WALK OFF: no flash. It just moves away out of sight. Is it still around here...?")]
    [Range(0f, 1f)] public float walkAwayWeight = 0.35f;
    [Tooltip("BAIT: reappears far down the hallway ahead of you (see BAIT). Uses a teleport (with flash).")]
    [Range(0f, 1f)] public float baitWeight = 0.15f;
    [Tooltip("STAY: waits right there around the corner. Follow it and it gets you. (Limited per spot, see STAYING PUT.)")]
    [Range(0f, 1f)] public float stayWeight = 0.15f;
    [Tooltip("Waiting around the corner: attacks if a player comes this close.")]
    public float cornerAmbushDistance = 4f;
    [Tooltip("Waiting around the corner: gives up after this long (seconds) and goes back to stalking.")]
    public float cornerWaitSeconds = 8f;

    [Header("WHEN SPOTTED: CAUGHT OFF GUARD (in the open)")]
    [Tooltip("Spotted with no corner nearby (you caught IT off guard): it doesn't run. It stands dead still and stares back. " +
             "Look away and it's gone. Keep staring (stare meter) or walk up to it, and it attacks.")]
    public bool standAndStareWhenCaughtInOpen = true;
    [Tooltip("'In the open' = no corner (cover) within this many metres.")]
    public float openNoCornerWithin = 3f;
    [Tooltip("Standing and staring: walk closer than this and it attacks.")]
    public float standAndStareAttackDistance = 6f;
    [Tooltip("Standing and staring: look away for just this long (seconds) and it's gone. Short = no 'still there, " +
             "still there' when you glance back and forth.")]
    public float standAndStareVanishAfterUnseen = 0.25f;

    [Header("WHEN SPOTTED: STAYING PUT (rare)")]
    [Tooltip("Spotted at a corner, it can stay right there once. Spotted AGAIN at the same spot, it only stays again with " +
             "this chance (and again, and again...). Otherwise it really leaves. Keeps 'still there... still there' rare.")]
    [Range(0f, 1f)] public float extraStayChance = 0.3f;

    [Header("WHEN SPOTTED: WATCHED TOO LONG")]
    [Tooltip("The Stalker's loop: spot it and let it leave. Stare at it or follow it too long and it attacks. " +
             "The meter does NOT fill while it's leaving (backing away after being spotted), resets after each " +
             "encounter, and pauses for a few seconds after a hunt.")]
    public bool attackWhenWatchedTooLong = true;
    [Tooltip("After a hunt: no stare meter and no stand-and-stare attack for this long (seconds). A breather.")]
    public float graceAfterHunt = 5f;
    [Tooltip("Seconds of being watched (the 'stare meter') before it attacks.")]
    public float watchedTooLongSeconds = 6f;
    [Tooltip("The stare meter fills this many times faster while you walk TOWARD it (following it).")]
    public float followPressureMultiplier = 2f;
    [Tooltip("How fast the stare meter empties while nobody is looking (seconds per second).")]
    public float pressureDecayPerSecond = 1f;
    [Header("WHEN SPOTTED: FREEZE")]
    [Tooltip("Freeze snaps if you get this close (metres). 0 = never snaps (e.g. the Statue).")]
    public float freezeBreakDistance = 5f;
    [Tooltip("...or if you walk this many metres closer than when it froze.")]
    public float freezeBreakApproach = 3f;
    [Tooltip("LEARNED: when freeze snaps, chance it ATTACKS you. Otherwise it runs as fast as it can.")]
    public BehaviourChance freezeBreakAttackChance = new BehaviourChance(0.4f, 0.2f, 0.7f);
    [Tooltip("Safety net: if neither of you moves for this long (min, max seconds), it slips away to hide.")]
    public Vector2 maxFreezeSeconds = new Vector2(12f, 20f);

    [Header("THE HIDING RULE")]
    [Tooltip("It only counts as hidden once NO player has seen ANY part of it for this many seconds. " +
             "Spotted reactions, getting into lockers and lurking all wait for this.")]
    public float unseenSecondsToCountAsHidden = 0.6f;

    [Header("SENSES")]
    public float sightRange = 25f;
    [Range(10f, 360f)] public float sightAngle = 140f;
    [Tooltip("Hears a player within (this × their noise level). Sprinting = full range.")]
    public float hearingRange = 18f;
    [Tooltip("Seconds without seeing/hearing the player before it gives up (or searches hiding spots).")]
    public float memorySeconds = 8f;
    public float loseInterestDistance = 45f;

    [Header("LEARNING")]
    [Tooltip("OFF = every chance stays at its 'start' value (plain randomness). Handy while testing the basics.")]
    public bool learningEnabled = true;
    [Range(0f, 1f)] public float adaptability = 0.6f;

    [HideInInspector] public int builderVersion;   // used by the scene builder to refresh old profiles
}
