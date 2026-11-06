using SimCore;
using UnityEngine;

/// <summary>Smoke and flames on a building that has lost much of its health.</summary>
public sealed class BuildingFx : MonoBehaviour
{
    private Building _building;
    private float _smokeTimer, _fireTimer;

    public static void Attach(GameObject view, Building building)
    {
        view.AddComponent<BuildingFx>()._building = building;
    }

    private void Update()
    {
        Building b = _building;
        if (b.UnderConstruction || b.Hp <= 0 || b.MaxHp <= 0) return;
        float health = b.Hp / (float)b.MaxHp;
        if (health >= 0.5f) return;

        float dt = Time.deltaTime;
        _smokeTimer -= dt;
        if (_smokeTimer <= 0f)
        {
            _smokeTimer = health < 0.25f ? 0.22f : 0.5f;
            Fx.Smoke(RandomPoint(), 1.1f);
        }
        if (health < 0.25f)
        {
            _fireTimer -= dt;
            if (_fireTimer <= 0f)
            {
                _fireTimer = 0.15f;
                Fx.Fire(RandomPoint());
            }
        }
    }

    private Vector3 RandomPoint() =>
        new Vector3(_building.X + Random.value * _building.Width, 0.9f + Random.value * 0.8f, _building.Y + Random.value * _building.Height);
}

/// <summary>A destroyed building does not vanish: it burns, sinks into the ground and crumbles for a moment, then leaves a little smoke.</summary>
public sealed class BuildingCollapse : MonoBehaviour
{
    private const float Duration = 1.6f;

    private float _age, _nextBlast;
    private int _blasts;
    private Vector3 _start, _size;

    public static void Begin(GameObject view)
    {
        foreach (BuildingFx fx in view.GetComponents<BuildingFx>()) Destroy(fx);
        var renderers = view.GetComponentsInChildren<Renderer>();
        Bounds bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(view.transform.position, Vector3.one * 2f);
        foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
        var collapse = view.AddComponent<BuildingCollapse>();
        collapse._start = view.transform.position;
        collapse._size = bounds.size;
        Fx.Explosion(bounds.center, 5f);
    }

    private void Update()
    {
        _age += Time.deltaTime;
        float t = Mathf.Clamp01(_age / Duration);

        // sink and flatten (ease-in: slow at first, then it gives way)
        float fall = t * t;
        transform.position = _start + new Vector3(Random.Range(-0.03f, 0.03f), -_size.y * 0.8f * fall, Random.Range(-0.03f, 0.03f)) * (1f - t);
        transform.localScale = new Vector3(1f + fall * 0.08f, 1f - fall * 0.7f, 1f + fall * 0.08f);

        // secondary blasts across the footprint
        if (_age >= _nextBlast && _blasts < 4)
        {
            _blasts++;
            _nextBlast = _age + 0.28f;
            Vector3 c = _start;
            Fx.Explosion(new Vector3(c.x + Random.Range(-0.5f, 0.5f) * _size.x, 0f, c.z + Random.Range(-0.5f, 0.5f) * _size.z), 2.2f);
        }
        if (_age >= Duration)
        {
            Fx.Smoke(new Vector3(_start.x, 0.6f, _start.z), 1.6f);
            Destroy(gameObject);
        }
    }
}
