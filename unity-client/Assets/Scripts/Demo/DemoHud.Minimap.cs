using SimCore;
using UnityEngine;

/// <summary>
/// The minimap (bottom-left, in matches): the terrain, every building and unit as dots (blue = you, red = the enemy),
/// and a frame for what the camera sees. Touching or dragging on it moves the camera there.
/// </summary>
public sealed partial class DemoHud
{
    private static readonly Color MiniSand = new Color(0.62f, 0.52f, 0.34f), MiniStony = new Color(0.45f, 0.38f, 0.28f),
        MiniRock = new Color(0.25f, 0.22f, 0.2f), MiniWater = new Color(0.16f, 0.32f, 0.5f);
    private static readonly Color MiniMine = new Color(0.3f, 0.65f, 1f), MiniEnemy = new Color(1f, 0.3f, 0.25f);

    private Texture2D _miniTerrain;
    private World _miniWorld;

    private Rect MinimapRect()
    {
        float size = Screen.height * 0.3f, gap = Screen.height * 0.015f;
        return new Rect(gap, Screen.height - size - gap, size, size);
    }

    private bool MinimapVisible => _game.Mode != DemoGame.GameMode.Menu && _game.World != null && !_game.Placing;

    private void EnsureMinimapTexture(World world)
    {
        if (_miniWorld == world && _miniTerrain != null) return;
        _miniWorld = world;
        MapGrid map = world.Map;
        if (_miniTerrain != null) Destroy(_miniTerrain);
        _miniTerrain = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var pixels = new Color[map.Width * map.Height];
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                switch (map.GetTerrain(x, y))
                {
                    case TerrainType.Stony: pixels[y * map.Width + x] = MiniStony; break;
                    case TerrainType.Rock: pixels[y * map.Width + x] = MiniRock; break;
                    case TerrainType.Water: pixels[y * map.Width + x] = MiniWater; break;
                    default: pixels[y * map.Width + x] = MiniSand; break;
                }
            }
        _miniTerrain.SetPixels(pixels);
        _miniTerrain.Apply();
    }

    private void DrawMinimap(World world)
    {
        EnsureMinimapTexture(world);
        Rect r = MinimapRect();
        float w = world.Map.Width, h = world.Map.Height;

        // frame, terrain
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        float border = Screen.height * 0.006f;
        GUI.DrawTexture(new Rect(r.x - border, r.y - border, r.width + border * 2f, r.height + border * 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTextureWithTexCoords(r, _miniTerrain, new Rect(0f, 0f, 1f, 1f));

        float px = r.width / w, py = r.height / h;
        foreach (Building b in world.Buildings)
        {
            GUI.color = b.Team == _game.LocalTeam ? MiniMine : MiniEnemy;
            float x = r.x + b.X * px, y = r.yMax - (b.Y + b.Height) * py;
            GUI.DrawTexture(new Rect(x, y, Mathf.Max(3f, b.Width * px), Mathf.Max(3f, b.Height * py)), Texture2D.whiteTexture);
        }
        float dot = Mathf.Max(3f, Screen.height * 0.007f);
        foreach (Unit u in world.Units)
        {
            if (u.Hp <= 0) continue;
            GUI.color = u.Team == _game.LocalTeam ? MiniMine : MiniEnemy;
            Vector3 p = UnitView.ToUnity(u.Pos);
            GUI.DrawTexture(new Rect(r.x + p.x * px - dot / 2f, r.yMax - p.z * py - dot / 2f, dot, dot), Texture2D.whiteTexture);
        }

        // what the camera sees: the four screen corners on the ground (a trapezoid, because the camera looks along the ground)
        if (_game.Cam != null)
        {
            var corners = new Vector2[4];
            Vector2[] screen = { new Vector2(0f, 0f), new Vector2(Screen.width, 0f), new Vector2(Screen.width, Screen.height), new Vector2(0f, Screen.height) };
            bool ok = true;
            for (int i = 0; i < 4 && ok; i++)
            {
                ok = _game.Cam.GroundPoint(screen[i], out Vector3 g);
                corners[i] = new Vector2(r.x + Mathf.Clamp(g.x, 0f, w) * px, r.yMax - Mathf.Clamp(g.z, 0f, h) * py);
            }
            if (ok)
            {
                GUI.color = Color.white;
                float t = Mathf.Max(1.5f, Screen.height * 0.003f);
                for (int i = 0; i < 4; i++) DrawMiniLine(corners[i], corners[(i + 1) % 4], t);
            }
        }
        GUI.color = old;

        // touch or drag = look there
        Event e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && r.Contains(e.mousePosition) && _game.Cam != null)
        {
            float mx = (e.mousePosition.x - r.x) / r.width * w, mz = (r.yMax - e.mousePosition.y) / r.height * h;
            _game.Cam.FocusOn(new Vector3(mx, 0f, mz));
            e.Use();
        }
    }

    private static void DrawMiniLine(Vector2 a, Vector2 b, float thickness)
    {
        Vector2 d = b - a;
        float length = d.magnitude;
        if (length < 0.5f) return;
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
        GUI.DrawTexture(new Rect(a.x, a.y - thickness / 2f, length, thickness), Texture2D.whiteTexture);
        GUI.matrix = saved;
    }
}
