using GameServer;

// Usage: dotnet run --project server/GameServer -- [port] [log-folder]
int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 7777;
string? logFolder = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "logs");

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

var server = new MatchServer(new ServerOptions { Port = port, LogFolder = logFolder });
server.Start();
Console.WriteLine($"Server listening on port {server.Port}. Press Ctrl+C to stop.");
// Typed commands (only for testing by hand): "drop CODE TEAM" cuts a player's connection, as a bad network would.
_ = Task.Run(() =>
{
    string? line;
    while ((line = Console.In.ReadLine()) != null)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0] == "lag" && int.TryParse(parts[1], out int ms))
        {
            Lag.Set(ms, parts.Length > 2 && int.TryParse(parts[2], out int jit) ? jit : 0);
            Console.WriteLine($"lag set: {ms} ms (+0..{(parts.Length > 2 ? parts[2] : "0")} ms) each way");
        }
        if (parts.Length == 3 && parts[0] == "drop" && int.TryParse(parts[2], out int team))
            Console.WriteLine(server.DropPlayer(parts[1], team) ? "dropped" : "no such player");
    }
});
try { await Task.Delay(Timeout.Infinite, cancel.Token); } catch (OperationCanceledException) { }
await server.StopAsync();
