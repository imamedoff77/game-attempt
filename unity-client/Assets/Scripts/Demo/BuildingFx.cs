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
