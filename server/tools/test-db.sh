#!/usr/bin/env bash
# A private PostgreSQL for the account server tests: its own data folder and port, no root, does not touch the system database.
#   tools/test-db.sh start   -> starts it (creates it the first time) and prints the connection string
#   tools/test-db.sh stop
set -euo pipefail
DIR="${ACCOUNTS_TEST_DB_DIR:-$HOME/.local/share/game-test-postgres}"
PORT="${ACCOUNTS_TEST_DB_PORT:-54329}"
CONN="Host=127.0.0.1;Port=$PORT;Username=game;Password=game;Database=postgres;Include Error Detail=true"
case "${1:-start}" in
  start)
    if [ ! -d "$DIR/data" ]; then
      mkdir -p "$DIR"
      echo game > "$DIR/pw"
      initdb -D "$DIR/data" --locale=C.UTF-8 -U game --pwfile="$DIR/pw" --auth=scram-sha-256 -E UTF8 >/dev/null
    fi
    if ! pg_ctl -D "$DIR/data" status >/dev/null 2>&1; then
      pg_ctl -D "$DIR/data" -o "-p $PORT -k $DIR -c listen_addresses=127.0.0.1 -c fsync=off" -l "$DIR/log" -w start >/dev/null
    fi
    echo "$CONN"
    ;;
  stop) pg_ctl -D "$DIR/data" stop -m fast ;;
esac
