# Stalker Prototype: Start Here

A tiny test project for the **modular monster**: a random map of big and small rooms joined by tight and wide corridors, lockers to hide in, a first-person player (a blue capsule), and a stalking monster (a red capsule with glowing eyes). The goal isn't to look good. It's for **messing around**: does stalking feel creepy? Does the learning work? What breaks?

> **Want to understand the code?** Read [CODE_WALKTHROUGH.md](CODE_WALKTHROUGH.md): a plain-English tour of the monster brain.

**Contents**
1. [Open the project](#1-open-the-project)
2. [Play it](#2-play-it)
3. [Experiments to try](#3-experiments-to-try)
4. [A tour of the Unity editor](#4-a-tour-of-the-unity-editor)
5. [How Unity code works (the 10 ideas you need)](#5-how-unity-code-works-the-10-ideas-you-need)
6. [C# for someone who knows Java / C++ / Python](#6-c-for-someone-who-knows-java--c--python)
7. [What's in this project](#7-whats-in-this-project)
8. [How the modular monster works](#8-how-the-modular-monster-works)
9. [How to add a new monster](#9-how-to-add-a-new-monster)
10. [Looking at your play data in Python](#10-looking-at-your-play-data-in-python)
11. [When things go wrong](#11-when-things-go-wrong)
12. [Game dev glossary](#12-game-dev-glossary)
13. [Where to learn more](#13-where-to-learn-more)

---

## 1. Open the project

1. Open **Unity Hub** and **sign in**. You need a free **Personal** licence active (Hub → *Manage licences*). Without one, the editor won't open projects.
2. In Hub: **Add → Add project from disk** → pick the `StalkerPrototype` folder.
3. Open it with **Unity 6000.6.8f1** (the version you have installed).
4. **The first open is slow** (a few minutes). Unity is importing packages and building its `Library` cache.
5. If you see a warning that the **new Input System Package is installed but not configured**, it's harmless (the controls work either way). To switch to the new system and remove the warning: **Edit → Project Settings → Player → Other Settings → Configuration → Active Input Handling → "Input System Package (New)"**, then let Unity restart.
6. When it's done, the editor **builds the test maze automatically** and opens `Assets/Scenes/TestMaze`. If you don't see a maze, double-click `Assets/Scenes/TestMaze` in the Project window, or use the menu **Stalker → Rebuild Test Maze**.

> ⚠️ **OneDrive warning:** this project is inside OneDrive. Unity's `Library` folder is big and changes constantly, and OneDrive syncing it can cause weird "file locked" errors. If that happens, move the project somewhere outside OneDrive (like `C:\Unity\StalkerPrototype`), or pause OneDrive while working. To share with friends, use **Git** (a `.gitignore` is already included) rather than OneDrive.

---

## 2. Play it

Press the **▶ Play** button at the top of the editor. Click inside the Game view to capture the mouse.

| Key | Action |
|---|---|
| **WASD** | Move |
| **Mouse** | Look |
| **Shift** | Sprint (loud, uses stamina: about 5 s on a full bar) |
| **Ctrl** or **C** | Crouch (quiet) |
| **Space** | Jump |
| **E** | Hide in / get out of a locker (look at it first) |
| **F1** | Show/hide the debug panel |
| **R** | Play again (after you win) |
| **Esc** | Free the mouse (to click around the editor) |

Press **▶** again to stop.

**The goal:** collect all **5 glowing gems** (counter in the top right). Walk into a gem to pick it up. Get them all and you win. Gems are spread out in the rooms furthest from where you start. The monster **knows where the gems are**. Sometimes it waits by one (see experiment 11), and picking up a gem **makes a sound** it can hear from 25 m away.

**The debug panel (top-left)** shows what the monster is thinking:
- **State:** Wander / Stalk / Spotted (+ which reaction) / Hunt ("FAKE" = fake hunt) / Search (checking lockers) / Hide (in a locker!)
- **HIDDEN FROM YOU:** the hiding rule. The monster only counts as hidden when you can't see **any** part of it (feet, middle, head; left, centre, right). Spotted reactions, getting into lockers and lurking all wait for this.
- **it sees you / you see it / can move**
- **RING:** the Stalker circles you at this distance, out of sight. It **shrinks** while nobody spots it and **grows** each time you spot it. At 4 m it's right behind you and attacks. Keep looking around!
- **STARE METER:** fills while you look at it (twice as fast if you walk toward it). Full = it attacks.
- **Gem guarding:** "(heading to guard a gem)", "(lurking by a gem)", "(going to hide by a gem)", "(inside a locker BY A GEM!)"
- **LEARNED values:** boldness (how close it dares to stalk), fake-hunt chance, hide-and-ambush chance, check-lockers chance, guard-gems chance. Watch them change as you play, always staying inside their limits.

---

## 3. Experiments to try

1. **Find it.** Walk around until it notices you (debug panel switches to **Stalk**). Sprinting is loud, so it hears you from further away.
2. **Never look back.** Keep walking without turning around. It creeps up **behind** you, gets **bolder** every 5 seconds it isn't seen, and eventually **hunts**.
3. **Keep checking behind you.** When you catch it, it picks a reaction by chance: usually it **stares back, then slips out of sight** and lurks there for a few seconds; sometimes it **freezes** and stares until you look away; rarely it runs; *very* rarely it charges. Each time, its **boldness drops**, so it stalks from further back. That's learning with limits in action.
4. **Hide.** Look at a locker and press **E**. If it saw you get in, it comes straight to that locker. If it lost you nearby, it **searches** lockers, checking the ones you've used most **first** (it learns your habits).
5. **Careful opening lockers.** The Stalker sometimes hides in a locker near you to **ambush**. Walk past and it bursts out; open its locker and... yeah.
6. **Wait for a fake hunt.** Sometimes it charges at you... then vanishes before touching you.
7. **Try the Statue.** Stop Play. In the **Hierarchy**, click `Monster - The Statue`, and tick the **checkbox next to its name** at the top of the Inspector. Turn off the Stalker the same way if you want it alone. Press Play: it only moves when you're not looking.
8. **Tweak numbers live.** In the Project window, open `Assets/Monsters/Stalker` and change values (speeds, sight angle, adaptability) **while playing**. Changes to these profile files **are kept** after you stop. (Changes to objects in the scene during Play are **not** kept. See section 4.)
9. **New map.** Menu **Stalker → Rebuild Test Maze (new random layout)**.
10. **See its senses.** Stop Play, select a monster in the Hierarchy, and look at the **Scene** view: yellow = sight cone, blue = hearing range. Green/red circles in front of lockers = free/occupied.
11. **Gem guarding.** Keep the debug panel open (F1) and wait for "guarding a gem". Then go get that gem. It's either **lurking around a corner** from the gem (out of sight of it, so you won't see it as you walk up), or **hiding in a locker beside it**. If it's in a locker, the moment you grab the gem the door bursts open. Every gem you grab while it's far away makes it **guard gems more often** (learning), and every time it waits for nothing it guards a bit less. To test it quickly, set **Guard Gem Chance → Start** to 0.7 in `Assets/Monsters/Stalker`.

Write down what feels good and what doesn't. That's the real output of this prototype.

---

## 4. A tour of the Unity editor

| Window | What it's for |
|---|---|
| **Scene** view | The 3D world you edit. **Hold right-click + WASD** to fly around; **F** focuses on the selected object; **Alt + left-drag** orbits. |
| **Game** view | What the player's camera sees when you press Play. |
| **Hierarchy** | List of every object in the current scene. Objects can be nested (children move with their parent). |
| **Inspector** | Shows the **components** of whatever you selected, and lets you edit their values. |
| **Project** | Your files: scripts, materials, scenes, monster profiles (everything under `Assets/`). |
| **Console** | Messages, warnings and **errors** from your code. Check it whenever something's weird. |

**Toolbar shortcuts (Scene view):** **W** move, **E** rotate, **R** scale, **Ctrl+D** duplicate, **Ctrl+Z** undo, **Ctrl+S** save scene.

> ⚠️ **The #1 beginner trap:** **anything you change in the scene while in Play mode is thrown away when you stop.** Edit the scene while stopped. Tip: *Edit → Preferences → Colors → Playmode tint*. Set it to red so you always know when you're playing.

---

## 5. How Unity code works (the 10 ideas you need)

1. **Scene:** a level. Ours is `TestMaze`.
2. **GameObject:** *anything* in a scene: the player, a wall, a light, an empty "folder" object. On its own it does nothing.
3. **Component:** a piece of behaviour attached to a GameObject. A wall = GameObject + `MeshRenderer` (draws it) + `BoxCollider` (makes it solid). **Your scripts are components too.** Unity is built on *composition*: you build things by stacking components, not by deep inheritance.
4. **Transform:** every GameObject has one: **position, rotation, scale**. `transform.position` is where it is.
5. **MonoBehaviour:** the base class for your scripts. Unity calls special methods on it for you:

   | Method | When Unity calls it |
   |---|---|
   | `Awake()` | Once, when the object is created (set up references here) |
   | `OnEnable()` / `OnDisable()` | When the object/component is switched on/off |
   | `Start()` | Once, just before its first `Update` (after every `Awake` has run) |
   | `Update()` | **Every frame** (~60+ times per second). Most game logic lives here |
   | `OnGUI()` | For quick debug text on screen |
   | `OnDrawGizmos...()` | To draw debug lines in the Scene view |

6. **`Time.deltaTime`:** seconds since the last frame. Multiply movement by it so speed is the same on fast and slow PCs: `position += velocity * Time.deltaTime`.
7. **Public fields show up in the Inspector.** `public float stalkSpeed = 3f;` becomes an editable box. `[Header]`, `[Tooltip]` and `[Range(0,1)]` make it prettier. **The Inspector value overrides the value in code**, so if changing the code number does nothing, check the Inspector!
8. **`GetComponent<T>()`:** how one component finds another on the same object, e.g. `GetComponent<NavMeshAgent>()`.
9. **ScriptableObject:** a data file (an *asset*) you edit in the Inspector. Our `MonsterProfile` is one. It's perfect for "same code, different settings."
10. **Physics queries:** colliders make things solid; **raycasts/linecasts** shoot an invisible line and tell you what it hits first. That's how "can I see you?" works.

Also useful:
- **Prefab:** a saved, reusable GameObject (like a class you can instantiate). Later each monster will be a prefab.
- **NavMesh:** a precomputed map of *walkable* floor. A **NavMeshAgent** component uses it to walk around walls. You just call `agent.SetDestination(point)`.
- **Coordinates:** **Y is up**. X/Z are the floor. Units are **metres**.
- **Rotations** are `Quaternion`s. Never edit their x/y/z/w directly; use `Quaternion.Euler(0, 90, 0)` or `Quaternion.LookRotation(direction)`.

---

## 6. C# for someone who knows Java / C++ / Python

C# reads almost exactly like **Java**. The differences you'll actually hit:

| C# | What it means |
|---|---|
| `public float Speed { get; private set; }` | A **property**: looks like a field, anyone can read, only this class can set |
| `public bool IsAlive => health > 0;` | `=>` is a one-line method/property ("expression body") |
| `var x = 5;` | Type inferred (still statically typed) |
| `$"Distance {d:0.0} m"` | String interpolation, like Python f-strings |
| `foreach (var p in list)` | Like Python `for p in list` |
| `List<T>`, `Dictionary<K,V>` | Like Java `ArrayList` / `HashMap` |
| `enum MonsterState { Wander, Stalk }` | Same as Java/C++ enums |
| `x switch { A => 1, _ => 0 }` | Switch *expression* (returns a value) |
| `using UnityEngine;` | Like Java `import` / C++ `#include` (no header files!) |
| `virtual` / `override` | Like C++: base class method that subclasses can replace (see `MonsterRule` → `StatueRule`) |

**Gotchas:**
- **`Vector3` is a struct (a value, copied), not a reference.** So `transform.position.x = 5;` **doesn't compile**. Do `var p = transform.position; p.x = 5; transform.position = p;`
- **A script's file name must match its class name** (`MonsterBrain.cs` → `class MonsterBrain`), or Unity can't attach it.
- **Compile errors block Play.** If ▶ does nothing, look for red errors in the Console.
- **`NullReferenceException`** almost always means an Inspector field was left empty (e.g. no profile assigned).

**Editor for code:** double-clicking a script opens your code editor. For VS Code, install the **Unity** extension and set *Edit → Preferences → External Tools → External Script Editor* to VS Code.

---

## 7. What's in this project

```
StalkerPrototype/
├── START_HERE.md                 ← you are here
├── Assets/
│   ├── Scenes/TestMaze.unity     ← the level (auto-generated)
│   ├── Monsters/                 ← MONSTER PROFILES (data files) - tweak these!
│   │   ├── Stalker.asset
│   │   └── Statue.asset
│   ├── Scripts/
│   │   ├── Monsters/
│   │   │   ├── MonsterBrain.cs   ← THE modular monster (states, senses, learning)
│   │   │   ├── MonsterProfile.cs ← the toolkit settings + BehaviourChance (learning limits)
│   │   │   ├── MonsterAnimator.cs ← picks the model's animation from what the brain is doing
│   │   │   ├── MonsterRule.cs    ← default rule (base class for quirks)
│   │   │   └── StatueRule.cs     ← example quirk: can't move while watched
│   │   ├── Player/
│   │   │   ├── FirstPersonController.cs ← WASD + mouse + noise level
│   │   │   ├── PlayerAIState.cs  ← what monsters know about the player + "am I watched?"
│   │   │   ├── PlayerHiding.cs   ← press E to hide in a locker
│   │   │   └── PlayerHUD.cs      ← on-screen text + debug panel
│   │   ├── Objective/
│   │   │   ├── Gem.cs            ← a gem: spins, gets picked up when you walk into it
│   │   │   └── GameObjective.cs  ← counts gems, announces pickups (monsters listen), win + restart
│   │   └── Core/
│   │       ├── InputHelper.cs    ← keyboard/mouse reading (don't need to touch)
│   │       ├── HidingSpot.cs     ← a locker: holds ONE player or monster
│   │       ├── NavMeshBootstrap.cs ← builds the NavMesh when the game starts
│   │       └── TelemetryLogger.cs  ← writes CSV play data
│   ├── Editor/StalkerSceneBuilder.cs ← builds the maze scene (editor-only)
│   └── Materials/
├── Telemetry/                    ← CSV files appear here after you play
└── Tools/analyze_telemetry.py    ← Python analysis starter
```

**Files to read first:** `MonsterProfile.cs` → `MonsterRule.cs` → `StatueRule.cs` → `MonsterBrain.cs`.

**Where is the monster code?** In Unity's **Project** window (bottom of the screen, not the Hierarchy): **Assets → Scripts → Monsters**. Double-click a script to open it in your code editor. On disk it's `StalkerPrototype/Assets/Scripts/Monsters/`.
- **Change what a monster DOES** → `MonsterBrain.cs` (each state is its own `Update...()` method)
- **Change a monster's NUMBERS** → click `Assets/Monsters/Stalker` and edit it in the Inspector (no code)
- **Change its QUIRK** → `MonsterRule.cs` / `StatueRule.cs`

---

## 8. How the modular monster works

**One brain, many monsters.** Every monster runs the same `MonsterBrain`. Two things make each one different:

| | What it is | Where |
|---|---|---|
| **Profile** | All the toolkit settings: wander/follow style, speeds, senses, spotted reaction, chances | A `MonsterProfile` asset in `Assets/Monsters/` |
| **Rule** | Its quirk: `CanMove()` (restricts) and `WantsToHunt()` (triggers) | A script that inherits `MonsterRule` |

**Each frame, the brain:**
1. **Picks a target** (closest player)
2. **Senses:** can I see them (range + view cone + line of sight)? can I hear them (noise × hearing range)? is any player watching me?
3. **Asks the rule** whether it can move this frame. The rule always wins.
4. **Runs its current state:**

```
WANDER (drifts toward your rough area) ──sees you──► STALK
STALK ──you look at it──► SPOTTED: picks a reaction by chance
                            (stare then hide / freeze / flee / vanish / chase) ──► back to STALK
STALK ──rule says hunt──► HUNT
STALK ──dice roll──► FAKE HUNT (vanishes before touching you)
STALK ──dice roll──► HIDE (gets into a locker near you, ambushes when you walk past)
lost you ──► SEARCH (checks lockers near where it lost you, most-used first) ──► WANDER
saw you get in a locker ──► SEARCH (that locker first)
```

**Learning (with limits):** the Stalker's **boldness** goes **down** when you spot it, **up** when it stays close unseen, and **up** when it catches you, but **never leaves its min–max limits**. Bolder = stalks closer = more likely to hunt. All of this is in `UpdateStalk()` in `MonsterBrain.cs`, marked `LEARNING`.

**Not built yet** (next steps from the design doc): the Director (hunt limits), noise events from doors and voice chat, more monsters, the particle filter (to replace the drift "cheat").

---

## 9. How to add a new monster

**Settings only (no code):**
1. Project window → `Assets/Monsters` → **right-click → Create → Stalker → Monster Profile**. Name it (e.g. `Brute`).
2. Select it and set its values in the Inspector (e.g. Follow Style = **None** → it hunts on sight).
3. In the Hierarchy, select `Monster - The Stalker` → **Ctrl+D** to duplicate → rename it.
4. In its **Monster Brain** component, drag your new profile into the **Profile** slot.
5. Move it somewhere in the maze (W tool), make sure it's switched on, and press Play.

**With a new rule (a bit of code):**
1. Project window → `Assets/Scripts/Monsters` → **right-click → Create → MonoBehaviour Script** (in some versions: *Create → Scripting → MonoBehaviour Script*). Name it e.g. `ListenerRule`.
2. Replace its contents:

```csharp
using UnityEngine;

public class ListenerRule : MonsterRule
{
    [Range(0f, 1f)] public float noiseThreshold = 0.5f;

    // Hunts the moment it hears a loud enough noise.
    public override bool WantsToHunt(MonsterBrain m) =>
        m.Target != null && m.Target.noiseLevel >= noiseThreshold && m.DistanceToTarget <= m.profile.hearingRange;
}
```

3. On your monster: **remove** the old rule component (⋮ menu → Remove Component) and **Add Component → ListenerRule**.

(For a truly blind Listener you'd also set its profile's **Sight Range** to 0.)

---

## 10. Looking at your play data in Python

Every time you press Play, `TelemetryLogger` writes two CSV files to `StalkerPrototype/Telemetry/`:
- `session_*.csv`: a snapshot 4× per second: positions, states, who sees whom, boldness, noise...
- `events_*.csv`: one row per monster state change

```bash
cd StalkerPrototype
pip install pandas matplotlib
python Tools/analyze_telemetry.py
```

It prints how long the monster spent in each state and plots distance over time, learned boldness over time, and a heat map of where you walked. This is your playground for the data science side.

---

## 11. When things go wrong

| Problem | Likely cause / fix |
|---|---|
| ▶ Play does nothing | Red **compile errors** in the Console. Fix them first. |
| `NullReferenceException` | An Inspector slot is empty (e.g. Monster Brain → Profile). |
| "The associated script can not be loaded" | File name ≠ class name, or a compile error. |
| Monster doesn't move at all | It's switched off (checkbox), has no profile, or it's the Statue and you're looking at it. 🙂 |
| "Failed to create agent because it is not close enough to the NavMesh" | The monster is placed inside a wall or off the floor. Move it onto the floor. |
| My changes disappeared | You made them during Play mode (see section 4). |
| Mouse is stuck | Press **Esc**. |
| Maze looks broken / want a fresh one | **Stalker → Rebuild Test Maze** |
| Weird "file in use" errors | OneDrive syncing (see section 1). |
| Lots of red errors after scripts changed **during Play** | Unity reloaded code mid-game. Stop Play and press Play again. To prevent it: *Edit → Preferences → General → Script Changes While Playing → **Recompile After Finished Playing***. |
| The map didn't change after an update | Menu **Stalker → Rebuild Test Maze (same layout)**. |
| The Creep model walks sideways / backwards | In `Assets/Editor/StalkerSceneBuilder.cs` set `ModelYaw` to 90, 180 or -90, then rebuild. |
| Creep feet slide or moonwalk | Select the monster → **Monster Animator** component → change **Walk Anim Speed** (feet slide forward = raise it). |
| Want a different animation for something | Same component: type another name from `Assets/Monsters/CreepAnimator` (double-click it to see all 19). |
| Too dark / not dark enough | **Window → Rendering → Lighting → Environment**: Ambient Color and Fog. Lasts until the next rebuild; for good, change the numbers in the builder's "Atmosphere" section. |

---

## 12. Game dev glossary

| Term | Meaning |
|---|---|
| **Game loop** | The endless cycle the engine runs: read input → update everything → draw the frame → repeat |
| **Frame / FPS** | One update+draw of the game; frames per second |
| **Delta time** | Seconds since the last frame (`Time.deltaTime`) |
| **Asset** | Any file the game uses: models, sounds, scripts, materials, profiles |
| **Scene** | A level or screen, made of GameObjects |
| **GameObject / Component** | A "thing" in the scene / a piece of behaviour attached to it |
| **Prefab** | A saved, reusable GameObject template |
| **Transform** | Position, rotation and scale |
| **Collider** | An invisible shape that makes something solid / detectable |
| **Trigger** | A collider you can walk through that fires an event (e.g. "entered the room") |
| **Rigidbody** | Makes an object obey physics (gravity, forces). Our player uses a CharacterController instead |
| **CharacterController** | A capsule for moving characters that slides along walls without full physics |
| **Raycast / Linecast** | Shoot an invisible ray/line and see what it hits first |
| **Layer / LayerMask** | Groups of objects, used to make raycasts ignore certain things |
| **Tag** | A label on a GameObject (`Player`, `MainCamera`) |
| **NavMesh** | Precomputed walkable area for AI pathfinding |
| **NavMeshAgent** | Component that walks a character along the NavMesh |
| **Pathfinding** | Finding a route around obstacles (A* under the hood) |
| **Line of sight (LOS)** | Nothing blocks the view between two points |
| **FOV (field of view)** | How wide a camera or monster can see, in degrees |
| **State machine (FSM)** | AI that is always in exactly one state (Wander, Stalk...) with rules for switching |
| **Behaviour tree / Utility AI** | Other common ways to structure AI decisions (see the design doc) |
| **Aggro** | When an enemy notices you and becomes hostile |
| **Spawn / respawn** | Appear in the world / appear again after dying |
| **Hitbox** | The collider used to decide whether an attack hit |
| **Grey-boxing** | Building a level from plain boxes to test gameplay before real art (this project!) |
| **Placeholder** | Temporary art or sound (our capsules) |
| **Playtest** | Having people play to see what works |
| **Telemetry** | Gameplay data recorded for analysis |
| **Gizmo** | Debug drawing visible only in the Scene view |
| **Material / Shader** | How a surface looks / the program that draws it |
| **Render pipeline** | Unity's rendering setup: Built-in, URP (Universal), HDRP (High Definition) |
| **Baked vs realtime** | Precomputed ahead of time (lighting, NavMesh) vs computed while playing |
| **Editor script** | Code that runs only in the Unity editor (tools), not in the game |
| **Play mode** | Running the game inside the editor |
| **ScriptableObject** | A data asset you edit in the Inspector |
| **Coroutine** | A method that can pause and resume over several frames (`yield return`) |
| **Event** | A "something happened" signal other code can subscribe to (`MonsterBrain.StateChanged`) |
| **Host / client / server** | In multiplayer: who runs the "real" game (host/server) vs who mirrors it (clients) |
| **Authoritative** | The machine whose version of the game is the truth (our AI runs on the host) |
| **Tick rate** | How many times per second the network or AI updates |
| **Netcode** | The code that keeps multiplayer games in sync |
| **Proximity chat** | Voice chat where volume depends on how close players are |
| **Jump scare** | A sudden fright; our **fake hunt** is a "you thought you were dead" scare |
| **Fog** | Hides distant things; cheap atmosphere and helps performance |

---

## 13. Where to learn more

- **Unity Learn** (learn.unity.com): the free *Unity Essentials* and *Junior Programmer* pathways are the standard starting point.
- **Unity Manual** and **Scripting API** (docs.unity3d.com): look up any class, e.g. "NavMeshAgent".
- **Our design doc:** `../Monster_AI_Design.md` and the Visio diagrams explain *why* the code is shaped this way.
- **Ask Claude:** paste an error from the Console, or describe what you want a monster to do.
