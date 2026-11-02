using UnityEngine;

/// <summary>
/// Fixed-angle strategy camera. Never rotates; only moves over the map (pan) and in/out (zoom).
/// Lives on the Main Camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public class RtsCamera : MonoBehaviour
{
    private const float Pitch = 55f;
    private const float MinDistance = 10f;
    private const float MaxDistance = 45f;

    private Vector3 _pivot;
    private float _distance = 24f;
    private float _mapSize = 64f;

    public Camera Camera { get; private set; }

    private void Awake()
    {
        Camera = GetComponent<Camera>();
    }

    public void Init(Vector3 pivot, float mapSize)
    {
        _pivot = pivot;
        _mapSize = mapSize;
        Apply();
    }

    /// <summary>The point on the ground (y = 0) under a screen position.</summary>
    public bool GroundPoint(Vector2 screenPos, out Vector3 point)
    {
        Ray ray = Camera.ScreenPointToRay(screenPos);
        var ground = new Plane(Vector3.up, Vector3.zero);
        if (ground.Raycast(ray, out float t))
        {
            point = ray.GetPoint(t);
            return true;
        }
        point = default;
        return false;
    }

    /// <summary>Drags the map with the finger: the ground point under the finger stays under it.</summary>
    public void PanByScreenDelta(Vector2 previous, Vector2 current)
    {
        if (!GroundPoint(previous, out Vector3 a) || !GroundPoint(current, out Vector3 b)) return;
        _pivot += a - b;
        _pivot.x = Mathf.Clamp(_pivot.x, 0f, _mapSize);
        _pivot.z = Mathf.Clamp(_pivot.z, 0f, _mapSize);
        Apply();
    }

    /// <param name="ratio">previousFingerDistance / currentFingerDistance: below 1 zooms in.</param>
    public void ZoomByRatio(float ratio)
    {
        _distance = Mathf.Clamp(_distance * ratio, MinDistance, MaxDistance);
        Apply();
    }

    private void Apply()
    {
        transform.rotation = Quaternion.Euler(Pitch, 0f, 0f);
        transform.position = _pivot - transform.forward * _distance;
    }
}
