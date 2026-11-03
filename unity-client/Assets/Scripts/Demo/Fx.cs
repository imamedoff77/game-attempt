using UnityEngine;

/// <summary>Short-lived effects: bullet tracers and explosion rings. Pure decoration.</summary>
public sealed class Fx : MonoBehaviour
{
    private enum Type { Tracer, Explosion }

    private static Mesh _strip, _ring;
    private static Material _playerShot, _enemyShot, _explosion;

    private Type _type;
    private float _age, _life, _size;

    public static void Init(Material unlitTemplate)
    {
        _strip = MeshFactory.Strip();
        _ring = MeshFactory.Ring(0.55f, 0.8f);
        _playerShot = new Material(unlitTemplate) { color = new Color(1f, 0.95f, 0.4f) };
        _enemyShot = new Material(unlitTemplate) { color = new Color(1f, 0.35f, 0.25f) };
        _explosion = new Material(unlitTemplate) { color = new Color(1f, 0.6f, 0.15f) };
    }

    public static void Tracer(Vector3 from, Vector3 to, bool heavy, bool enemy)
    {
        from.y = to.y = 0.8f;
        Vector3 d = to - from;
        float length = d.magnitude;
        if (length < 0.05f) return;

        GameObject go = MeshFactory.Spawn("Tracer", _strip, enemy ? _enemyShot : _playerShot);
        go.transform.position = from;
        go.transform.rotation = Quaternion.FromToRotation(Vector3.right, d / length);
        go.transform.localScale = new Vector3(length, 1f, heavy ? 0.35f : 0.14f);
        Fx fx = go.AddComponent<Fx>();
        fx._type = Type.Tracer;
        fx._life = 0.12f;
    }

    public static void Explosion(Vector3 at, float size)
    {
        GameObject go = MeshFactory.Spawn("Explosion", _ring, _explosion);
        go.transform.position = new Vector3(at.x, 0.3f, at.z);
        Fx fx = go.AddComponent<Fx>();
        fx._type = Type.Explosion;
        fx._life = 0.45f;
        fx._size = size;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = _age / _life;
        if (t >= 1f) { Destroy(gameObject); return; }
        if (_type == Type.Explosion)
        {
            float s = Mathf.Lerp(0.4f, _size, Mathf.Sqrt(t));
            transform.localScale = new Vector3(s, 1f, s);
        }
    }
}
