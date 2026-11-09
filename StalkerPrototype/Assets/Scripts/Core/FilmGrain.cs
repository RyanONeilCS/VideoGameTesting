using UnityEngine;

/// <summary>
/// Grainy horror-film look on the player camera: grain, slight desaturation, dark screen edges, a faint flicker.
/// Press G in play mode to switch it on/off (to compare). Tweak the numbers on the camera's FilmGrain component;
/// changes during play mode are lost when you stop, so write down the values you like.
/// (Added to the camera automatically if it's missing, so it works without rebuilding the scene.)
/// </summary>
[RequireComponent(typeof(Camera))]
public class FilmGrain : MonoBehaviour
{
    public bool grainOn = true;
    [Tooltip("How strong the grain is.")]
    [Range(0f, 0.5f)] public float intensity = 0.06f;   // was 0.14 (too noisy)
    [Tooltip("Size of each grain speck in screen pixels. Bigger = coarser, older-looking.")]
    [Range(1f, 4f)] public float grainSize = 1f;
    [Tooltip("How many times per second the grain changes. 24 = old film; 60 = smooth digital noise.")]
    [Range(1f, 60f)] public float grainFps = 24f;
    [Tooltip("0 = full colour, 1 = black and white.")]
    [Range(0f, 1f)] public float desaturate = 0.25f;
    [Tooltip("How dark the edges of the screen get.")]
    [Range(0f, 2f)] public float vignette = 0.9f;
    [Tooltip("Random brightness flicker, like an old projector.")]
    [Range(0f, 0.2f)] public float flicker = 0.02f;

    public Shader shader;

    Material mat;
    float seed, brightness = 1f, nextTick;

    void Update()
    {
        if (InputHelper.GrainTogglePressed()) grainOn = !grainOn;
    }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (!grainOn || !EnsureMaterial()) { Graphics.Blit(src, dst); return; }

        if (Time.unscaledTime >= nextTick)   // new grain "frame"
        {
            nextTick = Time.unscaledTime + 1f / Mathf.Max(1f, grainFps);
            seed = Random.value * 100f;
            brightness = 1f + Random.Range(-flicker, flicker);
        }

        mat.SetFloat("_Intensity", intensity);
        mat.SetFloat("_GrainSize", grainSize);
        mat.SetFloat("_Seed", seed);
        mat.SetFloat("_Desaturate", desaturate);
        mat.SetFloat("_Vignette", vignette);
        mat.SetFloat("_Brightness", brightness);
        Graphics.Blit(src, dst, mat);
    }

    bool EnsureMaterial()
    {
        if (mat != null) return true;
        if (shader == null) shader = Shader.Find("Hidden/Stalker/FilmGrain");
        if (shader == null || !shader.isSupported) return false;
        mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return true;
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AddToPlayerCamera()
    {
        var cam = Camera.main;
        if (cam != null && cam.GetComponent<FilmGrain>() == null) cam.gameObject.AddComponent<FilmGrain>();
    }
}
