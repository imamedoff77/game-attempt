using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AccountServer;

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName);
public sealed record LoginRequest(string? Email, string? Password);
public sealed record GoogleRequest(string? IdToken);
public sealed record ForgotRequest(string? Email);
public sealed record ResetRequest(string? Email, string? Code, string? NewPassword);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record DeleteAccountRequest(string? Password);
public sealed record UpdateMeRequest(string? DisplayName);
public sealed record ProfileUpdateRequest(long BaseRevision, JsonObject? Data);

public static class Endpoints
{
    public const int MinPassword = 8, MaxPassword = 128, MaxDisplayName = 24;
    private static readonly string[] ProfileKeys = { "campaign", "armory", "daily", "tutorial", "stats" };
    private const int MaxProfileValueLength = 512;

    private static IResult Error(int status, string code, object? extra = null) => Results.Json(new { error = code, details = extra }, statusCode: status);

    private static object UserDto(User u) => new { id = u.Id, email = u.Email, displayName = u.DisplayName, emailVerified = u.EmailVerified, hasPassword = u.PasswordHash != null, hasGoogle = u.GoogleSub != null };

    public static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        string trimmed = email.Trim();
        if (trimmed.Length > 254) return null;
        try
        {
            var address = new MailAddress(trimmed);
            if (address.Address != trimmed || !address.Host.Contains('.')) return null;
        }
        catch (FormatException) { return null; }
        return trimmed.ToLowerInvariant();
    }

    public static bool IsValidPassword(string? password, string emailNorm) =>
        password != null && password.Length >= MinPassword && password.Length <= MaxPassword && !string.Equals(password, emailNorm, StringComparison.OrdinalIgnoreCase);

    private static string CleanName(string? name, string fallback)
    {
        string cleaned = new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = fallback;
        return cleaned.Length > MaxDisplayName ? cleaned[..MaxDisplayName] : cleaned;
    }

    private static string? BearerToken(HttpContext http)
    {
        string header = http.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }

    private static async Task<User?> Authenticate(HttpContext http, AccountStore store, TimeProvider clock)
    {
        string? token = BearerToken(http);
        return string.IsNullOrEmpty(token) ? null : await store.FindSessionUser(Tokens.Hash(token), clock.GetUtcNow());
    }

    private sealed record Session(string Token, User User);

    private static async Task<Session> StartSession(User user, AccountStore store, AccountOptions options, TimeProvider clock)
    {
        string token = Tokens.New();
        DateTimeOffset now = clock.GetUtcNow();
        await store.CreateSession(Tokens.Hash(token), user.Id, now, now.AddDays(options.SessionDays));
        return new Session(token, user);
    }

    private static async Task SendVerification(User user, AccountStore store, IEmailSender mail, AccountOptions options, TimeProvider clock, ILogger log)
    {
        try
        {
            string token = Tokens.New();
            await store.CreateEmailToken(user.Id, "verify", Tokens.Hash(token), clock.GetUtcNow().AddDays(3));
            (string subject, string body) = EmailTemplates.Verification($"{options.PublicBaseUrl.TrimEnd('/')}/verify-email?token={token}");
            await mail.SendAsync(user.Email, subject, body);
        }
        catch (Exception e)
        {
            // A mail problem must not stop the player from playing; they can ask for the mail again.
            log.LogWarning(e, "Could not send the verification e-mail to user {UserId}", user.Id);
        }
    }

    public static void MapAccountEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/v1/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (RegisterRequest? req, AccountStore store, PasswordHasher hasher, IEmailSender mail, AccountOptions options, TimeProvider clock, ILoggerFactory logs) =>
        {
            string? email = NormalizeEmail(req?.Email);
            if (email == null) return Error(400, "invalid_email");
            if (!IsValidPassword(req?.Password, email)) return Error(400, "weak_password", new { min = MinPassword, max = MaxPassword });
            User? user = await store.CreateUser(req!.Email!.Trim(), email, hasher.Hash(req.Password!), false, CleanName(req.DisplayName, email.Split('@')[0]), null, clock.GetUtcNow());
            if (user == null) return Error(409, "email_taken");
            await SendVerification(user, store, mail, options, clock, logs.CreateLogger("Register"));
            Session session = await StartSession(user, store, options, clock);
            return Results.Json(new { token = session.Token, user = UserDto(user) }, statusCode: 201);
        });

        auth.MapPost("/login", async (LoginRequest? req, AccountStore store, PasswordHasher hasher, AccountOptions options, TimeProvider clock) =>
        {
            string? email = NormalizeEmail(req?.Email);
            User? user = email == null ? null : await store.GetByEmail(email);
            if (user?.PasswordHash == null || req?.Password == null)
            {
                hasher.BurnTime();
                return Error(401, "invalid_credentials");
            }
            DateTimeOffset now = clock.GetUtcNow();
            if (user.LockedUntil > now) return Error(423, "locked", new { retryAfterSeconds = (int)Math.Ceiling((user.LockedUntil.Value - now).TotalSeconds) });
            if (!hasher.Verify(req.Password, user.PasswordHash))
            {
                await store.RegisterFailedLogin(user.Id, now, options.MaxFailedLogins, TimeSpan.FromMinutes(options.LockMinutes));
                return Error(401, "invalid_credentials");
            }
            await store.ClearFailedLogins(user.Id);
            Session session = await StartSession(user, store, options, clock);
            return Results.Ok(new { token = session.Token, user = UserDto(user) });
        });

        auth.MapPost("/google", async (GoogleRequest? req, AccountStore store, IGoogleVerifier google, AccountOptions options, TimeProvider clock) =>
        {
            if (string.IsNullOrWhiteSpace(req?.IdToken)) return Error(400, "missing_token");
            GoogleIdentity? identity = await google.VerifyAsync(req.IdToken);
            string? email = NormalizeEmail(identity?.Email);
            if (identity == null || !identity.EmailVerified || email == null) return Error(401, "invalid_google_token");

            bool created = false;
            User? user = await store.GetByGoogleSub(identity.Subject);
            if (user == null)
            {
                user = await store.GetByEmail(email);
                if (user != null)
                {
                    // An account with this address exists. If its owner never confirmed the address, someone else may have made it:
                    // drop its password so only the real owner (Google) can use the account.
                    if (user.GoogleSub != null) return Error(401, "invalid_google_token");
                    await store.LinkGoogle(user.Id, identity.Subject, clearPassword: !user.EmailVerified);
                    user = await store.GetById(user.Id);
                }
                else
                {
                    user = await store.CreateUser(identity.Email.Trim(), email, null, true, CleanName(identity.Name, email.Split('@')[0]), identity.Subject, clock.GetUtcNow());
                    created = user != null;
                    user ??= await store.GetByEmail(email); // lost a race with a parallel sign-in
                }
            }
            if (user == null) return Error(401, "invalid_google_token");
            Session session = await StartSession(user, store, options, clock);
            return Results.Ok(new { token = session.Token, user = UserDto(user), created });
        });

        auth.MapPost("/logout", async (HttpContext http, AccountStore store) =>
        {
            string? token = BearerToken(http);
            if (!string.IsNullOrEmpty(token)) await store.DeleteSession(Tokens.Hash(token));
            return Results.NoContent();
        });

        auth.MapPost("/resend-verification", async (HttpContext http, AccountStore store, IEmailSender mail, AccountOptions options, TimeProvider clock, ILoggerFactory logs) =>
        {
            User? user = await Authenticate(http, store, clock);
            if (user == null) return Error(401, "unauthorized");
            if (!user.EmailVerified) await SendVerification(user, store, mail, options, clock, logs.CreateLogger("Verify"));
            return Results.NoContent();
        });

        auth.MapPost("/forgot-password", async (ForgotRequest? req, AccountStore store, IEmailSender mail, AccountOptions options, TimeProvider clock, ILoggerFactory logs) =>
        {
            // Always the same answer, so the endpoint does not tell whether an address is registered.
            string? email = NormalizeEmail(req?.Email);
            User? user = email == null ? null : await store.GetByEmail(email);
            if (user == null) return Results.NoContent();
            try
            {
                string code = Tokens.NewCode();
                await store.CreateEmailToken(user.Id, "reset", Tokens.Hash(code), clock.GetUtcNow().AddMinutes(options.ResetCodeMinutes));
                (string subject, string body) = EmailTemplates.ResetCode(code, options.ResetCodeMinutes);
                await mail.SendAsync(user.Email, subject, body);
            }
            catch (Exception e)
            {
                logs.CreateLogger("Reset").LogWarning(e, "Could not send the reset code to user {UserId}", user.Id);
            }
            return Results.NoContent();
        });

        auth.MapPost("/reset-password", async (ResetRequest? req, AccountStore store, PasswordHasher hasher, AccountOptions options, TimeProvider clock) =>
        {
            string? email = NormalizeEmail(req?.Email);
            User? user = email == null ? null : await store.GetByEmail(email);
            if (user == null || string.IsNullOrWhiteSpace(req?.Code)) return Error(400, "invalid_code");
            if (!IsValidPassword(req.NewPassword, email!)) return Error(400, "weak_password", new { min = MinPassword, max = MaxPassword });
            ResetResult result = await store.TryConsumeResetCode(user.Id, Tokens.Hash(Tokens.NormalizeCode(req.Code)), clock.GetUtcNow(), options.ResetCodeMaxAttempts);
            if (result != ResetResult.Ok) return Error(400, "invalid_code");
            await store.SetPassword(user.Id, hasher.Hash(req.NewPassword!));
            await store.MarkVerified(user.Id); // they just proved they can read mail sent to this address
            await store.DeleteSessions(user.Id);
            return Results.NoContent();
        });

        app.MapGet("/verify-email", async (string? token, AccountStore store, TimeProvider clock) =>
        {
            long? userId = string.IsNullOrEmpty(token) ? null : await store.ConsumeLinkToken("verify", Tokens.Hash(token), clock.GetUtcNow());
            if (userId != null) await store.MarkVerified(userId.Value);
            string message = userId != null ? "Your e-mail address is confirmed. You can go back to the game." : "This link is not valid any more. Ask the game for a new confirmation e-mail.";
            return Results.Content($"<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>E-mail confirmation</title></head><body style=\"font-family:sans-serif;margin:2em\"><p>{message}</p></body></html>", "text/html; charset=utf-8", statusCode: userId != null ? 200 : 400);
        });

        var api = app.MapGroup("/v1");

        api.MapGet("/me", async (HttpContext http, AccountStore store, TimeProvider clock) =>
        {
            User? user = await Authenticate(http, store, clock);
            return user == null ? Error(401, "unauthorized") : Results.Ok(UserDto(user));
        });

        api.MapPut("/me", async (UpdateMeRequest? req, HttpContext http, AccountStore store, TimeProvider clock) =>
        {
            User? user = await Authenticate(http, store, clock);
            if (user == null) return Error(401, "unauthorized");
            if (string.IsNullOrWhiteSpace(req?.DisplayName)) return Error(400, "invalid_name");
            await store.SetDisplayName(user.Id, CleanName(req.DisplayName, user.DisplayName));
            return Results.Ok(UserDto((await store.GetById(user.Id))!));
        });

        // Google Play requires that a player can delete the account from inside the app.
        api.MapPost("/me/delete", async (DeleteAccountRequest? req, HttpContext http, AccountStore store, PasswordHasher hasher, TimeProvider clock) =>
        {
            User? user = await Authenticate(http, store, clock);
            if (user == null) return Error(401, "unauthorized");
            if (user.PasswordHash != null && (req?.Password == null || !hasher.Verify(req.Password, user.PasswordHash))) return Error(403, "wrong_password");
            await store.DeleteUser(user.Id);
            return Results.NoContent();
        });

        api.MapPost("/me/password", async (ChangePasswordRequest? req, HttpContext http, AccountStore store, PasswordHasher hasher, TimeProvider clock) =>
        {
            User? user = await Authenticate(http, store, clock);
            if (user == null) return Error(401, "unauthorized");
            if (user.PasswordHash != null && (req?.CurrentPassword == null || !hasher.Verify(req.CurrentPassword, user.PasswordHash))) return Error(403, "wrong_password");
            if (!IsValidPassword(req?.NewPassword, NormalizeEmail(user.Email) ?? user.Email)) return Error(400, "weak_password", new { min = MinPassword, max = MaxPassword });
            await store.SetPassword(user.Id, hasher.Hash(req!.NewPassword!));
            await store.DeleteSessions(user.Id, except: Tokens.Hash(BearerToken(http)!)); // other devices must sign in again
            return Results.NoContent();
        });

        api.MapGet("/profile", async (HttpContext http, AccountStore store, TimeProvider clock) =>
        {
            User? user = await Authenticate(http, store, clock);
            if (user == null) return Error(401, "unauthorized");
            (long revision, string json) = await store.GetProfile(user.Id);
            return Results.Ok(new { revision, data = JsonNode.Parse(json) });
        });

        api.MapPut("/profile", async (ProfileUpdateRequest? req, HttpContext http, AccountStore store, TimeProvider clock) =>
        {
            User? user = await Authenticate(http, store, clock);
            if (user == null) return Error(401, "unauthorized");
            if (req?.Data == null) return Error(400, "invalid_profile");
            var clean = new JsonObject();
            foreach ((string key, JsonNode? value) in req.Data)
            {
                if (Array.IndexOf(ProfileKeys, key) < 0 || value is not JsonValue v || !v.TryGetValue(out string? text) || text.Length > MaxProfileValueLength)
                    return Error(400, "invalid_profile", new { key });
                clean[key] = text;
            }
            (bool ok, long revision, string json) = await store.UpdateProfile(user.Id, req.BaseRevision, clean.ToJsonString(), clock.GetUtcNow());
            return ok
                ? Results.Ok(new { revision, data = JsonNode.Parse(json) })
                : Error(409, "revision_conflict", new { revision, data = JsonNode.Parse(json) });
        });
    }
}
