using System;
using UnityEngine;

/// <summary>
/// The look of the interface: palette, cut-corner plates and a few small icons, all drawn in code (no art files).
/// Olive-charcoal metal, bone-coloured text, one signal-orange accent. Team colours stay blue and red.
/// </summary>
public static class UiTheme
{
    public static readonly Color Ink = new Color(0.10f, 0.11f, 0.09f, 0.92f);      // panel body
    public static readonly Color PlateTop = new Color(0.27f, 0.29f, 0.22f, 0.97f);  // button, lit side
    public static readonly Color PlateBottom = new Color(0.15f, 0.16f, 0.12f, 0.97f);
    public static readonly Color Edge = new Color(0.47f, 0.49f, 0.37f, 1f);         // steel-olive outline
    public static readonly Color Bone = new Color(0.90f, 0.88f, 0.79f, 1f);         // main text
    public static readonly Color Dim = new Color(0.65f, 0.64f, 0.55f, 1f);          // secondary text
    public static readonly Color Accent = new Color(0.91f, 0.51f, 0.17f, 1f);       // signal orange
    public static readonly Color Good = new Color(0.58f, 0.78f, 0.36f, 1f);
    public static readonly Color Warn = new Color(0.93f, 0.74f, 0.24f, 1f);
    public static readonly Color Alert = new Color(0.86f, 0.31f, 0.25f, 1f);

    public enum Icon { Resources, Energy, Units, Clock, Alert, Build, Pause, Flag, Cross, Target, Globe, Star, Person, Stop, Shield, Advance, Box, Rotate, Upgrade, Train }

    private static Texture2D[] _icons;
    private static GUIStyle _chipText;
    private static GUIStyle _caption;
    private static GUIStyle _iconLabel;
    private static GUIStyle _btnText;
    private static int _chipTextHeight;

    /// <summary>One status chip: plate, icon, value. An alert colour recolours the outline and the number.</summary>
    public static void Chip(Rect r, Icon icon, string value, Color? alert = null)
    {
        GUI.Box(r, "");
        if (alert.HasValue) Outline(r, alert.Value, Mathf.Max(2f, r.height * 0.045f));

        float pad = r.height * 0.14f;
        float iconSize = r.height - pad * 2f;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(r.x + pad * 1.3f, r.y + pad, iconSize, iconSize), IconTexture(icon));

        GUIStyle style = ChipText(r.height);
        Color old = style.normal.textColor;
        style.normal.textColor = alert ?? Bone;
        GUI.Label(new Rect(r.x + pad * 1.3f + iconSize, r.y, r.width - iconSize - pad * 2.6f, r.height), value, style);
        style.normal.textColor = old;
    }

    /// <summary>Small secondary text under a chip row, on its own dark plate so it reads on any ground.</summary>
    public static void Caption(Rect r, string text, Color? color = null)
    {
        int size = Mathf.RoundToInt(r.height * 0.72f);
        if (_caption == null || _caption.fontSize != size)
            _caption = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
        _caption.normal.textColor = color ?? Dim;
        float width = _caption.CalcSize(new GUIContent(text)).x + r.height * 0.9f;
        GUI.Box(new Rect(r.x, r.y, width, r.height), "");
        GUI.Label(new Rect(r.x + r.height * 0.45f, r.y, width, r.height), text, _caption);
    }

    /// <summary>A small icon followed by text, vertically centred in the rectangle.</summary>
    public static void IconLabel(Rect r, Icon icon, string text, Color color)
    {
        float size = r.height * 0.9f;
        GUI.color = new Color(1f, 1f, 1f, color.a);
        GUI.DrawTexture(new Rect(r.x, r.y + (r.height - size) / 2f, size, size), IconTexture(icon));
        GUI.color = Color.white;
        int fontSize = Mathf.RoundToInt(r.height * 0.78f);
        if (_iconLabel == null || _iconLabel.fontSize != fontSize)
            _iconLabel = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
        _iconLabel.normal.textColor = color;
        GUI.Label(new Rect(r.x + size * 1.15f, r.y, r.width - size * 1.15f, r.height), text, _iconLabel);
    }

    /// <summary>An optional icon and text centred together in a rectangle (for button faces).</summary>
    public static void CenteredLabel(Rect r, Icon? icon, string text, Color color)
    {
        int fontSize = Mathf.RoundToInt(r.height * 0.34f);
        if (_btnText == null || _btnText.fontSize != fontSize)
            _btnText = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        _btnText.normal.textColor = color;
        float size = r.height * 0.5f;
        float textW = string.IsNullOrEmpty(text) ? 0f : _btnText.CalcSize(new GUIContent(text)).x;
        float total = (icon.HasValue ? size : 0f) + (icon.HasValue && textW > 0f ? size * 0.3f : 0f) + textW;
        float x = r.center.x - total / 2f;
        if (icon.HasValue)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(x, r.center.y - size / 2f, size, size), IconTexture(icon.Value));
            x += size * 1.3f;
        }
        if (textW > 0f) GUI.Label(new Rect(x, r.y, textW + 4f, r.height), text, _btnText);
    }

    /// <summary>Text with a dark drop shadow so it reads over any ground.</summary>
    public static void ShadowLabel(Rect r, string text, GUIStyle style, Color color)
    {
        Color old = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, 0.85f * color.a);
        float o = Mathf.Max(1f, style.fontSize * 0.07f);
        GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, style);
        style.normal.textColor = color;
        GUI.Label(r, text, style);
        style.normal.textColor = old;
    }

    /// <summary>A thin coloured frame inside a rectangle.</summary>
    public static void Outline(Rect r, Color color, float t)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    private static GUIStyle ChipText(float height)
    {
        int size = Mathf.RoundToInt(height * 0.44f);
        if (_chipText == null || _chipTextHeight != size)
        {
            _chipTextHeight = size;
            _chipText = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Overflow };
        }
        return _chipText;
    }

    // ---------------------------------------------------------------- plates

    /// <summary>Nine-slice plate with 45-degree cut corners and a one-pixel outline.</summary>
    public static Texture2D Plate(Color top, Color bottom, Color border)
    {
        const int n = 40;
        const float cut = 12f;
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            Color fill = Color.Lerp(bottom, top, y / (n - 1f));
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Min(x + 0.5f, n - x - 0.5f);
                float dy = Mathf.Min(y + 0.5f, n - y - 0.5f);
                float d = Mathf.Min(Mathf.Min(dx, dy), (dx + dy - cut) * 0.7071f); // distance to the outline, positive inside
                float coverage = Mathf.Clamp01(d + 0.5f);
                Color c = Color.Lerp(border, fill, Mathf.Clamp01((d - 0.8f) / 1.2f));
                c.a *= coverage;
                pixels[y * n + x] = c;
            }
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // ---------------------------------------------------------------- icons

    public static Texture2D IconTexture(Icon icon)
    {
        if (_icons == null) _icons = new Texture2D[Enum.GetValues(typeof(Icon)).Length];
        Texture2D cached = _icons[(int)icon];
        if (cached != null) return cached;

        Func<Vector2, Color> shader;
        switch (icon)
        {
            case Icon.Resources: shader = CrateShader; break;
            case Icon.Energy: shader = BoltShader; break;
            case Icon.Units: shader = HelmetShader; break;
            case Icon.Alert: shader = AlertShader; break;
            case Icon.Build: shader = CraneShader; break;
            case Icon.Pause: shader = PauseShader; break;
            case Icon.Flag: shader = FlagShader; break;
            case Icon.Cross: shader = CrossShader; break;
            case Icon.Target: shader = TargetShader; break;
            case Icon.Globe: shader = GlobeShader; break;
            case Icon.Star: shader = StarShader; break;
            case Icon.Person: shader = PersonShader; break;
            case Icon.Stop: shader = StopShader; break;
            case Icon.Shield: shader = ShieldShader; break;
            case Icon.Advance: shader = AdvanceShader; break;
            case Icon.Box: shader = BoxShader; break;
            case Icon.Rotate: shader = RotateShader; break;
            case Icon.Upgrade: shader = UpgradeShader; break;
            case Icon.Train: shader = TrainShader; break;
            default: shader = ClockShader; break;
        }
        _icons[(int)icon] = Render(shader);
        return _icons[(int)icon];
    }

    /// <summary>Renders a shader (unit square, y up) into a 64x64 texture with 2x2 supersampling.</summary>
    private static Texture2D Render(Func<Vector2, Color> shader)
    {
        const int n = 64;
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                Color sum = Color.clear;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        Color c = shader(new Vector2((x + (sx + 0.5f) / 2f) / n, (y + (sy + 0.5f) / 2f) / n));
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                sum /= 4f;
                pixels[y * n + x] = sum.a > 0.001f ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : new Color32(0, 0, 0, 0);
            }
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>Supply crate: khaki box with a dark strap cross.</summary>
    private static Color CrateShader(Vector2 p)
    {
        if (p.x < 0.10f || p.x > 0.90f || p.y < 0.16f || p.y > 0.84f) return Color.clear;
        bool edge = p.x < 0.15f || p.x > 0.85f || p.y < 0.21f || p.y > 0.79f;
        if (edge) return new Color(0.38f, 0.33f, 0.20f, 1f);
        bool strap = Mathf.Abs(p.x - p.y - 0.0f) < 0.07f || Mathf.Abs(p.x + p.y - 1f) < 0.07f;
        return strap ? new Color(0.45f, 0.38f, 0.22f, 1f) : new Color(0.80f, 0.69f, 0.40f, 1f);
    }

    /// <summary>Lightning bolt in amber.</summary>
    private static Color BoltShader(Vector2 p)
    {
        Vector2[] bolt =
        {
            new Vector2(0.60f, 0.94f), new Vector2(0.22f, 0.46f), new Vector2(0.45f, 0.46f),
            new Vector2(0.36f, 0.06f), new Vector2(0.78f, 0.58f), new Vector2(0.54f, 0.58f),
        };
        return InPolygon(bolt, p) ? Warn : Color.clear;
    }

    /// <summary>Soldier helmet: dome, brim and a dark band.</summary>
    private static Color HelmetShader(Vector2 p)
    {
        Vector2 c = new Vector2(0.5f, 0.42f);
        float dx = p.x - c.x, dy = p.y - c.y;
        bool dome = dy >= 0f && dx * dx / (0.36f * 0.36f) + dy * dy / (0.40f * 0.40f) <= 1f;
        bool brim = p.y >= 0.26f && p.y <= 0.42f && Mathf.Abs(dx) <= 0.44f;
        if (!dome && !brim) return Color.clear;
        if (p.y >= 0.34f && p.y <= 0.42f) return new Color(0.36f, 0.40f, 0.28f, 1f);
        return Bone;
    }

    /// <summary>Tower crane: post, arm and a hanging hook.</summary>
    private static Color CraneShader(Vector2 p)
    {
        bool post = p.x > 0.16f && p.x < 0.30f && p.y > 0.08f && p.y < 0.90f;
        bool foot = p.x > 0.08f && p.x < 0.38f && p.y > 0.08f && p.y < 0.16f;
        bool arm = p.x > 0.16f && p.x < 0.92f && p.y > 0.76f && p.y < 0.88f;
        bool rope = p.x > 0.74f && p.x < 0.80f && p.y > 0.44f && p.y < 0.78f;
        bool hook = p.x > 0.66f && p.x < 0.88f && p.y > 0.32f && p.y < 0.46f;
        return post || foot || arm || rope || hook ? Bone : Color.clear;
    }

    /// <summary>Pole with a pennant: the rally point.</summary>
    private static Color FlagShader(Vector2 p)
    {
        bool pole = p.x > 0.20f && p.x < 0.30f && p.y > 0.06f && p.y < 0.94f;
        Vector2[] pennant = { new Vector2(0.30f, 0.92f), new Vector2(0.88f, 0.72f), new Vector2(0.30f, 0.52f) };
        return pole || InPolygon(pennant, p) ? Bone : Color.clear;
    }

    /// <summary>X mark: cancel.</summary>
    private static Color CrossShader(Vector2 p)
    {
        bool inside = p.x > 0.14f && p.x < 0.86f && p.y > 0.14f && p.y < 0.86f;
        bool d1 = Mathf.Abs(p.x - p.y) < 0.075f;
        bool d2 = Mathf.Abs(p.x + p.y - 1f) < 0.075f;
        return inside && (d1 || d2) ? Alert : Color.clear;
    }

    /// <summary>Crosshair: ring, cross and a dot.</summary>
    private static Color TargetShader(Vector2 p)
    {
        float r = (p - new Vector2(0.5f, 0.5f)).magnitude;
        bool ring = r > 0.30f && r < 0.40f;
        bool dot = r < 0.07f;
        bool cross = (Mathf.Abs(p.x - 0.5f) < 0.035f || Mathf.Abs(p.y - 0.5f) < 0.035f) && r > 0.18f && r < 0.48f;
        return ring || dot || cross ? Bone : Color.clear;
    }

    /// <summary>Globe: circle outline with a meridian and two parallels.</summary>
    private static Color GlobeShader(Vector2 p)
    {
        Vector2 d = p - new Vector2(0.5f, 0.5f);
        float r = d.magnitude;
        if (r > 0.42f) return Color.clear;
        bool rim = r > 0.35f;
        bool meridian = Mathf.Abs(d.x) < 0.03f || Mathf.Abs(d.x * d.x / (0.18f * 0.18f) + d.y * d.y / (0.42f * 0.42f) - 1f) < 0.12f;
        bool parallel = Mathf.Abs(d.y - 0.14f) < 0.03f || Mathf.Abs(d.y + 0.14f) < 0.03f || Mathf.Abs(d.y) < 0.03f;
        return rim || meridian || parallel ? Bone : Color.clear;
    }

    /// <summary>Five-point star.</summary>
    private static Color StarShader(Vector2 p)
    {
        var pts = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
            float rad = i % 2 == 0 ? 0.44f : 0.19f;
            pts[i] = new Vector2(0.5f + Mathf.Cos(a) * rad, 0.5f + Mathf.Sin(a) * rad);
        }
        return InPolygon(pts, p) ? Warn : Color.clear;
    }

    /// <summary>Head and shoulders.</summary>
    private static Color PersonShader(Vector2 p)
    {
        bool head = (p - new Vector2(0.5f, 0.68f)).magnitude < 0.19f;
        Vector2 b = p - new Vector2(0.5f, 0.12f);
        bool body = b.y >= 0f && b.x * b.x / (0.38f * 0.38f) + b.y * b.y / (0.36f * 0.36f) <= 1f;
        return head || body ? Bone : Color.clear;
    }

    /// <summary>Solid square: stop.</summary>
    private static Color StopShader(Vector2 p) =>
        p.x > 0.2f && p.x < 0.8f && p.y > 0.2f && p.y < 0.8f ? Bone : Color.clear;

    /// <summary>Heater shield: hold position.</summary>
    private static Color ShieldShader(Vector2 p)
    {
        Vector2[] outer = { new Vector2(0.16f, 0.88f), new Vector2(0.84f, 0.88f), new Vector2(0.84f, 0.50f), new Vector2(0.5f, 0.08f), new Vector2(0.16f, 0.50f) };
        if (!InPolygon(outer, p)) return Color.clear;
        Vector2[] inner = { new Vector2(0.26f, 0.78f), new Vector2(0.74f, 0.78f), new Vector2(0.74f, 0.52f), new Vector2(0.5f, 0.22f), new Vector2(0.26f, 0.52f) };
        return InPolygon(inner, p) ? new Color(0.30f, 0.34f, 0.24f, 1f) : Bone;
    }

    /// <summary>Two chevrons pointing up: advance and fight.</summary>
    private static Color AdvanceShader(Vector2 p)
    {
        for (int i = 0; i < 2; i++)
        {
            float oy = 0.12f + i * 0.30f;
            float d = Mathf.Abs(p.x - 0.5f);
            float ridge = oy + 0.42f - d * 0.9f; // upper edge of the chevron
            if (d < 0.38f && p.y < ridge && p.y > ridge - 0.14f) return Bone;
        }
        return Color.clear;
    }

    /// <summary>Dashed selection frame with a handle in each corner: box select.</summary>
    private static Color BoxShader(Vector2 p)
    {
        const float lo = 0.12f, hi = 0.88f, t = 0.055f;
        bool inside = p.x >= lo && p.x <= hi && p.y >= lo && p.y <= hi;
        if (!inside) return Color.clear;
        bool corner = (p.x < lo + 0.16f || p.x > hi - 0.16f) && (p.y < lo + 0.16f || p.y > hi - 0.16f);
        bool band = p.x < lo + t || p.x > hi - t || p.y < lo + t || p.y > hi - t;
        if (corner && band) return Bone;
        bool dash = (Mathf.FloorToInt((p.x + p.y) * 11f) & 1) == 0;
        if (band && dash) return Bone;
        bool handle = (p.x < lo + 0.14f || p.x > hi - 0.14f) && (p.y < lo + 0.14f || p.y > hi - 0.14f);
        return handle ? Accent : Color.clear;
    }

    /// <summary>Circular arrow: rotate.</summary>
    private static Color RotateShader(Vector2 p)
    {
        Vector2 d = p - new Vector2(0.5f, 0.5f);
        float r = d.magnitude;
        float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        if (ang < 0f) ang += 360f;
        if (r > 0.26f && r < 0.38f && ang >= 55f) return Bone;
        Vector2[] head = { new Vector2(0.5f + 0.32f - 0.14f, 0.5f), new Vector2(0.5f + 0.32f + 0.14f, 0.5f), new Vector2(0.5f + 0.32f, 0.5f + 0.24f) };
        return InPolygon(head, p) ? Accent : Color.clear;
    }

    /// <summary>Arrow pointing up over a base line: upgrade.</summary>
    private static Color UpgradeShader(Vector2 p)
    {
        Vector2[] head = { new Vector2(0.5f, 0.92f), new Vector2(0.14f, 0.52f), new Vector2(0.86f, 0.52f) };
        bool stem = p.x > 0.38f && p.x < 0.62f && p.y > 0.22f && p.y <= 0.53f;
        bool bar = p.x > 0.18f && p.x < 0.82f && p.y > 0.08f && p.y < 0.16f;
        return InPolygon(head, p) || stem ? Good : bar ? Bone : Color.clear;
    }

    /// <summary>A helmet with a plus: train units.</summary>
    private static Color TrainShader(Vector2 p)
    {
        Vector2 q = (p - new Vector2(0.38f, 0.52f)) / 0.78f + new Vector2(0.5f, 0.42f);
        Color helmet = HelmetShader(q);
        if (helmet.a > 0f) return helmet;
        bool plusH = Mathf.Abs(p.y - 0.24f) < 0.06f && Mathf.Abs(p.x - 0.78f) < 0.17f;
        bool plusV = Mathf.Abs(p.x - 0.78f) < 0.06f && Mathf.Abs(p.y - 0.24f) < 0.17f;
        return plusH || plusV ? Good : Color.clear;
    }

    /// <summary>Two vertical bars.</summary>
    private static Color PauseShader(Vector2 p)
    {
        bool bars = p.y > 0.18f && p.y < 0.82f && ((p.x > 0.24f && p.x < 0.42f) || (p.x > 0.58f && p.x < 0.76f));
        return bars ? Bone : Color.clear;
    }

    /// <summary>Warning triangle with an exclamation mark.</summary>
    private static Color AlertShader(Vector2 p)
    {
        Vector2[] tri = { new Vector2(0.5f, 0.92f), new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.12f) };
        if (!InPolygon(tri, p)) return Color.clear;
        bool bar = Mathf.Abs(p.x - 0.5f) < 0.06f && p.y > 0.36f && p.y < 0.68f;
        bool dot = (p - new Vector2(0.5f, 0.26f)).magnitude < 0.07f;
        return bar || dot ? new Color(0.14f, 0.12f, 0.08f, 1f) : Alert;
    }

    /// <summary>Clock face: ring with two hands.</summary>
    private static Color ClockShader(Vector2 p)
    {
        Vector2 c = new Vector2(0.5f, 0.5f);
        float r = (p - c).magnitude;
        if (r > 0.42f) return Color.clear;
        if (r > 0.34f) return Bone;
        bool minute = Mathf.Abs(p.x - 0.5f) < 0.035f && p.y >= 0.5f && p.y <= 0.78f;
        bool hour = Mathf.Abs(p.y - 0.5f) < 0.035f && p.x >= 0.5f && p.x <= 0.70f;
        return minute || hour ? Bone : new Color(0.17f, 0.18f, 0.14f, 1f);
    }

    private static bool InPolygon(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }
}
