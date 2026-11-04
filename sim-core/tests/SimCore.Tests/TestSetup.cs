using System.Runtime.CompilerServices;
using SimCore;

/// <summary>Loads the shipped balance data once, before any test touches the simulation.</summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void LoadGameData()
    {
        Defs.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gamedata.json")));
    }
}
