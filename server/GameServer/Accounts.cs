using System.Net.Http.Json;
using System.Text.Json;
using SimCore;

namespace GameServer;

/// <summary>Who a session token belongs to, as the account server says it.</summary>
public sealed record VerifiedPlayer(long UserId, string DisplayName, byte[] Kit);

/// <summary>Asks the account server about a token. Blocking on purpose: it runs on the player's own message task.</summary>
public interface IAccountVerifier
{
    /// <summary>Null when the token is unknown, expired, or the account server could not be asked.</summary>
    VerifiedPlayer? Verify(string token);
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
                Armory.Parse(root.TryGetProperty("armory", out JsonElement armory) ? armory.GetString() : "").ToBytes());
        }
        catch (Exception)
        {
            return null; // account server down: nobody gets in while the server cannot tell who they are
        }
    }

    public void Dispose() => _http.Dispose();
}
