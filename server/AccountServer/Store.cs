using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace AccountServer;

public enum ProductKind { Credits, Booster }

/// <summary>One thing that can be bought. Prices live in the Play Console (the ids must match there); the game shows Google's localized price.</summary>
public sealed record Product(string Id, ProductKind Kind, int Credits, string Booster, int BoosterDays);

public static class Catalog
{
    /// <summary>While this booster is active, campaign missions pay double credits (the game applies it).</summary>
    public const string DoubleCredits = "credits_x2";

    public static readonly Product[] All =
    {
        new("credits_500", ProductKind.Credits, 500, "", 0),
        new("credits_1500", ProductKind.Credits, 1500, "", 0),
        new("credits_4000", ProductKind.Credits, 4000, "", 0),
        new("booster_credits_x2_7d", ProductKind.Booster, 0, DoubleCredits, 7),
    };

    public static Product? Find(string? id) => All.FirstOrDefault(p => p.Id == id);
}

public enum PlayState { Invalid, Pending, Purchased }

public sealed record PlayPurchase(PlayState State, string OrderId);

/// <summary>Asks Google Play whether a purchase token is real, paid for and for this product.</summary>
public interface IPlayVerifier
{
    /// <summary>Null when the verifier is not set up (the store is closed).</summary>
    bool Enabled { get; }

    Task<PlayPurchase> VerifyAsync(string productId, string purchaseToken);
}

/// <summary>The store is closed: nothing can be bought.</summary>
public sealed class NoPlayVerifier : IPlayVerifier
{
    public bool Enabled => false;
    public Task<PlayPurchase> VerifyAsync(string productId, string purchaseToken) => Task.FromResult(new PlayPurchase(PlayState.Invalid, ""));
}

/// <summary>Development: tokens that start with "dev-" count as paid. Used only when <see cref="StoreOptions.AllowDevPurchases"/> is on.</summary>
public sealed class DevPlayVerifier : IPlayVerifier
{
    public bool Enabled => true;

    public Task<PlayPurchase> VerifyAsync(string productId, string purchaseToken) =>
        Task.FromResult(purchaseToken.StartsWith("dev-", StringComparison.Ordinal) ? new PlayPurchase(PlayState.Purchased, "DEV." + purchaseToken) : new PlayPurchase(PlayState.Invalid, ""));
}

/// <summary>Google Play Developer API: <c>purchases.products.get</c>, called with a service account that has access in the Play Console.</summary>
public sealed class GooglePlayVerifier : IPlayVerifier, IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ICredential _credential;
    private readonly string _package;

    public GooglePlayVerifier(StoreOptions options)
    {
        _package = options.PackageName;
        _credential = GoogleCredential.FromFile(options.ServiceAccountJsonPath).CreateScoped("https://www.googleapis.com/auth/androidpublisher").UnderlyingCredential;
    }

    public bool Enabled => true;

    public async Task<PlayPurchase> VerifyAsync(string productId, string purchaseToken)
    {
        string accessToken = await _credential.GetAccessTokenForRequestAsync();
        string url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{Uri.EscapeDataString(_package)}/purchases/products/{Uri.EscapeDataString(productId)}/tokens/{Uri.EscapeDataString(purchaseToken)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using HttpResponseMessage response = await _http.SendAsync(request);
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest) return new PlayPurchase(PlayState.Invalid, "");
        response.EnsureSuccessStatusCode(); // anything else (Google down, bad key): the caller answers "try again later"
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        int state = doc.RootElement.TryGetProperty("purchaseState", out JsonElement s) ? s.GetInt32() : -1;
        string order = doc.RootElement.TryGetProperty("orderId", out JsonElement o) ? o.GetString() ?? "" : "";
        return new PlayPurchase(state == 0 ? PlayState.Purchased : state == 2 ? PlayState.Pending : PlayState.Invalid, order);
    }

    public void Dispose() => _http.Dispose();
}
