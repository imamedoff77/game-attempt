using System.Net;

namespace AccountServer.Tests;

/// <summary>A pretend Google Play: the test decides what every token is worth.</summary>
public sealed class FakePlay : IPlayVerifier
{
    public PlayState State = PlayState.Purchased;
    public bool Down;
    public int Calls;
    public bool Enabled => true;

    public Task<PlayPurchase> VerifyAsync(string productId, string purchaseToken)
    {
        Calls++;
        if (Down) throw new HttpRequestException("google is down");
        return Task.FromResult(new PlayPurchase(State, "GPA." + purchaseToken));
    }
}

public class StoreTests
{
    private static Task<HttpResponseMessage> Buy(TestServer s, string token, string product, string session) =>
        s.Post("/v1/store/purchase", new { productId = product, purchaseToken = token }, session);

    [Fact]
    public async Task ClosedStore_SellsNothing()
    {
        await using TestServer s = await TestServer.StartAsync();
        string session = await s.Register();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Buy(s, "dev-1", "credits_500", session)).StatusCode);
    }

    [Fact]
    public async Task APaidCreditPack_IsOfferedOnce_UntilTheGameAcknowledgesIt()
    {
        var play = new FakePlay();
        await using TestServer s = await TestServer.StartAsync(play: play);
        string session = await s.Register();

        var first = await TestServer.Json(await Buy(s, "token-A", "credits_1500", session));
        Assert.False(first.GetProperty("alreadyProcessed").GetBoolean());
        var pending = first.GetProperty("pending").EnumerateArray().ToList();
        Assert.Single(pending);
        Assert.Equal(1500, pending[0].GetProperty("credits").GetInt32());
        long id = pending[0].GetProperty("id").GetInt64();

        // the phone asks again after a lost answer: nothing is granted twice
        var repeat = await TestServer.Json(await Buy(s, "token-A", "credits_1500", session));
        Assert.True(repeat.GetProperty("alreadyProcessed").GetBoolean());
        Assert.Single(repeat.GetProperty("pending").EnumerateArray());
        Assert.Equal(1, play.Calls); // Google was asked only once

        var acked = await TestServer.Json(await s.Post("/v1/store/ack", new { ids = new[] { id } }, session));
        Assert.Empty(acked.GetProperty("pending").EnumerateArray());
        var again = await TestServer.Json(await Buy(s, "token-A", "credits_1500", session));
        Assert.Empty(again.GetProperty("pending").EnumerateArray()); // delivered purchases are not delivered again
    }

    [Fact]
    public async Task ABooster_StartsAtOnce_AndBuyingAnotherExtendsIt()
    {
        await using TestServer s = await TestServer.StartAsync(play: new FakePlay());
        string session = await s.Register();

        var one = await TestServer.Json(await Buy(s, "token-B1", "booster_credits_x2_7d", session));
        Assert.Empty(one.GetProperty("pending").EnumerateArray()); // nothing to collect
        var boosters = one.GetProperty("boosters").EnumerateArray().ToList();
        Assert.Single(boosters);
        Assert.Equal(Catalog.DoubleCredits, boosters[0].GetProperty("kind").GetString());
        DateTimeOffset firstEnd = boosters[0].GetProperty("expiresAt").GetDateTimeOffset();
        Assert.Equal(s.Clock.GetUtcNow().AddDays(7), firstEnd);

        var two = await TestServer.Json(await Buy(s, "token-B2", "booster_credits_x2_7d", session));
        Assert.Equal(firstEnd.AddDays(7), two.GetProperty("boosters")[0].GetProperty("expiresAt").GetDateTimeOffset());

        s.Clock.Advance(TimeSpan.FromDays(15)); // past both weeks
        var state = await TestServer.Json(await s.Get("/v1/store/state", session));
        Assert.Empty(state.GetProperty("boosters").EnumerateArray());
    }

    [Fact]
    public async Task APurchaseTokenCannotBeUsedByAnotherAccount()
    {
        await using TestServer s = await TestServer.StartAsync(play: new FakePlay());
        string a = await s.Register("a@example.com", "correct horse");
        string b = await s.Register("b@example.com", "correct horse");
        Assert.Equal(HttpStatusCode.OK, (await Buy(s, "shared-token", "credits_500", a)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Buy(s, "shared-token", "credits_500", b)).StatusCode);
        var stateB = await TestServer.Json(await s.Get("/v1/store/state", b));
        Assert.Empty(stateB.GetProperty("pending").EnumerateArray());
    }

    [Fact]
    public async Task BadPurchases_AreRefused_AndGoogleTroubleIsRetryable()
    {
        var play = new FakePlay();
        await using TestServer s = await TestServer.StartAsync(play: play);
        string session = await s.Register();

        Assert.Equal(HttpStatusCode.BadRequest, (await Buy(s, "t1", "no_such_product", session)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Buy(s, "", "credits_500", session)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Buy(s, new string('x', 2000), "credits_500", session)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Buy(s, "t2", "credits_500", "not-a-session")).StatusCode);

        play.State = PlayState.Invalid;
        Assert.Equal(HttpStatusCode.BadRequest, (await Buy(s, "t3", "credits_500", session)).StatusCode);
        play.State = PlayState.Pending;
        Assert.Equal(HttpStatusCode.Accepted, (await Buy(s, "t4", "credits_500", session)).StatusCode);
        play.Down = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Buy(s, "t5", "credits_500", session)).StatusCode);

        // none of those left anything behind, and the same tokens work once Google says they are paid
        play.Down = false;
        play.State = PlayState.Purchased;
        var ok = await TestServer.Json(await Buy(s, "t5", "credits_500", session));
        Assert.Single(ok.GetProperty("pending").EnumerateArray());
    }

    [Fact]
    public async Task DevPurchases_OnlyAcceptDevTokens_AndOnlyWhenSwitchedOn()
    {
        await using TestServer s = await TestServer.StartAsync(c => c["Account:Store:AllowDevPurchases"] = "true");
        string session = await s.Register();
        Assert.Equal(HttpStatusCode.BadRequest, (await Buy(s, "real-looking-token", "credits_500", session)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Buy(s, "dev-1", "credits_500", session)).StatusCode);

        var products = await TestServer.Json(await s.Get("/v1/store/products"));
        Assert.Equal(Catalog.All.Length, products.GetArrayLength());
    }
}
