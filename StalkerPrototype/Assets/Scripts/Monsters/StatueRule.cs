using UnityEngine;

/// <summary>
/// THE STATUE: can't move while ANY player can see it, and only attacks when NOBODY is looking.
/// The same rule both stops it and triggers it.
/// </summary>
public class StatueRule : MonsterRule
{
    [Tooltip("How close it must get (while nobody is looking) before it lunges.")]
    public float lungeRange = 8f;

    public override bool CanMove(MonsterBrain m) => !m.IsWatched;

    public override bool WantsToHunt(MonsterBrain m) => !m.IsWatched && m.DistanceToTarget <= lungeRange;
}
