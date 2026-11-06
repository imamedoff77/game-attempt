using SimCore;
using UnityEngine;

/// <summary>The campaign screens: mission list, briefing, the objectives shown during a mission and its result card.</summary>
public sealed partial class DemoHud
{
    private Texture2D _starOn, _starOff;

    private static string Clock(int seconds) => (seconds / 60) + ":" + (seconds % 60).ToString("00");

    // ---------------------------------------------------------------- stars

    private Texture2D Star(bool lit)
    {
        if (_starOn == null)
        {
            _starOn = MakeStar(new Color32(255, 214, 60, 255));
            _starOff = MakeStar(new Color32(90, 90, 100, 200));
        }
        return lit ? _starOn : _starOff;
    }

    private static Texture2D MakeStar(Color32 colour)
    {
        const int n = 48;
        var points = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float angle = Mathf.PI / 2f + i * Mathf.PI / 5f;
            float radius = i % 2 == 0 ? 0.48f : 0.20f;
            points[i] = new Vector2(0.5f + Mathf.Cos(angle) * radius, 0.5f + Mathf.Sin(angle) * radius);
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                pixels[y * n + x] = InPolygon(points, new Vector2((x + 0.5f) / n, (y + 0.5f) / n)) ? colour : new Color32(0, 0, 0, 0);
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>Three stars in a row inside <paramref name="area"/>, the first <paramref name="lit"/> of them bright.</summary>
    private void DrawStars(Rect area, int lit)
    {
        float size = Mathf.Min(area.height, area.width / 3f);
        float x = area.x + (area.width - size * 3f) / 2f;
        for (int i = 0; i < 3; i++)
            GUI.DrawTexture(new Rect(x + i * size, area.y + (area.height - size) / 2f, size, size), Star(i < lit));
    }

    // ---------------------------------------------------------------- mission list

    private const int Columns = 5;

    private Rect[] MissionTiles()
    {
        int count = Campaign.Missions.Count, rows = (count + Columns - 1) / Columns;
        float gap = Screen.height * 0.03f;
        float w = (Screen.width * 0.86f - gap * (Columns - 1)) / Columns, h = Screen.height * 0.3f;
        float x0 = (Screen.width - (w * Columns + gap * (Columns - 1))) / 2f, y0 = Screen.height * 0.22f;
        var rects = new Rect[count];
        for (int i = 0; i < count; i++)
            rects[i] = new Rect(x0 + (i % Columns) * (w + gap), y0 + (i / Columns) * (h + gap), w, h);
        return rects;
    }

    private void DrawCampaignList()
    {
        CampaignProgress progress = _game.Progress;
        DrawPageTitle(Strings.Get("campaign.title"));
        DrawPageChips(progress.TotalStars + "/" + Campaign.Missions.Count * 3, progress.Credits.ToString());

        Rect[] tiles = MissionTiles();
        int numSize = Mathf.RoundToInt(Screen.height * 0.07f);
        var number = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = numSize };
        var name = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontStyle = FontStyle.Bold };
        for (int i = 0; i < tiles.Length; i++)
        {
            int mission = i + 1;
            bool open = progress.IsUnlocked(mission);
            int stars = progress.Stars(mission);
            Rect r = tiles[i];
            bool clicked = false;
            if (open) clicked = GUI.Button(r, "", _button);
            else GUI.Box(r, "");
            if (clicked) { _briefingMission = mission; _page = MenuPage.Briefing; }

            // stripe: green = three stars, orange = open, grey = locked
            Color stripe = !open ? UiTheme.Edge : stars >= 3 ? UiTheme.Good : UiTheme.Accent;
            Color old = GUI.color;
            GUI.color = stripe;
            GUI.DrawTexture(new Rect(r.x + r.height * 0.1f, r.y + 3f, r.width - r.height * 0.2f, Mathf.Max(3f, r.height * 0.02f)), Texture2D.whiteTexture);
            GUI.color = old;

            number.normal.textColor = open ? UiTheme.Accent : UiTheme.Dim;
            GUI.Label(new Rect(r.x, r.y + r.height * 0.06f, r.width, r.height * 0.3f), mission.ToString("00"), number);
            if (open)
            {
                name.normal.textColor = UiTheme.Bone;
                GUI.Label(new Rect(r.x + r.width * 0.06f, r.y + r.height * 0.36f, r.width * 0.88f, r.height * 0.28f), Strings.MissionName(mission), name);
                DrawStars(new Rect(r.x, r.y + r.height * 0.68f, r.width, r.height * 0.24f), stars);
            }
            else
            {
                name.normal.textColor = UiTheme.Dim;
                GUI.color = new Color(1f, 1f, 1f, 0.7f);
                float lock0 = r.height * 0.2f;
                GUI.DrawTexture(new Rect(r.center.x - lock0 / 2f, r.y + r.height * 0.42f, lock0, lock0), UiTheme.IconTexture(UiTheme.Icon.Alert));
                GUI.color = old;
                GUI.Label(new Rect(r.x, r.y + r.height * 0.68f, r.width, r.height * 0.22f), Strings.Get("campaign.locked"), name);
            }
        }

        var back = new Rect(Screen.width * 0.04f, Screen.height - Screen.height * 0.14f, Screen.width * 0.2f, Screen.height * 0.1f);
        if (IconButton(back, null, Strings.Get("menu.back"))) _page = MenuPage.Main;
    }

    // ---------------------------------------------------------------- briefing

    private void DrawBriefing()
    {
        Mission mission = Campaign.Get(_briefingMission);
        var card = new Rect(Screen.width * 0.1f, Screen.height * 0.05f, Screen.width * 0.8f, Screen.height * 0.9f);
        GUI.Box(card, "");
        float pad = card.width * 0.04f;

        // Header: small caption, mission name, accent bar.
        var caption = new GUIStyle(_label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerLeft, fontSize = Mathf.RoundToInt(Screen.height * 0.034f) };
        caption.normal.textColor = UiTheme.Accent;
        GUI.Label(new Rect(card.x + pad, card.y + card.height * 0.02f, card.width * 0.5f, card.height * 0.07f), Strings.Get("campaign.mission", mission.Number).ToUpperInvariant(), caption);
        var title = new GUIStyle(_stats) { alignment = TextAnchor.UpperLeft, fontSize = Mathf.RoundToInt(Screen.height * 0.07f) };
        GUI.Label(new Rect(card.x + pad, card.y + card.height * 0.085f, card.width - pad * 2f, card.height * 0.13f), Strings.MissionName(mission.Number), title);
        Color old = GUI.color;
        GUI.color = UiTheme.Accent;
        GUI.DrawTexture(new Rect(card.x + pad, card.y + card.height * 0.22f, card.width - pad * 2f, Mathf.Max(3f, Screen.height * 0.004f)), Texture2D.whiteTexture);
        GUI.color = old;

        float colW = (card.width - pad * 3f) / 2f;
        float top = card.y + card.height * 0.26f;
        var head = new GUIStyle(_label) { fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(Screen.height * 0.036f) };
        head.normal.textColor = UiTheme.Dim;
        var body = new GUIStyle(_label) { wordWrap = true, alignment = TextAnchor.UpperLeft };

        // Left column: briefing text, then the objectives.
        float lx = card.x + pad;
        GUI.Label(new Rect(lx, top, colW, card.height * 0.2f), Strings.MissionBrief(mission.Number), body);
        float y = top + card.height * 0.24f, line = card.height * 0.07f;
        GUI.Label(new Rect(lx, y, colW, line), Strings.Get("campaign.briefing").ToUpperInvariant(), head);
        y += line * 1.05f;
        foreach (Objective o in mission.Objectives)
        {
            GUI.color = UiTheme.Accent;
            GUI.DrawTexture(new Rect(lx, y + line * 0.3f, line * 0.25f, line * 0.25f), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(lx + line * 0.5f, y, colW - line * 0.5f, line * 1.2f), ObjectiveText(o, 0, ObjectiveTarget(mission, o)), body);
            y += line;
        }

        // Right column: what each of the three stars asks for.
        float rx = card.x + pad * 2f + colW;
        var plate = new Rect(rx, top, colW, card.height * 0.45f);
        GUI.Box(plate, "");
        GUI.Label(new Rect(rx + pad * 0.5f, top + card.height * 0.015f, colW, line), Strings.Get("campaign.stars").ToUpperInvariant(), head);
        int best = _game.Progress.Stars(mission.Number);
        string[] reasons =
        {
            Strings.Get("campaign.starWin"),
            Strings.Get("campaign.starTime", Clock(mission.ParSeconds)),
            Strings.Get("campaign.starLosses", mission.MaxLosses),
        };
        float sy = top + card.height * 0.1f, sline = card.height * 0.1f;
        for (int i = 0; i < 3; i++)
        {
            GUI.DrawTexture(new Rect(rx + pad * 0.5f, sy + i * sline, sline * 0.8f, sline * 0.8f), Star(i < best));
            GUI.Label(new Rect(rx + pad * 0.5f + sline, sy + i * sline, colW - pad - sline, sline), reasons[i], body);
        }

        float bh = card.height * 0.11f, bw = card.width * 0.3f, by = card.yMax - bh - card.height * 0.03f;
        if (IconButton(new Rect(card.x + pad, by, bw, bh), null, Strings.Get("menu.back"))) _page = MenuPage.Campaign;
        if (IconButton(new Rect(card.xMax - pad - bw, by, bw, bh), UiTheme.Icon.Flag, Strings.Get("campaign.start"), true))
        {
            _page = MenuPage.Campaign;
            _game.StartMission(mission.Number);
        }
    }

    // ---------------------------------------------------------------- during a mission

    private static int ObjectiveTarget(Mission mission, Objective o)
    {
        switch (o.Kind)
        {
            case ObjectiveKind.DestroyEnemyHq: return 1;
            case ObjectiveKind.DestroyEnemyBuildings: return 0; // known only once the world exists
            default: return o.Target;
        }
    }

    private static string DefaultObjectiveKey(ObjectiveKind kind) => "objective." + kind;

    private static string ObjectiveText(Objective o, int current, int target)
    {
        switch (o.Kind)
        {
            case ObjectiveKind.Survive:
                return Strings.Get("objective.Survive", Clock(current), Clock(target));
            case ObjectiveKind.BuildBuildings:
                return Strings.Get("objective.BuildBuildings", current, target, Strings.BuildingName(o.Building));
            case ObjectiveKind.DestroyEnemyBuildings:
                return target > 0 ? Strings.Get("objective.DestroyEnemyBuildings", current, target) : Strings.Get("objective.DestroyEnemyBuildings", 0, "?");
            default:
                return Strings.Get(DefaultObjectiveKey(o.Kind), current, target);
        }
    }

    /// <summary>Objectives (green when done) under the resource counters, the time left and the next attack.</summary>
    private void DrawMissionHud()
    {
        MissionRun run = _game.Mission;
        if (run == null || _game.Outcome != DemoGame.GameOutcome.Playing) return;

        var line = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
        float x = Screen.height * 0.015f, y = Screen.height * 0.2f, h = Screen.height * 0.05f, w = Screen.width * 0.4f;
        int rows = run.Objectives.Length + (run.SecondsToNextWave >= 0 ? 1 : 0) + (run.Mission.TimeLimitSeconds > 0 ? 1 : 0);
        GUI.Box(new Rect(x * 0.5f, y - h * 0.1f, w, rows * h + h * 0.2f), "");
        Color old = GUI.color;
        foreach (ObjectiveState s in run.Objectives)
        {
            GUI.color = s.Done ? new Color(0.5f, 1f, 0.55f) : Color.white;
            GUI.Label(new Rect(x, y, w, h), ObjectiveText(s.Objective, s.Done && s.Objective.Kind == ObjectiveKind.DestroyEnemyHq ? 1 : s.Current, s.Target), line);
            y += h;
        }
        GUI.color = old;

        int next = run.SecondsToNextWave;
        if (next >= 0 && run.Status == MissionStatus.Running)
        {
            GUI.color = new Color(1f, 0.75f, 0.35f);
            GUI.Label(new Rect(x, y, w, h), Strings.Get("campaign.nextWave", Clock(next)), line);
            y += h;
        }
        int limit = run.Mission.TimeLimitSeconds;
        if (limit > 0 && run.Status == MissionStatus.Running)
        {
            GUI.color = new Color(1f, 0.75f, 0.35f);
            GUI.Label(new Rect(x, y, w, h), Strings.Get("campaign.timeLeft", Clock(Mathf.Max(0, limit - run.Seconds))), line);
        }
        GUI.color = old;
    }

    private void DrawMissionResult(GUIStyle title)
    {
        MissionRun run = _game.Mission;
        bool won = _game.Outcome == DemoGame.GameOutcome.Victory;
        Rect card = EndCardRect();
        GUI.Box(card, "");
        GUI.Label(new Rect(card.x, card.y, card.width, card.height * 0.2f), Strings.Get(won ? "campaign.victory" : "campaign.defeat"),
            new GUIStyle(title) { fontSize = Mathf.RoundToInt(Screen.height * 0.065f) });

        int stars = run.Stars;
        DrawStars(new Rect(card.x + card.width * 0.25f, card.y + card.height * 0.2f, card.width * 0.5f, card.height * 0.2f), stars);

        var small = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter };
        Mission m = run.Mission;
        string text = Strings.Get("campaign.time", Clock(run.Seconds), Clock(m.ParSeconds)) + "\n" + Strings.Get("campaign.losses", run.UnitsLost, m.MaxLosses);
        if (_game.MissionPaid > 0) text += "\n" + Strings.Get("campaign.credits", _game.MissionPaid);
        GUI.Label(new Rect(card.x, card.y + card.height * 0.43f, card.width, card.height * 0.3f), text, small);

        bool hasNext = won && m.Number < Campaign.Missions.Count;
        int count = hasNext ? 3 : 2;
        float gap = card.height * 0.03f, bh = card.height * 0.16f, bw = (card.width - gap * (count + 1)) / count, by = card.yMax - bh - gap;
        int slot = 0;
        if (hasNext && GUI.Button(new Rect(card.x + gap + slot * (bw + gap), by, bw, bh), Strings.Get("campaign.next"), _button)) _game.StartMission(m.Number + 1);
        if (hasNext) slot++;
        if (GUI.Button(new Rect(card.x + gap + slot * (bw + gap), by, bw, bh), Strings.Get("campaign.retry"), _button)) _game.StartMission(m.Number);
        slot++;
        if (GUI.Button(new Rect(card.x + gap + slot * (bw + gap), by, bw, bh), Strings.Get("end.menu"), _button)) LeaveToMenu();
    }
}
