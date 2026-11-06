using SimCore;
using UnityEngine;

/// <summary>Card-style buttons for the bottom row: building menu and unit production.</summary>
public sealed partial class DemoHud
{
    private GUIStyle _cardTitle;
    private GUIStyle _cardBadge;

    /// <summary>The row of cards above the bottom buttons, to the right of the minimap.</summary>
    private Rect[] CardRects(int count)
    {
        float gap = Screen.height * 0.012f;
        float bottom = BottomRects()[0].y - gap;
        float h = Screen.height * 0.2f;
        float left = (MinimapVisible ? MinimapRect().xMax : 0f) + gap;
        float avail = Screen.width - left - gap;
        float w = Mathf.Min(avail / Mathf.Max(count, 1) - gap, Screen.width * 0.15f);
        var rects = new Rect[count];
        // Cards hug the right edge so the thumb finds them next to the bottom buttons.
        float x = Screen.width - gap - count * (w + gap) + gap;
        for (int i = 0; i < count; i++) rects[i] = new Rect(x + i * (w + gap), bottom - h, w, h);
        return rects;
    }

    /// <summary>
    /// One card: title, price, a detail line and an optional count badge. Returns true on a tap.
    /// A <paramref name="problem"/> text replaces the detail line and tints the card red.
    /// </summary>
    private bool Card(Rect r, string title, int cost, UiTheme.Icon detailIcon, string detail, bool enabled, string badge = null, string problem = null)
    {
        // A disabled card is a plain plate: GUI.enabled would wash it out at half alpha.
        bool clicked = false;
        if (enabled) clicked = GUI.Button(r, "", _button);
        else GUI.Box(r, "");
        float a = enabled ? 1f : 0.8f;
        float pad = r.height * 0.07f;
        float rowH = r.height * 0.2f;

        // Accent stripe along the top edge: orange when you can use the card.
        Color stripe = problem != null ? UiTheme.Alert : (enabled ? UiTheme.Accent : UiTheme.Edge);
        stripe.a = a;
        Color old = GUI.color;
        GUI.color = stripe;
        GUI.DrawTexture(new Rect(r.x + r.height * 0.16f, r.y + 3f, r.width - r.height * 0.32f, Mathf.Max(3f, r.height * 0.03f)), Texture2D.whiteTexture);
        GUI.color = old;

        int titleSize = Mathf.RoundToInt(r.height * 0.15f);
        if (_cardTitle == null || _cardTitle.fontSize != titleSize)
            _cardTitle = new GUIStyle(GUI.skin.label) { fontSize = titleSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
        _cardTitle.normal.textColor = new Color(UiTheme.Bone.r, UiTheme.Bone.g, UiTheme.Bone.b, a);
        GUI.Label(new Rect(r.x + pad * 1.6f, r.y + pad * 1.2f, r.width - pad * 3.2f, rowH * 1.3f), title, _cardTitle);

        if (!string.IsNullOrEmpty(badge))
        {
            int badgeSize = Mathf.RoundToInt(r.height * 0.13f);
            if (_cardBadge == null || _cardBadge.fontSize != badgeSize)
                _cardBadge = new GUIStyle(GUI.skin.label) { fontSize = badgeSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };
            _cardBadge.normal.textColor = new Color(UiTheme.Dim.r, UiTheme.Dim.g, UiTheme.Dim.b, a);
            GUI.Label(new Rect(r.x + pad, r.y + r.height * 0.40f, r.width - pad * 2f, rowH), badge, _cardBadge);
        }

        Color price = enabled || problem != null ? UiTheme.Bone : UiTheme.Dim;
        price.a = a;
        UiTheme.IconLabel(new Rect(r.x + pad * 1.6f, r.y + r.height * 0.52f, r.width - pad * 3.2f, rowH), UiTheme.Icon.Resources, cost.ToString(), price);

        if (problem != null)
            UiTheme.IconLabel(new Rect(r.x + pad * 1.6f, r.y + r.height * 0.74f, r.width - pad * 3.2f, rowH), UiTheme.Icon.Alert, problem, UiTheme.Alert);
        else
            UiTheme.IconLabel(new Rect(r.x + pad * 1.6f, r.y + r.height * 0.74f, r.width - pad * 3.2f, rowH), detailIcon, detail, new Color(UiTheme.Dim.r, UiTheme.Dim.g, UiTheme.Dim.b, a));
        return clicked;
    }
}
