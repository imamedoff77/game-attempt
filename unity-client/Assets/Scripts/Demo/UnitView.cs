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

    public static UnitView Create(Unit unit, Material body, Material accent, Material ring)
    {
        var root = new GameObject("Unit " + unit.Id);
        var view = root.AddComponent<UnitView>();
        view._unit = unit;

        GameObject capsule = Primitive(PrimitiveType.Capsule, root.transform, "Body", body);
        capsule.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        capsule.transform.localScale = new Vector3(0.6f, 0.45f, 0.6f);

        // A small "gun" so you can see which way the unit faces.
        GameObject gun = Primitive(PrimitiveType.Cube, root.transform, "Gun", accent);
        gun.transform.localPosition = new Vector3(0.12f, 0.5f, 0.4f);
        gun.transform.localScale = new Vector3(0.1f, 0.1f, 0.55f);

        view._selectionRing = Primitive(PrimitiveType.Cylinder, root.transform, "SelectionRing", ring);
        view._selectionRing.transform.localPosition = new Vector3(0f, 0.03f, 0f);
        view._selectionRing.transform.localScale = new Vector3(1.1f, 0.01f, 1.1f);
        view._selectionRing.SetActive(false);

        view.Refresh(0f, false);
        return view;
    }

    public void Refresh(float alpha, bool selected)
    {
        Vector3 from = ToUnity(_unit.PrevPos);
        Vector3 to = ToUnity(_unit.Pos);
        transform.position = Vector3.Lerp(from, to, alpha);

        Vector3 moved = to - from;
        if (moved.sqrMagnitude > 1e-8f)
            _facing = Quaternion.LookRotation(moved);
        transform.rotation = Quaternion.Slerp(transform.rotation, _facing, 1f - Mathf.Exp(-15f * Time.deltaTime));

        _selectionRing.SetActive(selected);
    }

    /// <summary>Sim (x, y) maps onto the Unity ground plane (x, z).</summary>
    public static Vector3 ToUnity(Vec2 p) => new Vector3(p.X.ToFloat(), 0f, p.Y.ToFloat());

    private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        Destroy(go.GetComponent<Collider>()); // picking is done on screen positions, not physics
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }
}
