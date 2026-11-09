using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Builds the NavMesh (the "walkable area" map monsters use to find paths)
/// the moment the game starts. A randomly generated map has to do this at runtime,
/// so we practise it now.
///
/// [DefaultExecutionOrder(-1000)] makes this run BEFORE the monsters' scripts.
/// </summary>
[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(NavMeshSurface))]
public class NavMeshBootstrap : MonoBehaviour
{
    void Awake()
    {
        GetComponent<NavMeshSurface>().BuildNavMesh();
    }
}
