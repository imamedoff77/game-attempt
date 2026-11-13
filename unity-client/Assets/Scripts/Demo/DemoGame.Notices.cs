using System.Collections.Generic;
using SimCore;
using UnityEngine;

public enum NoticeKind { Info, Good, Alert }

/// <summary>One message for the player ("under attack", "construction finished"...). Optionally points at a place on the map.</summary>
public struct GameNotice
{
    public NoticeKind Kind;
    public string Text;
    public bool HasPos;
    public Vector3 Pos;
}

/// <summary>Watches the match for things the player should hear about and queues them for the HUD. View-side only: nothing here touches the simulation.</summary>
public sealed partial class DemoGame
{
    /// <summary>Filled here, emptied by the HUD every frame.</summary>
    public readonly List<GameNotice> Notices = new List<GameNotice>();

    /// <summary>Where and when the last attack on you was seen (for the minimap ping).</summary>
    public Vector3 LastAttackPos { get; private set; }
    public float LastAttackTime { get; private set; } = -100f;

    private World _noticeWorld;
    private readonly HashSet<int> _noticeConstructing = new HashSet<int>();
    private readonly Dictionary<int, (int team, BuildingKind kind, Vector3 pos)> _noticeBuildings = new Dictionary<int, (int, BuildingKind, Vector3)>();
    private readonly Dictionary<int, int> _noticeLevels = new Dictionary<int, int>();
    private int _noticeBaseLevel;
    private bool _noticeShortage;
    private readonly int[] _noticeTiers = new int[8];
    private int _lastAttackTick = -100000;

    private void Notify(NoticeKind kind, string text, Vector3? pos = null)
    {
        Notices.Add(new GameNotice { Kind = kind, Text = text, HasPos = pos.HasValue, Pos = pos ?? Vector3.zero });
    }

    /// <summary>Called once per frame after the views are in sync.</summary>
    private void UpdateNotices(bool catchingUp)
    {
        if (World == null || Mode == GameMode.Menu) return;
        PlayerState me = World.Player(LocalTeam);

        if (_noticeWorld != World)
        {
            // A new match: learn the starting situation without announcing it.
            _noticeWorld = World;
            Notices.Clear();
            _noticeConstructing.Clear();
            _noticeBuildings.Clear();
            _noticeLevels.Clear();
            _noticeBaseLevel = me.BaseLevel;
            _noticeShortage = false;
            for (int k = 0; k < Defs.UpgradeCount && k < _noticeTiers.Length; k++) _noticeTiers[k] = me.UpgradeTier((UpgradeKind)k);
            _lastAttackTick = -100000;
            LastAttackTime = -100f;
        }
        if (catchingUp || Outcome != GameOutcome.Playing) return;

        // Buildings: finished, lost.
        var seen = new HashSet<int>();
        foreach (Building b in World.Buildings)
        {
            seen.Add(b.Id);
            Vector3 centre = new Vector3(b.X + b.Width / 2f, 0f, b.Y + b.Height / 2f);
            _noticeBuildings[b.Id] = (b.Team, b.Kind, centre);
            if (b.Team != LocalTeam) continue;
            if (_noticeLevels.TryGetValue(b.Id, out int knownLevel) && b.Level > knownLevel)
            {
                Notify(NoticeKind.Good, Strings.Get("notice.factoryUp", Strings.BuildingName(b.Kind), b.Level), centre);
                Sfx.Play("upgrade", 0.6f, 0f);
            }
            _noticeLevels[b.Id] = b.Level;
            if (b.UnderConstruction) _noticeConstructing.Add(b.Id);
            else if (_noticeConstructing.Remove(b.Id))
            {
                Notify(NoticeKind.Good, Strings.Get("notice.built", Strings.BuildingName(b.Kind)), centre);
                Sfx.Play("built", 0.6f, 0f);
            }
        }
        _removeNoticeBuffer.Clear();
        foreach (var pair in _noticeBuildings)
            if (!seen.Contains(pair.Key)) _removeNoticeBuffer.Add(pair.Key);
        foreach (int id in _removeNoticeBuffer)
        {
            var info = _noticeBuildings[id];
            _noticeBuildings.Remove(id);
            _noticeConstructing.Remove(id);
            if (info.team == LocalTeam) Notify(NoticeKind.Alert, Strings.Get("notice.lost", Strings.BuildingName(info.kind)), info.pos);
        }

        // Base level and research.
        if (me.BaseLevel > _noticeBaseLevel)
        {
            Notify(NoticeKind.Good, Strings.Get("notice.baseUp", me.BaseLevel));
            Sfx.Play("upgrade", 0.7f, 0f);
        }
        _noticeBaseLevel = me.BaseLevel;
        for (int k = 0; k < Defs.UpgradeCount && k < _noticeTiers.Length; k++)
        {
            int tier = me.UpgradeTier((UpgradeKind)k);
            if (tier > _noticeTiers[k]) Notify(NoticeKind.Good, Strings.Get("notice.research", Strings.UpgradeName((UpgradeKind)k)));
            _noticeTiers[k] = tier;
        }

        // Power: say it once when it runs out.
        if (me.EnergyShortage && !_noticeShortage) Notify(NoticeKind.Alert, Strings.Get("notice.power"));
        _noticeShortage = me.EnergyShortage;
    }

    private readonly List<int> _removeNoticeBuffer = new List<int>();

    /// <summary>An enemy shot that lands next to something of yours counts as "under attack" (at most once every ten seconds).</summary>
    private void NoticeShot(ShotEvent shot)
    {
        if (shot.Team == LocalTeam || Mode == GameMode.Menu || Outcome != GameOutcome.Playing) return;
        if (World.Tick - _lastAttackTick < 10 * SimCore.World.TicksPerSecond) return;

        Vector3 to = UnitView.ToUnity(shot.To);
        bool hitsMine = false;
        foreach (Unit u in World.Units)
        {
            if (u.Team != LocalTeam) continue;
            Vector3 p = UnitView.ToUnity(u.Pos);
            if ((p.x - to.x) * (p.x - to.x) + (p.z - to.z) * (p.z - to.z) < 9f) { hitsMine = true; break; }
        }
        if (!hitsMine)
            foreach (Building b in World.Buildings)
            {
                if (b.Team != LocalTeam) continue;
                if (to.x >= b.X - 1f && to.x <= b.X + b.Width + 1f && to.z >= b.Y - 1f && to.z <= b.Y + b.Height + 1f) { hitsMine = true; break; }
            }
        if (!hitsMine) return;

        _lastAttackTick = World.Tick;
        LastAttackPos = to;
        LastAttackTime = Time.unscaledTime;
        Notify(NoticeKind.Alert, Strings.Get("notice.attack"), to);
        Sfx.Play("alarm", 0.7f, 0f);
    }
}
