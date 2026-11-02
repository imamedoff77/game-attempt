using System;
using UnityEngine;

/// <summary>
/// Turns raw touches into game gestures:
///   one finger tap   = select a unit / order a move
///   one finger drag  = pan the map (or draw a selection box when box mode is on)
///   two finger pinch = zoom
/// </summary>
public sealed class TouchController
{
    private readonly RtsCamera _camera;
    private readonly DemoGame _game;

    private bool _tracking;
    private bool _dragging;
    private Vector2 _start;
    private Vector2 _last;

    /// <summary>When on, one-finger drag draws a selection box instead of panning.</summary>
    public bool BoxMode;

    /// <summary>Selection box in screen pixels (origin bottom-left), or null when not drawing.</summary>
    public Rect? BoxRect { get; private set; }

    /// <summary>Set by the HUD: returns true when a screen position is on a button.</summary>
    public Func<Vector2, bool> IsOverUi = _ => false;

    public TouchController(RtsCamera camera, DemoGame game)
    {
        _camera = camera;
        _game = game;
    }

    public void Update()
    {
        int count = Input.touchCount;

        if (count >= 2)
        {
            Pinch(Input.GetTouch(0), Input.GetTouch(1));
            return;
        }

        if (count == 0)
        {
            _tracking = false;
            BoxRect = null;
            return;
        }

        Touch t = Input.GetTouch(0);
        float slop = Screen.height * 0.02f;

        switch (t.phase)
        {
            case TouchPhase.Began:
                _tracking = !IsOverUi(t.position);
                _dragging = false;
                _start = _last = t.position;
                break;

            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (!_tracking) break;
                if (!_dragging && (t.position - _start).magnitude > slop) _dragging = true;
                if (_dragging)
                {
                    if (BoxMode) BoxRect = MakeRect(_start, t.position);
                    else _camera.PanByScreenDelta(_last, t.position);
                }
                _last = t.position;
                break;

            case TouchPhase.Ended:
                if (_tracking)
                {
                    if (!_dragging) _game.Tap(t.position);
                    else if (BoxMode) _game.SelectInRect(MakeRect(_start, t.position));
                }
                _tracking = false;
                _dragging = false;
                BoxRect = null;
                break;

            case TouchPhase.Canceled:
                _tracking = false;
                _dragging = false;
                BoxRect = null;
                break;
        }
    }

    private void Pinch(Touch a, Touch b)
    {
        _tracking = false;
        BoxRect = null;

        float previous = ((a.position - a.deltaPosition) - (b.position - b.deltaPosition)).magnitude;
        float current = (a.position - b.position).magnitude;
        if (previous > 1f && current > 1f)
            _camera.ZoomByRatio(previous / current);
    }

    private static Rect MakeRect(Vector2 a, Vector2 b)
    {
        Vector2 min = Vector2.Min(a, b);
        Vector2 max = Vector2.Max(a, b);
        return new Rect(min, max - min);
    }
}
