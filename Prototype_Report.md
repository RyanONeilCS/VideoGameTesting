# Stalker Prototype Report: Every System, Every Setting, Every Issue

**What this is:** a complete breakdown of the monster AI testbed (`StalkerPrototype/`) as of **8 Nov 2026**. It covers what each system does, what every setting means and how to tune it, every issue found in playtesting and its status, and what's still open.

**How to use it:** read Part 1 for the big picture. Use Part 3 as a reference while tweaking numbers in Unity (click `Assets/Monsters/Stalker` and every setting listed here appears in the Inspector, in the same order). Part 4 is the playtest log.

> **Status:** current version is **v21** (Trickster peeks from lockers; the Brute; darker map + film grain; Trickster AI as v18: the trickster: teleport mix, no long retreats, fair final approach). v10 was play-tested and liked; v11 compiles but is **not play-tested yet**. Some details in Parts 2–3 describe v8/v9 and get updated in the review.

### Decisions so far (8 Nov)
- **The Stalker is now its own defined monster:** *the teleporting stalker*. We stopped treating it as "the modular monster that does everything". Modularity comes later, once each monster works well on its own.
- **The Stalker's rule:** **keep an eye on it.** It teleports around you, just out of sight. If you don't keep spotting it, it gets closer and closer until it attacks. Spotting it pushes it back a little, but over a match it keeps creeping in. Glance, don't stare. Don't follow it around a corner.
- **Personality (lore idea):** a **trickster / imp**. It wants to get close to you and take you. It loves catching you off guard, toying with you and baiting you, and making sure you know it's there. It **hates being caught off guard**: catch it in the open and it freezes and stares. Stare back too long, or walk up to it, and it attacks. It baits you down hallways; approach carelessly and it charges.
- **Random attacks** are kept tiny (1%). Players need to trust the rule; the group decides later whether to keep any randomness.
- **Build each monster solo first**, then combine and balance in the real project. For now the Stalker should show up *more* rather than less: it's easier to tone a monster down later than to add presence.
- **Plan, in order:**
  1. Get the stalking working really well.
  2. **Vents / hidden passages** as a comparison to teleporting, possibly for a different monster.
  3. Hiding (lockers, maybe on a different monster).
  4. Each remaining part, per monster.
  5. Then turn it into a modular base.

---

## Contents
1. [The big picture](#1-the-big-picture)
2. [Systems explained](#2-systems-explained)
3. [Every setting](#3-every-setting)
4. [Issue log (playtest feedback)](#4-issue-log-playtest-feedback)
5. [Known risks and open problems](#5-known-risks-and-open-problems)
6. [Idea backlog](#6-idea-backlog)
7. [Quick tuning guide](#7-quick-tuning-guide)
8. [Version history and review agenda](#8-version-history-and-review-agenda)
9. [The Trickster: full design](#9-the-trickster-full-design-the-max-version-were-building-toward)

---

## 1. The big picture

### The goal for this monster
> **Maximise suspense:** you know it's there, but you don't know when it will attack.

The Stalker should be seen regularly but only in **glimpses**. It toys with the player instead of chasing them. **The player hides, not the monster.** When it does attack, the attack should feel **fair**: you got a warning and you had a chance to escape.

### The player's loop against the Stalker
1. You catch a glimpse of it: a shoulder around a corner, or a figure at the end of a hall.
2. You look at it. It **slips away around a corner and teleports** somewhere else near you, usually behind you. Sometimes it waits right there instead, so don't follow too closely.
3. You keep exploring, **checking around you** now and then. Every time you spot it, it backs off a little, but each sighting is a bit closer than the last.
4. If you stop checking, it creeps closer and closer (**the ring shrinks**) until it's right behind you. It **roars** and attacks.
5. You **sprint** out of sight (with limited stamina), **sidestep the lunge**, or **hide in a locker**, which is safe-ish because it learns if you rely on lockers too much.

### How the code is organised (one sentence each)
| Piece | What it is |
|---|---|
| **MonsterBrain** | The single shared "modular monster". All behaviour lives here, as a state machine. |
| **MonsterProfile** | A data file of settings per monster type (Stalker, Statue...). Change numbers without code. |
| **MonsterRule** | The monster's quirk: *when can it move* and *when does it attack*. The Statue overrides this. |
| **PlayerAIState** | Everything monsters can know about a player, including "can this player see this spot?" |
| **MonsterAnimator** | Watches the brain and picks animations. Currently unused (red pill). |
| **Editor builder** | Generates the test map (rooms, corridors, lockers, gems, lamps) and the monster files. |

### The states (what the monster is "doing")
| State | Meaning | Usually leads to |
|---|---|---|
| **Wander** | No target. Roaming or drifting toward players. Short breather after losing you. | Stalk (sees you, or tracking kicks in) |
| **Stalk** | Circling you on the ring, out of sight. Peeking, lurking, waiting at corners, scaring. | Spotted, Hunt, Hide |
| **Spotted** | A player saw it. Picks a reaction (usually: stare briefly, then dash out of sight). | Stalk |
| **Hunt** | The real attack (or a fake one). Roar → chase → lunge. | Stalk (lost you), Wander (caught you) |
| **Search** | Lost you, so it checks a few hiding spots near where you vanished. | Wander, Stalk, Hunt |
| **Hide** | Walking to a locker and hiding in it to ambush you, or guarding a gem. | Hunt (ambush), Stalk / Wander |

---

## 2. Systems explained

### 2.1 The ring (core of stalking) ✨ *new, your idea*
Each player has their own **ring**: the distance the monster keeps from them.
- **Starts** at 24 m, roughly how far you can see in the dark.
- **Shrinks** while the monster is in position, nobody can see it, and it isn't peeking. The base rate is 0.35 m/s, scaled by boldness: ×0.6 when careful, ×1.6 when bold.
- **Grows** by 7 m every time someone spots it.
- **Attack:** at 4 m it's right behind you. It roars and hunts.
- **After a hunt** the ring resets to 20 m. **After catching you** it resets to 24 m.
- **Where it stands:** every 0.5 s it tests 16 points around you at ring distance and scores each one. A spot that any player can see gets a huge penalty. Spots in front of you get a penalty (behind you is best). It also adds the distance off the ring and half the walking distance, and picks the lowest score.
- **Co-op:** it targets the player with the **smallest ring**, meaning whoever isn't watching their back. Players inside lockers are ignored.

**How long until it attacks if you never look back?**

| Boldness | Shrink speed | 24 m → 4 m takes |
|---|---|---|
| 0.2 (careful) | 0.28 m/s | ~71 s |
| 0.5 (start) | 0.39 m/s | ~52 s |
| 0.9 (bold) | 0.53 m/s | ~38 s |

Each spot pushes it back 7 m, which buys you about 18 s at default boldness.

### 2.2 Tracking (it always knows roughly where you are) ✨ *new*
- Every 2 s it updates a **rough guess** of your position, off by up to 5 m (less when the ring is small).
- **Paused while you're in a locker:** then it truly doesn't know where you are.
- **Paused during a hunt:** then it only uses sight and hearing, so breaking line of sight works.
- **Why:** before this, if you didn't walk toward its spawn you might never see it.

### 2.3 Senses
- **Sight:** range 25 m, a 140° cone, and a line of sight from its eyes (1.85 m up) to yours.
- **Hearing:** range = 18 m × your noise. Sprint (1.0) = 18 m, walk (0.4) = 7.2 m, crouch (0.1) = 1.8 m, still = 0. It's a **rough guess**: off by up to 25% of the distance, updated twice a second.
- **Memory:** after 8 s with no sight, sound or tracking, it has "lost you". It then searches lockers (by chance) or wanders.
- **Is it being watched?** It checks 9 points on its body: feet, middle and head × left, centre and right. Each point must be on the player's screen with nothing blocking it. Even a shoulder counts as seen.
- **The hiding rule:** it only counts as hidden after **no** player has seen **any** part of it for 0.6 s.

### 2.4 Peeking ✨ *new*
- If nobody has seen it for **20 s**, it finds a spot **in front of you** (within ±45° of where you're looking, inside the ring) where you'd see only **1 to 4 of its 9 body points**. That's an edge sticking out past a corner.
- It walks there and **waits** up to 10 s, facing you.
- When you notice it, the normal spotted reaction kicks in. Being spotted while peeking doesn't make it more careful, because it *wanted* to be seen.
- **Later:** this is where a "head and hands around the corner" pose plays. The code already finds the right spots.

### 2.5 Spotted reactions
When you look at it for **0.6 s**, it's spotted. The ring grows 7 m and it picks a reaction by weighted chance:

| Reaction | Chance | What it does |
|---|---|---|
| **Stare then hide** | ~79% | Stares back 0.2–0.8 s, then **dashes** (9 m/s, snappy turns) to the out-of-sight spot it can reach with the **shortest walk** (measured along real paths, not straight lines). |
| **Freeze** | ~10% | Stands dead still. Look away and it resumes stalking. Walk toward it (within 5 m, or 3 m closer) and it **snaps**: 40% attack, otherwise it flees. |
| **Flee** | ~8% | Runs away (slightly faster). |
| **Chase** | ~2% | Charges immediately (a real hunt). |
| **Vanish** | 0% | Teleports somewhere unseen (off by default). |

**Once fully out of sight**, it picks an outcome:

| Outcome | Chance | What you experience |
|---|---|---|
| **Gone** | 30% | It relocates somewhere unseen. You go to look... nothing. |
| **Waiting around the corner** ✨ | 35% | It stays right there for up to 8 s. **Come within 4 m and it attacks.** Spot it from further away and it slips off again. |
| **Lurk** | 35% | It holds still 2–5 s, then goes back to circling. |

### 2.6 Stare meter (watched too long)
- Fills while any player watches it, twice as fast if they walk toward it. Empties at 1/s when unseen.
- **Full at 5 s → it attacks.** The rule: *glance, don't stare.*
- Works in the Stalk, Spotted and Hide states. Off for the Statue.

### 2.7 Hunting and the attack ✨ *reworked*
1. **Roar** (0.6 s). It stops and faces you. That's your warning.
2. **Chase** at **5.2 m/s**: faster than your walk (3.5), slower than your sprint (6).
3. **Lunge:** within 3.5 m (and it can see you) it **stops for 0.35 s**, then lunges at 10 m/s for 0.35 s toward where you *were*, plus a bit past. You're caught if it gets within 1.3 m during the lunge. **Sidestep and it misses.**
4. **Missed:** it's slow (30% speed) for 0.8 s.
5. **Lost you:** if it can't **see** you for **2.5 s**, the hunt ends. Then:
   1. It walks to where it last saw or heard you and **looks around for 4 s**.
   2. Maybe (30%, learned) it checks a couple of lockers nearby.
   3. Otherwise it **wanders for 12 s**: your breather.
   4. Then it slowly drifts your way, and tracking picks you up again with the ring reset to 20 m.

   Tracking is paused for this whole search, so it only has its eyes and ears. If the hunt simply runs out (12 s) while it can still see you, it backs off to the ring instead.
6. **Caught:** you teleport to the start (placeholder for death), it goes back to its spawn, and the ring resets.
- **Fake hunts:** they look exactly like a hunt but **vanish** when close or after 2–4 s. Chance: 6% per scare check (every 6 s), with a 40 s cooldown, so about one every 1.5–2 min.
- **Random real hunts:** **off** (0%). With the ring on, the ring alone decides.

### 2.8 Lockers: players hiding
- Look at a locker within 2.5 m and press **E** to get in or out. Inside, you can look but not move, you make no noise, and the screen is **blacked out except a thin slit**.
- **It only "saw you hide"** if it saw you a split second (0.25 s) before you got in **and** it can see the locker door. Then it **bangs on the door** for 2.5–4.5 s:
  - **Burst out** during banging: it's startled for 0.8 s, then hunts (with a head start for you).
  - **Stay in:** 60% it opens the door (you're caught), 40% it gives up and won't search lockers again for 20 s.
- **If it didn't see you**, after 8 s without contact it **may search** (30% chance, learned). It checks the **2 spots** within 12 m of its rough guess of where you vanished, sorted by distance minus habit plus a random guess (0–8 m), so it isn't always the obvious one.
- **Habit learning:** every time players use a spot, that spot counts as 3 m closer when it decides what to search.

### 2.9 Lockers: the monster hiding (rare)
- Every scare check there's a ~6.4% chance (8% × the boldness reduction) that it **hides in a locker to ambush you**, with a 40 s cooldown.
- **Which locker?** One 5–22 m from you that no player can see. The score is its walk + your distance. **+12 m** penalty if you've already explored there, **+8 m** if it's behind you, **−6 m** if it's next to a gem.
- **Inside:** it bursts out if you walk within 3 m. It leaves after 15 s, or early if you go more than 28 m away. **If you grab a gem within 10 m of its locker, it bursts out and hunts.**
- **If you open its locker:** you're caught.

### 2.10 Gems (the placeholder objective)
- **5 gems**, spread across the rooms furthest from your start. Walk into one to pick it up. Collect all to win, then press **R** to play again.
- **Picking one up makes a sound:** monsters within 25 m come to look. Monsters within **10 m**, or one guarding *that* gem, **hunt immediately**.
- **Gem guarding** (Wander state only, so it's rare now that it tracks you): there's a 10% chance per new wander point (learned, max 25%). It only picks a gem within 30 m of you that you're not already standing next to. It then either hides in a locker by the gem (50%) or lurks around a corner from it (a spot about 6 m away by walking route with no line of sight to the gem), for 15–30 s.

### 2.11 Learning (all within limits)
Every "learned" chance has a **start, min and max** and can never leave that range. Each lesson moves it by *amount × adaptability* (0.6). The **Learning Enabled** switch turns all of it off.

| Learned value | Goes up when... | Goes down when... |
|---|---|---|
| **Boldness** (ring shrink speed) | It stays close and unseen for 5 s (+0.1); it catches you (+0.2) | You spot it while it's stalking (−0.3) |
| **Hide & ambush chance** | An ambush from a locker works (+0.1); you open its locker (+0.2) | It waited in a locker for nothing (−0.1) |
| **Check hiding spots chance** | It finds you in a hiding spot (+0.15) | A search finds nothing (−0.05) |
| **Open after banging** | It gave up banging but you were inside (+0.1) | — |
| **Guard gems chance** | You grab a gem while it's far away (+0.1); guarding pays off (+0.1) | It guarded for nothing (−0.05) |
| **Freeze → attack chance** | ⚠️ *Nothing changes this yet* | — |

### 2.12 Exploration map ✨ *new*
- Tracks where players have walked on a grid of 6 m squares. A spot counts as "explored" if any player has been in that square or one next to it.
- **Used for:** monster ambush lockers should be where you **haven't** been, since you're exploring to find objectives and probably won't come back.

### 2.13 Player side
| Feature | Details |
|---|---|
| **Movement** | Walk 3.5 m/s, sprint 6, crouch 1.6. Jump 1 m. |
| **Stamina** ✨ | 5 s of sprint. Refills at 0.8 s per second (a full refill takes ~6 s). Running dry locks sprint until 1.5 s worth has refilled. Bar at the bottom centre (only shown when not full). |
| **Noise** | Still 0, crouch 0.1, walk 0.4, sprint 1.0. |
| **Caught** | Teleported to the start and counted (placeholder; there is no death yet). |

### 2.14 The test map
- 104 × 104 m, ~14 rooms of three sizes (6–10 m, 10–16 m, 16–26 m), connected by corridors **4, 6 or 8 m wide**, plus 6 extra loops.
- **Walls are 9 m tall**, so you feel small. The monster is **3 m tall** (you're 1.8 m). Lamps hang high up.
- **Lockers** along room walls (never beside doorways), 0–3 per room, and at least 2 in gem rooms.
- **Darker lighting:** moonlight 0.15, very low ambient light, fog from 4 m to 32 m. About 30% of rooms have a dim lamp. Your flashlight (22 m range) and the gem glows do most of the lighting.
- Menu **Stalker → Rebuild Test Maze** (same layout or a new random one).

### 2.15 Debug panel (F1) and telemetry
- **The F1 panel shows:**
  - state and reaction (FAKE / ROAR! / LUNGING! / peeking / waiting around the corner / lurking by a gem...)
  - distance
  - whether it's hidden from you, and for how long
  - whether it sees you, and whether it can move
  - the **RING**
  - the **STARE METER**
  - all the learned values
- **Telemetry:**
  - `StalkerPrototype/Telemetry/session_*.csv`: a snapshot every 0.25 s
  - `events_*.csv`: every state change and gem pickup
  - analyse them with `Tools/analyze_telemetry.py`

---

## 3. Every setting

**How to read the tables:** *Default* is the Stalker's value. "Range" settings show (min, max), and a random value in between is picked each time. **LEARNED** settings have *start / min / max* (see 2.11).

> ⚠️ When the builder's `ProfileVersion` goes up, Unity resets the Stalker/Statue files to these defaults **once**, which wipes your tweaks. Write down values you like.

### 3.1 Wander
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Display Name | The Stalker | Name in the debug panel. | |
| Wander Style | DriftTowardPlayers | **Drift:** usually heads toward the players' rough area. **Roam:** random. **StayStill:** doesn't move. | |
| Wander Speed | 2.2 m/s | Speed while wandering. | |
| Wander Radius | 15 m | How far random wander points can be. | |
| Wander Pause | 1–3 s | Pause at each wander point. | |
| Drift Chance | 0.7 | Chance each new point is near the players instead of random. | Higher = finds you faster. |
| Drift Fuzziness | 15 m | How rough "near the players" is. | |

### 3.2 Follow / stalk
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Follow Style | StalkFromBehind | **StalkFromBehind:** circle and hide (uses the ring). **CreepWhenUnseen:** walk straight at you (Statue). **None:** skip stalking and hunt on sight. | |
| Stalk Speed | 3.2 m/s | Speed while stalking near its spot. | |
| Min / Max Stalk Distance | 4 / 12 m | **Only used when the ring is OFF:** how close it stalks when fully bold / careful. | |
| Boldness | **LEARNED** 0.5 / 0.2 / 0.9 | Ring on: how fast the ring shrinks. Ring off: how close it follows. | |

### 3.3 The ring ✨
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Use Ring | ON | Turns the whole ring system on (see 2.1). | Turn off to compare with the old "follow behind" stalking. |
| Ring Start Radius | 24 m | Starting distance. | Match it to how far players can see. |
| Ring Attack Radius | 4 m | At this size it attacks. | |
| Ring Shrink Per Second | 0.35 m | **The main suspense dial.** | Too slow = boring, too fast = constant attacks. |
| Ring Push Back When Spotted | 7 m | Reward for spotting it. | Bigger = looking around is more powerful. |
| Ring After Hunt | 20 m | Where the ring restarts after a hunt. | |
| Catch Up Speed | 4.5 m/s | Speed when more than 8 m from its spot. | Must beat your walk (3.5) or it can't keep up. |

### 3.4 Tracking ✨
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Always Tracks Players | ON | Knows roughly where you are unless you're hiding (see 2.2). | |
| Tracking Update Interval | 2 s | How old its guess can be. | Longer = easier to shake it. |
| Tracking Fuzz | 5 m | How wrong its guess can be. | |

### 3.4b After losing you ✨
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Investigate Seconds | 4 s | Looks around where it lost you. | |
| Breather After Losing You | 12 s | Wanders this long before tracking you again. Also the delay at the start of a match. | **The "lull" dial.** Longer = more quiet time between scares. |

### 3.5 Hide and hiding spots
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Can Hide | ON | Can it hide in lockers itself? | |
| Hide Ambush Chance | **LEARNED** 0.08 / 0.03 / 0.2 | Chance per scare check to go hide and ambush. | Keep low: players hide, not it. |
| Hide Max Seconds | 15 s | Longest it stays in a locker. | |
| Hide Cooldown | 40 s | Minimum time between hides. | |
| Leave Hiding If Players Farther Than | 28 m | Gets out if you went another way. | |
| Explored Spot Penalty | 12 m | Avoids lockers where you've already been. | |
| Behind Player Penalty | 8 m | Prefers lockers ahead of you. | |
| Ambush Distance | 3 m | Bursts out when you're this close. | |
| Check Hiding Spots Chance | **LEARNED** 0.3 / 0.15 / 0.7 | Chance it searches lockers after losing you. | The main "are lockers safe?" dial. |
| Search Radius | 12 m | Searches lockers within this distance of its guess. | |
| Max Spots To Check | 2 | How many lockers per search. | |
| Search Guess Randomness | 8 m | Randomness in which lockers it picks. | Bigger = less likely to guess right. |
| Hiding Habit Weight | 3 m | Popular lockers count as this much closer per use. | |
| Bang On Door When Saw Hide | ON | Warns you by banging instead of opening instantly. | |
| Bang Seconds | 2.5–4.5 s | How long it bangs. | |
| Open After Banging Chance | **LEARNED** 0.6 / 0.3 / 0.9 | After banging: open, or give up. | |
| Search Cooldown After Giving Up | 20 s | No locker searches for this long after giving up. | |

### 3.6 Guard the gems
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Guard Gems | ON | Can it wait by gems? | |
| Guard Gem Chance | **LEARNED** 0.1 / 0.05 / 0.25 | Chance per wander point to go guard a gem. | |
| Guard Only Gems Within | 30 m | Only gems near you. | |
| Locker By Gem Bonus | 6 m | Ambush lockers next to gems count as this much closer. | |
| Guard From Locker Chance | 0.5 | Guard from a locker vs. lurk around a corner. | |
| Guard Radius | 10 m | Lockers / lurk spots within this distance of the gem. **Also:** grabbing a gem within this distance of it starts a hunt. | |
| Guard Seconds | 15–30 s | How long it guards. | |
| Gem Pickup Hearing Range | 25 m | Hears gem pickups from this far. | |

### 3.7 Scare
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Fake Hunt Chance | **LEARNED** 0.06 / 0.02 / 0.15 | Chance per scare check of a fake hunt. | |
| Scare Check Interval | 6 s | How often it rolls the dice for fake hunts and locker hides. | |
| Scare Cooldown | 40 s | Minimum time between fake hunts. | |
| Fake Hunt Duration | 2–4 s | How long a fake hunt lasts. | |
| Real Hunt Chance | 0 | Random real hunts at boldness 0. | **Leave at 0 with the ring.** |
| Boldness Hunt Bonus | 0 | Extra random-hunt chance at full boldness. | Leave at 0 with the ring. |
| Boldness Hide Reduction | 0.4 | Bolder = hides in lockers less. | |

### 3.8 Peeking ✨
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Peek When Unseen | ON | Peeks if nobody has seen it for a while. | |
| Peek After Unseen | 20 s | How long unseen before it peeks. | Lower = you see it more often. |
| Peek Max Visible Points | 4 (of 9) | How much of it can show. | 1–2 = only an edge; 4 = shoulder and head. |
| Peek Max Seconds | 10 s | Gives up if you don't notice. | |

### 3.9 Hunt and attack
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Hunt Speed | 5.2 m/s | Chase speed. | **Keep between 3.5 (walk) and 6 (sprint).** |
| Hunt Windup Seconds | 0.6 s | The roar before every hunt. | 0 = instant (unfair). |
| Hunt Duration | 12 s | Maximum hunt length. | |
| Hunt Cooldown | 20 s | Minimum time between hunts. | |
| Hunt Lose Sight Seconds | 2.5 s | Out of sight this long = the hunt ends. | **The "can I escape?" dial.** |
| Hunt Trigger Distance | 6 m | Ring OFF only: hunt when this close. | |
| Min Stalk Seconds Before Hunt | 10 s | Ring OFF only. | |
| Attack Range | 3.5 m | Starts the lunge this close. | |
| Attack Windup Seconds | 0.35 s | The pause before the lunge (your cue to dodge). | |
| Lunge Speed | 10 m/s | Lunge speed. | |
| Lunge Seconds | 0.35 s | Lunge length (about 3.5 m). | |
| Catch Distance | 1.3 m | Caught if it gets this close during the lunge. | |
| Attack Recover Seconds | 0.8 s | Slow after a miss. | |

### 3.10 When spotted
| Setting | Default | What it means | Tuning tip |
|---|---|---|---|
| Spotted Reactions | 0.75 / 0.1 / 0.08 / 0 / 0.02 | Weights for stare-then-hide / freeze / flee / vanish / chase. They don't need to add up to 1. | |
| Seconds Watched Before Spotted | 0.6 s | How long you must look before it counts. | |
| Stare Seconds | 0.2–0.8 s | Its stare back before dashing away. | Short = scarier. |
| Escape Speed | 9 m/s | Dash to cover. | |
| Escape Acceleration | 60 | Snappy turns while escaping. | |
| Max Cover Path Length | 25 m | Only considers cover within this walking distance. | |
| Lurk After Hiding Seconds | 2–5 s | The "lurk" outcome. | |
| Relocate After Hiding Chance | 0.3 | The "gone" outcome. | |
| Wait Around Corner Chance | 0.35 | The "waiting around the corner" outcome. | |
| Corner Ambush Distance | 4 m | Attacks if you follow this close. | |
| Corner Wait Seconds | 8 s | How long it waits at the corner. | |
| Attack When Watched Too Long | ON | The stare meter. | |
| Watched Too Long Seconds | 5 s | Stare meter size. | |
| Follow Pressure Multiplier | 2× | The meter fills faster while you walk toward it. | |
| Pressure Decay Per Second | 1 | How fast the meter empties. | |
| Freeze Break Distance | 5 m | Freeze snaps this close. 0 = never. | |
| Freeze Break Approach | 3 m | ...or if you walk this much closer. | |
| Freeze Break Attack Chance | **LEARNED** 0.4 / 0.2 / 0.7 | Attack vs. flee when freeze snaps. | |
| Max Freeze Seconds | 12–20 s | Safety net so a freeze never lasts forever. | |

### 3.11 Hiding rule, senses, learning
| Setting | Default | What it means |
|---|---|---|
| Unseen Seconds To Count As Hidden | 0.6 s | "Fully hidden" = no part of it seen for this long. |
| Sight Range | 25 m | |
| Sight Angle | 140° | Its view cone. |
| Hearing Range | 18 m | × your noise level. |
| Memory Seconds | 8 s | No contact this long = it lost you. |
| Lose Interest Distance | 45 m | Further than this = it lost you. |
| Learning Enabled | ON | OFF = all chances stay at their start values. |
| Adaptability | 0.6 | How big each lesson is (0–1). |

### 3.12 The Statue's differences
Everything else uses the defaults above.

| Setting | Statue | Why |
|---|---|---|
| Rule | StatueRule | Can't move while **anyone** sees it. Lunges when unseen within 8 m. |
| Follow Style | CreepWhenUnseen | Walks straight at you. |
| Use Ring / Tracking / Peeking | OFF | Its rule is its whole loop. |
| Spotted reactions | Freeze 100% | |
| Stare meter | OFF | Being watched is good for you with this one. |
| Can Hide | OFF | |
| Speeds | Wander 3, stalk 4.5, hunt 9 | Fast whenever you look away. |
| Sight | 360°, 30 m | |
| Seconds Watched Before Spotted | 0.05 s | Freezes instantly. |
| Adaptability | 0.3 | Learns slowly. |
| Is it in the scene? | **Switched off** | Tick its checkbox in the Hierarchy to try it. |

### 3.13 Other components
| Where | Setting | Default | What it means |
|---|---|---|---|
| **Player → First Person Controller** | Walk / Sprint / Crouch Speed | 3.5 / 6 / 1.6 m/s | |
| | Max Sprint Seconds | 5 s | The stamina bar. |
| | Stamina Regen Per Second | 0.8 | |
| | Min Stamina To Sprint | 1.5 s | Lock-out after running dry. |
| | Look Sensitivity | 2 | Mouse. |
| | Stand / Crouch Height, Jump, Gravity | 1.8 / 1.1 m, 1 m, −20 | |
| | Walk / Sprint / Crouch Noise | 0.4 / 1.0 / 0.1 | How loud each is. |
| **Player → Player AI State** | Max View Distance | 60 m | Players can't "see" monsters further than this. |
| **Player → Player Hiding** | Reach | 2.5 m | How close you must be to use a locker. |
| **Player → Player HUD** | Show Debug | ON | F1 toggles it. |
| **Monster → Monster Brain** | Eye Height | 1.85 m | Where it sees from. |
| **Monster → Monster Animator** | Animation names, walk/fast anim speed, cross-fade, freeze when rule stops it | idle / walk / attack | Only used with a model (currently off). |
| **Gem** | Pickup Radius / Spin / Bob | 1.3 m / 90°/s / 0.15 m | |
| **Objective** | Gems To Win | 0 (= all) | |
| **Telemetry** | Interval | 0.25 s | |
| **Builder** (code constants in `StalkerSceneBuilder.cs`) | GridW/H, Tile, TargetRooms, GemCount, DefaultSeed | 46, 2 m, 14, 5, 12345 | Map generation. |
| | UseCreepModel | **false** | true = Creep model instead of the red pill. **The Creep asset was deleted from the project (2026-11-09)**; re-import it from the Asset Store before switching this on. |
| | ModelYaw, MonsterHeight | 0°, 2.2 m | Model fitting. |
| | Lighting (in "Atmosphere") | moon 0.15, ambient 0.07, fog 4–32 m | Darkness. |

---

## 4. Issue log (playtest feedback)

✅ = fixed in code (the newest fixes aren't play-tested yet) · 🟡 = partly addressed · 🔴 = open

### Setup and tools
| # | Issue | Cause | What we did | Status |
|---|---|---|---|---|
| 1 | Input System warning on open | Project set to the old input system only | Switched the setting; code supports both systems | ✅ |
| 2 | "Corrupted Library" prompt | First open / OneDrive sync | Let Unity rebuild the Library | ✅ |
| 3 | Couldn't find lockers or the code | The scene wasn't rebuilt; the guide was unclear | Rebuild prompt + menu; "Where is the monster code?" section | ✅ |
| 4 | Red error spam (HUD) | Live code reload left text styles empty | Recreate missing styles | ✅ |
| 5 | Rebuilding during Play threw an error | Scenes can't be rebuilt while playing | The rebuild stops Play mode first | ✅ |
| 6 | New code didn't appear (no Creep, old behaviour) | **OneDrive hides file changes from Unity** | Press **Ctrl+R** after updates; or turn off *Preferences → Asset Pipeline → Directory Monitoring* | 🟡 workaround |

### Monster behaviour
| # | Issue | Cause | What we did | Status |
|---|---|---|---|---|
| 7 | Ran away the instant you looked | No pause before reacting | Must be watched 0.6 s; stares back first | ✅ |
| 8 | Never checked lockers | No search behaviour | Search state | ✅ |
| 9 | Should slowly come toward you while wandering | Random wandering | DriftTowardPlayers | ✅ |
| 10 | Got stuck visible / stuck in freeze | "Hidden" only checked its centre | 9-point body check + the hiding rule | ✅ |
| 11 | Freeze too common (staring contests) | 25%+ chance | 10%, and it snaps when you approach | ✅ |
| 12 | Awkward escape in big rooms, wrong direction then switching | Picked cover by straight-line distance | Cover chosen by **real walking distance** + a fast dash | ✅ |
| 13 | Should attack if stared at / followed too long | — | **Stare meter** | ✅ |
| 14 | Instant catch if it saw you hide | — | **Bangs on the door** first | ✅ |
| 15 | Learning maybe unwanted | — | **Learning Enabled** switch | ✅ |
| 16 | Hid the whole match, far away, where you'd already been | 25% hide chance, 30 s hides, poor locker choice | 8% chance, 15 s, leaves if you're far, prefers unexplored lockers ahead of you | ✅ |
| 17 | Grabbing a gem near a hiding monster should trigger it | — | It bursts out and hunts | ✅ |
| 18 | **Impossible to escape** | Hunt speed 6.8 > sprint 6; hunts ignored line of sight | Hunt 5.2; **ends after 2.5 s out of sight**; stamina | ✅ (untested) |
| 19 | Waited outside your locker even when it didn't see you | "Saw you hide" was too generous; always checked the nearest locker first | Strict "saw you" check; fuzzy/old position guess; random locker choice; 30% learned check chance | ✅ (untested) |
| 20 | Turned on you instantly when you crouched around a corner | Hearing gave your exact position | Hearing is a rough guess; hunts need sight | ✅ (untested) |
| 21 | **Hunted all the time** | Random hunts + "close = hunt" rule + frequent fake hunts | Random hunts off; **the ring decides**; fake hunts rarer | ✅ (untested) |
| 22 | Didn't see it half the match | Only found you by wandering | **Tracking** | ✅ (untested) |
| 23 | Not enough toying / peeking | It mostly chased | **Ring, peeks, waiting around corners** | 🟡 needs playtesting |
| 24 | Spotted → it just sprints away | The dash is the main reaction | Added the "waits around the corner" outcome (35%) | 🟡 |
| 25 | Touch = instant death | Catch on contact | **Lunge** with wind-up + dodge + recovery | ✅ (untested) |

### Player, map and visuals
| # | Issue | Cause | What we did | Status |
|---|---|---|---|---|
| 26 | Map too small / samey | Grid maze | Rooms of 3 sizes, corridors of 3 widths, loops | ✅ |
| 27 | Needed an objective | — | 5 gems + win screen | ✅ |
| 28 | Map should be darker | — | Much darker lighting + fog | ✅ |
| 29 | Can see through lockers from inside | The camera sits inside the locker walls | Black overlay with a slit | ✅ |
| 30 | Creep model: lying down, jumping randomly | Some of the asset's animations misbehaved | **Back to the red pill**; animations limited to idle/walk/attack | ✅ (model off) |
| 31 | Want body-part peeks (head or hand around a corner) | Needs art | The code finds partly-visible spots; the pose comes later | 🟡 |
| 32 | Need a warning when a monster is in a locker | — | Not built yet | 🔴 |
| 33 | Want to feel small: tall rooms, wider corridors, bigger monster | Walls 3.2 m, 2 m corridors, monster 2.2 m | Walls 9 m, corridors 4–8 m, monster 3 m | ✅ (untested) |
| 34 | After escaping, it should search, then wander, then slowly come back | It went straight back to stalking | Look around 4 s → maybe lockers → 12 s breather → drift → track | ✅ (untested) |
| 35 | Overall pace: slow and tense, then very fast when it attacks | Speeds tuned for testing | To revisit once the feel is right (see Part 8) | 🔴 later |
| 36 | Hides "2 feet to the side", gets re-spotted, scoots back again and again | Cover only had to be off your screen, not behind a wall | Prefers real cover; if only off-screen it never stays: it teleports | ✅ (untested) |
| 37 | Sometimes sat still ~20 m away forever | Probably stranded on a wall top (walkable "island") after a vanish | Wall/locker tops not walkable; teleport spots must be reachable from the player; stuck/stranded → teleport | ✅ (untested) |
| 38 | Hunts chained back to back while circling it | Stare meter kept filling during the hunt | Meter only fills while stalking/spotted/hiding; reset after hunts | ✅ (untested) |
| 39 | Tracking dropped you when it was far (52 m) | Old "lose interest beyond 45 m" rule | Ignored while tracking | ✅ (untested) |
| 40 | Circling it beats the lunge (recharge stamina, tap sprint on the lunge) | Lunge only fires within 3.5 m with line of sight | Later: attack rework | 🔴 later |
| 41 | Want it to always be around, teleporting, not walk far away and wander back | — | Teleporting (v10) | ✅ (untested) |

---

## 5. Known risks and open problems
Things that may cause trouble, beyond what's listed above:

1. **The newest version is untested.** The ring, peeking, corner waits, lunge, tracking and stamina may all need number changes or bug fixes.
2. **"Seen" ignores darkness.** The game counts the monster as seen whenever it's on your screen with no wall in the way, even if it's too dark to make out. In the darker map that can trigger "spotted" or fill the stare meter unfairly. *Fix idea:* use the light-zone system from the design doc, or count only what your flashlight hits.
3. **The monster "cheats":** tracking, drift and gem guarding use your real position (with fuzz). That's acceptable for a stalker, but the design doc's particle filter (a proper guess built from clues) could replace it.
4. **Big open rooms:** if no ring point is hidden, it stands somewhere visible. You'll spot it often there, so the ring stays large. That may actually be good: open rooms feel safe.
5. **Peek trips can be seen:** walking to a peek spot can expose it on the way. That's fine (it's another glimpse), but it isn't a "pure" peek.
6. **Freeze → attack chance never changes**, even though it's listed as learned (there's no lesson for it yet).
7. **Gem burst range:** *any* monster within 10 m of a grabbed gem hunts, even one that didn't know you were there. That may feel harsh with several monsters.
8. **One monster only so far.** Balance will change completely with 4–6 monsters. That's what the Director (pacing and threat budget) is for.
9. **No sound at all.** Sound is the biggest missing suspense tool.
10. **Profile resets:** each `ProfileVersion` bump overwrites your tweaks once.
11. **Visio:** the 18-page diagram file is still in a temporary folder and needs copying into the project once Visio is closed.

---

## 6. Idea backlog
Collected from our discussions, roughly in suggested order:

| Idea | What it adds | Notes |
|---|---|---|
| **Teleport flash + fake teleports** (your idea) | A flash of light around a corner when it teleports, so you notice something happened without seeing it. Sometimes a FAKE flash: it didn't leave. Or you look away, hear running, look back: it's right in front of you, then it flashes away (a fake hunt that ends in a teleport). | Do after A/C/D/E/F feel right || **Fast when unseen** (shelved) | Moves very fast when nobody can see it, slowly when seen; teleporting becomes rare | Parked for now; close to the Statue's rule, so decide when designing the roster |
| **Fast when unseen** (shelved) | Moves very fast when nobody can see it, slowly when seen; teleporting becomes rare | Parked for now; close to the Statue's rule, so decide when designing the roster |
| **Sound tied to the ring** | Footsteps that stop when you turn around; breathing that gets louder as the ring shrinks | Cheapest, biggest scare boost |
| **Locker tell** | A sign a monster is inside (door ajar, breathing, a twitch) | Keeps monster lockers fair |
| **"Seen" respects light** | Darkness actually hides monsters | Fixes risk #2 |
| **Soft cover** (tall grass, curtains) | Blocks sight but not sound | A small change to the visibility check |
| **Peek animation** | Head and hands around the corner; the body hidden in any pose | The code already finds the spots |
| **Director** | Pacing cycle (build-up → peak → relax), a threat budget, max attackers at once | Essential with several monsters |
| **Vents (Species: Unknown style)** | Connected-everywhere vents for repositioning | Reuses the locker code |
| **Monster tracker item** | Radar showing monsters, even in vents | Counterplay |
| **Searcher monster** | Specialist at checking hiding spots | Makes lockers risky in a different way |
| **Mimic Locker monster** | Pretends to be a hiding spot, with visual tells | |
| **Particle filter** | An honest "where is the player?" guess | Replaces the tracking cheat |
| **Each monster's own loop** | Stalker: glance, don't stare. Statue: keep eyes on it. Brute: dodge it. | Different rules stacking up = juggling |
| **Post-armory behaviour** | The Stalker hides or flees once players are armed | Much later |

---

## 7. Quick tuning guide
| If it feels... | Change this |
|---|---|
| **Too passive / boring** | Ring Shrink Per Second ↑, Peek After Unseen ↓, Ring Push Back ↓ |
| **Too aggressive** | Ring Shrink Per Second ↓, Ring Push Back ↑, Hunt Cooldown ↑ |
| **Impossible to escape** | Hunt Speed ↓, Hunt Lose Sight Seconds ↓, Max Sprint Seconds ↑ |
| **Too easy to escape** | Hunt Lose Sight Seconds ↑, Hunt Speed ↑ (stay under 6) |
| **Lockers too safe** | Check Hiding Spots Chance start ↑, Search Guess Randomness ↓ |
| **Lockers useless** | Check Hiding Spots Chance max ↓, Max Spots To Check ↓ |
| **Never see it** | Peek After Unseen ↓, Peek Max Visible Points ↑ |
| **See it too much** | Peek After Unseen ↑, Peek Max Visible Points ↓ |
| **Lunge never hits** | Attack Windup Seconds ↓, Catch Distance ↑ |
| **Lunge can't be dodged** | Attack Windup Seconds ↑, Lunge Speed ↓ |
| **Following it is too safe** | Wait Around Corner Chance ↑, Corner Ambush Distance ↑ |
| **Too many fake scares** | Fake Hunt Chance ↓, Scare Cooldown ↑ |
| **Want plain randomness while testing** | Learning Enabled → OFF |

---

## 8. Version history and review agenda

### 8.1 Every version of the Stalker so far
Fill in the **Feel** column in the review: what you liked and what you didn't.

| Version | What changed | Why | Feel (your notes) |
|---|---|---|---|
| **v1: first prototype** | Small grid maze, capsule monster. Wander / stalk-from-behind / spotted (stare-hide, freeze) / hunt / fake hunt. Basic learning. | Get something playable | |
| **v2: bigger world** | Rooms of 3 sizes, corridors of 3 widths, loops. Lockers. Monster searches lockers. Drifts toward players. | "Map too small, should check lockers, should come toward me" | |
| **v3: the hiding rule** | 9-point body visibility; "hidden" = fully out of sight. Freeze 10% and snaps when you approach. | "Gets stuck visible / staring contests" | |
| **v4: the Stalker's loop** | Escape by real walking distance + fast dash. Relocating. **Stare meter.** **Banging on lockers.** Learning switch. | "Awkward escapes; stare too long = attack; warn me before opening my locker" | |
| **v5: objective** | 5 gems + win screen. Gem guarding (locker by a gem / lurk around a corner). Pickups make noise. | "Need a goal; hiding by the objective would be cool" | |
| **v6: looks** | Creep model + animations, darker map, roar before hunts. | "Try the asset; make it darker" | |
| **v7: less hiding** | Hide chance 25% → 8%, shorter hides, ambush in unexplored areas ahead of you, gem guarding only near you, "show itself" when unseen. | "It hid the whole match" | |
| **v8: the ring** | **The ring**, tracking, corner peeks, wait-around-the-corner, fair lunge, hunt slower than sprint, stamina, lose-sight escapes, smarter lockers, locker slit view, back to the red pill. | "Impossible to escape, hunts constantly, never see it, not enough toying" | |
| **v9: feel small** | 9 m walls, 4–8 m corridors, 3 m monster. After escaping: search → breather → drift back. | "Feel small; search before wandering off" | *Liked: peeking, occasional fake hunts. Disliked: hides 2 ft to the side and keeps getting re-spotted; sometimes just sits still; circling it beats the lunge.* |
| **v10: teleporting** | Spotted → ducks out of sight → **teleports near you, behind a wall** (60%) / waits at the corner (25%) / lurks. Teleports to its ring spot instead of long walks. "Hidden" = behind a real wall, not just off-screen. Can no longer get stranded on wall tops (tops not walkable + stuck detection). Stare meter no longer fills during hunts (no hunt chains). Tracking no longer drops you at 45 m. Small "angry" random attack chance (3%). Stamina refills slower. | "Teleport around me instead of running far away; it sat still; hunts chained" | *Liked a lot: ducks round a corner and is gone, then appears round another; followed it, it was waiting there and fake-hunted (great moment). Disliked: teleports land ~30 m away and it has to catch up; after a teleport it sometimes walked straight across my view as if it forgot me (T-corridor).* |
| **v11: ring memory** | Teleports land at the hidden spot **closest to the current ring distance** (no more 30 m away). Spotting it: small push-back (3 m) **but the ring's maximum drops 1.5 m** each time (pressure builds over a match; resets after a hunt). 1 s settle after teleports. **Sneaky movement**: won't walk across your view, waits up to 2 s then teleports instead. Random "angry" attacks 3% → 1%. | "Remember the ring; land near it; don't walk out in front of me; fewer random attacks" | |
| **v12: corner peeks** | Every peek spot must have a **duck point one step away (≤1.5 m) fully behind a wall**: peeks are always right at a corner. It gets to the hidden side first (teleporting there if far and unseen), then slowly leans out. When spotted it **steps back** behind the corner instead of a long run; same quick duck whenever it's spotted next to a corner. Peeks show at most 3 of 9 body points. Ring spots strongly prefer real cover. | "It pokes too far out and then does a long awkward run to hide" | *Felt worse: no peeks seen; I keep stumbling into it, then it runs (once right past me); it feels scared of me, not stalking me; I'm chasing it. Excitement dipped. Cause: v11–v12 taught it to AVOID being seen, so sightings were accidents followed by a sprint.* |
| **v13: menace, not fear** | Spotted: **holds eye contact 1–2 s, then backs away slowly still facing you** (only dashes if cover is >8 m away). Escape routes never pass within 3 m of a player. **Leans out to watch you** from behind its corner every 3–7 s (only an edge visible), then leans back. Peeks after 12 s unseen (was 20). Waiting spots strongly prefer BEHIND you (no stumbling into it ahead). Stare meter 6 s. | "It feels scared of me; I never see it watching; it ran past me" | *Liked a lot more: good charm; saw it one way, turned, it was suddenly behind me. Disliked: still turns its back and runs; still caught in the open with long runs; the step-out-of-view-and-stay loop repeats too often; still some runs past you.* |
| **v14: the trickster** | **A:** always faces you, even moving fast. **C:** only stops at corners (next to a wall, with a hidden spot one step away) for ring spots and teleport landings. **D:** caught in the OPEN (no corner within 3 m) → stands dead still and stares; look away → gone; walk within 6 m or stare too long → attacks. **E:** staying put after being spotted is rare (once per spot, more only by a 30% chance chain). **F: BAIT:** 35% of relocations go far down the hallway ahead (18–30 m), step out at the corner at the end and wait; come within 10 m → it charges; ignore it → it slips away. | "Always face me; never stuck in the open; caught off guard it should stare; less 'still there'; lure me down the hallway" | *Attacked a decent amount. Telemetry: almost every attack was the stare meter firing during/just after its own slow exit, or leftovers from many quick sightings; plus a bug loop (hunt times out next to you → instantly spotted → stand-and-stare attacks → another hunt, 4× in a row).* |
| **v15: fair stare + flash** | Stare meter **doesn't fill while it's leaving** (backing away after being spotted), **resets after each encounter**, and pauses for a **5 s grace period after every hunt** (no stand-and-stare attack either). The 'looking at it' timer resets when a hunt ends; a hunt that runs out next to you makes it **back off** instead of standing there (fixes the hunt loop). **Freeze removed** for the Stalker. **Purple teleport flash** where it was, every time it teleports. | "Noticing it shouldn't get me killed; fix the hunt loop; show a purple flash on teleport" | *Liked it: only 2–3 hunts, fair. Disliked: stand-and-stare flicker loop (glance away, still there, glance back...) then an attack; saw it teleport twice; saw its EYES down a hallway (it went from one place to another in view). Wants: brighter flash; teleport out at the end of a hunt.* |
| **v16: never seen teleporting** | **Bug fix:** the glowing eyes stick out 0.6 m in front of the body, so the game thought it was hidden while you could see its eyes; it now checks its face too (and the capsule's full width). **Stand-and-stare** vanishes after just 0.25 s of you looking away (no flicker loop). **End of a hunt:** if it lost you (and you're not in a locker) it teleports out with a flash; if the hunt ran out next to you it backs off, then always teleports away. **Brighter flash** (intensity 10, range 11 m). | "Never let me see it teleport; fix the stand-and-stare loop; poof at the end of a hunt; brighter flash" | *Better (2–3 hunts, fair). Problems: teleport is oversold (it's all he does); ignoring him completely (staring at walls, even bumping into him) never got me killed.* |
| **v17: ignore fix + teleport mix** | **Ignored:** if nobody sees him for 25 s he stops peeking/leaning (which used to pause the ring forever) and closes in 2× faster: ignoring him gets you killed in about a minute. **After slipping away** he picks one: teleport away 35% (flash), walk off 35% (no flash, avoids that spot), bait 15%, stay 15%. **Flash budget:** at most one signature flash per 20 s (otherwise he walks off). **Behind-the-scenes** teleports (catching up when 20 m+ away, getting unstuck, reaching a far peek corner) have **no flash**. Hunt endings and look-away vanishes always flash. | "Teleport is oversold; ignoring him is safe" | *Pretty good, attacked somewhat consistently. Problems: it backed away slowly for ages when cover was far (should just teleport); a pillar loop (followed it round a pillar while it retreated: infinite); ring attack from behind a wall: rounded the corner and killed me instantly.* |
| **v18: no long retreats, don't follow, fair final approach** | Cover more than 5 m away → it **teleports right after the stare, even if you're watching**. **Don't follow him:** still watching 3 s into its retreat and you kept up with it → it attacks; only watched → it teleports away. **Final approach:** when the ring has nearly closed it moves somewhere it can **see you** (and you could see it if you turned), and the ring attack only starts when it can see you (no more attacking from behind a wall). | "Teleport when cover is far; pillar loop; killed from behind a wall" | |
| **v19: darker map + film grain** | Moonlight 0.15 → 0.05, ambient light 0.07 → 0.03, fog darker and starts at 3 m (ends at 28 m, not lower, because he baits from 18–30 m). Lamps in about 1 room in 5 instead of 1 in 3, and dimmer (0.8 → 0.5). Flashlight unchanged. **Film grain** on the camera (grain 0.06, fine, at 24 fps (first try 0.14 was too noisy), stronger in the dark, slight desaturation, vignette, faint flicker; G toggles). Same AI as v18. | "Can you make the game darker?" + "add a grainy film" | |
| **v20: the Brute** | **New monster, the Brute** (3.8 m tall, 1.7 m wide, orange eyes), same brain as the Trickster with nearly everything switched off: no ring, teleports, stalking, peeks, bait, hiding or learning. Roams, hears sprinting, **sees you → ROAR (1.1 s) → charges at 4.8 m/s** (walk 3.5, sprint 6) → **tires out after 8 s and rests 6 s**. Loses you after 3 s out of sight (looks around, then roams). Slow wide lunge (wind-up 0.5 s, reach 1.9 m, recover 1.3 s). Never checks lockers; bangs on one it saw you enter and opens it only 25% of the time. **LOUD:** generated thuds, roar and panting (3D sound, heard up to 40 m) plus on-screen captions pointing toward the sound. New settings: Tires Out, Rest Seconds. Both monsters are on; untick either one in the Hierarchy to test solo. Known limit: it uses the same NavMesh as the Trickster, so it fits through the same gaps (it can clip walls slightly on corners). | Plan step 2: build the Brute (big, wide, loud, slow, charges on sight, tires out). | |
| **v21: Trickster peeks from lockers (current)** | **Locker peek** (new peek type, about 35% of peeks when a locker is in front of you): he slips into a locker 6–22 m ahead, door facing you, without a flash. After 1.5–3 s the door **creaks open** (30°) with **two glowing eyes** in the gap. **Spotted** (0.6 s): ring pushed back as normal, door eases to a crack, and he stays 8–15 s. Nobody notices for 10 s: he closes up and slips away. Leaves unseen with a quiet teleport. **Tells on every locker he's in:** door ajar 7°, one eye in the gap, a creak every 6–12 s. **Walk past (3 m):** door slams open with a **screech**, then 75% a fake hunt (lunge, gone in a flash), 25% a real 4 s chase. **Opening it** = caught (following him). Generated creak and screech sounds. New settings: Peek From Lockers, Locker Peek Chance, Locker Peek Delay, Stay In Locker After Spotted, Burst Out Real Attack Chance. The old walk-in ambush hide is unchanged (still rare) but now shows the same tells. | User liked v20 (Trickster and Brute). Wanted the Trickster's hiding back as an ability that fits him; their idea: hiding spots as a new place to peek from. Walk past: mostly a scare. Tells: both eyes and door. | |

### 8.2 Agenda for the review (after your playtest)
**Your side (feel and gameplay):**
- Which version felt best for suspense, and why?
- Which changes made it better, and which made it worse?
- Does the ring loop work: glimpse → spot it → it backs off → careless → it's on you?
- Is the pace right? You want slow, tense lulls and then very fast attacks. That's a speed and breather question.

**My side (how buildable is this, and does it scale?):** topics I'll bring:
- **Cost per frame.** Today each monster checks visibility for every player (up to 9 line checks each, every frame). The ring and cover searches test 16–48 spots a few times a second. With 6 monsters and 4 players that's a few hundred line checks per frame. That's fine on PC, but there are easy wins: run far-away monsters less often ("AI level of detail"), batch the line checks with Unity's job system, and spread searches across frames.
- **Multiplayer.** The AI should run on the host only, with clients just receiving positions. The "can this player see it?" checks need each player's camera direction on the host, which is sent anyway. Proximity chat volume can feed straight into the noise system.
- **Huge procedural maps.** Build the walkable-area map (the NavMesh) in tiles or in the background, so loading doesn't freeze. Monsters far from everyone can be simulated cheaply until someone gets close.
- **What carries over to the real project:** the profile + rule + modular brain structure, the ring, the visibility counting, the learning limits and the telemetry. What gets rewritten: the debug HUD, the map builder, and the cheats (tracking via the real position → the particle filter).
- **Simpler or better ways** to do any of the above, wherever I see them.

**Then together:** decide the next step: keep tuning this version, roll back a change, or move on to the next monster.

---

## 9. The Trickster: full design (the "max version" we're building toward)

**Who he is:** an imp / trickster. He wants to get close and take you. He loves catching you off guard, toying with you, baiting you, and making sure you know he's there. He **hates being caught off guard.**

**His rule:** *keep an eye on him, but don't stare.* If you stop checking around you, he closes in (the ring). If you catch him in the open and stare, or walk up to him, he attacks.

**His role in a full match:** **chaos and misdirection, low lethality.** He rarely kills on his own. He wastes your time and stamina, splits your attention, and lures you into worse places (including toward other monsters). With several monsters running, it's *good* that he doesn't attack much: his danger is what he sets up.

### Feature tiers
| Tier | Feature | Status |
|---|---|---|
| **Core** | Ring pressure (closes in unless you keep spotting him; pressure builds over a match) | ✅ built |
| | Teleports only when unseen, landing at a corner near the ring, with a **purple flash** where he was | ✅ built (v16: face/eyes now checked) |
| | Peeks + leaning out to watch you (only an edge visible) | ✅ built |
| | Caught in the open → stands and stares (look away: gone; stare or approach: attack) | ✅ built |
| | Bait: waits at the end of a hallway; come within 10 m and he charges | ✅ built |
| | Always faces you; backs away instead of running | ✅ built |
| | Teleports out at the end of a hunt | ✅ built (v16) |
| **Tricks** (next) | **Fake teleport:** flash, but he's still there (or right behind you) | 🔜 |
| | **Fake hunt-end:** the hunt "ends" with a flash... and he's behind you | 🔜 |
| | **Look away → hear running → look back: he's in your face**, then flashes away (a fake hunt) | 🔜 |
| | Sounds: footsteps that stop when you turn, a teleport "pop" | 🔜 |
| **Later** | **Disguise:** pretends to be another monster (e.g., the Statue). You obey the Statue's rule (stare at it)... he breaks it, a purple flash reveals him, and he charges | needs the Statue built first |
| | Camera room (teammate's idea): dead players watch cameras and radio teammates. Spectators/cameras **may** see his teleports; living players never should | way later |

### How we keep it simple
- **One new trick at a time,** play-tested before the next.
- **Every trick gets a switch and a chance setting** on the profile, so anything that doesn't work can be turned off without deleting code.
- **The rule stays the same** ("keep an eye on him, don't stare"). Tricks play *with* that rule; they don't add new rules players must learn.

### Solo vs. many monsters vs. many players
- **Build each monster solo at "full presence"** (what we're doing). Every behaviour has a dial: *presence* (how often you see him), *pressure* (how fast the ring closes), *lethality* (how often he attacks).
- **In the real game a Director turns those dials down** depending on how many monsters are active and how stressed the players are (design doc 6.4: pacing cycle + threat budget). We don't need to solve the multi-monster game now. We only need every monster to *have* the dials.
- **Many players:** "unseen" already means unseen by **every living player**, and he goes after the player whose ring is smallest (the straggler). More players = fewer hidden spots, so he'll teleport less and lean on peeks and bait more. That's a natural, self-balancing effect. Dead players in the camera room won't count as "seeing" him.
- **Cost:** visibility checks scale with monsters × players (for example 6 × 4). That's fine on PC with the optimisations listed in Part 8 (batching, running far-away monsters less often).
