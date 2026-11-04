# Server

A relay for 1v1 matches. It does not run the game: it pairs two players by room code, puts their commands in order
(`LockstepRelay`, from `sim-core`) and compares the world fingerprints they send. Design: see `FAZA_1_PLAN.md`, step 1.4.

```bash
dotnet run --project server/GameServer -- 7777        # port (default 7777), optional log folder as second argument
dotnet test server/GameServer.Tests                    # starts real servers on loopback ports
```

Broken (desynced) matches are saved in `logs/` next to the program: a `.txt` summary and a `.turns` file with every turn's commands.
