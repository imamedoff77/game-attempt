using UnityEngine;

/// <summary>The main hub: account and credits on top, three big mode cards, a nav row at the bottom. Plus the shared page title.</summary>
public sealed partial class DemoHud
{
    private GUIStyle _hubTitle;
    private GUIStyle _hubSub;
    private GUIStyle _hubWord;

    /// <summary>Title of a menu page: bold, centred, with a short orange bar under it.</summary>
    private void DrawPageTitle(string text)
    {
        int size = Mathf.RoundToInt(Screen.height * 0.075f);
        if (_hubTitle == null || _hubTitle.fontSize != size)
            _hubTitle = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        var r = new Rect(0f, Screen.height * 0.03f, Screen.width, Screen.height * 0.11f);
        UiTheme.ShadowLabel(r, text, _hubTitle, UiTheme.Bone);
        float bw = Screen.width * 0.1f, bh = Mathf.Max(3f, Screen.height * 0.006f);
        Color old = GUI.color;
        GUI.color = UiTheme.Accent;
        GUI.DrawTexture(new Rect((Screen.width - bw) / 2f, r.yMax + Screen.height * 0.003f, bw, bh), Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>Two small chips at the top right of a menu page: a star total (optional) and the credits.</summary>
    private void DrawPageChips(string stars, string credits)
    {
        float h = Screen.height * 0.075f, pad = Screen.height * 0.02f;
        float cw = Screen.width * 0.13f;
        float x = Screen.width - pad - cw;
        UiTheme.Chip(new Rect(x, pad, cw, h), UiTheme.Icon.Resources, credits);
        if (stars != null) UiTheme.Chip(new Rect(x - cw - pad * 0.6f, pad, cw, h), UiTheme.Icon.Star, stars);
    }

    /// <summary>A big tappable card: pictogram, title and a one-line description under an accent stripe.</summary>
    private bool ModeCard(Rect r, UiTheme.Icon icon, string title, string sub, Color stripe, string badge = null)
    {
        bool clicked = GUI.Button(r, "", _button);
        Color old = GUI.color;
        GUI.color = stripe;
        GUI.DrawTexture(new Rect(r.x + r.height * 0.1f, r.y + 3f, r.width - r.height * 0.2f, Mathf.Max(4f, r.height * 0.018f)), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float iconSize = r.height * 0.36f;
        GUI.DrawTexture(new Rect(r.center.x - iconSize / 2f, r.y + r.height * 0.1f, iconSize, iconSize), UiTheme.IconTexture(icon));
        GUI.color = old;

        int titleSize = Mathf.RoundToInt(r.height * 0.11f), subSize = Mathf.RoundToInt(r.height * 0.065f);
        if (_hubSub == null || _hubSub.fontSize != subSize)
        {
            _hubSub = new GUIStyle(GUI.skin.label) { fontSize = subSize, alignment = TextAnchor.UpperCenter, wordWrap = true };
            _hubWord = new GUIStyle(GUI.skin.label) { fontSize = titleSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }
        _hubWord.fontSize = titleSize;
        _hubWord.normal.textColor = UiTheme.Bone;
        _hubSub.normal.textColor = UiTheme.Dim;
        GUI.Label(new Rect(r.x, r.y + r.height * 0.52f, r.width, r.height * 0.16f), title, _hubWord);
        GUI.Label(new Rect(r.x + r.width * 0.08f, r.y + r.height * 0.70f, r.width * 0.84f, r.height * 0.26f), sub, _hubSub);

        if (!string.IsNullOrEmpty(badge))
        {
            float b = r.height * 0.09f;
            Rect dot = new Rect(r.xMax - b * 1.8f, r.y + b * 0.8f, b, b);
            GUI.color = UiTheme.Accent;
            GUI.DrawTexture(dot, Texture2D.whiteTexture);
            GUI.color = old;
        }
        return clicked;
    }

    private void DrawHub()
    {
        float pad = Screen.height * 0.02f;

        // Top row: who you are, sound, credits.
        float topH = Screen.height * 0.09f;
        string who = _game.Account.SignedIn && _game.Account.User != null ? _game.Account.User.displayName : Strings.Get("account.menuSignIn");
        if (IconButton(new Rect(pad, pad, Screen.width * 0.26f, topH), UiTheme.Icon.Person, who)) OpenAccountPage();
        float creditsW = Screen.width * 0.15f;
        UiTheme.Chip(new Rect(Screen.width - creditsW - pad, pad, creditsW, topH), UiTheme.Icon.Resources, _game.Progress.Credits.ToString());
        float soundW = Screen.width * 0.13f;
        if (IconButton(new Rect(Screen.width - creditsW - soundW - pad * 1.6f, pad, soundW, topH), null, Strings.Get(Sfx.Enabled ? "sound.on" : "sound.off"))) Sfx.Enabled = !Sfx.Enabled;

        // Wordmark.
        int size = Mathf.RoundToInt(Screen.height * 0.12f);
        var word = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        UiTheme.ShadowLabel(new Rect(0f, Screen.height * 0.12f, Screen.width, Screen.height * 0.17f), Application.productName.ToUpperInvariant(), word, UiTheme.Bone);

        // Mode cards.
        float cw = Screen.width * 0.25f, ch = Screen.height * 0.42f, gap = Screen.width * 0.025f;
        float x0 = (Screen.width - (cw * 3f + gap * 2f)) / 2f, y = Screen.height * 0.32f;
        if (ModeCard(new Rect(x0, y, cw, ch), UiTheme.Icon.Flag, Strings.Get("mode.campaign"), Strings.Get("hub.campaignSub"), UiTheme.Accent)) _page = MenuPage.Campaign;
        if (ModeCard(new Rect(x0 + cw + gap, y, cw, ch), UiTheme.Icon.Target, Strings.Get("mode.vsComputer"), Strings.Get("hub.vsSub"), UiTheme.Good)) _page = MenuPage.Difficulty;
        if (ModeCard(new Rect(x0 + (cw + gap) * 2f, y, cw, ch), UiTheme.Icon.Globe, Strings.Get("mode.online"), Strings.Get("hub.onlineSub"), UiTheme.Alert)) _page = MenuPage.Online;

        // Bottom navigation.
        int count = 4;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        count = 5;
#endif
        float nh = Screen.height * 0.12f, ny = Screen.height - nh - pad;
        float nw = (Screen.width - pad * (count + 1)) / count;
        int i = 0;
        if (IconButton(new Rect(pad + i++ * (nw + pad), ny, nw, nh), UiTheme.Icon.Units, Strings.Get("mode.armory"))) _page = MenuPage.Armory;
        var dailyRect = new Rect(pad + i++ * (nw + pad), ny, nw, nh);
        bool dailyReady = _game.Daily.HasSomethingToClaim(DemoGame.Today);
        if (IconButton(dailyRect, UiTheme.Icon.Star, Strings.Get("mode.daily"), dailyReady)) _page = MenuPage.Daily;
        if (IconButton(new Rect(pad + i++ * (nw + pad), ny, nw, nh), UiTheme.Icon.Resources, Strings.Get("store.title"))) OpenStorePage();
        if (IconButton(new Rect(pad + i++ * (nw + pad), ny, nw, nh), UiTheme.Icon.Person, Strings.Get("profile.title"))) _page = MenuPage.Profile;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IconButton(new Rect(pad + i++ * (nw + pad), ny, nw, nh), UiTheme.Icon.Build, Strings.Get("mode.sandbox"))) _game.StartDemo();
#endif
    }

    private void DrawDifficulty()
    {
        DrawPageTitle(Strings.Get("mode.vsComputer"));
        float cw = Screen.width * 0.25f, ch = Screen.height * 0.42f, gap = Screen.width * 0.025f;
        float x0 = (Screen.width - (cw * 3f + gap * 2f)) / 2f, y = Screen.height * 0.25f;
        Color[] colours = { UiTheme.Good, UiTheme.Warn, UiTheme.Alert };
        UiTheme.Icon[] icons = { UiTheme.Icon.Person, UiTheme.Icon.Units, UiTheme.Icon.Target };
        foreach (SimCore.AiDifficulty d in System.Enum.GetValues(typeof(SimCore.AiDifficulty)))
        {
            int k = (int)d;
            if (ModeCard(new Rect(x0 + k * (cw + gap), y, cw, ch), icons[Mathf.Min(k, 2)], Strings.DifficultyName(d), Strings.Get("hub.diff." + d), colours[Mathf.Min(k, 2)]))
            {
                _page = MenuPage.Main;
                _game.StartSkirmish(d);
            }
        }
        var back = new Rect(Screen.width * 0.04f, Screen.height - Screen.height * 0.14f, Screen.width * 0.16f, Screen.height * 0.1f);
        if (IconButton(back, null, Strings.Get("menu.back"))) _page = MenuPage.Main;
    }
}
