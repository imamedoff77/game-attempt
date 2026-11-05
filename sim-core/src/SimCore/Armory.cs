using System;
using System.Text;

namespace SimCore
{
    /// <summary>
    /// The player's permanent unit upgrades, bought with campaign credits. Each kind of unit has levels 0 to <see cref="MaxLevel"/>;
    /// every level adds <see cref="BonusPerLevelMilli"/> to its damage and health in matches (see <see cref="World.SetKitBonus"/>).
    /// Stored as short text ("1,0,3,...") like <see cref="CampaignProgress"/>.
    /// </summary>
    public sealed class Armory
    {
        public const int MaxLevel = 5;
        public const int BonusPerLevelMilli = 40;

        private static readonly int KindCount = Enum.GetValues(typeof(UnitKind)).Length;
        private readonly int[] _levels = new int[KindCount];

        public int Level(UnitKind kind) => _levels[(int)kind];

        public int BonusMilli(UnitKind kind) => _levels[(int)kind] * BonusPerLevelMilli;

        /// <summary>Credits the next level costs; 0 when the kind is already at the top.</summary>
        public int NextCost(UnitKind kind)
        {
            int level = _levels[(int)kind];
            if (level >= MaxLevel) return 0;
            // Heavier units cost more: infantry 30 per level step, vehicles 60, the sniper 45.
            int step = Defs.Unit(kind).Class == UnitClass.Vehicle ? 60 : kind == UnitKind.Sniper ? 45 : 30;
            return step * (level + 1);
        }

        public bool CanBuy(UnitKind kind, CampaignProgress wallet) => NextCost(kind) > 0 && wallet.Credits >= NextCost(kind);

        /// <summary>Buys the next level if there are enough credits.</summary>
        public bool Buy(UnitKind kind, CampaignProgress wallet)
        {
            int cost = NextCost(kind);
            if (cost == 0 || !wallet.Spend(cost)) return false;
            _levels[(int)kind]++;
            return true;
        }

        /// <summary>Hands the bonuses to a team at the start of a match.</summary>
        public void Apply(World world, int team)
        {
            for (int k = 0; k < KindCount; k++)
                if (_levels[k] > 0) world.SetKitBonus(team, (UnitKind)k, _levels[k] * BonusPerLevelMilli);
        }

        /// <summary>Joins another copy of the same player's armory (another device): the higher level of each kind.</summary>
        public void MergeFrom(Armory other)
        {
            for (int i = 0; i < _levels.Length; i++) _levels[i] = Math.Max(_levels[i], other._levels[i]);
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _levels.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(_levels[i]);
            }
            return sb.ToString();
        }

        public static Armory Parse(string text)
        {
            var armory = new Armory();
            if (string.IsNullOrEmpty(text)) return armory;
            string[] parts = text.Split(',');
            for (int i = 0; i < parts.Length && i < armory._levels.Length; i++)
                if (int.TryParse(parts[i], out int level)) armory._levels[i] = Math.Max(0, Math.Min(MaxLevel, level));
            return armory;
        }
    }
}
