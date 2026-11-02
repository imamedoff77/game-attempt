using SimCore;

/// <summary>
/// Determinism self-test shared by the editor smoke test and the on-device HUD.
/// Expected values were computed outside this codebase, so a mismatch on any CPU/runtime means a desync risk.
/// </summary>
public static class SimCoreSelfTest
{
    public static bool Run(out string failure)
    {
        var rng = new DeterministicRandom(12345);
        uint[] expected = { 2555902770, 3234773579, 328846939, 3161420795, 513335584 };
        foreach (uint e in expected)
        {
            uint got = rng.NextUInt32();
            if (got != e) { failure = $"RNG: expected {e}, got {got}"; return false; }
        }

        long sqrt2 = Fix64.Sqrt(Fix64.FromInt(2)).Raw;
        if (sqrt2 != 6074000999L) { failure = $"Sqrt(2): got {sqrt2}"; return false; }

        Fix64 prod = Fix64.FromFraction(3, 4) * Fix64.FromFraction(-1, 2);
        if (prod != Fix64.FromFraction(-3, 8)) { failure = $"Mul: got {prod.Raw}"; return false; }

        var h = StateHasher.Create();
        h.Add(1); h.Add(2); h.Add(3);
        if (h.Value != 0xDA2BFB225E0D1F05UL) { failure = $"Hash: got {h.Value:X}"; return false; }

        failure = null;
        return true;
    }
}
