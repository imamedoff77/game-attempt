#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using SimCore;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Developer-only tools: performance numbers, the 50-unit battle test and the build-area overlay.
/// Hidden by default. Open it by tapping the resource counter (top-left) three times quickly; the same panel closes it.
/// This whole file is left out of release builds.
/// </summary>
[RequireComponent(typeof(DemoGame))]
public sealed class DebugPanel : MonoBehaviour
{
    private const float TripleTapSeconds = 1.5f;

    private DemoGame _game;
    private bool _visible;
    private int _taps;
    private float _firstTapTime;

    private float _smoothedDelta = 1f / 60f;
    private float _worstFrame, _worstFrameWindow, _windowStart;
    private GUIStyle _label, _button, _field;

    // Online test: server address (remembered), room code to join with
    private const string AddressPref = "serverAddress";
    private string _address;
    private string _joinCode = "";

    // Art style test: what do real shadows, detailed models and a darker mood cost on this phone?
    private Light _sun;
    private int _shadowMode = 1;   // 0 off, 1 hard 1024, 2 hard 2048, 3 soft 2048
    private bool _dusk = true;
    private bool _uncapped;        // frame rate limit 60 -> 120: shows how much headroom is left
    private Color _dayAmbient, _dayLight, _dayBackground;
    private float _dayIntensity;
    private Quaternion _dayRotation;

    public bool Visible => _visible;

    private void Awake()
    {
        _game = GetComponent<DemoGame>();
        _address = PlayerPrefs.GetString(AddressPref, "");
        if (_address.Length == 0)
        {
            // dev builds can ship with the address of the developer's computer (Resources/devserver.txt, not in git)
            TextAsset preset = Resources.Load<TextAsset>("devserver");
            if (preset != null) _address = preset.text.Trim();
        }
        _sun = FindFirstObjectByType<Light>();
        if (_sun != null)
        {
            _dayLight = _sun.color; _dayIntensity = _sun.intensity; _dayRotation = _sun.transform.rotation;
            _dayAmbient = RenderSettings.ambientLight;
            _dayBackground = Camera.main != null ? Camera.main.backgroundColor : Color.gray;
            // The shipped look (art/ART_BIBLE.md): hard shadows from the main light and a dusk mood. The debug buttons can still switch them.
            SetShadows(1);
            SetDusk(true);
        }
    }

    // Frame times of the current 5-second window, logged as "[Perf] ..." (read with: adb logcat -s Unity | grep Perf).
    // Logging instead of screenshots: taking a screenshot itself causes a frame spike.
    private readonly float[] _frames = new float[1200];
    private int _frameCount;
    private float _frameWindowStart;

    private void LogPerf()
    {
        if (_frameCount < 30) { _frameCount = 0; _frameWindowStart = Time.unscaledTime; return; }
        float[] sorted = new float[_frameCount];
        System.Array.Copy(_frames, sorted, _frameCount);
        System.Array.Sort(sorted);
        float sum = 0f;
        for (int i = 0; i < sorted.Length; i++) sum += sorted[i];
        float p95 = sorted[Mathf.Min(sorted.Length - 1, (int)(sorted.Length * 0.95f))];
        string[] shadowNames = { "off", "hard1024", "hard2048", "soft2048" };
        // GPU/CPU time per frame from the driver (0 when the phone does not report it).
        double cpuMs = 0, gpuMs = 0;
        var timings = new FrameTiming[1];
        if (FrameTimingManager.GetLatestTimings(1, timings) > 0) { cpuMs = timings[0].cpuFrameTime; gpuMs = timings[0].gpuFrameTime; }
        Debug.Log($"[Perf] cpuMs={cpuMs:0.0} gpuMs={gpuMs:0.0} fps={sorted.Length / sum:0.0} avgMs={sum / sorted.Length * 1000f:0.0} p95Ms={p95 * 1000f:0.0} maxMs={sorted[sorted.Length - 1] * 1000f:0.0} " +
                  $"units={_game.World.Units.Count} shadows={shadowNames[_shadowMode]} models={(_game.DetailedModels ? "detailed" : "simple")} mood={(_dusk ? "dusk" : "day")} " +
                  $"simAvgMs={_game.SimMsAverage:0.0} cap={(_uncapped ? 120 : 60)}");
        _frameCount = 0;
        _frameWindowStart = Time.unscaledTime;
    }

    private void Update()
    {
        FrameTimingManager.CaptureFrameTimings();
        if (_game.World != null)
        {
            if (_frameCount < _frames.Length) _frames[_frameCount++] = Time.unscaledDeltaTime;
            if (Time.unscaledTime - _frameWindowStart >= 5f) LogPerf();
        }
        _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);

        // Worst frame time over the last 3 seconds (a spike shows up here even when the average FPS looks fine).
        _worstFrameWindow = Mathf.Max(_worstFrameWindow, Time.unscaledDeltaTime);
        if (Time.unscaledTime - _windowStart >= 3f)
        {
            _worstFrame = _worstFrameWindow;
            _worstFrameWindow = 0f;
            _windowStart = Time.unscaledTime;
        }
    }

    private void OnGUI()
    {
        if (_game.World == null) return;
        GUI.depth = -100; // in front of the game's own screens, so its buttons are never covered
        HandleSecretTap();
        if (!_visible) return;
        EnsureStyles();

        World world = _game.World;
        float pad = Screen.height * 0.02f;
        float line = Screen.height * 0.055f;

        string info = $"FPS {1f / _smoothedDelta:0}   Units {world.Units.Count}   Selected {_game.SelectedCount}   Enemy {_game.EnemyCount}   {_game.SelfTestStatus}";
        GUI.Label(new Rect(pad, pad + line * 2f, Screen.width * 0.6f, line), info, _label);
        string perf = $"Worst frame {_worstFrame * 1000f:0} ms   Sim {_game.SimMsAverage:0.0} ms (max {_game.SimMsWorst:0.0})   Data {Defs.DataHash:X8}";
        GUI.Label(new Rect(pad, pad + line * 2.7f, Screen.width * 0.7f, line), perf, _label);

        Rect[] r = ButtonRects();
        if (GUI.Button(r[0], "Test: +25 mine +25 enemy", _button)) _game.SpawnBattleTest(25);
        if (GUI.Button(r[1], _game.BuildOverlayVisible ? "Build areas: ON" : "Build areas: OFF", _button)) _game.ToggleBuildOverlay();
        if (GUI.Button(r[2], "Close debug", _button)) _visible = false;

        string[] shadowNames = { "Shadows: OFF", "Shadows: hard 1024", "Shadows: hard 2048", "Shadows: soft 2048" };
        if (GUI.Button(r[3], shadowNames[_shadowMode], _button)) SetShadows((_shadowMode + 1) % shadowNames.Length);
        if (GUI.Button(r[4], _game.DetailedModels ? "Models: DETAILED" : "Models: simple", _button)) _game.SetDetailedModels(!_game.DetailedModels);
        if (GUI.Button(r[6], _uncapped ? "FPS limit: 120" : "FPS limit: 60", _button))
        {
            _uncapped = !_uncapped;
            Application.targetFrameRate = _uncapped ? 120 : 60;
        }
        if (_game.Mode == DemoGame.GameMode.Campaign && GUI.Button(r[7], "Win mission", _button)) _game.DebugWinMission();
        if (GUI.Button(r[8], "+500 credits", _button)) _game.DebugAddCredits(500);
        if (GUI.Button(r[5], _dusk ? "Mood: DUSK (dark)" : "Mood: day", _button)) SetDusk(!_dusk);

        DrawOnline();
    }

    private void DrawOnline()
    {
        Rect[] o = OnlineRects();
        OnlineSession online = _game.Online;
        string status = online == null ? "Online test: not connected" : "Online: " + online.Status;
        if (online != null && _game.OnlineMatch && online.Client != null)
            status += $"   tick {online.Client.TicksExecuted}   buffered turns {online.Client.TurnsBuffered}";
        if (online != null && online.NetStats.Length > 0) status += "\n" + online.NetStats;
        GUI.Label(o[0], status, _label);

        bool busy = online != null && online.State != OnlineSession.Phase.Ended;
        _address = GUI.TextField(o[1], _address, 40, _field);
        _joinCode = GUI.TextField(o[3], _joinCode, MessageCodec.MaxCodeLength, _field).ToUpperInvariant();

        GUI.enabled = !busy && _address.Trim().Length > 0;
        if (GUI.Button(o[2], "Host", _button))
        {
            PlayerPrefs.SetString(AddressPref, _address.Trim());
            _game.HostOnline(_address);
        }
        GUI.enabled = !busy && _address.Trim().Length > 0 && _joinCode.Length > 0;
        if (GUI.Button(o[4], "Join", _button))
        {
            PlayerPrefs.SetString(AddressPref, _address.Trim());
            _game.JoinOnline(_address, _joinCode);
        }
        GUI.enabled = online != null;
        if (GUI.Button(o[5], "Leave", _button)) _game.LeaveOnline();
        GUI.enabled = true;
    }

    private void SetShadows(int mode)
    {
        _shadowMode = mode;
        if (_sun != null) _sun.shadows = mode == 0 ? LightShadows.None : mode == 3 ? LightShadows.Soft : LightShadows.Hard;
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
        {
            urp.mainLightShadowmapResolution = mode == 1 ? 1024 : 2048;
        }
    }

    private void SetDusk(bool on)
    {
        _dusk = on;
        if (_sun == null) return;
        _sun.color = on ? new Color(1f, 0.8f, 0.6f) : _dayLight;
        _sun.intensity = on ? 1.25f : _dayIntensity;
        _sun.transform.rotation = on ? Quaternion.Euler(34f, -55f, 0f) : _dayRotation;
        RenderSettings.ambientLight = on ? new Color(0.27f, 0.3f, 0.38f) : _dayAmbient;
        if (Camera.main != null) Camera.main.backgroundColor = on ? new Color(0.42f, 0.35f, 0.26f) : _dayBackground;
    }

    private void HandleSecretTap()
    {
        Event e = Event.current;
        if (e.type != EventType.MouseDown || !Hotspot().Contains(e.mousePosition)) return;

        if (_taps == 0 || Time.unscaledTime - _firstTapTime > TripleTapSeconds)
        {
            _taps = 0;
            _firstTapTime = Time.unscaledTime;
        }
        _taps++;
        if (_taps >= 3)
        {
            _taps = 0;
            _visible = !_visible;
        }
    }

    /// <summary>Screen areas that must not pass taps through to the map (IMGUI coordinates).</summary>
    public IEnumerable<Rect> ActiveRects()
    {
        yield return Hotspot();
        if (!_visible) yield break;
        foreach (Rect r in ButtonRects()) yield return r;
        foreach (Rect r in OnlineRects()) yield return r;
    }

    /// <summary>The resource counter in the top-left corner.</summary>
    private static Rect Hotspot()
    {
        float pad = Screen.height * 0.02f;
        return new Rect(pad, pad, Screen.width * 0.3f, Screen.height * 0.055f);
    }

    private static Rect[] ButtonRects()
    {
        float pad = Screen.height * 0.02f;
        float h = Screen.height * 0.1f;
        float w = Screen.width * 0.2f;
        float top = Screen.height * 0.30f;
        var rects = new Rect[9];
        for (int i = 0; i < rects.Length; i++)
            rects[i] = new Rect(pad + (i / 3) * (w + pad * 0.5f), top + (i % 3) * (h + pad * 0.5f), w, h);
        return rects;
    }

    /// <summary>Status line, address field, Host, code field, Join, Leave (bottom-left, two rows).</summary>
    private static Rect[] OnlineRects()
    {
        float pad = Screen.height * 0.02f;
        float h = Screen.height * 0.085f;
        float y1 = Screen.height * 0.68f;
        float y2 = y1 + h + pad * 0.5f;
        float x = pad;
        float addrW = Screen.width * 0.3f, btnW = Screen.width * 0.12f, codeW = Screen.width * 0.14f;
        var rects = new Rect[6];
        rects[0] = new Rect(x, y1 - h * 0.45f, Screen.width * 0.9f, h * 1.4f);
        rects[1] = new Rect(x, y2 + h * 0.1f, addrW, h);
        rects[2] = new Rect(x + addrW + pad * 0.5f, y2 + h * 0.1f, btnW, h);
        rects[3] = new Rect(rects[2].xMax + pad, y2 + h * 0.1f, codeW, h);
        rects[4] = new Rect(rects[3].xMax + pad * 0.5f, y2 + h * 0.1f, btnW, h);
        rects[5] = new Rect(rects[4].xMax + pad, y2 + h * 0.1f, btnW, h);
        return rects;
    }

    private void EnsureStyles()
    {
        UiSkin.Apply();
        if (_label != null) return;
        int size = Mathf.RoundToInt(Screen.height * 0.032f);
        _label = new GUIStyle(GUI.skin.label) { fontSize = size };
        _label.normal.textColor = Color.white;
        _button = new GUIStyle(GUI.skin.button) { fontSize = size, wordWrap = true };
        _field = new GUIStyle(GUI.skin.textField) { fontSize = size };
    }
}
#endif
