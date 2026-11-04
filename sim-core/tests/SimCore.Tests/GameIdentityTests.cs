using SimCore;

public class GameIdentityTests
{
    private static string RepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "game.json"))) dir = Path.GetDirectoryName(dir)!;
        return dir ?? throw new InvalidOperationException("game.json not found");
    }

    [Fact]
    public void NameAndPackageId_AreDerivedFromOneFile()
    {
        var id = GameIdentity.Parse("{\"name\": \"Iron Doctrine\", \"company\": \"imamedoff\"}");
        Assert.Equal("Iron Doctrine", id.Name);
        Assert.Equal("IronDoctrine", id.FileName);
        Assert.Equal("com.imamedoff.irondoctrine", id.PackageId);
        Assert.Equal("Builds/Android/IronDoctrine.apk", id.ReleaseApk);
        Assert.Equal("Builds/Android/IronDoctrine-dev.apk", id.DevApk);
    }

    [Fact]
    public void ChangingTheName_ChangesEverythingDerived()
    {
        var id = GameIdentity.Parse("{\"name\": \"Steel Command 2\", \"company\": \"Some Studio\"}");
        Assert.Equal("com.somestudio.steelcommand2", id.PackageId);
        Assert.Equal("SteelCommand2", id.FileName);
        Assert.Equal("Builds/Android/SteelCommand2-dev.apk", id.DevApk);
    }

    [Fact]
    public void NameStartingWithDigit_StillGivesAValidPackageId()
    {
        var id = GameIdentity.Parse("{\"name\": \"1914\", \"company\": \"x\"}");
        Assert.Equal("com.x.g1914", id.PackageId);
    }

    [Theory]
    [InlineData("{\"name\": \"\", \"company\": \"a\"}")]
    [InlineData("{\"name\": \"!!!\", \"company\": \"a\"}")]
    [InlineData("{\"name\": \"A\", \"company\": \"\"}")]
    [InlineData("{\"name\": \"A\"}")]
    [InlineData("[]")]
    public void BadFile_IsRejected(string json)
    {
        Assert.Throws<GameDataException>(() => GameIdentity.Parse(json));
    }

    [Fact]
    public void Readme_ShowsTheNameFromGameJson()
    {
        // If this fails after renaming the game, run: python3 tools/sync_identity.py
        string root = RepoRoot();
        var id = GameIdentity.Load(Path.Combine(root, "game.json"));
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Assert.Contains("# " + id.Name, readme);
        Assert.Contains(id.PackageId, readme);
        Assert.Contains("unity-client/" + id.ReleaseApk, readme);
        Assert.Contains("unity-client/" + id.DevApk, readme);
        Assert.Contains("adb install -r " + id.DevApk, readme);
    }

    [Fact]
    public void NoOldNameLeftInTheCode()
    {
        string root = RepoRoot();
        foreach (string folder in new[] { "unity-client/Assets/Editor", "unity-client/Assets/Scripts" })
            foreach (string file in Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                Assert.DoesNotContain("com.imamedoff", text);
                Assert.DoesNotContain("GameAttempt", text);
                Assert.DoesNotContain("Iron Doctrine", text);
            }
    }
}
