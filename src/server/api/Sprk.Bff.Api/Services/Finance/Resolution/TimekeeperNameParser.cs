using System.Globalization;
using System.Text;

namespace Sprk.Bff.Api.Services.Finance.Resolution;

/// <summary>A person's name split into given names, family name and generational suffix, folded for comparison (FR-23 normalized stage).</summary>
internal sealed class ParsedName
{
    public static readonly ParsedName Empty = new([], string.Empty, string.Empty);

    public ParsedName(IReadOnlyList<string> given, string family, string suffix)
    {
        Given = given;
        Family = family;
        Suffix = suffix;
    }

    /// <summary>Given names in order, lower case and folded. A single letter is an initial.</summary>
    public IReadOnlyList<string> Given { get; }

    /// <summary>The last word of the name, lower case and folded.</summary>
    public string Family { get; }

    /// <summary>"jr", "sr", "ii", "iii", "iv" or empty. A suffix tells two people apart, so it is kept.</summary>
    public string Suffix { get; }

    /// <summary>A family name and a first given name that is a word, not an initial. Only a full name can be matched at the normalized or AI stage.</summary>
    public bool IsFullName => Family.Length > 0 && Given.Count > 0 && Given[0].Length >= 2;

    /// <summary>The name in the order the AI stage embeds it: given names, family name, suffix.</summary>
    public string Canonical => string.Join(' ', Given.Append(Family).Concat(Suffix.Length > 0 ? [Suffix] : []));
}

/// <summary>
/// Name normalization for the roster match (FR-23): case, accents, punctuation, "Last, First" versus "First Last", honorifics.
/// Pure and deterministic.
/// </summary>
/// <remarks>
/// <para><b>What is folded away:</b> case; accents (José = Jose); apostrophes (O'Brien = OBrien); full stops and hyphens (Mary-Anne =
/// Mary Anne, "J.R." = "J R"); the order "Last, First"; leading honorifics (Mr, Mrs, Ms, Miss, Mx, Dr, Prof, Professor, Sir, Dame, Hon);
/// a trailing Esq / Esquire.</para>
/// <para><b>What is kept:</b> a generational suffix (Jr, Sr, II, III, IV) because "John Smith Jr" and "John Smith Sr" are two people.</para>
/// <para><b>An initial is never guessed.</b> Two names are the same only when the family names and the first given names are equal
/// words. "J. Smith" does not match "John Smith": an initial fits "Jane" as well. Initials are expanded only through the invoice's own
/// timekeeper summary (see <see cref="TimekeeperMatcher"/>), never from the roster. Middle names may be missing on either side and may
/// be initials ("John A Smith" = "John Smith" = "John Adam Smith"; "John A Smith" is not "John B Smith").</para>
/// </remarks>
internal static class TimekeeperNameParser
{
    private static readonly HashSet<string> Honorifics = new(StringComparer.Ordinal)
    {
        "mr", "mrs", "ms", "miss", "mx", "dr", "prof", "professor", "sir", "dame", "hon",
    };

    private static readonly HashSet<string> PostNominals = new(StringComparer.Ordinal) { "esq", "esquire" };

    private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal) { "jr", "sr", "ii", "iii", "iv" };

    public static ParsedName Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ParsedName.Empty;
        }

        var cleaned = new StringBuilder(raw.Length);
        foreach (var ch in Fold(raw))
        {
            if (char.IsLetter(ch) || ch == ',')
            {
                cleaned.Append(ch);
            }
            else if (ch is '\'' or '’' or 'ʼ' or '`' or '"')
            {
                // dropped: O'Brien = OBrien
            }
            else
            {
                cleaned.Append(' ');
            }
        }

        var parts = cleaned.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return ParsedName.Empty;
        }

        // "Last, First [Middle], [Suffix]" becomes "First [Middle] Last [Suffix]". A comma before a suffix or post-nominal only
        // ("John Smith, Jr.", "Jane Doe, Esq.") is not a "Last, First" comma: the name stays in the order it was printed.
        var tokens = new List<string>();
        if (parts.Length == 2 && Words(parts[1]).All(w => Suffixes.Contains(w) || PostNominals.Contains(w)))
        {
            tokens.AddRange(Words(parts[0]));
            tokens.AddRange(Words(parts[1]));
        }
        else if (parts.Length >= 2)
        {
            // A suffix or post-nominal at the end of the "First" part ("Smith, John Jr.") belongs after the family name.
            var first = Words(parts[1]).ToList();
            var trailing = new List<string>();
            while (first.Count >= 2 && (Suffixes.Contains(first[^1]) || PostNominals.Contains(first[^1])))
            {
                trailing.Insert(0, first[^1]);
                first.RemoveAt(first.Count - 1);
            }

            tokens.AddRange(first);
            tokens.AddRange(Words(parts[0]));
            tokens.AddRange(trailing);
            for (var i = 2; i < parts.Length; i++)
            {
                tokens.AddRange(Words(parts[i]));
            }
        }
        else
        {
            tokens.AddRange(Words(parts[0]));
        }

        while (tokens.Count > 0 && Honorifics.Contains(tokens[0]))
        {
            tokens.RemoveAt(0);
        }

        while (tokens.Count > 0 && PostNominals.Contains(tokens[^1]))
        {
            tokens.RemoveAt(tokens.Count - 1);
        }

        var suffix = string.Empty;
        if (tokens.Count >= 2 && Suffixes.Contains(tokens[^1]))
        {
            suffix = tokens[^1];
            tokens.RemoveAt(tokens.Count - 1);
        }

        return tokens.Count switch
        {
            0 => ParsedName.Empty,
            1 => new ParsedName([], tokens[0], suffix),
            _ => new ParsedName(tokens.Take(tokens.Count - 1).ToArray(), tokens[^1], suffix),
        };
    }

    /// <summary>
    /// True when both are full names of the same person under the rules in the class remarks: equal family name, equal suffix, equal first
    /// given name, and compatible middle names.
    /// </summary>
    public static bool AreSameName(ParsedName a, ParsedName b)
    {
        return a.IsFullName && b.IsFullName
            && a.Family == b.Family
            && a.Suffix == b.Suffix
            && a.Given[0] == b.Given[0]
            && MiddleNamesAgree(a, b);
    }

    /// <summary>True when no middle name that both names have contradicts the other ("John A" and "John B" do; "John" and "John A" do not).</summary>
    public static bool MiddleNamesAgree(ParsedName a, ParsedName b)
    {
        var shared = Math.Min(a.Given.Count, b.Given.Count);
        for (var i = 1; i < shared; i++)
        {
            if (!MiddleNamesCompatible(a.Given[i], b.Given[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Equal, or one is the first letter of the other.</summary>
    public static bool MiddleNamesCompatible(string x, string y) =>
        x == y
        || (x.Length == 1 && y.StartsWith(x, StringComparison.Ordinal))
        || (y.Length == 1 && x.StartsWith(y, StringComparison.Ordinal));

    /// <summary>
    /// True for a label that is initials only: "DL", "JRS", "D.L.", "D. L." (two or three letters, nothing else). Returns them lower case.
    /// A capitalized word such as "Wu" or "Li" is a short family name, not initials.
    /// </summary>
    public static bool TryGetBareInitials(string? raw, out string initials)
    {
        initials = string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains(','))
        {
            return false;
        }

        var words = raw.Split([' ', '.', '-'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            var word = words[0];
            if (word.Length is >= 2 and <= 3 && word.All(c => char.IsLetter(c) && char.IsUpper(c)))
            {
                initials = Fold(word);
                return true;
            }

            return false;
        }

        if (words.Length is >= 2 and <= 3 && words.All(w => w.Length == 1 && char.IsLetter(w[0])))
        {
            initials = Fold(string.Concat(words));
            return true;
        }

        return false;
    }

    /// <summary>The initials a full name can be billed under: every given initial plus the family initial ("ima"), and first given plus family ("ia").</summary>
    public static IReadOnlyList<string> InitialsOf(ParsedName name)
    {
        if (!name.IsFullName)
        {
            return [];
        }

        var all = string.Concat(name.Given.Select(g => g[0])) + name.Family[0];
        var firstAndLast = string.Concat(name.Given[0][0], name.Family[0]);
        return all == firstAndLast ? [all] : [all, firstAndLast];
    }

    /// <summary>Levenshtein distance, stopping once it is certain to exceed <paramref name="limit"/> (then <c>limit + 1</c> is returned).</summary>
    public static int EditDistance(string a, string b, int limit)
    {
        if (Math.Abs(a.Length - b.Length) > limit)
        {
            return limit + 1;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > limit)
            {
                return limit + 1;
            }

            (previous, current) = (current, previous);
        }

        return Math.Min(previous[b.Length], limit + 1);
    }

    private static IEnumerable<string> Words(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Lower case, accents removed.</summary>
    private static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
        }

        return sb.ToString();
    }
}
