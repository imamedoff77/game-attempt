using Google.Apis.Auth;

namespace AccountServer;

public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name);

public interface IGoogleVerifier
{
    /// <summary>Checks a Google ID token (signature, expiry, audience). Null when it is not valid.</summary>
    Task<GoogleIdentity?> VerifyAsync(string idToken);
}

public sealed class GoogleVerifier : IGoogleVerifier
{
    private readonly AccountOptions _options;

    public GoogleVerifier(AccountOptions options) => _options = options;

    public async Task<GoogleIdentity?> VerifyAsync(string idToken)
    {
        if (_options.GoogleClientIds.Length == 0) return null; // Google sign-in is not configured
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings { Audience = _options.GoogleClientIds };
            GoogleJsonWebSignature.Payload payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            return new GoogleIdentity(payload.Subject, payload.Email ?? "", payload.EmailVerified, payload.Name);
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}
