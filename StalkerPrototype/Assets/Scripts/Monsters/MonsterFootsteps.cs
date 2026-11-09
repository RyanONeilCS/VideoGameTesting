using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A LOUD monster (the Brute): heavy footsteps you hear from far away, a roar before it charges,
/// and heavy panting while it catches its breath. The sounds are generated in code (placeholders until
/// there are real ones). The HUD also shows each sound as a caption pointing toward where it came from.
/// </summary>
[RequireComponent(typeof(MonsterBrain))]
public class MonsterFootsteps : MonoBehaviour
{
    /// <summary>Every loud monster in the game (the HUD reads this for the captions).</summary>
    public static readonly List<MonsterFootsteps> All = new List<MonsterFootsteps>();

    [Tooltip("How far away you can hear it (metres).")]
    public float hearDistance = 40f;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Seconds between steps when walking slowly / when charging.")]
    public Vector2 stepInterval = new Vector2(0.95f, 0.42f);

    /// <summary>When the last sound happened, and what it was ("THUD", "ROAR", "huff").</summary>
    public float LastSoundTime { get; private set; } = -999f;
    public string LastSound { get; private set; } = "";
    public MonsterBrain Brain { get; private set; }

    AudioSource source;
    static AudioClip thud, roar, huff;
    float nextStep, nextHuff;
    bool wasWindingUp;

    void Awake()
    {
        Brain = GetComponent<MonsterBrain>();
        source = gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 1f;                       // 3D: louder when close, from the right direction
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 4f;
        source.maxDistance = hearDistance;
        source.dopplerLevel = 0f;
        source.playOnAwake = false;
        if (thud == null) MakeClips();
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        if (Brain.profile == null) return;

        // The roar: the moment it starts winding up a charge
        bool windingUp = Brain.IsWindingUp;
        if (windingUp && !wasWindingUp) Play(roar, "ROAR", 1f);
        wasWindingUp = windingUp;

        // Panting while it catches its breath
        if (Brain.IsResting)
        {
            if (Time.time >= nextHuff) { nextHuff = Time.time + 0.75f; Play(huff, "huff", 0.7f); }
            return;
        }

        // Footsteps: faster (and heavier) the faster it moves
        float speed = Brain.CurrentSpeed;
        if (speed < 0.3f) { nextStep = Time.time + 0.15f; return; }
        if (Time.time < nextStep) return;
        float t = Mathf.InverseLerp(1.5f, 5f, speed);
        nextStep = Time.time + Mathf.Lerp(stepInterval.x, stepInterval.y, t);
        Play(thud, "THUD", Mathf.Lerp(0.7f, 1f, t));
    }

    void Play(AudioClip clip, string caption, float loudness)
    {
        source.maxDistance = hearDistance;
        source.pitch = Random.Range(0.92f, 1.06f);
        source.PlayOneShot(clip, volume * loudness);
        LastSoundTime = Time.time;
        LastSound = caption;
    }

    // ---------- Placeholder sounds, made from sine waves and noise ----------
    static void MakeClips()
    {
        const int rate = 44100;
        var rng = new System.Random(7);

        // THUD: a low "boom" sliding down in pitch, with a short gritty click at the start
        thud = Clip("Brute Thud", 0.45f, rate, (t, i) =>
        {
            float freq = Mathf.Lerp(75f, 38f, t / 0.45f);
            float body = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * 9f);
            float click = ((float)rng.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 60f) * 0.5f;
            return (body + click) * 0.9f;
        });

        // ROAR: rough low growl (buzzy wave + noise), swelling then fading
        float smooth = 0f;
        roar = Clip("Brute Roar", 1.3f, rate, (t, i) =>
        {
            float env = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((1.3f - t) / 0.6f);
            float freq = 62f + 10f * Mathf.Sin(t * 9f);
            float buzz = Mathf.Repeat(freq * t, 1f) * 2f - 1f;                  // sawtooth
            smooth = Mathf.Lerp(smooth, (float)rng.NextDouble() * 2f - 1f, 0.08f); // low-passed noise
            float wobble = 0.75f + 0.25f * Mathf.Sin(t * 47f);
            return (buzz * 0.55f + smooth * 1.6f) * env * wobble * 0.8f;
        });

        // HUFF: a breathy burst of noise
        float s2 = 0f;
        huff = Clip("Brute Huff", 0.5f, rate, (t, i) =>
        {
            float env = Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t * 6f);
            s2 = Mathf.Lerp(s2, (float)rng.NextDouble() * 2f - 1f, 0.25f);
            return s2 * env * 0.9f;
        });
    }

    static AudioClip Clip(string name, float seconds, int rate, System.Func<float, int, float> sample)
    {
        int n = Mathf.CeilToInt(seconds * rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(sample(i / (float)rate, i), -1f, 1f);
        var clip = AudioClip.Create(name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
