using SimCore;
using UnityEngine;

/// <summary>
/// Draws one simulation Unit. Reads the sim state, never changes it.
/// The sim ticks 20 times a second; this smooths the motion between ticks.
/// </summary>
public sealed class UnitView : MonoBehaviour
{
    private Unit _unit;
    private GameObject _selectionRing;
    private GameObject _fireArc;
    private Quaternion _facing = Quaternion.identity;

    public int UnitId => _unit.Id;
    public int Team => _unit.Team;

    public static UnitView Create(Unit unit, GameObject model, Material palette, Material ringMaterial, Material arcMaterial)
    {
        var root = new GameObject("Unit " + unit.Id + " " + unit.Kind);
        var view = root.AddComponent<UnitView>();
        view._unit = unit;

        GameObject body = Instantiate(model, root.transform);
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>()) r.sharedMaterial = palette;

        float outer = Defs.Unit(unit.Kind).Class == UnitClass.Infantry ? 0.55f
                    : unit.Kind == UnitKind.Tank ? 1.4f : 1.0f;
        view._selectionRing = MeshFactory.Spawn("SelectionRing", MeshFactory.Ring(outer * 0.82f, outer), ringMaterial, root.transform);
        view._selectionRing.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        view._selectionRing.SetActive(false);

        // Firing arc: shown while selected, so you can see which way the unit can shoot.
        CombatDef combat = Defs.Combat(unit.Kind);
        view._fireArc = MeshFactory.Spawn("FireArc", MeshFactory.Wedge(combat.Range.ToFloat(), combat.ArcDegrees), arcMaterial, root.transform);
        view._fireArc.transform.localPosition = new Vector3(0f, 0.05f, 0f);
        view._fireArc.SetActive(false);

        view.Refresh(0f, false);
        view.transform.rotation = view._facing; // no turning animation on spawn
        return view;
    }

    public void Refresh(float alpha, bool selected)
    {
        Vector3 from = ToUnity(_unit.PrevPos);
        Vector3 to = ToUnity(_unit.Pos);
        transform.position = Vector3.Lerp(from, to, alpha);

        // The simulation decides which way the unit faces (movement direction, or towards its target when it stands and shoots).
        Vector3 facing = new Vector3(_unit.Facing.X.ToFloat(), 0f, _unit.Facing.Y.ToFloat());
        if (facing.sqrMagnitude > 1e-6f) _facing = Quaternion.LookRotation(facing);
        transform.rotation = Quaternion.Slerp(transform.rotation, _facing, 1f - Mathf.Exp(-12f * Time.deltaTime));

        _selectionRing.SetActive(selected);
        _fireArc.SetActive(selected);
    }

    /// <summary>Sim (x, y) maps onto the Unity ground plane (x, z).</summary>
    public static Vector3 ToUnity(Vec2 p) => new Vector3(p.X.ToFloat(), 0f, p.Y.ToFloat());
}
