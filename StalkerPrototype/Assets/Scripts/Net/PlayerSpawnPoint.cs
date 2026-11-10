using UnityEngine;

/// <summary>
/// Where players start. Players appear in a small circle around this point (one spot each).
/// </summary>
public class PlayerSpawnPoint : MonoBehaviour
{
    public static Vector3 PositionFor(ulong clientId)
    {
        var point = FindAnyObjectByType<PlayerSpawnPoint>();
        Vector3 center = point != null ? point.transform.position : Vector3.zero;
        if (clientId == 0) return center;
        float angle = clientId * 72f;
        return center + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 1.5f;
    }
}

