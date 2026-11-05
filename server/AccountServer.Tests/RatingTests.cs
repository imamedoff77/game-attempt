using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AccountServer.Tests;

public class RatingEndpointTests
{
    private const string Key = "relay-secret";

    private static async Task<HttpResponseMessage> Report(TestServer s, string? key, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/internal/report-match") { Content = JsonContent.Create(body) };
        if (key != null) request.Headers.Add("X-Internal-Key", key);
        return await s.Client.SendAsync(request);
    }

    private static async Task<long> IdOf(TestServer s, string token) => (await TestServer.Json(await s.Get("/v1/me", token))).GetProperty("id").GetInt64();

    [Fact]
    public async Task NewAccounts_StartAtTheStartRating()
    {
        await using TestServer s = await TestServer.StartAsync(c => c["Account:InternalKey"] = Key);
        string token = await s.Register();
        var me = await TestServer.Json(await s.Get("/v1/me", token));
        Assert.Equal(1000, me.GetProperty("rating").GetInt32());
        Assert.Equal(0, me.GetProperty("ratedGames").GetInt32());
    }

    [Fact]
    public async Task ReportMatch_MovesBothRatings_AndCountsTheMatchOnlyOnce()
    {
        await using TestServer s = await TestServer.StartAsync(c => c["Account:InternalKey"] = Key);
        string a = await s.Register("a@example.com", "correct horse");
        string b = await s.Register("b@example.com", "correct horse");
        long idA = await IdOf(s, a), idB = await IdOf(s, b);

        var first = await TestServer.Json(await Report(s, Key, new { matchId = "m1", winnerId = idA, loserId = idB }));
        Assert.False(first.GetProperty("alreadyRecorded").GetBoolean());
        Assert.Equal(1016, first.GetProperty("winnerNew").GetInt32());
        Assert.Equal(984, first.GetProperty("loserNew").GetInt32());

        var again = await TestServer.Json(await Report(s, Key, new { matchId = "m1", winnerId = idB, loserId = idA })); // a repeat (even a wrong one) changes nothing
        Assert.True(again.GetProperty("alreadyRecorded").GetBoolean());
        Assert.Equal(1016, again.GetProperty("winnerNew").GetInt32());

        var meA = await TestServer.Json(await s.Get("/v1/me", a));
        var meB = await TestServer.Json(await s.Get("/v1/me", b));
        Assert.Equal(1016, meA.GetProperty("rating").GetInt32());
        Assert.Equal(1, meA.GetProperty("ratedWins").GetInt32());
        Assert.Equal(1, meA.GetProperty("ratedGames").GetInt32());
        Assert.Equal(984, meB.GetProperty("rating").GetInt32());
        Assert.Equal(0, meB.GetProperty("ratedWins").GetInt32());
        Assert.Equal(1, meB.GetProperty("ratedGames").GetInt32());

        var verified = await TestServer.Json(await s.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/v1/internal/verify-session")
        {
            Content = JsonContent.Create(new { token = a }),
            Headers = { { "X-Internal-Key", Key } },
        }));
        Assert.Equal(1016, verified.GetProperty("rating").GetInt32());
    }

    [Fact]
    public async Task ReportMatch_RefusesStrangersAndNonsense()
    {
        await using TestServer s = await TestServer.StartAsync(c => c["Account:InternalKey"] = Key);
        string a = await s.Register("a@example.com", "correct horse");
        long idA = await IdOf(s, a);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Report(s, null, new { matchId = "m", winnerId = idA, loserId = idA + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Report(s, "wrong", new { matchId = "m", winnerId = idA, loserId = idA + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(s, Key, new { matchId = "", winnerId = idA, loserId = idA + 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(s, Key, new { matchId = "m", winnerId = idA, loserId = idA })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Report(s, Key, new { matchId = "m", winnerId = idA, loserId = idA + 999 })).StatusCode);
    }

    [Fact]
    public async Task ReportMatch_DoesNotExistWithoutAKey()
    {
        await using TestServer s = await TestServer.StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await Report(s, "x", new { matchId = "m", winnerId = 1, loserId = 2 })).StatusCode);
    }
}
