using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// ---- the JSON shapes of the account server (see server/ACCOUNTS.md); JsonUtility needs plain serializable classes ----

[Serializable] public sealed class AccountUser { public long id; public string email; public string displayName; public bool emailVerified; public bool hasPassword; public bool hasGoogle; }
[Serializable] public sealed class ProfileDto { public string campaign; public string armory; public string daily; public string tutorial; public string stats; }
[Serializable] public sealed class SessionBody { public string token; public AccountUser user; public bool created; }
[Serializable] public sealed class ProfileBody { public long revision; public ProfileDto data; }
[Serializable] public sealed class ErrorDetails { public int retryAfterSeconds; public long revision; public ProfileDto data; }
[Serializable] public sealed class ErrorBody { public string error; public ErrorDetails details; }

[Serializable] public sealed class RegisterRequest { public string email; public string password; public string displayName; }
[Serializable] public sealed class LoginRequest { public string email; public string password; }
[Serializable] public sealed class GoogleRequest { public string idToken; }
[Serializable] public sealed class ForgotRequest { public string email; }
[Serializable] public sealed class ResetRequest { public string email; public string code; public string newPassword; }
[Serializable] public sealed class DeleteRequest { public string password; }
[Serializable] public sealed class ProfilePut { public long baseRevision; public ProfileDto data; }

/// <summary>The answer of one call: HTTP status (0 when the server could not be reached), body text and the error code the server gave.</summary>
public sealed class ApiResult
{
    public long Status;
    public string Body = "";
    public string Error = "";
    public ErrorDetails Details;

    public bool Ok => Status >= 200 && Status < 300;
    public bool NetworkError => Status == 0;

    public T Read<T>() where T : class
    {
        try { return JsonUtility.FromJson<T>(Body); }
        catch (Exception) { return null; }
    }
}

/// <summary>
/// Talks to the account server: sign-up, sign-in (password or Google), password reset, account deletion and the profile.
/// Keeps the session token between runs. Every call is asynchronous; the callback runs on the main thread.
/// </summary>
public sealed class AccountService : MonoBehaviour
{
    private const string TokenKey = "account.token";
    private const string UserKey = "account.user";

    public string BaseUrl { get; private set; }
    public string Token { get; private set; }
    public AccountUser User { get; private set; }
    public bool SignedIn => !string.IsNullOrEmpty(Token);

    /// <summary>Raised when the signed-in state or the user's details change.</summary>
    public event Action Changed;

    private void Update() => global::GoogleSignIn.Pump();

    private void Awake()
    {
        BaseUrl = AccountSettings.BaseUrl;
        try
        {
            Token = PlayerPrefs.GetString(TokenKey, "");
            string user = PlayerPrefs.GetString(UserKey, "");
            if (!string.IsNullOrEmpty(user)) User = JsonUtility.FromJson<AccountUser>(user);
        }
        catch (Exception) { }
        if (!SignedIn) User = null;
    }

    private void Store()
    {
        try
        {
            PlayerPrefs.SetString(TokenKey, Token ?? "");
            PlayerPrefs.SetString(UserKey, User != null ? JsonUtility.ToJson(User) : "");
            PlayerPrefs.Save();
        }
        catch (Exception) { }
    }

    /// <summary>Forgets the session on this phone (the server session is ended by <see cref="Logout"/>).</summary>
    public void ForgetSession()
    {
        Token = null;
        User = null;
        Store();
        Changed?.Invoke();
    }

    private void StartSession(SessionBody body)
    {
        Token = body.token;
        User = body.user;
        Store();
        Changed?.Invoke();
    }

    // ---- calls ----

    public void Register(string email, string password, string name, Action<ApiResult> done) =>
        Send("POST", "/v1/auth/register", new RegisterRequest { email = email, password = password, displayName = name }, WithSession(done));

    public void Login(string email, string password, Action<ApiResult> done) =>
        Send("POST", "/v1/auth/login", new LoginRequest { email = email, password = password }, WithSession(done));

    public void GoogleSignIn(string idToken, Action<ApiResult> done) =>
        Send("POST", "/v1/auth/google", new GoogleRequest { idToken = idToken }, WithSession(done));

    private Action<ApiResult> WithSession(Action<ApiResult> done) => r =>
    {
        if (r.Ok)
        {
            SessionBody body = r.Read<SessionBody>();
            if (body != null && !string.IsNullOrEmpty(body.token)) StartSession(body);
        }
        done?.Invoke(r);
    };

    public void Logout(Action<ApiResult> done)
    {
        Send("POST", "/v1/auth/logout", null, r => { ForgetSession(); done?.Invoke(r); });
    }

    public void ForgotPassword(string email, Action<ApiResult> done) => Send("POST", "/v1/auth/forgot-password", new ForgotRequest { email = email }, done);

    public void ResetPassword(string email, string code, string newPassword, Action<ApiResult> done) =>
        Send("POST", "/v1/auth/reset-password", new ResetRequest { email = email, code = code, newPassword = newPassword }, done);

    public void ResendVerification(Action<ApiResult> done) => Send("POST", "/v1/auth/resend-verification", null, done);

    public void DeleteAccount(string password, Action<ApiResult> done) =>
        Send("POST", "/v1/me/delete", new DeleteRequest { password = password }, r => { if (r.Ok) ForgetSession(); done?.Invoke(r); });

    /// <summary>Refreshes the user's details (for example whether the e-mail address is confirmed now).</summary>
    public void RefreshMe(Action<ApiResult> done = null) => Send("GET", "/v1/me", null, r =>
    {
        if (r.Ok)
        {
            AccountUser user = r.Read<AccountUser>();
            if (user != null) { User = user; Store(); Changed?.Invoke(); }
        }
        done?.Invoke(r);
    });

    public void GetProfile(Action<ApiResult> done) => Send("GET", "/v1/profile", null, done);

    public void PutProfile(long baseRevision, ProfileDto data, Action<ApiResult> done) =>
        Send("PUT", "/v1/profile", new ProfilePut { baseRevision = baseRevision, data = data }, done);

    // ---- transport ----

    private void Send(string method, string path, object body, Action<ApiResult> done) => StartCoroutine(Run(method, path, body, done));

    private IEnumerator Run(string method, string path, object body, Action<ApiResult> done)
    {
        bool hadSession = SignedIn;
        using (var request = new UnityWebRequest(BaseUrl.TrimEnd('/') + path, method))
        {
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.downloadHandler = new DownloadHandlerBuffer();
            if (SignedIn) request.SetRequestHeader("Authorization", "Bearer " + Token);
            request.timeout = 15;
            yield return request.SendWebRequest();

            var result = new ApiResult { Status = request.result == UnityWebRequest.Result.ConnectionError ? 0 : request.responseCode, Body = request.downloadHandler.text ?? "" };
            if (!result.Ok && result.Status != 0)
            {
                ErrorBody error = result.Read<ErrorBody>();
                if (error != null) { result.Error = error.error ?? ""; result.Details = error.details; }
            }
            // The server no longer knows this session (expired, password changed elsewhere): sign out here too.
            if (result.Status == 401 && hadSession && result.Error == "unauthorized") ForgetSession();
            done?.Invoke(result);
        }
    }
}

/// <summary>Where the account server is. Development builds can carry the developer computer's address (Resources/accountserver.txt, not in git).</summary>
public static class AccountSettings
{
    // Replaced by the real address when the server is hosted (https is required for release builds).
    private const string ReleaseUrl = "https://accounts.invalid";

    public static string BaseUrl
    {
        get
        {
            TextAsset preset = Resources.Load<TextAsset>("accountserver");
            string text = preset != null ? preset.text.Trim() : "";
            return text.Length > 0 ? text : ReleaseUrl;
        }
    }
}
