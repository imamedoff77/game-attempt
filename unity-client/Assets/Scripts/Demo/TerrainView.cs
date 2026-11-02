using SimCore;
using UnityEngine;

/// <summary>
/// Draws the desert from the simulation's MapGrid: a painted ground texture, rocks on blocked cells,
/// some decoration, and an overlay that shows where buildings can / cannot be placed.
/// Purely visual: the rules live in SimCore.
/// </summary>
public sealed class TerrainView : MonoBehaviour
{
    private const int PixelsPerCell = 6;

    private static readonly Color Sand = new Color(0.87f, 0.76f, 0.52f);
    private static readonly Color Stony = new Color(0.66f, 0.58f, 0.47f);
    private static readonly Color RockGround = new Color(0.55f, 0.48f, 0.40f);
    private static readonly Color Water = new Color(0.20f, 0.50f, 0.68f);

    private GameObject _overlay;
    private Texture2D _overlayTexture;
    private MapGrid _map;

    public void Build(MapGrid map, Material groundMaterial, Material overlayMaterial, GameObject[] rocks,
                      GameObject palm, GameObject cactus, GameObject bush, Material palette, Vector2Int propFreeCenter, int propFreeRadius)
    {
        _map = map;

        // Ground
        var ground = MeshFactory.Spawn("Ground", MeshFactory.Quad(map.Width, map.Height),
            new Material(groundMaterial) { mainTexture = BuildGroundTexture(map) }, transform);
        ground.transform.localPosition = Vector3.zero;

        // Overlay (hidden until asked for)
        _overlayTexture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        _overlay = MeshFactory.Spawn("BuildOverlay", MeshFactory.Quad(map.Width, map.Height),
            new Material(overlayMaterial) { mainTexture = _overlayTexture }, transform);
        _overlay.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        RefreshOverlay();
        _overlay.SetActive(false);

        PlaceProps(map, rocks, palm, cactus, bush, palette, propFreeCenter, propFreeRadius);
    }

    public void SetOverlayVisible(bool visible)
    {
        if (visible) RefreshOverlay();
        _overlay.SetActive(visible);
    }

    public bool OverlayVisible => _overlay != null && _overlay.activeSelf;

    /// <summary>Green = a building can go here, red = it cannot.</summary>
    public void RefreshOverlay()
    {
        var pixels = new Color32[_map.Width * _map.Height];
        var green = new Color32(60, 230, 90, 105);
        var red = new Color32(235, 60, 50, 105);
        for (int y = 0; y < _map.Height; y++)
            for (int x = 0; x < _map.Width; x++)
                pixels[y * _map.Width + x] = _map.GetTerrain(x, y) == TerrainType.Sand && _map.BuildingAt(x, y) == 0 ? green : red;
        _overlayTexture.SetPixels32(pixels);
        _overlayTexture.Apply();
    }

    // ---------------------------------------------------------------- ground painting

    private static Texture2D BuildGroundTexture(MapGrid map)
    {
        int w = map.Width * PixelsPerCell, h = map.Height * PixelsPerCell;
        var pixels = new Color32[w * h];

        for (int py = 0; py < h; py++)
        {
            float cz = (py + 0.5f) / PixelsPerCell;
            for (int px = 0; px < w; px++)
            {
                float cx = (px + 0.5f) / PixelsPerCell;
                int nx = Mathf.Min((int)cx, map.Width - 1), nz = Mathf.Min((int)cz, map.Height - 1);
                TerrainType nearest = map.GetTerrain(nx, nz);

                Color c;
                if (nearest == TerrainType.Water)
                {
                    float wave = 0.05f * Mathf.Sin((cx + cz) * 3.1f) + 0.03f * Mathf.Sin(cx * 5.3f - cz * 2.1f);
                    c = Water * (1f + wave);
                }
                else if (nearest == TerrainType.Rock)
                {
                    c = RockGround;
                }
                else
                {
                    c = SoftColor(map, cx, cz);
                    // Slow, wide ripples so the sand reads like dunes.
                    float dune = 0.045f * Mathf.Sin(cx * 0.35f + 2f * Mathf.Sin(cz * 0.17f)) + 0.03f * Mathf.Sin(cz * 0.5f + cx * 0.12f);
                    c *= 1f + dune;
                }

                float grain = (Hash(px, py) - 0.5f) * 0.05f;
                c *= 1f + grain;
                pixels[py * w + px] = new Color(c.r, c.g, c.b, 1f);
            }
        }

        var tex = new Texture2D(w, h, TextureFormat.RGB24, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    // Bilinear blend between neighbouring cells' ground colours, so sand melts into stony ground.
    private static Color SoftColor(MapGrid map, float cx, float cz)
    {
        float fx = cx - 0.5f, fz = cz - 0.5f;
        int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
        float tx = fx - x0, tz = fz - z0;
        Color a = Color.Lerp(CellColor(map, x0, z0), CellColor(map, x0 + 1, z0), tx);
        Color b = Color.Lerp(CellColor(map, x0, z0 + 1), CellColor(map, x0 + 1, z0 + 1), tx);
        return Color.Lerp(a, b, tz);
    }

    private static Color CellColor(MapGrid map, int x, int z)
    {
        x = Mathf.Clamp(x, 0, map.Width - 1);
        z = Mathf.Clamp(z, 0, map.Height - 1);
        TerrainType t = map.GetTerrain(x, z);
        Color baseColor = (t == TerrainType.Stony || t == TerrainType.Rock) ? Stony : Sand;
        return baseColor * (0.97f + 0.06f * Hash(x * 7 + 3, z * 13 + 5));
    }

    private static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFF) / 65535f;
        }
    }

    // ---------------------------------------------------------------- props

    private void PlaceProps(MapGrid map, GameObject[] rocks, GameObject palm, GameObject cactus, GameObject bush,
                            Material palette, Vector2Int freeCenter, int freeRadius)
    {
        var props = new GameObject("Props").transform;
        props.SetParent(transform, false);

        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                TerrainType t = map.GetTerrain(x, y);
                float r1 = Hash(x * 31 + 1, y * 17 + 9), r2 = Hash(x * 11 + 7, y * 23 + 2), r3 = Hash(x * 5 + 13, y * 3 + 4);

                if (t == TerrainType.Rock)
                {
                    GameObject rock = rocks[(int)(r1 * rocks.Length) % rocks.Length];
                    float scale = 1.25f + r2 * 0.7f;
                    Spawn(rock, props, palette, new Vector3(x + 0.5f + (r3 - 0.5f) * 0.3f, 0f, y + 0.5f + (r1 - 0.5f) * 0.3f),
                          r2 * 360f, new Vector3(scale, scale * (0.8f + r3 * 0.6f), scale));
                    continue;
                }

                if (t != TerrainType.Sand && t != TerrainType.Stony) continue;
                if (map.BuildingAt(x, y) != 0) continue;
                if (Mathf.Abs(x - freeCenter.x) <= freeRadius && Mathf.Abs(y - freeCenter.y) <= freeRadius) continue;

                bool nextToWater = NextTo(map, x, y, TerrainType.Water);
                if (nextToWater && r1 < 0.35f)
                    Spawn(palm, props, palette, new Vector3(x + 0.5f, 0f, y + 0.5f), r2 * 360f, Vector3.one * (1.1f + r3 * 0.5f));
                else if (t == TerrainType.Sand && r1 < 0.025f)
                    Spawn(cactus, props, palette, new Vector3(x + 0.5f, 0f, y + 0.5f), r2 * 360f, Vector3.one * (0.9f + r3 * 0.5f));
                else if (r1 < 0.06f)
                    Spawn(bush, props, palette, new Vector3(x + 0.5f, 0f, y + 0.5f), r2 * 360f, Vector3.one * (0.8f + r3 * 0.6f));
            }
        }
    }

    private static bool NextTo(MapGrid map, int x, int y, TerrainType type)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (map.InBounds(x + dx, y + dy) && map.GetTerrain(x + dx, y + dy) == type) return true;
        return false;
    }

    private static void Spawn(GameObject model, Transform parent, Material palette, Vector3 position, float yaw, Vector3 scale)
    {
        GameObject go = Instantiate(model, parent);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = scale;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = palette;
    }
}
