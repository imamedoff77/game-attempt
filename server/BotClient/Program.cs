using System.Diagnostics;
using SimCore;
using SimCore.Tests;

// A computer-controlled second player, for trying the online mode with only one phone.
//   dotnet run --project server/BotClient -- <server[:port]> host
//   dotnet run --project server/BotClient -- <server[:port]> join <CODE>
// It plays in real time (20 ticks a second), gives random orders, and prints a world fingerprint every 10 seconds
// so it can be compared with what the phone shows in the developer panel.
if (args.Length < 2 || (args[1] == "join" && args.Length < 3))
{
    Console.WriteLine("usage: BotClient <server[:port]> host | join <CODE> [seconds]");
    return 1;
}

Defs.Load(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gamedata.json")));
string address = args[0];
string host = address;
int port = 7777;
int colon = address.LastIndexOf(':');
if (colon > 0 && int.TryParse(address[(colon + 1)..], out int p)) { host = address[..colon]; port = p; }

using var link = new GameConnection();
link.Connect(host, port);
// BOT_TOKEN: an account session token, for servers that only let signed-in players in.
string authToken = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? "";
if (args[1] == "host") link.Send(new CreateRoom(MessageCodec.ProtocolVersion, Defs.DataHash, null, authToken));
else link.Send(new JoinRoom(MessageCodec.ProtocolVersion, Defs.DataHash, args[2].ToUpperInvariant(), null, authToken));
double seconds = args.Length > (args[1] == "join" ? 3 : 2) ? double.Parse(args[^1]) : 600;

World? world = null;
LockstepClient? client = null;
var rng = new Random();
var clock = Stopwatch.StartNew();
double accumulator = 0, last = 0, nextThink = 0, nextReport = 10;

while (clock.Elapsed.TotalSeconds < seconds)
{
    while (link.TryReceive(out NetMessage? m))
    {
        switch (m)
        {
            case RoomCreated rc: Console.WriteLine("Room code: " + rc.Code); break;
            case MatchStart ms:
                Console.WriteLine($"Match started, I am team {ms.Team}");
                world = OnlineScenario.Create();
                for (int team = 0; team < ms.Kits.Length; team++) Armory.FromBytes(ms.Kits[team]).Apply(world, team);
                client = new LockstepClient(world, ms.Team, ms.InputDelayTurns, link.Send);
                client.Start();
                break;
            case Reject r: Console.WriteLine("Refused: " + r.Reason); return 2;
            case PeerLeft: Console.WriteLine("The opponent lost the connection (waiting for it to come back)."); break;
            case PeerBack: Console.WriteLine("The opponent is back."); break;
            default: client?.Receive(m); break;
        }
    }
    if (link.Closed) { Console.WriteLine("Connection closed: " + link.Failure); return 3; }

    double now = clock.Elapsed.TotalSeconds;
    accumulator = Math.Min(accumulator + (now - last), 0.4);
    last = now;
    if (client != null && world != null)
    {
        if (now >= nextThink)
        {
            nextThink = now + 0.5 + rng.NextDouble();
            Command? c = MatchBot.Next(world, client.Team, rng);
            if (c != null) client.QueueCommand(c);
        }
        while (accumulator >= 0.05 && client.TryStep()) accumulator -= 0.05;
        if (client.Desynced) { Console.WriteLine($"OUT OF SYNC at turn {client.DesyncInfo.Turn}"); return 4; }
        if (now >= nextReport)
        {
            nextReport += 10;
            Console.WriteLine($"tick {client.TicksExecuted}  hash {world.StateHash():X16}  waiting={client.Waiting}");
        }
    }
    Thread.Sleep(5);
}
Console.WriteLine("Time is up.");
return 0;
