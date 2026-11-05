using System.Text.RegularExpressions;
using SimCore;

public class StringTableTests
{
    private static StringTable English() => new StringTable(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "en.json")));

    [Fact]
    public void Get_ReturnsTheText_AndFillsPlaceholders()
    {
        var t = new StringTable("{\"a\": \"Hello\", \"b\": \"{0} of {1}\"}");
        Assert.Equal("Hello", t.Get("a"));
        Assert.Equal("3 of 7", t.Get("b", 3, 7));
    }

    [Fact]
    public void MissingKey_ShowsTheKey_AndIsReportedOnlyOnce()
    {
        var t = new StringTable("{}");
        var reported = new List<string>();
        t.OnMissing = reported.Add;
        Assert.Equal("ui.nothing", t.Get("ui.nothing"));
        Assert.Equal("ui.nothing", t.Get("ui.nothing", 1));
        Assert.Equal(new[] { "ui.nothing" }, reported);
    }

    [Fact]
    public void BadPlaceholders_DoNotThrow()
    {
        var t = new StringTable("{\"a\": \"{0} {1} {2}\", \"b\": \"{oops\"}");
        Assert.Equal("{0} {1} {2}", t.Get("a", 1));
        Assert.Equal("{oops", t.Get("b", 1));
    }

    [Fact]
    public void NonTextValue_IsRejected()
    {
        Assert.Throws<GameDataException>(() => new StringTable("{\"a\": 5}"));
        Assert.Throws<GameDataException>(() => new StringTable("[1]"));
    }

    [Fact]
    public void English_HasNameForEveryUnitAndBuilding()
    {
        StringTable en = English();
        foreach (UnitKind k in Enum.GetValues<UnitKind>()) Assert.True(en.Has("unit." + k), "unit." + k);
        foreach (BuildingKind k in Enum.GetValues<BuildingKind>()) Assert.True(en.Has("building." + k), "building." + k);
        foreach (UpgradeKind k in Enum.GetValues<UpgradeKind>()) Assert.True(en.Has("upgrade." + k), "upgrade." + k);
        foreach (AiDifficulty k in Enum.GetValues<AiDifficulty>()) Assert.True(en.Has("difficulty." + k), "difficulty." + k);
    }

    [Fact]
    public void English_EveryKeyUsedInTheClient_Exists()
    {
        // Every Strings.Get("...") in the client source must have a text, so a gap shows up here and not as a raw key on the phone.
        StringTable en = English();
        string root = FindRepoRoot();
        var used = new HashSet<string>();
        foreach (string file in Directory.GetFiles(Path.Combine(root, "unity-client", "Assets", "Scripts"), "*.cs", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "Strings\\.Get\\(\\s*\"([^\"]+)\""))
                used.Add(m.Groups[1].Value);
        foreach (Match m in Regex.Matches(File.ReadAllText(Path.Combine(root, "unity-client", "Assets", "Scripts", "Demo", "DemoHud.cs")), "Get\\(\\s*[\\w.]+\\s*\\?\\s*\"([^\"]+)\"\\s*:\\s*\"([^\"]+)\""))
        {
            used.Add(m.Groups[1].Value);
            used.Add(m.Groups[2].Value);
        }

        Assert.NotEmpty(used);
        foreach (string key in used) Assert.True(en.Has(key), "missing text: " + key);
    }

    [Fact]
    public void English_HasNoUnusedPlaceholderGaps()
    {
        // Placeholders must be numbered from 0 without holes.
        StringTable en = English();
        foreach (string key in en.Keys)
        {
            var numbers = Regex.Matches(en.Get(key), "\\{(\\d+)\\}").Select(m => int.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n).ToList();
            for (int i = 0; i < numbers.Count; i++) Assert.True(numbers[i] == i, key + " has a gap in its placeholders");
        }
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "unity-client"))) dir = Path.GetDirectoryName(dir);
        return dir ?? throw new InvalidOperationException("repo root not found");
    }
}
