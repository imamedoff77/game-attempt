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
    private GUIStyle _label;
    private GUIStyle _stats;
    private GUIStyle _button;
    private Texture2D _boltIcon;
    private bool _menuOpen;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private DebugPanel _debug;
#endif

    private void Awake()
    {
        _game = GetComponent<DemoGame>();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _debug = gameObject.AddComponent<DebugPanel>();
#endif
    }

    private void Update()
    {
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
        DrawProductionBars(world);
        DrawHealthBars(world);

        // Top-right: build menu button and simulation speed.
        Rect[] top = TopRects();
        GUI.enabled = !_game.Placing;
        if (GUI.Button(top[0], Strings.Get(_menuOpen ? "menu.buildClose" : "menu.build"), _button)) _menuOpen = !_menuOpen;
        GUI.enabled = true;
        for (int i = 0; i < Speeds.Length && !_game.OnlineMatch; i++) // everybody plays at normal speed online
        {
            string text = (_game.SimSpeed == Speeds[i] ? "[x" + Speeds[i] + "]" : "x" + Speeds[i]);
            if (GUI.Button(top[i + 1], text, _button)) _game.SimSpeed = Speeds[i];
        }

        if (_game.SelectedBuilding != null) _menuOpen = false;
        if (_menuOpen && !_game.Placing) DrawBuildMenu(world);
        if (_game.SelectedBuilding != null && !_game.Placing) DrawBuildingPanel(world, _game.SelectedBuilding);
        if (_game.SettingRally) DrawHint(Strings.Get("hint.rally"));

        // Bottom-right row
        Rect[] r = BottomRects();
        if (GUI.Button(r[0], Strings.Get(_game.Touch.BoxMode ? "button.boxOn" : "button.boxOff"), _button)) _game.Touch.BoxMode = !_game.Touch.BoxMode;
        if (_game.Placing)
        {
            GUI.enabled = _game.PlacingCheck == BuildCheck.Ok;
            if (GUI.Button(r[1], Strings.Get("button.confirm"), _button)) _game.ConfirmPlacement();
            GUI.enabled = true;
            if (GUI.Button(r[2], Strings.Get("button.cancel"), _button)) _game.CancelPlacement();
            DrawPlacementHint();
        }
        else
        {
            if (GUI.Button(r[1], Strings.Get("button.selectAll"), _button)) _game.SelectAll();
            if (GUI.Button(r[2], Strings.Get("button.clear"), _button)) _game.ClearSelection();
        }

        DrawOutcome();
        DrawOnlineBanner();

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

        PlayerState me = world.Player(_game.LocalTeam);
        string resources = Strings.Get("hud.resources", me.Resources, me.IncomePerMinute);
        string energy = Strings.Get("hud.energy", me.EnergyUsed, me.EnergyCapacity);
        GUI.Label(new Rect(pad, pad, Screen.width * 0.5f, line), resources, _stats);

        GUIStyle energyStyle = _stats;
        if (me.EnergyShortage)
        {
            energyStyle = new GUIStyle(_stats);
            energyStyle.normal.textColor = new Color(1f, 0.4f, 0.35f);
            energy += Strings.Get("hud.energyShortage");
        }
        GUI.Label(new Rect(pad, pad + line, Screen.width * 0.5f, line), energy, energyStyle);
    }

    private void DrawBuildMenu(World world)
    {
        Rect[] items = MenuRects();
        for (int i = 0; i < Buildable.Length; i++)
        {
            BuildingKind kind = Buildable[i];
            BuildingDef def = Defs.Building(kind);
            GUI.enabled = world.Player(_game.LocalTeam).Resources >= def.Cost;
            if (GUI.Button(items[i], $"{Strings.BuildingName(kind)}  -  {def.Cost}\n{Describe(world, _game.LocalTeam, kind, def)}", _button))
            {
                _menuOpen = false;
                _game.BeginPlacement(kind);
            }
        }
        GUI.enabled = true;
    }

    private static string Describe(World world, int team, BuildingKind kind, BuildingDef def)
    {
        switch (kind)
        {
            case BuildingKind.PowerPlant: return Strings.Get("menu.energyPlus", world.NextOutput(kind, team));
            case BuildingKind.SupplyCenter: return Strings.Get("menu.energyMinusIncome", def.EnergyUse, world.NextOutput(kind, team));
            default: return Strings.Get("menu.energyMinus", def.EnergyUse);
        }
    }

    private void DrawBuildingPanel(World world, Building b)
    {
        UnitKind[] units = Defs.Produces(b.Kind);
        Rect[] rects = PanelRects(units.Length);

        string state = Strings.Get(b.Powered ? "panel.working" : "panel.noEnergy");
        string text = Strings.Get("panel.title", Strings.BuildingName(b.Kind), state);
        if (units.Length > 0)
        {
            text += Strings.Get("panel.queue");
            if (b.Queue.Count == 0) text += Strings.Get("panel.queueEmpty");
            for (int i = 0; i < b.Queue.Count; i++)
            {
                if (i > 0) text += ", ";
                text += Strings.UnitName(b.Queue[i]);
                if (i == 0) text += $" {Percent(b)}%";
            }
        }
        GUI.Box(rects[0], "");
        GUI.Label(rects[0], text, _label);

        for (int i = 0; i < units.Length; i++)
        {
            UnitDef ud = Defs.Unit(units[i]);
            QueueCheck check = world.CheckQueue(b, units[i]);
            GUI.enabled = check == QueueCheck.Ok;
            string extra = check == QueueCheck.QueueFull ? Strings.Get("panel.queueFull") : "";
            if (GUI.Button(rects[1 + i], Strings.Get("panel.unitButton", Strings.UnitName(units[i]), ud.Cost, ud.BuildSeconds, extra), _button))
                _game.QueueUnit(units[i]);
        }

        if (units.Length > 0)
        {
            GUI.enabled = b.Queue.Count > 0;
            if (GUI.Button(rects[1 + units.Length], Strings.Get("panel.cancelLast"), _button)) _game.CancelLastQueued();
            GUI.enabled = true;
            if (GUI.Button(rects[2 + units.Length], Strings.Get(b.HasRally ? "panel.rallyChange" : "panel.rally"), _button)) _game.BeginSetRally();
        }
        GUI.enabled = true;
    }

    private static int Percent(Building b)
    {
        if (b.Queue.Count == 0) return 0;
        int total = Defs.Unit(b.Queue[0]).BuildSeconds * World.TicksPerSecond;
        return Mathf.Min(100, b.Progress * 100 / total);
    }

    /// <summary>A small progress bar over every building that is producing something.</summary>
    private void DrawProductionBars(World world)
    {
        if (_game.Cam == null) return;
        float w = Screen.height * 0.12f, h = Screen.height * 0.014f;
        foreach (Building b in world.Buildings)
        {
            if (b.Queue.Count == 0 || b.Team != _game.LocalTeam) continue;
            Vector3 sp = _game.Cam.Camera.WorldToScreenPoint(new Vector3(b.X + b.Width / 2f, 0.2f, b.Y + b.Height / 2f));
            if (sp.z <= 0f) continue;
            var back = new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = b.Powered ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.9f, 0.7f, 0.2f);
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * Percent(b) / 100f, back.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }

    /// <summary>Health bars over damaged (or selected) units and damaged buildings. Green = yours, red = enemy.</summary>
    private void DrawHealthBars(World world)
    {
        if (_game.Cam == null) return;
        Camera cam = _game.Cam.Camera;
        float h = Screen.height * 0.011f;

        foreach (Unit u in world.Units)
        {
            if (u.Hp >= u.MaxHp && !_game.IsSelected(u.Id)) continue;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(u.Pos.X.ToFloat(), 1.4f, u.Pos.Y.ToFloat()));
            if (sp.z <= 0f) continue;
            DrawBar(sp, Screen.height * 0.06f, h, (float)u.Hp / u.MaxHp, u.Team == _game.LocalTeam);
        }
        foreach (Building b in world.Buildings)
        {
            if (b.Hp >= b.MaxHp) continue;
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(b.X + b.Width / 2f, 2.2f, b.Y + b.Height / 2f));
            if (sp.z <= 0f) continue;
            DrawBar(sp, Screen.height * 0.12f, h, (float)b.Hp / b.MaxHp, b.Team == _game.LocalTeam);
        }
    }

    private static void DrawBar(Vector3 screenPoint, float width, float height, float fraction, bool mine)
    {
        var back = new Rect(screenPoint.x - width / 2f, Screen.height - screenPoint.y - height / 2f, width, height);
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        GUI.color = mine ? new Color(0.3f, 0.95f, 0.4f) : new Color(1f, 0.3f, 0.25f);
        GUI.DrawTexture(new Rect(back.x, back.y, back.width * Mathf.Clamp01(fraction), back.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    private void DrawOutcome()
    {
        if (_game.Outcome == DemoGame.GameOutcome.Playing) return;
        bool won = _game.Outcome == DemoGame.GameOutcome.Victory;
        var style = new GUIStyle(_stats)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Screen.height * 0.08f),
        };
        style.normal.textColor = won ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 0.45f, 0.4f);
        var rect = new Rect(0f, Screen.height * 0.3f, Screen.width, Screen.height * 0.2f);
        GUI.Label(rect, Strings.Get(won ? "outcome.victory" : "outcome.defeat"), style);
    }

    /// <summary>One line in the middle of the top edge when the network needs the player's attention.</summary>
    private void DrawOnlineBanner()
    {
        if (!_game.OnlineMatch) return;
        OnlineSession online = _game.Online;
        string text = null;
        if (online == null) text = Strings.Get("online.connectionLost");
        else if (online.Reconnecting) text = Strings.Get("online.reconnecting");
        else if (online.Client != null && online.Client.CatchingUp) text = Strings.Get("online.catchingUp");
        else if (online.Client == null) text = null;
        else if (online.Client.Desynced) text = Strings.Get("online.desync");
        else if (online.OpponentLeft) text = Strings.Get("online.opponentLeft");
        else if (online.State == OnlineSession.Phase.Ended) text = Strings.Get("online.connectionLost");
        else if (online.Client.Waiting) text = Strings.Get("online.waitingNetwork");
        if (text == null) return;

        var style = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.045f) };
        style.normal.textColor = new Color(1f, 0.8f, 0.3f);
        GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, Screen.height * 0.08f), text, style);
    }

    private void DrawHint(string text)
    {
        float pad = Screen.height * 0.02f;
        float y = Screen.height - Screen.height * 0.11f - Screen.height * 0.015f - Screen.height * 0.12f;
        var rect = new Rect(Screen.width - Screen.width * 0.5f - pad, y, Screen.width * 0.5f, Screen.height * 0.1f);
        GUI.Box(rect, "");
        GUI.Label(rect, text, _label);
    }

    private void DrawPlacementHint()
    {
        string reason;
        switch (_game.PlacingCheck)
        {
            case BuildCheck.Ok: reason = Strings.Get("hint.placeOk"); break;
            case BuildCheck.BadGround: reason = Strings.Get("hint.placeBadGround"); break;
            case BuildCheck.UnitInWay: reason = Strings.Get("hint.placeUnitInWay"); break;
            case BuildCheck.NotEnoughResources: reason = Strings.Get("hint.placeNoResources"); break;
            default: reason = Strings.Get("hint.placeNo"); break;
        }
        float pad = Screen.height * 0.02f;
        float y = Screen.height - Screen.height * 0.11f - Screen.height * 0.015f - Screen.height * 0.12f;
        GUI.Box(new Rect(Screen.width - Screen.width * 0.5f - pad, y, Screen.width * 0.5f, Screen.height * 0.1f), "");
        GUI.Label(new Rect(Screen.width - Screen.width * 0.5f - pad, y, Screen.width * 0.5f, Screen.height * 0.1f),
            Strings.Get("hint.place", Strings.BuildingName(_game.PlacingKind), reason), _label);
    }

    /// <summary>A lightning icon over every building that is switched off for lack of energy.</summary>
    private void DrawUnpoweredIcons(World world)
    {
        if (_game.Cam == null) return;
        float size = Screen.height * 0.075f;
        foreach (Building b in world.Buildings)
        {
            if (b.Powered || b.Team != _game.LocalTeam) continue;
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_debug != null)
            foreach (Rect r in _debug.ActiveRects()) yield return r;
#endif
        if (_menuOpen && !_game.Placing)
            foreach (Rect r in MenuRects()) yield return r;
        if (_game.SelectedBuilding != null && !_game.Placing)
            foreach (Rect r in PanelRects(Defs.Produces(_game.SelectedBuilding.Kind).Length)) yield return r;
    }

    /// <summary>Right-hand panel of the selected building: info box, one button per unit, cancel, rally.</summary>
    private static Rect[] PanelRects(int unitCount)
    {
        float gap = Screen.height * 0.015f;
        float w = Screen.width * 0.34f;
        float x = Screen.width - w - gap;
        float y = gap * 2f + Screen.height * 0.09f;
        float infoH = Screen.height * 0.17f;
        float buttonH = Screen.height * 0.1f;

        int count = unitCount == 0 ? 1 : 1 + unitCount + 2;
        var rects = new Rect[count];
        rects[0] = new Rect(x, y, w, infoH);
        for (int i = 1; i < count; i++)
            rects[i] = new Rect(x, y + infoH + gap + (i - 1) * (buttonH + gap), w, buttonH);
        return rects;
    }

    private static Rect[] BottomRects()
    {
        const int count = 3;
        float h = Screen.height * 0.11f;
        float w = Mathf.Min(Screen.width * 0.19f, h * 3.6f);
        float gap = Screen.height * 0.015f;
        float y = Screen.height - h - gap;

        var rects = new Rect[count];
        for (int i = 0; i < count; i++)
            rects[i] = new Rect(Screen.width - (count - i) * (w + gap), y, w, h);
        return rects;
    }

    /// <summary>Top-right: Build, x1, x5, x10.</summary>
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
