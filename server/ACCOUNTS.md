# Account server

Accounts for the game: sign-up with e-mail and password, Google sign-in, e-mail confirmation, password reset, and a small server-side profile (campaign progress, armory, daily rewards). Plan and decisions: `ACCOUNTS_PLAN.md` at the repository root.

ASP.NET Core (.NET 10), PostgreSQL (`Npgsql`, plain SQL), MailKit for e-mail, `Google.Apis.Auth` for Google ID tokens.

## Run it

```bash
export Account__ConnectionString="Host=localhost;Username=game;Password=...;Database=accounts"
export ASPNETCORE_URLS=http://0.0.0.0:8080
dotnet run --project server/AccountServer
```

The tables are created at start-up (`Schema.cs`). Without `Account__ConnectionString` the server refuses to start.

## Settings (environment variables, `Account__` prefix)

| Name | Meaning | Default |
|---|---|---|
| `ConnectionString` | PostgreSQL connection string | required |
| `PublicBaseUrl` | public address, used in e-mail links (**https** in production) | `http://localhost:8080` |
| `GoogleClientIds__0`, `__1`, ... | OAuth client IDs whose Google ID tokens are accepted (web client and Android client) | none: Google sign-in answers 401 |
| `Smtp__Host`, `Smtp__Port`, `Smtp__User`, `Smtp__Password`, `Smtp__From`, `Smtp__FromName` | mail server. Without `Smtp__Host`, mails are written as text files into `OutboxDir` | file outbox |
| `OutboxDir` | development mail folder | `outbox` |
| `TrustProxy` | `true` when running behind Caddy/nginx: the client address is read from `X-Forwarded-For` (otherwise the rate limit sees only the proxy). Never turn it on when the server is directly reachable | `false` |
| `AuthRatePerMinute` | sign-in requests per minute and address | 30 |
| `SessionDays`, `MaxFailedLogins`, `LockMinutes`, `ResetCodeMinutes`, `ResetCodeMaxAttempts`, `PasswordIterations` | security knobs | 90, 5, 15, 30, 5, 600000 |

Secrets (database password, SMTP password) belong in environment variables or the hosting provider's secret store, never in files in the repository.

## API (JSON)

Errors look like `{"error": "code", "details": ...}`. Protected calls need `Authorization: Bearer <token>`.

| Call | Body | Answer |
|---|---|---|
| `POST /v1/auth/register` | `email`, `password` (8-128 characters), `displayName`? | 201 `{token, user}`; 400 `invalid_email` / `weak_password`; 409 `email_taken` |
| `POST /v1/auth/login` | `email`, `password` | 200 `{token, user}`; 401 `invalid_credentials`; 423 `locked` (`retryAfterSeconds`) |
| `POST /v1/auth/google` | `idToken` | 200 `{token, user, created}`; 401 `invalid_google_token` |
| `POST /v1/auth/logout` | | 204 |
| `POST /v1/auth/resend-verification` | | 204 |
| `GET /verify-email?token=` | | small HTML page (the link in the mail) |
| `POST /v1/auth/forgot-password` | `email` | always 204; mails an 8-character code |
| `POST /v1/auth/reset-password` | `email`, `code`, `newPassword` | 204; 400 `invalid_code` / `weak_password`. All sessions end |
| `GET /v1/me`, `PUT /v1/me` (`displayName`) | | user |
| `POST /v1/me/password` | `currentPassword`, `newPassword` | 204; other devices are signed out |
| `POST /v1/me/delete` | `password` (not needed for Google-only accounts) | 204; deletes account, sessions and profile (Google Play requires in-app account deletion) |
| `GET /v1/profile` | | `{revision, data}` |
| `PUT /v1/profile` | `baseRevision`, `data` (strings under `campaign`, `armory`, `daily`, `tutorial`) | 200 `{revision, data}`; 409 `revision_conflict` with the current `details.revision` / `details.data` |

Sign-in endpoints (`/v1/auth/*`) share a per-address rate limit (429).

## Setting up Google sign-in (done by the project owner)

1. Google Cloud Console: create a project, set up the OAuth consent screen.
2. Create an OAuth client of type **Web application** (its client ID is what the server checks) and one of type **Android** (package name `com.imamedoff.irondoctrine` and the SHA-1 of the signing key; debug and release keys are different, add both).
3. Put the Web client ID (and any other client IDs the app may use) into `Account__GoogleClientIds__0`, ...
4. Put the same Web client ID into the Unity client's server settings (see the client section of `ACCOUNTS_PLAN.md`, A7).

## Tests

```bash
server/tools/test-db.sh start          # private PostgreSQL on port 54329 (no root, own data folder)
dotnet test server/AccountServer.Tests # each test works in its own schema
server/tools/test-db.sh stop
```

Another database can be used with `ACCOUNTS_TEST_DB` (a connection string with the right to create schemas).

## Production checklist

- Run behind HTTPS (Caddy or nginx) and set `PublicBaseUrl` and `TrustProxy=true`.
- Real SMTP settings (and SPF/DKIM for the sending domain, or mails end up in spam).
- Regular PostgreSQL backups.
- Credits and armory levels are still stored as the phone reports them (not checked by the server): before paid items exist the server must validate missions and spending itself (Faza 5/6).

## Unity client settings (not in git)

- `unity-client/Assets/Resources/accountserver.txt`: base URL of this server (e.g. `https://accounts.example.com`).
- `unity-client/Assets/Resources/googleclient.txt`: the **Web application** OAuth client ID from Google Cloud (the same value as `Google:ClientId` on the server). The Android bridge (`Assets/Plugins/Android/GoogleSignInBridge.java`) also needs an **Android** OAuth client for package `com.imamedoff.irondoctrine` and the SHA-1 of the signing key registered in the same Google Cloud project. Without the file the Google button says it is not set up.
