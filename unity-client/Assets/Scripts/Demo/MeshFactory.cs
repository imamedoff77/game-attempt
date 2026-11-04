using UnityEngine;

/// <summary>Small flat meshes built in code (no primitives, so no physics module is needed at runtime).</summary>
public static class MeshFactory
{
    /// <summary>Horizontal quad covering x 0..width, z 0..depth at y = 0, UVs 0..1.</summary>
    public static Mesh Quad(float width, float depth)
    {
        var mesh = new Mesh { name = "Quad" };
        mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(width, 0, 0), new Vector3(width, 0, depth), new Vector3(0, 0, depth) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Thin flat strip along +x from the origin (length 1, width 1 centred on z), for tracers.</summary>
    public static Mesh Strip()
    {
        var mesh = new Mesh { name = "Strip" };
        mesh.vertices = new[] { new Vector3(0, 0, -0.5f), new Vector3(1, 0, -0.5f), new Vector3(1, 0, 0.5f), new Vector3(0, 0, 0.5f) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Flat pie slice at y = 0 pointing along +z: the given radius and total angle (degrees), centred on +z.</summary>
    public static Mesh Wedge(float radius, float arcDegrees, int segments = 24)
    {
        var verts = new Vector3[segments + 2];
        var tris = new int[segments * 3];
        verts[0] = Vector3.zero;
        float half = Mathf.Deg2Rad * arcDegrees * 0.5f;
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Lerp(-half, half, i / (float)segments);
            verts[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
        }
        for (int i = 0; i < segments; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 2;
            tris[i * 3 + 2] = i + 1;
        }
        var mesh = new Mesh { name = "Wedge", vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>Flat ring around the origin at y = 0 (use innerRadius near 0 for a disc).</summary>
    public static Mesh Ring(float innerRadius, float outerRadius, int segments = 28)
    {
        var verts = new Vector3[segments * 2];
        var tris = new int[segments * 6];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            verts[i * 2] = new Vector3(c * outerRadius, 0f, s * outerRadius);
            verts[i * 2 + 1] = new Vector3(c * innerRadius, 0f, s * innerRadius);
        }
        for (int i = 0; i < segments; i++)
        {
            int a = i * 2, d = i * 2 + 1;
            int b = ((i + 1) % segments) * 2, c = b + 1;
            int t = i * 6;
            tris[t] = a; tris[t + 1] = c; tris[t + 2] = b;
            tris[t + 3] = a; tris[t + 4] = d; tris[t + 5] = c;
        }
        var mesh = new Mesh { name = "Ring", vertices = verts, triangles = tris };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static GameObject Spawn(string name, Mesh mesh, Material material, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }
}
