using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Short-lived effects: bullet tracers, muzzle flashes, explosions, smoke, dust and fire. Pure decoration.
/// Objects are reused (pooled) because a big fight makes hundreds of them a second.
/// </summary>
public sealed class Fx : MonoBehaviour
{
    private enum Type { Tracer, Ring, Puff, Flash }

    private const int MaxActive = 220;

    private static Mesh _strip, _ring, _disc;
    private static Material _playerShot, _enemyShot, _explosion, _smoke, _darkSmoke, _dust, _fire, _flash;
    private static readonly Stack<Fx> Free = new Stack<Fx>();
    private static Transform _root;
    private static int _active;
    // The camera never turns, so effects that face it are turned once when they appear.
    private static readonly Quaternion Billboard = Quaternion.Euler(55f, 0f, 0f);

    private Type _type;
    private float _age, _life, _size;
    private Vector3 _velocity;
    private MeshFilter _filter;
    private MeshRenderer _renderer;

    public static void Init(Material unlitTemplate)
    {
        Free.Clear();
        _active = 0;
        _root = new GameObject("Fx").transform;
        _strip = MeshFactory.Strip();
        _ring = MeshFactory.Ring(0.55f, 0.8f);
        _disc = MeshFactory.Disc(0.5f);
        _playerShot = Tint(unlitTemplate, new Color(1f, 0.95f, 0.4f));
        _enemyShot = Tint(unlitTemplate, new Color(1f, 0.35f, 0.25f));
        _explosion = Tint(unlitTemplate, new Color(1f, 0.6f, 0.15f));
        _smoke = Tint(unlitTemplate, new Color(0.42f, 0.4f, 0.38f));
        _darkSmoke = Tint(unlitTemplate, new Color(0.16f, 0.15f, 0.14f));
        _dust = Tint(unlitTemplate, new Color(0.78f, 0.67f, 0.46f));
        _fire = Tint(unlitTemplate, new Color(1f, 0.5f, 0.1f));
        _flash = Tint(unlitTemplate, new Color(1f, 0.93f, 0.65f));
    }

    private static Material Tint(Material template, Color color) => new Material(template) { color = color };

    private static Fx Take(Type type, Mesh mesh, Material material, Vector3 position, Quaternion rotation, float life)
    {
        Fx fx;
        if (Free.Count > 0)
        {
            fx = Free.Pop();
            fx.gameObject.SetActive(true);
        }
        else
        {
            var go = new GameObject("Fx");
            go.transform.SetParent(_root, false);
            fx = go.AddComponent<Fx>();
            fx._filter = go.AddComponent<MeshFilter>();
            fx._renderer = go.AddComponent<MeshRenderer>();
            fx._renderer.shadowCastingMode = ShadowCastingMode.Off;
            fx._renderer.receiveShadows = false;
        }
        fx._filter.sharedMesh = mesh;
        fx._renderer.sharedMaterial = material;
        fx.transform.SetPositionAndRotation(position, rotation);
        fx.transform.localScale = Vector3.one;
        fx._type = type;
        fx._age = 0f;
        fx._life = life;
        fx._velocity = Vector3.zero;
        _active++;
        return fx;
    }

    private static bool Crowded => _active >= MaxActive;

    private static Fx Puff(Material material, Vector3 at, float size, float life, Vector3 velocity)
    {
        if (Crowded) return null;
        Fx fx = Take(Type.Puff, _disc, material, at, Billboard, life);
        fx._size = size;
        fx._velocity = velocity;
        fx.transform.localScale = Vector3.one * (size * 0.5f);
        return fx;
    }

    public static void Tracer(Vector3 from, Vector3 to, bool heavy, bool enemy)
    {
        from.y = to.y = 0.8f;
        Vector3 d = to - from;
        float length = d.magnitude;
        if (length < 0.05f || Crowded) return;

        Fx fx = Take(Type.Tracer, _strip, enemy ? _enemyShot : _playerShot, from, Quaternion.FromToRotation(Vector3.right, d / length), 0.12f);
        fx.transform.localScale = new Vector3(length, 1f, heavy ? 0.22f : 0.09f);
    }

    /// <summary>A short bright flash at the muzzle, a little ahead of the shooter.</summary>
    public static void MuzzleFlash(Vector3 from, Vector3 to, bool heavy)
    {
        Vector3 d = to - from;
        d.y = 0f;
        if (d.sqrMagnitude < 1e-4 || Crowded) return;
        Vector3 at = from + d.normalized * (heavy ? 1.1f : 0.7f);
        at.y = heavy ? 1.1f : 0.85f;
        Fx fx = Take(Type.Flash, _disc, _flash, at, Billboard, heavy ? 0.1f : 0.07f);
        fx._size = heavy ? 0.8f : 0.32f;
    }

    /// <summary>Fireball, shock ring and rising dark smoke. <paramref name="size"/>: about 1.6 for a unit, 5 for a building.</summary>
    public static void Explosion(Vector3 at, float size)
    {
        Fx ring = Take(Type.Ring, _ring, _explosion, new Vector3(at.x, 0.3f, at.z), Quaternion.identity, 0.45f);
        ring._size = size;

        Puff(_fire, new Vector3(at.x, 0.7f + size * 0.1f, at.z), size * 0.9f, 0.4f, new Vector3(0f, 0.6f, 0f));
        Puff(_flash, new Vector3(at.x, 0.8f + size * 0.1f, at.z), size * 0.5f, 0.2f, Vector3.zero);

        int smokes = size > 3f ? 8 : 3;
        for (int i = 0; i < smokes; i++)
        {
            Vector2 o = Random.insideUnitCircle * size * 0.3f;
            var velocity = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.8f, 1.7f), Random.Range(-0.3f, 0.3f));
            Puff(i % 3 == 0 ? _smoke : _darkSmoke, new Vector3(at.x + o.x, 0.8f, at.z + o.y), size * Random.Range(0.3f, 0.5f), Random.Range(0.9f, 1.6f), velocity);
        }
    }

    /// <summary>A small cloud kicked up behind a moving vehicle.</summary>
    public static void Dust(Vector3 at)
    {
        Puff(_dust, new Vector3(at.x, 0.25f, at.z), Random.Range(0.45f, 0.7f), 0.7f, new Vector3(Random.Range(-0.2f, 0.2f), 0.5f, Random.Range(-0.2f, 0.2f)));
    }

    /// <summary>Smoke rising from a damaged building.</summary>
    public static void Smoke(Vector3 at, float size)
    {
        Puff(Random.value < 0.5f ? _smoke : _darkSmoke, at, size, 1.8f, new Vector3(Random.Range(-0.1f, 0.25f), Random.Range(0.9f, 1.4f), 0f));
    }

    public static void Fire(Vector3 at)
    {
        Puff(_fire, at, Random.Range(0.5f, 0.9f), 0.35f, new Vector3(0f, Random.Range(0.8f, 1.5f), 0f));
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = _age / _life;
        if (t >= 1f)
        {
            gameObject.SetActive(false);
            Free.Push(this);
            _active--;
            return;
        }

        switch (_type)
        {
            case Type.Ring:
            {
                float s = Mathf.Lerp(0.4f, _size, Mathf.Sqrt(t));
                transform.localScale = new Vector3(s, 1f, s);
                break;
            }
            case Type.Puff:
            {
                transform.position += _velocity * Time.deltaTime;
                // swells quickly, then shrinks away (there is no transparency to fade with)
                float s = t < 0.25f ? Mathf.Lerp(0.5f, 1f, t / 0.25f) : Mathf.Lerp(1f, 0f, (t - 0.25f) / 0.75f);
                transform.localScale = Vector3.one * (_size * s);
                break;
            }
            case Type.Flash:
                transform.localScale = Vector3.one * (_size * (1f - t));
                break;
        }
    }
}
