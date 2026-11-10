using UnityEngine;

/// <summary>
/// Where a monster starts. When the match starts, the HOST spawns this monster here (it's a prefab in
/// Assets/Prefabs, so its settings are on the prefab and its Monster Profile).
/// Untick this object's checkbox (top of the Inspector) to leave that monster out of the match.
/// </summary>
public class MonsterSpawnPoint : MonoBehaviour
{
    [Tooltip("The monster prefab to spawn here (Assets/Prefabs).")]
    public GameObject prefab;
}
