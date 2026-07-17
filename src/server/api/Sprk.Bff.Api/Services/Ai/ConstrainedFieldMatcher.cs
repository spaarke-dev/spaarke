using System.Globalization;
using System.Text;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// The pure, deterministic match ladder at the heart of <see cref="ConstrainedFieldResolver"/> — the
/// genuinely-new piece of spec FR-B1. Given a proposed value and a closed candidate set, it returns the best
/// candidate and a confidence, with NO I/O and NO model call, so it is exhaustively unit-testable.
/// </summary>
/// <remarks>
/// Ladder (first tier that hits wins): <br/>
/// 1. <b>exact</b> — case-insensitive equality of trimmed strings → <see cref="ResolutionConfidence.High"/>. <br/>
/// 2. <b>normalized</b> — equality after lowercase / punctuation-fold / whitespace-collapse / synonyms →
///    <see cref="ResolutionConfidence.High"/>. <br/>
/// 3. <b>fuzzy</b> — max normalized Levenshtein similarity ≥ <see cref="ConstrainedFieldMatchOptions.FuzzyThreshold"/>
///    → <see cref="ResolutionConfidence.Low"/>. <br/>
/// 4. <b>none</b> — otherwise → <see cref="ResolutionConfidence.None"/>.
/// </remarks>
public static class ConstrainedFieldMatcher
{
    /// <summary>The best candidate for a proposed value plus the tier-derived confidence.</summary>
    public readonly record struct FieldMatch(FieldCandidate? Best, ResolutionConfidence Confidence)
    {
        public static readonly FieldMatch NoMatch = new(null, ResolutionConfidence.None);
    }

    /// <summary>
    /// Run the deterministic ladder. Returns <see cref="FieldMatch.NoMatch"/> when the proposal is blank,
    /// the candidate set is empty, or no tier reaches the fuzzy threshold.
    /// </summary>
    public static FieldMatch Match(
        string? proposedValue,
        IReadOnlyList<FieldCandidate> candidates,
        ConstrainedFieldMatchOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(proposedValue) || candidates is null || candidates.Count == 0)
        {
            return FieldMatch.NoMatch;
        }

        options ??= new ConstrainedFieldMatchOptions();

        var proposedTrimmed = proposedValue.Trim();

        // Tier 1 — exact (case-insensitive).
        foreach (var candidate in candidates)
        {
            if (string.Equals(candidate.Label?.Trim(), proposedTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                return new FieldMatch(candidate, ResolutionConfidence.High);
            }
        }

        // Tier 2 — normalized equality.
        var proposedNorm = Normalize(proposedTrimmed, options.Synonyms);
        if (proposedNorm.Length > 0)
        {
            foreach (var candidate in candidates)
            {
                if (Normalize(candidate.Label, options.Synonyms) == proposedNorm)
                {
                    return new FieldMatch(candidate, ResolutionConfidence.High);
                }
            }
        }

        // Tier 3 — fuzzy (best normalized similarity above threshold).
        FieldCandidate? bestCandidate = null;
        var bestSimilarity = 0.0;
        foreach (var candidate in candidates)
        {
            var candidateNorm = Normalize(candidate.Label, options.Synonyms);
            var similarity = Similarity(proposedNorm, candidateNorm);
            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestCandidate = candidate;
            }
        }

        if (bestCandidate is not null && bestSimilarity >= options.FuzzyThreshold)
        {
            return new FieldMatch(bestCandidate, ResolutionConfidence.Low);
        }

        // Tier 4 — none.
        return FieldMatch.NoMatch;
    }

    /// <summary>
    /// Deterministic normalization: lowercase, fold every non-alphanumeric character to a single space,
    /// collapse runs of whitespace, then apply the optional synonym map. Culture-invariant.
    /// </summary>
    internal static string Normalize(string? value, IReadOnlyDictionary<string, string>? synonyms)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lowered = value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);
        var lastWasSpace = false;
        foreach (var ch in lowered)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                // Fold any punctuation/separator run to a single space.
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        var normalized = builder.ToString().Trim();

        if (synonyms is not null && synonyms.TryGetValue(normalized, out var canonical))
        {
            return canonical;
        }

        return normalized;
    }

    /// <summary>Normalized Levenshtein similarity in [0,1]: <c>1 - distance / max(len)</c>. Two empty strings → 0.</summary>
    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 0.0;
        }

        var distance = LevenshteinDistance(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return maxLen == 0 ? 0.0 : 1.0 - ((double)distance / maxLen);
    }

    /// <summary>Classic two-row Levenshtein edit distance. O(a·b) time, O(min) space.</summary>
    private static int LevenshteinDistance(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }
        if (b.Length == 0)
        {
            return a.Length;
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
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
