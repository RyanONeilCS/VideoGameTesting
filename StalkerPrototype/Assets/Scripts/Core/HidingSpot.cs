using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A locker / closet that holds ONE occupant: a player OR a monster (design doc 6.2).
/// Put this on the locker's root object. Its blue arrow (forward) should point OUT, toward the room.
///
/// TELLS (when a monster is inside): the door sits a crack open with one glowing eye in the gap, and creaks now
/// and then. When it PEEKS out of the locker, the door creaks wider and both eyes watch you. When it bursts out,
/// the door slams open with a screech.
/// </summary>
public class HidingSpot : MonoBehaviour
{
    /// <summary>Every hiding spot in the level. Monsters search this list.</summary>
    public static readonly List<HidingSpot> All = new List<HidingSpot>();

    [Tooltip("How many times players have hidden here. Monsters learn from this (they check popular spots first).")]
    public int playerUseCount;

    [Tooltip("Door angle (degrees) while a monster hides inside: just a crack.")]
    public float ajarAngle = 7f;
    [Tooltip("Door angle (degrees) while a monster peeks out at you.")]
    public float peekAngle = 30f;

    public GameObject Occupant { get; private set; }
    public bool IsFree => Occupant == null;
    public MonsterBrain OccupyingMonster => Occupant != null ? Occupant.GetComponent<MonsterBrain>() : null;
    public PlayerAIState OccupyingPlayer => Occupant != null ? Occupant.GetComponent<PlayerAIState>() : null;

    /// <summary>Where the occupant stands inside (on the floor, in the middle).</summary>
    public Vector3 InsidePosition => transform.position;

    /// <summary>Where you stand to get in or out: just in front of the door.</summary>
    public Vector3 ExitPosition => transform.position + transform.forward * 1.2f;

    /// <summary>Where the peeking eyes are (in the gap by the door's free edge). Players "spot" the monster here.</summary>
    public Vector3 PeekEyePoint => transform.TransformPoint(new Vector3(0.46f, 1.95f, 0.6f));

    enum Tell { None, Ajar, Peek, Burst }
    Tell tell;
    float angle, targetAngle, closeBurstAt, nextCreak;
    Transform hinge;
    GameObject eyeOuter, eyeInner;
    AudioSource source;
    static AudioClip creak, screech;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Awake()
    {
        // Put the door on a hinge (its left edge), so it can swing open.
        Transform door = transform.Find("Door");
        if (door != null)
        {
            hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(transform, false);
            hinge.localPosition = door.localPosition - new Vector3(door.localScale.x / 2f, 0f, 0f);
            door.SetParent(hinge, true);
        }
        source = gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = 22f;
        source.playOnAwake = false;
        if (creak == null) MakeClips();
    }

    public bool TryEnter(GameObject who)
    {
        if (!IsFree) return false;
        Occupant = who;
        if (who.GetComponent<PlayerAIState>() != null) playerUseCount++;
        return true;
    }

    public void Exit(GameObject who)
    {
        if (Occupant != who) return;
        Occupant = null;
        if (tell != Tell.Burst) SetTell(Tell.None, null);
    }

    // ---------- Tells (called by the monster inside) ----------
    /// <summary>Hiding inside: door a crack open, one eye in the gap, a creak now and then.</summary>
    public void ShowAjar(Material eyeMat) => SetTell(Tell.Ajar, eyeMat);
    /// <summary>Peeking out at you: the door creaks wider, both eyes watching.</summary>
    public void ShowPeek(Material eyeMat) => SetTell(Tell.Peek, eyeMat);
    /// <summary>Bursting out: the door slams open with a screech, then swings shut.</summary>
    public void BurstOpen()
    {
        SetTell(Tell.Burst, null);
        closeBurstAt = Time.time + 1.5f;
        source.pitch = Random.Range(0.95f, 1.1f);
        source.PlayOneShot(screech, 1f);
    }

    void SetTell(Tell next, Material eyeMat)
    {
        if (tell == next) return;
        bool opening = (next == Tell.Peek && tell != Tell.Peek) || (next == Tell.Ajar && tell == Tell.None);
        tell = next;
        targetAngle = next == Tell.Ajar ? ajarAngle : next == Tell.Peek ? peekAngle : next == Tell.Burst ? 100f : 0f;
        if (eyeMat != null) EnsureEyes(eyeMat);
        if (eyeOuter != null) eyeOuter.SetActive(next == Tell.Ajar || next == Tell.Peek);
        if (eyeInner != null) eyeInner.SetActive(next == Tell.Peek);
        if (opening) Creak(next == Tell.Peek ? 0.9f : 0.5f);
        nextCreak = Time.time + Random.Range(6f, 12f);
    }

    void Update()
    {
        if (tell == Tell.Burst && Time.time >= closeBurstAt) SetTell(Tell.None, null);

        // Swing the door: slow and creaky opening, a little faster closing, a SLAM when bursting out
        float speed = tell == Tell.Burst ? 700f : targetAngle > angle ? 22f : 35f;
        angle = Mathf.MoveTowards(angle, targetAngle, speed * Time.deltaTime);
        if (hinge != null) hinge.localRotation = Quaternion.Euler(0f, -angle, 0f);   // negative = swings outward

        // Something is in there... the door creaks now and then
        if (tell == Tell.Ajar && Time.time >= nextCreak)
        {
            nextCreak = Time.time + Random.Range(6f, 12f);
            Creak(0.35f);
        }
    }

    void Creak(float loudness)
    {
        source.pitch = Random.Range(0.85f, 1.15f);
        source.PlayOneShot(creak, loudness);
    }

    void EnsureEyes(Material mat)
    {
        if (eyeOuter != null) return;
        eyeOuter = MakeEye(new Vector3(0.52f, 1.95f, 0.5f), mat);   // by the door's free edge: shows even when just ajar
        eyeInner = MakeEye(new Vector3(0.40f, 1.95f, 0.5f), mat);   // only shows when the door is open wider
    }

    GameObject MakeEye(Vector3 localPos, Material mat)
    {
        var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(eye.GetComponent<Collider>());
        eye.name = "Monster Eye (tell)";
        eye.transform.SetParent(transform, false);
        eye.transform.localPosition = localPos;
        eye.transform.localScale = Vector3.one * 0.07f;
        eye.GetComponent<Renderer>().sharedMaterial = mat;
        eye.SetActive(false);
        return eye;
    }

    // ---------- Placeholder sounds, made in code ----------
    static void MakeClips()
    {
        const int rate = 44100;
        var rng = new System.Random(11);

        // CREAK: a slow, scratchy, wobbling squeak (stick-slip: lots of tiny clicks)
        creak = Clip("Locker Creak", 0.9f, rate, t =>
        {
            float env = Mathf.Clamp01(t / 0.1f) * Mathf.Clamp01((0.9f - t) / 0.3f);
            float freq = 180f + 60f * Mathf.Sin(t * 7f) + 40f * t;
            float pulse = Mathf.Repeat(freq * t, 1f) < 0.12f ? 1f : -0.15f;   // a train of clicks = a creak
            float grit = ((float)rng.NextDouble() * 2f - 1f) * 0.25f;
            return (pulse * 0.6f + grit) * env * 0.7f;
        });

        // SCREECH: a harsh, high, falling shriek
        screech = Clip("Trickster Screech", 0.7f, rate, t =>
        {
            float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((0.7f - t) / 0.35f);
            float freq = Mathf.Lerp(1100f, 600f, t / 0.7f) + 80f * Mathf.Sin(t * 60f);
            float saw = Mathf.Repeat(freq * t, 1f) * 2f - 1f;
            float saw2 = Mathf.Repeat(freq * 1.51f * t, 1f) * 2f - 1f;          // a clashing second voice
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.4f;
            return (saw * 0.45f + saw2 * 0.3f + noise) * env * 0.8f;
        });
    }

    static AudioClip Clip(string name, float seconds, int rate, System.Func<float, float> sample)
    {
        int n = Mathf.CeilToInt(seconds * rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)rate), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Occupant == null ? Color.green : Color.red;
        Gizmos.DrawWireSphere(ExitPosition + Vector3.up * 0.05f, 0.25f);
    }
}
