using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Plays the model's animations to match what the MonsterBrain is doing.
/// The brain never touches animations; this script just WATCHES the brain (state, speed) and picks one.
/// That keeps "what it decides" and "how it looks" separate, so artists can swap models freely.
///
/// The text fields below are state names in the Animator Controller (Assets/Monsters/CreepAnimator).
/// To try a different animation for something: double-click that controller to see the names, type one in here.
/// </summary>
[RequireComponent(typeof(MonsterBrain))]
public class MonsterAnimator : MonoBehaviour
{
    [Tooltip("The Animator on the model (found automatically if left empty).")]
    public Animator animator;

    // Kept deliberately SIMPLE for now (idle / walk / attack). Fancy animations (peeking, roaring, banging)
    // come later, when you design your own. Each slot below is ready for that: just type a new state name.
    [Header("Which animation for what")]
    public string idle = "Idle1";
    [Tooltip("Lurking after slipping out of sight, waiting by a gem, standing behind you.")]
    public string lurk = "Idle1";
    public string walk = "Walk1";
    [Tooltip("Moving while stalking you.")]
    public string stalk = "Walk1";
    [Tooltip("Moving fast: escaping, hunting.")]
    public string fastMove = "Walk1";
    [Tooltip("The Freeze reaction: dead still.")]
    public string freeze = "Idle1";
    [Tooltip("The warning before a hunt.")]
    public string roar = "Idle1";
    [Tooltip("The lunge.")]
    public string attack = "Bite";
    [Tooltip("Banging on your locker door.")]
    public string bang = "Punch";
    [Tooltip("Opening a hiding spot to look inside.")]
    public string checkSpot = "Idle1";

    [Header("Matching the feet to the speed")]
    [Tooltip("How fast (m/s) the walk animation moves at normal playback. Feet slide forward = raise it; moonwalking = lower it.")]
    public float walkAnimSpeed = 1.4f;
    [Tooltip("Same, for the fast animation.")]
    public float fastAnimSpeed = 2.5f;
    [Tooltip("Above this speed (m/s) it switches to the fast animation.")]
    public float fastThreshold = 4.5f;
    [Tooltip("Seconds to blend from one animation into the next.")]
    public float crossFade = 0.15f;
    [Tooltip("When its rule stops it moving (the Statue while watched), freeze the animation mid-pose. Creepy.")]
    public bool freezeWhenRuleStopsIt = true;

    MonsterBrain brain;
    NavMeshAgent agent;
    string current;

    void Awake()
    {
        brain = GetComponent<MonsterBrain>();
        agent = GetComponent<NavMeshAgent>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;   // the NavMeshAgent moves it, not the animation
    }

    void Update()
    {
        if (animator == null || animator.runtimeAnimatorController == null || brain.profile == null) return;
        if (brain.IsHiddenInSpot) return;   // invisible inside a locker anyway

        // The Statue (or any monster whose rule says "can't move"): stop the animation exactly where it is.
        if (freezeWhenRuleStopsIt && !brain.CanMoveNow)
        {
            animator.speed = 0f;
            return;
        }

        float speed = agent.enabled ? agent.velocity.magnitude : 0f;
        bool wasMoving = current == walk || current == stalk || current == fastMove;
        bool moving = speed > (wasMoving ? 0.15f : 0.35f);   // two thresholds, so it doesn't flicker walk/idle/walk

        string next;
        float playSpeed = 1f;
        if (brain.IsAttacking) next = attack;
        else if (brain.IsWindingUp) next = roar;
        else if (brain.IsBanging) next = bang;
        else if (brain.IsCheckingSpot) next = checkSpot;
        else if (moving && speed > fastThreshold) { next = fastMove; playSpeed = speed / fastAnimSpeed; }
        else if (moving) { next = brain.State == MonsterState.Stalk ? stalk : walk; playSpeed = speed / walkAnimSpeed; }
        else if (brain.State == MonsterState.Spotted && brain.CurrentReaction == SpottedReaction.Freeze) next = freeze;
        else if (brain.IsLurking || brain.IsWaitingAtGem || brain.State == MonsterState.Stalk) next = lurk;
        else next = idle;

        animator.speed = Mathf.Clamp(playSpeed, 0.5f, 3f);
        if (next == current) return;
        current = next;

        if (animator.HasState(0, Animator.StringToHash(next)))
            animator.CrossFadeInFixedTime(next, crossFade);
        else
            Debug.LogWarning($"{name}: no animation called '{next}' in {animator.runtimeAnimatorController.name}. " +
                             "Check the names on the MonsterAnimator component.", this);
    }
}
