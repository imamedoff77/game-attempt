using System.Net;
using AccountServer;

namespace AccountServer.Tests;

public class AuthTests
{
    [Fact]
    public async Task Register_ReturnsSessionAndUser_AndSendsVerificationMail()
    {
        await using TestServer s = await TestServer.StartAsync();
        HttpResponseMessage r = await s.Post("/v1/auth/register", new { email = "  Player@Example.com ", password = "correct horse", displayName = "Ace" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var body = await TestServer.Json(r);
        string token = body.GetProperty("token").GetString()!;
        Assert.False(string.IsNullOrEmpty(token));
        Assert.Equal("Ace", body.GetProperty("user").GetProperty("displayName").GetString());
        Assert.False(body.GetProperty("user").GetProperty("emailVerified").GetBoolean());

        var me = await TestServer.Json(await s.Get("/v1/me", token));
        Assert.Equal("Player@Example.com", me.GetProperty("email").GetString());

        Assert.Single(s.Mail.Sent);
        Assert.Equal("Player@Example.com", s.Mail.Last.To);
        Assert.StartsWith("https://game.example/verify-email?token=", s.Mail.LastLink);
    }

    [Fact]
    public async Task Register_Rejects_BadEmail_WeakPassword_AndDuplicates()
    {
        await using TestServer s = await TestServer.StartAsync();
        foreach (string bad in new[] { "", "no-at-sign", "a@b", "two@@example.com", "spaces in@example.com" })
            Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/register", new { email = bad, password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/register", new { email = "a@example.com", password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/register", new { email = "a@example.com", password = new string('x', 129) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/register", new { email = "same@example.com", password = "same@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/register")).StatusCode);

        await s.Register("dup@example.com");
        HttpResponseMessage dup = await s.Post("/v1/auth/register", new { email = "DUP@example.com", password = "another password" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Register_StillWorks_WhenTheMailServerIsDown()
    {
        await using TestServer s = await TestServer.StartAsync();
        s.Mail.Fail = true;
        string token = await s.Register();
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/v1/me", token)).StatusCode);
    }

    [Fact]
    public async Task DisplayName_IsCleanedAndShortened()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register("name@example.com", "correct horse", "  Very\nLong " + new string('x', 60));
        var me = await TestServer.Json(await s.Get("/v1/me", token));
        string name = me.GetProperty("displayName").GetString()!;
        Assert.True(name.Length <= Endpoints.MaxDisplayName);
        Assert.DoesNotContain('\n', name);

        string fallback = await s.Register("fallback@example.com");
        Assert.Equal("fallback", (await TestServer.Json(await s.Get("/v1/me", fallback))).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Login_Works_AndFailsTheSameWayForWrongPasswordAndUnknownEmail()
    {
        await using TestServer s = await TestServer.StartAsync();
        await s.Register("player@example.com", "correct horse");
        Assert.NotNull(await s.Login("PLAYER@example.com", "correct horse"));

        HttpResponseMessage wrong = await s.Post("/v1/auth/login", new { email = "player@example.com", password = "wrong password" });
        HttpResponseMessage unknown = await s.Post("/v1/auth/login", new { email = "nobody@example.com", password = "wrong password" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/auth/login")).StatusCode);
    }

    [Fact]
    public async Task Login_LocksTheAccount_AfterFiveWrongPasswords_ForFifteenMinutes()
    {
        await using TestServer s = await TestServer.StartAsync();
        await s.Register("player@example.com", "correct horse");
        for (int i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/auth/login", new { email = "player@example.com", password = "wrong " + i })).StatusCode);

        HttpResponseMessage locked = await s.Post("/v1/auth/login", new { email = "player@example.com", password = "correct horse" });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        Assert.InRange((await TestServer.Json(locked)).GetProperty("details").GetProperty("retryAfterSeconds").GetInt32(), 800, 900);

        s.Clock.Advance(TimeSpan.FromMinutes(16));
        Assert.NotNull(await s.Login("player@example.com", "correct horse"));
    }

    [Fact]
    public async Task Login_AGoodPassword_ResetsTheFailureCount()
    {
        await using TestServer s = await TestServer.StartAsync();
        await s.Register("player@example.com", "correct horse");
        for (int round = 0; round < 3; round++)
        {
            for (int i = 0; i < 4; i++) await s.Post("/v1/auth/login", new { email = "player@example.com", password = "wrong" });
            Assert.NotNull(await s.Login("player@example.com", "correct horse"));
        }
    }

    [Fact]
    public async Task Logout_EndsOnlyThatSession()
    {
        await using TestServer s = await TestServer.StartAsync();
        string first = await s.Register("player@example.com", "correct horse");
        string second = (await s.Login("player@example.com", "correct horse"))!;
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/logout", null, first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me", first)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/v1/me", second)).StatusCode);
    }

    [Fact]
    public async Task Sessions_ExpireAfterNinetyDays_AndGarbageTokensAreRejected()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register();
        s.Clock.Advance(TimeSpan.FromDays(89));
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/v1/me", token)).StatusCode);
        s.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me", token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me", "garbage")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me")).StatusCode);
    }

    [Fact]
    public async Task EmailVerification_LinkWorksOnce_AndExpires()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register();
        string link = s.Mail.LastLink.Replace("https://game.example", "");

        Assert.False((await TestServer.Json(await s.Get("/v1/me", token))).GetProperty("emailVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await s.Get(link)).StatusCode);
        Assert.True((await TestServer.Json(await s.Get("/v1/me", token))).GetProperty("emailVerified").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Get(link)).StatusCode); // already used
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Get("/verify-email?token=nonsense")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Get("/verify-email")).StatusCode);

        await using TestServer late = await TestServer.StartAsync();
        await late.Register();
        string lateLink = late.Mail.LastLink.Replace("https://game.example", "");
        late.Clock.Advance(TimeSpan.FromDays(4));
        Assert.Equal(HttpStatusCode.BadRequest, (await late.Get(lateLink)).StatusCode);
    }

    [Fact]
    public async Task ResendVerification_SendsANewLink_AndTheOldOneStopsWorking()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register();
        string oldLink = s.Mail.LastLink.Replace("https://game.example", "");
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/resend-verification", null, token)).StatusCode);
        Assert.Equal(2, s.Mail.Sent.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Get(oldLink)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await s.Get(s.Mail.LastLink.Replace("https://game.example", ""))).StatusCode);

        // already verified: nothing more is sent
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/resend-verification", null, token)).StatusCode);
        Assert.Equal(2, s.Mail.Sent.Count);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/auth/resend-verification")).StatusCode);
    }

    [Fact]
    public async Task PasswordReset_FullFlow_RevokesSessions_AndTheCodeWorksOnce()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register("player@example.com", "old password");
        int before = s.Mail.Sent.Count;

        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/forgot-password", new { email = "PLAYER@example.com" })).StatusCode);
        Assert.Equal(before + 1, s.Mail.Sent.Count);
        string code = s.Mail.LastCode;
        Assert.Equal(8, code.Length);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code = "WRONGCDE", newPassword = "new password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code, newPassword = "short" })).StatusCode);
        // typed by hand: lower case, with a dash
        string typed = (code[..4] + "-" + code[4..]).ToLowerInvariant();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code = typed, newPassword = "new password" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me", token)).StatusCode); // old sessions are gone
        Assert.Null(await s.Login("player@example.com", "old password"));
        Assert.NotNull(await s.Login("player@example.com", "new password"));
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code, newPassword = "third password" })).StatusCode);
    }

    [Fact]
    public async Task PasswordReset_UnknownAddress_LooksTheSame_AndSendsNothing()
    {
        await using TestServer s = await TestServer.StartAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/forgot-password", new { email = "nobody@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/forgot-password", new { email = "not an address" })).StatusCode);
        Assert.Empty(s.Mail.Sent);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/reset-password", new { email = "nobody@example.com", code = "ABCDEFGH", newPassword = "new password" })).StatusCode);
    }

    [Fact]
    public async Task PasswordReset_CodeDiesAfterFiveWrongTries_AndAfterThirtyMinutes()
    {
        await using TestServer s = await TestServer.StartAsync();
        await s.Register("player@example.com", "old password");
        await s.Post("/v1/auth/forgot-password", new { email = "player@example.com" });
        string code = s.Mail.LastCode;
        for (int i = 0; i < 5; i++)
            await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code = "AAAAAAA" + i, newPassword = "new password" });
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code, newPassword = "new password" })).StatusCode);

        await s.Post("/v1/auth/forgot-password", new { email = "player@example.com" });
        string fresh = s.Mail.LastCode;
        s.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code = fresh, newPassword = "new password" })).StatusCode);
        Assert.NotNull(await s.Login("player@example.com", "old password")); // never changed
    }

    [Fact]
    public async Task PasswordReset_NewCodeReplacesTheOldOne_AndConfirmsTheAddress()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register("player@example.com", "old password");
        await s.Post("/v1/auth/forgot-password", new { email = "player@example.com" });
        string first = s.Mail.LastCode;
        await s.Post("/v1/auth/forgot-password", new { email = "player@example.com" });
        string second = s.Mail.LastCode;
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code = first, newPassword = "new password" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/reset-password", new { email = "player@example.com", code = second, newPassword = "new password" })).StatusCode);

        string again = (await s.Login("player@example.com", "new password"))!;
        Assert.True((await TestServer.Json(await s.Get("/v1/me", again))).GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task ChangePassword_NeedsTheCurrentOne_AndSignsOutOtherDevices()
    {
        await using TestServer s = await TestServer.StartAsync();
        string phone = await s.Register("player@example.com", "old password");
        string tablet = (await s.Login("player@example.com", "old password"))!;

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Post("/v1/me/password", new { currentPassword = "nope", newPassword = "new password" }, phone)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/me/password", new { currentPassword = "old password", newPassword = "short" }, phone)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/me/password", new { currentPassword = "old password", newPassword = "new password" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/me/password", new { currentPassword = "old password", newPassword = "new password" }, phone)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await s.Get("/v1/me", phone)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me", tablet)).StatusCode);
        Assert.Null(await s.Login("player@example.com", "old password"));
        Assert.NotNull(await s.Login("player@example.com", "new password"));
    }

    [Fact]
    public async Task UpdateName_Works()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register();
        var me = await TestServer.Json(await s.Put("/v1/me", new { displayName = "Commander" }, token));
        Assert.Equal("Commander", me.GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Put("/v1/me", new { displayName = "  " }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Put("/v1/me", new { displayName = "X" })).StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_NeedsThePassword_AndRemovesEverything()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register("player@example.com", "correct horse");
        await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = "3,0" } }, token);

        Assert.Equal(HttpStatusCode.Forbidden, (await s.Post("/v1/me/delete", new { password = "wrong" }, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.Post("/v1/me/delete", null, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/me/delete", new { password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/me/delete", new { password = "correct horse" }, token)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me", token)).StatusCode);
        Assert.Null(await s.Login("player@example.com", "correct horse"));
        // the address can be used again, with a clean profile
        string again = await s.Register("player@example.com", "correct horse");
        Assert.Equal(0, (await TestServer.Json(await s.Get("/v1/profile", again))).GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task SignInEndpoints_AreRateLimitedPerAddress()
    {
        await using TestServer s = await TestServer.StartAsync(c => c["Account:AuthRatePerMinute"] = "4");
        for (int i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/auth/login", new { email = "a@example.com", password = "x" })).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await s.Post("/v1/auth/login", new { email = "a@example.com", password = "x" })).StatusCode);
        // other endpoints are not part of the limit
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me")).StatusCode);
    }
}
