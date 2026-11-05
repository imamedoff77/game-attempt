using System.Threading.RateLimiting;
using AccountServer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 64 * 1024); // every request here is a few hundred bytes

// Options are read lazily so that test hosts can change the configuration before they are used.
builder.Services.AddSingleton(sp => sp.GetRequiredService<IConfiguration>().GetSection("Account").Get<AccountOptions>() ?? new AccountOptions());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AccountStore>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<IGoogleVerifier, GoogleVerifier>();
builder.Services.AddSingleton<IEmailSender>(sp =>
{
    var options = sp.GetRequiredService<AccountOptions>();
    return string.IsNullOrEmpty(options.Smtp.Host) ? new FileEmailSender(options) : new SmtpEmailSender(options);
});
builder.Services.AddSingleton<IConfigureOptions<RateLimiterOptions>, ConfigureRateLimiter>();
builder.Services.AddRateLimiter();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); // only used when Account:TrustProxy is on, i.e. the server is reachable through the proxy only
    o.KnownProxies.Clear();
});

WebApplication app = builder.Build();
if (app.Services.GetRequiredService<AccountOptions>().TrustProxy) app.UseForwardedHeaders();
app.UseRateLimiter();
await app.Services.GetRequiredService<AccountStore>().InitAsync();
app.MapGet("/", () => "Account server");
app.MapAccountEndpoints();
app.Run();

public partial class Program;

internal sealed class ConfigureRateLimiter : IConfigureOptions<RateLimiterOptions>
{
    private readonly AccountOptions _options;

    public ConfigureRateLimiter(AccountOptions options) => _options = options;

    public void Configure(RateLimiterOptions limiter)
    {
        limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        limiter.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = _options.AuthRatePerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    }
}
