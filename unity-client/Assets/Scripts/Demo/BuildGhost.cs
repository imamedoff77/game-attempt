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

    public BuildGhost(BuildingKind kind, GameObject model, Material palette, Material overlay)
    {
        BuildingDef def = Defs.Building(kind);
        _width = def.Width;
        _height = def.Height;

        _footprintMaterial = new Material(overlay) { color = Good };
        _root = MeshFactory.Spawn("BuildGhost", MeshFactory.Quad(_width, _height), _footprintMaterial);

        GameObject visual = Object.Instantiate(model, _root.transform);
        visual.transform.localPosition = new Vector3(_width / 2f, 0f, _height / 2f);
        foreach (Renderer r in visual.GetComponentsInChildren<Renderer>()) r.sharedMaterial = palette;
    }

    /// <summary>Moves the preview so its top-left cell is (x, y).</summary>
    public void SetCell(int x, int y, bool allowed)
    {
        _root.transform.position = new Vector3(x, 0.06f, y);
        _footprintMaterial.color = allowed ? Good : Bad;
    }

    public void Destroy()
    {
        Object.Destroy(_footprintMaterial);
        Object.Destroy(_root);
    }
}
