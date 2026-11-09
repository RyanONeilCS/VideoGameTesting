using UnityEngine;

/// <summary>
/// A short flash of light that fades out and removes itself. Used for the Stalker's teleport:
/// a purple flash around the corner where it just was. You don't see it teleport... you see the light.
/// Make one with LightFlash.Spawn(...). No setup needed.
/// </summary>
public class LightFlash : MonoBehaviour
{
    Light flashLight;
    float startIntensity, duration, age;

    public static void Spawn(Vector3 position, Color color, float range, float intensity, float seconds)
    {
        var go = new GameObject("Teleport Flash");
        go.transform.position = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;   // cheap, and it still lights up the walls around the corner

        var flash = go.AddComponent<LightFlash>();
        flash.flashLight = light;
        flash.startIntensity = intensity;
        flash.duration = Mathf.Max(0.05f, seconds);
    }

    void Update()
    {
        age += Time.deltaTime;
        float t = age / duration;
        if (t >= 1f) { Destroy(gameObject); return; }
        flashLight.intensity = startIntensity * (1f - t) * (1f - t);   // bright, then fades fast
    }
}
