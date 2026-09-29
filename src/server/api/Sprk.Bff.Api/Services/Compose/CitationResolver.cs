using System.Globalization;
using System.Text;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-18 (task 042, WS-4) — the CITATION RESOLVER: maps a human legal citation string ↔ the exact
/// paragraph <c>w14:paraId</c>(s), built ON TOP of the per-paragraph reference map WS-4 already produces
/// (<see cref="ParaIdMapEntry"/> from task 040 on the projection payload; <see cref="ParaReferenceMapEntry"/>
/// from task 041 on the session ledger). It does NOT recompute numbering — it reads the already-computed
/// <see cref="ParaIdMapEntry.ComputedNumber"/> / <see cref="ParaIdMapEntry.ListPath"/> chain and matches a
/// parsed citation against it. Covers the three reference shapes the project's locked WS-4 decision requires:
/// <list type="bullet">
///   <item><b>Single label</b> — <c>"Section 4.2"</c> / <c>"4.2"</c> / <c>"§ 4.2"</c> → the paragraph whose
///   ordinal chain is <c>[4, 2]</c>.</item>
///   <item><b>Sub-item depth</b> — <c>"4.2(b)(iii)"</c> → the ordinal path <c>[4, 2, 2, 3]</c> (<c>b</c> = 2nd
///   lower-letter, <c>iii</c> = 3rd lower-roman) → the paragraph whose <see cref="ParaIdMapEntry.ListPath"/>
///   equals it. Decimal sub-items (<c>"1.1.1"</c> → <c>[1, 1, 1]</c>) resolve through the same path parse.</item>
///   <item><b>Contiguous range</b> — <c>"Sections 4–7"</c> / <c>"4-7"</c> → every citable paragraph whose
///   TOP-LEVEL ordinal is in <c>[4..7]</c> inclusive (including their sub-items), in document order.</item>
/// </list>
///
/// <para>
/// <b>Consumer contract (spec Unresolved Question — "Citation-model API shape" — resolved here).</b> The
/// analysis/citation tool already holds the projection (or the session's <see cref="ChatSession.ReferenceMap"/>);
/// it consumes this resolver as a PURE STATIC function over that map — there is NO new endpoint, NO DI
/// injection, and NO AI-internal type reaching <c>Services/Compose/</c> (ADR-013 Tier-1). BOTH stores are
/// supported: the projection-payload half via the <see cref="ParaIdMapEntry"/> overloads and the session-ledger
/// half via the <see cref="ParaReferenceMapEntry"/> overloads (task 041 note — read from whichever is in hand).
/// The result carries the matched <see cref="CitationTarget"/>(s); an unresolvable citation returns an EXPLICIT
/// not-found (empty <see cref="CitationResolution.Matches"/>, <see cref="CitationResolution.IsResolved"/> =
/// <c>false</c>) — never a fabricated or nearest-guess <c>paraId</c>, and never a throw.
/// </para>
///
/// <para>
/// <b>THIS TYPE HAS A CLIENT MIRROR — changes here must land there too (#699, 2026-09-01).</b>
/// <c>src/client/shared/Spaarke.Compose.Components/src/widgets/composeCitationResolver.ts</c> reimplements
/// the FORWARD half of this resolver in TypeScript (a browser cannot call a C# static, and a
/// resolve-citation endpoint would add per-finding latency for data the projection payload already
/// carries). The mirror is what <c>ComposeEditor.placeAdvisoryComments</c> anchors advisory review
/// findings with, so a shape this parser understands and the mirror does not makes that finding fall
/// through to TEXT search — the wrong-clause anchoring #699 exists to close. Two mechanisms enforce it:
/// the shared corpus <c>tests/fixtures/compose-citation-parity/cases.json</c>, executed by BOTH parsers
/// (<c>ComposeCitationParityCorpusTests</c> + <c>composeCitationResolver.parity.test.ts</c>), and the
/// source-level drift detector <c>tests/Spaarke.ArchTests/ComposeCitationResolverParityGuardTests.cs</c>,
/// which pins <see cref="CitationShape"/>, the leading-label vocabulary and the range separators.
/// ADDING A CITATION SHAPE MEANS ADDING A CASE TO THE SHARED CORPUS.
/// </para>
///
/// <para>
/// <b>Pure (ADR-007/013).</b> No I/O, no <c>Microsoft.Graph</c>, no model call, no router — string parse +
/// integer-chain matching over the in-memory reference map. Stateless; safe as a shared utility.
/// <see cref="ParaReferenceMapEntry"/> lives in <c>Models/Ai/Chat</c>, a namespace <c>Services/Compose</c>
/// already depends on (task 041) — no new dependency direction is introduced.
/// </para>
/// </summary>
public static class CitationResolver
{
    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Public API — citation string → paraId(s)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolves <paramref name="citation"/> against the projection-payload reference map
    /// (<see cref="ComposeDocxProjection.ParaIdMap"/>). Total — never throws for any input string.
    /// </summary>
    public static CitationResolution Resolve(string? citation, IReadOnlyList<ParaIdMapEntry> referenceMap)
        => ResolveCore(citation, Normalize(referenceMap));

    /// <summary>
    /// Resolves <paramref name="citation"/> against the session-ledger reference map
    /// (<see cref="ChatSession.ReferenceMap"/>). Total — never throws for any input string.
    /// </summary>
    public static CitationResolution Resolve(string? citation, IReadOnlyList<ParaReferenceMapEntry> referenceMap)
        => ResolveCore(citation, Normalize(referenceMap));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Public API — reverse: paraId → its canonical human citation number (bidirectional bonus)
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reverse resolution: given a <paramref name="paraId"/>, returns its canonical citation number
    /// (the normalized computed label, e.g. <c>"4.2"</c> — trailing punctuation trimmed), or <c>null</c>
    /// when the paragraph is unknown or is NOT citable (unnumbered / bullet glyph). Total — never throws.
    /// </summary>
    public static string? ResolveCitation(string? paraId, IReadOnlyList<ParaIdMapEntry> referenceMap)
        => ResolveCitationCore(paraId, Normalize(referenceMap));

    /// <summary>Reverse resolution over the session-ledger map. See the <see cref="ParaIdMapEntry"/> overload.</summary>
    public static string? ResolveCitation(string? paraId, IReadOnlyList<ParaReferenceMapEntry> referenceMap)
        => ResolveCitationCore(paraId, Normalize(referenceMap));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Core resolution
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static CitationResolution ResolveCore(string? citation, IReadOnlyList<CitationTarget> targets)
    {
        if (string.IsNullOrWhiteSpace(citation))
        {
            return CitationResolution.NotFound(citation ?? string.Empty, CitationShape.Unrecognized);
        }

        var query = citation.Trim();
        var parsed = CitationParser.Parse(query);

        switch (parsed.Shape)
        {
            case CitationShape.Range:
                {
                    var matches = targets
                        .Where(t => IsCitable(t) && t.ListPath.Count >= 1
                                    && t.ListPath[0] >= parsed.RangeLow && t.ListPath[0] <= parsed.RangeHigh)
                        .OrderBy(t => t.Index)
                        .ToArray();
                    return new CitationResolution(query, CitationShape.Range, matches);
                }

            case CitationShape.Single:
            case CitationShape.SubItem:
                {
                    // A citation parses to one or more CANDIDATE ordinal paths (a single ambiguous sub-item
                    // token such as "(i)" yields both a letter and a roman candidate — see CitationParser).
                    // A paragraph matches when its raw ListPath equals ANY candidate path exactly.
                    var matches = targets
                        .Where(t => IsCitable(t)
                                    && parsed.Paths.Any(path => PathEquals(t.ListPath, path)))
                        .OrderBy(t => t.Index)
                        .ToArray();
                    return new CitationResolution(query, parsed.Shape, matches);
                }

            default:
                return CitationResolution.NotFound(query, CitationShape.Unrecognized);
        }
    }

    private static string? ResolveCitationCore(string? paraId, IReadOnlyList<CitationTarget> targets)
    {
        if (string.IsNullOrWhiteSpace(paraId))
        {
            return null;
        }

        var target = targets.FirstOrDefault(t =>
            string.Equals(t.ParaId, paraId, StringComparison.OrdinalIgnoreCase));

        if (target.ParaId is null || !IsCitable(target))
        {
            return null;
        }

        return NormalizeLabel(target.ComputedNumber);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Citability + matching helpers
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// A paragraph is CITABLE by number only when it carries a computed NUMERIC legal label (task 040 note:
    /// a bullet glyph carries a non-numeric <see cref="ParaIdMapEntry.ComputedNumber"/> but a numeric
    /// <see cref="ParaIdMapEntry.ListPath"/> — it must NOT be reachable by a section/range citation). We gate
    /// on the label starting with an ASCII digit, so bullets and unnumbered paragraphs are excluded.
    /// </summary>
    private static bool IsCitable(CitationTarget target)
    {
        if (target.ListPath.Count == 0)
        {
            return false;
        }

        var label = target.ComputedNumber?.TrimStart();
        return !string.IsNullOrEmpty(label) && char.IsAsciiDigit(label![0]);
    }

    private static bool PathEquals(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Trims trailing/leading whitespace and trailing dots from a computed label ("4.2." → "4.2").</summary>
    private static string NormalizeLabel(string? label)
        => (label ?? string.Empty).Trim().TrimEnd('.');

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // Normalization of the two source record shapes onto a single internal target
    // ═════════════════════════════════════════════════════════════════════════════════════════

    private static IReadOnlyList<CitationTarget> Normalize(IReadOnlyList<ParaIdMapEntry> map)
    {
        if (map.Count == 0)
        {
            return Array.Empty<CitationTarget>();
        }

        var targets = new List<CitationTarget>(map.Count);
        for (var i = 0; i < map.Count; i++)
        {
            var e = map[i];
            targets.Add(new CitationTarget(
                e.ParaId,
                e.Index,
                e.ComputedNumber,
                e.ListPath ?? Array.Empty<int>()));
        }

        return targets;
    }

    private static IReadOnlyList<CitationTarget> Normalize(IReadOnlyList<ParaReferenceMapEntry> map)
    {
        if (map.Count == 0)
        {
            return Array.Empty<CitationTarget>();
        }

        // ParaReferenceMapEntry carries no Index field — the ledger list IS document order (task 041,
        // built 1:1 from the ordered ParaIdMap), so the list position is the document-order index.
        var targets = new List<CitationTarget>(map.Count);
        for (var i = 0; i < map.Count; i++)
        {
            var e = map[i];
            targets.Add(new CitationTarget(
                e.ParaId,
                i,
                e.ComputedNumber,
                e.ListPath ?? Array.Empty<int>()));
        }

        return targets;
    }
}

/// <summary>The three legal-citation shapes FR-18 resolves, plus the unrecognized/malformed sentinel.</summary>
public enum CitationShape
{
    /// <summary>A single label — "Section 4.2" / "4.2" / "4".</summary>
    Single,

    /// <summary>Sub-item depth — "4.2(b)(iii)".</summary>
    SubItem,

    /// <summary>A contiguous top-level range — "Sections 4–7".</summary>
    Range,

    /// <summary>The input parsed to no recognizable citation (malformed / empty) — always a not-found result.</summary>
    Unrecognized,
}

/// <summary>
/// One resolved paragraph target: its <see cref="ParaId"/>, document-order <see cref="Index"/>, the computed
/// legal label (<see cref="ComputedNumber"/>), and the raw ordinal <see cref="ListPath"/> chain it matched on.
/// A value type over the reference-map fields — no document text.
/// </summary>
public readonly record struct CitationTarget(
    string ParaId,
    int Index,
    string? ComputedNumber,
    IReadOnlyList<int> ListPath);

/// <summary>
/// The outcome of resolving one citation string: the <see cref="Query"/> echoed, the detected
/// <see cref="Shape"/>, and the ordered <see cref="Matches"/> (empty ⇒ explicit not-found). Pure data.
/// </summary>
public sealed record CitationResolution(
    string Query,
    CitationShape Shape,
    IReadOnlyList<CitationTarget> Matches)
{
    /// <summary>True when the citation resolved to at least one paragraph. Zero matches is a VALID answer, not an error.</summary>
    public bool IsResolved => Matches.Count > 0;

    /// <summary>The matched paragraph <c>w14:paraId</c>(s), in document order (empty for a not-found result).</summary>
    public IReadOnlyList<string> ParaIds => Matches.Select(m => m.ParaId).ToArray();

    /// <summary>A not-found result for <paramref name="query"/> (empty matches; <paramref name="shape"/> preserved).</summary>
    public static CitationResolution NotFound(string query, CitationShape shape)
        => new(query, shape, Array.Empty<CitationTarget>());
}

/// <summary>
/// The parse of one citation string into either a contiguous top-level range, or one-or-more candidate ordinal
/// PATHS (a single ambiguous sub-item token yields both a letter and a roman candidate). Internal to the
/// resolver — the public surface is <see cref="CitationResolver"/> + <see cref="CitationResolution"/>.
/// </summary>
internal static class CitationParser
{
    internal readonly record struct ParsedCitation(
        CitationShape Shape,
        IReadOnlyList<IReadOnlyList<int>> Paths,
        int RangeLow,
        int RangeHigh)
    {
        public static ParsedCitation Unrecognized { get; } =
            new(CitationShape.Unrecognized, Array.Empty<IReadOnlyList<int>>(), 0, 0);
    }

    // Leading citation words / marks we tolerate + strip: "Section(s)", "Article(s)", "Clause(s)",
    // "Paragraph(s)"/"Para", the section sign "§"/"§§". Case-insensitive; any surrounding whitespace collapsed.
    private static readonly string[] LeadingLabelWords =
    {
        "sections", "section", "articles", "article", "clauses", "clause",
        "paragraphs", "paragraph", "paras", "para",
    };

    internal static ParsedCitation Parse(string raw)
    {
        var s = CollapseWhitespace(raw).Trim();
        if (s.Length == 0)
        {
            return ParsedCitation.Unrecognized;
        }

        s = StripLeadingLabel(s);
        if (s.Length == 0)
        {
            return ParsedCitation.Unrecognized;
        }

        // ── RANGE: "<lo> <dash> <hi>" at the TOP level (hyphen / en-dash / em-dash), digits only. ──
        if (TryParseRange(s, out var lo, out var hi))
        {
            // A descending or degenerate "7-4" is normalized to an ascending span so [lo..hi] is inclusive.
            if (lo > hi)
            {
                (lo, hi) = (hi, lo);
            }

            return new ParsedCitation(CitationShape.Range, Array.Empty<IReadOnlyList<int>>(), lo, hi);
        }

        // ── SINGLE / SUB-ITEM: leading dotted-decimal base, then zero+ parenthesized sub-item tokens. ──
        return ParseOrdinalPath(s);
    }

    private static bool TryParseRange(string s, out int low, out int high)
    {
        low = 0;
        high = 0;

        var dashIndex = -1;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] is '-' or '–' or '—') // hyphen-minus, en dash, em dash
            {
                dashIndex = i;
                break;
            }
        }

        if (dashIndex <= 0 || dashIndex >= s.Length - 1)
        {
            return false;
        }

        var left = s[..dashIndex].Trim();
        var right = s[(dashIndex + 1)..].Trim();

        return IsAllDigits(left) && IsAllDigits(right)
            && int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out low)
            && int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out high);
    }

    private static ParsedCitation ParseOrdinalPath(string s)
    {
        var i = 0;

        // 1) Leading dotted-decimal base ("4", "4.2", "1.1.1", optional trailing dot).
        var basePath = new List<int>();
        while (i < s.Length && char.IsAsciiDigit(s[i]))
        {
            var start = i;
            while (i < s.Length && char.IsAsciiDigit(s[i]))
            {
                i++;
            }

            basePath.Add(int.Parse(s[start..i], CultureInfo.InvariantCulture));

            if (i < s.Length && s[i] == '.')
            {
                i++; // consume the level separator; a trailing dot before '(' or EOS is tolerated.
            }
        }

        if (basePath.Count == 0)
        {
            return ParsedCitation.Unrecognized; // no leading number ⇒ not a citation we resolve.
        }

        // 2) Zero-or-more parenthesized sub-item tokens: "(b)", "(iii)", "(12)". Each token expands to a
        //    set of candidate ordinals; the running candidate paths are the cartesian product.
        var hadSubItem = false;
        IReadOnlyList<IReadOnlyList<int>> paths = new List<IReadOnlyList<int>> { basePath };

        while (i < s.Length)
        {
            // Tolerate incidental whitespace between tokens.
            if (char.IsWhiteSpace(s[i]))
            {
                i++;
                continue;
            }

            if (s[i] != '(')
            {
                return ParsedCitation.Unrecognized; // trailing junk ⇒ not a clean citation.
            }

            var close = s.IndexOf(')', i + 1);
            if (close < 0)
            {
                return ParsedCitation.Unrecognized; // unbalanced parenthesis.
            }

            var token = s[(i + 1)..close].Trim();
            var candidates = SubItemTokenOrdinals(token);
            if (candidates.Count == 0)
            {
                return ParsedCitation.Unrecognized; // token was neither number, letter, nor roman.
            }

            hadSubItem = true;
            paths = Expand(paths, candidates);
            i = close + 1;
        }

        var shape = hadSubItem ? CitationShape.SubItem : CitationShape.Single;
        return new ParsedCitation(shape, paths, 0, 0);
    }

    /// <summary>
    /// Expands each parsed sub-item token into its candidate ordinal value(s):
    /// <list type="bullet">
    ///   <item>all-digits → that integer.</item>
    ///   <item>multi-char valid roman numeral ("ii", "iii", "iv") → the roman value (legal outlines use
    ///   roman at deeper levels).</item>
    ///   <item>single alpha char → the lower-letter ordinal (<c>a</c>=1..<c>z</c>=26) PLUS, when the char is
    ///   also a roman digit (i, v, x, l, c, d, m), the roman value as an ALTERNATE candidate — the resolver
    ///   keeps whichever actually matches a paragraph; ties favor the letter reading.</item>
    ///   <item>multi-char non-roman alpha ("aa") → the bijective base-26 letter ordinal.</item>
    /// </list>
    /// </summary>
    private static IReadOnlyList<int> SubItemTokenOrdinals(string token)
    {
        if (token.Length == 0)
        {
            return Array.Empty<int>();
        }

        if (IsAllDigits(token))
        {
            return new[] { int.Parse(token, CultureInfo.InvariantCulture) };
        }

        if (!IsAllAsciiLetters(token))
        {
            return Array.Empty<int>();
        }

        var letter = LetterToOrdinal(token);
        var roman = TryRomanToOrdinal(token);

        if (token.Length == 1)
        {
            // Ambiguous single char (e.g. "i"): letter primary, roman alternate when distinct.
            return roman.HasValue && roman.Value != letter
                ? new[] { letter, roman.Value }
                : new[] { letter };
        }

        // Multi-char: a valid roman numeral reads as roman; otherwise a bijective base-26 letter run.
        return roman.HasValue ? new[] { roman.Value } : new[] { letter };
    }

    private static IReadOnlyList<IReadOnlyList<int>> Expand(
        IReadOnlyList<IReadOnlyList<int>> paths, IReadOnlyList<int> candidates)
    {
        var expanded = new List<IReadOnlyList<int>>(paths.Count * candidates.Count);
        foreach (var path in paths)
        {
            foreach (var candidate in candidates)
            {
                var next = new List<int>(path.Count + 1);
                next.AddRange(path);
                next.Add(candidate);
                expanded.Add(next);
            }
        }

        return expanded;
    }

    // ── token decoders ─────────────────────────────────────────────────────────────────────────

    /// <summary>Bijective base-26: "a"→1, "z"→26, "aa"→27. Case-insensitive.</summary>
    private static int LetterToOrdinal(string token)
    {
        var value = 0;
        foreach (var ch in token)
        {
            value = value * 26 + (char.ToLowerInvariant(ch) - 'a' + 1);
        }

        return value;
    }

    /// <summary>
    /// Parses a canonical roman numeral (case-insensitive) to its integer value, or <c>null</c> when the
    /// token is not a valid roman numeral. Validates by re-encoding and comparing, so "iiii"/"vv" reject.
    /// </summary>
    private static int? TryRomanToOrdinal(string token)
    {
        var lower = token.ToLowerInvariant();
        var total = 0;
        var prevValue = 0;

        for (var i = lower.Length - 1; i >= 0; i--)
        {
            var value = lower[i] switch
            {
                'i' => 1,
                'v' => 5,
                'x' => 10,
                'l' => 50,
                'c' => 100,
                'd' => 500,
                'm' => 1000,
                _ => -1,
            };

            if (value < 0)
            {
                return null;
            }

            if (value < prevValue)
            {
                total -= value;
            }
            else
            {
                total += value;
                prevValue = value;
            }
        }

        if (total <= 0)
        {
            return null;
        }

        // Canonical-form check: the parsed value must re-encode to the same token (rejects "iiii", "im", ...).
        return string.Equals(ToRoman(total), lower, StringComparison.Ordinal) ? total : null;
    }

    private static string ToRoman(int value)
    {
        (int Value, string Numeral)[] table =
        {
            (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"),
            (100, "c"), (90, "xc"), (50, "l"), (40, "xl"),
            (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i"),
        };

        var sb = new StringBuilder();
        foreach (var (v, numeral) in table)
        {
            while (value >= v)
            {
                sb.Append(numeral);
                value -= v;
            }
        }

        return sb.ToString();
    }

    // ── string helpers ───────────────────────────────────────────────────────────────────────

    private static string StripLeadingLabel(string s)
    {
        var changed = true;
        while (changed)
        {
            changed = false;

            // Strip leading section signs "§" / "§§".
            var t = s.TrimStart();
            while (t.StartsWith('§'))
            {
                t = t[1..].TrimStart();
                changed = true;
            }

            foreach (var word in LeadingLabelWords)
            {
                if (t.Length >= word.Length
                    && t[..word.Length].Equals(word, StringComparison.OrdinalIgnoreCase)
                    && (t.Length == word.Length || !char.IsLetter(t[word.Length])))
                {
                    t = t[word.Length..].TrimStart();
                    changed = true;
                    break;
                }
            }

            s = t;
        }

        return s;
    }

    private static string CollapseWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                sb.Append(' ');
                pendingSpace = false;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static bool IsAllDigits(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (var ch in s)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllAsciiLetters(string s)
    {
        if (s.Length == 0)
        {
            return false;
        }

        foreach (var ch in s)
        {
            if (!char.IsAsciiLetter(ch))
            {
                return false;
            }
        }

        return true;
    }
}
