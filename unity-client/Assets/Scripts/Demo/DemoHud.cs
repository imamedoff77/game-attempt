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
    private GUIStyle _timerStyle;
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

        UpdateToasts();
        DrawStats(world);
        DrawUnpoweredIcons(world);
        DrawProductionBars(world);
        DrawHealthBars(world);
        DrawOrderLines(world);

        // Top-right: build menu button and simulation speed.
        Rect[] top = TopRects();
        GUI.enabled = !_game.Placing;
        if (IconButton(top[0], UiTheme.Icon.Build, Strings.Get(_menuOpen ? "menu.buildClose" : "menu.build"), _menuOpen)) _menuOpen = !_menuOpen;
        GUI.enabled = true;
        float[] speeds = _game.IsOfflineMatch ? SkirmishSpeeds : Speeds;
        for (int i = 0; i < speeds.Length && !_game.OnlineMatch; i++) // everybody plays at normal speed online
        {
            if (IconButton(top[i + 1], null, "x" + speeds[i], _game.SimSpeed == speeds[i])) _game.SimSpeed = speeds[i];
        }
        if (_game.IsOfflineMatch && _game.Outcome == DemoGame.GameOutcome.Playing)
            if (IconButton(top[3], UiTheme.Icon.Pause, null)) _game.Paused = true;
        if (_game.OnlineMatch && _game.Outcome == DemoGame.GameOutcome.Playing)
        {
            // Giving up needs a second tap so a stray touch does not lose the match.
            bool armed = Time.unscaledTime < _surrenderArmedUntil;
            if (GUI.Button(top[3], Strings.Get(armed ? "online.surrenderSure" : "online.surrender"), _button))
            {
                if (armed) { _surrenderArmedUntil = 0f; _game.Surrender(); }
                else _surrenderArmedUntil = Time.unscaledTime + 3f;
            }
            DrawChat(top[1]);
            if (GUI.Button(top[2], Strings.Get("online.leave"), _button)) LeaveToMenu(); // just drops the connection: the opponent waits, then wins when the grace time ends
        }

        if (_game.SelectedBuilding != null) _menuOpen = false;
        if (_menuOpen && !_game.Placing) DrawBuildMenu(world);
        if (_game.SelectedBuilding != null && !_game.Placing) DrawBuildingPanel(world, _game.SelectedBuilding);
        if (SelectionBarVisible) DrawSelectionBar(world);
        DrawToasts();
        if (_game.SettingRally) DrawHint(Strings.Get("hint.rally"));

        // Bottom-right row
        Rect[] r = BottomRects();
        if (IconButton(r[0], null, Strings.Get("button.box"), _game.Touch.BoxMode)) _game.Touch.BoxMode = !_game.Touch.BoxMode;
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
            if (IconButton(r[1], UiTheme.Icon.Units, Strings.Get("button.selectAll"))) _game.SelectAll();
            if (IconButton(r[2], UiTheme.Icon.Cross, Strings.Get("button.clear"))) _game.ClearSelection();
        }

        if (MinimapVisible) DrawMinimap(world);
        DrawOutcome();
        DrawOnlineBanner();
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

    /// <summary>Top-left status bar: resources, energy, units and the match clock as four chips, with a caption under them.</summary>
    private void DrawStats(World world)
    {
        PlayerState me = world.Player(_game.LocalTeam);
        float pad = Screen.height * 0.015f;
        float h = Screen.height * 0.085f;
        float w = Screen.width * 0.095f;
        float gap = Screen.height * 0.01f;
        float x = pad, y = pad;

        UiTheme.Chip(new Rect(x, y, w * 1.05f, h), UiTheme.Icon.Resources, me.Resources.ToString());
        x += w * 1.05f + gap;

        bool shortage = me.EnergyShortage;
        UiTheme.Chip(new Rect(x, y, w * 1.4f, h), UiTheme.Icon.Energy, $"{me.EnergyUsed}/{me.EnergyCapacity}", shortage ? UiTheme.Alert : (Color?)null);
        x += w * 1.4f + gap;

        bool full = me.PopUsed >= me.PopCap;
        UiTheme.Chip(new Rect(x, y, w, h), UiTheme.Icon.Units, $"{me.PopUsed}/{me.PopCap}", full ? UiTheme.Warn : (Color?)null);
        x += w + gap;

        int secs = Mathf.RoundToInt(_game.MatchSeconds);
        UiTheme.Chip(new Rect(x, y, w, h), UiTheme.Icon.Clock, $"{secs / 60}:{secs % 60:00}");

        string caption = Strings.Get("hud.caption", me.IncomePerMinute, me.BaseLevel);
        Color captionColor = UiTheme.Dim;
        if (shortage) { caption = Strings.Get("hud.energyShortage"); captionColor = UiTheme.Alert; }
        else if (me.Upgrading) { caption = Strings.Get("hud.upgrading", UpgradePercent(me)); captionColor = UiTheme.Accent; }
        UiTheme.Caption(new Rect(pad, y + h + gap, Screen.width * 0.4f, Screen.height * 0.045f), caption, captionColor);
    }

    private void DrawBuildMenu(World world)
    {
        Rect[] items = MenuRects();
        PlayerState me = world.Player(_game.LocalTeam);
        for (int i = 0; i < Buildable.Length; i++)
        {
            BuildingKind kind = Buildable[i];
            BuildingDef def = Defs.Building(kind);
            bool locked = me.BaseLevel < def.RequiredLevel;
            int owned = 0;
            foreach (Building b in world.Buildings)
                if (b.Team == _game.LocalTeam && b.Kind == kind) owned++;
            string problem = locked ? Strings.Get("card.locked", def.RequiredLevel) : null;
            if (Card(items[i], Strings.BuildingName(kind), def.Cost, UiTheme.Icon.Energy, Describe(world, _game.LocalTeam, kind, def),
                     !locked && me.Resources >= def.Cost, owned > 0 ? Strings.Get("card.owned", owned) : null, problem))
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
            case BuildingKind.PowerPlant: return Strings.Get("card.energyPlus", world.NextOutput(kind, team));
            case BuildingKind.SupplyCenter: return Strings.Get("card.energyMinusIncome", def.EnergyUse, world.NextOutput(kind, team));
            default: return Strings.Get("card.energyMinus", def.EnergyUse);
        }
    }

    private void DrawBuildingPanel(World world, Building b)
    {
        UnitKind[] units = Defs.Produces(b.Kind);
        Rect[] rects = PanelRects(PanelButtonCount(b));
        PlayerState me = world.Player(_game.LocalTeam);

        DrawBuildingInfo(rects[0], world, b, me);

        if (b.UnderConstruction)
        {
            if (IconButton(rects[1], UiTheme.Icon.Cross, Strings.Get("panel.cancelBuild", Defs.Building(b.Kind).Cost * Economy.CancelRefundPercent / 100))) _game.CancelConstruction();
            GUI.enabled = true;
            return;
        }

        Rect[] cards = CardRects(BuildingCardCount(b));
        for (int i = 0; i < units.Length; i++)
        {
            UnitDef ud = Defs.Unit(units[i]);
            QueueCheck check = world.CheckQueue(b, units[i]);
            string problem = check == QueueCheck.QueueFull ? Strings.Get("card.queueFull")
                           : check == QueueCheck.PopCapReached ? Strings.Get("card.popFull")
                           : check == QueueCheck.LevelTooLow ? Strings.Get("card.locked", ud.RequiredLevel) : null;
            int queued = 0;
            foreach (UnitKind k in b.Queue) if (k == units[i]) queued++;
            if (Card(cards[i], Strings.UnitName(units[i]), ud.Cost, UiTheme.Icon.Clock, Strings.Get("card.seconds", ud.BuildSeconds),
                     check == QueueCheck.Ok, queued > 0 ? Strings.Get("card.queued", queued) : null, problem))
                _game.QueueUnit(units[i]);
        }

        int next = 1;
        if (units.Length > 0)
        {
            GUI.enabled = b.Queue.Count > 0;
            if (IconButton(rects[next++], UiTheme.Icon.Cross, Strings.Get("panel.cancelLast"))) _game.CancelLastQueued();
            GUI.enabled = true;
            if (IconButton(rects[next++], UiTheme.Icon.Flag, Strings.Get(b.HasRally ? "panel.rallyChange" : "panel.rally"), _game.SettingRally)) _game.BeginSetRally();
        }
        if (b.Kind == BuildingKind.Hq) DrawHqCards(world, me);
        GUI.enabled = true;
    }

    /// <summary>How many buttons the selected building's panel shows under the info box.</summary>
    private static int PanelButtonCount(Building b)
    {
        if (b.UnderConstruction) return 1;
        int units = Defs.Produces(b.Kind).Length;
        return units == 0 ? 0 : 2; // cancel last + rally; unit and HQ cards sit in the bottom row
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

    /// <summary>A progress bar with the time left over every building of yours that is being built or is producing.</summary>
    private void DrawProductionBars(World world)
    {
        if (_game.Cam == null) return;
        float w = Screen.height * 0.17f, h = Screen.height * 0.022f;
        int textSize = Mathf.RoundToInt(h * 1.35f);
        if (_timerStyle == null || _timerStyle.fontSize != textSize)
            _timerStyle = new GUIStyle(GUI.skin.label) { fontSize = textSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
        foreach (Building b in world.Buildings)
        {
            if ((b.Queue.Count == 0 && !b.UnderConstruction) || b.Team != _game.LocalTeam) continue;
            Vector3 sp = _game.Cam.Camera.WorldToScreenPoint(new Vector3(b.X + b.Width / 2f, 0.2f, b.Y + b.Height / 2f));
            if (sp.z <= 0f) continue;

            float fraction;
            int ticksLeft;
            if (b.UnderConstruction)
            {
                fraction = ConstructionPercent(b) / 100f;
                ticksLeft = b.ConstructionTicks - b.BuildProgress;
            }
            else
            {
                fraction = Percent(b) / 100f;
                ticksLeft = Defs.Unit(b.Queue[0]).BuildSeconds * World.TicksPerSecond - b.Progress;
            }
            int secs = Mathf.Max(0, Mathf.CeilToInt(ticksLeft / (float)World.TicksPerSecond));
            Color fill = b.UnderConstruction ? UiTheme.Accent : b.Powered ? UiTheme.Good : UiTheme.Warn;

            var back = new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h);
            Color old = GUI.color;
            GUI.color = new Color(0.06f, 0.07f, 0.05f, 0.85f);
            GUI.DrawTexture(new Rect(back.x - 2f, back.y - 2f, back.width + 4f, back.height + 4f), Texture2D.whiteTexture);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * fraction, back.height), Texture2D.whiteTexture);
            GUI.color = old;

            string label = $"{secs / 60}:{secs % 60:00}";
            if (!b.UnderConstruction && b.Queue.Count > 1) label += $"  x{b.Queue.Count}";
            UiTheme.ShadowLabel(new Rect(back.x, back.y - h * 1.7f, back.width, h * 1.6f), label, _timerStyle, Color.white);
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

        if (_game.Mode == DemoGame.GameMode.Online)
        {
            Rect onlineCard = EndCardRect();
            GUI.Box(onlineCard, "");
            GUI.Label(new Rect(onlineCard.x, onlineCard.y, onlineCard.width, onlineCard.height * 0.5f), Strings.Get(won ? "outcome.victoryShort" : "outcome.defeatShort"), style);
            OnlineSession ranked = _game.Online;
            if (ranked != null && ranked.Ranked)
            {
                var note = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.045f) };
                string line = ranked.RatingChanged
                    ? Strings.Get("end.rating", ranked.RatingBefore, ranked.RatingAfter, (ranked.RatingAfter >= ranked.RatingBefore ? "+" : "") + (ranked.RatingAfter - ranked.RatingBefore))
                    : Strings.Get("end.ratingWait");
                GUI.Label(new Rect(onlineCard.x, onlineCard.y + onlineCard.height * 0.5f, onlineCard.width, onlineCard.height * 0.15f), line, note);
            }
            Rect menuButton = EndButtonRects(onlineCard)[0];
            menuButton.x = onlineCard.center.x - menuButton.width / 2f;
            if (GUI.Button(menuButton, Strings.Get("end.menu"), _button)) LeaveToMenu();
            return;
        }

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

    private float _surrenderArmedUntil;

    private enum MenuPage { Main, Difficulty, Campaign, Briefing, Armory, Daily, Account, Profile, Online, Store }
    private MenuPage _page = MenuPage.Main;
    private int _briefingMission = 1;

    private void DrawMainMenu()
    {
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;

        if (_page == MenuPage.Campaign) { DrawCampaignList(); return; }
        if (_page == MenuPage.Briefing) { DrawBriefing(); return; }
        if (_page == MenuPage.Armory) { DrawArmory(); return; }
        if (_page == MenuPage.Daily) { DrawDaily(); return; }
        if (_page == MenuPage.Account) { DrawAccount(); return; }
        if (_page == MenuPage.Profile) { DrawProfile(); return; }
        if (_page == MenuPage.Online) { DrawOnlinePage(); return; }
        if (_page == MenuPage.Store) { DrawStore(); return; }

        if (_page == MenuPage.Difficulty) DrawDifficulty();
        else DrawHub();
    }

    /// <summary>Back to the main menu; after a campaign mission the mission list is shown again.</summary>
    private void LeaveToMenu()
    {
        bool campaign = _game.Mode == DemoGame.GameMode.Campaign;
        bool online = _game.OnlineMatch;
        _game.ReturnToMenu();
        _page = campaign ? MenuPage.Campaign : online ? MenuPage.Online : MenuPage.Main;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private const int MainMenuButtons = 6;
#else
    private const int MainMenuButtons = 5;
#endif

    private static Rect[] MenuColumn(int count)
    {
        bool tight = count > 4;
        bool tighter = count > 5;
        float w = Screen.width * 0.36f, h = Screen.height * (tighter ? 0.085f : tight ? 0.1f : 0.12f), gap = Screen.height * (tighter ? 0.017f : tight ? 0.02f : 0.025f);
        float x = (Screen.width - w) / 2f, y = Screen.height * (tighter ? 0.28f : tight ? 0.3f : 0.32f);
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
        if (MinimapVisible) yield return MinimapRect();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_debug != null)
            foreach (Rect r in _debug.ActiveRects()) yield return r;
#endif
        if (_menuOpen && !_game.Placing)
            foreach (Rect r in MenuRects()) yield return r;
        if (_game.SelectedBuilding != null && !_game.Placing)
        {
            foreach (Rect r in PanelRects(PanelButtonCount(_game.SelectedBuilding))) yield return r;
            if (!_game.SelectedBuilding.UnderConstruction)
                foreach (Rect r in CardRects(BuildingCardCount(_game.SelectedBuilding))) yield return r;
        }
        if (SelectionBarVisible)
        {
            RefreshSelectionGroups(_game.World);
            foreach (Rect r in CardRects(_selectionGroups.Count)) yield return r;
        }
        foreach (Rect r in ToastRects()) yield return r;
        if (_chatOpen) yield return ChatPanelRect();
        if (_game.Paused) yield return PauseCardRect();
        if ((_game.IsOfflineMatch || _game.OnlineMatch) && _game.Outcome != DemoGame.GameOutcome.Playing) yield return EndCardRect();
    }

    /// <summary>Right-hand panel of the selected building: info box, one button per unit, cancel, rally.</summary>
    private static Rect[] PanelRects(int buttonCount)
    {
        float gap = Screen.height * 0.015f;
        float w = Screen.width * 0.34f;
        float x = Screen.width - w - gap;
        float y = gap * 2f + Screen.height * 0.09f;
        float infoH = Screen.height * 0.15f;
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

    private Rect[] MenuRects() => CardRects(Buildable.Length);

    // ---------------------------------------------------------------- resources

    private void EnsureStyles()
    {
        UiSkin.Apply();
        if (_label != null) return;
        int size = Mathf.RoundToInt(Screen.height * 0.032f);
        _label = new GUIStyle(GUI.skin.label) { fontSize = size };
        _label.normal.textColor = UiTheme.Bone;
        _stats = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.042f), fontStyle = FontStyle.Bold };
        _stats.normal.textColor = UiTheme.Bone;
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
