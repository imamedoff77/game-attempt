using SimCore;
using UnityEngine;

/// <summary>The profile screen: who the player is, campaign progress and lifetime numbers.</summary>
public sealed partial class DemoHud
{
    private void DrawProfile()
    {
        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.08f) };
        GUI.Label(new Rect(0f, Screen.height * 0.03f, Screen.width, Screen.height * 0.12f), Strings.Get("profile.title"), title);

        bool signedIn = _game.Account.SignedIn && _game.Account.User != null;
        var who = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.05f) };
        GUI.Label(new Rect(0f, Screen.height * 0.15f, Screen.width, Screen.height * 0.08f), signedIn ? _game.Account.User.displayName : Strings.Get("profile.guest"), who);

        int armoryLevels = 0, armoryMax = 0;
        foreach (UnitKind kind in ArmoryOrder)
        {
            armoryLevels += _game.Armory.Level(kind);
            armoryMax += Armory.MaxLevel;
        }

        PlayerStats s = _game.Stats;
        AccountUser me = signedIn ? _game.Account.User : null;
        string[] left =
        {
            Strings.Get("profile.stars", _game.Progress.TotalStars, Campaign.Missions.Count * 3),
            Strings.Get("profile.credits", _game.Progress.Credits),
            Strings.Get("profile.armory", armoryLevels, armoryMax),
            Strings.Get("profile.streak", _game.Daily.Streak),
            me != null ? Strings.Get("profile.rating", me.rating > 0 ? me.rating : Rating.Start, me.ratedWins, me.ratedGames) : Strings.Get("profile.ratingNone"),
        };
        string[] right =
        {
            Strings.Get("profile.matches", s.MatchesPlayed),
            Strings.Get("profile.wins", s.MatchesWon, s.WinPercent),
            Strings.Get("profile.missions", s.MissionsWon),
            Strings.Get("profile.units", s.UnitsBuilt),
            Strings.Get("profile.kills", s.EnemiesDestroyed),
        };
        var line = new GUIStyle(_label) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(Screen.height * 0.042f) };
        float colW = Screen.width * 0.36f, rowH = Screen.height * 0.095f, top = Screen.height * 0.27f;
        DrawProfileColumn(left, Screen.width * 0.12f, top, colW, rowH, line);
        DrawProfileColumn(right, Screen.width * 0.52f, top, colW, rowH, line);

        if (!signedIn)
        {
            var hint = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(Screen.height * 0.03f) };
            GUI.Label(new Rect(Screen.width * 0.2f, Screen.height * 0.85f, Screen.width * 0.6f, Screen.height * 0.08f), Strings.Get("profile.guestHint"), hint);
        }

        var back = new Rect(Screen.width * 0.04f, Screen.height - Screen.height * 0.14f, Screen.width * 0.1f, Screen.height * 0.1f);
        if (GUI.Button(back, Strings.Get("menu.back"), _button)) _page = MenuPage.Main;
    }

    private void DrawProfileColumn(string[] lines, float x, float y, float w, float h, GUIStyle style)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            GUI.Box(new Rect(x, y + i * h, w, h * 0.88f), "");
            GUI.Label(new Rect(x + w * 0.05f, y + i * h, w * 0.92f, h * 0.88f), lines[i], style);
        }
    }
}
