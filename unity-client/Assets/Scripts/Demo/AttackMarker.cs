using System.Collections.Generic;
using SimCore;
using UnityEngine;

/// <summary>
/// The "attack!" signal: a red reticle that snaps in on a target the moment it is ordered, and a slowly turning red reticle that stays under
/// every enemy the selected units are fighting. Purely visual.
/// </summary>
public sealed class AttackMarker : MonoBehaviour
{
    private const float Lifetime = 0.9f;
    private static readonly Color Red = new Color(1f, 0.18f, 0.12f, 0.95f);
    private static Mesh _mesh;

    private Material _material;
    private float _age, _size;

    /// <summary>A reticle that closes in on <paramref name="size"/> (radius, map units) and fades.</summary>
    public static void Flash(Vector3 position, float size, Material overlayTemplate)
    {
        if (_mesh == null) _mesh = MeshFactory.Reticle(1f, 0.13f);
        var material = new Material(overlayTemplate) { color = Red };
        GameObject go = MeshFactory.Spawn("AttackFlash", _mesh, material);
        go.transform.position = new Vector3(position.x, 0.09f, position.z);
        var marker = go.AddComponent<AttackMarker>();
        marker._material = material;
        marker._size = size;
        marker.Apply();
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= Lifetime) { Destroy(gameObject); return; }
        Apply();
    }

    private void Apply()
    {
        float t = Mathf.Clamp01(_age / Lifetime);
        float scale = _size * Mathf.Lerp(3.2f, 1f, 1f - (1f - t) * (1f - t)); // closes in fast, settles
        transform.localScale = new Vector3(scale, 1f, scale);
        transform.rotation = Quaternion.Euler(0f, t * 140f, 0f);
        Color c = Red;
        c.a = Red.a * (t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f);
        _material.color = c;
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }

    /// <summary>The reticles that stay under the targets of the selected units.</summary>
    public sealed class Indicators
    {
        private const int MaxShown = 6;
        private readonly List<GameObject> _pool = new List<GameObject>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly Material _material;
        private readonly Mesh _reticle = MeshFactory.Reticle(1f, 0.11f);

        public Indicators(Material overlayTemplate)
        {
            _material = new Material(overlayTemplate) { color = new Color(1f, 0.2f, 0.14f, 0.8f) };
        }

        public void Update(World world, IEnumerable<int> selected)
        {
            int used = 0;
            _seen.Clear();
            if (world != null && selected != null)
            {
                foreach (int id in selected)
                {
                    if (used >= MaxShown) break;
                    Unit u = world.GetUnit(id);
                    if (u == null || u.Hp <= 0 || u.AttackKind == AttackKind.None || !u.Ordered) continue; // only the targets the player chose
                    long key = ((long)(int)u.AttackKind << 32) | (uint)u.AttackId;
                    if (!_seen.Add(key)) continue;

                    Vector3 at;
                    float radius;
                    if (u.AttackKind == AttackKind.Unit)
                    {
                        Unit target = world.GetUnit(u.AttackId);
                        if (target == null || target.Hp <= 0) continue;
                        at = UnitView.ToUnity(target.Pos);
                        radius = Defs.Unit(target.Kind).Class == UnitClass.Vehicle ? 1.5f : 1.0f;
                    }
                    else
                    {
                        Building target = world.GetBuilding(u.AttackId);
                        if (target == null || target.Hp <= 0) continue;
                        at = new Vector3(target.X + target.Width / 2f, 0f, target.Y + target.Height / 2f);
                        radius = Mathf.Max(target.Width, target.Height) * 0.75f + 0.6f;
                    }

                    GameObject go = Get(used++);
                    float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 7f);
                    go.transform.position = new Vector3(at.x, 0.08f, at.z);
                    go.transform.localScale = new Vector3(radius * pulse, 1f, radius * pulse);
                    go.transform.rotation = Quaternion.Euler(0f, Time.time * 70f, 0f);
                    go.SetActive(true);
                }
            }
            for (int i = used; i < _pool.Count; i++) _pool[i].SetActive(false);
        }

        private GameObject Get(int index)
        {
            while (_pool.Count <= index)
            {
                GameObject go = MeshFactory.Spawn("AttackTarget", _reticle, _material);
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _pool.Add(go);
            }
            return _pool[index];
        }
    }
}
