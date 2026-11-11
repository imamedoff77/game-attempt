using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a destroyed vehicle leaves behind: the charred hull, burning for a few seconds and smoking for a while,
/// then sinking into the sand. Purely visual; the simulation has already removed the unit.
/// </summary>
public sealed class Wreck : MonoBehaviour
{
    private const float Life = 32f, FireTime = 6f, SmokeTime = 22f, SinkTime = 4f;
    private const int MaxWrecks = 10;
    private static readonly List<Wreck> All = new List<Wreck>();
    private static Material _charred;

    private float _age, _nextPuff;

    /// <summary>Takes the vehicle's model out of its view, blackens it and leaves it where it stood.</summary>
    public static void Spawn(UnitView view, Material palette)
    {
        if (_charred == null) _charred = new Material(palette) { color = new Color(0.2f, 0.19f, 0.18f) };

        var go = new GameObject("Wreck");
        go.transform.SetPositionAndRotation(view.transform.position, view.transform.rotation);
        Transform body = view.Body;
        body.SetParent(go.transform, true);
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>()) r.sharedMaterial = _charred;

        // Knocked a little off level, as if the blast shoved it.
        go.transform.rotation *= Quaternion.Euler(Random.Range(-5f, 5f), Random.Range(-12f, 12f), Random.Range(-5f, 5f));

        var wreck = go.AddComponent<Wreck>();
        All.Add(wreck);
        if (All.Count > MaxWrecks)
        {
            Destroy(All[0].gameObject);
            All.RemoveAt(0);
        }
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= Life) { Destroy(gameObject); return; }

        if (_age >= Life - SinkTime)
        {
            Vector3 p = transform.position;
            p.y -= Time.deltaTime * 0.35f;
            transform.position = p;
        }

        if (_age < SmokeTime && _age >= _nextPuff)
        {
            Vector3 at = transform.position + Vector3.up * 0.9f;
            if (_age < FireTime) { Fx.Fire(at); _nextPuff = _age + 0.25f; }
            else { Fx.Smoke(at, Mathf.Lerp(1.0f, 0.5f, _age / SmokeTime)); _nextPuff = _age + 0.45f; }
        }
    }

    private void OnDestroy() => All.Remove(this);
}
