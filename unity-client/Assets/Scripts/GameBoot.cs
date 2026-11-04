using SimCore;
using UnityEngine;

/// <summary>
/// Loads the shipped data before any scene object wakes up: balance numbers into the simulation, display texts into <see cref="Strings"/>.
/// If a file is broken the error is logged loudly and the game stops instead of running with half-loaded numbers.
/// </summary>
public static class GameBoot
{
    public const string GameDataResource = "gamedata";   // sim-core/src/SimCore/Resources/gamedata.json
    public const string StringsResource = "Strings/en";  // Assets/Resources/Strings/en.json

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Boot()
    {
        LoadAll();
    }

    /// <summary>Also callable from editor tools. Returns false (and logs) when something could not be loaded.</summary>
    public static bool LoadAll()
    {
        try
        {
            TextAsset data = Resources.Load<TextAsset>(GameDataResource);
            if (data == null) throw new GameDataException("Resources/" + GameDataResource + " is missing");
            Defs.Load(data.text);

            TextAsset strings = Resources.Load<TextAsset>(StringsResource);
            if (strings == null) throw new GameDataException("Resources/" + StringsResource + " is missing");
            Strings.Load(strings.text);

            Debug.Log("[Boot] game data loaded, hash " + Defs.DataHash.ToString("X16") + ", " + Strings.Table.Count + " texts");
            return true;
        }
        catch (GameDataException e)
        {
            Debug.LogError("[Boot] DATA ERROR: " + e.Message);
            return false;
        }
    }
}
