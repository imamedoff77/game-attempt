using System.Collections.Generic;
using SimCore;
using UnityEngine;

/// <summary>On-screen buttons and diagnostics (IMGUI: no UI package needed for the demo).</summary>
[RequireComponent(typeof(DemoGame))]
public sealed class DemoHud : MonoBehaviour
{
    private static readonly BuildingKind[] Buildable =
    {
        BuildingKind.PowerPlant, BuildingKind.SupplyCenter, BuildingKind.Barracks, BuildingKind.VehicleFactory,
    };
    private static readonly float[] Speeds = { 1f, 5f, 10f };

    private DemoGame _game;
    private float _smoothedDelta = 1f / 60f;
    private GUIStyle _label;
    private GUIStyle _stats;
    private GUIStyle _button;
    private Texture2D _boltIcon;
    private bool _menuOpen;

    private void Awake()
    {
        _game = GetComponent<DemoGame>();
    }

    private void Update()
    {
        _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);

        // Touch positions have their origin bottom-left, IMGUI rects top-left.
        if (_game.Touch != null)
            _game.Touch.IsOverUi = pos =>
            {
                var gui = new Vector2(pos.x, Screen.height - pos.y);
                foreach (Rect r in ActiveRects())
                    if (r.Contains(gui)) return true;
                return false;
            };
    }

    private void OnGUI()
    {
        if (_game.World == null) return;
        EnsureStyles();
        World world = _game.World;

        DrawStats(world);
        DrawUnpoweredIcons(world);

        // Top-right: build menu button and simulation speed.
        Rect[] top = TopRects();
        GUI.enabled = !_game.Placing;
        if (GUI.Button(top[0], _menuOpen ? "Tikinti (bağla)" : "Tikinti", _button)) _menuOpen = !_menuOpen;
        GUI.enabled = true;
        for (int i = 0; i < Speeds.Length; i++)
        {
            string text = (_game.SimSpeed == Speeds[i] ? "[x" + Speeds[i] + "]" : "x" + Speeds[i]);
            if (GUI.Button(top[i + 1], text, _button)) _game.SimSpeed = Speeds[i];
        }

        if (_menuOpen && !_game.Placing) DrawBuildMenu(world);

        // Bottom-right row
        Rect[] r = BottomRects();
        if (GUI.Button(r[0], _game.BuildOverlayVisible ? "Tikinti sahələri: AÇIQ" : "Tikinti sahələri", _button)) _game.ToggleBuildOverlay();
        if (GUI.Button(r[1], _game.Touch.BoxMode ? "Çərçivə: AÇIQ" : "Çərçivə: BAĞLI", _button)) _game.Touch.BoxMode = !_game.Touch.BoxMode;
        if (_game.Placing)
        {
            GUI.enabled = _game.PlacingCheck == BuildCheck.Ok;
            if (GUI.Button(r[2], "Təsdiq", _button)) _game.ConfirmPlacement();
            GUI.enabled = true;
            if (GUI.Button(r[3], "Ləğv et", _button)) _game.CancelPlacement();
            DrawPlacementHint();
        }
        else
        {
            if (GUI.Button(r[2], "Hamısı", _button)) _game.SelectAll();
            if (GUI.Button(r[3], "Ləğv et", _button)) _game.ClearSelection();
        }

        if (_game.Touch.BoxRect.HasValue)
        {
            Rect b = _game.Touch.BoxRect.Value;
            var gui = new Rect(b.x, Screen.height - b.yMax, b.width, b.height);
            Color old = GUI.color;
            GUI.color = new Color(0.3f, 0.8f, 1f, 0.25f);
            GUI.DrawTexture(gui, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }

    // ---------------------------------------------------------------- drawing

    private void DrawStats(World world)
    {
        float pad = Screen.height * 0.02f;
        float line = Screen.height * 0.055f;

        string resources = $"Resurs {world.Resources}  (+{world.IncomePerMinute}/dəq)";
        string energy = $"Enerji {world.EnergyUsed}/{world.EnergyCapacity}";
        GUI.Label(new Rect(pad, pad, Screen.width * 0.5f, line), resources, _stats);

        GUIStyle energyStyle = _stats;
        if (world.EnergyShortage)
        {
            energyStyle = new GUIStyle(_stats);
            energyStyle.normal.textColor = new Color(1f, 0.4f, 0.35f);
            energy += "  - ÇATMIR!";
        }
        GUI.Label(new Rect(pad, pad + line, Screen.width * 0.5f, line), energy, energyStyle);

        string info = $"FPS {1f / _smoothedDelta:0}   Units {world.Units.Count}   Selected {_game.SelectedCount}   {_game.SelfTestStatus}";
        GUI.Label(new Rect(pad, pad + line * 2f, Screen.width * 0.6f, line), info, _label);
    }

    private void DrawBuildMenu(World world)
    {
        Rect[] items = MenuRects();
        for (int i = 0; i < Buildable.Length; i++)
        {
            BuildingKind kind = Buildable[i];
            BuildingDef def = Defs.Building(kind);
            GUI.enabled = world.Resources >= def.Cost;
            if (GUI.Button(items[i], $"{def.Name}  -  {def.Cost}\n{Describe(world, kind, def)}", _button))
            {
                _menuOpen = false;
                _game.BeginPlacement(kind);
            }
        }
        GUI.enabled = true;
    }

    private static string Describe(World world, BuildingKind kind, BuildingDef def)
    {
        switch (kind)
        {
            case BuildingKind.PowerPlant: return $"+{world.NextOutput(kind)} enerji";
            case BuildingKind.SupplyCenter: return $"-{def.EnergyUse} enerji,  +{world.NextOutput(kind)} resurs/dəq";
            default: return $"-{def.EnergyUse} enerji";
        }
    }

    private void DrawPlacementHint()
    {
        BuildingDef def = Defs.Building(_game.PlacingKind);
        string reason;
        switch (_game.PlacingCheck)
        {
            case BuildCheck.Ok: reason = "Təsdiq düyməsinə basın"; break;
            case BuildCheck.BadGround: reason = "Burada tikmək olmaz (qırmızı sahə)"; break;
            case BuildCheck.UnitInWay: reason = "Vahid yoldadır, onu kənara çəkin"; break;
            case BuildCheck.NotEnoughResources: reason = "Resurs çatmır"; break;
            default: reason = "Tikmək olmaz"; break;
        }
        float pad = Screen.height * 0.02f;
        float y = Screen.height - Screen.height * 0.11f - Screen.height * 0.015f - Screen.height * 0.12f;
        GUI.Box(new Rect(Screen.width - Screen.width * 0.5f - pad, y, Screen.width * 0.5f, Screen.height * 0.1f), "");
        GUI.Label(new Rect(Screen.width - Screen.width * 0.5f - pad, y, Screen.width * 0.5f, Screen.height * 0.1f),
            $"{def.Name}: yeri seçin (xəritəyə toxunun)\n{reason}", _label);
    }

    /// <summary>A lightning icon over every building that is switched off for lack of energy.</summary>
    private void DrawUnpoweredIcons(World world)
    {
        if (_game.Cam == null) return;
        float size = Screen.height * 0.075f;
        foreach (Building b in world.Buildings)
        {
            if (b.Powered) continue;
            Vector3 world3 = new Vector3(b.X + b.Width / 2f, 1.8f, b.Y + b.Height / 2f);
            Vector3 sp = _game.Cam.Camera.WorldToScreenPoint(world3);
            if (sp.z <= 0f) continue;
            GUI.DrawTexture(new Rect(sp.x - size / 2f, Screen.height - sp.y - size / 2f, size, size), BoltIcon());
        }
    }

    // ---------------------------------------------------------------- layout (IMGUI coordinates, origin top-left)

    private IEnumerable<Rect> ActiveRects()
    {
        foreach (Rect r in TopRects()) yield return r;
        foreach (Rect r in BottomRects()) yield return r;
        if (_menuOpen && !_game.Placing)
            foreach (Rect r in MenuRects()) yield return r;
    }

    private static Rect[] BottomRects()
    {
        const int count = 4;
        float h = Screen.height * 0.11f;
        float w = Mathf.Min(Screen.width * 0.19f, h * 3.6f);
        float gap = Screen.height * 0.015f;
        float y = Screen.height - h - gap;

        var rects = new Rect[count];
        for (int i = 0; i < count; i++)
            rects[i] = new Rect(Screen.width - (count - i) * (w + gap), y, w, h);
        return rects;
    }

    /// <summary>Top-right: Tikinti, x1, x5, x10.</summary>
    private static Rect[] TopRects()
    {
        float h = Screen.height * 0.09f;
        float gap = Screen.height * 0.015f;
        float speedW = Screen.width * 0.085f;
        float buildW = Screen.width * 0.17f;

        var rects = new Rect[4];
        float x = Screen.width - gap;
        for (int i = 3; i >= 1; i--)
        {
            x -= speedW;
            rects[i] = new Rect(x, gap, speedW, h);
            x -= gap;
        }
        rects[0] = new Rect(x - buildW, gap, buildW, h);
        return rects;
    }

    private static Rect[] MenuRects()
    {
        float h = Screen.height * 0.12f;
        float gap = Screen.height * 0.015f;
        float w = Screen.width * 0.34f;
        float x = Screen.width - w - gap;
        float y = gap * 2f + Screen.height * 0.09f;

        var rects = new Rect[Buildable.Length];
        for (int i = 0; i < rects.Length; i++)
            rects[i] = new Rect(x, y + i * (h + gap), w, h);
        return rects;
    }

    // ---------------------------------------------------------------- resources

    private void EnsureStyles()
    {
        if (_label != null) return;
        int size = Mathf.RoundToInt(Screen.height * 0.032f);
        _label = new GUIStyle(GUI.skin.label) { fontSize = size };
        _label.normal.textColor = Color.white;
        _stats = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.042f), fontStyle = FontStyle.Bold };
        _stats.normal.textColor = Color.white;
        _button = new GUIStyle(GUI.skin.button) { fontSize = size, wordWrap = true };
    }

    /// <summary>Yellow lightning bolt on a dark disc, drawn into a small texture once.</summary>
    private Texture2D BoltIcon()
    {
        if (_boltIcon != null) return _boltIcon;

        const int n = 48;
        // Bolt outline in a 0..1 square (y up).
        Vector2[] bolt =
        {
            new Vector2(0.58f, 0.92f), new Vector2(0.27f, 0.50f), new Vector2(0.46f, 0.50f),
            new Vector2(0.38f, 0.08f), new Vector2(0.73f, 0.58f), new Vector2(0.52f, 0.58f),
        };

        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                Color32 c = new Color32(0, 0, 0, 0);
                if ((p - new Vector2(0.5f, 0.5f)).sqrMagnitude <= 0.48f * 0.48f) c = new Color32(30, 30, 40, 220);
                if (InPolygon(bolt, p)) c = new Color32(255, 220, 40, 255);
                pixels[y * n + x] = c;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        _boltIcon = tex;
        return _boltIcon;
    }

    private static bool InPolygon(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }
}
