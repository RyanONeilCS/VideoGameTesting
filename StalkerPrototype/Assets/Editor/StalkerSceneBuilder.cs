using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// Builds the test scene (rooms + corridors + lockers + player + monsters) so you don't have to set it up by hand.
/// - Runs automatically the first time you open the project.
/// - Menu: Stalker > Rebuild Test Maze ... to rebuild it any time.
/// Editor scripts like this only run inside the Unity editor, never in the game.
/// You don't need to read this file to use the project.
/// </summary>
[InitializeOnLoad]
public static class StalkerSceneBuilder
{
    const string ScenePath = "Assets/Scenes/TestMaze.unity";
    const int GridW = 52, GridH = 52;          // map size in tiles
    const float Tile = 2f, WallHeight = 9f;    // metres (map = 104 x 104 m). TALL walls: you feel small
    const int TargetRooms = 14;
    const int DefaultSeed = 12345;

    const int GemCount = 5;

    // The Creep Horror Creature asset (free, Asset Store). DELETED from the project (2026-11-09): monsters are plain capsules.
    // To use it again: re-import it from the Asset Store, set UseCreepModel = true, and rebuild.
    const string CreepFolder = "Assets/Creep Horror Creature";
    const string CreepFbx = CreepFolder + "/Meshes/Creep_mesh.fbx";
    const string StalkerModel = CreepFolder + "/Prefabs/Creep1.prefab";
    const string StatueModel = CreepFolder + "/Prefabs/Creep3.prefab";
    const string CreepControllerPath = "Assets/Monsters/CreepAnimator.controller";
    const float MonsterHeight = 3f;     // monster height (capsule and models): much bigger than you (1.8 m)
    const float BruteHeight = 3.8f, BruteWidth = 1.7f;   // the Brute: taller, and a much WIDER pill (Trickster: 3 x 1.1)
    const float ModelYaw = 0f;          // if the model walks sideways/backwards, change this (90, 180, -90) and rebuild
    // The Creep model is OFF for now (the red pill is easier to read while we work on behaviour).
    // Set to true and rebuild to try it again; MonsterAnimator only uses idle / walk / attack.
    static readonly bool UseCreepModel = false;

    const int SceneVersion = 10;   // bump when the generator changes -> offers a rebuild
    const int ProfileVersion = 16;  // bump when default monster settings change -> refreshes profiles

    static string VersionKey => "Stalker.SceneVersion." + Application.dataPath;

    static StalkerSceneBuilder()
    {
        EditorApplication.delayCall += BuildOnOpen;
    }

    static void BuildOnOpen()
    {
        if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!File.Exists(ScenePath)) { Build(DefaultSeed); return; }

        // Update monster profiles if the default settings changed (keeps your tweaks otherwise)
        if (AssetDatabase.IsValidFolder("Assets/Monsters"))
        {
            Profile(StalkerPath, SetupStalker);
            Profile(StatuePath, SetupStatue);
            Profile(BrutePath, SetupBrute);
            AssetDatabase.SaveAssets();
        }

        if (EditorPrefs.GetInt(VersionKey, 1) < SceneVersion)
        {
            EditorPrefs.SetInt(VersionKey, SceneVersion);   // only ask once
            if (EditorUtility.DisplayDialog("Stalker: the test map was updated",
                    "NEW MONSTER: the Brute (big, loud, charges when it sees you, then tires out).\n" +
                    "Also: darker lighting and film grain.\n\n" +
                    "Rebuild the test scene now?",
                    "Rebuild", "Later (use the Stalker menu)"))
                Build(DefaultSeed);
        }
    }

    [MenuItem("Stalker/Rebuild Test Maze (same layout)")]
    static void RebuildSame() => ConfirmAndBuild(DefaultSeed);

    [MenuItem("Stalker/Rebuild Test Maze (new random layout)")]
    static void RebuildRandom() => ConfirmAndBuild(System.Environment.TickCount);

    static int pendingSeed;

    static void ConfirmAndBuild(int seed)
    {
        // Scenes can't be rebuilt while the game is running: stop Play mode first, then rebuild.
        if (EditorApplication.isPlaying)
        {
            pendingSeed = seed;
            EditorApplication.playModeStateChanged += BuildAfterPlayMode;
            EditorApplication.ExitPlaymode();
            Debug.Log("Stalker: stopping Play mode first, then rebuilding the map...");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild the test maze?",
                "This replaces Assets/Scenes/TestMaze.unity (anything you changed in that scene is lost).\n\n" +
                "Your scripts and Monster Profiles are kept.", "Rebuild", "Cancel"))
            return;
        Build(seed);
    }

    // ---------- Default settings for the two test monsters ----------
    const string StalkerPath = "Assets/Monsters/Stalker.asset";
    const string StatuePath = "Assets/Monsters/Statue.asset";
    const string BrutePath = "Assets/Monsters/Brute.asset";

    static void SetupStalker(MonsterProfile p)
    {
        p.displayName = "The Stalker";
        p.wanderStyle = WanderStyle.DriftTowardPlayers;
        p.followStyle = FollowStyle.StalkFromBehind;
        p.spottedReactions = new SpottedReactionWeights { stareThenHide = 0.9f, freeze = 0f, flee = 0.08f, vanish = 0f, chase = 0.02f };   // no Freeze: stand-and-stare covers it
        p.canHide = true;
    }

    // THE BRUTE: big, loud, slow, dumb. Sees you -> roars -> charges -> runs out of steam -> rests.
    // Almost every trick is switched off: no ring, no teleports, no peeking, no stalking, no hiding.
    static void SetupBrute(MonsterProfile p)
    {
        p.displayName = "The Brute";
        p.wanderStyle = WanderStyle.DriftTowardPlayers;   // roams, drifting roughly your way now and then
        p.driftChance = 0.5f;
        p.driftFuzziness = 20f;
        p.wanderSpeed = 2f;
        p.wanderPause = new Vector2(2f, 4f);
        p.followStyle = FollowStyle.None;                  // never stalks: sees you = charges
        p.stalkSpeed = 2.6f;                               // (used while it looks around after losing you)

        // Tricks: all off
        p.useRing = false;
        p.canTeleport = false;
        p.teleportFlash = false;
        p.sneakyMovement = false;
        p.alwaysTracksPlayers = false;                     // it only knows where you are if it sees or hears you
        p.canHide = false;
        p.guardGems = false;
        p.guardGemChance = new BehaviourChance(0f, 0f, 0f, true);
        p.canBait = false;
        p.peekWhenUnseen = false;
        p.leanOutToWatch = false;
        p.standAndStareWhenCaughtInOpen = false;
        p.attackWhenWatchedTooLong = false;                // looking at it doesn't matter: it's coming anyway
        p.teleportAwayWeight = 0f;
        p.baitWeight = 0f;
        p.fakeHuntChance = new BehaviourChance(0f, 0f, 0f, true);
        p.hideAmbushChance = new BehaviourChance(0f, 0f, 0f, true);
        p.realHuntChance = 0f;
        p.boldnessHuntBonus = 0f;
        p.spottedReactions = new SpottedReactionWeights { stareThenHide = 0f, freeze = 0f, flee = 0f, vanish = 0f, chase = 1f };

        // Lockers beat it: it never checks them, and if it SAW you get in, it bangs on the door but rarely opens it
        p.checkHidingSpotsChance = new BehaviourChance(0f, 0f, 0f, true);
        p.bangOnDoorWhenSawHide = true;
        p.bangSeconds = new Vector2(3f, 5f);
        p.openAfterBangingChance = new BehaviourChance(0.25f, 0.25f, 0.25f, true);

        // The charge: faster than walking (3.5), slower than sprinting (6). Big slow wind-ups, wide reach.
        p.huntSpeed = 4.8f;
        p.huntWindupSeconds = 1.1f;     // a long ROAR: your cue to run
        p.huntDuration = 8f;            // then it runs out of steam...
        p.tiresOut = true;
        p.restSeconds = 6f;             // ...and stands there heaving
        p.huntCooldown = 3f;
        p.huntLoseSightSeconds = 3f;
        p.attackRange = 3.8f;
        p.attackWindupSeconds = 0.5f;
        p.lungeSpeed = 9f;
        p.lungeSeconds = 0.4f;
        p.catchDistance = 1.9f;         // it's wide
        p.attackRecoverSeconds = 1.3f;

        // Senses: decent eyes and ears, short memory (it's not clever)
        p.sightRange = 25f;
        p.sightAngle = 120f;
        p.hearingRange = 22f;
        p.memorySeconds = 6f;
        p.loseInterestDistance = 40f;
        p.investigateSeconds = 4f;
        p.learningEnabled = false;      // the Brute doesn't learn
    }

    static void SetupStatue(MonsterProfile p)
    {
        p.displayName = "The Statue";
        p.wanderStyle = WanderStyle.DriftTowardPlayers;
        p.driftChance = 0.8f;
        p.wanderSpeed = 3f;
        p.followStyle = FollowStyle.CreepWhenUnseen;
        p.stalkSpeed = 4.5f;
        p.huntSpeed = 9f;
        p.huntDuration = 10f;
        p.huntCooldown = 6f;
        p.spottedReactions = new SpottedReactionWeights { stareThenHide = 0f, freeze = 1f, flee = 0f, vanish = 0f, chase = 0f };
        p.secondsWatchedBeforeSpotted = 0.05f;
        p.freezeBreakDistance = 0f;   // its rule already decides when it moves
        p.attackWhenWatchedTooLong = false;   // being watched is its whole rule
        p.teleportAwayWeight = 0f;
        p.baitWeight = 0f;
        p.canTeleport = false;         // a statue does not teleport: you always know where it is
        p.sneakyMovement = false;      // its rule already decides when it moves
        p.leanOutToWatch = false;
        p.canBait = false;
        p.standAndStareWhenCaughtInOpen = false;   // it already freezes when seen
        p.canHide = false;
        p.peekWhenUnseen = false;      // it is always creeping up in plain sight anyway
        p.useRing = false;             // its rule (move only when unseen) is its whole loop
        p.alwaysTracksPlayers = false;
        p.realHuntChance = 0f;         // its rule alone decides when it lunges
        p.boldnessHuntBonus = 0f;
        p.checkHidingSpotsChance = new BehaviourChance(0.3f, 0.1f, 0.6f);
        p.sightAngle = 360f;
        p.sightRange = 30f;
        p.memorySeconds = 10f;
        p.fakeHuntChance = new BehaviourChance(0.1f, 0.05f, 0.25f);
        p.adaptability = 0.3f;
    }

    static void BuildAfterPlayMode(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= BuildAfterPlayMode;
        ConfirmAndBuild(pendingSeed);
    }

    // =====================================================================
    static void Build(int seed)
    {
        EnsureFolder("Assets", "Scenes");
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets", "Monsters");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var wallMat = Mat("Wall", new Color(0.36f, 0.34f, 0.31f));
        var floorMat = Mat("Floor", new Color(0.16f, 0.16f, 0.18f));
        var playerMat = Mat("Player", new Color(0.2f, 0.45f, 0.9f));
        var stalkerMat = Mat("Stalker", new Color(0.5f, 0.04f, 0.04f));
        var statueMat = Mat("Statue", new Color(0.78f, 0.77f, 0.73f));
        var bruteMat = Mat("Brute", new Color(0.3f, 0.22f, 0.16f));
        var bruteEyeMat = Mat("BruteEyes", new Color(1f, 0.55f, 0.1f), emissive: true);
        var eyeMat = Mat("Eyes", Color.white, emissive: true);
        var lockerMat = Mat("Locker", new Color(0.22f, 0.3f, 0.26f));
        var doorMat = Mat("LockerDoor", new Color(0.3f, 0.38f, 0.33f));
        var gemMat = Mat("Gem", new Color(0.2f, 0.9f, 1f), emissive: true);

        var stalkerProfile = Profile(StalkerPath, SetupStalker);
        var statueProfile = Profile(StatuePath, SetupStatue);
        var bruteProfile = Profile(BrutePath, SetupBrute);

        // ---------- Atmosphere: very dim moonlight + thick fog (your flashlight does most of the work) ----------
        // Want it darker/brighter? Change these numbers and rebuild (or tweak Window > Rendering > Lighting live).
        var moon = new GameObject("Moonlight").AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.intensity = 0.05f;   // was 0.15 (v19: darker)
        moon.color = new Color(0.6f, 0.65f, 1f);
        moon.shadows = LightShadows.Soft;
        moon.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.03f, 0.03f, 0.04f);   // was 0.07 (v19: darker)
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.005f, 0.005f, 0.008f);
        RenderSettings.fogStartDistance = 3f;
        RenderSettings.fogEndDistance = 28f;    // not lower: he baits from 18-30 m away

        // ---------- Layout: rooms of different sizes + corridors of different widths ----------
        var rng = new System.Random(seed);
        var rooms = new List<RectInt>();
        bool[,] wall = GenerateLayout(rng, rooms);

        var maze = new GameObject("Maze");
        var surface = maze.AddComponent<NavMeshSurface>();
        surface.collectObjects = CollectObjects.Children;   // bake floor, walls and lockers (not players/monsters)
        maze.AddComponent<NavMeshBootstrap>();

        var floor = Cube("Floor", maze.transform, floorMat);
        floor.transform.position = new Vector3(GridW * Tile / 2f, -0.1f, GridH * Tile / 2f);
        floor.transform.localScale = new Vector3(GridW * Tile, 0.2f, GridH * Tile);

        var walls = new GameObject("Walls").transform;
        walls.SetParent(maze.transform);
        MakeNotWalkable(walls.gameObject);   // nobody walks on top of walls (no stranded monsters)
        for (int z = 0; z < GridH; z++)
        {
            int x = 0;
            while (x < GridW)
            {
                if (!wall[x, z]) { x++; continue; }
                int start = x;
                while (x < GridW && wall[x, z]) x++;   // merge a whole row of wall tiles into one long cube
                var c = Cube("Wall", walls, wallMat);
                c.transform.position = new Vector3((start + x) * Tile / 2f, WallHeight / 2f, z * Tile + Tile / 2f);
                c.transform.localScale = new Vector3((x - start) * Tile, WallHeight, Tile);
            }
        }

        // Gem rooms are chosen first, so they can be guaranteed a couple of lockers (for the monster to guard from)
        List<int> gemRooms = PickGemRooms(rooms);

        var spots = new GameObject("HidingSpots").transform;
        spots.SetParent(maze.transform);
        MakeNotWalkable(spots.gameObject);   // ...or on top of lockers
        PlaceHidingSpots(wall, rooms, rng, spots, lockerMat, doorMat, gemRooms);

        // ---------- The objective: gems to collect ----------
        var gems = new GameObject("Gems").transform;   // NOT under Maze, so they don't affect the NavMesh
        foreach (int i in gemRooms)
        {
            RectInt r = rooms[i];
            // Somewhere inside the room, away from the walls (lockers sit on the edge tiles)
            var tile = new Vector2Int(r.x + 1 + rng.Next(Mathf.Max(1, r.width - 2)), r.y + 1 + rng.Next(Mathf.Max(1, r.height - 2)));
            MakeGem(TileCenter(tile), gems, gemMat);
        }
        new GameObject("Objective").AddComponent<GameObjective>();

        // A few dim lamps so some rooms are lit and some are pitch dark
        var lamps = new GameObject("Lamps").transform;
        foreach (var r in rooms)
        {
            if (rng.NextDouble() > 0.2) continue;   // only ~1 room in 5 has a lamp
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.transform.SetParent(lamps);
            lamp.transform.position = RoomCenter(r) + Vector3.up * (WallHeight - 0.5f);   // hanging high up
            lamp.type = LightType.Point;
            lamp.range = Mathf.Max(r.width, r.height) * Tile * 0.7f + WallHeight;
            lamp.intensity = 0.5f;
            lamp.color = new Color(1f, 0.82f, 0.6f);
        }

        // ---------- Player (first room) and monsters (the rooms furthest away) ----------
        MakePlayer(RoomCenter(rooms[0]), playerMat);

        var byDistance = new List<RectInt>(rooms);
        byDistance.Sort((a, b) => Vector3.Distance(RoomCenter(b), RoomCenter(rooms[0]))
                                  .CompareTo(Vector3.Distance(RoomCenter(a), RoomCenter(rooms[0]))));
        var creepController = UseCreepModel ? CreepAnimatorController() : null;   // null = red pill
        MakeMonster("Monster - The Stalker", RoomCenter(byDistance[0]), stalkerProfile, stalkerMat, eyeMat, typeof(MonsterRule),
                    StalkerModel, creepController);
        var statue = MakeMonster("Monster - The Statue", RoomCenter(byDistance[Mathf.Min(1, byDistance.Count - 1)]), statueProfile, statueMat, eyeMat, typeof(StatueRule),
                                 StatueModel, creepController);
        statue.SetActive(false);   // switched OFF to start: tick its checkbox in the Inspector to try it

        // THE BRUTE: taller and much wider than the Trickster, dull orange eyes, LOUD.
        // (Untick its checkbox in the Inspector to test the Trickster on its own, or untick the Stalker to test the Brute alone.)
        var brute = MakeMonster("Monster - The Brute", RoomCenter(byDistance[Mathf.Min(2, byDistance.Count - 1)]), bruteProfile, bruteMat, bruteEyeMat,
                                typeof(MonsterRule), StalkerModel, null, height: BruteHeight, width: BruteWidth);
        brute.AddComponent<MonsterFootsteps>();

        // ---------- Telemetry ----------
        new GameObject("Telemetry").AddComponent<TelemetryLogger>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        EditorPrefs.SetInt(VersionKey, SceneVersion);
        Debug.Log($"Stalker: built test map (seed {seed}, {rooms.Count} rooms). Press Play!");
    }

    // =====================================================================
    // LAYOUT
    // =====================================================================
    static bool[,] GenerateLayout(System.Random rng, List<RectInt> rooms)
    {
        var wall = new bool[GridW, GridH];
        for (int x = 0; x < GridW; x++)
            for (int z = 0; z < GridH; z++)
                wall[x, z] = true;

        // 1) Drop rooms of random sizes where they don't overlap
        for (int attempt = 0; attempt < 500 && rooms.Count < TargetRooms; attempt++)
        {
            int roll = rng.Next(100), w, h;
            if (roll < 40) { w = rng.Next(3, 6); h = rng.Next(3, 6); }        // small:  6-10 m
            else if (roll < 80) { w = rng.Next(6, 9); h = rng.Next(5, 9); }   // medium: 10-16 m
            else { w = rng.Next(9, 14); h = rng.Next(8, 13); }                // big:    16-26 m

            var room = new RectInt(rng.Next(1, GridW - w - 1), rng.Next(1, GridH - h - 1), w, h);
            bool overlaps = false;
            foreach (var other in rooms)
                if (Grow(other, 2).Overlaps(room)) { overlaps = true; break; }
            if (overlaps) continue;

            rooms.Add(room);
            CarveRect(wall, room.x, room.y, room.width, room.height);
        }

        // 2) Connect every room (always join the closest unconnected room), so everything is reachable
        var connected = new List<int> { 0 };
        var remaining = new List<int>();
        for (int i = 1; i < rooms.Count; i++) remaining.Add(i);
        while (remaining.Count > 0)
        {
            int bestA = 0, bestB = remaining[0];
            float bestD = float.MaxValue;
            foreach (int a in connected)
                foreach (int b in remaining)
                {
                    float d = Vector2Int.Distance(Center(rooms[a]), Center(rooms[b]));
                    if (d < bestD) { bestD = d; bestA = a; bestB = b; }
                }
            CarveCorridor(wall, rooms[bestA], rooms[bestB], rng);
            connected.Add(bestB);
            remaining.Remove(bestB);
        }

        // 3) A few extra corridors to nearby rooms, so there are loops (good for stalking and escaping)
        for (int i = 0; i < 6 && rooms.Count > 3; i++)
        {
            int a = rng.Next(rooms.Count);
            var others = new List<int>();
            for (int j = 0; j < rooms.Count; j++) if (j != a) others.Add(j);
            others.Sort((p, q) => Vector2Int.Distance(Center(rooms[a]), Center(rooms[p]))
                                  .CompareTo(Vector2Int.Distance(Center(rooms[a]), Center(rooms[q]))));
            CarveCorridor(wall, rooms[a], rooms[others[rng.Next(1, 3)]], rng);   // 2nd or 3rd nearest
        }
        return wall;
    }

    static void CarveCorridor(bool[,] wall, RectInt a, RectInt b, System.Random rng)
    {
        int roll = rng.Next(100);
        int width = roll < 45 ? 2 : roll < 85 ? 3 : 4;   // 4 m, 6 m, or 8 m (wide). No more 2 m squeezes
        Vector2Int ca = Center(a), cb = Center(b);
        int minX = Mathf.Min(ca.x, cb.x), minZ = Mathf.Min(ca.y, cb.y);
        int lenX = Mathf.Abs(ca.x - cb.x) + width, lenZ = Mathf.Abs(ca.y - cb.y) + width;

        if (rng.Next(2) == 0)   // L-shape: across first, then up/down
        {
            CarveRect(wall, minX, ca.y, lenX, width);
            CarveRect(wall, cb.x, minZ, width, lenZ);
        }
        else                    // or up/down first, then across
        {
            CarveRect(wall, ca.x, minZ, width, lenZ);
            CarveRect(wall, minX, cb.y, lenX, width);
        }
    }

    static void CarveRect(bool[,] wall, int x, int z, int w, int h)
    {
        for (int i = Mathf.Max(1, x); i < Mathf.Min(GridW - 1, x + w); i++)
            for (int j = Mathf.Max(1, z); j < Mathf.Min(GridH - 1, z + h); j++)
                wall[i, j] = false;
    }

    static RectInt Grow(RectInt r, int m) => new RectInt(r.x - m, r.y - m, r.width + 2 * m, r.height + 2 * m);
    static Vector2Int Center(RectInt r) => new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);
    static Vector3 TileCenter(Vector2Int t) => new Vector3(t.x * Tile + Tile / 2f, 0f, t.y * Tile + Tile / 2f);
    static Vector3 RoomCenter(RectInt r) => TileCenter(Center(r));

    // ---------- Gems: spread out, far from the player's start ----------
    static List<int> PickGemRooms(List<RectInt> rooms)
    {
        // "Farthest point" picking: each new gem goes in the room furthest from the start AND from the other gems.
        var chosen = new List<int>();
        var taken = new List<Vector3> { RoomCenter(rooms[0]) };   // the player's start room counts as taken
        for (int g = 0; g < GemCount && chosen.Count < rooms.Count - 1; g++)
        {
            int best = -1;
            float bestDistance = -1f;
            for (int i = 1; i < rooms.Count; i++)
            {
                if (chosen.Contains(i)) continue;
                float nearest = float.MaxValue;
                foreach (var t in taken) nearest = Mathf.Min(nearest, Vector3.Distance(t, RoomCenter(rooms[i])));
                if (nearest > bestDistance) { bestDistance = nearest; best = i; }
            }
            chosen.Add(best);
            taken.Add(RoomCenter(rooms[best]));
        }
        return chosen;
    }

    static void MakeGem(Vector3 floorPosition, Transform parent, Material mat)
    {
        var root = new GameObject("Gem");
        root.transform.SetParent(parent);
        root.transform.position = floorPosition + Vector3.up * 1f;

        var stone = Primitive(PrimitiveType.Cube, "Stone", root.transform, mat);   // a cube on its corner = a gem
        stone.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        stone.transform.localScale = Vector3.one * 0.35f;

        var glow = new GameObject("Glow").AddComponent<Light>();   // so you can spot it in the dark
        glow.transform.SetParent(root.transform, false);
        glow.type = LightType.Point;
        glow.range = 5f;
        glow.intensity = 1.5f;
        glow.color = new Color(0.3f, 0.9f, 1f);

        root.AddComponent<Gem>();
    }

    // ---------- Lockers along room walls (not blocking doorways) ----------
    static void PlaceHidingSpots(bool[,] wall, List<RectInt> rooms, System.Random rng, Transform parent, Material lockerMat, Material doorMat, List<int> gemRooms)
    {
        var used = new HashSet<Vector2Int>();
        for (int i = 0; i < rooms.Count; i++)
        {
            var r = rooms[i];
            int count = r.width * r.height >= 60 ? rng.Next(2, 4) : rng.Next(0, 3);
            if (gemRooms.Contains(i)) count = Mathf.Max(count, 2);   // gem rooms always have lockers to lurk in
            for (int placed = 0, tries = 0; placed < count && tries < 40; tries++)
            {
                Vector2Int tile, outward;
                switch (rng.Next(4))
                {
                    case 0: tile = new Vector2Int(r.x + rng.Next(r.width), r.y); outward = Vector2Int.down; break;
                    case 1: tile = new Vector2Int(r.x + rng.Next(r.width), r.yMax - 1); outward = Vector2Int.up; break;
                    case 2: tile = new Vector2Int(r.x, r.y + rng.Next(r.height)); outward = Vector2Int.left; break;
                    default: tile = new Vector2Int(r.xMax - 1, r.y + rng.Next(r.height)); outward = Vector2Int.right; break;
                }
                if (used.Contains(tile)) continue;

                // Must have a solid wall behind it, and no doorway right beside it (corners)
                Vector2Int behind = tile + outward;
                if (!wall[behind.x, behind.y]) continue;
                var side = new Vector2Int(outward.y, outward.x);
                if (!r.Contains(tile + side) && !wall[(tile + side).x, (tile + side).y]) continue;
                if (!r.Contains(tile - side) && !wall[(tile - side).x, (tile - side).y]) continue;

                used.Add(tile);
                placed++;
                var outDir = new Vector3(outward.x, 0f, outward.y);
                Vector3 position = TileCenter(tile) + outDir * (Tile / 2f - 0.5f);   // back against the wall
                MakeLocker(position, Quaternion.LookRotation(-outDir), parent, lockerMat, doorMat);
            }
        }
    }

    static void MakeLocker(Vector3 position, Quaternion facing, Transform parent, Material lockerMat, Material doorMat)
    {
        var root = new GameObject("Locker (HidingSpot)");
        root.transform.SetParent(parent);
        root.transform.SetPositionAndRotation(position, facing);

        var box = Cube("Box", root.transform, lockerMat);   // solid: has a collider
        box.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        box.transform.localScale = new Vector3(1.1f, 2.3f, 0.9f);

        var door = Primitive(PrimitiveType.Cube, "Door", root.transform, doorMat);   // just looks
        door.transform.localPosition = new Vector3(0f, 1.15f, 0.46f);
        door.transform.localScale = new Vector3(1f, 2.2f, 0.03f);

        root.AddComponent<HidingSpot>();
    }

    // =====================================================================
    // PLAYER & MONSTERS
    // =====================================================================
    static void MakePlayer(Vector3 position, Material mat)
    {
        var player = new GameObject("Player") { tag = "Player" };
        player.transform.position = position;

        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);

        var body = Primitive(PrimitiveType.Capsule, "Body", player.transform, mat);
        body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
        body.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;   // don't block our own view

        var camGO = new GameObject("PlayerCamera") { tag = "MainCamera" };
        camGO.transform.SetParent(player.transform, false);
        camGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        var cam = camGO.AddComponent<Camera>();
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.05f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = RenderSettings.fogColor;
        camGO.AddComponent<AudioListener>();
        var grain = camGO.AddComponent<FilmGrain>();   // grainy film look (G toggles it in play mode)
        grain.shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/FilmGrain.shader");

        var flashlight = new GameObject("Flashlight").AddComponent<Light>();
        flashlight.transform.SetParent(camGO.transform, false);
        flashlight.type = LightType.Spot;
        flashlight.range = 22f;
        flashlight.spotAngle = 55f;
        flashlight.intensity = 2f;
        flashlight.shadows = LightShadows.Soft;

        var ai = player.AddComponent<PlayerAIState>();
        ai.playerCamera = cam;
        player.AddComponent<FirstPersonController>();
        player.AddComponent<PlayerHiding>();
        player.AddComponent<PlayerHUD>();
    }

    static GameObject MakeMonster(string name, Vector3 position, MonsterProfile profile, Material bodyMat, Material eyeMat, System.Type ruleType,
                                  string modelPath, RuntimeAnimatorController controller, float height = MonsterHeight, float width = 1.1f)
    {
        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 180f, 0f));

        var col = root.AddComponent<CapsuleCollider>();
        col.height = height;
        col.radius = width / 2f;
        col.center = new Vector3(0f, height / 2f, 0f);

        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.5f;
        agent.height = height;
        agent.baseOffset = 0f;
        agent.acceleration = 20f;
        agent.angularSpeed = 360f;
        agent.enabled = false;   // MonsterBrain turns it on after the NavMesh is built

        root.AddComponent(ruleType);
        var brain = root.AddComponent<MonsterBrain>();
        brain.profile = profile;
        brain.eyeHeight = height - 0.45f;

        // The look: the Creep model with animations if it's imported, otherwise a capsule with glowing eyes.
        // (The invisible CapsuleCollider above is what players "see" and bump into either way.)
        if (!UseCreepModel || !AddModel(root, modelPath, controller))
        {
            var body = Primitive(PrimitiveType.Capsule, "Body", root.transform, bodyMat);
            body.transform.localPosition = new Vector3(0f, height / 2f, 0f);
            body.transform.localScale = new Vector3(width, height / 2f, width);

            foreach (float side in new[] { -width * 0.18f, width * 0.18f })
            {
                var eye = Primitive(PrimitiveType.Sphere, "Eye", root.transform, eyeMat);
                eye.transform.localPosition = new Vector3(side, height - 0.45f, width / 2f - 0.05f);
                eye.transform.localScale = Vector3.one * 0.16f;
            }
        }
        return root;
    }

    // =====================================================================
    // MONSTER MODEL + ANIMATIONS (the Creep asset)
    // =====================================================================
    static bool AddModel(GameObject root, string prefabPath, RuntimeAnimatorController controller)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null || controller == null) return false;

        var model = Object.Instantiate(prefab);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
        model.transform.localScale = Vector3.one;
        foreach (var c in model.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);   // the root's capsule is enough

        // Scale it to the monster's height and stand it on the floor
        Bounds b = RendererBounds(model);
        if (b.size.y > 0.01f) model.transform.localScale = Vector3.one * (MonsterHeight / b.size.y);
        b = RendererBounds(model);
        model.transform.position += Vector3.up * (root.transform.position.y - b.min.y);

        var animator = model.GetComponentInChildren<Animator>();
        if (animator == null) animator = model.AddComponent<Animator>();
        if (animator.avatar == null)
            animator.avatar = AssetDatabase.LoadAllAssetsAtPath(CreepFbx).OfType<Avatar>().FirstOrDefault();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        root.AddComponent<MonsterAnimator>().animator = animator;
        return true;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>
    /// Makes Assets/Monsters/CreepAnimator.controller: one state per animation in the model, named simply
    /// ("Creep|Walk1_Action" -> "Walk1"). MonsterAnimator switches between them by name.
    /// </summary>
    static RuntimeAnimatorController CreepAnimatorController()
    {
        if (!File.Exists(CreepFbx)) return null;
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(CreepControllerPath);
        if (existing != null) return existing;

        MakeCreepClipsLoop();
        var clips = AssetDatabase.LoadAllAssetsAtPath(CreepFbx).OfType<AnimationClip>()
                                 .Where(c => !c.name.StartsWith("__preview__")).ToList();
        if (clips.Count == 0) return null;

        var controller = AnimatorController.CreateAnimatorControllerAtPath(CreepControllerPath);
        var stateMachine = controller.layers[0].stateMachine;
        foreach (var clip in clips)
        {
            var state = stateMachine.AddState(CleanClipName(clip.name));
            state.motion = clip;
            if (state.name == "Idle1") stateMachine.defaultState = state;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Stalker: made {CreepControllerPath} with {clips.Count} animations.");
        return controller;
    }

    static string CleanClipName(string clipName)
    {
        int bar = clipName.IndexOf('|');
        if (bar >= 0) clipName = clipName.Substring(bar + 1);
        return clipName.Replace("_Action", "");
    }

    /// <summary>The model's animations play once and stop by default. Walks and idles need to loop.</summary>
    static void MakeCreepClipsLoop()
    {
        var importer = AssetImporter.GetAtPath(CreepFbx) as ModelImporter;
        if (importer == null || importer.clipAnimations.Length > 0) return;   // already set up (by us, or by you)

        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            string n = CleanClipName(c.name);
            c.loopTime = !(n == "Bite" || n == "Roar" || n == "JumpIn" || n == "JumpOut" || n == "Death");   // one-shots
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    // =====================================================================
    // SMALL HELPERS
    // =====================================================================
    static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // visual only
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static GameObject Cube(string name, Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);   // keeps its collider
        go.name = name;
        go.transform.SetParent(parent, false);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Material Mat(string name, Color color, bool emissive = false)
    {
        string path = $"Assets/Materials/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        // Copy the default material, so this works with any render pipeline.
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mat = new Material(temp.GetComponent<Renderer>().sharedMaterial) { color = color };
        Object.DestroyImmediate(temp);
        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>Creates the profile, or refreshes it if it was made by an older version of this builder.</summary>
    static MonsterProfile Profile(string path, System.Action<MonsterProfile> setup)
    {
        var existing = AssetDatabase.LoadAssetAtPath<MonsterProfile>(path);
        if (existing != null && existing.builderVersion >= ProfileVersion) return existing;   // keep your tweaks

        var fresh = ScriptableObject.CreateInstance<MonsterProfile>();
        setup(fresh);
        fresh.builderVersion = ProfileVersion;
        if (existing == null)
        {
            AssetDatabase.CreateAsset(fresh, path);
            return fresh;
        }

        fresh.name = existing.name;
        EditorUtility.CopySerialized(fresh, existing);
        EditorUtility.SetDirty(existing);
        Object.DestroyImmediate(fresh);
        Debug.Log($"Stalker: refreshed {path} with the new default settings.");
        return existing;
    }

    static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
    }

    /// <summary>
    /// Everything under this object still BLOCKS walking (it's solid), but its top surface isn't walkable.
    /// Without this, the tops of walls and lockers become little NavMesh islands a monster can get stranded on.
    /// </summary>
    static void MakeNotWalkable(GameObject go)
    {
        var modifier = go.AddComponent<NavMeshModifier>();
        modifier.overrideArea = true;
        modifier.area = NavMesh.GetAreaFromName("Not Walkable");
        modifier.applyToChildren = true;
    }
}