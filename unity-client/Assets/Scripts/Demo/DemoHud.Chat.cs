using SimCore;
using UnityEngine;

/// <summary>Ready-made chat phrases in an online match: a button, a small panel of phrases, and the opponent's phrase shown for a few seconds.</summary>
public sealed partial class DemoHud
{
    private const float ChatShownSeconds = 4f;
    private bool _chatOpen;
    private float _chatSentAt = -10f;

    private static Rect ChatPanelRect()
    {
        float w = Screen.width * 0.6f, h = Screen.height * 0.36f;
        return new Rect((Screen.width - w) / 2f, Screen.height * 0.3f, w, h);
    }

    private void DrawChat(Rect button)
    {
        OnlineSession online = _game.Online;
        if (online == null || _game.Outcome != DemoGame.GameOutcome.Playing) { _chatOpen = false; return; }

        if (GUI.Button(button, Strings.Get("chat.button"), _button)) _chatOpen = !_chatOpen;

        if (_chatOpen)
        {
            Rect panel = ChatPanelRect();
            GUI.Box(panel, "");
            int columns = 4, rows = (ChatPresets.Count + columns - 1) / columns;
            float gap = panel.height * 0.04f;
            float cw = (panel.width - gap * (columns + 1)) / columns, ch = (panel.height - gap * (rows + 1)) / rows;
            for (int i = 0; i < ChatPresets.Count; i++)
            {
                var r = new Rect(panel.x + gap + (i % columns) * (cw + gap), panel.y + gap + (i / columns) * (ch + gap), cw, ch);
                bool ready = Time.unscaledTime - _chatSentAt >= ChatPresets.MinSecondsBetween;
                GUI.enabled = ready;
                if (GUI.Button(r, Strings.Get("chat." + i), _button))
                {
                    online.SendChat(i);
                    _chatSentAt = Time.unscaledTime;
                    _chatOpen = false;
                }
                GUI.enabled = true;
            }
        }

        // What the opponent said last.
        if (online.LastChatPreset >= 0 && Time.realtimeSinceStartup - online.LastChatAt < ChatShownSeconds)
        {
            var style = new GUIStyle(_stats) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(Screen.height * 0.05f) };
            style.normal.textColor = new Color(0.6f, 0.9f, 1f);
            GUI.Label(new Rect(0f, Screen.height * 0.2f, Screen.width, Screen.height * 0.09f), Strings.Get("chat.opponent", Strings.Get("chat." + online.LastChatPreset)), style);
        }
    }
}
