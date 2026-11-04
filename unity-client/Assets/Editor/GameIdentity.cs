using System.IO;
using System.Text;
using SimCore;

/// <summary>
/// The game's name and everything derived from it. The ONLY place that holds the name is game.json in the repository root;
/// the app name, Android package id, APK file names and the README identity block all come from it.
/// To rename the game: edit game.json, then run  python3 tools/sync_identity.py  (and rebuild).
/// Pure C# (no Unity types), so the unit tests can check it too.
/// </summary>
public sealed class GameIdentity
{
    /// <summary>Display name, from game.json.</summary>
    public string Name { get; }
    /// <summary>Developer name, from game.json.</summary>
    public string Company { get; }

    /// <summary>the name without spaces and symbols, for file names.</summary>
    public string FileName { get; }
    /// <summary>Android package id (com.company.name, lowercase). Cannot be changed after the first Play Store upload.</summary>
    public string PackageId { get; }

    public string ReleaseApk => "Builds/Android/" + FileName + ".apk";
    public string DevApk => "Builds/Android/" + FileName + "-dev.apk";

    private GameIdentity(string name, string company)
    {
        Name = name;
        Company = company;
        FileName = Letters(name, keepCase: true);
        PackageId = "com." + Segment(company) + "." + Segment(name);
    }

    public static GameIdentity Parse(string json)
    {
        var map = MiniJson.Parse(json) as System.Collections.Generic.Dictionary<string, object>;
        if (map == null) throw new GameDataException("game.json: expected an object { \"name\": ..., \"company\": ... }");
        string name = Text(map, "name");
        string company = Text(map, "company");
        if (Letters(name, true).Length == 0) throw new GameDataException("game.json: \"name\" needs at least one letter or digit");
        if (Segment(company).Length == 0) throw new GameDataException("game.json: \"company\" needs at least one letter or digit");
        return new GameIdentity(name.Trim(), company.Trim());
    }

    public static GameIdentity Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Walks up from a folder until it finds game.json (the repository root).</summary>
    public static string FindFile(string startFolder)
    {
        string dir = startFolder;
        while (!string.IsNullOrEmpty(dir))
        {
            string candidate = Path.Combine(dir, "game.json");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("game.json not found above " + startFolder);
    }

    private static string Text(System.Collections.Generic.Dictionary<string, object> map, string key)
    {
        if (!map.TryGetValue(key, out object v) || !(v is string s)) throw new GameDataException("game.json: \"" + key + "\" must be text");
        return s;
    }

    /// <summary>Only ASCII letters and digits are kept.</summary>
    private static string Letters(string text, bool keepCase)
    {
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
            if (!ok) continue;
            sb.Append(keepCase ? c : char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>One part of a package id: lowercase letters and digits, and it must not start with a digit.</summary>
    private static string Segment(string text)
    {
        string s = Letters(text, keepCase: false);
        if (s.Length > 0 && s[0] >= '0' && s[0] <= '9') s = "g" + s;
        return s;
    }
}
