using System.Collections.Generic;
using SimCore;
using UnityEngine;

/// <summary>
/// Demo: a desert map with a small base and four kinds of units you can select and send around.
/// Owns the simulation (SimCore.World) and drives it at a fixed tick rate;
/// everything on screen is just a view of that simulation.
/// </summary>
public sealed class DemoGame : MonoBehaviour
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

    /// <summary>Victory when nothing of the enemy is left; defeat when the player's base is destroyed.</summary>
    public GameOutcome Outcome { get; private set; }
    public int EnemyCount => World == null ? 0 : World.CountAlive(SimCore.World.TeamEnemy);

    /// <summary>1 = real time. Higher values fast-forward the simulation (for testing).</summary>
    public float SimSpeed { get; set; } = 1f;

    private void Start()
    {
        Application.targetFrameRate = 60;

        bool ok = SimCoreSelfTest.Run(out string failure);
        SelfTestStatus = ok ? "SimCore: OK" : "SimCore FAIL: " + failure;
        Debug.Log("[Demo] " + SelfTestStatus);

        World = DemoScenario.Create(); // map, player base and squad, enemy base
        MapGrid map = World.Map;
        _enemyPalette = MakeEnemyPalette();
        Fx.Init(markerMaterial);

        _terrain = new GameObject("Terrain").AddComponent<TerrainView>();
        _terrain.Build(map, groundMaterial, overlayMaterial, rockModels, palmModel, cactusModel, bushModel, paletteMaterial, StartCenter, 8);
        SyncBuildingViews();

        SyncBuildingViews();
        SyncUnitViews();

        Cam = Camera.main.GetComponent<RtsCamera>();
        Cam.Init(new Vector3(StartCenter.x, 0f, StartCenter.y + 2f), MapSize);
        Touch = new TouchController(Cam, this);
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
                    _views[unit.Id] = UnitView.Create(unit, unitModels[(int)unit.Kind],
                        unit.Team == SimCore.World.TeamEnemy ? _enemyPalette : paletteMaterial, ringMaterial);

            _removeBuffer.Clear();
            foreach (int id in _views.Keys)
                if (World.GetUnit(id) == null) _removeBuffer.Add(id);
            foreach (int id in _removeBuffer)
            {
                Fx.Explosion(_views[id].transform.position, 1.6f);
                Destroy(_views[id].gameObject);
                _views.Remove(id);
                _selected.Remove(id);
            }
        }
    }

    private GameObject SpawnBuildingView(Building b)
    {
        GameObject go = Instantiate(buildingModels[(int)b.Kind], transform);
        go.name = "Building " + b.Kind;
        // The model is centred on its footprint.
        go.transform.position = new Vector3(b.X + b.Width / 2f, 0f, b.Y + b.Height / 2f);
        Material mat = b.Team == SimCore.World.TeamEnemy ? _enemyPalette : paletteMaterial;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
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
        Touch.Update();

        _accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.1f) * SimSpeed;
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < 5)
        {
            _stepTimer.Restart();
            World.Step();
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

        // Bullet tracers for the shots fired in the ticks just simulated.
        _shotBuffer.Clear();
        World.DrainShots(_shotBuffer);
        foreach (ShotEvent shot in _shotBuffer)
            Fx.Tracer(UnitView.ToUnity(shot.From), UnitView.ToUnity(shot.To), shot.Kind == UnitKind.RocketTrooper || shot.Kind == UnitKind.Tank, shot.Team == SimCore.World.TeamEnemy);

        float alpha = _accumulator / TickSeconds;
        foreach (UnitView view in _views.Values)
        {
            Unit u = World.GetUnit(view.UnitId);
            bool aiming = u != null && World.Tick - u.LastShotTick < 8;
            view.Refresh(alpha, _selected.Contains(view.UnitId), aiming);
        }
    }

    private void UpdateOutcome()
    {
        if (Outcome != GameOutcome.Playing) return;
        if (!World.HasBuilding(BuildingKind.Hq, SimCore.World.TeamPlayer)) Outcome = GameOutcome.Defeat;
        else if (EnemyCount == 0) Outcome = GameOutcome.Victory;
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
                World.Enqueue(new SetRallyCommand(_selectedBuilding.Id, ToVec2(rally)));
                TapMarker.Spawn(rally, markerMaterial);
            }
            SettingRally = false;
            return;
        }

        int unitId = PickUnit(screen, SimCore.World.TeamPlayer);

        // Tapping an enemy with units selected = attack order.
        if (unitId == -1 && _selected.Count > 0)
        {
            int enemyId = PickUnit(screen, SimCore.World.TeamEnemy);
            if (enemyId != -1) { OrderAttack(AttackKind.Unit, enemyId, World.GetUnit(enemyId).Pos); return; }

            if (PickBuilding(screen) is Building target && target.Team == SimCore.World.TeamEnemy)
            {
                OrderAttack(AttackKind.Building, target.Id, new Vec2(Fix64.FromInt(target.X + target.Width / 2), Fix64.FromInt(target.Y + target.Height / 2)));
                return;
            }
        }

        if (unitId == -1)
        {
            Building hit = PickBuilding(screen);
            if (hit != null && hit.Team == SimCore.World.TeamPlayer)
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
            if (view.Team != SimCore.World.TeamPlayer) continue;
            Vector3 sp = Cam.Camera.WorldToScreenPoint(view.transform.position);
            if (sp.z > 0f && screenRect.Contains(sp)) _selected.Add(view.UnitId);
        }
    }

    public void SelectAll()
    {
        _selectedBuilding = null;
        _selected.Clear();
        foreach (UnitView view in _views.Values)
            if (view.Team == SimCore.World.TeamPlayer) _selected.Add(view.UnitId);
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
        if (_selectedBuilding != null) World.Enqueue(new QueueUnitCommand(_selectedBuilding.Id, kind));
    }

    /// <summary>Removes the last unit in the selected building's queue (it is refunded).</summary>
    public void CancelLastQueued()
    {
        if (_selectedBuilding != null && _selectedBuilding.Queue.Count > 0)
            World.Enqueue(new CancelQueuedCommand(_selectedBuilding.Id, _selectedBuilding.Queue.Count - 1));
    }

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
        _ghost = new BuildGhost(kind, buildingModels[(int)kind], paletteMaterial, overlayMaterial);

        // Start in the middle of the screen.
        if (!Cam.GroundPoint(new Vector2(Screen.width / 2f, Screen.height / 2f), out Vector3 centre))
            centre = new Vector3(StartCenter.x, 0f, StartCenter.y);
        MovePlacement(centre);
    }

    public void ConfirmPlacement()
    {
        if (!Placing || PlacingCheck != BuildCheck.Ok) return;
        World.Enqueue(new BuildCommand(_placingKind, PlacingCell.x, PlacingCell.y));
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
        PlacingCheck = World.CheckBuild(_placingKind, PlacingCell.x, PlacingCell.y);
        _ghost.SetCell(PlacingCell.x, PlacingCell.y, PlacingCheck == BuildCheck.Ok);
    }

    // ---- internals ----

    private void OrderMove(Vector3 ground)
    {
        // Quantise the input to 1/16 of a map unit so the command is a clean fixed-point value.
        Vec2 target = ToVec2(ground);

        var ids = new int[_selected.Count];
        _selected.CopyTo(ids);
        World.Enqueue(new MoveCommand(ids, target));

        TapMarker.Spawn(ground, markerMaterial);
    }

    private void OrderAttack(AttackKind kind, int targetId, Vec2 targetPos)
    {
        var ids = new int[_selected.Count];
        _selected.CopyTo(ids);
        World.Enqueue(new AttackCommand(ids, kind, targetId));
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
