using SimCore;
using UnityEngine;

/// <summary>Shortcut for the current language's <see cref="StringTable"/>. 1.0 ships English only; every visible text goes through here.</summary>
public static class Strings
{
    private static StringTable _table;

    /// <summary>The game's name: set from game.json when the app is built, so it is never typed in code or texts.</summary>
    public static string GameName => Application.productName;

    public static StringTable Table => _table;

    public static void Load(string json)
    {
        var table = new StringTable(json);
        table.OnMissing = key => Debug.LogWarning("[Strings] missing text for key \"" + key + "\"");
        _table = table;
    }

    public static string Get(string key) => _table != null ? _table.Get(key) : key;

    public static string Get(string key, params object[] args) => _table != null ? _table.Get(key, args) : key;

    public static string UnitName(UnitKind kind) => Get("unit." + kind);

    public static string BuildingName(BuildingKind kind) => Get("building." + kind);

    public static string UpgradeName(UpgradeKind kind) => Get("upgrade." + kind);

    public static string DifficultyName(AiDifficulty difficulty) => Get("difficulty." + difficulty);

    public static string MissionName(int number) => Get("mission." + number + ".name");

    public static string MissionBrief(int number) => Get("mission." + number + ".brief");

    public static string TutorialText(int step) => Get("tutorial." + step);

    public static string DailyTaskText(DailyTask task)
    {
        string key = "daily.task." + task.Kind + (task.Kind == DailyTaskKind.WinMission && task.Target > 1 ? "s" : "");
        return Get(key, task.Target);
    }
}
