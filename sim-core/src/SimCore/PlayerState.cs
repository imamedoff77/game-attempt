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
        /// <summary>Resource-ticks collected towards the next whole resource.</summary>
        internal int IncomeProgress;
    }
}
