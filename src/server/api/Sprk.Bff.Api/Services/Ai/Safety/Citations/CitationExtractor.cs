using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.Safety.Citations;

/// <summary>
/// Extracts and classifies legal citations from free text using compiled regular expressions.
///
/// Supported citation types and example matches:
///   <list type="table">
///     <item><term>CaseLaw</term>   <description>Smith v. Jones, 542 U.S. 296 (2004)</description></item>
///     <item><term>Statute</term>   <description>35 U.S.C. § 101  |  42 U.S.C. Section 1983</description></item>
///     <item><term>Patent</term>    <description>U.S. Patent No. 9,123,456  |  EP3456789  |  WO2021/123456</description></item>
///     <item><term>SecFiling</term> <description>Form 10-K  |  Form 8-K  |  Form 10-Q</description></item>
///     <item><term>Regulation</term><description>47 C.F.R. § 73.3999  |  21 CFR Part 312</description></item>
///   </list>
///
/// All patterns use <see cref="RegexOptions.Compiled"/> for throughput in the AI pipeline.
/// Patterns are ordered from most-specific to least-specific to avoid mis-classification.
///
/// ADR-015: this class operates on raw LLM text — it must not log the text.
/// </summary>
public static partial class CitationExtractor
{
    // =========================================================================
    // Compiled regex patterns — most-specific first
    // =========================================================================

    /// <summary>
    /// Case law: "Smith v. Jones, 542 U.S. 296 (2004)"
    /// Captures the reporter citation (volume + reporter + page) as the normalized key.
    /// Reporter abbreviations include U.S., F.3d, F.4th, F.Supp., F.Supp.2d/3d, S.Ct., L.Ed., A.2d/3d, etc.
    /// </summary>
    [GeneratedRegex(
        @"(?<volume>\d{1,4})\s+(?<reporter>[A-Z][A-Za-z0-9]*(?:\.[A-Za-z0-9]+)*\.?(?:\s*\d+d|\s*\d+th)?)\s+(?<page>\d{1,5})(?:\s*\(\w[\w\s]*\d{4}\))?",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex CaseLawPattern();

    /// <summary>
    /// US Statute (USC): "35 U.S.C. § 101"  or  "42 U.S.C. Section 1983"
    /// Also handles state codes with "§" symbol.
    /// </summary>
    [GeneratedRegex(
        @"\b(?<title>\d{1,3})\s+U\.S\.C\.?(?:\s+§{1,2}|\s+Sections?)\s*(?<section>\d[\d\-\.]*[a-z]?(?:\([a-z0-9]+\))*)",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex StatutePattern();

    /// <summary>
    /// US Patent: "U.S. Patent No. 9,123,456"  |  "US 9123456"
    /// EP Patent:  "EP3456789"  |  "EP 3 456 789"
    /// PCT/WO:     "WO2021/123456"
    /// </summary>
    [GeneratedRegex(
        @"\b(?:U\.S\.?\s+Patent\s+(?:No\.?\s*)?|US\s*)(?<us>[\d,]{5,15})\b|(?<ep>EP\s*[\d\s]{7,12})\b|(?<wo>WO\s*\d{4}[/\-]\d{4,8})\b",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex PatentPattern();

    /// <summary>
    /// SEC Filing form types: "Form 10-K", "Form 10-Q", "Form 8-K", "Form S-1", "Form 20-F", etc.
    /// The company name or CIK may follow but is not required for classification.
    /// </summary>
    [GeneratedRegex(
        @"\bForm\s+(?<formtype>10-[KQ](?:/A)?|8-K(?:/A)?|S-[14]|20-F|DEF\s+14A|SC\s+13[DG])\b",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex SecFilingPattern();

    /// <summary>
    /// Federal Regulation (CFR): "47 C.F.R. § 73.3999"  |  "21 CFR Part 312"
    /// </summary>
    [GeneratedRegex(
        @"\b(?<title>\d{1,3})\s+C\.F\.R\.?(?:\s+(?:Part|§)\s*)(?<part>\d[\d\-\.]*)",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 500)]
    private static partial Regex RegulationPattern();

    // =========================================================================
    // Pattern dispatch table
    // =========================================================================

    private sealed record PatternEntry(Regex Pattern, CitationType Type, Func<Match, string> Normalizer);

    /// <summary>
    /// Ordered dispatch table — patterns are tried in this order per text span.
    /// Statute before CaseLaw to avoid "35 U.S.C. § 101" matching as a volume/reporter/page triple.
    /// </summary>
    private static readonly IReadOnlyList<PatternEntry> Patterns =
    [
        new(StatutePattern(),   CitationType.Statute,    NormalizeStatute),
        new(PatentPattern(),    CitationType.Patent,     NormalizePatent),
        new(SecFilingPattern(), CitationType.SecFiling,  NormalizeSecFiling),
        new(RegulationPattern(),CitationType.Regulation, NormalizeRegulation),
        new(CaseLawPattern(),   CitationType.CaseLaw,    NormalizeCaseLaw),
    ];

    // =========================================================================
    // Public API
    // =========================================================================

    /// <summary>
    /// Scans <paramref name="text"/> for legal citations and returns them in order of appearance.
    /// </summary>
    /// <param name="text">LLM-generated text to scan. Must not be null.</param>
    /// <returns>
    /// Distinct citations (by <see cref="Citation.NormalizedKey"/>) in document order.
    /// Empty list when no citations are found or the text is empty.
    /// </returns>
    public static IReadOnlyList<Citation> ExtractCitations(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        // Track covered character ranges so overlapping patterns don't double-emit.
        var covered = new List<(int Start, int End)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<(int Position, Citation Citation)>();

        foreach (var entry in Patterns)
        {
            foreach (Match match in entry.Pattern.Matches(text))
            {
                if (!match.Success) continue;

                // Skip if this span is already claimed by a higher-priority pattern.
                if (IsOverlapping(covered, match.Index, match.Index + match.Length))
                    continue;

                var normalizedKey = entry.Normalizer(match);
                if (string.IsNullOrWhiteSpace(normalizedKey)) continue;

                if (!seen.Add(normalizedKey)) continue;

                covered.Add((match.Index, match.Index + match.Length));
                results.Add((match.Index, new Citation(match.Value.Trim(), entry.Type, normalizedKey)));
            }
        }

        results.Sort((a, b) => a.Position.CompareTo(b.Position));
        return results.Select(r => r.Citation).ToList().AsReadOnly();
    }

    // =========================================================================
    // Private: overlap detection
    // =========================================================================

    private static bool IsOverlapping(List<(int Start, int End)> covered, int start, int end)
    {
        foreach (var (s, e) in covered)
        {
            if (start < e && end > s)
                return true;
        }
        return false;
    }

    // =========================================================================
    // Private: normalizers
    // =========================================================================

    private static string NormalizeCaseLaw(Match m)
    {
        // Canonical form: "{volume} {reporter} {page}" (strips year/court parenthetical)
        var volume   = m.Groups["volume"].Value.Trim();
        var reporter = m.Groups["reporter"].Value.Trim().TrimEnd('.');
        var page     = m.Groups["page"].Value.Trim();
        return $"{volume} {reporter} {page}";
    }

    private static string NormalizeStatute(Match m)
    {
        // Canonical form: "{title} U.S.C. § {section}"
        var title   = m.Groups["title"].Value.Trim();
        var section = m.Groups["section"].Value.Trim();
        return $"{title} U.S.C. § {section}";
    }

    private static string NormalizePatent(Match m)
    {
        // US patents: strip commas and spaces → "US9123456"
        if (m.Groups["us"].Success)
            return "US" + m.Groups["us"].Value.Replace(",", "").Replace(" ", "");

        if (m.Groups["ep"].Success)
            return "EP" + Regex.Replace(m.Groups["ep"].Value, @"[^\dA-Za-z]", "");

        if (m.Groups["wo"].Success)
            return "WO" + Regex.Replace(m.Groups["wo"].Value, @"[^\dA-Za-z/]", "");

        return m.Value.Trim();
    }

    private static string NormalizeSecFiling(Match m) =>
        // Canonical form: the form type in upper case, e.g. "10-K", "8-K/A"
        m.Groups["formtype"].Value.Trim().ToUpperInvariant();

    private static string NormalizeRegulation(Match m)
    {
        // Canonical form: "{title} C.F.R. § {part}"
        var title = m.Groups["title"].Value.Trim();
        var part  = m.Groups["part"].Value.Trim();
        return $"{title} C.F.R. § {part}";
    }
}
