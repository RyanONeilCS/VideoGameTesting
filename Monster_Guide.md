# Monster Guide

**What this is:** one place to describe every monster: who it is, what it does, its rule, exactly when it attacks, and how to survive it. **No code or technical detail** (that's in `Monster_AI_Design.md` and `Prototype_Report.md`).

**Only monsters we've actually built are in here.** New monsters get added as we build them (use the template at the bottom). All names are working titles. Edit freely.

---

## The Trickster
*(working title; formerly "the Stalker")*

**In one line:** an imp that teleports around you, watches you from corners, lures you in, and makes sure you *know* he's there.

**Personality:** a trickster. He wants to get close to you and take you. He loves catching you off guard, toying with you, baiting you, and playing with your expectations. He **hates being caught off guard**: catch him in the open and he freezes and stares back.

### What he generally does
- **Circles you at a distance,** always just out of sight, slowly closing in (the "ring").
- **Peeks** around corners ahead of you, and **leans out** from behind corners to watch you. Only part of him shows: a head, a shoulder, his eyes.
- **Peeks out of lockers,** too: a locker door ahead of you **slowly opens**, and his **eyes watch you through the gap**. Spot him and the door eases shut... but now you *know* he's in there. A locker he's hiding in sits **a crack open, one eye glinting**. Hiding, he makes **no sound at all**: you have to *see* it.
- When you spot him, he **holds your gaze, then backs away** behind the corner, always facing you, never turning his back. (If there's no cover close by, he just **teleports away on the spot**, even while you're watching.) Then he either:
  - **teleports away** (a **purple flash** around the corner)... or **pretends to**: about 1 flash in 6 is fake, and he's still right there around the corner,
  - **walks off** out of sight (no flash: is he still nearby?),
  - **baits you:** he reappears far down the hallway ahead of you, just standing there,
  - or (rarely) **stays right there** around the corner.
- Every sighting brings him a little closer over the match. The pressure builds.
- **Disguises himself as another monster in the match** (now and then, out of sight). Right now: **the Brute**. Same size, same look, same charge... but **completely silent.** No thuds, no roar, no panting. The real Brute is always loud. He stays disguised for up to a minute, then changes back where you can't see.

### The rule
> **Keep an eye on him, but don't stare.**
> Glance around regularly. When you see him, let him go. Don't follow him. Don't walk toward him.

### He attacks when...
1. **You stop looking around.** If nobody spots him for a while, he closes in until he's right behind you, then attacks. For that final approach he comes around to **where he can see you** (so you could see him if you turned in time), roars, and attacks. If you ignore him completely (about 25 s with nobody seeing him), he stops showing off and comes straight for you, fast. **Ignoring him gets you killed in about a minute.**
2. **You stare at him too long** (about 6 seconds) while he's watching you, baiting you, or caught in the open.
3. **You walk up to him** when you've caught him in the open.
4. **You take the bait:** you walk down the hallway toward him and get within about 10 m.
5. **You follow him:** around a corner while he's waiting there for you (including after a **fake teleport**: going to check where he "left" from counts), or by keeping up with him while he backs away (for about 3 s, like circling a pillar with him).
6. **You open a locker he's in** (that's following him). **Walk right past it** and he bursts out with a screech: usually just a scare (he flashes away), but about 1 in 4 times it's real.
7. **Rarely, for no reason.** He's "angry" (about once every 10 minutes). *Open question: keep it, or keep the rule 100% consistent?*
8. **Disguised as another monster** (see below): reach you as the fake Brute and it's usually just a purple flash in your face, but about 1 in 4 times it's real. He also **opens the locker** he saw you get into (the real Brute almost never does).

### When he attacks
- He **roars** first (a moment of warning).
- He chases you: **faster than walking, slower than sprinting.** Sprint is limited by stamina.
- His attack is a **lunge**: he stops for a split second, then lunges where you were. **Sidestep and he misses.**
- **Break line of sight for a couple of seconds** and the hunt ends. **Purple flash**, and he's gone... *(planned trick: sometimes the hunt only pretends to end).*

### He won't / can't
- He never **appears** where a living player can see him. (He may vanish in front of you if there's no cover nearby.)
- He never turns his back on you.
- He doesn't attack just because you noticed him.

### How to survive
- Check behind you regularly; every sighting pushes him back a little.
- When you catch him in the open, **look away.** He'll leave.
- Don't chase him, don't follow him around corners, don't walk toward him down a hallway.
- **Watch the lockers.** A door open a crack with an eye in it: walk wide around it, and don't open it.
- **Listen to the Brute.** If a Brute is coming at you **without a sound**, it's him: **stand your ground and watch it.** He's exposed and flashes away. (Do that to the *real* Brute and you're caught. Trust your ears.) And never hide from the silent one in a locker: he *will* open it.
- Stick together: he goes after **whoever isn't watching their back.**

### Tells (how you know he's around)
- The **purple flash** (usually means he teleported... usually. About 1 in 6 is fake: he never left).
- Glowing eyes in a dark corner; a head poking around a corner.
- A locker door that sits **open a crack** with an eye in the gap, or slowly **swings open** with two eyes watching (silent: you have to see it).
- A monster that's **acting wrong**: a **silent Brute** is him in disguise.
- *(Planned)* footsteps that stop when you turn around.

### Tricks (planned, added one at a time)
- ✅ **Fake teleport (built):** a flash, but he never left. He's waiting right there around the corner. Go check where he "left" from and he gets you. About 1 flash in 6, so you usually trust the flash.
- **Fake hunt ending:** the hunt "ends" with a flash... and he's right behind you.
- **Look away, hear running, look back:** he's right in your face, then he flashes away.
- **Disguise (later, more monsters):** copies of other monsters as they're built (e.g., the Statue), each with its own tell.

### Role in a full match
**Chaos and misdirection.** He rarely kills on his own; his danger is the attention he takes away and the places he lures you. With other monsters around, that's exactly when they get you.

### Open questions
- How often should he teleport vs. walk vs. stay? *(Currently testing: about 35% teleport, 35% walk off, 15% bait, 15% stay, with at most one purple flash every ~20 s.)*
- How rare should fake teleports be? *(Currently testing: about 1 in 6 of his "slipped away" flashes.)*
- Keep the rare "angry" random attack?
- Final look: tall, thin, imp-like? Lots of room for great animations (peeking, backing away, the stare).
- Copy **any** monster later (even ones not in the match)? For now he only copies monsters that are actually in the game, so a copy is never automatically fake.

---

## The Brute
*(working title)*

**In one line:** a huge, loud, dumb thing that charges the moment it sees you, then has to stop and catch its breath.

**Personality:** all muscle, no tricks. It doesn't stalk, hide, peek or teleport. You **always hear it coming**. It's not scary because it's clever. It's scary because when it sees you, it *comes*.

### What it generally does
- **Roams** the map slowly, drifting roughly toward the players now and then. Heavy **THUD... THUD...** footsteps you can hear from far away.
- **Hears you sprint** nearby and lumbers over to check.
- **Sees you:** stops, **ROARS**, and charges.
- After a short chase (about 8 s) it **runs out of steam**: stops dead, heaving, glaring at you. Then it goes back to roaming, or charges again if it can still see you.

### The rule
> **Don't let it see you.**
> Listen for it. When you hear it close, get out of its way or hide.

### It attacks when...
1. **It sees you.** Any time, from up to about 25 m away. (It roars first: that's your cue to run.)
2. **It catches you in a chase.** It's faster than walking but **slower than sprinting**, so you can outrun it if you have the stamina.

### When it attacks
- A long **ROAR** first (about a second): run.
- It charges: **faster than walking, slower than sprinting.**
- Its attack is a slow, wide **lunge**: a big wind-up, a long reach. **Sidestep** and it misses, and it's clumsy for a moment after.
- **Break line of sight** for a few seconds and it loses you: it looks around where it last saw you, then wanders off.
- Chase it for too long and it **tires out** (about 6 s of resting). Use that to get away.

### It won't / can't
- It never teleports, stalks, peeks or hides. **You always know roughly where it is** (you can hear it).
- It doesn't check lockers. If it **saw** you get in, it bangs on the door for a few seconds and **usually gives up**.
- It doesn't learn. It's the same every match.

### How to survive
- **Listen.** The footsteps (and the on-screen THUD) tell you how close it is and which way it is.
- Keep corners between you and it. If it sees you: **sprint**, break line of sight, or dive into a locker it didn't see you enter.
- Save some stamina. A chase you can't outrun ends at the lunge.

### Tells (how you know it's around)
- **THUD... THUD...** heavy footsteps (faster when it's charging).
- A **ROAR** right before it charges.
- **Heavy breathing** while it rests.
- Dull orange eyes, and it's *big* (taller and much wider than the Trickster).
- **It is never silent.** If a "Brute" moves without a sound, it's the Trickster in disguise.

### Role in a full match
**Pressure and noise.** It pushes players around the map, forces sprints (burning stamina), and drives them into lockers and corners, which is exactly where the Trickster likes them. It's the monster you can always hear; the Trickster is the one you can't.

### Open questions
- Should it fit through narrow gaps? *(Planned: a small gap or vent it can't follow you through.)*
- Is 8 s of chasing / 6 s of resting the right rhythm?
- Should it break doors or smash lockers later?
- Should the Trickster be able to use the Brute's noise as cover?
- **The Trickster can disguise himself as the Brute.** A Brute that makes **no sound** is not the Brute (see the Trickster).

---

## Template for a new monster
Copy this when adding a monster:

```
## The <Name>
**In one line:**
**Personality:**

### What it generally does
### The rule
> <one sentence players can remember>
### It attacks when...
### It won't / can't
### How to survive
### Tells (how you know it's around)
### Role in a full match
### Open questions
```
