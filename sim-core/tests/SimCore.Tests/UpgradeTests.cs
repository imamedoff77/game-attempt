using SimCore;
using Xunit;

public class UpgradeTests
{
    private static Vec2 P(int x, int y) => new Vec2(Fix64.FromInt(x), Fix64.FromInt(y));
    private static void Run(World w, int seconds) { for (int i = 0; i < seconds * World.TicksPerSecond; i++) w.Step(); }

    private static World NewWorld()
    {
        var w = new World(Fix64.FromInt(64), Fix64.FromInt(64));
        w.PlaceBuilding(BuildingKind.Hq, 10, 10, 0);
        w.PlaceBuilding(BuildingKind.Hq, 50, 50, 1);
        w.GiveResources(0, 5000);
        w.GiveResources(1, 5000);
        return w;
    }

    private static void Research(World w, UpgradeKind kind, int team = 0)
    {
        w.Enqueue(new ResearchCommand(kind), team);
        w.Step();
        Assert.True(w.Player(team).Researching, "research did not start");
        Run(w, Defs.Upgrade(kind).Seconds[w.Player(team).UpgradeTier(kind)] + 1);
    }

    [Fact]
    public void Research_CostsResources_TakesTime_ThenAddsATier()
    {
        var w = NewWorld();
        int before = w.Player(0).Resources;
        w.Enqueue(new ResearchCommand(UpgradeKind.InfantryDamage), 0);
        w.Step();
        Assert.Equal(before - Defs.Upgrade(UpgradeKind.InfantryDamage).Cost[0], w.Player(0).Resources);
        Assert.True(w.Player(0).Researching);
        Assert.Equal(0, w.Player(0).UpgradeTier(UpgradeKind.InfantryDamage));

        Run(w, 29);
        Assert.Equal(0, w.Player(0).UpgradeTier(UpgradeKind.InfantryDamage));
        Run(w, 2);
        Assert.Equal(1, w.Player(0).UpgradeTier(UpgradeKind.InfantryDamage));
        Assert.False(w.Player(0).Researching);
    }

    [Fact]
    public void OnlyOneResearchAtATime()
    {
        var w = NewWorld();
        w.Enqueue(new ResearchCommand(UpgradeKind.InfantryDamage), 0);
        w.Step();
        Assert.Equal(ResearchCheck.AlreadyResearching, w.CheckResearch(0, UpgradeKind.VehicleHealth));
    }

    [Fact]
    public void LaterTiers_NeedAHigherBaseLevel()
    {
        var w = NewWorld();
        Research(w, UpgradeKind.InfantryDamage);
        Assert.Equal(ResearchCheck.LevelTooLow, w.CheckResearch(0, UpgradeKind.InfantryDamage)); // tier 2 needs base level 2

        w.Enqueue(new UpgradeBaseCommand(), 0);
        Run(w, 41);
        Assert.Equal(ResearchCheck.Ok, w.CheckResearch(0, UpgradeKind.InfantryDamage));
    }

    [Fact]
    public void ResearchIsRefused_WithoutMoney_OrAtMaxTier()
    {
        var w = NewWorld();
        w.GiveResources(0, -w.Player(0).Resources);
        Assert.Equal(ResearchCheck.NotEnoughResources, w.CheckResearch(0, UpgradeKind.InfantryDamage));

        w.GiveResources(0, 100000);
        w.Enqueue(new UpgradeBaseCommand(), 0); Run(w, 41);
        w.Enqueue(new UpgradeBaseCommand(), 0); Run(w, 71);
        for (int t = 0; t < 3; t++) Research(w, UpgradeKind.VehicleDamage);
        Assert.Equal(ResearchCheck.MaxTier, w.CheckResearch(0, UpgradeKind.VehicleDamage));
    }

    [Fact]
    public void DamageUpgrade_RaisesDamage_ForTheOwnerOnly()
    {
        var w = NewWorld();
        Research(w, UpgradeKind.InfantryDamage);
        Assert.Equal(Defs.Combat(UnitKind.Rifleman).Damage * 11 / 10, w.EffectiveDamage(0, UnitKind.Rifleman));
        Assert.Equal(Defs.Combat(UnitKind.Rifleman).Damage, w.EffectiveDamage(1, UnitKind.Rifleman));
        Assert.Equal(Defs.Combat(UnitKind.Tank).Damage, w.EffectiveDamage(0, UnitKind.Tank)); // vehicles are not infantry
    }

    [Fact]
    public void DamageUpgrade_ShowsUpInActualCombat()
    {
        Unit Shoot(bool upgraded)
        {
            var w = NewWorld();
            if (upgraded) Research(w, UpgradeKind.VehicleDamage);
            Unit shooter = w.SpawnUnit(UnitKind.Tank, P(30, 30), 0);
            Unit target = w.SpawnUnit(UnitKind.Tank, P(34, 30), 1);
            int hp = target.Hp;
            for (int i = 0; i < 40; i++) w.Step();
            Assert.True(target.Hp < hp);
            return target;
        }
        Unit plain = Shoot(false), boosted = Shoot(true);
        Assert.True(boosted.MaxHp - boosted.Hp >= plain.MaxHp - plain.Hp);
    }

    [Fact]
    public void HealthUpgrade_RaisesLivingAndNewUnits_Proportionally()
    {
        var w = NewWorld();
        Unit living = w.SpawnUnit(UnitKind.Tank, P(30, 30), 0);
        Unit enemy = w.SpawnUnit(UnitKind.Tank, P(60, 60), 1); // far away: no fighting
        int baseHp = living.MaxHp;
        w.DamageUnit(living.Id, 100);

        Research(w, UpgradeKind.VehicleHealth);

        Assert.Equal(baseHp * 11 / 10, living.MaxHp);
        Assert.Equal(baseHp - 100 + (baseHp * 11 / 10 - baseHp), living.Hp); // the extra health is added, damage stays
        Assert.Equal(baseHp, enemy.MaxHp);

        Unit fresh = w.SpawnUnit(UnitKind.Tank, P(40, 40), 0);
        Assert.Equal(baseHp * 11 / 10, fresh.MaxHp);
        Assert.Equal(fresh.MaxHp, fresh.Hp);
    }

    [Fact]
    public void Codec_RoundTrips_AndRejectsUnknownUpgrade()
    {
        byte[] bytes = CommandCodec.Encode(new ResearchCommand(UpgradeKind.VehicleHealth));
        Assert.True(CommandCodec.TryDecode(bytes, out Command decoded));
        Assert.Equal(UpgradeKind.VehicleHealth, ((ResearchCommand)decoded).Kind);

        bytes[bytes.Length - 1] = 200;
        Assert.False(CommandCodec.TryDecode(bytes, out _));
    }

    [Fact]
    public void Upgrades_AreDeterministic()
    {
        ulong Hash()
        {
            var w = NewWorld();
            w.SpawnUnit(UnitKind.Rifleman, P(30, 30), 0);
            Research(w, UpgradeKind.InfantryHealth);
            Research(w, UpgradeKind.InfantryDamage);
            return w.StateHash();
        }
        Assert.Equal(Hash(), Hash());
    }

    [Fact]
    public void MissingUpgrade_IsRejected()
    {
        string json = System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "gamedata.json"));
        string broken = json.Replace("\"id\": \"VehicleHealth\"", "\"id\": \"VehicleDamage\"");
        var ex = Assert.Throws<GameDataException>(() => GameData.Parse(broken));
        Assert.Contains("VehicleDamage", ex.Message);
    }
}
