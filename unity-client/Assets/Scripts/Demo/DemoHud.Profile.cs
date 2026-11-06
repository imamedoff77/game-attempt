using SimCore;
using UnityEngine;

/// <summary>The profile screen: who the player is, campaign progress and lifetime numbers as stat tiles.</summary>
public sealed partial class DemoHud
{
    private void DrawProfile()
    {
        float w = Screen.width, h = Screen.height;
        DrawPageTitle(Strings.Get("profile.title"));
        DrawPageChips(null, _game.Progress.Credits.ToString());

        bool signedIn = _game.Account.SignedIn && _game.Account.User != null;
        AccountUser me = signedIn ? _game.Account.User : null;

        // header: avatar, name, rating line
        var head = new Rect(w * 0.08f, h * 0.17f, w * 0.84f, h * 0.15f);
        StripePlate(head, UiTheme.Accent);
        float av = head.height * 0.7f;
        GUI.DrawTexture(new Rect(head.x + head.height * 0.3f, head.y + (head.height - av) / 2f, av, av), UiTheme.IconTexture(UiTheme.Icon.Person));
        float tx = head.x + head.height * 1.2f;
        PageText(new Rect(tx, head.y + head.height * 0.08f, head.width * 0.6f, head.height * 0.5f), signedIn ? me.displayName : Strings.Get("profile.guest"), 0.058f, UiTheme.Bone);
        string sub = me != null ? Strings.Get("profile.ratingLine", me.rating > 0 ? me.rating : Rating.Start, me.ratedWins, me.ratedGames) : Strings.Get("profile.ratingNone");
        PageText(new Rect(tx, head.y + head.height * 0.55f, head.width * 0.7f, head.height * 0.35f), sub, 0.035f, UiTheme.Dim);

        int armoryLevels = 0, armoryMax = 0;
        foreach (UnitKind kind in ArmoryOrder)
        {
            armoryLevels += _game.Armory.Level(kind);
            armoryMax += Armory.MaxLevel;
        }
        PlayerStats s = _game.Stats;

        string[,] tiles =
        {
            { _game.Progress.TotalStars + " / " + Campaign.Missions.Count * 3, Strings.Get("profile.stars") },
            { _game.Progress.Credits.ToString(), Strings.Get("profile.credits") },
            { armoryLevels + " / " + armoryMax, Strings.Get("profile.armory") },
            { _game.Daily.Streak.ToString(), Strings.Get("profile.streak") },
            { me != null ? (me.rating > 0 ? me.rating : Rating.Start).ToString() : "-", Strings.Get("profile.rating") },
            { s.MatchesPlayed.ToString(), Strings.Get("profile.matches") },
            { s.MatchesWon + " (" + s.WinPercent + "%)", Strings.Get("profile.wins") },
            { s.MissionsWon.ToString(), Strings.Get("profile.missions") },
            { s.UnitsBuilt.ToString(), Strings.Get("profile.units") },
            { s.EnemiesDestroyed.ToString(), Strings.Get("profile.kills") },
        };
        const int columns = 5;
        float gap = h * 0.025f;
        float tw = (w * 0.84f - gap * (columns - 1)) / columns, th = h * 0.215f;
        float x0 = w * 0.08f, y0 = h * 0.37f;
        for (int i = 0; i < 10; i++)
        {
            var r = new Rect(x0 + (i % columns) * (tw + gap), y0 + (i / columns) * (th + gap), tw, th);
            GUI.Box(r, "");
            Color stripe = i < columns ? UiTheme.Accent : UiTheme.Edge;
            Color old = GUI.color;
            GUI.color = stripe;
            GUI.DrawTexture(new Rect(r.x + r.height * 0.08f, r.y + 3f, r.width - r.height * 0.16f, Mathf.Max(4f, r.height * 0.03f)), Texture2D.whiteTexture);
            GUI.color = old;
            PageText(new Rect(r.x, r.y + r.height * 0.12f, r.width, r.height * 0.45f), tiles[i, 0], 0.062f, UiTheme.Bone, TextAnchor.MiddleCenter);
            PageText(new Rect(r.x + r.width * 0.06f, r.y + r.height * 0.58f, r.width * 0.88f, r.height * 0.36f), tiles[i, 1], 0.03f, UiTheme.Dim, TextAnchor.UpperCenter, false, true);
        }

        if (!signedIn)
            PageText(new Rect(w * 0.2f, h * 0.86f, w * 0.7f, h * 0.1f), Strings.Get("profile.guestHint"), 0.03f, UiTheme.Dim, TextAnchor.MiddleLeft, false, true);
        DrawBackButton(0.1f);
    }
}
