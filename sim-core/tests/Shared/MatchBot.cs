#nullable disable
using SimCore;

namespace SimCore.Tests;

/// <summary>A computer player that gives random but legal orders. Shared by the in-memory and the real-socket match tests.</summary>
public static class MatchBot
{
    public static Command Next(World w, int team, Random rng)
    {
        var mine = w.Units.Where(u => u.Team == team && u.Hp > 0).ToList();
        var myBuildings = w.Buildings.Where(b => b.Team == team && b.Hp > 0).ToList();
        var enemyUnits = w.Units.Where(u => u.Team != team && u.Hp > 0).ToList();
        var enemyBuildings = w.Buildings.Where(b => b.Team != team && b.Hp > 0).ToList();
        Vec2 Point() => new Vec2(Fix64.FromInt(rng.Next(2, 62)), Fix64.FromInt(rng.Next(2, 62)));
        int[] Some() => mine.OrderBy(_ => rng.Next()).Take(rng.Next(1, 12)).Select(u => u.Id).OrderBy(i => i).ToArray();

        switch (rng.Next(11))
        {
            case 0:
            case 1:
                {
                    var producers = myBuildings.Where(b => Defs.Produces(b.Kind).Length > 0).ToList();
                    if (producers.Count == 0) return null;
                    Building b = producers[rng.Next(producers.Count)];
                    UnitKind[] kinds = Defs.Produces(b.Kind);
                    return new QueueUnitCommand(b.Id, kinds[rng.Next(kinds.Length)]);
                }
            case 2:
                return new BuildCommand((BuildingKind)rng.Next(1, 5), rng.Next(0, 60), rng.Next(0, 60));
            case 3:
            case 4:
                return mine.Count == 0 ? null : new MoveCommand(Some(), Point());
            case 5:
                {
                    if (mine.Count == 0) return null;
                    if (enemyUnits.Count > 0 && rng.Next(2) == 0) return new AttackCommand(Some(), AttackKind.Unit, enemyUnits[rng.Next(enemyUnits.Count)].Id);
                    if (enemyBuildings.Count > 0) return new AttackCommand(Some(), AttackKind.Building, enemyBuildings[rng.Next(enemyBuildings.Count)].Id);
                    return null;
                }
            case 6:
                return myBuildings.Count == 0 ? null : new CancelQueuedCommand(myBuildings[rng.Next(myBuildings.Count)].Id, rng.Next(0, 3));
            case 8:
                return new UpgradeBaseCommand();
            case 9:
                {
                    var building = myBuildings.Where(b => b.UnderConstruction).ToList();
                    return building.Count == 0 ? null : new CancelBuildCommand(building[rng.Next(building.Count)].Id);
                }
            case 10:
                return new ResearchCommand((UpgradeKind)rng.Next(Defs.UpgradeCount));
            default:
                return myBuildings.Count == 0 ? null : new SetRallyCommand(myBuildings[rng.Next(myBuildings.Count)].Id, Point());
        }
    }
}
