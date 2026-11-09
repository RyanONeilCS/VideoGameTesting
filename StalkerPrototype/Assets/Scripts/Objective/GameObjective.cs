using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The win condition (a placeholder until the real objective / armory): collect the gems.
/// Counts gems, announces each pickup (monsters listen!), and handles winning + restarting.
/// There is one of these in the scene, on the "Objective" object.
/// </summary>
public class GameObjective : MonoBehaviour
{
    /// <summary>The objective in the current scene (null if there isn't one).</summary>
    public static GameObjective Instance { get; private set; }

    /// <summary>Fires when a player picks up a gem: (gem, player). Monsters use this to "hear" the pickup.</summary>
    public static event System.Action<Gem, PlayerAIState> GemCollected;

    [Tooltip("Collect this many gems to win. 0 = all the gems on the map.")]
    public int gemsToWin = 0;

    public int Collected { get; private set; }
    public int Needed { get; private set; }
    public bool Won { get; private set; }
    public float LastCollectTime { get; private set; } = -999f;
    /// <summary>Seconds since the match started (stops counting once you win).</summary>
    public float MatchTime => (Won ? wonAt : Time.time) - startTime;

    float startTime, wonAt;

    void Awake() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        // Gems add themselves to Gem.All in OnEnable, which runs before any Start, so they're all counted here.
        int onMap = Gem.All.Count;
        Needed = gemsToWin > 0 ? Mathf.Min(gemsToWin, onMap) : onMap;
        startTime = Time.time;
    }

    public void OnGemCollected(Gem gem, PlayerAIState player)
    {
        if (Won) return;
        Collected++;
        LastCollectTime = Time.time;
        GemCollected?.Invoke(gem, player);

        if (Needed > 0 && Collected >= Needed)
        {
            Won = true;
            wonAt = Time.time;
            Debug.Log($"Stalker: YOU WIN! {Collected} gems in {MatchTime:0} s.");
        }
    }

    void Update()
    {
        // After winning, R reloads the scene for a fresh match.
        if (Won && InputHelper.RestartPressed())
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
