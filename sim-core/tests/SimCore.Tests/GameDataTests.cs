using SimCore;

public class GameDataTests
{
    private static string Shipped() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "gamedata.json"));

    [Fact]
    public void ShippedFile_HoldsTheDemoBalance()
    {
        Assert.Equal(300, Economy.StartResources);
        Assert.Equal(20, Economy.HqEnergy);
        Assert.Equal(100, Economy.HqIncomePerMinute);
        Assert.Equal(new[] { 120, 80, 60, 50, 40 }, Economy.PowerPlantOutput);
        Assert.Equal(new[] { 200, 140, 100, 80, 60 }, Economy.SupplyCenterIncome);

        Assert.Equal(Fix64.FromFraction(5, 2), Defs.Unit(UnitKind.RocketTrooper).Speed);
        Assert.Equal(300, Defs.Unit(UnitKind.Tank).Cost);
        Assert.Equal(15, Defs.Unit(UnitKind.Tank).BuildSeconds);
        Assert.Equal(BuildingKind.VehicleFactory, Defs.Unit(UnitKind.Jeep).Producer);

        CombatDef tank = Defs.Combat(UnitKind.Tank);
        Assert.Equal((1200, 50, 30, 90), (tank.Hp, tank.Damage, tank.CooldownTicks, tank.ArcDegrees));
        Assert.Equal(Fix64.FromInt(8), tank.Range);
        Assert.Equal(Fix64.FromFraction(707, 1000), tank.CosHalfArc);

        Assert.Equal(15000, Defs.BuildingHp(BuildingKind.Hq));
        Assert.Equal(50, Defs.Building(BuildingKind.VehicleFactory).EnergyUse);
    }

    [Fact]
    public void Produces_FollowsEachUnitsProducer_InFileOrder()
    {
        Assert.Equal(new[] { UnitKind.Rifleman, UnitKind.RocketTrooper, UnitKind.Sniper }, Defs.Produces(BuildingKind.Barracks));
        Assert.Equal(new[] { UnitKind.Jeep, UnitKind.Tank, UnitKind.ArmoredCar }, Defs.Produces(BuildingKind.VehicleFactory));
        Assert.Empty(Defs.Produces(BuildingKind.Hq));
    }

    [Fact]
    public void Hash_IsTheSame_ForTheSameValues_EvenWithDifferentSpacing()
    {
        string json = Shipped();
        ulong a = GameData.Parse(json).Hash;
        ulong b = GameData.Parse(json.Replace("  ", "\t").Replace("\r\n", "\n")).Hash;
        Assert.Equal(a, b);
        Assert.Equal(a, Defs.DataHash);
    }

    [Fact]
    public void Hash_Changes_WhenOneNumberChanges()
    {
        string json = Shipped();
        string changed = json.Replace("\"hp\": 1200 }", "\"hp\": 1201 }"); // power plant health
        Assert.NotEqual(json, changed);
        Assert.NotEqual(GameData.Parse(json).Hash, GameData.Parse(changed).Hash);
    }

    [Fact]
    public void LoadingOtherValues_ChangesTheSimulation_ThenRestore()
    {
        string json = Shipped();
        try
        {
            Defs.Load(json.Replace("\"startResources\": 300", "\"startResources\": 777"));
            Assert.Equal(777, new World(new MapGrid(8, 8)).Resources);
        }
        finally
        {
            Defs.Load(json);
        }
        Assert.Equal(300, Economy.StartResources);
    }

    [Theory]
    [InlineData("\"speedMilli\": 3000", "\"speedMilli\": 3.5", "whole numbers")]
    [InlineData("\"speedMilli\": 3000", "\"speedMilli\": 0", "outside the allowed range")]
    [InlineData("\"hp\": 1500", "\"hp\": -5", "outside the allowed range")]
    [InlineData("\"cost\": 50,", "\"cost\": \"cheap\",", "expected a whole number")]
    [InlineData("\"producer\": \"Barracks\",", "\"producer\": \"Castle\",", "not a valid BuildingKind")]
    [InlineData("\"damage\": 6,", "\"damage\": 6, \"extra\": 1,", "unknown name")]
    [InlineData("\"version\": 1", "\"version\": 2", "version 2 is not supported")]
    [InlineData("\"economy\": {", "\"economi\": {", "missing \"economy\"")]
    public void BadFile_IsRejected_WithAClearMessage(string find, string replace, string expectedPart)
    {
        string json = Shipped();
        Assert.Contains(find, json);
        var ex = Assert.Throws<GameDataException>(() => GameData.Parse(json.Replace(find, replace)));
        Assert.Contains(expectedPart, ex.Message);
    }

    [Fact]
    public void MissingUnit_IsRejected()
    {
        var lines = Shipped().Replace("\r\n", "\n").Split('\n').ToList();
        int last = lines.FindIndex(l => l.Contains("\"id\": \"ArmoredCar\""));
        lines.RemoveAt(last);
        lines[last - 1] = lines[last - 1].TrimEnd(',', ' '); // the line before is now the last one in the list
        var ex = Assert.Throws<GameDataException>(() => GameData.Parse(string.Join("\n", lines)));
        Assert.Contains("missing entry for ArmoredCar", ex.Message);
    }

    [Fact]
    public void NewUnitsAndBuildings_NeedBaseLevel2()
    {
        Assert.Equal(1, Defs.Unit(UnitKind.Rifleman).RequiredLevel);
        Assert.Equal(2, Defs.Unit(UnitKind.Sniper).RequiredLevel);
        Assert.Equal(2, Defs.Unit(UnitKind.ArmoredCar).RequiredLevel);
        Assert.Equal(1, Defs.Building(BuildingKind.Barracks).RequiredLevel);
        Assert.Equal(2, Defs.Building(BuildingKind.Tower).RequiredLevel);
        Assert.True(Defs.Building(BuildingKind.Tower).Damage > 0);
        Assert.True(Defs.Building(BuildingKind.RepairStation).RepairPerSecond > 0);
        Assert.Equal(0, Defs.Building(BuildingKind.Barracks).Damage);
    }

    [Fact]
    public void ShootingBuilding_WithoutRange_IsRejected()
    {
        string json = Shipped().Replace("\"damage\": 12, \"range\": 9, \"cooldownTicks\": 12", "\"damage\": 12");
        var ex = Assert.Throws<GameDataException>(() => GameData.Parse(json));
        Assert.Contains("needs range", ex.Message);
    }

    [Fact]
    public void FailedLoad_KeepsThePreviousData()
    {
        Assert.Throws<GameDataException>(() => Defs.Load("{ nonsense"));
        Assert.Equal(300, Economy.StartResources);
    }

    [Fact]
    public void MiniJson_ReadsTheBasicShapes()
    {
        var map = (Dictionary<string, object>)MiniJson.Parse("{\"a\": [1, -2, true, null], \"b\": \"x\\ny\\u0041\"}");
        var list = (List<object>)map["a"];
        Assert.Equal(1L, list[0]);
        Assert.Equal(-2L, list[1]);
        Assert.Equal(true, list[2]);
        Assert.Null(list[3]);
        Assert.Equal("x\nyA", map["b"]);
    }

    [Theory]
    [InlineData("{\"a\": 1, \"a\": 2}", "duplicate name")]
    [InlineData("[1, 2", "unexpected end")]
    [InlineData("{\"a\": 1} extra", "after the end")]
    [InlineData("{\"a\": 1e3}", "whole numbers")]
    [InlineData("\"open", "not closed")]
    public void MiniJson_RejectsBrokenText(string text, string expectedPart)
    {
        var ex = Assert.Throws<GameDataException>(() => MiniJson.Parse(text));
        Assert.Contains(expectedPart, ex.Message);
    }
}
