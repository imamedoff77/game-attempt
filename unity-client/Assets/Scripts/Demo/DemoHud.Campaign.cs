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
        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.08f) };
        GUI.Label(new Rect(0f, Screen.height * 0.03f, Screen.width, Screen.height * 0.12f), Strings.Get("campaign.title"), title);
        var sub = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0f, Screen.height * 0.14f, Screen.width, Screen.height * 0.06f),
            Strings.Get("campaign.total", progress.TotalStars, Campaign.Missions.Count * 3, progress.Credits), sub);

        Rect[] tiles = MissionTiles();
        var number = new GUIStyle(_stats) { alignment = TextAnchor.UpperCenter };
        var name = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
        for (int i = 0; i < tiles.Length; i++)
        {
            int mission = i + 1;
            bool open = progress.IsUnlocked(mission);
            Rect r = tiles[i];
            GUI.enabled = open;
            if (GUI.Button(r, "", _button)) { _briefingMission = mission; _page = MenuPage.Briefing; }
            GUI.enabled = true;

            GUI.Label(new Rect(r.x, r.y + r.height * 0.04f, r.width, r.height * 0.3f), mission.ToString(), number);
            if (open)
            {
                GUI.Label(new Rect(r.x + r.width * 0.05f, r.y + r.height * 0.32f, r.width * 0.9f, r.height * 0.3f), Strings.MissionName(mission), name);
                DrawStars(new Rect(r.x, r.y + r.height * 0.66f, r.width, r.height * 0.28f), progress.Stars(mission));
            }
            else
            {
                GUI.Label(new Rect(r.x, r.y + r.height * 0.45f, r.width, r.height * 0.3f), Strings.Get("campaign.locked"), name);
            }
        }

        var back = new Rect(Screen.width * 0.04f, Screen.height - Screen.height * 0.14f, Screen.width * 0.2f, Screen.height * 0.1f);
        if (GUI.Button(back, Strings.Get("menu.back"), _button)) _page = MenuPage.Main;
    }

    // ---------------------------------------------------------------- briefing

    private void DrawBriefing()
    {
        Mission mission = Campaign.Get(_briefingMission);
        var card = new Rect(Screen.width * 0.15f, Screen.height * 0.06f, Screen.width * 0.7f, Screen.height * 0.88f);
        GUI.Box(card, "");

        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(card.x, card.y + card.height * 0.02f, card.width, card.height * 0.12f),
            Strings.Get("campaign.mission", mission.Number) + ": " + Strings.MissionName(mission.Number), title);

        var body = new GUIStyle(_label) { wordWrap = true, alignment = TextAnchor.UpperLeft };
        float pad = card.width * 0.05f;
        GUI.Label(new Rect(card.x + pad, card.y + card.height * 0.15f, card.width - pad * 2f, card.height * 0.26f), Strings.MissionBrief(mission.Number), body);

        var head = new GUIStyle(_label) { fontStyle = FontStyle.Bold };
        GUI.Label(new Rect(card.x + pad, card.y + card.height * 0.42f, card.width - pad * 2f, card.height * 0.07f), Strings.Get("campaign.briefing"), head);
        float y = card.y + card.height * 0.49f, line = card.height * 0.065f;
        foreach (Objective o in mission.Objectives)
        {
            GUI.Label(new Rect(card.x + pad, y, card.width - pad * 2f, line), "- " + ObjectiveText(o, 0, ObjectiveTarget(mission, o)), body);
            y += line;
        }

        // what the three stars ask for
        float starsY = card.y + card.height * 0.63f;
        int best = _game.Progress.Stars(mission.Number);
        string[] reasons =
        {
            Strings.Get("campaign.starWin"),
            Strings.Get("campaign.starTime", Clock(mission.ParSeconds)),
            Strings.Get("campaign.starLosses", mission.MaxLosses),
        };
        for (int i = 0; i < 3; i++)
        {
            GUI.DrawTexture(new Rect(card.x + pad, starsY + i * line, line * 0.9f, line * 0.9f), Star(i < best));
            GUI.Label(new Rect(card.x + pad + line * 1.1f, starsY + i * line, card.width - pad * 2f - line, line), reasons[i], body);
        }

        float bh = card.height * 0.11f, bw = card.width * 0.4f, by = card.yMax - bh - card.height * 0.03f;
        if (GUI.Button(new Rect(card.x + card.width * 0.07f, by, bw, bh), Strings.Get("menu.back"), _button)) _page = MenuPage.Campaign;
        if (GUI.Button(new Rect(card.xMax - card.width * 0.07f - bw, by, bw, bh), Strings.Get("campaign.start"), _button))
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
