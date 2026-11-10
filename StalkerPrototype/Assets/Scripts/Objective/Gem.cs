using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A glowing gem to collect (the placeholder objective). Walk into it to pick it up.
/// Monsters know where the gems are, so they can wait near them (see "GUARD THE GEMS" in the profile).
/// </summary>
public class Gem : MonoBehaviour
{
    /// <summary>Every gem still on the map (collected gems remove themselves).</summary>
    public static readonly List<Gem> All = new List<Gem>();

    [Tooltip("How close (metres) a player must get to pick it up.")]
    public float pickupRadius = 1.3f;
    public float spinSpeed = 90f;
    public float bobHeight = 0.15f;

    /// <summary>Where the gem sits (it bobs up and down around this point).</summary>
    public Vector3 Position { get; private set; }

    void Awake() => Position = transform.position;
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Update()
    {
        // Spin and bob, so it's easy to spot
        transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        transform.position = Position + Vector3.up * (Mathf.Sin(Time.time * 2f) * bobHeight);

        if (GameObjective.Instance != null && GameObjective.Instance.Won) return;
        if (!NetRole.IsServer) return;   // multiplayer: only the host decides who picked it up

        // Any player (not hiding) close enough picks it up. Distance check on the ground plane = no physics setup needed.
        foreach (var p in PlayerAIState.All)
        {
            if (p.IsHiding) continue;
            Vector3 d = p.transform.position - Position;
            d.y = 0f;
            if (d.magnitude <= pickupRadius)
            {
                Collect(p);
                return;
            }
        }
    }

    void Collect(PlayerAIState player)
    {
        if (GameObjective.Instance != null) GameObjective.Instance.OnGemCollected(this, player);
        gameObject.SetActive(false);   // gone (and removed from All by OnDisable)
    }
}
