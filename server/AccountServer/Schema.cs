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
        alter table users add column if not exists rating int not null default 1000;
        alter table users add column if not exists rated_games int not null default 0;
        alter table users add column if not exists rated_wins int not null default 0;
        create table if not exists rated_matches (
            match_id text primary key,
            winner_id bigint not null,
            loser_id bigint not null,
            winner_old int not null,
            winner_new int not null,
            loser_old int not null,
            loser_new int not null,
            created_at timestamptz not null
        );
        create table if not exists purchases (
            id bigserial primary key,
            user_id bigint not null references users(id) on delete cascade,
            product_id text not null,
            token_hash bytea not null unique,
            order_id text not null default '',
            credits int not null default 0,
            delivered boolean not null default false,
            created_at timestamptz not null
        );
        create index if not exists purchases_user on purchases(user_id);
        create table if not exists boosters (
            user_id bigint not null references users(id) on delete cascade,
            kind text not null,
            expires_at timestamptz not null,
            primary key (user_id, kind)
        );
        """;
}
