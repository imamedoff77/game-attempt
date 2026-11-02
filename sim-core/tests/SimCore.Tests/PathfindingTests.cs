using System.Diagnostics;
using SimCore;

namespace SimCore.Tests;

public class PathfindingTests
{
    private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));

    private static MapGrid WithWall(int size, int x, int fromY, int toY)
    {
        var map = new MapGrid(size, size);
        for (int y = fromY; y <= toY; y++) map.SetTerrain(x, y, TerrainType.Rock);
        return map;
    }

    [Fact]
    public void Path_GoesAroundWall_ThroughTheGap()
    {
        MapGrid map = WithWall(20, 10, 0, 14); // gap at y = 15..19

        var path = Pathfinder.FindPath(map, 2, 5, 18, 5);

        Assert.NotNull(path);
        Assert.Equal((2, 5), path[0]);
        Assert.Equal((18, 5), path[^1]);
        Assert.True(path.Count >= 3);
        Assert.Contains(path, c => c.y >= 15);
        Assert.All(path, c => Assert.True(map.IsWalkable(c.x, c.y)));
        for (int i = 1; i < path.Count; i++)
            Assert.True(Pathfinder.HasLineOfSight(map, path[i - 1].x, path[i - 1].y, path[i].x, path[i].y));
    }

    [Fact]
    public void Path_InOpenField_IsASingleStraightLeg()
    {
        var map = new MapGrid(30, 30);
        var path = Pathfinder.FindPath(map, 1, 1, 25, 20);
        Assert.Equal(2, path.Count);
    }

    [Fact]
    public void Path_DoesNotCutCornersBetweenTwoRocks()
    {
        var map = new MapGrid(12, 12);
        map.SetTerrain(5, 6, TerrainType.Rock);
        map.SetTerrain(6, 5, TerrainType.Rock);

        var path = Pathfinder.FindPath(map, 5, 5, 6, 6);

        Assert.NotNull(path);
        Assert.True(path.Count > 2, "a direct diagonal would squeeze between the two rocks");
    }

    [Fact]
    public void Path_ToEnclosedGoal_IsNull()
    {
        var map = new MapGrid(20, 20);
        for (int x = 8; x <= 12; x++)
        {
            map.SetTerrain(x, 8, TerrainType.Rock);
            map.SetTerrain(x, 12, TerrainType.Rock);
        }
        for (int y = 8; y <= 12; y++)
        {
            map.SetTerrain(8, y, TerrainType.Rock);
            map.SetTerrain(12, y, TerrainType.Rock);
        }

        Assert.Null(Pathfinder.FindPath(map, 1, 1, 10, 10));
    }

    [Fact]
    public void Path_ToBlockedGoal_IsNull()
    {
        var map = WithWall(10, 5, 0, 9);
        Assert.Null(Pathfinder.FindPath(map, 1, 1, 5, 5));
    }

    [Fact]
    public void NearestWalkable_FindsAClosestFreeCell()
    {
        var map = new MapGrid(20, 20);
        for (int y = 8; y <= 12; y++)
            for (int x = 8; x <= 12; x++)
                map.SetTerrain(x, y, TerrainType.Rock);

        var cell = Pathfinder.NearestWalkable(map, 10, 10);

        Assert.NotNull(cell);
        Assert.True(map.IsWalkable(cell.Value.x, cell.Value.y));
        Assert.True(System.Math.Max(System.Math.Abs(cell.Value.x - 10), System.Math.Abs(cell.Value.y - 10)) == 3);
    }

    [Fact]
    public void Path_IsIdenticalOnRepeatedRuns()
    {
        MapGrid map = DemoMap.Generate(64, 64, 2026, 26, 26, 6);
        var goal = Pathfinder.NearestWalkable(map, 55, 50).Value; // (55,50) itself may be rock or water
        var a = Pathfinder.FindPath(map, 26, 26, goal.x, goal.y);
        var b = Pathfinder.FindPath(map, 26, 26, goal.x, goal.y);
        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Path_Performance_Is_Reasonable_On64x64()
    {
        MapGrid map = DemoMap.Generate(64, 64, 2026, 26, 26, 6);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 200; i++)
            Pathfinder.FindPath(map, 26, 26, 5 + i % 50, 5 + (i * 7) % 50);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 3000, $"200 paths took {sw.ElapsedMilliseconds} ms");
    }

    // ---- inside the World ----

    [Fact]
    public void Unit_WalksAroundWall_NeverEntersBlockedCell_AndArrives()
    {
        MapGrid map = WithWall(30, 15, 0, 22); // gap at y = 23..29
        var w = new World(map);
        Unit u = w.SpawnUnit(UnitKind.Jeep, P(3, 5));
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(26, 5)));

        for (int i = 0; i < 2000; i++)
        {
            w.Step();
            Assert.True(map.IsWalkable(u.Pos.X.ToInt(), u.Pos.Y.ToInt()), $"unit inside blocked cell at tick {w.Tick}: {u.Pos}");
            if (!u.Moving) break;
        }

        Assert.False(u.Moving);
        Assert.Equal(P(26, 5), u.Pos);
    }

    [Fact]
    public void Order_ToRockCell_EndsOnTheNearestFreeCell()
    {
        var map = new MapGrid(30, 30);
        for (int y = 12; y <= 18; y++)
            for (int x = 12; x <= 18; x++)
                map.SetTerrain(x, y, TerrainType.Rock);
        var w = new World(map);
        Unit u = w.SpawnUnit(UnitKind.Rifleman, P(2, 2));
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(15, 15)));

        for (int i = 0; i < 2000 && (i == 0 || u.Moving); i++) w.Step();

        Assert.False(u.Moving);
        Assert.True(map.IsWalkable(u.Pos.X.ToInt(), u.Pos.Y.ToInt()));
    }

    [Fact]
    public void Order_ToUnreachablePocket_IsIgnored()
    {
        var map = new MapGrid(20, 20);
        for (int i = 8; i <= 12; i++)
        {
            map.SetTerrain(i, 8, TerrainType.Rock);
            map.SetTerrain(i, 12, TerrainType.Rock);
            map.SetTerrain(8, i, TerrainType.Rock);
            map.SetTerrain(12, i, TerrainType.Rock);
        }
        var w = new World(map);
        Unit u = w.SpawnUnit(UnitKind.Rifleman, P(2, 2));
        w.Enqueue(new MoveCommand(new[] { u.Id }, P(10, 10)));

        w.Step();

        Assert.False(u.Moving);
        Assert.Equal(P(2, 2), u.Pos);
    }

    [Fact]
    public void Vehicles_AndInfantry_HaveDifferentSpeeds()
    {
        Assert.True(Defs.Unit(UnitKind.Jeep).Speed > Defs.Unit(UnitKind.Rifleman).Speed);
        Assert.True(Defs.Unit(UnitKind.Tank).Speed < Defs.Unit(UnitKind.Rifleman).Speed);
        Assert.Equal(UnitClass.Infantry, Defs.Unit(UnitKind.RocketTrooper).Class);
        Assert.Equal(UnitClass.Vehicle, Defs.Unit(UnitKind.Tank).Class);
    }
}

public class BuildingTests
{
    [Fact]
    public void Buildings_HaveDifferentFootprints()
    {
        Assert.Equal((4, 4), (Defs.Building(BuildingKind.Hq).Width, Defs.Building(BuildingKind.Hq).Height));
        Assert.Equal((2, 2), (Defs.Building(BuildingKind.PowerPlant).Width, Defs.Building(BuildingKind.PowerPlant).Height));
        Assert.Equal((3, 3), (Defs.Building(BuildingKind.SupplyCenter).Width, Defs.Building(BuildingKind.SupplyCenter).Height));
        Assert.Equal((3, 2), (Defs.Building(BuildingKind.Barracks).Width, Defs.Building(BuildingKind.Barracks).Height));
        Assert.Equal((4, 3), (Defs.Building(BuildingKind.VehicleFactory).Width, Defs.Building(BuildingKind.VehicleFactory).Height));
    }

    [Fact]
    public void Placement_OnSand_Works_AndOccupiesWholeFootprint()
    {
        var w = new World(new MapGrid(30, 30));
        Building b = w.PlaceBuilding(BuildingKind.Hq, 10, 10);

        Assert.NotNull(b);
        for (int y = 10; y < 14; y++)
            for (int x = 10; x < 14; x++)
            {
                Assert.Equal(b.Id, w.Map.BuildingAt(x, y));
                Assert.False(w.Map.IsWalkable(x, y));
            }
        Assert.True(w.Map.IsWalkable(14, 10));
    }

    [Fact]
    public void Placement_IsRefused_OnStony_Rock_Water_Overlap_AndOutsideMap()
    {
        var map = new MapGrid(30, 30);
        map.SetTerrain(5, 5, TerrainType.Stony);
        map.SetTerrain(15, 5, TerrainType.Rock);
        map.SetTerrain(25, 5, TerrainType.Water);
        var w = new World(map);

        Assert.Null(w.PlaceBuilding(BuildingKind.PowerPlant, 4, 4));   // touches stony
        Assert.Null(w.PlaceBuilding(BuildingKind.PowerPlant, 14, 4));  // touches rock
        Assert.Null(w.PlaceBuilding(BuildingKind.PowerPlant, 24, 4));  // touches water
        Assert.Null(w.PlaceBuilding(BuildingKind.Hq, 28, 28));         // sticks out of the map
        Assert.Null(w.PlaceBuilding(BuildingKind.Hq, -1, 0));

        Assert.NotNull(w.PlaceBuilding(BuildingKind.Barracks, 10, 20));
        Assert.Null(w.PlaceBuilding(BuildingKind.PowerPlant, 11, 21)); // overlaps the barracks
    }

    [Fact]
    public void Building_BlocksUnits_TheyWalkAround()
    {
        var w = new World(new MapGrid(30, 30));
        w.PlaceBuilding(BuildingKind.VehicleFactory, 10, 5); // 4 x 3 at (10..13, 5..7)
        Unit u = w.SpawnUnit(UnitKind.Tank, new Vec2(Fix64.FromInt(3), Fix64.FromInt(6)));
        w.Enqueue(new MoveCommand(new[] { u.Id }, new Vec2(Fix64.FromInt(20), Fix64.FromInt(6))));

        for (int i = 0; i < 3000; i++)
        {
            w.Step();
            Assert.True(w.Map.IsWalkable(u.Pos.X.ToInt(), u.Pos.Y.ToInt()));
            if (!u.Moving) break;
        }

        Assert.False(u.Moving);
        Assert.Equal(Fix64.FromInt(20), u.Pos.X);
    }
}

public class DemoMapTests
{
    private const ulong Seed = 2026;

    [Fact]
    public void SameSeed_SameMap_DifferentSeed_DifferentMap()
    {
        Assert.Equal(DemoMap.Generate(64, 64, Seed, 26, 26, 6).Hash(), DemoMap.Generate(64, 64, Seed, 26, 26, 6).Hash());
        Assert.NotEqual(DemoMap.Generate(64, 64, Seed, 26, 26, 6).Hash(), DemoMap.Generate(64, 64, Seed + 1, 26, 26, 6).Hash());
    }

    [Fact]
    public void Map_HasEveryTerrainType_AndAClearStartArea()
    {
        MapGrid map = DemoMap.Generate(64, 64, Seed, 26, 26, 6);

        var seen = new HashSet<TerrainType>();
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
                seen.Add(map.GetTerrain(x, y));
        Assert.Equal(4, seen.Count);

        for (int y = 20; y <= 32; y++)
            for (int x = 20; x <= 32; x++)
                Assert.Equal(TerrainType.Sand, map.GetTerrain(x, y));
    }

    [Fact]
    public void Map_MostWalkableLandIsReachableFromTheStart()
    {
        MapGrid map = DemoMap.Generate(64, 64, Seed, 26, 26, 6);
        int walkable = 0, reachable = 0;
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                if (!map.IsWalkable(x, y)) continue;
                walkable++;
                if (Pathfinder.FindPath(map, 26, 26, x, y) != null) reachable++;
            }

        Assert.True(reachable * 100 / walkable >= 90, $"only {reachable} of {walkable} walkable cells are reachable");
    }

    [Fact]
    public void WholeWorld_IsDeterministic_WithMapBuildingsAndOrders()
    {
        ulong Run()
        {
            var w = new World(DemoMap.Generate(64, 64, Seed, 26, 26, 6));
            w.PlaceBuilding(BuildingKind.Hq, 22, 22);
            w.PlaceBuilding(BuildingKind.PowerPlant, 28, 22);
            var ids = new List<int>
            {
                w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(26), Fix64.FromInt(28))).Id,
                w.SpawnUnit(UnitKind.RocketTrooper, new Vec2(Fix64.FromInt(27), Fix64.FromInt(28))).Id,
                w.SpawnUnit(UnitKind.Jeep, new Vec2(Fix64.FromInt(28), Fix64.FromInt(28))).Id,
                w.SpawnUnit(UnitKind.Tank, new Vec2(Fix64.FromInt(29), Fix64.FromInt(28))).Id,
            };
            w.Enqueue(new MoveCommand(ids.ToArray(), new Vec2(Fix64.FromInt(55), Fix64.FromInt(50))));
            for (int i = 0; i < 1500; i++) w.Step();
            return w.StateHash();
        }

        Assert.Equal(Run(), Run());
    }
}
