using UnityEngine;

/// <summary>Gives the on-screen buttons and panels one look: rounded dark slate boxes with a light edge. Called from OnGUI, once.</summary>
public static class UiSkin
{
    private static bool _applied;

    public static void Apply()
    {
        if (_applied) return;
        _applied = true;

        GUISkin skin = GUI.skin;
        var edge = new RectOffset(14, 14, 14, 14);

        Texture2D panel = Rounded(new Color(0.07f, 0.10f, 0.14f, 0.90f), new Color(0.07f, 0.10f, 0.14f, 0.90f), new Color(0.38f, 0.50f, 0.62f, 0.95f));
        skin.box.normal.background = panel;
        skin.box.border = edge;

        Texture2D idle = Rounded(new Color(0.23f, 0.31f, 0.40f, 0.96f), new Color(0.12f, 0.17f, 0.23f, 0.96f), new Color(0.52f, 0.67f, 0.82f, 1f));
        Texture2D pressed = Rounded(new Color(0.36f, 0.50f, 0.64f, 1f), new Color(0.22f, 0.33f, 0.45f, 1f), new Color(0.82f, 0.92f, 1f, 1f));
        GUIStyle b = skin.button;
        b.border = edge;
        b.normal.background = b.hover.background = b.focused.background = b.onNormal.background = b.onHover.background = b.onFocused.background = idle;
        b.active.background = b.onActive.background = pressed;
        b.normal.textColor = b.hover.textColor = b.active.textColor = b.focused.textColor = Color.white;
        b.onNormal.textColor = b.onHover.textColor = b.onActive.textColor = b.onFocused.textColor = Color.white;
    }

    /// <summary>A rounded rectangle with a vertical gradient and a one-pixel edge, for nine-slice drawing.</summary>
    private static Texture2D Rounded(Color top, Color bottom, Color border)
    {
        const int n = 48;
        const float radius = 14f;
        var pixels = new Color32[n * n];
        float half = n * 0.5f;
        for (int y = 0; y < n; y++)
        {
            Color fill = Color.Lerp(bottom, top, y / (n - 1f));
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy) - radius; // negative inside
                float coverage = Mathf.Clamp01(0.5f - distance);
                Color c = Color.Lerp(fill, border, Mathf.Clamp01((distance + 2.2f) / 1.2f));
                c.a *= coverage;
                pixels[y * n + x] = c;
            }
        }
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }
}
