namespace AccountServer;

/// <summary>The tables of the account database. Created at start-up if they do not exist yet.</summary>
public static class Schema
{
    public const string Sql = """
        create table if not exists users (
            id bigserial primary key,
            email text not null,
            email_norm text not null unique,
            password_hash text,
            email_verified boolean not null default false,
            display_name text not null,
            google_sub text unique,
            created_at timestamptz not null,
            failed_logins int not null default 0,
            locked_until timestamptz
        );
        create table if not exists sessions (
            token_hash bytea primary key,
            user_id bigint not null references users(id) on delete cascade,
            created_at timestamptz not null,
            expires_at timestamptz not null
        );
        create index if not exists sessions_user on sessions(user_id);
        create table if not exists email_tokens (
            id bigserial primary key,
            user_id bigint not null references users(id) on delete cascade,
            purpose text not null,
            token_hash bytea not null,
            expires_at timestamptz not null,
            attempts int not null default 0,
            used boolean not null default false
        );
        create index if not exists email_tokens_lookup on email_tokens(purpose, token_hash);
        create index if not exists email_tokens_user on email_tokens(user_id, purpose);
        create table if not exists profiles (
            user_id bigint primary key references users(id) on delete cascade,
            revision bigint not null default 0,
            data jsonb not null default '{}',
            updated_at timestamptz not null
        );
        """;
}
