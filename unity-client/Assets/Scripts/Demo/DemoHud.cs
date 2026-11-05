using System.Collections.Generic;
using SimCore;
using UnityEngine;

/// <summary>On-screen buttons and diagnostics (IMGUI: no UI package needed for the demo).</summary>
[RequireComponent(typeof(DemoGame))]
public sealed partial class DemoHud : MonoBehaviour
{
    private static readonly BuildingKind[] Buildable =
    {
        BuildingKind.PowerPlant, BuildingKind.SupplyCenter, BuildingKind.Barracks, BuildingKind.VehicleFactory,
        BuildingKind.RepairStation, BuildingKind.Tower,
    };
    private static readonly float[] Speeds = { 1f, 5f, 10f };
    private static readonly float[] SkirmishSpeeds = { 1f, 2f };

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
        DrawAll();
        // A press that landed on a button has captured the mouse by now (IMGUI gives no other hint).
        if (Event.current.type == EventType.MouseDown && GUIUtility.hotControl != 0) Sfx.Play("click", 0.7f, 0.02f);
    }

    private void DrawAll()
    {
        if (_game.World == null) return;
        EnsureStyles();
        World world = _game.World;

        if (_game.Mode == DemoGame.GameMode.Menu) { DrawMainMenu(); return; }

        DrawStats(world);
        DrawUnpoweredIcons(world);
        DrawProductionBars(world);
        DrawHealthBars(world);

        // Top-right: build menu button and simulation speed.
        Rect[] top = TopRects();
        GUI.enabled = !_game.Placing;
        if (GUI.Button(top[0], Strings.Get(_menuOpen ? "menu.buildClose" : "menu.build"), _button)) _menuOpen = !_menuOpen;
        GUI.enabled = true;
        float[] speeds = _game.IsOfflineMatch ? SkirmishSpeeds : Speeds;
        for (int i = 0; i < speeds.Length && !_game.OnlineMatch; i++) // everybody plays at normal speed online
        {
            string text = (_game.SimSpeed == speeds[i] ? "[x" + speeds[i] + "]" : "x" + speeds[i]);
            if (GUI.Button(top[i + 1], text, _button)) _game.SimSpeed = speeds[i];
        }
        if (_game.IsOfflineMatch && _game.Outcome == DemoGame.GameOutcome.Playing)
            if (GUI.Button(top[3], Strings.Get("button.pause"), _button)) _game.Paused = true;

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
        if (_game.IsOfflineMatch) DrawMatchClock();
        if (_game.Mode == DemoGame.GameMode.Campaign) DrawMissionHud();
        if (_game.TutorialStep >= 0 && !_game.Paused && _game.Outcome == DemoGame.GameOutcome.Playing) DrawTutorial();
        if (_game.Paused) DrawPauseDialog();

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

        GUI.Box(new Rect(pad * 0.5f, pad * 0.5f, Screen.width * 0.34f, line * 3f + pad), "");
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

        string pop = Strings.Get("hud.pop", me.PopUsed, me.PopCap, me.BaseLevel);
        if (me.Upgrading) pop += Strings.Get("hud.upgrading", UpgradePercent(me));
        GUIStyle popStyle = _stats;
        if (me.PopUsed >= me.PopCap)
        {
            popStyle = new GUIStyle(_stats);
            popStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        }
        GUI.Label(new Rect(pad, pad + line * 2f, Screen.width * 0.6f, line), pop, popStyle);
    }

    private void DrawBuildMenu(World world)
    {
        Rect[] items = MenuRects();
        for (int i = 0; i < Buildable.Length; i++)
        {
            BuildingKind kind = Buildable[i];
            BuildingDef def = Defs.Building(kind);
            PlayerState me = world.Player(_game.LocalTeam);
            bool locked = me.BaseLevel < def.RequiredLevel;
            GUI.enabled = !locked && me.Resources >= def.Cost;
            string detail = locked ? Strings.Get("menu.needsLevel", def.RequiredLevel) : Describe(world, _game.LocalTeam, kind, def);
            if (GUI.Button(items[i], $"{Strings.BuildingName(kind)}  -  {def.Cost}\n{detail}", _button))
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
        Rect[] rects = PanelRects(PanelButtonCount(b));
        PlayerState me = world.Player(_game.LocalTeam);

        string state = Strings.Get(b.Powered ? "panel.working" : "panel.noEnergy");
        string text = Strings.Get("panel.title", Strings.BuildingName(b.Kind), b.UnderConstruction ? Strings.Get("panel.constructing", ConstructionPercent(b)) : state);
        if (!b.UnderConstruction && units.Length > 0)
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
        if (!b.UnderConstruction && b.Kind == BuildingKind.Hq)
        {
            text += me.Upgrading ? Strings.Get("panel.upgrading", UpgradePercent(me)) : Strings.Get("panel.baseLevel", me.BaseLevel);
            if (me.Researching) text += Strings.Get("panel.researching", Strings.UpgradeName(me.ResearchKind), ResearchPercent(me));
        }
        GUI.Box(rects[0], "");
        GUI.Label(rects[0], text, _label);

        if (b.UnderConstruction)
        {
            if (GUI.Button(rects[1], Strings.Get("panel.cancelBuild", Defs.Building(b.Kind).Cost * Economy.CancelRefundPercent / 100), _button)) _game.CancelConstruction();
            GUI.enabled = true;
            return;
        }

        for (int i = 0; i < units.Length; i++)
        {
            UnitDef ud = Defs.Unit(units[i]);
            QueueCheck check = world.CheckQueue(b, units[i]);
            GUI.enabled = check == QueueCheck.Ok;
            string extra = check == QueueCheck.QueueFull ? Strings.Get("panel.queueFull")
                         : check == QueueCheck.PopCapReached ? Strings.Get("panel.popFull")
                         : check == QueueCheck.LevelTooLow ? Strings.Get("panel.needsLevel", ud.RequiredLevel) : "";
            if (GUI.Button(rects[1 + i], Strings.Get("panel.unitButton", Strings.UnitName(units[i]), ud.Cost, ud.BuildSeconds, extra), _button))
                _game.QueueUnit(units[i]);
        }

        int next = 1 + units.Length;
        if (units.Length > 0)
        {
            GUI.enabled = b.Queue.Count > 0;
            if (GUI.Button(rects[next++], Strings.Get("panel.cancelLast"), _button)) _game.CancelLastQueued();
            GUI.enabled = true;
            if (GUI.Button(rects[next++], Strings.Get(b.HasRally ? "panel.rallyChange" : "panel.rally"), _button)) _game.BeginSetRally();
        }
        if (b.Kind == BuildingKind.Hq)
        {
            UpgradeCheck up = world.CheckUpgradeBase(_game.LocalTeam);
            GUI.enabled = up == UpgradeCheck.Ok;
            string label = up == UpgradeCheck.MaxLevel
                ? Strings.Get("panel.maxLevel")
                : Strings.Get("panel.upgradeBase", me.BaseLevel + 1, Economy.BaseUpgradeCost(me.BaseLevel), Economy.BaseUpgradeSeconds(me.BaseLevel));
            if (GUI.Button(rects[next++], label, _button)) _game.UpgradeBase();

            for (int k = 0; k < Defs.UpgradeCount; k++)
            {
                var kind = (UpgradeKind)k;
                UpgradeDef ud = Defs.Upgrade(kind);
                int tier = me.UpgradeTier(kind);
                ResearchCheck rc = world.CheckResearch(_game.LocalTeam, kind);
                GUI.enabled = rc == ResearchCheck.Ok;
                string text2;
                if (tier >= ud.Tiers) text2 = Strings.Get("panel.researchMax", Strings.UpgradeName(kind), tier * ud.BonusMilli / 10);
                else
                {
                    text2 = Strings.Get("panel.research", Strings.UpgradeName(kind), ud.BonusMilli / 10, tier + 1, ud.Tiers, ud.Cost[tier], ud.Seconds[tier]);
                    if (rc == ResearchCheck.LevelTooLow) text2 += Strings.Get("panel.needsLevel", ud.RequiredLevel[tier]);
                }
                if (GUI.Button(rects[next++], text2, _button)) _game.Research(kind);
            }
        }
        GUI.enabled = true;
    }

    /// <summary>How many buttons the selected building's panel shows under the info box.</summary>
    private static int PanelButtonCount(Building b)
    {
        if (b.UnderConstruction) return 1;
        int units = Defs.Produces(b.Kind).Length;
        int count = units == 0 ? 0 : units + 2;
        if (b.Kind == BuildingKind.Hq) count += 1 + Defs.UpgradeCount;
        return count;
    }

    private static int ConstructionPercent(Building b) =>
        b.ConstructionTicks == 0 ? 100 : Mathf.Min(100, b.BuildProgress * 100 / b.ConstructionTicks);

    private static int ResearchPercent(PlayerState p)
    {
        UpgradeDef def = Defs.Upgrade(p.ResearchKind);
        int total = def.Seconds[p.UpgradeTier(p.ResearchKind)] * World.TicksPerSecond;
        return Mathf.Clamp((total - p.ResearchTicksLeft) * 100 / total, 0, 100);
    }

    private static int UpgradePercent(PlayerState p)
    {
        int total = Economy.BaseUpgradeSeconds(p.BaseLevel) * World.TicksPerSecond;
        return Mathf.Clamp((total - p.UpgradeTicksLeft) * 100 / total, 0, 100);
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
            if ((b.Queue.Count == 0 && !b.UnderConstruction) || b.Team != _game.LocalTeam) continue;
            Vector3 sp = _game.Cam.Camera.WorldToScreenPoint(new Vector3(b.X + b.Width / 2f, 0.2f, b.Y + b.Height / 2f));
            if (sp.z <= 0f) continue;
            var back = new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h);
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = b.UnderConstruction ? new Color(0.35f, 0.7f, 1f) : b.Powered ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.9f, 0.7f, 0.2f);
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * (b.UnderConstruction ? ConstructionPercent(b) : Percent(b)) / 100f, back.height), Texture2D.whiteTexture);
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

        if (_game.Mode == DemoGame.GameMode.Campaign) { DrawMissionResult(style); return; }

        if (_game.Mode != DemoGame.GameMode.Skirmish)
        {
            var rect = new Rect(0f, Screen.height * 0.3f, Screen.width, Screen.height * 0.2f);
            GUI.Label(rect, Strings.Get(won ? "outcome.victory" : "outcome.defeat"), style);
            return;
        }

        // Match against the computer: a result card with the numbers and what to do next.
        Rect card = EndCardRect();
        GUI.Box(card, "");
        GUI.Label(new Rect(card.x, card.y, card.width, card.height * 0.28f), Strings.Get(won ? "outcome.victoryShort" : "outcome.defeatShort"), style);
        var small = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter };
        int secs = Mathf.RoundToInt(_game.MatchSeconds);
        GUI.Label(new Rect(card.x, card.y + card.height * 0.28f, card.width, card.height * 0.42f),
            Strings.Get("end.stats", secs / 60, (secs % 60).ToString("00"), _game.UnitsBuilt, _game.UnitsLost, _game.EnemiesDestroyed, Strings.DifficultyName(_game.Difficulty)), small);
        Rect[] buttons = EndButtonRects(card);
        if (GUI.Button(buttons[0], Strings.Get("end.again"), _button)) _game.StartSkirmish(_game.Difficulty);
        if (GUI.Button(buttons[1], Strings.Get("end.menu"), _button)) LeaveToMenu();
    }

    private static Rect EndCardRect()
    {
        float w = Screen.width * 0.5f, h = Screen.height * 0.6f;
        return new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
    }

    private static Rect[] EndButtonRects(Rect card)
    {
        float gap = card.height * 0.03f, h = card.height * 0.16f, w = (card.width - gap * 3f) / 2f;
        float y = card.yMax - h - gap;
        return new[] { new Rect(card.x + gap, y, w, h), new Rect(card.x + gap * 2f + w, y, w, h) };
    }

    private void DrawMatchClock()
    {
        int secs = Mathf.RoundToInt(_game.MatchSeconds);
        var style = new GUIStyle(_stats) { alignment = TextAnchor.UpperCenter };
        GUI.Label(new Rect(Screen.width * 0.3f, Screen.height * 0.02f, Screen.width * 0.2f, Screen.height * 0.06f), $"{secs / 60}:{(secs % 60):00}", style);
    }

    private void DrawPauseDialog()
    {
        Rect card = PauseCardRect();
        GUI.Box(card, "");
        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(card.x, card.y, card.width, card.height * 0.2f), Strings.Get("pause.title"), title);
        Rect[] b = PauseButtonRects(card);
        if (GUI.Button(b[0], Strings.Get("pause.resume"), _button)) _game.Paused = false;
        if (GUI.Button(b[1], Strings.Get(Sfx.Enabled ? "sound.on" : "sound.off"), _button)) Sfx.Enabled = !Sfx.Enabled;
        if (GUI.Button(b[2], Strings.Get("pause.surrender"), _button)) _game.Surrender();
        if (GUI.Button(b[3], Strings.Get("pause.menu"), _button)) LeaveToMenu();
    }

    private static Rect PauseCardRect()
    {
        float w = Screen.width * 0.4f, h = Screen.height * 0.76f;
        return new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
    }

    private static Rect[] PauseButtonRects(Rect card)
    {
        float gap = card.height * 0.025f, h = card.height * 0.16f, w = card.width - gap * 2f;
        float y = card.y + card.height * 0.17f;
        var rects = new Rect[4];
        for (int i = 0; i < 4; i++) rects[i] = new Rect(card.x + gap, y + i * (h + gap), w, h);
        return rects;
    }

    // ---------------------------------------------------------------- main menu

    private enum MenuPage { Main, Difficulty, Campaign, Briefing, Armory, Daily }
    private MenuPage _page = MenuPage.Main;
    private int _briefingMission = 1;

    private void DrawMainMenu()
    {
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;

        var title = new GUIStyle(_stats)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(Screen.height * 0.11f),
        };
        if (_page == MenuPage.Main || _page == MenuPage.Difficulty)
        {
            var sound = new Rect(Screen.width - Screen.width * 0.22f - Screen.height * 0.02f, Screen.height - Screen.height * 0.12f, Screen.width * 0.22f, Screen.height * 0.1f);
            if (GUI.Button(sound, Strings.Get(Sfx.Enabled ? "sound.on" : "sound.off"), _button)) Sfx.Enabled = !Sfx.Enabled;
        }
        if (_page == MenuPage.Campaign) { DrawCampaignList(); return; }
        if (_page == MenuPage.Briefing) { DrawBriefing(); return; }
        if (_page == MenuPage.Armory) { DrawArmory(); return; }
        if (_page == MenuPage.Daily) { DrawDaily(); return; }

        GUI.Label(new Rect(0f, Screen.height * 0.08f, Screen.width, Screen.height * 0.18f), Application.productName, title);

        Rect[] buttons = MenuColumn(_page == MenuPage.Main ? MainMenuButtons : 4);
        if (_page == MenuPage.Main)
        {
            if (GUI.Button(buttons[0], Strings.Get("mode.campaign"), _button)) _page = MenuPage.Campaign;
            if (GUI.Button(buttons[1], Strings.Get("mode.vsComputer"), _button)) _page = MenuPage.Difficulty;
            if (GUI.Button(buttons[2], Strings.Get("mode.armory"), _button)) _page = MenuPage.Armory;
            string daily = Strings.Get("mode.daily") + (_game.Daily.HasSomethingToClaim(DemoGame.Today) ? "  (!)" : "");
            if (GUI.Button(buttons[3], daily, _button)) _page = MenuPage.Daily;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GUI.Button(buttons[4], Strings.Get("mode.sandbox"), _button)) _game.StartDemo();
#endif
            return;
        }

        foreach (AiDifficulty d in System.Enum.GetValues(typeof(AiDifficulty)))
            if (GUI.Button(buttons[(int)d], Strings.DifficultyName(d), _button)) { _page = MenuPage.Main; _game.StartSkirmish(d); }
        if (GUI.Button(buttons[3], Strings.Get("menu.back"), _button)) _page = MenuPage.Main;
    }

    /// <summary>Back to the main menu; after a campaign mission the mission list is shown again.</summary>
    private void LeaveToMenu()
    {
        bool campaign = _game.Mode == DemoGame.GameMode.Campaign;
        _game.ReturnToMenu();
        _page = campaign ? MenuPage.Campaign : MenuPage.Main;
    }

    private const int MainMenuButtons = 5;

    private static Rect[] MenuColumn(int count)
    {
        bool tight = count > 4;
        float w = Screen.width * 0.36f, h = Screen.height * (tight ? 0.1f : 0.12f), gap = Screen.height * (tight ? 0.02f : 0.025f);
        float x = (Screen.width - w) / 2f, y = Screen.height * (tight ? 0.3f : 0.32f);
        var rects = new Rect[count];
        for (int i = 0; i < count; i++) rects[i] = new Rect(x, y + i * (h + gap), w, h);
        return rects;
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
            case BuildCheck.LevelTooLow: reason = Strings.Get("hint.placeLevel"); break;
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
            foreach (Rect r in PanelRects(PanelButtonCount(_game.SelectedBuilding))) yield return r;
        if (_game.Paused) yield return PauseCardRect();
        if (_game.IsOfflineMatch && _game.Outcome != DemoGame.GameOutcome.Playing) yield return EndCardRect();
    }

    /// <summary>Right-hand panel of the selected building: info box, one button per unit, cancel, rally.</summary>
    private static Rect[] PanelRects(int buttonCount)
    {
        float gap = Screen.height * 0.015f;
        float w = Screen.width * 0.34f;
        float x = Screen.width - w - gap;
        float y = gap * 2f + Screen.height * 0.09f;
        float infoH = Screen.height * 0.17f;
        float buttonH = Screen.height * 0.1f;

        int count = 1 + buttonCount;
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
        float h = Screen.height * 0.1f;
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
        UiSkin.Apply();
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
