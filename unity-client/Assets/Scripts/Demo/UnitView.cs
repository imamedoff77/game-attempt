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
    private Quaternion _facing = Quaternion.identity;

    public int UnitId => _unit.Id;
    public int Team => _unit.Team;

    public static UnitView Create(Unit unit, GameObject model, Material palette, Material ringMaterial)
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

        view.Refresh(0f, false, false);
        return view;
    }

    public void Refresh(float alpha, bool selected, bool aiming)
    {
        Vector3 from = ToUnity(_unit.PrevPos);
        Vector3 to = ToUnity(_unit.Pos);
        transform.position = Vector3.Lerp(from, to, alpha);

        Vector3 moved = to - from;
        if (aiming)
        {
            Vector3 toAim = ToUnity(_unit.AimPoint) - to;
            toAim.y = 0f;
            if (toAim.sqrMagnitude > 1e-6f) _facing = Quaternion.LookRotation(toAim);
        }
        else if (moved.sqrMagnitude > 1e-8f)
            _facing = Quaternion.LookRotation(moved);
        transform.rotation = Quaternion.Slerp(transform.rotation, _facing, 1f - Mathf.Exp(-12f * Time.deltaTime));

        _selectionRing.SetActive(selected);
    }

    /// <summary>Sim (x, y) maps onto the Unity ground plane (x, z).</summary>
    public static Vector3 ToUnity(Vec2 p) => new Vector3(p.X.ToFloat(), 0f, p.Y.ToFloat());
}
