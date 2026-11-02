using System.Collections.Generic;
using SimCore;
using UnityEngine;

/// <summary>
/// D1 demo: a flat map with a squad of soldiers you can select and send around.
/// Owns the simulation (SimCore.World) and drives it at a fixed tick rate;
/// everything on screen is just a view of that simulation.
/// </summary>
public sealed class DemoGame : MonoBehaviour
{
    public const float MapSize = 64f;
    private const int InitialUnits = 12;
    private const float TickSeconds = 1f / World.TicksPerSecond;

    [SerializeField] private Material groundMaterial;
    [SerializeField] private Material unitMaterial;
    [SerializeField] private Material accentMaterial;
    [SerializeField] private Material ringMaterial;
    [SerializeField] private Material markerMaterial;

    private readonly Dictionary<int, UnitView> _views = new Dictionary<int, UnitView>();
    private readonly SortedSet<int> _selected = new SortedSet<int>(); // sorted: commands must list ids in a fixed order
    private float _accumulator;

    public World World { get; private set; }
    public RtsCamera Cam { get; private set; }
    public TouchController Touch { get; private set; }
    public int SelectedCount => _selected.Count;
    public string SelfTestStatus { get; private set; }

    /// <summary>1 = real time. Higher values fast-forward the simulation (for testing).</summary>
    public float SimSpeed { get; set; } = 1f;

    private void Start()
    {
        Application.targetFrameRate = 60;

        bool ok = SimCoreSelfTest.Run(out string failure);
        SelfTestStatus = ok ? "SimCore: OK" : "SimCore FAIL: " + failure;
        Debug.Log("[Demo] " + SelfTestStatus);

        World = new World(Fix64.FromInt((int)MapSize), Fix64.FromInt((int)MapSize));
        for (int i = 0; i < InitialUnits; i++)
        {
            var pos = new Vec2(
                Fix64.FromInt(24) + Fix64.FromFraction(3, 2) * (i % 4),
                Fix64.FromInt(24) + Fix64.FromFraction(3, 2) * (i / 4));
            Unit unit = World.SpawnUnit(pos, Fix64.FromInt(3));
            _views[unit.Id] = UnitView.Create(unit, unitMaterial, accentMaterial, ringMaterial);
        }

        BuildGround();

        Cam = Camera.main.GetComponent<RtsCamera>();
        Cam.Init(new Vector3(26f, 0f, 26f), MapSize);
        Touch = new TouchController(Cam, this);
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

        float alpha = _accumulator / TickSeconds;
        foreach (UnitView view in _views.Values)
            view.Refresh(alpha, _selected.Contains(view.UnitId));
    }

    // ---- player actions (called by TouchController and the HUD) ----

    public void Tap(Vector2 screen)
    {
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
        float bestSqr = Mathf.Pow(Screen.height * 0.06f, 2f);
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

    private void BuildGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        Destroy(ground.GetComponent<Collider>());
        ground.transform.position = new Vector3(MapSize / 2f, 0f, MapSize / 2f);
        ground.transform.localScale = new Vector3(MapSize / 10f, 1f, MapSize / 10f); // a Plane is 10x10 units

        // 2x2 checker, repeated once per two map units, so movement is easy to see.
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        Color light = new Color(0.36f, 0.45f, 0.30f), dark = new Color(0.31f, 0.40f, 0.26f);
        tex.SetPixels(new[] { light, dark, dark, light });
        tex.Apply();

        var mat = new Material(groundMaterial) { mainTexture = tex, mainTextureScale = new Vector2(MapSize / 2f, MapSize / 2f) };
        ground.GetComponent<Renderer>().sharedMaterial = mat;
    }
}

/// <summary>A ring that appears where you ordered a move, then fades out.</summary>
public sealed class TapMarker : MonoBehaviour
{
    private const float Lifetime = 0.6f;
    private float _age;

    public static void Spawn(Vector3 position, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "TapMarker";
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial = material;
        go.transform.position = new Vector3(position.x, 0.05f, position.z);
        go.AddComponent<TapMarker>();
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = _age / Lifetime;
        if (t >= 1f) { Destroy(gameObject); return; }
        float size = Mathf.Lerp(1.4f, 0.4f, t);
        transform.localScale = new Vector3(size, 0.01f, size);
    }
}
