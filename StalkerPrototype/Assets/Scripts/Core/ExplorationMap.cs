using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Remembers where players have walked, on a rough grid of 6 m squares (a simple "visited" heat map).
/// Monsters use it to ambush where players HAVEN'T been yet: players explore new areas to find the
/// objectives, so a locker in a room they already searched is a wasted ambush.
/// </summary>
public static class ExplorationMap
{
    public const float CellSize = 6f;
    static readonly HashSet<Vector2Int> visited = new HashSet<Vector2Int>();

    public static int VisitedCount => visited.Count;

    static Vector2Int Cell(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));

    /// <summary>Called by every player, every frame.</summary>
    public static void Record(Vector3 playerPosition) => visited.Add(Cell(playerPosition));

    /// <summary>Has any player been within about one square of this point?</summary>
    public static bool IsExplored(Vector3 point)
    {
        Vector2Int c = Cell(point);
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (visited.Contains(new Vector2Int(c.x + dx, c.y + dz))) return true;
        return false;
    }

    // Start fresh every match (static data would otherwise survive a scene reload).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Init()
    {
        visited.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => visited.Clear();
}
