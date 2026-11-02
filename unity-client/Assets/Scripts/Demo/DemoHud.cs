using UnityEngine;

/// <summary>On-screen buttons and diagnostics (IMGUI: no UI package needed for the demo).</summary>
[RequireComponent(typeof(DemoGame))]
public sealed class DemoHud : MonoBehaviour
{
    private const int ButtonCount = 4;

    private DemoGame _game;
    private float _smoothedDelta = 1f / 60f;
    private GUIStyle _label;
    private GUIStyle _button;

    private void Awake()
    {
        _game = GetComponent<DemoGame>();
    }

    private void Update()
    {
        _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);

        // Touch positions have their origin bottom-left, IMGUI rects top-left.
        if (_game.Touch != null)
            _game.Touch.IsOverUi = pos =>
            {
                var gui = new Vector2(pos.x, Screen.height - pos.y);
                foreach (Rect r in ButtonRects())
                    if (r.Contains(gui)) return true;
                return false;
            };
    }

    private void OnGUI()
    {
        if (_game.World == null) return;
        EnsureStyles();

        float pad = Screen.height * 0.02f;
        string info = $"FPS {1f / _smoothedDelta:0}   Units {_game.World.Units.Count}   Selected {_game.SelectedCount}\n{_game.SelfTestStatus}";
        GUI.Label(new Rect(pad, pad, Screen.width * 0.7f, Screen.height * 0.2f), info, _label);

        Rect[] r = ButtonRects();
        if (GUI.Button(r[0], _game.BuildOverlayVisible ? "Tikinti sahələri: AÇIQ" : "Tikinti sahələri", _button)) _game.ToggleBuildOverlay();
        if (GUI.Button(r[1], _game.Touch.BoxMode ? "Çərçivə: AÇIQ" : "Çərçivə: BAĞLI", _button)) _game.Touch.BoxMode = !_game.Touch.BoxMode;
        if (GUI.Button(r[2], "Hamısı", _button)) _game.SelectAll();
        if (GUI.Button(r[3], "Ləğv et", _button)) _game.ClearSelection();

        if (_game.Touch.BoxRect.HasValue)
        {
            Rect b = _game.Touch.BoxRect.Value;
            var gui = new Rect(b.x, Screen.height - b.yMax, b.width, b.height);
            Color old = GUI.color;
            GUI.color = new Color(0.3f, 0.8f, 1f, 0.25f);
            GUI.DrawTexture(gui, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }

    /// <summary>Bottom-right row of buttons, in IMGUI coordinates (origin top-left).</summary>
    private static Rect[] ButtonRects()
    {
        float h = Screen.height * 0.11f;
        float w = Mathf.Min(Screen.width * 0.19f, h * 3.6f);
        float gap = Screen.height * 0.015f;
        float y = Screen.height - h - gap;

        var rects = new Rect[ButtonCount];
        for (int i = 0; i < ButtonCount; i++)
        {
            float x = Screen.width - (ButtonCount - i) * (w + gap);
            rects[i] = new Rect(x, y, w, h);
        }
        return rects;
    }

    private void EnsureStyles()
    {
        if (_label != null) return;
        int size = Mathf.RoundToInt(Screen.height * 0.032f);
        _label = new GUIStyle(GUI.skin.label) { fontSize = size };
        _label.normal.textColor = Color.white;
        _button = new GUIStyle(GUI.skin.button) { fontSize = size, wordWrap = true };
    }
}
