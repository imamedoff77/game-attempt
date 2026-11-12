using System.Collections.Generic;
using SimCore;
using UnityEngine;

/// <summary>Ordering units without picking a factory, building upgrades, and the signals over upgrading buildings and attack targets.</summary>
public sealed partial class DemoHud
{
    /// <summary>The unit kinds of the "Train" panel: infantry first, then vehicles, each group from the cheapest tier up.</summary>
    private static UnitKind[] _trainUnits;
    private static UnitKind[] TrainUnits => _trainUnits ?? (_trainUnits = BuildTrainOrder()); // needs the balance data, so not built at type load

    private static UnitKind[] BuildTrainOrder()
    {
        var kinds = new List<UnitKind>((UnitKind[])System.Enum.GetValues(typeof(UnitKind)));
        kinds.Sort((a, b) =>
        {
            UnitDef x = Defs.Unit(a), y = Defs.Unit(b);
            int c = x.Producer.CompareTo(y.Producer);
            if (c != 0) return c;
            c = x.RequiredLevel.CompareTo(y.RequiredLevel);
            return c != 0 ? c : x.Cost.CompareTo(y.Cost);
        });
        return kinds.ToArray();
    }

    /// <summary>The short reason a unit card is greyed out (null when there is nothing to say).</summary>
    private static string QueueProblem(QueueCheck check, UnitDef unit, BuildingKind producer)
    {
        switch (check)
        {
            case QueueCheck.NoBuilding: return Strings.Get("train.noBuilding", Strings.BuildingName(producer));
            case QueueCheck.LevelTooLow: return Strings.Get("card.locked", unit.RequiredLevel);
            case QueueCheck.BuildingLevelTooLow: return Strings.Get("train.buildingLevel", Strings.BuildingName(producer), unit.RequiredLevel);
            case QueueCheck.QueueFull: return Strings.Get("card.queueFull");
            case QueueCheck.PopCapReached: return Strings.Get("card.popFull");
            default: return null;
        }
    }

    /// <summary>
    /// The cards of every unit you could ever make. A tap orders one from the least busy factory that can make it;
    /// a card that cannot be used says why (no such building, building or base level too low, no resources, queues full).
    /// </summary>
    private void DrawTrainPanel(World world)
    {
        Rect[] cards = CardRects(TrainUnits.Length);
        for (int i = 0; i < TrainUnits.Length; i++)
        {
            UnitKind kind = TrainUnits[i];
            UnitDef ud = Defs.Unit(kind);
            QueueCheck check = world.CheckQueueAny(_game.LocalTeam, kind);
            string problem = QueueProblem(check, ud, ud.Producer);
            if (check == QueueCheck.NotEnoughResources) problem = Strings.Get("train.noMoney");

            int queued = 0, factories = 0;
            foreach (Building b in world.Buildings)
            {
                if (b.Team != _game.LocalTeam || b.Kind != ud.Producer || b.UnderConstruction) continue;
                factories++;
                foreach (UnitKind q in b.Queue) if (q == kind) queued++;
            }
            string badge = queued > 0 ? Strings.Get("card.queued", queued) : factories > 1 ? Strings.Get("card.owned", factories) + " " + Strings.BuildingName(ud.Producer) : null;
            if (Card(cards[i], Strings.UnitName(kind), ud.Cost, UiTheme.Icon.Clock, Strings.Get("card.seconds", ud.BuildSeconds), check == QueueCheck.Ok, badge, problem))
                _game.QueueAnywhere(kind);
        }
        GUI.enabled = true;
    }

    /// <summary>The upgrade button of a factory in the building panel: next level, price and time, or why it cannot be done.</summary>
    private void DrawBuildingUpgradeButton(Rect r, World world, Building b, PlayerState me)
    {
        BuildingDef def = Defs.Building(b.Kind);
        UpgradeCheck check = world.CheckUpgradeBuilding(b);
        string text;
        switch (check)
        {
            case UpgradeCheck.MaxLevel: text = Strings.Get("panel.upgradeMax"); break;
            case UpgradeCheck.AlreadyUpgrading: text = Strings.Get("panel.upgradeBusy"); break;
            case UpgradeCheck.BaseLevelTooLow: text = Strings.Get("panel.upgradeNeedsBase", b.Level + 1); break;
            case UpgradeCheck.NotEnoughResources: text = Strings.Get("panel.upgradeNeedsMoney", b.Level + 1, def.UpgradeCost[b.Level - 1]); break;
            case UpgradeCheck.Ok: text = Strings.Get("panel.upgrade", b.Level + 1, def.UpgradeCost[b.Level - 1], def.UpgradeSeconds[b.Level - 1]); break;
            default: text = ""; break;
        }
        GUI.enabled = check == UpgradeCheck.Ok;
        if (IconButton(r, UiTheme.Icon.Upgrade, text)) _game.UpgradeSelectedBuilding();
        GUI.enabled = true;
    }

    /// <summary>A progress bar with the percentage over every building of yours that is being upgraded (the base included).</summary>
    private void DrawUpgradeBars(World world)
    {
        if (_game.Cam == null) return;
        PlayerState me = world.Player(_game.LocalTeam);
        float w = Screen.height * 0.2f, h = Screen.height * 0.026f;
        int textSize = Mathf.RoundToInt(h * 1.3f);
        if (_upgradeStyle == null || _upgradeStyle.fontSize != textSize)
            _upgradeStyle = new GUIStyle(GUI.skin.label) { fontSize = textSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };

        foreach (Building b in world.Buildings)
        {
            if (b.Team != _game.LocalTeam || b.Hp <= 0) continue;
            int percent;
            if (b.Upgrading) percent = world.UpgradeProgressMilli(b) / 10;
            else if (b.Kind == BuildingKind.Hq && me.Upgrading) percent = UpgradePercent(me);
            else continue;

            Vector3 sp = _game.Cam.Camera.WorldToScreenPoint(new Vector3(b.X + b.Width / 2f, 3.4f, b.Y + b.Height / 2f));
            if (sp.z <= 0f) continue;
            var back = new Rect(sp.x - w / 2f, Screen.height - sp.y - h / 2f, w, h);
            Color old = GUI.color;
            GUI.color = new Color(0.06f, 0.07f, 0.05f, 0.9f);
            GUI.DrawTexture(new Rect(back.x - 2f, back.y - 2f, back.width + 4f, back.height + 4f), Texture2D.whiteTexture);
            GUI.color = UiTheme.Accent;
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * percent / 100f, back.height), Texture2D.whiteTexture);
            GUI.color = old;
            UiTheme.ShadowLabel(new Rect(back.x, back.y - h * 1.7f, back.width, h * 1.6f), Strings.Get("hud.upgradeBar", percent), _upgradeStyle, UiTheme.Accent);
        }
    }

    private GUIStyle _upgradeStyle;

    /// <summary>A pulsing red target mark over every enemy the selected units were ordered to attack.</summary>
    private void DrawAttackTargets(World world)
    {
        if (_game.Cam == null || _game.SelectedCount == 0) return;
        Camera cam = _game.Cam.Camera;
        float size = Screen.height * (0.075f + 0.012f * Mathf.Sin(Time.unscaledTime * 8f));
        var seen = new HashSet<long>();
        Color old = GUI.color;
        GUI.color = new Color(1f, 0.28f, 0.2f, 0.95f);
        foreach (int id in _game.SelectedIds)
        {
            if (seen.Count >= 6) break;
            Unit u = world.GetUnit(id);
            if (u == null || u.Hp <= 0 || !u.Ordered || u.AttackKind == AttackKind.None) continue;
            if (!seen.Add(((long)(int)u.AttackKind << 32) | (uint)u.AttackId)) continue;

            Vector3 at;
            if (u.AttackKind == AttackKind.Unit)
            {
                Unit target = world.GetUnit(u.AttackId);
                if (target == null || target.Hp <= 0) continue;
                at = new Vector3(target.Pos.X.ToFloat(), 2.3f, target.Pos.Y.ToFloat());
            }
            else
            {
                Building target = world.GetBuilding(u.AttackId);
                if (target == null || target.Hp <= 0) continue;
                at = new Vector3(target.X + target.Width / 2f, 3.8f, target.Y + target.Height / 2f);
            }
            Vector3 sp = cam.WorldToScreenPoint(at);
            if (sp.z <= 0f) continue;
            GUI.DrawTexture(new Rect(sp.x - size / 2f, Screen.height - sp.y - size / 2f, size, size), UiTheme.IconTexture(UiTheme.Icon.Target));
        }
        GUI.color = old;
    }
}
