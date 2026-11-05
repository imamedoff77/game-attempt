namespace SimCore
{
    /// <summary>Economy of one team: stock, energy balance and income.</summary>
    public sealed class PlayerState
    {
        /// <summary>Resources in stock. Income arrives automatically (no gatherers).</summary>
        public int Resources { get; internal set; } = Economy.StartResources;
        /// <summary>Energy the base can supply (the base itself plus power plants).</summary>
        public int EnergyCapacity { get; internal set; }
        /// <summary>Energy the buildings need to work.</summary>
        public int EnergyUsed { get; internal set; }
        /// <summary>True when the buildings need more energy than the base supplies; they stop working.</summary>
        public bool EnergyShortage => EnergyUsed > EnergyCapacity;
        /// <summary>Resources per minute at the moment.</summary>
        public int IncomePerMinute { get; internal set; }
        /// <summary>Base level, 1 to <see cref="Economy.MaxBaseLevel"/>. It sets the unit limit.</summary>
        public int BaseLevel { get; internal set; } = 1;
        /// <summary>Ticks left until the base reaches the next level (0 = no upgrade under way).</summary>
        public int UpgradeTicksLeft { get; internal set; }
        public bool Upgrading => UpgradeTicksLeft > 0;
        /// <summary>Finished tiers of every <see cref="UpgradeKind"/>.</summary>
        internal readonly int[] UpgradeTiers = new int[Defs.UpgradeCount];
        public int UpgradeTier(UpgradeKind kind) => UpgradeTiers[(int)kind];
        /// <summary>Permanent bonus (thousandths) to the damage and health of each kind of unit, from the player's armory. Set when the match starts.</summary>
        internal readonly int[] KitBonusMilli = new int[System.Enum.GetValues(typeof(UnitKind)).Length];
        public int KitBonus(UnitKind kind) => KitBonusMilli[(int)kind];
        /// <summary>The upgrade being researched (only meaningful while <see cref="Researching"/>).</summary>
        public UpgradeKind ResearchKind { get; internal set; }
        public int ResearchTicksLeft { get; internal set; }
        public bool Researching => ResearchTicksLeft > 0;
        /// <summary>Places taken by living units and by units waiting in production queues.</summary>
        public int PopUsed { get; internal set; }
        /// <summary>The team gave up (<see cref="SurrenderCommand"/>): the match is lost for it.</summary>
        public bool Surrendered { get; internal set; }
        public int PopCap => Economy.PopCap(BaseLevel);
        /// <summary>Resource-ticks collected towards the next whole resource.</summary>
        internal int IncomeProgress;
    }
}
