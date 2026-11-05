using SimCore;
using Xunit;

public class ArmoryTests
{
    private static CampaignProgress Rich()
    {
        var p = new CampaignProgress();
        for (int i = 1; i <= 10; i++) p.Record(i, 3); // plenty of credits
        return p;
    }

    [Fact]
    public void NewArmory_HasNoBonus_AndLevelsCostMoreAndMore()
    {
        var a = new Armory();
        foreach (UnitKind k in System.Enum.GetValues<UnitKind>())
        {
            Assert.Equal(0, a.Level(k));
            Assert.Equal(0, a.BonusMilli(k));
        }
        Assert.Equal(30, a.NextCost(UnitKind.Rifleman));
        Assert.Equal(60, a.NextCost(UnitKind.Tank));

        var wallet = Rich();
        a.Buy(UnitKind.Rifleman, wallet);
        Assert.Equal(60, a.NextCost(UnitKind.Rifleman));
    }

    [Fact]
    public void Buy_SpendsCredits_RaisesLevel_AndStopsAtTheTop()
    {
        var a = new Armory();
        var wallet = Rich();
        int before = wallet.Credits;
        Assert.True(a.Buy(UnitKind.Tank, wallet));
        Assert.Equal(before - 60, wallet.Credits);
        Assert.Equal(1, a.Level(UnitKind.Tank));
        Assert.Equal(Armory.BonusPerLevelMilli, a.BonusMilli(UnitKind.Tank));

        while (a.Buy(UnitKind.Tank, wallet)) { }
        Assert.Equal(Armory.MaxLevel, a.Level(UnitKind.Tank));
        Assert.Equal(0, a.NextCost(UnitKind.Tank));
        Assert.False(a.CanBuy(UnitKind.Tank, wallet));
    }

    [Fact]
    public void Buy_WithTooFewCredits_ChangesNothing()
    {
        var a = new Armory();
        var wallet = new CampaignProgress();
        Assert.False(a.CanBuy(UnitKind.Rifleman, wallet));
        Assert.False(a.Buy(UnitKind.Rifleman, wallet));
        Assert.Equal(0, a.Level(UnitKind.Rifleman));
        Assert.Equal(0, wallet.Credits);
        Assert.False(wallet.Spend(1));
        Assert.False(wallet.Spend(-5));
    }

    [Fact]
    public void SaveAndLoad_KeepLevels_AndBadTextIsHarmless()
    {
        var a = new Armory();
        var wallet = Rich();
        a.Buy(UnitKind.Sniper, wallet);
        a.Buy(UnitKind.Sniper, wallet);
        a.Buy(UnitKind.Jeep, wallet);
        var b = Armory.Parse(a.Serialize());
        Assert.Equal(2, b.Level(UnitKind.Sniper));
        Assert.Equal(1, b.Level(UnitKind.Jeep));
        Assert.Equal(a.Serialize(), b.Serialize());

        foreach (string bad in new[] { null, "", "x,y", "99,-3,2", ",,,," })
        {
            var c = Armory.Parse(bad);
            foreach (UnitKind k in System.Enum.GetValues<UnitKind>()) Assert.InRange(c.Level(k), 0, Armory.MaxLevel);
        }
    }

    [Fact]
    public void Apply_MakesUnitsStrongerForThatTeamOnly()
    {
        var a = new Armory();
        var wallet = Rich();
        for (int i = 0; i < 3; i++) a.Buy(UnitKind.Rifleman, wallet); // +12 %

        World w = SkirmishScenario.Create(1);
        Unit mine = w.Units.First(u => u.Team == 0);
        Unit theirs = w.Units.First(u => u.Team == 1);
        int baseHp = mine.MaxHp, baseDamage = w.EffectiveDamage(0, UnitKind.Rifleman);

        a.Apply(w, 0);

        Assert.Equal(baseHp * 1120 / 1000, mine.MaxHp);
        Assert.Equal(mine.MaxHp, mine.Hp);
        Assert.Equal(baseHp, theirs.MaxHp);
        Assert.Equal(baseDamage, w.EffectiveDamage(1, UnitKind.Rifleman));

        // a unit made later gets the bonus too
        Unit fresh = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(20), Fix64.FromInt(20)), 0);
        Assert.Equal(mine.MaxHp, fresh.MaxHp);
    }

    [Fact]
    public void DamageBonus_AddsUpOverManyShots_EvenWhenSmallerThanOnePoint()
    {
        // two riflemen shoot at identical dummy buildings for 30 s; the boosted one must have dealt about 12 % more
        int Dealt(int kitMilli)
        {
            var map = new MapGrid(32, 32);
            var w = new World(map);
            w.PlaceBuilding(BuildingKind.Hq, 2, 2, 0);
            Building target = w.PlaceBuilding(BuildingKind.Hq, 20, 20, 1);
            w.SetBuildingHealth(target.Id, 1000000);
            w.SetKitBonus(0, UnitKind.Rifleman, kitMilli);
            Unit u = w.SpawnUnit(UnitKind.Rifleman, new Vec2(Fix64.FromInt(18), Fix64.FromInt(20)), 0);
            w.Enqueue(new AttackCommand(new[] { u.Id }, AttackKind.Building, target.Id), 0);
            for (int i = 0; i < 30 * World.TicksPerSecond; i++) w.Step();
            return 1000000 - target.Hp;
        }
        int plain = Dealt(0), boosted = Dealt(120);
        Assert.True(plain > 0);
        Assert.InRange(boosted * 1000L / plain, 1090, 1150);
    }

    [Fact]
    public void KitBonus_IsPartOfTheHash_OnlyWhenUsed()
    {
        World plain = SkirmishScenario.Create(1), same = SkirmishScenario.Create(1), boosted = SkirmishScenario.Create(1);
        boosted.SetKitBonus(0, UnitKind.Tank, 80);
        Assert.Equal(plain.StateHash(), same.StateHash());
        Assert.NotEqual(plain.StateHash(), boosted.StateHash());
    }

    [Fact]
    public void BoostedMatches_StayDeterministic()
    {
        ulong Run()
        {
            World w = SkirmishScenario.Create(3);
            var a = new Armory();
            var wallet = Rich();
            a.Buy(UnitKind.Rifleman, wallet);
            a.Buy(UnitKind.Tank, wallet);
            a.Apply(w, 0);
            var ai0 = new AiPlayer(0, AiDifficulty.Medium, 3);
            var ai1 = new AiPlayer(1, AiDifficulty.Medium, 4);
            var buf = new List<Command>();
            for (int i = 0; i < 20 * 120; i++)
            {
                buf.Clear(); ai0.Update(w, buf); foreach (Command c in buf) w.Enqueue(c, 0);
                buf.Clear(); ai1.Update(w, buf); foreach (Command c in buf) w.Enqueue(c, 1);
                w.Step();
            }
            return w.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
