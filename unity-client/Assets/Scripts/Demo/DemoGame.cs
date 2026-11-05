using System.Collections.Generic;
using SimCore;
using UnityEngine;

/// <summary>
/// Demo: a desert map with a small base and four kinds of units you can select and send around.
/// Owns the simulation (SimCore.World) and drives it at a fixed tick rate;
/// everything on screen is just a view of that simulation.
/// </summary>
public sealed partial class DemoGame : MonoBehaviour
{
    public const int MapSize = DemoScenario.MapSize;
    private static readonly Vector2Int StartCenter = new Vector2Int(DemoScenario.StartX, DemoScenario.StartY);
    private const float TickSeconds = 1f / World.TicksPerSecond;

    [Header("Materials")]
    [SerializeField] private Material paletteMaterial;
    [SerializeField] private Material groundMaterial;
    [SerializeField] private Material overlayMaterial;
    [SerializeField] private Material ringMaterial;
    [SerializeField] private Material markerMaterial;

    [Header("Models (index = enum value of UnitKind / BuildingKind)")]
    [SerializeField] private GameObject[] unitModels;
    [SerializeField] private GameObject[] buildingModels;
    [Header("Detailed models for the art style test (same indexes, an empty slot = no detailed version)")]
    [SerializeField] private GameObject[] unitModelsHd;
    [SerializeField] private GameObject[] buildingModelsHd;
    [SerializeField] private GameObject[] rockModels;
    [SerializeField] private GameObject palmModel;
    [SerializeField] private GameObject cactusModel;
    [SerializeField] private GameObject bushModel;

    private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();
    private readonly SortedSet<int> _selected = new SortedSet<int>(); // sorted: commands must list ids in a fixed order
    private float _accumulator;
    private TerrainView _terrain;
    private readonly Dictionary<int, GameObject> _buildingObjects = new Dictionary<int, GameObject>();
    private readonly List<ShotEvent> _shotBuffer = new List<ShotEvent>();
    private readonly List<int> _removeBuffer = new List<int>();
    private Material _enemyPalette;
    private Material _arcMaterial;
    private readonly System.Diagnostics.Stopwatch _stepTimer = new System.Diagnostics.Stopwatch();
    private double _stepMsSum, _stepMsWindowMax;
    private int _stepCount;
    private float _statsWindowStart;

    // Selected building (instead of units), and the markers that show it
    private Building _selectedBuilding;
    private GameObject _buildingHighlight;
    private GameObject _rallyMarker;
    private Material _highlightMaterial;

    // Choosing where to build
    private BuildGhost _ghost;
    private BuildingKind _placingKind;
    private bool _overlayWasVisible;

    /// <summary>Draw the detailed models (the shipped look); the simple ones stay as a fallback for slow phones and tests.</summary>
    public bool DetailedModels { get; private set; } = true;

    public World World { get; private set; }
    public RtsCamera Cam { get; private set; }
    public TouchController Touch { get; private set; }
    public int SelectedCount => _selected.Count;
    public bool IsSelected(int unitId) => _selected.Contains(unitId);
    public string SelfTestStatus { get; private set; }
    public bool BuildOverlayVisible => _terrain != null && _terrain.OverlayVisible;

    public Building SelectedBuilding => _selectedBuilding;
    /// <summary>True after "Toplanma nöqtəsi" was pressed: the next tap on the map sets the rally point.</summary>
    public bool SettingRally { get; private set; }

    public bool Placing => _ghost != null;
    public BuildingKind PlacingKind => _placingKind;
    public Vector2Int PlacingCell { get; private set; }
    public BuildCheck PlacingCheck { get; private set; }

    /// <summary>Average and worst time of one simulation tick over the last few seconds (milliseconds). For the performance test.</summary>
    public float SimMsAverage { get; private set; }
    public float SimMsWorst { get; private set; }

    public enum GameOutcome { Playing, Victory, Defeat }

    /// <summary>What the screen is doing: the main menu, the old sandbox scenario (development builds), a match against the computer, or an online match.</summary>
    public enum GameMode { Menu, Demo, Skirmish, Online, Campaign }

    public GameMode Mode { get; private set; } = GameMode.Menu;
    /// <summary>A match against the computer or a campaign mission: pausable, ends with a result card.</summary>
    public bool IsOfflineMatch => Mode == GameMode.Skirmish || Mode == GameMode.Campaign;

    /// <summary>The campaign mission being played (null otherwise) and what the player has finished so far.</summary>
    public MissionRun Mission { get; private set; }
    public CampaignProgress Progress { get; private set; }
    /// <summary>The player's permanent unit upgrades, bought with campaign credits.</summary>
    public Armory Armory { get; private set; }
    private const string ArmoryKey = "armory.levels";
    /// <summary>The daily login reward and daily tasks (local clock for now).</summary>
    public DailyProgress Daily { get; private set; }
    public PlayerStats Stats { get; private set; } = new PlayerStats();
    private const string DailyKey = "daily.progress";
    private const string StatsKey = "player.stats";
    /// <summary>The first mission's guided hints: 0..3 are the steps, -1 means off (already done, skipped or another mode).</summary>
    public int TutorialStep { get; private set; } = -1;
    private bool _tutorialDone;
    private int _ordersIssued;
    private const string TutorialKey = "tutorial.done";
    public const int TutorialLastStep = 3;
    public static int Today => (int)(System.DateTime.Now.Date - new System.DateTime(1970, 1, 1)).TotalDays;
    /// <summary>Credits the mission just finished paid out (0 when lost or nothing new was earned).</summary>
    public int MissionPaid { get; private set; }
    private bool _missionRecorded;
    private const string ProgressKey = "campaign.progress";
    public bool Paused { get; set; }
    public AiDifficulty Difficulty { get; private set; } = AiDifficulty.Medium;
    private AiPlayer _ai;
    private readonly List<Command> _aiBuffer = new List<Command>();

    /// <summary>Counters for the end-of-match screen (local player's units built and lost, enemy units destroyed).</summary>
    public int UnitsBuilt { get; private set; }
    public int UnitsLost { get; private set; }
    public int EnemiesDestroyed { get; private set; }
    private bool _countStats;
    public float MatchSeconds => World == null ? 0f : World.Tick / (float)SimCore.World.TicksPerSecond;

    /// <summary>Victory when nothing of the enemy is left; defeat when the player's base is destroyed.</summary>
    public GameOutcome Outcome { get; private set; }
    public int EnemyCount => World == null ? 0 : World.CountAlive(OpponentTeam);

    /// <summary>The team this screen plays (always 0 against the computer; 0 or 1 online).</summary>
    public int LocalTeam { get; private set; } = SimCore.World.TeamPlayer;
    public int OpponentTeam => 1 - LocalTeam;

    /// <summary>Set while an online connection exists (lobby or match).</summary>
    public OnlineSession Online { get; private set; }
    /// <summary>True from the moment the online match starts: the world then moves only through the lockstep engine.</summary>
    public bool OnlineMatch { get; private set; }
    private Vector2Int _home = StartCenter;

    /// <summary>1 = real time. Higher values fast-forward the simulation (for testing).</summary>
    public float SimSpeed { get; set; } = 1f;

    private void Start()
    {
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep; // a match against the computer can go minutes without a touch

        bool ok = SimCoreSelfTest.Run(out string failure);
        SelfTestStatus = ok ? "SimCore: OK" : "SimCore FAIL: " + failure;
        Debug.Log("[Demo] " + SelfTestStatus);

        _enemyPalette = MakeEnemyPalette();
        _arcMaterial = new Material(overlayMaterial) { color = new Color(0.4f, 0.85f, 1f, 0.22f) };
        Fx.Init(markerMaterial);
        Sfx.Init(transform);

        Account = gameObject.AddComponent<AccountService>();
        LoadProgress();
        Cam = Camera.main.GetComponent<RtsCamera>();
        LoadWorld(DemoScenario.Create(), StartCenter, 0); // frozen backdrop behind the main menu
        Touch = new TouchController(Cam, this);
        if (Account.SignedIn) SyncProfile();
    }

    // ---- matches ----

    /// <summary>A match against the computer on the fair mirrored map. The player is team 0 (bottom-left).</summary>
    public void StartSkirmish(AiDifficulty difficulty)
    {
        LeaveOnline();
        OnlineMatch = false;
        ulong seed = (ulong)System.DateTime.UtcNow.Ticks;
        _countStats = false;
        World skirmish = SkirmishScenario.Create(seed);
        Armory.Apply(skirmish, 0);
        LoadWorld(skirmish, new Vector2Int(8, 8), 0);
        Difficulty = difficulty;
        _ai = new AiPlayer(OpponentTeam, difficulty, seed);
        Mode = GameMode.Skirmish;
        BeginPlay();
    }

    /// <summary>The old sandbox (a squad against guards). Development builds only; also the place from which the debug panel starts online matches.</summary>
    public void StartDemo()
    {
        LeaveOnline();
        OnlineMatch = false;
        _countStats = false;
        LoadWorld(DemoScenario.Create(), StartCenter, 0);
        _ai = null;
        Mode = GameMode.Demo;
        BeginPlay();
    }

    /// <summary>Starts a campaign mission. The player is team 0; the mission runs the opponent and the scripted waves.</summary>
    public void StartMission(int number)
    {
        LeaveOnline();
        OnlineMatch = false;
        _countStats = false;
        Mission = new MissionRun(SimCore.Campaign.Get(number), (ulong)System.DateTime.UtcNow.Ticks);
        _missionRecorded = false;
        MissionPaid = 0;
        Armory.Apply(Mission.World, 0);
        _ordersIssued = 0;
        TutorialStep = number == 1 && !_tutorialDone ? 0 : -1;
        Building hq = null;
        foreach (Building b in Mission.World.Buildings) if (b.Team == 0 && b.Kind == BuildingKind.Hq) hq = b;
        LoadWorld(Mission.World, hq != null ? new Vector2Int(hq.X, hq.Y) : new Vector2Int(8, 8), 0);
        _ai = null;
        Mode = GameMode.Campaign;
        BeginPlay();
    }

    private void LoadProgress()
    {
        string text = null;
        try { text = PlayerPrefs.GetString(ProgressKey, ""); } catch (System.Exception) { }
        Progress = CampaignProgress.Parse(text);
        string levels = null;
        try { levels = PlayerPrefs.GetString(ArmoryKey, ""); } catch (System.Exception) { }
        Armory = Armory.Parse(levels);
        string daily = null;
        try { daily = PlayerPrefs.GetString(DailyKey, ""); } catch (System.Exception) { }
        Daily = DailyProgress.Parse(daily);
        string stats = null;
        try { stats = PlayerPrefs.GetString(StatsKey, ""); } catch (System.Exception) { }
        Stats = PlayerStats.Parse(stats);
        try { _tutorialDone = PlayerPrefs.GetInt(TutorialKey, 0) != 0; } catch (System.Exception) { }
    }

    public void FinishTutorial()
    {
        TutorialStep = -1;
        _tutorialDone = true;
        try { PlayerPrefs.SetInt(TutorialKey, 1); PlayerPrefs.Save(); } catch (System.Exception) { }
        ProfileDirty = true;
    }

    // Moves the hints along as the player does what the current one asks.
    private void UpdateTutorial()
    {
        if (TutorialStep < 0) return;
        if (Mode != GameMode.Campaign || Outcome == GameOutcome.Defeat) { TutorialStep = -1; return; }
        if (Outcome == GameOutcome.Victory) { FinishTutorial(); return; }
        if (TutorialStep == 0 && SelectedCount > 0) TutorialStep = 1;
        else if (TutorialStep == 1 && _ordersIssued > 0) TutorialStep = 2;
        else if (TutorialStep == 2 && UnitsBuilt > 0) TutorialStep = 3;
    }

    private void SaveProgress()
    {
        ProfileDirty = true;
        try
        {
            PlayerPrefs.SetString(ProgressKey, Progress.Serialize());
            PlayerPrefs.SetString(ArmoryKey, Armory.Serialize());
            PlayerPrefs.SetString(DailyKey, Daily.Serialize());
            PlayerPrefs.SetString(StatsKey, Stats.Serialize());
            PlayerPrefs.Save();
        }
        catch (System.Exception) { }
    }

    /// <summary>Buys the next armory level of a unit kind with campaign credits.</summary>
    public bool BuyArmoryLevel(UnitKind kind)
    {
        if (!Armory.Buy(kind, Progress)) return false;
        SaveProgress();
        Sfx.Play("place", 0.8f, 0f);
        return true;
    }

    public void ClaimDailyReward()
    {
        if (Daily.ClaimReward(Today, Progress) == 0) return;
        SaveProgress();
        Sfx.Play("place", 0.8f, 0f);
    }

    public void ClaimDailyTask(int task)
    {
        if (Daily.ClaimTask(Today, task, Progress) == 0) return;
        SaveProgress();
        Sfx.Play("place", 0.8f, 0f);
    }

    // A finished match against the computer counts towards the daily tasks (giving up in the first minute does not).
    private void RecordDaily()
    {
        bool won = Outcome == GameOutcome.Victory;
        bool counted = won || MatchSeconds >= 60f;
        if (counted) Daily.Report(Today, DailyTaskKind.PlayMatches);
        Stats.RecordMatch(counted, won, won && Mode == GameMode.Campaign, UnitsBuilt, EnemiesDestroyed);
        if (won && Mode == GameMode.Campaign) Daily.Report(Today, DailyTaskKind.WinMission);
        else if (won && Mode == GameMode.Skirmish) Daily.Report(Today, DailyTaskKind.WinSkirmish);
        SaveProgress();
    }

    private void BeginPlay()
    {
        Sfx.SetMusicVolume(0.2f);
        Paused = false;
        SimSpeed = 1f;
        _accumulator = 0f;
        UnitsBuilt = UnitsLost = EnemiesDestroyed = 0;
        _countStats = true;
    }

    public void ReturnToMenu()
    {
        LeaveOnline();
        OnlineMatch = false;
        _ai = null;
        Mission = null;
        Paused = false;
        CancelPlacement();
        ClearSelection();
        Mode = GameMode.Menu;
        Sfx.SetMusicVolume(0.28f);
    }

    public void Surrender()
    {
        if (IsOfflineMatch && Outcome == GameOutcome.Playing) Outcome = GameOutcome.Defeat;
        Paused = false;
    }

    /// <summary>Shows a (new) world: terrain, views and camera. The old world, if any, is thrown away.</summary>
    private void LoadWorld(World world, Vector2Int home, int localTeam)
    {
        CancelPlacement();
        ClearSelection();
        foreach (UnitView v in _views.Values) Destroy(v.gameObject);
        _views.Clear();
        foreach (GameObject go in _buildingObjects.Values) Destroy(go);
        _buildingObjects.Clear();
        if (_terrain != null) Destroy(_terrain.gameObject);

        World = world;
        LocalTeam = localTeam;
        _home = home;
        Outcome = GameOutcome.Playing;
        _outcomeSoundPlayed = false;

        _terrain = new GameObject("Terrain").AddComponent<TerrainView>();
        _terrain.Build(World.Map, groundMaterial, overlayMaterial, rockModels, palmModel, cactusModel, bushModel, paletteMaterial, home, 8);
        SyncBuildingViews();
        SyncUnitViews();
        Cam.Init(new Vector3(home.x, 0f, home.y + 2f), MapSize);
    }

    // ---- online ----

    public void HostOnline(string address) => StartOnline(OnlineSession.Host(address, Armory.ToBytes(), Account.Token ?? ""));
    public void JoinOnline(string address, string code) => StartOnline(OnlineSession.Join(address, code, Armory.ToBytes(), Account.Token ?? ""));

    private void StartOnline(OnlineSession session)
    {
        LeaveOnline();
        Online = session;
    }

    /// <summary>Closes the connection. A match in progress stays on screen frozen; use the debug panel to start something new.</summary>
    public void LeaveOnline()
    {
        Online?.Leave();
        Online = null;
    }

    /// <summary>Builds the starting world of an online match (also after a reconnect: the server then replays everything played so far).</summary>
    private void BeginOnlineMatch(int team, byte[][] kits, System.Action<World> attach)
    {
        World world = OnlineScenario.Create();
        for (int t = 0; t < kits.Length; t++) Armory.FromBytes(kits[t]).Apply(world, t); // both players' bonuses, as the server announced them
        Vec2 hq = Vec2.Zero;
        foreach (Building b in world.Buildings)
            if (b.Kind == BuildingKind.Hq && b.Team == team) hq = new Vec2(Fix64.FromInt(b.X), Fix64.FromInt(b.Y));
        var home = new Vector2Int(hq.X.ToInt(), hq.Y.ToInt());
        _countStats = false;
        LoadWorld(world, home, team);
        attach(world);
        OnlineMatch = true;
        Mode = GameMode.Online;
        _ai = null;
        BeginPlay();
    }

    /// <summary>Every command goes through here. Online it is sent to the server and played when both players have it.</summary>
    private void Issue(Command command)
    {
        _ordersIssued++;
        if (OnlineMatch)
        {
            // While reconnecting or replaying the screen shows an old situation: orders given now would make no sense.
            if (Online?.Client != null && !Online.Reconnecting && !Online.Client.CatchingUp) Online.Client.QueueCommand(command);
        }
        else World.Enqueue(command);
    }

    /// <summary>Keeps one model per building: creates missing ones, removes those whose building was destroyed.</summary>
    private void SyncBuildingViews()
    {
        bool changed = false;
        foreach (Building b in World.Buildings)
        {
            if (_buildingObjects.ContainsKey(b.Id)) continue;
            _buildingObjects[b.Id] = SpawnBuildingView(b);
            changed = true;
        }

        if (_buildingObjects.Count != World.Buildings.Count)
        {
            _removeBuffer.Clear();
            foreach (int id in _buildingObjects.Keys)
                if (World.GetBuilding(id) == null) _removeBuffer.Add(id);
            foreach (int id in _removeBuffer)
            {
                Fx.Explosion(_buildingObjects[id].transform.position, 5f);
                Sfx.PlayAt(_buildingObjects[id].transform.position, "explosion_big", 1f, 0.15f);
                Destroy(_buildingObjects[id]);
                _buildingObjects.Remove(id);
                changed = true;
            }
        }
        if (changed && _terrain.OverlayVisible) _terrain.RefreshOverlay();
    }

    /// <summary>Keeps one view per unit: creates missing ones, removes (with a small explosion) those that died.</summary>
    private void SyncUnitViews()
    {
        if (_views.Count != World.Units.Count)
        {
            foreach (Unit unit in World.Units)
                if (!_views.ContainsKey(unit.Id))
                {
                    _views[unit.Id] = UnitView.Create(unit, UnitModel(unit.Kind),
                        unit.Team != LocalTeam ? _enemyPalette : paletteMaterial, ringMaterial, _arcMaterial,
                        IsBorrowed(unit.Kind) ? BorrowedScale(unit.Kind) : Vector3.one);
                    if (_countStats && unit.Team == LocalTeam) { UnitsBuilt++; Sfx.Play("ready", 0.5f, 0.03f, 0.3f); }
                }

            _removeBuffer.Clear();
            foreach (int id in _views.Keys)
                if (World.GetUnit(id) == null) _removeBuffer.Add(id);
            foreach (int id in _removeBuffer)
            {
                Fx.Explosion(_views[id].transform.position, 1.6f);
                Sfx.PlayAt(_views[id].transform.position, "explosion_small", 0.6f, 0.06f);
                if (_countStats) { if (_views[id].Team == LocalTeam) UnitsLost++; else EnemiesDestroyed++; }
                Destroy(_views[id].gameObject);
                _views.Remove(id);
                _selected.Remove(id);
            }
        }
    }

    // Placeholder looks until the new kinds get their own models (art phase): they borrow a similar model.
    private static UnitKind ModelKind(UnitKind kind) =>
        kind == UnitKind.Sniper ? UnitKind.Rifleman : kind == UnitKind.ArmoredCar ? UnitKind.Jeep : kind;

    private static BuildingKind ModelKind(BuildingKind kind) =>
        kind == BuildingKind.RepairStation ? BuildingKind.SupplyCenter : kind == BuildingKind.Tower ? BuildingKind.PowerPlant : kind;

    private static bool Has(GameObject[] models, int i) => models != null && i < models.Length && models[i] != null;

    private GameObject UnitModel(UnitKind kind)
    {
        int i = (int)kind;
        if (DetailedModels && Has(unitModelsHd, i)) return unitModelsHd[i];
        if (!Has(unitModels, i)) i = (int)ModelKind(kind);
        return unitModels[i];
    }

    /// <summary>True when the kind is drawn with another kind's model (no model of its own in the current style).</summary>
    private bool IsBorrowed(UnitKind kind) =>
        !(DetailedModels && Has(unitModelsHd, (int)kind)) && !Has(unitModels, (int)kind);

    private bool IsBorrowed(BuildingKind kind) =>
        !(DetailedModels && Has(buildingModelsHd, (int)kind)) && !Has(buildingModels, (int)kind);

    private GameObject BuildingModel(BuildingKind kind)
    {
        int i = (int)kind;
        if (DetailedModels && Has(buildingModelsHd, i)) return buildingModelsHd[i];
        if (!Has(buildingModels, i)) i = (int)ModelKind(kind);
        return buildingModels[i];
    }

    /// <summary>Size tweak for a model borrowed from another kind, so the two can be told apart.</summary>
    private static Vector3 BorrowedScale(UnitKind kind) => kind == UnitKind.ArmoredCar ? new Vector3(1.2f, 1.2f, 1.2f) : Vector3.one;

    private static Vector3 BorrowedScale(BuildingKind kind) => kind == BuildingKind.Tower ? new Vector3(0.8f, 1.8f, 0.8f) : Vector3.one;

    /// <summary>Switches between simple and detailed models: every unit and building view is rebuilt (no explosions, selection is cleared).</summary>
    public void SetDetailedModels(bool on)
    {
        if (DetailedModels == on) return;
        DetailedModels = on;
        bool counting = _countStats;
        _countStats = false; // rebuilding the views is not units being built
        foreach (UnitView v in _views.Values) Destroy(v.gameObject);
        _views.Clear();
        foreach (GameObject go in _buildingObjects.Values) Destroy(go);
        _buildingObjects.Clear();
        ClearSelection();
        SyncBuildingViews();
        SyncUnitViews();
        _countStats = counting;
    }

    private GameObject SpawnBuildingView(Building b)
    {
        GameObject go = Instantiate(BuildingModel(b.Kind), transform);
        go.name = "Building " + b.Kind;
        if (IsBorrowed(b.Kind)) go.transform.localScale = BorrowedScale(b.Kind);
        // The model is centred on its footprint.
        go.transform.position = new Vector3(b.X + b.Width / 2f, 0f, b.Y + b.Height / 2f);
        Material mat = b.Team != LocalTeam ? _enemyPalette : paletteMaterial;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        BuildingFx.Attach(go, b);
        return go;
    }

    /// <summary>The shared palette with its blues turned red, so the enemy is easy to tell apart.</summary>
    private Material MakeEnemyPalette()
    {
        var source = (Texture2D)paletteMaterial.GetTexture("_BaseMap");

        // Copy through a render texture: works whether or not the imported texture is marked readable.
        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(source, rt);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
        copy.Apply();
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        var pixels = copy.GetPixels32();
        Destroy(copy);
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 c = pixels[i];
            if (c.b > c.r + 25 && c.b > c.g + 5) pixels[i] = new Color32(c.b, c.g, c.r, c.a);
        }
        var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(pixels);
        tex.Apply();

        var mat = new Material(paletteMaterial);
        mat.SetTexture("_BaseMap", tex);
        mat.mainTexture = tex;
        return mat;
    }

    private void Update()
    {
        TickProfileSync();
        if (Online != null)
        {
            // Also at the menu: a room created from the Online page starts the match from here.
            NetMessage begin = Online.Pump();
            if (begin is MatchStart start) BeginOnlineMatch(start.Team, start.Kits, w => Online.AttachWorld(w));
            else if (begin is MatchResume resume) BeginOnlineMatch(resume.Team, resume.Kits, w => Online.AttachResume(w, resume));
        }
        if (Mode == GameMode.Menu) return; // the world is only a backdrop
        if (!Paused) Touch.Update();

        bool catchingUp = OnlineMatch && Online?.Client != null && Online.Client.CatchingUp;
        if (catchingUp)
        {
            // Replaying the match after a reconnect: play as fast as the phone can for a few milliseconds a frame.
            _stepTimer.Restart();
            while (_stepTimer.Elapsed.TotalMilliseconds < 10.0 && Online.Client.CatchingUp && Online.Client.TryStep()) { }
            _accumulator = 0f;
        }

        // A match against the computer stands still while paused and once it is decided.
        bool frozen = IsOfflineMatch && (Paused || Outcome != GameOutcome.Playing);
        if (!frozen) _accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.1f) * (OnlineMatch ? 1f : SimSpeed);
        int steps = 0;
        while (!catchingUp && !frozen && _accumulator >= TickSeconds && steps < 5)
        {
            _stepTimer.Restart();
            if (OnlineMatch)
            {
                if (Online?.Client == null || !Online.Client.TryStep())
                {
                    // Waiting for the other player (or the match is over): do not save up time to rush through later.
                    _accumulator = Mathf.Min(_accumulator, TickSeconds * 2f);
                    break;
                }
            }
            else if (Mode == GameMode.Campaign)
            {
                Mission.Advance(); // the opponent, the waves and the objectives all move with the world
            }
            else
            {
                if (_ai != null)
                {
                    _aiBuffer.Clear();
                    _ai.Update(World, _aiBuffer);
                    foreach (Command c in _aiBuffer) World.Enqueue(c, _ai.Team);
                }
                World.Step();
            }
            double ms = _stepTimer.Elapsed.TotalMilliseconds;
            _stepMsSum += ms;
            _stepCount++;
            if (ms > _stepMsWindowMax) _stepMsWindowMax = ms;
            _accumulator -= TickSeconds;
            steps++;
        }
        if (Time.unscaledTime - _statsWindowStart >= 3f)
        {
            SimMsAverage = _stepCount == 0 ? 0f : (float)(_stepMsSum / _stepCount);
            SimMsWorst = (float)_stepMsWindowMax;
            _stepMsSum = _stepMsWindowMax = 0;
            _stepCount = 0;
            _statsWindowStart = Time.unscaledTime;
        }
        if (steps == 5) _accumulator = 0f; // device too slow: drop the backlog instead of spiralling

        SyncBuildingViews();
        SyncUnitViews();
        if (_selectedBuilding != null && World.GetBuilding(_selectedBuilding.Id) == null) _selectedBuilding = null;
        UpdateGhost();
        UpdateSelectionMarkers();
        UpdateOutcome();
        UpdateTutorial();
        if (Outcome != GameOutcome.Playing && !_outcomeSoundPlayed && (IsOfflineMatch || OnlineMatch))
        {
            _outcomeSoundPlayed = true;
            Sfx.Play(Outcome == GameOutcome.Victory ? "victory" : "defeat", 0.9f, 0f);
            RecordDaily();
        }

        // Bullet tracers for the shots fired in the ticks just simulated.
        _shotBuffer.Clear();
        World.DrainShots(_shotBuffer);
        foreach (ShotEvent shot in _shotBuffer)
        {
            if (catchingUp) continue;
            bool heavyShot = shot.Kind == UnitKind.RocketTrooper || shot.Kind == UnitKind.Tank;
            Fx.MuzzleFlash(UnitView.ToUnity(shot.From), UnitView.ToUnity(shot.To), heavyShot);
            PlayShotSound(shot);
            Fx.Tracer(UnitView.ToUnity(shot.From), UnitView.ToUnity(shot.To), heavyShot, shot.Team != LocalTeam);
        }

        float alpha = _accumulator / TickSeconds;
        foreach (UnitView view in _views.Values)
        {
            view.Refresh(alpha, _selected.Contains(view.UnitId));
        }
    }

    private static void PlayShotSound(ShotEvent shot)
    {
        Vector3 at = UnitView.ToUnity(shot.From);
        switch (shot.Kind)
        {
            case UnitKind.Tank: Sfx.PlayAt(at, "cannon", 0.8f, 0.08f); break;
            case UnitKind.RocketTrooper: Sfx.PlayAt(at, "rocket", 0.7f, 0.1f); break;
            case UnitKind.Sniper: Sfx.PlayAt(at, "rifle", 0.9f, 0.1f); break;
            default: Sfx.PlayAt(at, "rifle", 0.5f, 0.05f); break;
        }
    }

    private bool _outcomeSoundPlayed;

    private void UpdateOutcome()
    {
        if (Outcome != GameOutcome.Playing) return;
        if (Mode == GameMode.Campaign)
        {
            if (Mission.Status == MissionStatus.Running) return;
            Outcome = Mission.Status == MissionStatus.Won ? GameOutcome.Victory : GameOutcome.Defeat;
            RecordMission();
            return;
        }
        if (!World.HasBuilding(BuildingKind.Hq, LocalTeam)) Outcome = GameOutcome.Defeat;
        else if (OnlineMatch || Mode == GameMode.Skirmish ? !World.HasBuilding(BuildingKind.Hq, OpponentTeam) : EnemyCount == 0) Outcome = GameOutcome.Victory;
    }

    /// <summary>Development builds: destroys the enemy headquarters so the victory path can be tested without playing the whole mission.</summary>
    /// <summary>Development builds: credits for testing the armory.</summary>
    public void DebugAddCredits(int amount)
    {
        Progress.Add(amount);
        SaveProgress();
    }

    public void DebugWinMission()
    {
        if (Mode != GameMode.Campaign || Outcome != GameOutcome.Playing) return;
        foreach (Building b in World.Buildings)
            if (b.Team == 1 && b.Kind == BuildingKind.Hq) World.SetBuildingHealth(b.Id, 0);
    }

    private void RecordMission()
    {
        if (_missionRecorded) return;
        _missionRecorded = true;
        MissionPaid = Progress.Record(Mission.Mission.Number, Mission.Stars);
        SaveProgress();
    }

    // ---- player actions (called by TouchController and the HUD) ----

    public void Tap(Vector2 screen)
    {
        if (Placing)
        {
            if (Cam.GroundPoint(screen, out Vector3 spot)) MovePlacement(spot);
            return;
        }

        if (SettingRally)
        {
            if (Cam.GroundPoint(screen, out Vector3 rally))
            {
                Issue(new SetRallyCommand(_selectedBuilding.Id, ToVec2(rally)));
                TapMarker.Spawn(rally, markerMaterial);
            }
            SettingRally = false;
            return;
        }

        int unitId = PickUnit(screen, LocalTeam);

        // Tapping an enemy with units selected = attack order.
        if (unitId == -1 && _selected.Count > 0)
        {
            int enemyId = PickUnit(screen, OpponentTeam);
            if (enemyId != -1) { OrderAttack(AttackKind.Unit, enemyId, World.GetUnit(enemyId).Pos); return; }

            if (PickBuilding(screen) is Building target && target.Team == OpponentTeam)
            {
                OrderAttack(AttackKind.Building, target.Id, new Vec2(Fix64.FromInt(target.X + target.Width / 2), Fix64.FromInt(target.Y + target.Height / 2)));
                return;
            }
        }

        if (unitId == -1)
        {
            Building hit = PickBuilding(screen);
            if (hit != null && hit.Team == LocalTeam)
            {
                _selected.Clear();
                _selectedBuilding = hit;
                return;
            }
            // Tapping empty ground with only a building selected just deselects it.
            if (_selectedBuilding != null && _selected.Count == 0)
            {
                _selectedBuilding = null;
                return;
            }
        }

        if (unitId != -1)
        {
            _selectedBuilding = null;
            // Tapping the only selected unit again deselects it.
            if (_selected.Count == 1 && _selected.Contains(unitId))
            {
                _selected.Clear();
            }
            else
            {
                _selected.Clear();
                _selected.Add(unitId);
            }
            return;
        }

        if (_selected.Count > 0 && Cam.GroundPoint(screen, out Vector3 ground))
            OrderMove(ground);
    }

    public void SelectInRect(Rect screenRect)
    {
        _selectedBuilding = null;
        _selected.Clear();
        foreach (UnitView view in _views.Values)
        {
            if (view.Team != LocalTeam) continue;
            Vector3 sp = Cam.Camera.WorldToScreenPoint(view.transform.position);
            if (sp.z > 0f && screenRect.Contains(sp)) _selected.Add(view.UnitId);
        }
    }

    public void SelectAll()
    {
        _selectedBuilding = null;
        _selected.Clear();
        foreach (UnitView view in _views.Values)
            if (view.Team == LocalTeam) _selected.Add(view.UnitId);
    }

    public void ClearSelection()
    {
        _selected.Clear();
        _selectedBuilding = null;
        SettingRally = false;
    }

    public void ToggleBuildOverlay() => _terrain.SetOverlayVisible(!_terrain.OverlayVisible);

    // ---- performance test ----

    /// <summary>
    /// Puts <paramref name="perSide"/> player units and as many enemy guards face to face in open sand
    /// in the south-west, so they fight at once. Debug only: it bypasses the command queue.
    /// </summary>
    public void SpawnBattleTest(int perSide)
    {
        if (OnlineMatch) return; // would change this phone's world only: instant desync
        UnitKind[] kinds = { UnitKind.Rifleman, UnitKind.Rifleman, UnitKind.RocketTrooper, UnitKind.Jeep, UnitKind.Tank };
        const int columns = 12;
        for (int i = 0; i < perSide; i++)
        {
            UnitKind kind = kinds[i % kinds.Length];
            Fix64 x = Fix64.FromInt(5 + i % columns) + Fix64.Half;
            Fix64 row = Fix64.FromInt(i / columns);
            World.SpawnUnit(kind, new Vec2(x, Fix64.FromInt(8) + row) + new Vec2(Fix64.Zero, Fix64.Half));
            Unit enemy = World.SpawnUnit(kind, new Vec2(x, Fix64.FromInt(15) + row) + new Vec2(Fix64.Zero, Fix64.Half), SimCore.World.TeamEnemy);
            World.SetGuard(enemy);
        }
        if (Cam != null) Cam.FocusOn(new Vector3(11f, 0f, 14f));
    }

    // ---- production ----

    public void QueueUnit(UnitKind kind)
    {
        if (_selectedBuilding != null) Issue(new QueueUnitCommand(_selectedBuilding.Id, kind));
    }

    /// <summary>Removes the last unit in the selected building's queue (it is refunded).</summary>
    public void CancelLastQueued()
    {
        if (_selectedBuilding != null && _selectedBuilding.Queue.Count > 0)
            Issue(new CancelQueuedCommand(_selectedBuilding.Id, _selectedBuilding.Queue.Count - 1));
    }

    /// <summary>Stops building the selected building that is still under construction (part of the cost is refunded).</summary>
    public void CancelConstruction()
    {
        if (_selectedBuilding != null && _selectedBuilding.UnderConstruction) Issue(new CancelBuildCommand(_selectedBuilding.Id));
    }

    public void UpgradeBase() => Issue(new UpgradeBaseCommand());

    public void Research(UpgradeKind kind) => Issue(new ResearchCommand(kind));

    public void BeginSetRally() => SettingRally = _selectedBuilding != null;

    /// <summary>
    /// The building under a screen point. Uses the building's 3D volume, not its footprint on the ground:
    /// the camera looks down at an angle, so a tap on a roof lands on the ground behind the building.
    /// </summary>
    private Building PickBuilding(Vector2 screen)
    {
        Ray ray = Cam.Camera.ScreenPointToRay(screen);
        Building best = null;
        float bestDistance = float.MaxValue;
        foreach (Building b in World.Buildings)
        {
            const float height = 3.2f;
            var bounds = new Bounds(new Vector3(b.X + b.Width / 2f, height / 2f, b.Y + b.Height / 2f), new Vector3(b.Width, height, b.Height));
            if (bounds.IntersectRay(ray, out float distance) && distance < bestDistance)
            {
                bestDistance = distance;
                best = b;
            }
        }
        return best;
    }

    /// <summary>Yellow footprint under the selected building, and a ring where its units gather.</summary>
    private void UpdateSelectionMarkers()
    {
        if (_highlightMaterial == null)
            _highlightMaterial = new Material(overlayMaterial) { color = new Color(1f, 0.9f, 0.2f, 0.45f) };

        if (_selectedBuilding == null || Placing)
        {
            if (_buildingHighlight != null) _buildingHighlight.SetActive(false);
            if (_rallyMarker != null) _rallyMarker.SetActive(false);
            return;
        }

        Building b = _selectedBuilding;
        if (_buildingHighlight == null)
            _buildingHighlight = MeshFactory.Spawn("BuildingHighlight", MeshFactory.Quad(1f, 1f), _highlightMaterial);
        _buildingHighlight.SetActive(true);
        _buildingHighlight.transform.position = new Vector3(b.X - 0.25f, 0.05f, b.Y - 0.25f);
        _buildingHighlight.transform.localScale = new Vector3(b.Width + 0.5f, 1f, b.Height + 0.5f);

        if (_rallyMarker == null)
            _rallyMarker = MeshFactory.Spawn("RallyMarker", MeshFactory.Ring(0.35f, 0.55f), markerMaterial);
        _rallyMarker.SetActive(b.HasRally);
        if (b.HasRally) _rallyMarker.transform.position = new Vector3(b.Rally.X.ToFloat(), 0.07f, b.Rally.Y.ToFloat());
    }

    // ---- building ----

    public void BeginPlacement(BuildingKind kind)
    {
        CancelPlacement();
        _placingKind = kind;
        _overlayWasVisible = _terrain.OverlayVisible;
        _terrain.SetOverlayVisible(true);
        _ghost = new BuildGhost(kind, BuildingModel(kind), paletteMaterial, overlayMaterial);

        // Start in the middle of the screen.
        if (!Cam.GroundPoint(new Vector2(Screen.width / 2f, Screen.height / 2f), out Vector3 centre))
            centre = new Vector3(_home.x, 0f, _home.y);
        MovePlacement(centre);
    }

    public void ConfirmPlacement()
    {
        if (!Placing || PlacingCheck != BuildCheck.Ok) return;
        Issue(new BuildCommand(_placingKind, PlacingCell.x, PlacingCell.y));
        Sfx.Play("place", 0.8f, 0f);
        EndPlacement();
    }

    public void CancelPlacement() => EndPlacement();

    private void EndPlacement()
    {
        if (_ghost == null) return;
        _ghost.Destroy();
        _ghost = null;
        _terrain.SetOverlayVisible(_overlayWasVisible);
    }

    /// <summary>Puts the building's centre on the tapped point, snapped to whole cells and kept inside the map.</summary>
    private void MovePlacement(Vector3 ground)
    {
        BuildingDef def = Defs.Building(_placingKind);
        int x = Mathf.Clamp(Mathf.RoundToInt(ground.x - def.Width / 2f), 0, MapSize - def.Width);
        int y = Mathf.Clamp(Mathf.RoundToInt(ground.z - def.Height / 2f), 0, MapSize - def.Height);
        PlacingCell = new Vector2Int(x, y);
    }

    private void UpdateGhost()
    {
        if (!Placing) return;
        PlacingCheck = World.CheckBuild(_placingKind, PlacingCell.x, PlacingCell.y, LocalTeam);
        _ghost.SetCell(PlacingCell.x, PlacingCell.y, PlacingCheck == BuildCheck.Ok);
    }

    // ---- internals ----

    private void OrderMove(Vector3 ground)
    {
        // Quantise the input to 1/16 of a map unit so the command is a clean fixed-point value.
        Vec2 target = ToVec2(ground);

        var ids = new int[_selected.Count];
        _selected.CopyTo(ids);
        Issue(new MoveCommand(ids, target));

        TapMarker.Spawn(ground, markerMaterial);
    }

    private void OrderAttack(AttackKind kind, int targetId, Vec2 targetPos)
    {
        var ids = new int[_selected.Count];
        _selected.CopyTo(ids);
        Issue(new AttackCommand(ids, kind, targetId));
        TapMarker.Spawn(UnitView.ToUnity(targetPos), markerMaterial);
    }

    private static Vec2 ToVec2(Vector3 ground) => new Vec2(
        Fix64.FromFraction(Mathf.RoundToInt(ground.x * 16f), 16),
        Fix64.FromFraction(Mathf.RoundToInt(ground.z * 16f), 16));

    private int PickUnit(Vector2 screen, int team)
    {
        float bestSqr = Mathf.Pow(Screen.height * 0.07f, 2f);
        int best = -1;
        foreach (UnitView view in _views.Values)
        {
            if (view.Team != team) continue;
            Vector3 sp = Cam.Camera.WorldToScreenPoint(view.transform.position + Vector3.up * 0.5f);
            if (sp.z <= 0f) continue;
            float d = ((Vector2)sp - screen).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = view.UnitId; }
        }
        return best;
    }
}

/// <summary>A ring that appears where you ordered a move, then shrinks away.</summary>
public sealed class TapMarker : MonoBehaviour
{
    private const float Lifetime = 0.6f;
    private static Mesh _mesh;
    private float _age;

    public static void Spawn(Vector3 position, Material material)
    {
        if (_mesh == null) _mesh = MeshFactory.Ring(0.5f, 0.7f);
        GameObject go = MeshFactory.Spawn("TapMarker", _mesh, material);
        go.transform.position = new Vector3(position.x, 0.07f, position.z);
        go.AddComponent<TapMarker>();
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = _age / Lifetime;
        if (t >= 1f) { Destroy(gameObject); return; }
        float size = Mathf.Lerp(1.6f, 0.5f, t);
        transform.localScale = new Vector3(size, 1f, size);
    }
}
