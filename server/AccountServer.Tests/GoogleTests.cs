using System.Net;

namespace AccountServer.Tests;

public class GoogleTests
{
    private static async Task<(string Token, System.Text.Json.JsonElement Body)> SignIn(TestServer s, string idToken)
    {
        HttpResponseMessage r = await s.Post("/v1/auth/google", new { idToken });
        var body = await TestServer.Json(r);
        return (r.IsSuccessStatusCode ? body.GetProperty("token").GetString()! : "", body);
    }

    [Fact]
    public async Task NewGoogleUser_GetsAnAccount_AndSignsInAgainToTheSameOne()
    {
        await using TestServer s = await TestServer.StartAsync();
        (string token, var body) = await SignIn(s, s.Google.Issue("sub-1", "Gamer@Gmail.com", name: "Gamer One"));
        Assert.True(body.GetProperty("created").GetBoolean());
        var user = body.GetProperty("user");
        Assert.True(user.GetProperty("emailVerified").GetBoolean());
        Assert.False(user.GetProperty("hasPassword").GetBoolean());
        Assert.True(user.GetProperty("hasGoogle").GetBoolean());
        Assert.Equal("Gamer One", user.GetProperty("displayName").GetString());
        Assert.Empty(s.Mail.Sent);

        (string again, var second) = await SignIn(s, s.Google.Issue("sub-1", "gamer@gmail.com"));
        Assert.False(second.GetProperty("created").GetBoolean());
        Assert.Equal(user.GetProperty("id").GetInt64(), second.GetProperty("user").GetProperty("id").GetInt64());
        Assert.NotEqual(token, again);
    }

    [Fact]
    public async Task GoogleUser_CannotLogInWithAPassword_ButCanSetOneByReset()
    {
        await using TestServer s = await TestServer.StartAsync();
        await SignIn(s, s.Google.Issue("sub-1", "gamer@gmail.com"));
        Assert.Null(await s.Login("gamer@gmail.com", "anything at all"));

        await s.Post("/v1/auth/forgot-password", new { email = "gamer@gmail.com" });
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/auth/reset-password", new { email = "gamer@gmail.com", code = s.Mail.LastCode, newPassword = "my new password" })).StatusCode);
        Assert.NotNull(await s.Login("gamer@gmail.com", "my new password"));
    }

    [Fact]
    public async Task Google_LinksToAnExistingVerifiedPasswordAccount_AndKeepsThePassword()
    {
        await using TestServer s = await TestServer.StartAsync();
        string token = await s.Register("player@gmail.com", "correct horse");
        await s.Get(s.Mail.LastLink.Replace("https://game.example", "")); // confirmed the address
        await s.Put("/v1/profile", new { baseRevision = 0, data = new { campaign = "5" } }, token);

        (string google, var body) = await SignIn(s, s.Google.Issue("sub-9", "player@gmail.com"));
        Assert.False(body.GetProperty("created").GetBoolean());
        Assert.True(body.GetProperty("user").GetProperty("hasPassword").GetBoolean());
        Assert.NotNull(await s.Login("player@gmail.com", "correct horse"));
        Assert.Equal("5", (await TestServer.Json(await s.Get("/v1/profile", google))).GetProperty("data").GetProperty("campaign").GetString());
    }

    [Fact]
    public async Task Google_TakingOverAnUnverifiedPasswordAccount_RemovesThatPassword()
    {
        // Someone registered the victim's address with a password of their own, without being able to read the mail.
        await using TestServer s = await TestServer.StartAsync();
        string attacker = await s.Register("victim@gmail.com", "attacker password");

        (string google, var body) = await SignIn(s, s.Google.Issue("sub-victim", "victim@gmail.com"));
        Assert.True(body.GetProperty("user").GetProperty("emailVerified").GetBoolean());
        Assert.False(body.GetProperty("user").GetProperty("hasPassword").GetBoolean());
        Assert.Null(await s.Login("victim@gmail.com", "attacker password"));
        Assert.Equal(HttpStatusCode.OK, (await s.Get("/v1/me", google)).StatusCode);
    }

    [Fact]
    public async Task Google_RejectsUnknownTokens_UnverifiedMail_AndAnotherGoogleIdOnTheSameAddress()
    {
        await using TestServer s = await TestServer.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/auth/google", new { idToken = "forged" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/google", new { idToken = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Post("/v1/auth/google")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/auth/google", new { idToken = s.Google.Issue("sub-2", "x@gmail.com", emailVerified: false) })).StatusCode);

        await SignIn(s, s.Google.Issue("sub-3", "shared@gmail.com"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Post("/v1/auth/google", new { idToken = s.Google.Issue("sub-4", "shared@gmail.com") })).StatusCode);
    }

    [Fact]
    public async Task GoogleUser_CanDeleteTheAccountWithoutAPassword()
    {
        await using TestServer s = await TestServer.StartAsync();
        (string token, _) = await SignIn(s, s.Google.Issue("sub-1", "gamer@gmail.com"));
        Assert.Equal(HttpStatusCode.NoContent, (await s.Post("/v1/me/delete", null, token)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Get("/v1/me", token)).StatusCode);
    }
}
