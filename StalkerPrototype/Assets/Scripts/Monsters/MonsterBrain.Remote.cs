using UnityEngine;

/// <summary>
/// MULTIPLAYER, the monster side (this is the same MonsterBrain class, split into a second file).
///
/// Only the HOST's monster thinks. Every other player has a "remote copy" of it that doesn't think at all:
///   host:   brain runs  ->  GetView() packs up what it's doing  ->  MonsterNet sends it
///   others: MonsterNet receives it  ->  ApplyView() makes the copy show the same thing
/// "What it's doing" = state, the tricks in progress (peeking, baiting, winding up...), hidden in a locker,
/// disguised as whom, etc. Everything the HUD, the sounds and the visuals read. Position comes separately
/// (NetworkTransform).
///
/// This file knows nothing about networking itself: MonsterNet turns the View into network data.
/// </summary>
public partial class MonsterBrain
{
    /// <summary>True on everyone except the host: this copy just mirrors the host's monster.</summary>
    public bool IsRemoteCopy { get; private set; }

    /// <summary>The eyes' material (the locker tell shows these eyes in the gap).</summary>
    public Material EyeMaterial => eyeMaterial;

    /// <summary>Fires on the host when it flashes (teleport flash): position of the light.</summary>
    public event System.Action<Vector3> Flashed;

    float remoteSpeed;

    [System.Flags]
    public enum ViewFlags
    {
        None = 0,
        CanSeeTarget = 1 << 0, IsWatched = 1 << 1, CanMoveNow = 1 << 2, FakeHunt = 1 << 3,
        HiddenInSpot = 1 << 4, Banging = 1 << 5, WindingUp = 1 << 6, CheckingSpot = 1 << 7,
        Lurking = 1 << 8, Peeking = 1 << 9, LeaningOut = 1 << 10, Baiting = 1 << 11,
        WaitingAroundCorner = 1 << 12, FakeTeleport = 1 << 13, Attacking = 1 << 14, LookingAround = 1 << 15,
        Resting = 1 << 16, LockerPeeking = 1 << 17, WaitingAtGem = 1 << 18,
    }

    /// <summary>A snapshot of what the monster is doing right now (what the other players need to see).</summary>
    public struct View
    {
        public MonsterState state;
        public SpottedReaction reaction;
        public ViewFlags flags;
        public float speed, distance, unseenFor, watchPressure, ring, ringMax, bluff;
        public PlayerAIState target;
        public MonsterBrain disguisedAs;
        public HidingSpot bangingOn;
        public Gem guardingGem;
    }

    /// <summary>HOST: pack up what I'm doing.</summary>
    public View GetView()
    {
        var f = ViewFlags.None;
        if (CanSeeTarget) f |= ViewFlags.CanSeeTarget;
        if (IsWatched) f |= ViewFlags.IsWatched;
        if (CanMoveNow) f |= ViewFlags.CanMoveNow;
        if (IsFakeHunt) f |= ViewFlags.FakeHunt;
        if (IsHiddenInSpot) f |= ViewFlags.HiddenInSpot;
        if (IsBanging) f |= ViewFlags.Banging;
        if (IsWindingUp) f |= ViewFlags.WindingUp;
        if (IsCheckingSpot) f |= ViewFlags.CheckingSpot;
        if (IsLurking) f |= ViewFlags.Lurking;
        if (IsPeeking) f |= ViewFlags.Peeking;
        if (IsLeaningOut) f |= ViewFlags.LeaningOut;
        if (IsBaiting) f |= ViewFlags.Baiting;
        if (IsWaitingAroundCorner) f |= ViewFlags.WaitingAroundCorner;
        if (IsFakeTeleporting) f |= ViewFlags.FakeTeleport;
        if (IsAttacking) f |= ViewFlags.Attacking;
        if (IsLookingAround) f |= ViewFlags.LookingAround;
        if (IsResting) f |= ViewFlags.Resting;
        if (IsLockerPeeking) f |= ViewFlags.LockerPeeking;
        if (IsWaitingAtGem) f |= ViewFlags.WaitingAtGem;

        return new View
        {
            state = State, reaction = CurrentReaction, flags = f,
            speed = CurrentSpeed, distance = DistanceToTarget, unseenFor = UnseenFor, watchPressure = WatchPressure,
            ring = Ring, ringMax = RingMax, bluff = BluffMeter,
            target = Target, disguisedAs = DisguisedAs, bangingOn = IsBanging ? BangingOn : null, guardingGem = GuardingGem,
        };
    }

    /// <summary>Everyone but the host: stop thinking, just mirror. (Called before Start.)</summary>
    public void BecomeRemoteCopy()
    {
        IsRemoteCopy = true;
        if (agent != null) agent.enabled = false;   // the host moves it; we only receive its position
    }

    /// <summary>Everyone but the host: show what the host's monster is doing.</summary>
    public void ApplyView(View v)
    {
        bool Has(ViewFlags flag) => (v.flags & flag) != 0;
        const float Forever = 1e9f;

        State = v.state;
        CurrentReaction = v.reaction;
        Target = v.target;
        CanSeeTarget = Has(ViewFlags.CanSeeTarget);
        IsWatched = Has(ViewFlags.IsWatched);
        CanMoveNow = Has(ViewFlags.CanMoveNow);
        IsFakeHunt = Has(ViewFlags.FakeHunt);
        IsBanging = Has(ViewFlags.Banging);
        BangingOn = v.bangingOn;
        DistanceToTarget = v.distance;
        UnseenFor = v.unseenFor;
        WatchPressure = v.watchPressure;
        remoteSpeed = v.speed;
        bluffFor = v.bluff;

        // The "Is..." properties read these timers and switches, so set them to match
        huntStartsAt = Has(ViewFlags.WindingUp) ? Forever : -1f;
        openingUntil = Has(ViewFlags.CheckingSpot) ? 1f : -1f;
        lurkUntil = Has(ViewFlags.Lurking) ? Forever : -1f;
        peeking = Has(ViewFlags.Peeking);
        leaning = Has(ViewFlags.LeaningOut);
        leanReturning = false;
        baiting = Has(ViewFlags.Baiting);
        cornerWaitUntil = Has(ViewFlags.WaitingAroundCorner) ? Forever : -1f;
        fakeTeleportUntil = Has(ViewFlags.FakeTeleport) ? Forever : -1f;
        attackPhase = Has(ViewFlags.Attacking) ? AttackPhase.Lunge : AttackPhase.None;
        lookingAround = Has(ViewFlags.LookingAround);
        restUntil = Has(ViewFlags.Resting) ? Forever : -1f;
        lockerPeek = Has(ViewFlags.LockerPeeking);
        lockerPeekDone = false;
        lockerPeekOpenAt = -1f;
        guardGem = v.guardingGem;
        hideGuardGem = null;
        guardUntil = Has(ViewFlags.WaitingAtGem) ? 1f : -1f;
        if (Target != null)
        {
            rings[Target] = v.ring;
            ringMaxes[Target] = v.ringMax;
        }

        // Inside a locker: invisible
        bool hidden = Has(ViewFlags.HiddenInSpot);
        if (hidden != IsHiddenInSpot)
        {
            IsHiddenInSpot = hidden;
            SetVisible(!hidden);
        }

        // Disguised: take on the other monster's look (or change back)
        if (v.disguisedAs != copying)
        {
            if (v.disguisedAs != null) CopyLookOf(v.disguisedAs);
            else RestoreLook();
            copying = v.disguisedAs;
            disguised = v.disguisedAs != null;
        }
    }

    /// <summary>Everyone but the host: the host's monster flashed (teleport flash) at this point.</summary>
    public void PlayFlashAt(Vector3 at)
    {
        MonsterProfile p = OwnProfile;
        if (p == null || !p.teleportFlash) return;
        LightFlash.Spawn(at, p.teleportFlashColor, p.teleportFlashRange, p.teleportFlashIntensity, p.teleportFlashSeconds);
    }
}
