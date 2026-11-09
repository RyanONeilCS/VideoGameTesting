using UnityEngine;

/// <summary>
/// A monster's quirk: its "always true" rule (design doc section 5).
/// A rule answers two questions every frame:
///   1. CanMove     - RESTRICTS it (e.g. the Statue can't move while watched)
///   2. WantsToHunt - TRIGGERS its attack
///
/// This base class is the "no special rule" default. To make a new rule, make a new script that
/// inherits from MonsterRule and override one or both methods (see StatueRule.cs).
/// </summary>
public class MonsterRule : MonoBehaviour
{
    public virtual bool CanMove(MonsterBrain m) => true;

    /// <summary>
    /// Default WITH the ring: hunt once the ring has shrunk all the way (you stopped checking behind you)
    /// and it's actually close to you.
    /// Default WITHOUT the ring: hunt if it can see you, it's close, and it has stalked you for a while.
    /// </summary>
    public virtual bool WantsToHunt(MonsterBrain m)
    {
        // (It must be able to SEE you: it comes around to where you could see it if you turned, then attacks.
        //  No attacking from behind a wall, then rounding the corner too fast to react.)
        if (m.profile.useRing)
            return m.Ring <= m.profile.ringAttackRadius + 0.01f
                && m.DistanceToTarget <= m.profile.ringAttackRadius + 3f
                && m.CanSeeTarget
                && !m.Target.IsHiding;

        return m.CanSeeTarget
            && m.DistanceToTarget <= m.profile.huntTriggerDistance
            && m.TimeInState >= m.profile.minStalkSecondsBeforeHunt;
    }
}
