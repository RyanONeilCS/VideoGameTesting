using System.Text;
using UnityEngine;

/// <summary>
/// Draws simple text on screen: crosshair, controls, hide prompt, "CAUGHT!", and a debug panel
/// showing what every monster is thinking (press F1 to hide/show).
/// OnGUI is Unity's old, quick way to draw UI. Perfect for debugging, not for a real game UI.
/// </summary>
[RequireComponent(typeof(PlayerAIState))]
public class PlayerHUD : MonoBehaviour
{
    public bool showDebug = true;

    PlayerAIState ai;
    PlayerHiding hiding;
    FirstPersonController mover;
    GUIStyle small, big, prompt;

    void Awake()
    {
        ai = GetComponent<PlayerAIState>();
        hiding = GetComponent<PlayerHiding>();
        mover = GetComponent<FirstPersonController>();
    }

    void Update()
    {
        if (InputHelper.DebugTogglePressed()) showDebug = !showDebug;
    }

    void OnGUI()
    {
        if (small == null || prompt == null || big == null)   // (re)create styles if any are missing
        {
            small = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            small.normal.textColor = Color.white;
            prompt = new GUIStyle(small) { fontSize = 20, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            big = new GUIStyle(GUI.skin.label) { fontSize = 48, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            big.normal.textColor = new Color(1f, 0.15f, 0.15f);
        }

        float w = Screen.width, h = Screen.height;

        // Inside a locker: black out everything except a thin slit (you're looking through the door's vents).
        // (Also stops you seeing through the locker's walls from inside.)
        if (ai.IsHiding)
        {
            Color old = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, w, h * 0.42f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0f, h * 0.53f, w, h * 0.47f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // Stamina bar (bottom centre), only when not full
        if (mover != null && mover.Stamina01 < 0.999f)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(w / 2f - 101f, h - 61f, 202f, 10f), Texture2D.whiteTexture);
            GUI.color = mover.Stamina01 < 0.3f ? new Color(1f, 0.3f, 0.2f) : new Color(0.85f, 0.85f, 0.85f);
            GUI.DrawTexture(new Rect(w / 2f - 100f, h - 60f, 200f * mover.Stamina01, 8f), Texture2D.whiteTexture);
            GUI.color = old;
        }

        // Crosshair
        GUI.Label(new Rect(w / 2f - 4f, h / 2f - 12f, 20f, 24f), "+", small);

        // Hiding prompts
        if (ai.IsHiding)
            GUI.Label(new Rect(0f, h * 0.7f, w, 30f), "Hiding...   [E] get out", prompt);
        else if (hiding != null && hiding.LookedAtSpot != null)
            GUI.Label(new Rect(0f, h * 0.7f, w, 30f), "[E] Hide", prompt);

        if (Cursor.lockState != CursorLockMode.Locked)
            GUI.Label(new Rect(0f, h / 2f + 20f, w, 40f), "Click to play   (Esc frees the mouse)", prompt);

        if (Time.time - ai.lastCaughtTime < 2.5f)
            GUI.Label(new Rect(0f, 0f, w, h), "CAUGHT!", big);

        // The objective: gem counter (top right), pickup flash, win screen
        var goal = GameObjective.Instance;
        if (goal != null)
        {
            GUI.Label(new Rect(w - 260f, 10f, 250f, 30f), $"GEMS  {goal.Collected} / {goal.Needed}", prompt);
            if (Time.time - goal.LastCollectTime < 1.5f && !goal.Won)
                GUI.Label(new Rect(0f, h * 0.25f, w, 40f), "+1 GEM", prompt);
            if (goal.Won)
            {
                GUI.Label(new Rect(0f, h * 0.38f, w, 60f), "YOU WIN!", big);
                int secs = Mathf.FloorToInt(goal.MatchTime);
                GUI.Label(new Rect(0f, h * 0.38f + 60f, w, 30f),
                    $"All gems collected in {secs / 60}:{secs % 60:00}   •   caught {ai.timesCaught}x   •   [R] back to the menu", prompt);
            }
        }

        // Something is banging on the door of the locker you're in (placeholder until there are real sounds)
        foreach (var m in MonsterBrain.All)
        {
            if (m.IsBanging && ai.IsHiding && m.BangingOn == ai.currentHidingSpot && Mathf.Repeat(Time.time, 0.6f) < 0.3f)
                GUI.Label(new Rect(0f, h * 0.35f, w, 60f), "*BANG*   *BANG*   *BANG*", big);
        }

        // Loud monsters (the Brute): a caption for each sound, placed around the crosshair in the direction it
        // came from (top = ahead, bottom = behind you), bigger and brighter the closer it is.
        var cam = ai.playerCamera;
        if (cam != null)
        {
            foreach (var f in MonsterFootsteps.All)
            {
                float age = Time.time - f.LastSoundTime;
                if (age > 0.45f) continue;
                Vector3 to = f.transform.position - cam.transform.position;
                float dist = to.magnitude;
                if (dist > f.hearDistance) continue;
                float closeness = 1f - dist / f.hearDistance;
                Vector3 fwd = cam.transform.forward; fwd.y = 0f;
                to.y = 0f;
                float angle = Vector3.SignedAngle(fwd, to, Vector3.up) * Mathf.Deg2Rad;
                float radius = Mathf.Min(w, h) * 0.32f;
                var pos = new Vector2(w / 2f + Mathf.Sin(angle) * radius, h / 2f - Mathf.Cos(angle) * radius * 0.8f);
                var style = new GUIStyle(prompt) { fontSize = Mathf.RoundToInt(Mathf.Lerp(14f, 34f, closeness)) };
                Color c = f.LastSound == "ROAR" ? new Color(1f, 0.25f, 0.2f) : new Color(0.9f, 0.85f, 0.75f);
                c.a = Mathf.Lerp(0.25f, 1f, closeness) * (1f - age / 0.45f);
                style.normal.textColor = c;
                string caption = f.LastSound == "ROAR" ? "*ROAR*" : f.LastSound == "huff" ? "(heavy breathing)" : "THUD";
                GUI.Label(new Rect(pos.x - 150f, pos.y - 20f, 300f, 40f), caption, style);
            }
        }

        GUI.Label(new Rect(10f, h - 30f, w, 30f),
            "Collect the glowing gems  •  WASD move  •  Mouse look  •  Shift sprint  •  Ctrl/C crouch  •  Space jump  •  E hide  •  G film grain  •  F1 debug", small);

        if (!showDebug) return;

        // Debug panel: what is each monster doing and thinking?
        var sb = new StringBuilder();
        sb.AppendLine($"YOU: {(ai.IsHiding ? "HIDING" : ai.moveState.ToString())}   noise {ai.noiseLevel:0.0}   caught {ai.timesCaught}x");
        foreach (var m in MonsterBrain.All)
        {
            if (m.profile == null) continue;
            bool gem = m.GuardingGem != null;
            string detail = m.State == MonsterState.Spotted ? $" ({m.CurrentReaction})"
                          : m.IsFakeHunt ? " (FAKE)"
                          : m.IsWindingUp ? " (ROAR!)"
                          : m.IsAttacking ? " (LUNGING!)"
                          : m.IsResting ? " (WORN OUT: catching its breath... get away!)"
                          : m.IsLookingAround ? " (lost you: looking around)"
                          : m.IsPeeking ? " (peeking at you from a corner)"
                          : m.IsLeaningOut ? " (leaning out, watching you)"
                          : m.IsBaiting ? " (BAITING you from down the hall... don't get too close)"
                          : m.IsFakeTeleporting ? " (FAKE TELEPORT: it never left... still around the corner. Don't go check)"
                          : m.IsWaitingAroundCorner ? " (waiting around the corner... don't follow)"
                          : m.IsLockerPeeking ? " (PEEKING OUT OF A LOCKER: door open, eyes on you)"
                          : m.IsHiddenInSpot ? (gem ? " (inside a locker BY A GEM!)" : " (inside a hiding spot!)")
                          : m.State == MonsterState.Hide && gem ? " (going to hide by a gem)"
                          : m.IsWaitingAtGem ? " (lurking by a gem)"
                          : gem ? " (heading to guard a gem)" : "";
            sb.AppendLine();
            sb.AppendLine($"{m.profile.displayName}:  {m.State}{detail}   distance {m.DistanceToTarget:0.0} m");
            if (m.IsDisguised)
                sb.AppendLine($"   << DISGUISED as {m.DisguisedAs.OwnProfile.displayName}   TELL: {m.DisguisedAs.OwnProfile.disguiseTell} >>" +
                              $"   call its bluff (watch it charge, don't sprint): {m.BluffMeter:0.0} / {m.OwnProfile.bluffCallSeconds:0.0}");
            string hidden = m.IsWatched ? "NO (you can see part of it)" : $"YES (unseen {m.UnseenFor:0.0} s)";
            sb.AppendLine($"   HIDDEN FROM YOU: {hidden}    it sees you: {YesNo(m.CanSeeTarget)}    can move: {YesNo(m.CanMoveNow)}");
            if (m.profile.useRing && m.Target == ai)
                sb.AppendLine($"   RING {m.Ring:0.0} m  (max {m.RingMax:0.0})   shrinks while unseen, attacks at {m.profile.ringAttackRadius:0} m;" +
                              " spotting it pushes it back a little but lowers the max" +
                              (m.IsBeingIgnored ? "   << YOU'RE IGNORING IT: closing in fast >>" : ""));
            if (m.profile.attackWhenWatchedTooLong)
                sb.AppendLine($"   STARE METER {m.WatchPressure:0.0} / {m.profile.watchedTooLongSeconds:0.0}  (full = it attacks)");
            string learning = m.profile.learningEnabled ? "LEARNED" : "LEARNING OFF -";
            sb.AppendLine($"   {learning}  boldness {m.Boldness.current:0.00}   fake hunt {m.FakeHuntChance.current:0.00}" +
                          $"   hide & ambush {m.HideAmbushChance.current:0.00}   check spots {m.CheckSpotsChance.current:0.00}" +
                          $"   guard gems {m.GuardGemChance.current:0.00}");
        }
        string text = sb.ToString();
        float height = small.CalcHeight(new GUIContent(text), 700f) + 16f;
        GUI.Box(new Rect(8f, 8f, 720f, height), GUIContent.none);
        GUI.Label(new Rect(16f, 14f, 700f, height), text, small);
    }

    static string YesNo(bool b) => b ? "YES" : "no";
}
