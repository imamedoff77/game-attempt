using UnityEngine;

/// <summary>Minimal on-screen diagnostics for device testing (IMGUI, no UI package needed).</summary>
public class FpsHud : MonoBehaviour
{
    private float _smoothedDelta = 1f / 60f;
    private string _simStatus;
    private GUIStyle _style;

    private void Start()
    {
        Application.targetFrameRate = 60;
        bool ok = SimCoreSelfTest.Run(out string failure);
        _simStatus = ok ? "SimCore determinism: OK" : "SimCore determinism: FAIL (" + failure + ")";
        Debug.Log("[GameAttempt] " + _simStatus);
    }

    private void Update()
    {
        _smoothedDelta = Mathf.Lerp(_smoothedDelta, Time.unscaledDeltaTime, 0.05f);
    }

    private void OnGUI()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.04f) };
            _style.normal.textColor = Color.white;
        }
        float pad = Screen.height * 0.02f;
        string text = $"FPS: {1f / _smoothedDelta:0}\n{Screen.width}x{Screen.height}\n{SystemInfo.deviceModel}\n{_simStatus}";
        GUI.Label(new Rect(pad, pad, Screen.width - pad * 2, Screen.height * 0.5f), text, _style);
    }
}
