# Project Stalker — Monster AI Design Doc

> **Status:** Draft v0.2 — living document
> **Owner (Monster AI):** _your name here_
> **Engine:** Unity
> **Companion diagrams:** `Monster_AI_Diagrams.vsdx` (Visio, 18 pages: 1 Big Picture • 2 Player Data • 3 Inside a Monster • 4 Monster States • 5 Monster Toolkit • 6 How a Monster Decides • 7 Learning • 8 Learning Limits • 9 Shared Systems • 10 Designing a Monster • 11 Roster Checklist • 12 Monster Pool • 13–18 one page per monster)

---

## Table of Contents
1. [Game Overview](#1-game-overview)
2. [AI Philosophy: Authored Systems + ML](#2-ai-philosophy-authored-systems--ml)
3. [AI Architecture](#3-ai-architecture)
4. [The Monster Brain](#4-the-monster-brain)
5. [The Monster Toolkit](#5-the-monster-toolkit)
6. [Shared Systems](#6-shared-systems)
7. [Controlled Randomness](#7-controlled-randomness)
8. [Adaptive Behaviour (Learning Player Habits)](#8-adaptive-behaviour-learning-player-habits)
9. [Player Data the AI Tracks](#9-player-data-the-ai-tracks)
10. [Where Data Science / ML Fits](#10-where-data-science--ml-fits)
11. [Multiplayer Notes](#11-multiplayer-notes)
12. [Monster Draft Pool](#12-monster-draft-pool)
13. [Build Order / Roadmap (AI)](#13-build-order--roadmap-ai)
14. [Monster Design Template](#14-monster-design-template)
15. [Open Questions](#15-open-questions)
16. [Glossary](#16-glossary)

---

## 1. Game Overview

**Pitch:** A co-op first-person horror game. Players spawn into a **randomly generated map** with **4–6 random monsters**. They must **run and survive** long enough to find the **armory**, then **fight back and kill the monsters**.

**What makes it special:** The monsters are smart. Each one has its **own quirks and rules** that players must learn to evade it. They don't just chase: they **stalk, watch, toy with, and scare** the players.

**Two phases of a match:**

| Phase | Player goal | Monster goal |
|---|---|---|
| **1. Survive** | Explore, hide, find the armory | Stalk, scare, pick off players |
| **2. Fight back** | Use the armory's weapons to kill monsters | _TBD: flee? ambush? defend?_ (see [Open Questions](#15-open-questions)) |

---

## 2. AI Philosophy: Authored Systems + ML

### Authored system
An **authored system** is AI whose rules are **designed by hand** by a developer, not learned by a machine-learning model.

```
IF  monster can see a player AND the player is NOT looking at it  →  creep closer
IF  the player turns and looks at it                               →  duck behind cover
IF  it hears a noise                                                →  investigate the source
```

Common forms:
- **State machine:** the monster is always in one state (Wander, Stalk, Hunt...) with rules for switching.
- **Behaviour tree:** a tree of tasks checked top-down. Industry standard. Unity has an official **Behavior** package.
- **Utility AI:** every action gets a score; the monster picks from the best-scoring actions.

### Why not train the whole monster with reinforcement learning?

| | Authored system | Trained ML model (RL) |
|---|---|---|
| Who decides behaviour | Designer writes the rules | Learned from rewards/data |
| Predictable | Yes, so quirks stay learnable | Often not |
| Debuggable | Easy ("rule Y fired") | Hard (black box) |
| Change speed | Edit and test right away | Often requires retraining |
| Optimises for "scary"? | Yes, by design | Hard to reward "scary"; tends to just chase |

### Our approach: the hybrid
- **Authored** = the skeleton: states, quirk rules, behaviour.
- **ML / data science** = the information that feeds it: where players probably are, how scared they are, what habits they have.
- Example: the rule *"if stress is low → do a scare"* is authored; the **stress score** comes from a model.

---

## 3. AI Architecture

> See **Visio page 1: Big Picture** and **page 3: Inside a Monster**.

```
            ┌──────────────────────────────────────┐
            │   DIRECTOR  (one for the whole game) │
            │ • Tracks each player's stress level  │
            │ • Paces the game: build → peak → rest│
            │ • Picks which monster may hunt whom  │◄── ML / data science
            └────────────────┬─────────────────────┘
                             │ orders ("stalk player 2", "back off")
      ┌──────────────────────┼──────────────────────┐
      ▼                      ▼                      ▼
 ┌──────────┐          ┌──────────┐           ┌──────────┐
 │ Monster A│          │ Monster B│    ...    │ Monster F│
 │  Brain   │          │  Brain   │           │  Brain   │
 └────┬─────┘          └──────────┘           └──────────┘
      │  behaviour tree / utility AI + quirk rules
      ▼
 ┌─────────────────────────────────────────────────────┐
 │ PERCEPTION: sight cone + line of sight, hearing,    │
 │ memory, "is a player looking at me?", belief about  │
 │ where players are (particle filter)                 │◄── ML / data science
 └─────────────────────────────────────────────────────┘
      ▼
 NAVIGATION: Unity NavMesh, rebuilt at runtime per generated map
```

### Layers

| Layer | Responsibility | Count |
|---|---|---|
| **Director** | Pacing, stress tracking, assigning targets, stopping monsters from piling on one player at once | 1 per match |
| **Habit Tracker** | Counts player habits and turns them into probabilities | 1 per player (or shared) |
| **Monster Brain** | Picks what to do: state, action, target | 1 per monster |
| **Perception** | What the monster sees, hears, and remembers | 1 per monster |
| **Navigation** | Pathfinding on the NavMesh (rebuilt at runtime with `NavMeshSurface.BuildNavMesh()`) | Shared |
| **Telemetry Logger** | Writes player and monster data to CSV/JSON for analysis | 1 per match |

---

## 4. The Monster Brain

> See **Visio page 4: Monster States** and **page 6: How a Monster Decides**. Page 6 is a simplified 5-step version of the full 10-step pipeline below.

### Base behaviour states (shared by all monsters)

| State | What it does |
|---|---|
| **Wander** | Roams; no target yet |
| **Stalk** | Follows a target at a distance, staying out of view |
| **Watch** | Holds a hidden spot that can see the target; waits |
| **Toy** | Deliberate scares: door slams, appear-and-vanish, fake noises |
| **Hunt** | Actively closes in on the target |
| **Spotted!** | A player has seen it. **The reaction depends on the monster** (chase, flee, freeze, vanish, charge, scream...) |
| **Post-armory states** | _TBD per monster: Flee / Ambush / Defend / Enrage_ |

Stalk, Watch, Toy and Hunt are the **active** states: the monster has a target.

### Transitions

| From | To | When |
|---|---|---|
| Wander | Stalk | Spots a player |
| Stalk | Watch | Finds a good hiding spot |
| Watch | Toy | Scare time (target relaxed + director allows it) |
| Toy | Watch | Scare done |
| Watch | Hunt | Target is alone and vulnerable |
| Any active state | **Spotted!** | A player sees the monster |
| Spotted! | **Stalk** | Its reaction is done, so it goes back to stalking |
| Spotted! | **Wander** | The player ran away |
| Any state | Wander | Players get far enough away |

Each monster's **quirk rules** sit on top of these and **always override them**.

**Where the particle filter fits:** after a player runs away, Wander doesn't have to be random. The monster can drift toward where the player *probably* went, so it picks the trail back up naturally.

### Stalking techniques

- **View-cone check:** compare the player's look direction with the direction to the monster (dot product), then confirm with a raycast. If the player is looking → slip into cover. If not → creep closer.
- **Watching-spot search:** score candidate points: they must see the target, be outside the target's view, be dark, and be near an escape route. Go to the best one and wait.
- **Toy actions:** door slams, appearing at the end of a hallway then vanishing, deliberate noises to herd players, following the player who strays.
- **Scares only one player sees:** the host tells just one player's game to show something. Their friends see nothing.

### The stalking ring (built in the prototype)
The core of "stalking" is one number per player: **the ring**, the distance the monster keeps.
- It starts at about how far players can see (24 m). It **shrinks** slowly while the monster is in position and nobody spots it. It shrinks faster when the monster is bolder (learned).
- **Spotting the monster pushes the ring back** (+7 m), and the monster slips away.
- When the ring reaches about 4 m, the monster is right behind you. It roars, then attacks.
- **The player's rule:** keep checking around you and it never gets close. Get careless and it's on you.
- **Each tick:** the monster tests 16 points on the ring and picks the one that is out of every player's sight, behind the target, and a short walk away.

**In co-op:** every player has their own ring. The monster goes after the player whose ring is smallest, which is whoever isn't watching their back. Teammates who cover each other keep it away, and the player who strays from the group gets picked off. That is exactly the behaviour we want to encourage.

**The suspense toolkit around the ring:**

| Tool | What the player experiences |
|---|---|
| **Peek** | Nobody has seen it for 20 s, so it goes to a spot in front of you where only 1–4 of its 9 body points show: a shoulder or a head past a corner. |
| **Duck and wait** | Spotted, it ducks around a corner and **stays there**. Follow it within 4 m and it attacks. Sometimes it's simply gone (relocated). |
| **Stare meter** | Staring at it too long is also punished. Glance, don't stare. |
| **Fair attack** | It roars (0.6 s), then lunges in a fixed direction after a short wind-up, so you can sidestep. Hunt speed sits between walking and sprinting, and sprint is limited by stamina. Breaking line of sight for 2.5 s ends the hunt, and the ring resets to 20 m. |

**Peeking with real art later:** the code already finds "partly visible" spots. The animation team only needs a *peek pose*: head and hands around a corner, body hidden. Players only ever see the visible part, so the body can be in any pose.

### Decision pipeline (each AI tick)

1. **Sense:** sight, hearing, events
2. **Update belief:** where is each player probably? (particle filter)
3. **Read director orders:** target, intensity, "back off"
4. **Score actions:** utility scores from the player data
5. **Apply habit weights:** boost actions that counter what players tend to do
6. **Quirk filter:** remove anything that breaks the monster's hard rules
7. **Pick an action:** softmax with temperature
8. **Cooldown / no-repeat:** lower the weights of recently used actions
9. **Execute:** NavMesh movement, animation, sound
10. **Log:** write the decision and event to telemetry

---

## 5. The Monster Toolkit

> See **Visio page 5: Monster Toolkit**.

**Every monster has every part.** What makes monsters different is the **settings**: on/off, how likely each option is, and which style it uses. This keeps them from feeling the same while keeping the code shared.

| Part | What it is | Settings |
|---|---|---|
| **Wander** | How it moves with no target | Style: patrol / roam / stay still / drift toward where players probably are • speed |
| **Follow** (stalk / prowl) | How it tails a player | Style: stalk at a distance / creep when unseen / **none** (straight to hunt) • chance |
| **Hide** | Whether and how it hides | Can hide: **yes / no** • type: hiding spots / blend in with props / darkness / ceiling • can ambush from hiding? |
| **Guard** (objectives) | Waiting where players *have* to go | On/off • chance (learned) • lurk around a corner vs. hide in a locker by it • how long it waits • hears pickups |
| **Scare** (toy) | Its favourite scares | List of scare types, each with its own chance • cooldown |
| **Hunt** | The real attack | **Hunt condition** (comes from its rule) • speed • what ends it (lose sight / player hides / timer) |
| **When spotted** | Its reaction to being seen | chase / flee / freeze / vanish / charge / ignore, then back to Follow |
| **Senses** | How it finds players | Sight range & cone • hearing sensitivity • special (light, silence, being watched) |
| **Rules** (the quirk) | Always true, never random | **Restricts** it (when it can't act) **and triggers** it (when it will hunt) |
| **Learning** | How much it adapts | Adaptability 0–100% • limits on every chance (section 8, *Learning limits*) |

### Rules both restrict AND trigger

The rule doesn't just limit a monster; it often decides **when it attacks**:

| Monster | Restricts | Triggers the hunt |
|---|---|---|
| Statue | Can't move while watched | Attacks when **nobody** is watching |
| Listener | Blind | Any loud noise |
| Hush | Can't approach noisy or grouped players | A player who is alone **and** silent |

### Scares are fake hunts

A **fake hunt** looks and sounds exactly like a real hunt (lights, sprinting, audio) but **can't hurt anyone** and **ends after 2–4 seconds**. That means it reuses the hunt code:

```csharp
void StartHunt(PlayerAIState target, bool fake)
{
    isHunting = true;
    canDamage = !fake;   // a fake hunt can't hurt anyone
    huntEndsAt = Time.time + (fake ? Random.Range(2f, 4f) : profile.huntDuration);
    // ...same chase, sounds and lights as a real hunt
}
```

Other scare types (appear & vanish, noise lure, lights, doors, hiding-spot jumpscare) are small one-off actions. Each monster has its own **chance** for each type.

### How it looks in Unity

**One shared brain script + one data file per monster.** You write the brain once. Each monster is a **`MonsterProfile`** asset (a *ScriptableObject*: a data file you fill in from Unity's Inspector, so your friends can tweak numbers without touching code), plus a small **rule script** for its quirk.

```csharp
[CreateAssetMenu(menuName = "Stalker/Monster Profile")]
public class MonsterProfile : ScriptableObject
{
    [Header("Wander / Follow")]
    public WanderStyle wanderStyle;      // Patrol, Roam, StayStill, DriftToPlayers
    public FollowStyle followStyle;      // StalkAtDistance, CreepWhenUnseen, None
    public float wanderSpeed = 2f;

    [Header("Hide")]
    public bool canHide;
    public HideType hideType;            // HidingSpot, BlendIn, Darkness, Ceiling
    public bool canAmbushFromHiding;

    [Header("Scares")]
    public List<BehaviourChance> scares; // fake hunt, appear & vanish, noise lure...
    public float scareCooldown = 60f;

    [Header("Hunt")]
    public float huntSpeed = 6f, huntDuration = 20f;

    [Header("Spotted")]
    public SpottedReaction whenSpotted;  // Chase, Flee, Freeze, Vanish, Charge, Ignore

    [Header("Senses")]
    public float sightRange = 20f, hearingThreshold = 0.5f;

    [Header("Learning")]
    [Range(0, 1)] public float adaptability = 0.5f;
}
```

`WanderStyle`, `HideType` and the others are simple `enum` lists you define once.

Each monster's quirk is a small script that answers two questions:

```csharp
public interface IMonsterRule
{
    bool CanMove(MonsterBrain m);                            // restricts
    bool WantsToHunt(MonsterBrain m, PlayerAIState target);  // triggers
}

public class StatueRule : MonoBehaviour, IMonsterRule
{
    public bool CanMove(MonsterBrain m) => !m.IsWatchedByAnyone();
    public bool WantsToHunt(MonsterBrain m, PlayerAIState target) =>
        !m.IsWatchedByAnyone() && m.DistanceTo(target) < m.lungeRange;
}
```

The brain checks the rule **every tick, before anything else** (step 4 on Visio page 6), so no amount of randomness or learning can break it.

---

## 6. Shared Systems

> See **Visio page 9: Shared Systems**.

**Build these once and every monster uses them.** Most of the work lives here, not in the individual monsters.

### 6.1 Noise system
Every sound becomes a **noise event**: where it happened and how loud it was. Footsteps, doors, gunshots, and **proximity voice chat** all go through it.

```csharp
public struct NoiseEvent { public Vector3 position; public float loudness; public GameObject source; public float time; }

public static class NoiseSystem
{
    public static event System.Action<NoiseEvent> OnNoise;

    public static void Emit(Vector3 pos, float loudness, GameObject source) =>
        OnNoise?.Invoke(new NoiseEvent { position = pos, loudness = loudness, source = source, time = Time.time });
}
```

- Monsters subscribe to `OnNoise` and decide whether they heard it (loudness falls off with distance).
- **Voice chat:** most voice chat tools can tell you when someone is speaking and roughly how loud. The host turns that into noise events. **Check this when choosing the voice tool.**
- **Silence** (for the Hush) = time since a player's last noise event.

### 6.2 Hiding spots
Lockers, beds, closets... Each spot holds **one occupant: a player or a monster**.

```csharp
public class HidingSpot : MonoBehaviour
{
    public GameObject occupant;            // null = empty
    public bool IsFree => occupant == null;

    // Returns the monster inside if this was an ambush, otherwise null
    public GameObject TryEnter(GameObject who)
    {
        if (IsFree) { occupant = who; return null; }
        return occupant.CompareTag("Monster") ? occupant : null;  // surprise!
    }
}
```

- Opening an occupied spot = **ambush** (if the occupant is a monster).
- Every entry and exit is logged, which feeds habit learning ("players love this locker").

### 6.3 "Am I being watched?"
For each player: is the monster inside their view, with nothing blocking? That's one angle check and one raycast.

```csharp
bool IsWatchedBy(PlayerAIState player, Transform monster, float fov = 90f, float maxDist = 30f)
{
    Vector3 eye = player.eyePosition;                          // camera position (add this field)
    Vector3 toMonster = (monster.position + Vector3.up * 1.5f) - eye;
    if (toMonster.magnitude > maxDist) return false;

    // 1) Inside their view cone?
    if (Vector3.Angle(player.lookDirection, toMonster) > fov * 0.5f) return false;

    // 2) Anything blocking the view?
    if (Physics.Raycast(eye, toMonster.normalized, out RaycastHit hit, maxDist))
        return hit.transform == monster || hit.transform.IsChildOf(monster);
    return false;
}
```

Used by: the Statue (its whole rule) and every stalker (to stay out of sight).

### 6.4 Director (hunt cap)
- Limits **how many monsters can hunt at once** (e.g. 1–2), so 6 monsters never pile onto the team.
- Decides **when scares are allowed** and gives players breathing room.
- "Hunts on sight" monsters (like the Brute) get **priority** when they ask to hunt.

**The Director is also our main balance tool.** With 4–6 monsters, the danger is constant chaos (players get overwhelmed and burn out) or long stretches of nothing (boring). The goal is **waves**: overwhelming for a while, then a breather.
- **Pacing cycle:** *build-up* (quiet, small signs, monsters drifting closer) → *peak* (hunts and scares allowed, maybe two monsters active at once) → *relax* (monsters back off; players regroup and loot). The Director moves between phases based on how stressed players are and how long it's been.
- **Threat budget:** each monster action "costs" points (a scare is cheap, a hunt is expensive). The Director refills the budget slowly and refills it faster during relax phases. Monsters ask before spending. This one number controls how intense the game feels, and it's easy to tune from playtest data.
- **Monsters should cover each other's gaps, not stack.** If one monster punishes hiding and another punishes running, players always have *some* safe option. If every monster covers everything, the game is unfair. This is why each monster's rules and toolkit switches matter more than any single number.

### 6.5 Light zones
Trigger boxes that mark areas as **lit or dark**. This is much simpler than measuring real light. Lights turning off just flips the zone. Used by the Shade (and the Brute's poor dark vision).

### 6.6 Vent network (idea, not built yet)
Monsters climb into a **vent**, travel through the vent network out of sight, and come out of a different vent elsewhere on the map.

**Why it's a good idea:**
- **Solves co-op hiding.** With 4 players watching in 4 directions, "get fully out of sight" can be nearly impossible. A vent is a guaranteed exit.
- **Repositioning.** A monster can leave and start stalking again from a new direction, which fits the "always around, keep your head on a swivel" feel.
- **Free scares.** Clanking inside the walls, a vent grate that's suddenly open, a monster dropping out of the ceiling.
- **Per-monster setting in the toolkit:** *uses vents yes/no* (the Brute is too big; the Lurker would love them).

**Rules to keep it fair** (otherwise it feels like teleporting):
- **Telegraph it:** vents make noise when a monster passes through, so attentive players can track it.
- **Travel takes time,** based on the distance between vents, not instant.
- **Climbing in or out takes a moment** and can be seen. Seeing where it went is useful information for players.
- **Limit how often it's used** (a cooldown) so it stays special.

**Simplest version (like Species: Unknown):** the vent network is **connected everywhere**. Only the entrances physically exist, so there are no ducts to build or path through. Travel time is just based on the distance between the two entrances.

**Item idea: monster tracker.** A radar-like item (found on the map or equipped) that shows monsters nearby, **including inside vents** ("it's in the vent above you"). It gives players counterplay against vent movement without everyone having it.

**How it would work in code:** a vent is basically a `HidingSpot` that's linked to other vents. The monster gets in (like a locker), waits for the travel time while vents along the way play sounds, then exits at a chosen vent: one near the players' rough area that nobody can currently see. Most of the locker code can be reused.

### 6.7 Objectives and guarding them (built in the prototype)
Players **have** to go to the objectives (gems in the prototype; later keys, parts, the armory). That makes objectives the best place for a monster to wait. A smart predator waits where its prey has to go, not only where the prey is right now.

**Prototype placeholder:** 5 glowing gems spread across the rooms furthest from the start (each new gem goes in the room furthest from the start and the gems already placed). Collect them all to win. Gem rooms always get lockers.

**What the monster does** (Wander state, when a dice roll on `guardGemChance` succeeds):
1. **Picks a gem:** usually the one nearest the players (where they'll probably go next), sometimes a random one. It skips gems the players are about to reach.
2. **Either** hides **in a locker by the gem** and bursts out the moment someone grabs it, **or** lurks **around a corner** from the gem: a spot a short *walk* away that the gem has no line of sight to, so players don't see it on the way in.
3. Waits 15–30 s, then gives up and wanders again.

**Picking up a gem makes a sound.** Monsters within 25 m hear it and come to look. The objective is a risk, not a free point.

**Learning:** a gem grabbed while the monster was far away → guard more. Waiting for nothing → guard a bit less. Caught someone by guarding → guard more.

**Fairness ideas for later:** a tell that a gem is guarded (a locker door slightly open, breathing, a cold light flicker), and a Director rule that at most one monster guards at a time.

---

## 7. Controlled Randomness

Goal: **surprising, but never unfair.** Randomise *what the monster chooses*, never *the rules players rely on*.

| Technique | Description |
|---|---|
| **Weighted random choice** | e.g. Stalk 50% / Toy 30% / Hunt 20% |
| **Softmax + temperature** | Pick randomly, favouring better scores. Low temp = focused, high temp = erratic. The director can adjust it. |
| **No-repeat / cooldowns** | Lower an action's weight after it's used, so the monster doesn't slam the same door twice |
| **Randomise the details** | Timing (wait 3–12 s), which watching spot, path, speed, sound choices |
| **Personality per match** | Roll each monster's weights at spawn: patient this match, aggressive the next |

**Never randomise:** the quirk rules. For example, "can't move while watched" holds **100%** of the time.

```csharp
// Softmax weighted pick over utility scores
string ChooseAction(Dictionary<string, float> scores, float temperature)
{
    var weights = new Dictionary<string, float>();
    float total = 0f;
    foreach (var kv in scores)
    {
        float w = Mathf.Exp(kv.Value / temperature);
        weights[kv.Key] = w;
        total += w;
    }

    float roll = Random.value * total;
    foreach (var kv in weights)
    {
        roll -= kv.Value;
        if (roll <= 0f) return kv.Key;
    }
    return weights.Keys.Last(); // fallback for rounding errors (needs: using System.Linq;)
}
```

---

## 8. Adaptive Behaviour (Learning Player Habits)

> See **Visio page 7: Learning**.

**Idea:** count what players do, turn the counts into probabilities, and feed those into the monsters' choice weights.

### Steps
1. **Track habits** by behaviour type (locker / bed / vent / run) **and** by location (heat map of spots and rooms).
2. **Turn counts into probabilities with a prior** (Dirichlet-multinomial), so one early event doesn't dominate:
   `P(option) = (count + prior) / (total + prior × number_of_options)`
3. **Decay** all counts (e.g. ×0.95 per minute) so players can change strategy and the monster re-adapts.
4. **Feed into behaviour** through the weights in the decision pipeline.

### Habit → adaptation examples

| Player habit | Monster adapts by... |
|---|---|
| Hides in lockers a lot | Checks lockers more when searching |
| Hides in lockers a lot | **Hides in lockers itself** to ambush players |
| Runs the same route | Waits along that route |
| Grabs objectives while it's far away | **Guards objectives more often** (built in the prototype: `guardGemChance`) |
| Sticks close to teammates | Goes after whoever strays |
| Uses vents | Starts moving through vents |
| Stares at the monster to freeze it | Approaches from behind / targets the player who isn't watching |

### Fairness rules
1. **Show players the monster is adapting:** let players hear it ripping open lockers in other rooms first.
2. **Cap it:** never above ~70–80% certainty on any spot.
3. **Decide the memory scope per monster:** per player vs. team; shared hive-mind vs. each monster on its own.
4. **Ramp up within one match:** the monster gets smarter as the night goes on. Random maps make memory across matches less useful.

```csharp
public class PlayerHabits
{
    Dictionary<string, float> counts = new()
    {
        {"Locker", 1f}, {"Bed", 1f}, {"Vent", 1f}, {"Run", 1f}   // priors
    };

    public void Record(string action) => counts[action] += 1f;

    public void Decay(float rate = 0.95f)
    {
        foreach (var key in counts.Keys.ToList())
            counts[key] = Mathf.Max(counts[key] * rate, 0.5f); // never fully forget
    }

    public float Probability(string action) => counts[action] / counts.Values.Sum();
}
```

### Learning limits

> See **Visio page 8: Learning Limits**.

**Problem:** if monsters learn from players, a monster's personality could drift until every monster acts the same. For example, a Brute that learned to hide would stop feeling like the Brute.

**Solution:** every chance has a **starting value**, a **min–max range**, and an optional **lock**. Learning nudges the chance, but only **inside its range**. The monster's **adaptability** sets how fast it moves.

| Example (the Lurker) | Start | Limits | Notes |
|---|---|---|---|
| Hide in hiding spots | 70% | 50–95% | Its core habit; can learn to do it even more |
| Ambush from a hiding spot | 40% | 25–70% | |
| Fake hunt (scare) | 20% | 10–35% | Stays rare so it keeps working |
| Chase far from hiding spots | 0% | **locked** | Breaks its rule, so learning can never turn it on |

```csharp
[System.Serializable]
public class BehaviourChance
{
    public string name;
    [Range(0, 1)] public float start = 0.3f;  // starting chance
    [Range(0, 1)] public float min = 0.1f;    // learning can't go below this
    [Range(0, 1)] public float max = 0.6f;    // ...or above this
    public bool locked;                       // true = never changes
    [HideInInspector] public float current;

    public void Reset() => current = start;   // call at the start of each match

    // amount comes from the habit tracker, e.g. +0.05 when players keep using lockers
    public void Nudge(float amount, float adaptability)
    {
        if (locked) return;
        current = Mathf.Clamp(current + amount * adaptability, min, max);
    }
}
```

These `current` values are the weights that feed the weighted random choice in section 7. Learning changes **what's likely**; the limits keep **who the monster is**.

**Data science angle:** after playtests, compare chances over time with how scared players were and how often they survived. That tells you whether a range is too wide (the monster stops feeling like itself) or too narrow (players figure it out too fast).

---

## 9. Player Data the AI Tracks

> See **Visio page 2: Player Data**.

All of this lives in **one component per player** (`PlayerAIState`), so monsters never reach into random scripts and logging is a single dump.

### 7.1 Body / condition
| Value | Why the AI cares |
|---|---|
| **Health** | Wounded = easier target, or the monster toys with them more |
| **Stamina** | Exhausted players can't run; the monster can close in or wait them out |
| **Sanity** _(if added)_ | Low sanity unlocks hallucinations and fake-outs only that player sees |
| **Injured / limping / bleeding** | Leaves trails or sounds to follow |

### 7.2 Equipment
| Value | Why the AI cares |
|---|---|
| **Has weapon (+ which)** | Hunt vs. avoid that player |
| **Ammo / weapon condition** | Smart monsters wait for empty clips and reloads |
| **Flashlight on/off, battery** | Light reveals players; some monsters may hate or love light |
| **Key items carried** | Target whoever carries what the team needs |

### 7.3 Perception (what the player sees)
| Value | Why the AI cares |
|---|---|
| **Look direction / view cone** | Core to stalking: is the player looking at the monster? |
| **Which monsters are visible to them** | And for how long they've been looked at |
| **Last time they saw any monster** | Long time = relaxed player = time for a scare |
| **Light level where they stand** | Dark corners = player can't see well |

### 7.4 Movement & noise
| Value | Why the AI cares |
|---|---|
| **Position and current room** | The basis for everything else |
| **Movement state** (sprint / walk / crouch / still) | Sprinting is loud; crouching is quiet |
| **Noise level** | Main input to monster hearing |
| **Is hiding (+ where)** | Feeds habit tracking |
| **Velocity / heading** | Predicting where they're going |

### 7.5 Social (co-op)
| Value | Why the AI cares |
|---|---|
| **Distance to nearest teammate / players nearby** | Isolated players = prime targets |
| **Alone time** | How long since they were near anyone |
| **Talking on voice chat** _(if proximity chat)_ | Monsters can hear players talk |
| **Reviving / helping a teammate** | A vulnerable moment |

### 7.6 Progress / game state
| Value | Why the AI cares |
|---|---|
| **Distance to armory / armory found** | The director raises pressure near it |
| **Time in match** | Difficulty curve |
| **Deaths / downs so far** | Ease off if the team is losing badly; push if they're cruising |

### 7.7 Derived values (calculated)
- **Stress / fear estimate:** recent scares, health, alone time, jittery movement
- **Threat level to monsters:** armed + grouped + healthy = dangerous
- **Vulnerability score:** alone + low stamina + unarmed + dark = prime target
- **Habit profile:** from the habit tracker

### 7.8 State vs. events

| Type | Examples | Used for |
|---|---|---|
| **State** (check anytime) | health, position, stamina | Live AI decisions, periodic snapshots |
| **Events** (happen once) | gunshot, door opened, scream, scare triggered, entered hiding spot | Monster reactions + telemetry log |

### 7.9 Live AI vs. logging

| Purpose | What | How often |
|---|---|---|
| **Live AI** | Position, look direction, noise, hiding | Every AI tick |
| **Logging for analysis** | Everything | Snapshot every 0.5–1 s + every event |

```csharp
public class PlayerAIState : MonoBehaviour
{
    // Body
    public float health, stamina, sanity;
    public bool isInjured;

    // Equipment
    public bool hasWeapon;
    public int ammo;
    public bool flashlightOn;

    // Perception
    public Vector3 lookDirection;
    public float lastSawMonsterTime;

    // Movement and noise
    public MoveState moveState;          // Sprint, Walk, Crouch, Still
    public float noiseLevel;
    public HidingSpot currentHidingSpot; // null if not hiding

    // Social
    public float distanceToNearestTeammate;
    public float aloneTime;
    public bool isTalking;

    // Derived
    public float stressEstimate;
    public float vulnerability;
}
```

---

## 10. Where Data Science / ML Fits

| # | Feature | Technique | Priority |
|---|---|---|---|
| 1 | **Belief state:** where a lost player probably is | Particle filter | ⭐ High (best fit) |
| 2 | **Habit adaptation** | Dirichlet-multinomial counts + decay | ⭐ High (quick to build) |
| 3 | **Stress / fear estimation** | Heuristic first → logistic regression / gradient boosting on playtest data + self-reported fear | Medium |
| 4 | **Movement prediction** | Markov model over the room graph | Medium |
| 5 | **Playstyle clustering** | Clustering on logged matches (hiders / runners / groupers) | Later |
| 6 | **Director tuning** | Bayesian optimisation / bandits on playtest metrics | Later |
| 7 | **Narrow RL skill** | Unity ML-Agents (e.g. chase movement only) | Stretch goal |

**Key setup:** log telemetry to CSV/JSON **from day one**. That's what links the game to Python and notebooks.

Validation ideas:
- Compare survival rate and fear ratings with adaptation **on vs. off**.
- If one action causes 80% of deaths, the weights need tuning.

---

## 11. Multiplayer Notes

- **The AI runs only on the host/server.** Clients just receive monster positions, animations, and sounds.
- Clients must send the host what it can't already know: **look direction**, **voice activity**, possibly **sanity effects**.
- Scares only one player sees: the host sends a targeted message to that client only.
- **Pick the networking library early** (Netcode for GameObjects, Mirror, Fish-Net, or Photon); it affects how the AI code is written.

---

## 12. Monster Draft Pool

> See **Visio page 12: Monster Pool** and **pages 13–18** (one page per monster).
> **Rough draft.** Names are working titles. Change anything.

Each match picks **4–6** monsters from this pool. They're numbered in **suggested build order, easiest first**.

### At a glance

| # | Monster | Rule (restricts it) | Hunts when... | Hides? | Follow style | When spotted | Difficulty |
|---|---|---|---|---|---|---|---|
| 1 | **The Brute** | Too big for small gaps, vents, hiding spots | It **sees** any player | No (too big) | None: straight to hunt | Charges | Easy |
| 2 | **The Statue** | Can't move while **any** player sees it | **Nobody** is watching + target in lunge range | Yes: blends in with props | Creeps closer when unseen | Freezes | Easy–Medium |
| 3 | **The Lurker** | Moves between hiding spots only when unseen; leashed to them | A player opens/enters its spot, or walks past alone | Yes: hiding spots (its whole thing) | Moves into spots near the target | Slips into nearest spot | Medium |
| 4 | **The Listener** | Completely blind | Any noise above its threshold, **including voice chat** | No | Drifts toward small sounds | Doesn't care (blind) | Medium |
| 5 | **The Hush** | Only approaches silent, isolated players; noise pushes it back | Target alone **and** silent for N seconds | Yes: dark corners | Stalks the quietest player | Backs away, watching | Medium |
| 6 | **The Shade** | Can't enter lit areas; flashlight pushes it back | Target is in the dark | Yes: any darkness | Follows from the edge of the dark | Backs out of the light | Medium |

### Each monster's gameplay loop

Every monster should ask players to do **one clear thing**. That loop is what makes it feel different, and it's the first thing to write down for a new monster.

| Monster | The player's loop |
|---|---|
| **The Stalker** | **Keep an eye on it.** It teleports around you, always just out of sight. If you don't keep spotting it, it gets closer and closer (the ring) until it attacks. Spotting it pushes it back a little, but it creeps closer over the match. Glance, don't stare (stare meter). Don't follow it around a corner. (Prototype: built, v11.) |
| **The Statue** | **Keep your eyes on it, move, check, move.** Teammates watch each other's backs. |
| **The Brute** | **Break line of sight and use tight spaces.** |
| **The Lurker** | **Check before you hide.** Look for the tell. |
| **The Listener** | **Stay quiet,** including on voice chat. |
| **The Hush** | **Stay together and keep talking.** |
| **The Shade** | **Stay in the light,** manage batteries. |

**The Stalker gets scarier the more monsters there are.** It does the most damage when players' attention is on something else.

### More monster ideas
- **The Searcher:** methodically checks hiding spots, so hiding is never fully safe while it's around.
- **The Mimic Locker:** disguises itself as a hiding spot. It must have **visual tells** (slightly wrong colour or size, breathing, a door that's slightly open) so careful players can spot it, even though panicking players won't.

**Stretch idea (after launch):** **The Mimic** records voices from proximity chat and plays them back to lure players. **Hard:** audio capture, networking, and player privacy all get involved.

**First playable goal:** Brute + Statue + Lurker working together in one match.

### Design notes per monster

**1. The Brute:** huge, heavy, relentless; you always hear it coming.
- *Wander:* slow, loud patrol. *Scares:* distant roar, door smashed nearby, fake charge that stops short.
- *Learns:* common escape routes (adaptability: **low**).
- *Survive:* break line of sight; use small gaps and vents it can't follow.
- *Build:* NavMesh + sight check. Giving it a **bigger NavMesh agent** automatically stops it from fitting through narrow gaps. **Build this first: it teaches you the basics.**

**2. The Statue:** only moves when nobody is looking. The rule both **stops** it and **triggers** its attack.
- *Hide:* blends in by standing still among statues, mannequins, or props (needs props on the map).
- *Scares:* suddenly somewhere new when you turn around; lights flicker and it's closer.
- *Learns:* which players look away or split from the group most (adaptability: **medium**).
- *Survive:* keep eyes on it; teammates cover each other's backs.
- *Build:* the "am I being watched?" check (section 6.3).

**3. The Lurker:** lives inside hiding spots, so the place you run to for safety might already be taken.
- *Tell:* an occupied spot always shows a small sign (breathing, door ajar), so it's fair.
- *Scares:* knocks from inside a locker; grabs a hiding player, then lets go (fake hunt).
- *Learns:* players' favourite hiding spots, then moves into them (adaptability: **high**).
- *Build:* the hiding spot system (section 6.2), which players need anyway.

**4. The Listener:** blind, hears everything. Proximity chat makes it terrifying.
- *Scares:* makes fake noises to panic players into making real ones.
- *Learns:* where noise usually happens, then waits nearby (adaptability: **medium**).
- *Survive:* stay quiet, stop talking, crouch; throw objects to distract it (if we add throwables).
- *Build:* noise system (section 6.1) + voice volume from the voice chat tool.

**5. The Hush:** the Listener's opposite. It hunts the quiet, lonely player.
- **Twin option:** always spawns *with* the Listener, so there's no safe volume. That's a designed dilemma.
- *Scares:* whispers or breathing only the target can hear.
- *Build:* reuses the noise system (silence = time since a player's last noise).

**6. The Shade:** lives in the dark; light holds it back; batteries run out.
- *Scares:* lights flicker or go out; a shape at the edge of your flashlight beam.
- *Learns:* who uses their flashlight least (adaptability: **medium**).
- *Build:* light zones (section 6.5): trigger boxes that mark areas as lit or dark.

---

## 13. Build Order / Roadmap (AI)

Plan for **about 3 months**, written for someone **new to Unity and game programming**. If it slips, **cut the Shade first, then the Hush**.

| Weeks | Goal | You'll learn |
|---|---|---|
| **1–2** | Unity + C# basics. A capsule "monster" that wanders and follows you around a grey-box test map | Unity editor, C# scripts, NavMesh |
| **3–4** | `MonsterBrain` + `MonsterProfile` with Wander / Follow / Hunt / Spotted. **The Brute** fully playable | State machines, ScriptableObjects |
| **5** | "Am I being watched?" check → **The Statue** | Raycasts and vector math (your linear algebra helps here) |
| **6–7** | Hiding spot system → **The Lurker** | Triggers, interactions |
| **8–9** | Noise system (+ voice volume once proximity chat exists) → **The Listener** + **The Hush** | Events, audio |
| **10** | Scares / fake hunts + Director v1 (hunt cap) | Timers / coroutines |
| **11** | Telemetry logging + habit learning with limits | Data: your home turf |
| **12** | Playtest and tune. **The Shade** if time allows | — |

**Networking:** the AI only runs on the host (section 11). Agree on the networking library with your friend **early**, and write AI code so that it simply doesn't run on clients (e.g. `if (!IsServer) return;` in Netcode for GameObjects). It's much easier than retrofitting later.

**Later (after the core works):** particle filter for smarter wandering, stress model from playtest data, Markov route prediction, ML-Agents experiment (stretch).

---

## 14. Monster Design Template

> See **Visio page 10: Designing a Monster**, **page 11: Roster Checklist**, and **pages 13–18** (one page per monster, same fields as this template).
> Copy this block for each new monster.

```markdown
### Monster: <Name>

**Concept / fantasy:** What is it? What does it feel like to be hunted by it?

**The player's loop (one sentence):** What do players have to keep doing to survive it? e.g. "Spot it, let it leave, don't follow it."

**Rules (ALWAYS true):**
- Restricts it: when it CAN'T act
- Triggers it: when it WILL hunt

**Toolkit settings:**
| Wander | Follow | Hide (yes/no + type) | Scares (+ chances) | Hunts when... | When spotted |
|---|---|---|---|---|---|
|  |  |  |  |  |  |

**Senses:** sight / hearing / special

**Player data it reads:** (from section 9) e.g. lookDirection, noiseLevel, aloneTime

**What it learns + adaptability:** e.g. favourite hiding spots → hides in them itself. Adaptability: low / medium / high

**Learning limits:** which chances are locked, and the min–max for the rest

**After the armory:** Flee / Ambush / Defend / Enrage / ...

**How to survive it / how to kill it:**

**Speed / size / sound:**

**How hard to build + which shared systems it needs:**
```

### Roster balance checklist
- [ ] Mix of senses (not all sight-based)
- [ ] Mix of speeds (slow and relentless vs. fast and fragile)
- [ ] Each monster has a **different** counterplay
- [ ] Counterplays don't directly contradict each other (or do so on purpose, as a designed dilemma, like the Listener + Hush)
- [ ] At least one monster punishes splitting up, and one punishes grouping
- [ ] Each quirk is learnable within 1–2 encounters
- [ ] Not every monster stalks first: some hunt on sight

---

## 15. Open Questions

- [ ] **Monsters:** keep, change, or replace the 6 drafts in section 12?
- [ ] **Twins:** do the Listener and Hush always spawn together?
- [ ] **After the armory:** do monsters flee, ambush, enrage, or become vulnerable only under their quirk's rules?
- [ ] **Hunt cap:** how many monsters may hunt at once (1? 2?)
- [ ] **Map generation:** connected rooms/corridors vs. open areas? (Room graphs make the Markov model and particle filter much easier.)
- [ ] **Map props:** statues/mannequins for the Statue to blend in with? Lit vs. dark areas for the Shade?
- [ ] **Throwables** (bottles, cans) to distract noise-based monsters?
- [ ] **Player count** per match
- [ ] **Sanity system:** in or out?
- [x] **Proximity voice chat:** yes. Still to decide: which voice chat tool? It must tell us **when someone is speaking and how loud**.
- [ ] **Networking library**
- [x] **Timeline:** roughly 2–3 months for the core
- [ ] **Monster memory:** per monster, or shared hive-mind?
- [ ] **Death / after-death gameplay:** out of scope for the AI for now

---

## 16. Glossary

| Term | Meaning |
|---|---|
| **Authored system** | AI whose rules are hand-designed rather than learned |
| **Behaviour tree** | Tree of tasks evaluated top-down to choose behaviour |
| **Utility AI** | Scores every action and picks from the best |
| **Director** | Global AI that controls pacing and coordinates monsters |
| **Hunt cap** | The Director's limit on how many monsters can hunt at once |
| **Fake hunt** | A hunt that can't hurt anyone and ends after a few seconds: a scare that feels real |
| **Monster toolkit** | The parts every monster has (wander, follow, hide, scare, hunt, spotted, senses, rules, learning) |
| **MonsterProfile** | A Unity data asset (ScriptableObject) holding one monster's toolkit settings |
| **ScriptableObject** | A Unity file that stores data you edit in the Inspector, no code needed |
| **Noise event** | A record of a sound: where it happened and how loud it was |
| **Raycast** | Shooting an invisible line to check whether anything blocks it |
| **Adaptability** | How fast a monster's chances move when it learns |
| **Learning limits** | The min–max range each chance is allowed to move within |
| **Softmax / temperature** | Turns scores into probabilities; temperature controls randomness |
| **Prior** | Starting assumption before any data (pretend counts) |
| **Decay** | Gradually shrinking old data so recent behaviour matters more |
| **Particle filter** | Tracks a probability spread over where a hidden player might be |
| **Markov model** | Predicts the next room or action from the current one |
| **Telemetry** | Gameplay data logged for analysis |
| **NavMesh** | Unity's walkable-surface map used for pathfinding |
| **Host-authoritative** | Only the host runs the AI; clients mirror the results |
