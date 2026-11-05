namespace AccountServer;

/// <summary>Settings of the account server. Read from the "Account" configuration section (environment: Account__ConnectionString, ...).</summary>
public sealed class AccountOptions
{
    /// <summary>PostgreSQL connection string (required).</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>The address players' browsers reach this server at (used in e-mail links). Must be https in production.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>OAuth client IDs (Google Cloud) whose ID tokens are accepted: the web/server client and the Android client.</summary>
    public string[] GoogleClientIds { get; set; } = Array.Empty<string>();

    /// <summary>When the server runs behind a reverse proxy (Caddy, nginx) the client address comes from X-Forwarded-For.</summary>
    public bool TrustProxy { get; set; }

    /// <summary>Requests per minute and address for the sign-in endpoints.</summary>
    public int AuthRatePerMinute { get; set; } = 30;

    /// <summary>
    /// Shared secret of the game (relay) server: it may ask who a session token belongs to (<c>POST /v1/internal/verify-session</c>).
    /// Empty = that endpoint does not exist. Use a long random value and keep it out of the apps.
    /// </summary>
    public string InternalKey { get; set; } = "";

    public int SessionDays { get; set; } = 90;
    public int PasswordIterations { get; set; } = 600_000;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockMinutes { get; set; } = 15;
    public int ResetCodeMinutes { get; set; } = 30;
    public int ResetCodeMaxAttempts { get; set; } = 5;

    /// <summary>Without SMTP settings, e-mails are written as text files into this folder (development).</summary>
    public string OutboxDir { get; set; } = "outbox";

    public SmtpOptions Smtp { get; set; } = new();

    public StoreOptions Store { get; set; } = new();
}

/// <summary>In-app purchases (see server/ACCOUNTS.md, "Store").</summary>
public sealed class StoreOptions
{
    /// <summary>The app's package name in Google Play (for example com.imamedoff.irondoctrine).</summary>
    public string PackageName { get; set; } = "";

    /// <summary>Path of the service account key (JSON) that may read purchases in the Play Console. Empty = purchases are not checked against Google.</summary>
    public string ServiceAccountJsonPath { get; set; } = "";

    /// <summary>Development only: accept purchase tokens that start with "dev-" without asking Google, so the whole flow can be tried without Play. Never in production.</summary>
    public bool AllowDevPurchases { get; set; }
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public string FromName { get; set; } = "Iron Doctrine";
}
