using System.Collections.Generic;
using SimCore;
using UnityEngine;

/// <summary>The selection bar, order lines on the ground and the HQ's upgrade cards.</summary>
public sealed partial class DemoHud
{
    private struct SelectionGroup
    {
        public UnitKind Kind;
        public int Count;
        public int Hp, MaxHp;
    }

    private readonly List<SelectionGroup> _selectionGroups = new List<SelectionGroup>();

    private bool SelectionBarVisible =>
        _game.Mode != DemoGame.GameMode.Menu && _game.World != null && !_game.Placing && !_menuOpen
        && _game.SelectedBuilding == null && _game.SelectedCount > 0 && _game.Outcome == DemoGame.GameOutcome.Playing;

    /// <summary>How many cards the selected building shows in the bottom row.</summary>
    private static int BuildingCardCount(Building b) =>
        b.Kind == BuildingKind.Hq ? 1 + Defs.UpgradeCount : Defs.Produces(b.Kind).Length;

    private void RefreshSelectionGroups(World world)
    {
        _selectionGroups.Clear();
        foreach (int id in _game.SelectedIds)
        {
            Unit u = world.GetUnit(id);
            if (u == null) continue;
            int i = _selectionGroups.FindIndex(g => g.Kind == u.Kind);
            if (i < 0) { _selectionGroups.Add(new SelectionGroup { Kind = u.Kind }); i = _selectionGroups.Count - 1; }
            SelectionGroup g = _selectionGroups[i];
            g.Count++;
            g.Hp += u.Hp;
            g.MaxHp += u.MaxHp;
            _selectionGroups[i] = g;
        }
    }

    /// <summary>Bottom row while units are selected: one card per unit kind with count and health. Tapping a card narrows the selection to that kind.</summary>
    private void DrawSelectionBar(World world)
    {
        RefreshSelectionGroups(world);
        if (_selectionGroups.Count == 0) return;
        Rect[] cards = CardRects(_selectionGroups.Count);

        for (int i = 0; i < _selectionGroups.Count; i++)
        {
            SelectionGroup g = _selectionGroups[i];
            Rect r = cards[i];
            if (GUI.Button(r, "", _button) && _selectionGroups.Count > 1) _game.SelectKind(g.Kind);

            float pad = r.height * 0.07f;
            int titleSize = Mathf.RoundToInt(r.height * 0.15f);
            if (_cardTitle == null || _cardTitle.fontSize != titleSize)
                _cardTitle = new GUIStyle(GUI.skin.label) { fontSize = titleSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _cardTitle.normal.textColor = UiTheme.Bone;
            GUI.Label(new Rect(r.x + pad * 1.6f, r.y + pad * 1.2f, r.width - pad * 3.2f, r.height * 0.26f), Strings.UnitName(g.Kind), _cardTitle);

            // Big count.
            int countSize = Mathf.RoundToInt(r.height * 0.34f);
            var countStyle = new GUIStyle(_cardTitle) { fontSize = countSize, alignment = TextAnchor.MiddleLeft };
            countStyle.normal.textColor = UiTheme.Accent;
            GUI.Label(new Rect(r.x + pad * 1.6f, r.y + r.height * 0.34f, r.width - pad * 3.2f, r.height * 0.36f), "x" + g.Count, countStyle);

            // Health bar of the whole group.
            float f = g.MaxHp > 0 ? Mathf.Clamp01(g.Hp / (float)g.MaxHp) : 0f;
            var bar = new Rect(r.x + pad * 1.6f, r.yMax - r.height * 0.2f, r.width - pad * 3.2f, r.height * 0.08f);
            Color old = GUI.color;
            GUI.color = new Color(0.04f, 0.05f, 0.03f, 0.9f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = f > 0.5f ? UiTheme.Good : f > 0.25f ? UiTheme.Warn : UiTheme.Alert;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * f, bar.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        float capH = Screen.height * 0.045f;
        UiTheme.Caption(new Rect(cards[0].x, cards[0].y - capH - Screen.height * 0.008f, Screen.width * 0.3f, capH), Strings.Get("hud.selected", _game.SelectedCount), UiTheme.Dim);
    }

    /// <summary>Dotted lines on the ground from every moving selected unit to where it is going: green for a move, red when it is going to attack.</summary>
    private void DrawOrderLines(World world)
    {
        if (_game.Cam == null || _game.SelectedCount == 0) return;
        Camera cam = _game.Cam.Camera;
        float spacing = Screen.height * 0.032f, dot = Screen.height * 0.009f;
        Color old = GUI.color;
        int drawn = 0;
        foreach (int id in _game.SelectedIds)
        {
            if (drawn >= 14) break; // enough to read the order; a big group would only clutter
            Unit u = world.GetUnit(id);
            if (u == null || !u.Moving) continue;
            drawn++;
            GUI.color = u.AttackKind != AttackKind.None ? new Color(UiTheme.Alert.r, UiTheme.Alert.g, UiTheme.Alert.b, 0.85f) : new Color(UiTheme.Good.r, UiTheme.Good.g, UiTheme.Good.b, 0.85f);

            Vector3 prev = cam.WorldToScreenPoint(UnitView.ToUnity(u.Pos));
            float carry = 0f;
            int dots = 0;
            int start = u.Path != null ? Mathf.Clamp(u.PathIndex, 0, u.Path.Length) : 0;
            int steps = (u.Path != null ? u.Path.Length - start : 0) + 1;
            for (int s = 0; s < steps && dots < 60; s++)
            {
                Vec2 wp = u.Path != null && start + s < u.Path.Length ? u.Path[start + s] : u.Target;
                Vector3 next = cam.WorldToScreenPoint(UnitView.ToUnity(wp));
                if (prev.z <= 0f || next.z <= 0f) { prev = next; continue; }
                float len = Vector2.Distance(prev, next);
                for (float d = spacing - carry; d <= len && dots < 60; d += spacing, dots++)
                {
                    Vector2 p = Vector2.Lerp(prev, next, d / len);
                    GUI.DrawTexture(new Rect(p.x - dot / 2f, Screen.height - p.y - dot / 2f, dot, dot), Texture2D.whiteTexture);
                }
                carry = (carry + len) % spacing;
                prev = next;
            }
        }
        GUI.color = old;
    }

    /// <summary>The HQ's base upgrade and research as cards in the bottom row.</summary>
    private void DrawHqCards(World world, PlayerState me)
    {
        Rect[] cards = CardRects(1 + Defs.UpgradeCount);

        UpgradeCheck up = world.CheckUpgradeBase(_game.LocalTeam);
        bool max = up == UpgradeCheck.MaxLevel;
        int level = max ? me.BaseLevel : me.BaseLevel + 1;
        int cost = max ? 0 : Economy.BaseUpgradeCost(me.BaseLevel);
        int secs = max ? 0 : Economy.BaseUpgradeSeconds(me.BaseLevel);
        if (Card(cards[0], Strings.Get("card.baseTo", level), cost, UiTheme.Icon.Clock, Strings.Get("card.seconds", secs), up == UpgradeCheck.Ok, null,
                 max ? Strings.Get("card.max") : null))
            _game.UpgradeBase();

        for (int k = 0; k < Defs.UpgradeCount; k++)
        {
            var kind = (UpgradeKind)k;
            UpgradeDef ud = Defs.Upgrade(kind);
            int tier = me.UpgradeTier(kind);
            ResearchCheck rc = world.CheckResearch(_game.LocalTeam, kind);
            string badge = Strings.Get("card.tier", ud.BonusMilli * tier / 10, tier, ud.Tiers);
            if (tier >= ud.Tiers)
            {
                Card(cards[1 + k], Strings.UpgradeName(kind), ud.Cost[ud.Tiers - 1], UiTheme.Icon.Clock, "", false, badge, Strings.Get("card.max"));
                continue;
            }
            string problem = rc == ResearchCheck.LevelTooLow ? Strings.Get("card.locked", ud.RequiredLevel[tier]) : null;
            if (Card(cards[1 + k], Strings.UpgradeName(kind), ud.Cost[tier], UiTheme.Icon.Clock, Strings.Get("card.seconds", ud.Seconds[tier]), rc == ResearchCheck.Ok, badge, problem))
                _game.Research(kind);
        }
    }
}
