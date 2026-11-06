using System.Collections.Generic;
using UnityEngine;

/// <summary>Short messages under the top bar: attacks, finished buildings, losses. A banner that points at a place can be tapped to look there.</summary>
public sealed partial class DemoHud
{
    private struct Toast
    {
        public GameNotice Notice;
        public float Born;
    }

    private const float ToastLife = 4.5f;
    private const int MaxToasts = 3;
    private readonly List<Toast> _toasts = new List<Toast>();
    private GUIStyle _toastText;

    private void UpdateToasts()
    {
        float now = Time.unscaledTime;
        foreach (GameNotice n in _game.Notices)
        {
            int same = _toasts.FindIndex(t => t.Notice.Text == n.Text);
            if (same >= 0) _toasts.RemoveAt(same); // the same message again: show it fresh instead of stacking
            _toasts.Insert(0, new Toast { Notice = n, Born = now });
        }
        _game.Notices.Clear();
        _toasts.RemoveAll(t => now - t.Born > ToastLife);
        while (_toasts.Count > MaxToasts) _toasts.RemoveAt(_toasts.Count - 1);
    }

    private Rect ToastRect(int index)
    {
        float w = Screen.width * 0.3f, h = Screen.height * 0.075f, gap = Screen.height * 0.01f;
        return new Rect((Screen.width - w) / 2f, Screen.height * 0.12f + index * (h + gap), w, h);
    }

    private IEnumerable<Rect> ToastRects()
    {
        for (int i = 0; i < _toasts.Count; i++) yield return ToastRect(i);
    }

    private void DrawToasts()
    {
        float now = Time.unscaledTime;
        Color saved = GUI.color;
        for (int i = 0; i < _toasts.Count; i++)
        {
            Toast t = _toasts[i];
            float age = now - t.Born;
            float alpha = Mathf.Min(1f, age / 0.2f, (ToastLife - age) / 0.6f);
            Rect r = ToastRect(i);

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Box(r, "");

            Color accent = t.Notice.Kind == NoticeKind.Alert ? UiTheme.Alert : t.Notice.Kind == NoticeKind.Good ? UiTheme.Good : UiTheme.Accent;
            accent.a = alpha;
            GUI.color = accent;
            GUI.DrawTexture(new Rect(r.x + 3f, r.y + r.height * 0.2f, Mathf.Max(4f, r.height * 0.07f), r.height * 0.6f), Texture2D.whiteTexture);

            UiTheme.Icon icon = t.Notice.Kind == NoticeKind.Alert ? UiTheme.Icon.Alert : t.Notice.Kind == NoticeKind.Good ? UiTheme.Icon.Build : UiTheme.Icon.Clock;
            float size = r.height * 0.55f;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(r.x + r.height * 0.3f, r.y + (r.height - size) / 2f, size, size), UiTheme.IconTexture(icon));

            int fontSize = Mathf.RoundToInt(r.height * 0.36f);
            if (_toastText == null || _toastText.fontSize != fontSize)
                _toastText = new GUIStyle(GUI.skin.label) { fontSize = fontSize, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            _toastText.normal.textColor = new Color(UiTheme.Bone.r, UiTheme.Bone.g, UiTheme.Bone.b, alpha);
            GUI.Label(new Rect(r.x + r.height * 1.05f, r.y, r.width - r.height * 1.2f, r.height), t.Notice.Text, _toastText);

            // Tap = look there.
            if (t.Notice.HasPos && _game.Cam != null && GUI.Button(r, GUIContent.none, GUIStyle.none)) _game.Cam.FocusOn(t.Notice.Pos);
        }
        GUI.color = saved;
    }
}
