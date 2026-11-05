using System.Net.Http.Json;
using System.Text.Json;
using SimCore;

namespace GameServer;

/// <summary>Who a session token belongs to, as the account server says it.</summary>
public sealed record VerifiedPlayer(long UserId, string DisplayName, byte[] Kit, int Rating = Rating.Start);

/// <summary>What a counted ranked match did to the two ratings.</summary>
public sealed record RatedOutcome(int WinnerOld, int WinnerNew, int LoserOld, int LoserNew);

/// <summary>Asks the account server about a token. Blocking on purpose: it runs on the player's own message task.</summary>
public interface IAccountVerifier
{
    /// <summary>Null when the token is unknown, expired, or the account server could not be asked.</summary>
    VerifiedPlayer? Verify(string token);

    /// <summary>Tells the account server who won a ranked match (counted once per <paramref name="matchId"/>). Null when it could not be counted.</summary>
    RatedOutcome? ReportMatch(string matchId, long winnerId, long loserId);
}

/// <summary>Talks to <c>POST /v1/internal/verify-session</c> of the account server (see server/ACCOUNTS.md).</summary>
public sealed class HttpAccountVerifier : IAccountVerifier, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _key;

    public HttpAccountVerifier(string baseUrl, string internalKey, TimeSpan? timeout = null)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = timeout ?? TimeSpan.FromSeconds(5) };
        _key = internalKey;
    }

    public VerifiedPlayer? Verify(string token)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "v1/internal/verify-session") { Content = JsonContent.Create(new { token }) };
            request.Headers.Add("X-Internal-Key", _key);
            using HttpResponseMessage response = _http.Send(request);
            if (!response.IsSuccessStatusCode) return null;
            using JsonDocument doc = JsonDocument.Parse(response.Content.ReadAsStream());
            JsonElement root = doc.RootElement;
            return new VerifiedPlayer(
                root.GetProperty("userId").GetInt64(),
                root.TryGetProperty("displayName", out JsonElement name) ? name.GetString() ?? "" : "",
                Armory.Parse(root.TryGetProperty("armory", out JsonElement armory) ? armory.GetString() : "").ToBytes(),
                root.TryGetProperty("rating", out JsonElement rating) ? rating.GetInt32() : Rating.Start);
        }
        catch (Exception)
        {
            return null; // account server down: nobody gets in while the server cannot tell who they are
        }
    }

    public RatedOutcome? ReportMatch(string matchId, long winnerId, long loserId)
    {
        // The result must not get lost because the account server was busy for a moment: a few tries, then give up (the caller logs it).
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "v1/internal/report-match") { Content = JsonContent.Create(new { matchId, winnerId, loserId }) };
                request.Headers.Add("X-Internal-Key", _key);
                using HttpResponseMessage response = _http.Send(request);
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound || response.StatusCode == System.Net.HttpStatusCode.BadRequest) return null; // will never work
                if (response.IsSuccessStatusCode)
                {
                    using JsonDocument doc = JsonDocument.Parse(response.Content.ReadAsStream());
                    JsonElement r = doc.RootElement;
                    return new RatedOutcome(r.GetProperty("winnerOld").GetInt32(), r.GetProperty("winnerNew").GetInt32(), r.GetProperty("loserOld").GetInt32(), r.GetProperty("loserNew").GetInt32());
                }
            }
            catch (Exception) { }
            Thread.Sleep(500 * (attempt + 1));
        }
        return null;
    }

    public void Dispose() => _http.Dispose();
}
