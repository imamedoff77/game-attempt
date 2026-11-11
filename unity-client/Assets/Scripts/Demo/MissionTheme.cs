using UnityEngine;

/// <summary>
/// How a campaign mission's battlefield looks: ground colours and the sky behind the map. Purely visual (the simulation
/// never reads it), so every mission can feel like a different theatre without touching the rules.
/// Skirmish, online and the menu backdrop use the default desert.
/// </summary>
public readonly struct MissionTheme
{
    public readonly Color Sand, Stony, Rock, Background;

    private MissionTheme(Color sand, Color stony, Color rock, Color background)
    {
        Sand = sand; Stony = stony; Rock = rock; Background = background;
    }

    private static MissionTheme Make(float r, float g, float b, float stonyMix = 0.78f) =>
        new MissionTheme(new Color(r, g, b), new Color(r * stonyMix, g * stonyMix * 0.99f, b * stonyMix * 0.96f),
                         new Color(r * 0.64f, g * 0.62f, b * 0.6f), new Color(r * 0.48f, g * 0.46f, b * 0.5f));

    public static readonly MissionTheme Desert = Make(0.87f, 0.76f, 0.52f, 0.76f);

    /// <summary>The theme of the mission being played (the default desert outside the campaign).</summary>
    public static MissionTheme Current = Desert;

    public static MissionTheme For(int mission)
    {
        switch (mission)
        {
            case 1: return Desert;                              // Outpost Alpha: open desert
            case 2: return Make(0.74f, 0.71f, 0.45f);           // Supply Lines: dry scrubland
            case 3: return Make(0.78f, 0.52f, 0.36f);           // Hold the Line: red earth
            case 4: return Make(0.91f, 0.84f, 0.65f);           // First Skirmish: pale dust flats
            case 5: return Make(0.62f, 0.60f, 0.55f, 0.85f);   // Iron Fist: grey gravel
            case 6: return Make(0.72f, 0.45f, 0.30f, 0.82f);    // Canyon Run: red canyon floor
            case 7: return Make(0.55f, 0.62f, 0.38f);           // Even Odds: steppe
            case 8: return Make(0.47f, 0.44f, 0.40f, 0.88f);   // The Fortress: dark basalt
            case 9: return Make(0.60f, 0.52f, 0.47f);           // Last Stand: ash and dusk
            case 10: return Make(0.40f, 0.35f, 0.32f, 0.9f);   // Final Offensive: scorched ground
            default: return Desert;
        }
    }
}
