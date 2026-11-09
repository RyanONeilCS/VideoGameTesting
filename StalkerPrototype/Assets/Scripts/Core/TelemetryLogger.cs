using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Records what happens while you play, as CSV files you can open in Python/pandas.
/// Files go in:  StalkerPrototype/Telemetry/
///   session_*.csv : a snapshot of every monster + its target, several times a second
///   events_*.csv  : one row every time a monster changes state (and every gem pickup)
/// Analyse them with Tools/analyze_telemetry.py
/// </summary>
public class TelemetryLogger : MonoBehaviour
{
    [Tooltip("Seconds between snapshots.")]
    public float interval = 0.25f;

    StreamWriter snapshots, events;
    float nextSnapshot;
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;   // always "1.5", never "1,5"

    void OnEnable()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Telemetry"));
        Directory.CreateDirectory(folder);
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        snapshots = new StreamWriter(Path.Combine(folder, $"session_{stamp}.csv"));
        snapshots.WriteLine("time,monster,state,fake_hunt,monster_x,monster_z,player_x,player_z,look_x,look_z," +
                            "move_state,noise,distance,monster_sees_player,player_sees_monster,boldness,fake_hunt_chance,times_caught");

        events = new StreamWriter(Path.Combine(folder, $"events_{stamp}.csv"));
        events.WriteLine("time,monster,from_state,to_state,distance");

        MonsterBrain.StateChanged += OnStateChanged;
        GameObjective.GemCollected += OnGemCollected;
        Debug.Log($"Telemetry: logging to {folder}");
    }

    void OnDisable()
    {
        MonsterBrain.StateChanged -= OnStateChanged;
        GameObjective.GemCollected -= OnGemCollected;
        snapshots?.Close();
        events?.Close();
        snapshots = null;
        events = null;
    }

    void OnStateChanged(MonsterBrain m, MonsterState from, MonsterState to)
    {
        events?.WriteLine(string.Join(",", F(Time.time), Q(m.name), from, to, F(m.DistanceToTarget)));
    }

    // Gem pickups go in the events file too: monster = "(player)", from_state = GemCollected, to_state = "2/5"
    void OnGemCollected(Gem gem, PlayerAIState player)
    {
        var goal = GameObjective.Instance;
        string progress = goal != null ? $"{goal.Collected}/{goal.Needed}" : "";
        events?.WriteLine(string.Join(",", F(Time.time), Q("(player)"), "GemCollected", progress, ""));
    }

    void Update()
    {
        if (snapshots == null || Time.time < nextSnapshot) return;
        nextSnapshot = Time.time + interval;

        foreach (var m in MonsterBrain.All)
        {
            var p = m.Target;
            if (p == null) continue;
            snapshots.WriteLine(string.Join(",",
                F(Time.time), Q(m.name), m.State, m.IsFakeHunt ? 1 : 0,
                F(m.transform.position.x), F(m.transform.position.z),
                F(p.transform.position.x), F(p.transform.position.z),
                F(p.LookDirection.x), F(p.LookDirection.z),
                p.moveState, F(p.noiseLevel), F(m.DistanceToTarget),
                m.CanSeeTarget ? 1 : 0, m.IsWatched ? 1 : 0,
                F(m.Boldness.current), F(m.FakeHuntChance.current), p.timesCaught));
        }
    }

    static string F(float v) => float.IsInfinity(v) || float.IsNaN(v) ? "" : v.ToString("0.###", Invariant);
    static string Q(string s) => "\"" + s.Replace("\"", "'") + "\"";
}
