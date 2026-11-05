namespace AccountServer;

public sealed record PurchaseRequest(string? ProductId, string? PurchaseToken);
public sealed record AckRequest(long[]? Ids);

/// <summary>
/// In-app purchases. The phone buys through Google Play, then sends the purchase token here; the server asks Google whether it is real and paid,
/// records it once, and either lists credits for the game to collect (<c>pending</c>) or starts a booster. Collected credits are acknowledged.
/// </summary>
public static class StoreEndpoints
{
    private static object Dto(PurchaseRecord p) => new { id = p.Id, productId = p.ProductId, credits = p.Credits };

    private static async Task<object> State(AccountStore store, long userId, TimeProvider clock, bool alreadyProcessed = false) => new
    {
        alreadyProcessed,
        pending = (await store.PendingGrants(userId)).Select(Dto),
        boosters = (await store.ActiveBoosters(userId, clock.GetUtcNow())).Select(b => new { kind = b.Kind, expiresAt = b.ExpiresAt }),
    };

    public static void MapStoreEndpoints(this WebApplication app)
    {
        var store = app.MapGroup("/v1/store");

        // What can be bought (ids must match the products in the Play Console).
        store.MapGet("/products", () => Results.Ok(Catalog.All.Select(p => new
        {
            id = p.Id, kind = p.Kind.ToString().ToLowerInvariant(), credits = p.Credits, booster = p.Booster, boosterDays = p.BoosterDays,
        })));

        store.MapGet("/state", async (HttpContext http, AccountStore db, TimeProvider clock) =>
        {
            User? user = await Endpoints.Authenticate(http, db, clock);
            return user == null ? Endpoints.Error(401, "unauthorized") : Results.Ok(await State(db, user.Id, clock));
        });

        store.MapPost("/purchase", async (PurchaseRequest? req, HttpContext http, AccountStore db, IPlayVerifier play, TimeProvider clock, ILoggerFactory logs) =>
        {
            User? user = await Endpoints.Authenticate(http, db, clock);
            if (user == null) return Endpoints.Error(401, "unauthorized");
            if (!play.Enabled) return Endpoints.Error(503, "store_unavailable");
            Product? product = Catalog.Find(req?.ProductId);
            if (product == null) return Endpoints.Error(400, "unknown_product");
            string? token = req!.PurchaseToken;
            if (string.IsNullOrEmpty(token) || token.Length > 1024) return Endpoints.Error(400, "invalid_token");

            byte[] hash = Tokens.Hash(token);
            PurchaseRecord? known = await db.FindPurchase(hash);
            if (known != null)
                return known.UserId == user.Id ? Results.Ok(await State(db, user.Id, clock, alreadyProcessed: true)) : Endpoints.Error(409, "token_used");

            PlayPurchase purchase;
            try { purchase = await play.VerifyAsync(product.Id, token); }
            catch (Exception e)
            {
                logs.CreateLogger("Store").LogWarning(e, "Could not ask Google about a purchase of user {UserId}", user.Id);
                return Endpoints.Error(503, "store_unavailable"); // the phone keeps the purchase and tries again later
            }
            if (purchase.State == PlayState.Pending) return Endpoints.Error(202, "purchase_pending");
            if (purchase.State != PlayState.Purchased) return Endpoints.Error(400, "purchase_not_valid");

            PurchaseRecord? added = await db.AddPurchase(user.Id, product, hash, purchase.OrderId, clock.GetUtcNow());
            if (added == null) // a parallel request recorded the same token a moment ago
            {
                PurchaseRecord? other = await db.FindPurchase(hash);
                if (other == null || other.UserId != user.Id) return Endpoints.Error(409, "token_used");
            }
            return Results.Ok(await State(db, user.Id, clock, alreadyProcessed: added == null));
        });

        store.MapPost("/ack", async (AckRequest? req, HttpContext http, AccountStore db, TimeProvider clock) =>
        {
            User? user = await Endpoints.Authenticate(http, db, clock);
            if (user == null) return Endpoints.Error(401, "unauthorized");
            if (req?.Ids == null || req.Ids.Length > 100) return Endpoints.Error(400, "invalid_ids");
            await db.AckGrants(user.Id, req.Ids);
            return Results.Ok(await State(db, user.Id, clock));
        });
    }
}
