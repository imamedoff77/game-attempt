using SimCore;
using UnityEngine;

/// <summary>The Online page of the main menu: create a room and share its code, or join a friend's room.</summary>
public sealed partial class DemoHud
{
    private string _onlineCode = "";

    private void DrawOnlinePage()
    {
        float w = Screen.width, h = Screen.height;
        var title = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(h * 0.08f) };
        GUI.Label(new Rect(0f, h * 0.03f, w, h * 0.12f), Strings.Get("online.title"), title);

        var text = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = Mathf.RoundToInt(h * 0.04f) };
        Rect body = new Rect(w * 0.2f, h * 0.2f, w * 0.6f, h * 0.12f);

        if (!_game.Account.SignedIn)
        {
            GUI.Label(body, Strings.Get("online.needAccount"), text);
            if (GUI.Button(new Rect(w * 0.35f, h * 0.38f, w * 0.3f, h * 0.11f), Strings.Get("online.openAccount"), _button)) OpenAccountPage();
        }
        else if (!_game.Progress.OnlineUnlocked && !Debug.isDebugBuild) // development builds skip the lock so online play can be tried at once
        {
            GUI.Label(body, Strings.Get("online.locked", Campaign.OnlineUnlockMission, Mathf.Min(_game.Progress.NextMission - 1, Campaign.OnlineUnlockMission)), text);
            if (GUI.Button(new Rect(w * 0.35f, h * 0.38f, w * 0.3f, h * 0.11f), Strings.Get("online.openCampaign"), _button)) _page = MenuPage.Campaign;
        }
        else if (OnlineSettings.ServerAddress.Length == 0)
        {
            GUI.Label(body, Strings.Get("online.noServer"), text);
        }
        else
        {
            OnlineSession online = _game.Online;
            bool busy = online != null && online.State != OnlineSession.Phase.Ended;
            if (busy && online.Searching)
            {
                GUI.Label(new Rect(w * 0.2f, h * 0.3f, w * 0.6f, h * 0.3f), Strings.Get("online.searching", Mathf.FloorToInt((float)online.SearchSeconds)), text);
                if (GUI.Button(new Rect(w * 0.35f, h * 0.7f, w * 0.3f, h * 0.11f), Strings.Get("online.cancel"), _button)) _game.LeaveOnline();
            }
            else if (busy)
            {
                bool waiting = online.State == OnlineSession.Phase.Waiting && !string.IsNullOrEmpty(online.RoomCode);
                GUI.Label(body, waiting ? Strings.Get("online.shareCode") : Strings.Get("online.connecting"), text);
                if (waiting)
                {
                    var code = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(h * 0.18f) };
                    GUI.Label(new Rect(0f, h * 0.35f, w, h * 0.25f), online.RoomCode, code);
                }
                if (GUI.Button(new Rect(w * 0.35f, h * 0.7f, w * 0.3f, h * 0.11f), Strings.Get("online.cancel"), _button)) _game.LeaveOnline();
            }
            else
            {
                var note = new Rect(w * 0.15f, h * 0.13f, w * 0.7f, h * 0.11f);
                if (online != null) // the last attempt ended: say why
                {
                    var error = new GUIStyle(text);
                    error.normal.textColor = new Color(1f, 0.55f, 0.5f);
                    GUI.Label(note, online.Status, error);
                }
                else GUI.Label(note, Strings.Get("online.intro"), text);

                AccountUser me = _game.Account.User;
                if (me != null)
                {
                    var rating = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(h * 0.035f) };
                    GUI.Label(new Rect(w * 0.2f, h * 0.25f, w * 0.6f, h * 0.06f),
                        Strings.Get("online.rating", me.rating > 0 ? me.rating : Rating.Start, me.ratedGames, me.ratedWins), rating);
                }

                if (GUI.Button(new Rect(w * 0.3f, h * 0.33f, w * 0.4f, h * 0.12f), Strings.Get("online.find"), _button))
                    _game.QuickMatchOnline(OnlineSettings.ServerAddress);

                var rankedNote = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter, wordWrap = true, fontSize = Mathf.RoundToInt(h * 0.027f) };
                GUI.Label(new Rect(w * 0.25f, h * 0.455f, w * 0.5f, h * 0.045f), Strings.Get("online.rankedNote"), rankedNote);

                if (GUI.Button(new Rect(w * 0.3f, h * 0.52f, w * 0.4f, h * 0.11f), Strings.Get("online.create"), _button))
                    _game.HostOnline(OnlineSettings.ServerAddress);

                var small = new GUIStyle(_label) { alignment = TextAnchor.LowerLeft, fontSize = Mathf.RoundToInt(h * 0.03f) };
                GUI.Label(new Rect(w * 0.3f, h * 0.65f, w * 0.4f, h * 0.045f), Strings.Get("online.code"), small);
                _onlineCode = GUI.TextField(new Rect(w * 0.3f, h * 0.7f, w * 0.18f, h * 0.1f), _onlineCode, MessageCodec.MaxCodeLength, _accField ?? _button).ToUpperInvariant();
                GUI.enabled = _onlineCode.Length > 0;
                if (GUI.Button(new Rect(w * 0.5f, h * 0.7f, w * 0.2f, h * 0.1f), Strings.Get("online.join"), _button))
                    _game.JoinOnline(OnlineSettings.ServerAddress, _onlineCode);
                GUI.enabled = true;
            }
        }

        var back = new Rect(w * 0.04f, h - h * 0.14f, w * 0.1f, h * 0.1f);
        if (GUI.Button(back, Strings.Get("menu.back"), _button)) { _game.LeaveOnline(); _page = MenuPage.Main; }
    }
}

/// <summary>Where the game server is (Resources/gameserver.txt, "host:port"; not in git). Empty when none is set.</summary>
public static class OnlineSettings
{
    public static string ServerAddress
    {
        get
        {
            TextAsset preset = Resources.Load<TextAsset>("gameserver");
            return preset != null ? preset.text.Trim() : "";
        }
    }
}
