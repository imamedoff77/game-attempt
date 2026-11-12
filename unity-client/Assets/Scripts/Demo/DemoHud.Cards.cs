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

    /// <summary>A toolbar button with an icon and/or text on its face. An active one is drawn on the orange plate.</summary>
    private bool IconButton(Rect r, UiTheme.Icon? icon, string text, bool active = false)
    {
        bool clicked = GUI.Button(r, "", active ? UiSkin.ActiveButton : _button);
        float a = GUI.enabled ? 1f : 0.5f;
        Color color = active ? new Color(0.12f, 0.09f, 0.05f, a) : new Color(UiTheme.Bone.r, UiTheme.Bone.g, UiTheme.Bone.b, a);
        UiTheme.CenteredLabel(r, icon, text, color);
        return clicked;
    }

    private GUIStyle _infoTitle;
    private GUIStyle _infoSmall;

    /// <summary>The selected building's plate: name, a status line, queue slots with progress, and base info for the HQ.</summary>
    private void DrawBuildingInfo(Rect r, World world, Building b, PlayerState me)
    {
        GUI.Box(r, "");
        float pad = r.height * 0.07f;
        float x = r.x + pad * 1.6f, w = r.width - pad * 3.2f;

        int titleSize = Mathf.RoundToInt(r.height * 0.19f);
        int smallSize = Mathf.RoundToInt(r.height * 0.14f);
        if (_infoTitle == null || _infoTitle.fontSize != titleSize)
        {
            _infoTitle = new GUIStyle(GUI.skin.label) { fontSize = titleSize, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
            _infoSmall = new GUIStyle(GUI.skin.label) { fontSize = smallSize, fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Clip };
        }
        _infoTitle.normal.textColor = UiTheme.Bone;
        string title = Strings.BuildingName(b.Kind);
        if (Defs.Building(b.Kind).MaxLevel > 1) title += "  " + Strings.Get("panel.level", b.Level);
        GUI.Label(new Rect(x, r.y + pad, w * 0.62f, r.height * 0.26f), title, _infoTitle);

        // Status on the right of the title line.
        string status;
        Color statusColor;
        if (b.UnderConstruction) { status = Strings.Get("panel.constructing", ConstructionPercent(b)); statusColor = UiTheme.Accent; }
        else if (!b.Powered) { status = Strings.Get("panel.noEnergy"); statusColor = UiTheme.Alert; }
        else if (b.Upgrading) { status = Strings.Get("panel.upgrading", world.UpgradeProgressMilli(b) / 10); statusColor = UiTheme.Accent; }
        else if (b.Kind == BuildingKind.Hq && me.Upgrading) { status = Strings.Get("panel.upgrading", UpgradePercent(me)); statusColor = UiTheme.Accent; }
        else { status = Strings.Get("panel.working"); statusColor = UiTheme.Good; }
        _infoSmall.alignment = TextAnchor.MiddleRight;
        _infoSmall.normal.textColor = statusColor;
        GUI.Label(new Rect(x + w * 0.45f, r.y + pad, w * 0.55f, r.height * 0.26f), status, _infoSmall);

        // Queue slots.
        UnitKind[] produces = Defs.Produces(b.Kind);
        float y = r.y + r.height * 0.36f;
        if (!b.UnderConstruction && produces.Length > 0)
        {
            const int slots = 5;
            float gap = pad * 0.7f;
            float sw = (w - gap * (slots - 1)) / slots, sh = r.height * 0.3f;
            _infoSmall.alignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < slots; i++)
            {
                var slot = new Rect(x + i * (sw + gap), y, sw, sh);
                Color old = GUI.color;
                GUI.color = new Color(0.04f, 0.05f, 0.03f, 0.9f);
                GUI.DrawTexture(slot, Texture2D.whiteTexture);
                if (i < b.Queue.Count)
                {
                    if (i == 0)
                    {
                        GUI.color = UiTheme.Good;
                        GUI.DrawTexture(new Rect(slot.x, slot.yMax - sh * 0.14f, slot.width * Percent(b) / 100f, sh * 0.14f), Texture2D.whiteTexture);
                    }
                    GUI.color = old;
                    _infoSmall.normal.textColor = UiTheme.Bone;
                    string name = Strings.UnitName(b.Queue[i]);
                    GUI.Label(slot, name.Length > 6 ? name.Substring(0, 6) : name, _infoSmall);
                }
                GUI.color = old;
                UiTheme.Outline(slot, i < b.Queue.Count ? UiTheme.Edge : new Color(0.25f, 0.27f, 0.2f, 1f), 1.5f);
            }
            y += sh + pad * 0.8f;
        }

        if (!b.UnderConstruction && b.Kind == BuildingKind.Hq)
        {
            string line = me.Upgrading ? Strings.Get("hud.upgrading", UpgradePercent(me)) : Strings.Get("hud.baseLevel", me.BaseLevel);
            if (me.Researching) line = Strings.Get("panel.researching", Strings.UpgradeName(me.ResearchKind), ResearchPercent(me)).Trim();
            _infoSmall.alignment = TextAnchor.MiddleLeft;
            _infoSmall.normal.textColor = me.Upgrading || me.Researching ? UiTheme.Accent : UiTheme.Dim;
            GUI.Label(new Rect(x, y, w, r.height * 0.2f), line, _infoSmall);
        }
    }
}
