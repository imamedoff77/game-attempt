using System.Text;

namespace AccountServer;

/// <summary>
/// Keeps the worst words out of display names (the only free text players show to each other; chat is canned phrases).
/// Letters are lower-cased, look-alike digits and symbols are mapped back (1→i, 0→o, @→a ...), everything that is not a letter is dropped,
/// and the result is searched for the words below. A short list on purpose: it stops the obvious, it is not moderation.
/// Players can also be reported through support (see FAZA_8_PLAN.md).
/// </summary>
public static class NameFilter
{
    // Whole-name matching of longer stems; short words (3 letters) only count when they are the whole name, to avoid blocking innocent names.
    private static readonly string[] Stems =
    {
        "fuck", "shit", "bitch", "nigger", "nigga", "faggot", "whore", "slut", "asshole", "bastard", "dickhead", "cocksucker", "motherfucker",
        "rapist", "nazi", "hitler", "heil", "jihad", "kys",
        "orospu", "siktir", "gotveren", "qəhbə", "qehbe", "pezevenk", "amcik", "yarrak",
        "suka", "blyat", "pidor", "pidar", "huesos", "ebat", "mudak",
    };

    private static readonly string[] WholeOnly = { "ass", "dick", "cock", "pussy", "sex", "porn", "rape", "anus", "tit", "tits", "cunt", "cunts" };

    public static bool IsBlocked(string name)
    {
        string exact = Flatten(name, squeeze: false);
        if (exact.Length == 0) return false;
        foreach (string word in WholeOnly) if (exact == word) return true;
        string flat = Flatten(name, squeeze: true);
        foreach (string stem in Stems) if (flat.Contains(Flatten(stem, squeeze: true), StringComparison.Ordinal)) return true;
        return false;
    }

    private static string Flatten(string name, bool squeeze)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char raw in name.ToLowerInvariant())
        {
            char c = raw switch { '0' => 'o', '1' => 'i', '!' => 'i', '3' => 'e', '4' => 'a', '@' => 'a', '5' => 's', '$' => 's', '7' => 't', 'ə' => 'e', 'ı' => 'i', 'ö' => 'o', 'ü' => 'u', 'ş' => 's', 'ç' => 'c', 'ğ' => 'g', _ => raw };
            if (char.IsLetter(c) && c < 128) sb.Append(c);
        }
        if (!squeeze) return sb.ToString();
        // collapse repeats ("fuuuck" → "fuck")
        var squeezed = new StringBuilder(sb.Length);
        foreach (char c in sb.ToString()) if (squeezed.Length == 0 || squeezed[^1] != c) squeezed.Append(c);
        return squeezed.ToString();
    }
}
