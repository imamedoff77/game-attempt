using SimCore;
using UnityEngine;

/// <summary>The preview shown while choosing where to build: the model plus a green / red footprint.</summary>
public sealed class BuildGhost
{
    private static readonly Color Good = new Color(0.25f, 0.95f, 0.35f, 0.55f);
    private static readonly Color Bad = new Color(0.95f, 0.2f, 0.15f, 0.6f);

    private readonly GameObject _root;
    private readonly Material _footprintMaterial;
    private readonly int _width, _height;

    private readonly GameObject _door;
    private readonly Material _doorMaterial;
    private readonly int _facing;

    public BuildGhost(BuildingKind kind, GameObject model, int facing, Material palette, Material overlay)
    {
        _facing = facing;
        Defs.Footprint(kind, facing, out _width, out _height);

        _footprintMaterial = new Material(overlay) { color = Good };
        _root = MeshFactory.Spawn("BuildGhost", MeshFactory.Quad(_width, _height), _footprintMaterial);

        GameObject visual = Object.Instantiate(model, _root.transform);
        visual.transform.localPosition = new Vector3(_width / 2f, 0f, _height / 2f);
        visual.transform.localRotation = Quaternion.Euler(0f, DemoGame.FacingAngle(facing), 0f);
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial = palette;

        // An arrow out of the door side, so it is clear where the units will come out.
        _doorMaterial = new Material(overlay) { color = new Color(1f, 0.62f, 0.12f, 0.95f) };
        _door = MeshFactory.Spawn("DoorArrow", MeshFactory.Arrow(0.45f, 1.1f), _doorMaterial);
    }

    /// <summary>Moves the preview so its top-left cell is (x, y).</summary>
    public void SetCell(int x, int y, bool allowed)
    {
        _root.transform.position = new Vector3(x, 0.06f, y);
        _footprintMaterial.color = allowed ? Good : Bad;
        DemoGame.PlaceDoorArrow(_door, x, y, _width, _height, _facing);
    }

    public void Destroy()
    {
        Object.Destroy(_footprintMaterial);
        Object.Destroy(_doorMaterial);
        Object.Destroy(_door);
        Object.Destroy(_root);
    }
}
