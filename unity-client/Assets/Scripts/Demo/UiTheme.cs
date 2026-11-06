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

    public enum Icon { Resources, Energy, Units, Clock, Alert, Build, Pause }

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

    private static Texture2D IconTexture(Icon icon)
    {
        if (_icons == null) _icons = new Texture2D[7];
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
