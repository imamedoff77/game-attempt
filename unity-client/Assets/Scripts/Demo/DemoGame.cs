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
    public const int MapSize = 64;
    private const ulong MapSeed = 2026;
    private static readonly Vector2Int StartCenter = new Vector2Int(26, 26);
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
    private int _buildingViews;

    // Choosing where to build
    private BuildGhost _ghost;
    private BuildingKind _placingKind;
    private bool _overlayWasVisible;

    public World World { get; private set; }
    public RtsCamera Cam { get; private set; }
    public TouchController Touch { get; private set; }
    public int SelectedCount => _selected.Count;
    public string SelfTestStatus { get; private set; }
    public bool BuildOverlayVisible => _terrain != null && _terrain.OverlayVisible;

    public bool Placing => _ghost != null;
    public BuildingKind PlacingKind => _placingKind;
    public Vector2Int PlacingCell { get; private set; }
    public BuildCheck PlacingCheck { get; private set; }

    /// <summary>1 = real time. Higher values fast-forward the simulation (for testing).</summary>
    public float SimSpeed { get; set; } = 1f;

    private void Start()
    {
        Application.targetFrameRate = 60;

        bool ok = SimCoreSelfTest.Run(out string failure);
        SelfTestStatus = ok ? "SimCore: OK" : "SimCore FAIL: " + failure;
        Debug.Log("[Demo] " + SelfTestStatus);

        MapGrid map = DemoMap.Generate(MapSize, MapSize, MapSeed, StartCenter.x, StartCenter.y, 6);
        World = new World(map);
        World.PlaceBuilding(BuildingKind.Hq, 22, 21); // everything else the player builds

        _terrain = new GameObject("Terrain").AddComponent<TerrainView>();
        _terrain.Build(map, groundMaterial, overlayMaterial, rockModels, palmModel, cactusModel, bushModel, paletteMaterial, StartCenter, 8);
        SyncBuildingViews();

        SpawnStartingUnits();

        Cam = Camera.main.GetComponent<RtsCamera>();
        Cam.Init(new Vector3(StartCenter.x, 0f, StartCenter.y + 2f), MapSize);
        Touch = new TouchController(Cam, this);
    }

    /// <summary>Creates a model for every building the simulation has that has no model yet.</summary>
    private void SyncBuildingViews()
    {
        if (_buildingViews == World.Buildings.Count) return;
        while (_buildingViews < World.Buildings.Count)
            SpawnBuildingView(World.Buildings[_buildingViews++]);
        if (_terrain.OverlayVisible) _terrain.RefreshOverlay();
    }

    private void SpawnBuildingView(Building b)
    {
        GameObject go = Instantiate(buildingModels[(int)b.Kind], transform);
        go.name = "Building " + b.Kind;
        // The model is centred on its footprint.
        go.transform.position = new Vector3(b.X + b.Width / 2f, 0f, b.Y + b.Height / 2f);
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = paletteMaterial;
    }

    private void SpawnStartingUnits()
    {
        // Three of each kind: one column per kind, south of the base (inside the cleared start area).
        UnitKind[] order = { UnitKind.Rifleman, UnitKind.RocketTrooper, UnitKind.Jeep, UnitKind.Tank };
        for (int col = 0; col < order.Length; col++)
        {
            for (int i = 0; i < 3; i++)
            {
                var pos = new Vec2(Fix64.FromInt(23 + col * 2) + Fix64.Half, Fix64.FromInt(30 + i) + Fix64.Half);
                Unit unit = World.SpawnUnit(order[col], pos);
                _views[unit.Id] = UnitView.Create(unit, unitModels[(int)unit.Kind], paletteMaterial, ringMaterial);
            }
        }
    }

    private void Update()
    {
        Touch.Update();

        _accumulator += Mathf.Min(Time.unscaledDeltaTime, 0.1f) * SimSpeed;
        int steps = 0;
        while (_accumulator >= TickSeconds && steps < 5)
        {
            World.Step();
            _accumulator -= TickSeconds;
            steps++;
        }
        if (steps == 5) _accumulator = 0f; // device too slow: drop the backlog instead of spiralling

        SyncBuildingViews();
        UpdateGhost();

        float alpha = _accumulator / TickSeconds;
        foreach (UnitView view in _views.Values)
            view.Refresh(alpha, _selected.Contains(view.UnitId));
    }

    // ---- player actions (called by TouchController and the HUD) ----

    public void Tap(Vector2 screen)
    {
        if (Placing)
        {
            if (Cam.GroundPoint(screen, out Vector3 spot)) MovePlacement(spot);
            return;
        }

        int unitId = PickUnit(screen);
        if (unitId != -1)
        {
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
        _selected.Clear();
        foreach (UnitView view in _views.Values)
        {
            Vector3 sp = Cam.Camera.WorldToScreenPoint(view.transform.position);
            if (sp.z > 0f && screenRect.Contains(sp)) _selected.Add(view.UnitId);
        }
    }

    public void SelectAll()
    {
        _selected.Clear();
        foreach (int id in _views.Keys) _selected.Add(id);
    }

    public void ClearSelection() => _selected.Clear();

    public void ToggleBuildOverlay() => _terrain.SetOverlayVisible(!_terrain.OverlayVisible);

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
        var target = new Vec2(
            Fix64.FromFraction(Mathf.RoundToInt(ground.x * 16f), 16),
            Fix64.FromFraction(Mathf.RoundToInt(ground.z * 16f), 16));

        var ids = new int[_selected.Count];
        _selected.CopyTo(ids);
        World.Enqueue(new MoveCommand(ids, target));

        TapMarker.Spawn(ground, markerMaterial);
    }

    private int PickUnit(Vector2 screen)
    {
        float bestSqr = Mathf.Pow(Screen.height * 0.07f, 2f);
        int best = -1;
        foreach (UnitView view in _views.Values)
        {
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
