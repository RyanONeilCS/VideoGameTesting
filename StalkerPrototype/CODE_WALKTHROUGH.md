# How the Monster Code Works

A plain-English tour of `MonsterBrain.cs`, built around the questions you'll have when you first open it. You don't need to memorise any of this. Keep it open next to the code.

**Contents**
1. [The 5 patterns that explain 90% of the file](#1-the-5-patterns-that-explain-90-of-the-file)
2. [The top of the file: what all those lines are](#2-the-top-of-the-file-what-all-those-lines-are)
3. [The variables, one by one](#3-the-variables-one-by-one)
4. [When Unity runs each method (the lifecycle)](#4-when-unity-runs-each-method-the-lifecycle)
5. [One frame, start to finish](#5-one-frame-start-to-finish)
6. [The states](#6-the-states)
7. [The math, explained](#7-the-math-explained)
8. [The NavMesh calls](#8-the-navmesh-calls)
9. [Exercises to get comfortable](#9-exercises-to-get-comfortable)

---

## 1. The 5 patterns that explain 90% of the file

Learn these five and the rest is just repetition.

### Pattern 1: Timers are "a moment in the future"
Unity has no built-in "wait 3 seconds" inside `Update`. Instead we store **the time when something should happen**, and check every frame whether that time has come:

```csharp
lurkUntil = Time.time + 3f;          // "lurk until 3 seconds from now"
...
if (Time.time < lurkUntil) return;   // every frame: still lurking? then do nothing else
```

`Time.time` = seconds since the game started. Every variable ending in **`Until`**, **`At`** or **`EndsAt`** (`stareUntil`, `huntAllowedAt`, `huntEndsAt`...) is one of these timers.

### Pattern 2: A state machine = one variable + one switch
The monster is always in exactly one `State`. `Update()` runs the matching method:

```csharp
switch (State)
{
    case MonsterState.Wander: UpdateWander(); break;
    case MonsterState.Stalk:  UpdateStalk();  break;
    ...
}
```

To change state, the code calls `Enter(MonsterState.Hunt)`. `Enter` records the time (for `TimeInState`), resets a few things, and announces the change (the `StateChanged` event, which the logger writes to a CSV).

### Pattern 3: "Check, then return early"
Each `Update...()` method is a list of checks in priority order. The first one that fires does its thing and **returns**, so nothing below it runs that frame:

```csharp
void UpdateStalk()
{
    if (watchedFor >= ...) { Enter(MonsterState.Spotted); return; }   // 1st priority
    if (rule.WantsToHunt(this)) { StartHunt(false); return; }         // 2nd priority
    ...                                                               // otherwise: keep stalking
}
```

Read them top to bottom like a decision list.

### Pattern 4: Variables that hold *references* to other things
`HidingSpot sawPlayerHideIn;` isn't true/false. It holds **a reference to one specific locker object** (or `null` = none). So `sawPlayerHideIn = Target.currentHidingSpot;` means "remember *which* locker I saw you climb into." Later, `if (sawPlayerHideIn != null)` asks "did I see you hide anywhere?" and `spotsToCheck.Insert(0, sawPlayerHideIn)` puts *that exact locker* first in the search list.

Same idea for `PlayerAIState Target` (*which* player I'm after), `NavMeshAgent agent` (*my* walking component), and so on.

### Pattern 5: Chances are dice rolls
`BehaviourChance` (*chance*, not "change") is a number from 0 to 1. `FakeHuntChance.Roll()` returns `true` with that probability, like rolling dice. Learning nudges the number up or down **inside its limits**. With learning switched off (profile → *Learning Enabled*), it's plain randomness.

---

## 2. The top of the file: what all those lines are

| You see | What it is |
|---|---|
| `using UnityEngine;` | Imports, like Java `import` |
| `public enum MonsterState { Wander, Stalk, ... }` | A list of named options (the states) |
| `/// <summary> ... </summary>` | **Documentation comments.** Just notes for humans. Your editor shows them when you hover over a name. Not code, not "a summary of every monster." |
| `[Tooltip("...")]` | Text that appears when you **hover over that field in Unity's Inspector**. Also just help text. |
| `[RequireComponent(typeof(NavMeshAgent))]` | Tells Unity "this script needs a NavMeshAgent on the same object" (it adds one if missing) |
| `[Header("WANDER")]` | Draws a bold heading in the Inspector, to group settings |
| `[Range(0, 1)]` | Shows a slider in the Inspector instead of a text box |
| `public class MonsterBrain : MonoBehaviour` | Our script. `: MonoBehaviour` = "this is a Unity component" (can be attached to objects; gets `Update` etc.) |
| `public static readonly List<MonsterBrain> All` | **One shared list of every monster in the game.** `static` = belongs to the class, not to one monster. Each monster adds itself in `OnEnable`. The HUD and logger loop over it. |
| `public static event ... StateChanged` | **A notification.** When any monster changes state, everyone "subscribed" gets told. The logger subscribes so it can write each change to a file. |
| `public MonsterProfile profile;` | A **public field**: shows up in the Inspector, where you drag in the profile file |
| `public MonsterState State { get; private set; }` | A **property**: anyone can *read* it (HUD, rules), only this script can *change* it |
| `public bool IsFullyHidden => UnseenFor >= ...;` | A property computed fresh every time you read it (`=>` means "returns") |

---

## 3. The variables, one by one

### Settings (set in the Inspector)
| Variable | Meaning |
|---|---|
| `profile` | The monster's settings file (`Assets/Monsters/Stalker`) |
| `eyeHeight` | How high its eyes are (for line-of-sight checks) |

### What it knows right now (public, read-only for others)
| Variable | Meaning |
|---|---|
| `State` | Which state it's in |
| `Target` | Which player it's focused on (the closest one) |
| `CanSeeTarget` | Can it see that player this frame? |
| `IsWatched` | Can **any** player see **any** part of it this frame? |
| `UnseenFor` / `IsFullyHidden` | Seconds since anyone saw it / "has it been unseen long enough to count as hidden?" |
| `WatchPressure` | The stare meter: fills while watched, attacks when full |
| `CanMoveNow` | What its rule says this frame (the Statue's is false while watched) |
| `DistanceToTarget` | Metres to the player |
| `IsFakeHunt`, `IsHiddenInSpot`, `IsBanging`, `CurrentReaction` | Details for the HUD and rules |

### Learned chances
`Boldness`, `FakeHuntChance`, `HideAmbushChance`, `CheckSpotsChance`, `FreezeBreakAttackChance`, `OpenAfterBangingChance`. These are **copies** of the profile's chances, so learning during play doesn't change the saved file.

### References to its own parts (filled in `Awake`)
| Variable | Meaning |
|---|---|
| `agent` (`NavMeshAgent`) | Its "legs": the Unity component that walks along the NavMesh. We tell it *where* to go; it figures out *how*. |
| `rule` (`MonsterRule`) | Its quirk script (default rule, or `StatueRule`) |
| `renderers` (`Renderer[]`) | Everything that *draws* it (body, eyes). Switched off to make it invisible inside a locker. |
| `colliders` (`Collider[]`) | Its invisible physical shape. Blocks movement and line-of-sight. Switched off while inside a locker, and briefly while it tests hiding spots (so its own body doesn't "block the view"). |

### Memory
| Variable | Meaning |
|---|---|
| `spawnPosition` | Where **the monster** started. After catching you, it gets sent back here. |
| `lastKnownPosition` | Where it last saw or heard **the player**. It goes here when it loses sight of you. |
| `lastSeenTime`, `lastHeardTime` | **When** it last saw / heard the player (used for "did they get away?") |
| `stateEnteredAt` | When it entered its current state (so `TimeInState` = how long it's been in it) |
| `watchedFor` | How long it's been watched *continuously* (resets the moment you look away) |
| `sawPlayerHideIn` | *Which* locker it saw you climb into (or `null`) |
| `spotsToCheck`, `spotIndex` | Its search to-do list, and which one it's on |
| `hideSpot` | The locker *it* is hiding in or heading to |

### Timers (Pattern 1)
`wanderPauseUntil`, `nextRepathTime`, `nextScareCheck`, `scareAllowedAt`, `huntAllowedAt`, `huntEndsAt`, `hideAllowedAt`, `stareUntil`, `freezeUntil`, `lurkUntil`, `bangUntil`, `searchAllowedAt`... all "moments in the future."

---

## 4. When Unity runs each method (the lifecycle)

You never call these yourself. **Unity calls them.**

| Method | When | What ours does |
|---|---|---|
| `Awake()` | Once, when the object is loaded (before the first frame) | Grabs its own parts (`GetComponent`), checks it has a profile, makes its own copies of the chances |
| `OnEnable()` | When the object is switched on | Adds itself to the `All` list |
| `OnDisable()` | When the object is switched off or destroyed | Removes itself from the `All` list |
| `Start()` | Once, just before its first `Update` (after every object's `Awake`) | Puts itself on the NavMesh, turns its legs on, sets the first timers, enters Wander |
| `Update()` | **Every frame** (~60+ per second) | Everything else: see section 5 |
| `OnDrawGizmosSelected()` | In the editor, when you select it | Draws the yellow sight cone and blue hearing circle |

Why is the agent turned on in `Start` and not `Awake`? The NavMesh is built in `NavMeshBootstrap.Awake()`. `Start` is guaranteed to run after **every** `Awake`, so the walkable map exists by then.

---

## 5. One frame, start to finish

Every frame, `Update()` does this, in order:

1. **`PickTarget()`:** loop over all players, keep the closest.
2. **Inside a locker?** Run the small locker update instead and stop.
3. **`Sense()`:**
   - **Sight:** is the player in range, inside my view cone, and not behind a wall?
   - **Hearing:** is the player's noise × my hearing range bigger than the distance?
   - **Being watched:** can any player see any part of me? Update `watchedFor`, `UnseenFor`, and the stare meter.
4. **Ask the rule:** `CanMoveNow = rule.CanMove(this)`. If false, the legs stop. The rule always wins.
5. **Global checks:** stared at too long → attack. Lost the player → search lockers or wander.
6. **Run the current state** (the `switch`).

---

## 6. The states

| State | Method | In one line |
|---|---|---|
| **Wander** | `UpdateWander` | Walk to random points, usually near your rough area; investigate noises; sees you → Stalk |
| **Stalk** | `UpdateStalk` | Follow behind you. Checks, in order: spotted? → rule says hunt? → roll for a scare or a locker ambush → learn → lurk → move |
| **Spotted** | `UpdateSpotted` | Pick a reaction. Stare-then-hide: short stare, then **dash to the cover with the shortest walk**; done only when fully unseen. Then lurk there, or relocate. |
| **Hunt** | `UpdateHunt` | Chase. Real: catch you if close. Fake: vanish before touching you. |
| **Search** | `UpdateSearch` | Check lockers near where it lost you. If it *saw* you get in: bang on the door first, then maybe open it. |
| **Hide** | `UpdateGoingToHide` + `UpdateHiddenInSpot` | Walk to a free locker near you, get in when unseen, burst out when you walk past |

---

## 7. The math, explained

You know linear algebra, so this is just the Unity spelling of it.

| Code | Meaning |
|---|---|
| `transform.position` | **Where this object is** in the world (a `Vector3`: x, y, z). "Transform" is just Unity's name for the position/rotation/scale component. Nothing is being transformed. |
| `Target.transform.position` | Where the player is |
| `b - a` | The vector **from a to b** (direction × distance) |
| `(b - a).magnitude` or `Vector3.Distance(a, b)` | The distance between them |
| `v.normalized` | Same direction, length 1 |
| `Vector3.Angle(a, b)` | Angle between two directions, in degrees. Used for "is the player inside my view cone?" |
| `Vector3.Dot(a, b)` | Dot product. With normalized vectors, > 0.5 means "roughly the same direction" |
| `Vector3.up * 1.85f` | 1.85 m straight up (y is up in Unity) |
| `Quaternion.Euler(0, 45, 0) * Vector3.forward` | "Forward, turned 45° to the right": used to fan out search directions |
| `Quaternion.LookRotation(d)` | A rotation that faces direction `d` |
| `Quaternion.Slerp(a, b, t)` | Smoothly turn part of the way from rotation a to b (so it doesn't snap) |
| `Physics.Linecast(a, b, out hit)` | Shoot an invisible line from a to b. Returns true if something is in the way, and `hit` says what. Used for **line of sight**. |
| `Random.value`, `Random.Range(a, b)` | Random number 0–1, or between a and b |

**Example: "Can I see the player?"**
```csharp
Vector3 toTarget = Target.EyePosition - EyePosition;                 // vector from my eyes to theirs
bool inRange = toTarget.magnitude <= profile.sightRange;              // close enough?
bool inCone  = Vector3.Angle(transform.forward, toTarget) <= profile.sightAngle * 0.5f;  // in front of me?
CanSeeTarget = inRange && inCone && HasLineOfSight(...);              // and no wall in between?
```

---

## 8. The NavMesh calls

The **NavMesh** is a map of where it's possible to walk, baked from the floor and walls when the game starts. The **NavMeshAgent** walks on it.

| Code | Meaning |
|---|---|
| `agent.SetDestination(p)` | "Walk to p." The agent plans the route around walls by itself. |
| `agent.speed`, `agent.acceleration` | How fast it walks / how quickly it reaches that speed |
| `agent.isStopped = true` | Freeze the legs (keeps the destination) |
| `agent.remainingDistance` | How far it still has to walk. `< 0.6` ≈ "arrived" |
| `agent.pathPending` | Still calculating the route. Don't trust `remainingDistance` yet. |
| `agent.Warp(p)` | Teleport there instantly (vanishing, respawning) |
| `NavMesh.SamplePosition(p, out hit, r, ...)` | "Find the nearest walkable point to p, within r metres." Random points might be inside a wall; this fixes them. |
| `NavMesh.CalculatePath(from, to, ..., path)` | Work out the route **without** walking it. `path.corners` = the turning points. Adding up the gaps between corners gives the **real walking distance**. That's how it picks the *quickest* cover now. |

---

## 9. Exercises to get comfortable

Try these in order. Each one is tiny. Stop Play before editing code.

1. **Print every state change.** In `Enter()`, add `Debug.Log($"{name}: {previous} -> {next}");`. Press Play and watch the **Console**.
2. **Make it hide slower.** Profile → *Escape Speed* from 9 to 4. Feel the difference? (That's why it's 9.)
3. **Make it angrier.** Profile → *Watched Too Long Seconds* from 4 to 1.5.
4. **Turn learning off.** Profile → untick *Learning Enabled*. The debug panel says "LEARNING OFF".
5. **Read one state fully.** Read `UpdateStalk()` top to bottom and say out loud what each check does.
6. **Change a rule.** In `MonsterRule.WantsToHunt`, change `>= m.profile.minStalkSecondsBeforeHunt` to `>= 3f`. What happens?
7. **Write your first rule.** Follow section 9 of `START_HERE.md` (the ListenerRule example).

**Tip:** if you write something in another language first (Python, Java), paste it to Claude and ask for the C# version.
