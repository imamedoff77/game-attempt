using Npgsql;

namespace AccountServer;

public sealed record User(long Id, string Email, string? PasswordHash, bool EmailVerified, string DisplayName, string? GoogleSub, int FailedLogins, DateTimeOffset? LockedUntil, int Rating = 1000, int RatedGames = 0, int RatedWins = 0);

/// <summary>What a ranked match did to the two ratings (see <see cref="AccountStore.RecordRatedMatch"/>).</summary>
public sealed record RatedResult(bool AlreadyRecorded, int WinnerOld, int WinnerNew, int LoserOld, int LoserNew);

public enum ResetResult { Ok, Invalid }

public sealed record PurchaseRecord(long Id, long UserId, string ProductId, int Credits, bool Delivered);
public sealed record BoosterRecord(string Kind, DateTimeOffset ExpiresAt);

/// <summary>All database access of the account server (plain SQL, one place).</summary>
public sealed class AccountStore : IDisposable
{
    private const string UserColumns = "id, email, password_hash, email_verified, display_name, google_sub, failed_logins, locked_until, rating, rated_games, rated_wins";
    private readonly NpgsqlDataSource _db;

    public AccountStore(AccountOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("Account:ConnectionString is not set (PostgreSQL connection string).");
        _db = NpgsqlDataSource.Create(options.ConnectionString);
    }

    public void Dispose() => _db.Dispose();

    public async Task InitAsync()
    {
        await using NpgsqlCommand cmd = _db.CreateCommand(Schema.Sql);
        await cmd.ExecuteNonQueryAsync();
    }

    private static User ReadUser(NpgsqlDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetBoolean(3), r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5), r.GetInt32(6), r.IsDBNull(7) ? null : r.GetFieldValue<DateTimeOffset>(7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10));

    private async Task<User?> QueryUser(string where, string name, object value)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand($"select {UserColumns} from users where {where}");
        cmd.Parameters.AddWithValue(name, value);
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadUser(r) : null;
    }

    public Task<User?> GetByEmail(string emailNorm) => QueryUser("email_norm = @v", "v", emailNorm);
    public Task<User?> GetById(long id) => QueryUser("id = @v", "v", id);
    public Task<User?> GetByGoogleSub(string sub) => QueryUser("google_sub = @v", "v", sub);

    /// <summary>Creates an account and its empty profile. Null when the e-mail address is already registered.</summary>
    public async Task<User?> CreateUser(string email, string emailNorm, string? passwordHash, bool verified, string displayName, string? googleSub, DateTimeOffset now)
    {
        await using NpgsqlConnection conn = await _db.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        try
        {
            await using var insert = new NpgsqlCommand(
                $"insert into users (email, email_norm, password_hash, email_verified, display_name, google_sub, created_at) values (@e, @n, @p, @v, @d, @g, @t) returning {UserColumns}", conn, tx);
            insert.Parameters.AddWithValue("e", email);
            insert.Parameters.AddWithValue("n", emailNorm);
            insert.Parameters.AddWithValue("p", (object?)passwordHash ?? DBNull.Value);
            insert.Parameters.AddWithValue("v", verified);
            insert.Parameters.AddWithValue("d", displayName);
            insert.Parameters.AddWithValue("g", (object?)googleSub ?? DBNull.Value);
            insert.Parameters.AddWithValue("t", now);
            User user;
            await using (NpgsqlDataReader r = await insert.ExecuteReaderAsync())
            {
                await r.ReadAsync();
                user = ReadUser(r);
            }
            await using var profile = new NpgsqlCommand("insert into profiles (user_id, updated_at) values (@u, @t)", conn, tx);
            profile.Parameters.AddWithValue("u", user.Id);
            profile.Parameters.AddWithValue("t", now);
            await profile.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return user;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return null;
        }
    }

    private async Task Exec(string sql, params (string Name, object? Value)[] args)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand(sql);
        foreach ((string name, object? value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public Task SetPassword(long userId, string hash) =>
        Exec("update users set password_hash = @h, failed_logins = 0, locked_until = null where id = @u", ("h", hash), ("u", userId));

    public Task MarkVerified(long userId) => Exec("update users set email_verified = true where id = @u", ("u", userId));

    public Task SetDisplayName(long userId, string name) => Exec("update users set display_name = @n where id = @u", ("n", name), ("u", userId));

    /// <summary>Attaches a Google identity. With <paramref name="clearPassword"/> the old password is removed (an unverified password account could have been made by someone else).</summary>
    public Task LinkGoogle(long userId, string sub, bool clearPassword) =>
        Exec("update users set google_sub = @g, email_verified = true, password_hash = case when @c then null else password_hash end where id = @u",
            ("g", sub), ("c", clearPassword), ("u", userId));

    public Task DeleteUser(long userId) => Exec("delete from users where id = @u", ("u", userId));

    /// <summary>Counts a wrong password; after <paramref name="max"/> in a row the account is locked until the returned time.</summary>
    public async Task<DateTimeOffset?> RegisterFailedLogin(long userId, DateTimeOffset now, int max, TimeSpan lockFor)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand(
            @"update users set
                locked_until = case when failed_logins + 1 >= @max then @until else locked_until end,
                failed_logins = case when failed_logins + 1 >= @max then 0 else failed_logins + 1 end
              where id = @u returning locked_until");
        cmd.Parameters.AddWithValue("max", max);
        cmd.Parameters.AddWithValue("until", now + lockFor);
        cmd.Parameters.AddWithValue("u", userId);
        object? result = await cmd.ExecuteScalarAsync();
        return result is DateTimeOffset until && until > now ? until : null;
    }

    public Task ClearFailedLogins(long userId) => Exec("update users set failed_logins = 0, locked_until = null where id = @u", ("u", userId));

    // ---- sessions ----

    public Task CreateSession(byte[] tokenHash, long userId, DateTimeOffset now, DateTimeOffset expires) =>
        Exec("insert into sessions (token_hash, user_id, created_at, expires_at) values (@h, @u, @c, @e)", ("h", tokenHash), ("u", userId), ("c", now), ("e", expires));

    public async Task<User?> FindSessionUser(byte[] tokenHash, DateTimeOffset now)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand(
            "select u.id, u.email, u.password_hash, u.email_verified, u.display_name, u.google_sub, u.failed_logins, u.locked_until, u.rating, u.rated_games, u.rated_wins " +
            "from sessions s join users u on u.id = s.user_id where s.token_hash = @h and s.expires_at > @now");
        cmd.Parameters.AddWithValue("h", tokenHash);
        cmd.Parameters.AddWithValue("now", now);
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? ReadUser(r) : null;
    }

    public Task DeleteSession(byte[] tokenHash) => Exec("delete from sessions where token_hash = @h", ("h", tokenHash));

    public Task DeleteSessions(long userId, byte[]? except = null) =>
        Exec("delete from sessions where user_id = @u and (@x::bytea is null or token_hash <> @x::bytea)", ("u", userId), ("x", except));

    // ---- e-mail tokens (verification links, reset codes) ----

    /// <summary>Creates a token; older unused ones of the same kind for the user stop working.</summary>
    public async Task CreateEmailToken(long userId, string purpose, byte[] tokenHash, DateTimeOffset expires)
    {
        await using NpgsqlConnection conn = await _db.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        await using (var old = new NpgsqlCommand("update email_tokens set used = true where user_id = @u and purpose = @p and not used", conn, tx))
        {
            old.Parameters.AddWithValue("u", userId);
            old.Parameters.AddWithValue("p", purpose);
            await old.ExecuteNonQueryAsync();
        }
        await using (var insert = new NpgsqlCommand("insert into email_tokens (user_id, purpose, token_hash, expires_at) values (@u, @p, @h, @e)", conn, tx))
        {
            insert.Parameters.AddWithValue("u", userId);
            insert.Parameters.AddWithValue("p", purpose);
            insert.Parameters.AddWithValue("h", tokenHash);
            insert.Parameters.AddWithValue("e", expires);
            await insert.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    /// <summary>Uses a link token once. Returns the user it belongs to, or null if unknown, used or expired.</summary>
    public async Task<long?> ConsumeLinkToken(string purpose, byte[] tokenHash, DateTimeOffset now)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand(
            "update email_tokens set used = true where purpose = @p and token_hash = @h and not used and expires_at > @now returning user_id");
        cmd.Parameters.AddWithValue("p", purpose);
        cmd.Parameters.AddWithValue("h", tokenHash);
        cmd.Parameters.AddWithValue("now", now);
        object? result = await cmd.ExecuteScalarAsync();
        return result is long id ? id : null;
    }

    /// <summary>Checks a typed reset code. A wrong code counts as an attempt; after <paramref name="maxAttempts"/> the code is dead.</summary>
    public async Task<ResetResult> TryConsumeResetCode(long userId, byte[] codeHash, DateTimeOffset now, int maxAttempts)
    {
        await using NpgsqlConnection conn = await _db.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        long id; byte[] stored; int attempts;
        await using (var select = new NpgsqlCommand(
            "select id, token_hash, attempts from email_tokens where user_id = @u and purpose = 'reset' and not used and expires_at > @now order by id desc limit 1 for update", conn, tx))
        {
            select.Parameters.AddWithValue("u", userId);
            select.Parameters.AddWithValue("now", now);
            await using NpgsqlDataReader r = await select.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return ResetResult.Invalid;
            id = r.GetInt64(0);
            stored = (byte[])r.GetValue(1);
            attempts = r.GetInt32(2);
        }
        bool match = attempts < maxAttempts && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(stored, codeHash);
        await using (var update = new NpgsqlCommand(
            match ? "update email_tokens set used = true where id = @i" : "update email_tokens set attempts = attempts + 1, used = (attempts + 1 >= @max) where id = @i", conn, tx))
        {
            update.Parameters.AddWithValue("i", id);
            if (!match) update.Parameters.AddWithValue("max", maxAttempts);
            await update.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return match ? ResetResult.Ok : ResetResult.Invalid;
    }

    // ---- profile ----

    public async Task<(long Revision, string Json)> GetProfile(long userId)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand("select revision, data::text from profiles where user_id = @u");
        cmd.Parameters.AddWithValue("u", userId);
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? (r.GetInt64(0), r.GetString(1)) : (0, "{}");
    }

    /// <summary>Writes the profile only if nobody changed it since <paramref name="baseRevision"/>. Otherwise returns false and the current version.</summary>
    public async Task<(bool Ok, long Revision, string Json)> UpdateProfile(long userId, long baseRevision, string json, DateTimeOffset now)
    {
        await using (NpgsqlCommand cmd = _db.CreateCommand(
            "update profiles set revision = revision + 1, data = @d::jsonb, updated_at = @t where user_id = @u and revision = @b returning revision"))
        {
            cmd.Parameters.AddWithValue("d", json);
            cmd.Parameters.AddWithValue("t", now);
            cmd.Parameters.AddWithValue("u", userId);
            cmd.Parameters.AddWithValue("b", baseRevision);
            object? result = await cmd.ExecuteScalarAsync();
            if (result is long revision) return (true, revision, json);
        }
        (long current, string data) = await GetProfile(userId);
        return (false, current, data);
    }

    // ---- ranked matches ----

    /// <summary>
    /// Counts a finished ranked match once per <paramref name="matchId"/> (asking again changes nothing and returns what was decided the first time).
    /// Null when one of the two accounts does not exist (deleted meanwhile) or both are the same.
    /// </summary>
    public async Task<RatedResult?> RecordRatedMatch(string matchId, long winnerId, long loserId, DateTimeOffset now)
    {
        if (winnerId == loserId) return null;
        await using NpgsqlConnection conn = await _db.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();

        await using (var known = new NpgsqlCommand("select winner_old, winner_new, loser_old, loser_new from rated_matches where match_id = @m", conn, tx))
        {
            known.Parameters.AddWithValue("m", matchId);
            await using NpgsqlDataReader r = await known.ExecuteReaderAsync();
            if (await r.ReadAsync()) return new RatedResult(true, r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3));
        }

        // Lock both rows in id order so two matches at once cannot deadlock.
        var rows = new Dictionary<long, (int Rating, int Games)>();
        await using (var select = new NpgsqlCommand("select id, rating, rated_games from users where id = any(@ids) order by id for update", conn, tx))
        {
            select.Parameters.AddWithValue("ids", new[] { winnerId, loserId });
            await using NpgsqlDataReader r = await select.ExecuteReaderAsync();
            while (await r.ReadAsync()) rows[r.GetInt64(0)] = (r.GetInt32(1), r.GetInt32(2));
        }
        if (!rows.TryGetValue(winnerId, out var w) || !rows.TryGetValue(loserId, out var l)) return null;

        SimCore.Rating.Apply(w.Rating, w.Games, l.Rating, l.Games, out int winnerNew, out int loserNew);
        await using (var update = new NpgsqlCommand(
            "update users set rating = @r, rated_games = rated_games + 1, rated_wins = rated_wins + @win where id = @u", conn, tx))
        {
            NpgsqlParameter pRating = update.Parameters.AddWithValue("r", 0);
            NpgsqlParameter pWin = update.Parameters.AddWithValue("win", 0);
            NpgsqlParameter pUser = update.Parameters.AddWithValue("u", 0L);
            foreach ((long id, int rating, int win) in new[] { (winnerId, winnerNew, 1), (loserId, loserNew, 0) })
            {
                pRating.Value = rating;
                pWin.Value = win;
                pUser.Value = id;
                await update.ExecuteNonQueryAsync();
            }
        }
        await using (var insert = new NpgsqlCommand(
            "insert into rated_matches (match_id, winner_id, loser_id, winner_old, winner_new, loser_old, loser_new, created_at) values (@m, @w, @l, @wo, @wn, @lo, @ln, @t)", conn, tx))
        {
            insert.Parameters.AddWithValue("m", matchId);
            insert.Parameters.AddWithValue("w", winnerId);
            insert.Parameters.AddWithValue("l", loserId);
            insert.Parameters.AddWithValue("wo", w.Rating);
            insert.Parameters.AddWithValue("wn", winnerNew);
            insert.Parameters.AddWithValue("lo", l.Rating);
            insert.Parameters.AddWithValue("ln", loserNew);
            insert.Parameters.AddWithValue("t", now);
            await insert.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return new RatedResult(false, w.Rating, winnerNew, l.Rating, loserNew);
    }

    // ---- store ----

    public async Task<PurchaseRecord?> FindPurchase(byte[] tokenHash)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand("select id, user_id, product_id, credits, delivered from purchases where token_hash = @h");
        cmd.Parameters.AddWithValue("h", tokenHash);
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new PurchaseRecord(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetBoolean(4)) : null;
    }

    /// <summary>
    /// Records a paid purchase once (the token is unique) and gives what it contains: credits wait to be collected by the game, a booster
    /// starts or extends right away. Returns null when the token was already recorded (by a parallel request).
    /// </summary>
    public async Task<PurchaseRecord?> AddPurchase(long userId, Product product, byte[] tokenHash, string orderId, DateTimeOffset now)
    {
        await using NpgsqlConnection conn = await _db.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        long id;
        await using (var insert = new NpgsqlCommand(
            "insert into purchases (user_id, product_id, token_hash, order_id, credits, delivered, created_at) values (@u, @p, @h, @o, @c, @d, @t) on conflict (token_hash) do nothing returning id", conn, tx))
        {
            insert.Parameters.AddWithValue("u", userId);
            insert.Parameters.AddWithValue("p", product.Id);
            insert.Parameters.AddWithValue("h", tokenHash);
            insert.Parameters.AddWithValue("o", orderId);
            insert.Parameters.AddWithValue("c", product.Credits);
            insert.Parameters.AddWithValue("d", product.Credits == 0); // nothing to collect for a booster
            insert.Parameters.AddWithValue("t", now);
            object? result = await insert.ExecuteScalarAsync();
            if (result is not long newId) return null;
            id = newId;
        }
        if (product.Kind == ProductKind.Booster)
        {
            await using var booster = new NpgsqlCommand(
                @"insert into boosters (user_id, kind, expires_at) values (@u, @k, @t + make_interval(days => @d))
                  on conflict (user_id, kind) do update set expires_at = greatest(boosters.expires_at, @t) + make_interval(days => @d)", conn, tx);
            booster.Parameters.AddWithValue("u", userId);
            booster.Parameters.AddWithValue("k", product.Booster);
            booster.Parameters.AddWithValue("t", now);
            booster.Parameters.AddWithValue("d", product.BoosterDays);
            await booster.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return new PurchaseRecord(id, userId, product.Id, product.Credits, product.Credits == 0);
    }

    public async Task<List<PurchaseRecord>> PendingGrants(long userId)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand("select id, user_id, product_id, credits, delivered from purchases where user_id = @u and not delivered order by id");
        cmd.Parameters.AddWithValue("u", userId);
        var list = new List<PurchaseRecord>();
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new PurchaseRecord(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetInt32(3), r.GetBoolean(4)));
        return list;
    }

    /// <summary>The game has added these purchases' credits to the player's wallet: they will not be offered again.</summary>
    public Task AckGrants(long userId, long[] ids) =>
        Exec("update purchases set delivered = true where user_id = @u and id = any(@ids)", ("u", userId), ("ids", ids));

    public async Task<List<BoosterRecord>> ActiveBoosters(long userId, DateTimeOffset now)
    {
        await using NpgsqlCommand cmd = _db.CreateCommand("select kind, expires_at from boosters where user_id = @u and expires_at > @now order by kind");
        cmd.Parameters.AddWithValue("u", userId);
        cmd.Parameters.AddWithValue("now", now);
        var list = new List<BoosterRecord>();
        await using NpgsqlDataReader r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new BoosterRecord(r.GetString(0), r.GetFieldValue<DateTimeOffset>(1)));
        return list;
    }
}
