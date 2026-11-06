using UnityEngine;

/// <summary>Gives the on-screen buttons and panels one look: cut-corner olive-charcoal plates (see UiTheme). Called from OnGUI, once.</summary>
public static class UiSkin
{
    private static bool _applied;

    public static void Apply()
    {
        if (_applied) return;
        _applied = true;

        GUISkin skin = GUI.skin;
        var edge = new RectOffset(14, 14, 14, 14);

        Texture2D panel = UiTheme.Plate(UiTheme.Ink, UiTheme.Ink, UiTheme.Edge);
        skin.box.normal.background = panel;
        skin.box.border = edge;

        Texture2D idle = UiTheme.Plate(UiTheme.PlateTop, UiTheme.PlateBottom, UiTheme.Edge);
        Texture2D pressed = UiTheme.Plate(new Color(0.78f, 0.45f, 0.16f, 1f), new Color(0.55f, 0.29f, 0.09f, 1f), UiTheme.Accent);
        GUIStyle b = skin.button;
        b.border = edge;
        b.normal.background = b.hover.background = b.focused.background = b.onNormal.background = b.onHover.background = b.onFocused.background = idle;
        b.active.background = b.onActive.background = pressed;
        b.normal.textColor = b.hover.textColor = b.active.textColor = b.focused.textColor = UiTheme.Bone;
        b.onNormal.textColor = b.onHover.textColor = b.onActive.textColor = b.onFocused.textColor = UiTheme.Bone;
    }
}
