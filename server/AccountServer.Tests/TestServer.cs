using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AccountServer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace AccountServer.Tests;

public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public sealed record Mail(string To, string Subject, string Body);

public sealed class FakeMail : IEmailSender
{
    public List<Mail> Sent { get; } = new();
    public bool Fail { get; set; }

    public Task SendAsync(string to, string subject, string body)
    {
        if (Fail) throw new InvalidOperationException("mail server down");
        lock (Sent) Sent.Add(new Mail(to, subject, body));
        return Task.CompletedTask;
    }

    public Mail Last => Sent[^1];
    public string LastLink => Regex.Match(Last.Body, @"https?://\S+").Value;
    public string LastCode => Regex.Match(Last.Body, @"\n\n([A-Z2-9]{8})\n\n").Groups[1].Value;
}

public sealed class FakeGoogle : IGoogleVerifier
{
    private readonly Dictionary<string, GoogleIdentity> _tokens = new();

    /// <summary>Makes a token the fake accepts and returns it.</summary>
    public string Issue(string subject, string email, bool emailVerified = true, string? name = "Gamer")
    {
        string token = "google-" + subject + "-" + _tokens.Count;
        _tokens[token] = new GoogleIdentity(subject, email, emailVerified, name);
        return token;
    }

    public Task<GoogleIdentity?> VerifyAsync(string idToken) => Task.FromResult(_tokens.TryGetValue(idToken, out GoogleIdentity? i) ? i : null);
}

/// <summary>One account server on a PostgreSQL schema of its own, with a fake mailbox, fake Google and a clock the test controls.</summary>
public sealed class TestServer : IAsyncDisposable
{
    public static string AdminConnection => Environment.GetEnvironmentVariable("ACCOUNTS_TEST_DB")
        ?? "Host=127.0.0.1;Port=54329;Username=game;Password=game;Database=postgres;Include Error Detail=true";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _schema;
    public HttpClient Client { get; }
    public FakeMail Mail { get; }
    public FakeGoogle Google { get; }
    public ManualClock Clock { get; }

    private TestServer(WebApplicationFactory<Program> factory, string schema, FakeMail mail, FakeGoogle google, ManualClock clock)
    {
        _factory = factory;
        _schema = schema;
        Mail = mail;
        Google = google;
        Clock = clock;
        Client = factory.CreateClient();
    }

    public static async Task<TestServer> StartAsync(Action<Dictionary<string, string?>>? settings = null)
    {
        string schema = "t_" + Guid.NewGuid().ToString("N");
        await using (var admin = NpgsqlDataSource.Create(AdminConnection))
        {
            await using NpgsqlCommand create = admin.CreateCommand($"create schema \"{schema}\"");
            await create.ExecuteNonQueryAsync();
        }

        var config = new Dictionary<string, string?>
        {
            ["Account:ConnectionString"] = AdminConnection + ";Search Path=" + schema,
            ["Account:PasswordIterations"] = "1000",
            ["Account:AuthRatePerMinute"] = "10000",
            ["Account:PublicBaseUrl"] = "https://game.example",
            ["Account:OutboxDir"] = Path.Combine(Path.GetTempPath(), "account-tests-outbox"),
        };
        settings?.Invoke(config);

        var mail = new FakeMail();
        var google = new FakeGoogle();
        var clock = new ManualClock();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            foreach ((string key, string? value) in config) b.UseSetting(key, value);
            b.ConfigureServices(services =>
            {
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(mail);
                services.RemoveAll<IGoogleVerifier>();
                services.AddSingleton<IGoogleVerifier>(google);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
            });
        });
        return new TestServer(factory, schema, mail, google, clock);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        await using var admin = NpgsqlDataSource.Create(AdminConnection);
        await using NpgsqlCommand drop = admin.CreateCommand($"drop schema \"{_schema}\" cascade");
        await drop.ExecuteNonQueryAsync();
    }

    // ---- helpers ----

    public Task<HttpResponseMessage> Post(string url, object? body = null, string? token = null) => Send(HttpMethod.Post, url, body, token);
    public Task<HttpResponseMessage> Put(string url, object? body, string? token = null) => Send(HttpMethod.Put, url, body, token);
    public Task<HttpResponseMessage> Get(string url, string? token = null) => Send(HttpMethod.Get, url, null, token);

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body, string? token)
    {
        var request = new HttpRequestMessage(method, url);
        if (body != null) request.Content = JsonContent.Create(body);
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Client.SendAsync(request);
    }

    public static async Task<JsonElement> Json(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Registers an account and returns its session token.</summary>
    public async Task<string> Register(string email = "player@example.com", string password = "correct horse", string? name = null)
    {
        HttpResponseMessage r = await Post("/v1/auth/register", new { email, password, displayName = name });
        Assert.Equal(System.Net.HttpStatusCode.Created, r.StatusCode);
        return (await Json(r)).GetProperty("token").GetString()!;
    }

    public async Task<string?> Login(string email, string password)
    {
        HttpResponseMessage r = await Post("/v1/auth/login", new { email, password });
        return r.IsSuccessStatusCode ? (await Json(r)).GetProperty("token").GetString() : null;
    }
}
