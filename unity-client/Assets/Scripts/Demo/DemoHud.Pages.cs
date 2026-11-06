using System.Collections.Generic;
using UnityEngine;

/// <summary>Small drawing helpers shared by the menu sub-pages: styled text, row plates with a colour stripe, progress bars.</summary>
public sealed partial class DemoHud
{
    private readonly Dictionary<int, GUIStyle> _pageStyles = new Dictionary<int, GUIStyle>();

    /// <summary>Text with a cached style: size is a fraction of the screen height.</summary>
    private void PageText(Rect r, string text, float size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = true, bool wrap = false)
    {
        int px = Mathf.Max(8, Mathf.RoundToInt(Screen.height * size));
        int key = px * 64 + (int)anchor * 4 + (bold ? 2 : 0) + (wrap ? 1 : 0);
        if (!_pageStyles.TryGetValue(key, out GUIStyle style))
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = px, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, alignment = anchor, wordWrap = wrap, clipping = TextClipping.Clip };
            _pageStyles[key] = style;
        }
        style.normal.textColor = color;
        GUI.Label(r, text, style);
    }

    /// <summary>A row/tile plate with a coloured stripe along its left edge.</summary>
    private void StripePlate(Rect r, Color stripe)
    {
        GUI.Box(r, "");
        Color old = GUI.color;
        GUI.color = stripe;
        GUI.DrawTexture(new Rect(r.x + 3f, r.y + r.height * 0.1f, Mathf.Max(5f, r.height * 0.045f), r.height * 0.8f), Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>A flat progress bar on a dark track.</summary>
    private static void PageBar(Rect r, float fraction, Color fill)
    {
        Color old = GUI.color;
        GUI.color = new Color(0.04f, 0.05f, 0.03f, 0.9f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = fill;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>The hint line at the bottom of a page, on a dark plate beside the Back button.</summary>
    private void PageHint(string text)
    {
        var r = new Rect(Screen.width * 0.2f, Screen.height * 0.86f, Screen.width * 0.72f, Screen.height * 0.1f);
        GUI.Box(r, "");
        PageText(new Rect(r.x + r.height * 0.2f, r.y, r.width - r.height * 0.4f, r.height), text, 0.028f, UiTheme.Dim, TextAnchor.MiddleLeft, false, true);
    }

    private void DrawBackButton(float widthFraction = 0.12f)
    {
        var back = new Rect(Screen.width * 0.04f, Screen.height * 0.86f, Screen.width * widthFraction, Screen.height * 0.1f);
        if (IconButton(back, null, Strings.Get("menu.back"))) _page = MenuPage.Main;
    }
}
