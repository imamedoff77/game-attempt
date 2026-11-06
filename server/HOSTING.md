# Hosting the servers (one small VPS is enough to start)

Three things run: **PostgreSQL**, the **account server** (HTTPS, port 443 through a reverse proxy) and the **game server** (plain TCP, port 7777). Both servers are .NET 10 programs. A VPS with 2 vCPU / 2 GB RAM (5–10 USD per month, Linux) carries hundreds of players; a match costs the relay almost nothing (it forwards a few small messages per second).

Needs from you: a VPS, a domain name (for example `accounts.yourgame.com` and `play.yourgame.com`, both pointing to the VPS), and an e-mail sending service (SMTP) for confirmation and reset mails. Everything below is written so Claude can do it for you over SSH once you give access.

## 1. Build

On the developer machine:

```bash
dotnet publish server/AccountServer -c Release -o /tmp/publish/account
dotnet publish server/GameServer -c Release -o /tmp/publish/game
```

`gamedata.json` is copied next to `GameServer` automatically (the relay needs it to replay ranked matches). Copy both folders to the VPS (for example `/opt/game/account` and `/opt/game/game`). Install the .NET 10 ASP.NET runtime on the VPS (or publish with `--self-contained`).

## 2. PostgreSQL

Install PostgreSQL 16+, create a database and a user with a long random password, listen on localhost only. The tables are created by the account server at start-up. **Back up every day**: `pg_dump accounts | gzip > /backup/accounts-$(date +%F).sql.gz` from cron, and copy the files off the machine (provider snapshots are not enough on their own).

## 3. Account server (systemd)

`/etc/systemd/system/account.service`:

```ini
[Unit]
Description=Account server
After=network.target postgresql.service

[Service]
WorkingDirectory=/opt/game/account
ExecStart=/opt/game/account/AccountServer
Restart=always
User=game
EnvironmentFile=/etc/game/account.env

[Install]
WantedBy=multi-user.target
```

`/etc/game/account.env` (readable only by root and the service user, never in git):

```
ASPNETCORE_URLS=http://127.0.0.1:8080
Account__ConnectionString=Host=localhost;Username=game;Password=...;Database=accounts
Account__PublicBaseUrl=https://accounts.yourgame.com
Account__InternalKey=<long random secret>
Account__GoogleClientIds__0=<web client id>
Account__Store__PackageName=com.imamedoff.irondoctrine
Account__Store__ServiceAccountJsonPath=/etc/game/play-service-account.json
# SMTP settings: see ACCOUNTS.md
```

Do **not** set `Account__Store__AllowDevPurchases` or `Account__OutboxDir` on a public server. All settings are explained in `ACCOUNTS.md` and `../STORE_SETUP.md`.

## 4. HTTPS (Caddy, automatic certificates)

`/etc/caddy/Caddyfile`:

```
accounts.yourgame.com {
    reverse_proxy 127.0.0.1:8080
}
```

Caddy gets and renews the certificate itself. The account server must only be reachable through Caddy (it listens on 127.0.0.1). The game reads the server's clock from the `Date` header of these answers (daily rewards, boosters).

## 5. Game server (systemd)

```ini
[Unit]
Description=Game relay server
After=network.target account.service

[Service]
WorkingDirectory=/opt/game/game
ExecStart=/opt/game/game/GameServer 7777 /var/log/game
Restart=always
User=game
Environment=GAME_ACCOUNT_URL=http://127.0.0.1:8080
EnvironmentFile=/etc/game/game.env

[Install]
WantedBy=multi-user.target
```

`/etc/game/game.env` holds `GAME_ACCOUNT_KEY=<the same secret as Account__InternalKey>`. The game server talks to the account server over the loopback address, not through the internet. Open TCP port 7777 in the firewall (`ufw allow 7777/tcp`, `ufw allow 80,443/tcp`, SSH). Match logs are written to `/var/log/game`; rotate or delete them after a few days (`PRIVACY_POLICY.md` promises a limited time).

## 6. Point the game at it

- `unity-client/Assets/Resources/accountserver.txt` → `https://accounts.yourgame.com` (development builds), and `AccountSettings.ReleaseUrl` in `AccountService.cs` for release builds.
- `unity-client/Assets/Resources/gameserver.txt` → `play.yourgame.com:7777`.

Release builds refuse plain `http`, so the account address must be `https`.

## 7. After it runs

- Test from a phone on mobile data (not Wi-Fi): sign up, confirm the e-mail, play a ranked match against a bot (`server/BotClient`).
- Watch `journalctl -u account -u game -f` for the first days; disk space and the database size once a week.
- Updating: publish again, copy, `systemctl restart account game`. A restart ends running matches (players have 60 s to reconnect, so restart when few play).
- Known limit: one game server process holds all matches in memory. That is fine for the first thousands of players; more needs several servers behind a lobby (not built).
