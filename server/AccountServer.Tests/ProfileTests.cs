using System.Net;
using AccountServer;

namespace AccountServer.Tests;

public class ProfileTests
{
    [Fact]
    public async Task NewProfile_IsEmptyAtRevisionZero()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register();
        var p = await TestServer.Json(await s.Get("/v1/profile", token));
        Assert.Equal(0, p.GetProperty("revision").GetInt64());
        Assert.Empty(p.GetProperty("data").EnumerateObject());
    }

    [Fact]
    public async Task Update_BumpsTheRevision_AndAStaleWriteGetsTheCurrentVersionBack()
    {
        await using TestServer s = await TestServer.StartAsync();
        string phone = await s.Register("player@example.com", "correct horse");
        string tablet = (await s.Login("player@example.com", "correct horse"))!;

        var first = await TestServer.Json(await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = "3,2,0|120", armory = "1,0,0,0,0,0" } }, phone));
        Assert.Equal(1, first.GetProperty("revision").GetInt64());

        // the tablet still thinks the profile is at revision 0
        HttpResponseMessage stale = await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = "overwrites" } }, tablet);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var details = (await TestServer.Json(stale)).GetProperty("details");
        Assert.Equal(1, details.GetProperty("revision").GetInt64());
        Assert.Equal("3,2,0|120", details.GetProperty("data").GetProperty("campaign").GetString());

        var second = await TestServer.Json(await s.Put("/v1/profile", new { baseRevision = 1, data = new { campaign = "4,2,0|140", armory = "1,0,0,0,0,0" } }, tablet));
        Assert.Equal(2, second.GetProperty("revision").GetInt64());
        var read = await TestServer.Json(await s.Get("/v1/profile", phone));
        Assert.Equal("4,2,0|140", read.GetProperty("data").GetProperty("campaign").GetString());
    }

    [Fact]
    public async Task Update_RejectsUnknownKeys_NonStrings_AndHugeValues()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register();
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Put("/v1/profile", new { baseRevision = 0, data = new { credits = "999999" } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = 5 } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = new { a = 1 } } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = new string('x', 513) } }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Put("/v1/profile", new { baseRevision = 0 }, token)).StatusCode);
        Assert.Equal(0, (await TestServer.Json(await s.Get("/v1/profile", token))).GetProperty("revision").GetInt64()); // nothing was written
    }

    [Fact]
    public async Task Profile_NeedsASession_AndEachPlayerHasHisOwn()
    {
        await using TestServer s = await TestServer.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = "1" } })).StatusCode);

        string a = await s.Register("a@example.com");
        string b = await s.Register("b@example.com");
        await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = "A" } }, a);
        Assert.Equal(0, (await TestServer.Json(await s.Get("/v1/profile", b))).GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task Requests_AreLimitedInSize()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register();
        HttpResponseMessage r = await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = new string('x', 100_000) } }, token);
        Assert.False(r.IsSuccessStatusCode);
    }
}

public class SecurityTests
{
    private static PasswordHasher Hasher(int iterations = 1000) => new(new AccountOptions { PasswordIterations = iterations });

    [Fact]
    public void Hash_IsSalted_AndVerifiesOnlyTheRightPassword()
    {
        PasswordHasher h = Hasher();
        string a = h.Hash("correct horse"), b = h.Hash("correct horse");
        Assert.NotEqual(a, b);
        Assert.DoesNotContain("correct horse", a);
        Assert.True(h.Verify("correct horse", a));
        Assert.False(h.Verify("Correct horse", a));
        Assert.False(h.Verify("", a));
    }

    [Fact]
    public void Verify_UsesTheIterationCountStoredWithTheHash_AndSurvivesGarbage()
    {
        string old = Hasher(500).Hash("secret password");
        Assert.True(Hasher(2000).Verify("secret password", old));
        PasswordHasher h = Hasher();
        foreach (string bad in new[] { "", "x", "a$b$c$d", "pbkdf2-sha256$0$AAAA$AAAA", "pbkdf2-sha256$1000$!!!$???" })
            Assert.False(h.Verify("secret password", bad));
    }

    [Fact]
    public void Tokens_AreLongAndDifferent_AndCodesAreEasyToType()
    {
        Assert.NotEqual(Tokens.New(), Tokens.New());
        Assert.True(Tokens.New().Length >= 43);
        for (int i = 0; i < 200; i++)
        {
            string code = Tokens.NewCode();
            Assert.Equal(8, code.Length);
            Assert.Matches("^[A-HJKMNP-Z2-9]{8}$", code);
        }
        Assert.Equal("ABCD1234", Tokens.NormalizeCode(" abcd-1234 "));
        Assert.Equal(Tokens.Hash("x"), Tokens.Hash("x"));
        Assert.NotEqual(Tokens.Hash("x"), Tokens.Hash("y"));
    }

    [Fact]
    public void Email_IsNormalized()
    {
        Assert.Equal("a@example.com", Endpoints.NormalizeEmail("  A@Example.COM "));
        Assert.Null(Endpoints.NormalizeEmail("Name <a@example.com>"));
        Assert.Null(Endpoints.NormalizeEmail(new string('a', 250) + "@example.com"));
        Assert.Null(Endpoints.NormalizeEmail(null));
    }
}
