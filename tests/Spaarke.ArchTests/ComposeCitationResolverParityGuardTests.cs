// #699 — the CITATION-RESOLVER PARITY DRIFT DETECTOR.
//
// THE SITUATION IT GUARDS. `composeCitationResolver.ts` is a hand-written client mirror of the server
// `CitationResolver.cs`. The mirror is the right call and its own header argues why (there is no way to
// call a pure C# function from a browser, and a `resolve-citation` endpoint would add per-finding
// latency for data the projection payload already carries). What the mirror lacked was any mechanism
// keeping the two in step: parity rested on ported test cases and `@see` comments, so a shape added to
// the server parser and not to the client would ship green.
//
// WHY THAT IS AN ANCHORING DEFECT AND NOT A TIDINESS ONE. `ComposeEditor.placeAdvisoryComments` tries
// the DETERMINISTIC leg first — the review model's `sectionRef` -> `resolveCitation` -> paraId -> that
// paragraph's live span. When the client cannot parse a citation the server can, the leg returns null
// and the finding falls through to TEXT search, which is where an advisory note can attach to the wrong
// clause. The gap does not degrade into "slightly less precise": it reopens #699 for exactly the
// findings that were cited precisely enough to be safe.
//
// TWO MECHANISMS, DELIBERATELY DIFFERENT IN KIND:
//
//   1. BEHAVIOUR — `tests/fixtures/compose-citation-parity/cases.json`, executed by BOTH resolvers
//      (`ComposeCitationParityCorpusTests.cs` + `composeCitationResolver.parity.test.ts`). Catches drift
//      in what the parsers DO for the shapes already covered.
//
//   2. SURFACE — this file. Catches drift in what they KNOW: a vocabulary word or a shape added on one
//      side only. A behavioural corpus cannot catch that by itself, because nobody adding a shape to
//      one parser is obliged to add a case for it. This rule makes them notice.
//
// Neither is sufficient alone, which is the reason both exist. This one reads SOURCE — the TypeScript
// half has no CLR representation, so there is nothing else to read.
//
// KEEP path: tests/Spaarke.ArchTests/** (ADR-038 Amendment A1) — a structural fitness function. Per
// tests/CLAUDE.md's authoring rules for this path, the naming and setup-ratio heuristics do not apply,
// and every rule carries a negative control (fires on a seeded violation) and a positive control (does
// NOT fire on the sanctioned shape).

using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

public class ComposeCitationResolverParityGuardTests
{
    private const string ServerRelativePath =
        "src/server/api/Sprk.Bff.Api/Services/Compose/CitationResolver.cs";

    private const string ClientRelativePath =
        "src/client/shared/Spaarke.Compose.Components/src/widgets/composeCitationResolver.ts";

    // =================================================================================================
    // The detectors, as pure functions over source text — so the controls below can exercise them
    // against synthetic snippets rather than trusting that they work on the real files.
    // =================================================================================================

    /// <summary>
    /// Extracts the string literals from the block that follows <paramref name="anchor"/>, up to the
    /// block's closing brace/bracket. Deliberately lexical: it must read BOTH a C# array initializer and
    /// a TypeScript array literal, and there is no parser in this repo that reads both.
    /// </summary>
    internal static IReadOnlyList<string> ExtractQuotedListAfter(string source, string anchor)
    {
        var start = source.IndexOf(anchor, StringComparison.Ordinal);
        if (start < 0) return Array.Empty<string>();

        var open = source.IndexOfAny(new[] { '{', '[' }, start);
        if (open < 0) return Array.Empty<string>();

        var closer = source[open] == '{' ? '}' : ']';
        var close = source.IndexOf(closer, open);
        if (close < 0) return Array.Empty<string>();

        return Regex.Matches(source[open..close], @"['""]([^'""]*)['""]")
            .Select(m => m.Groups[1].Value)
            .Where(v => v.Length > 0)
            .ToList();
    }

    /// <summary>
    /// The characters a parser treats as a range separator, read out of a `s[i] is '-' or '–' or '—'`
    /// (C#) or `s[i] === '-' || …` (TS) style comparison chain within the range-parsing function.
    /// </summary>
    internal static IReadOnlySet<char> ExtractRangeDashes(string source, string functionAnchor)
    {
        var start = source.IndexOf(functionAnchor, StringComparison.Ordinal);
        if (start < 0) return new HashSet<char>();

        // A window large enough to contain the comparison chain, small enough not to swallow the next
        // function. The chain sits within a few lines of the function's opening in both files.
        var window = source[start..Math.Min(source.Length, start + 1200)];

        return Regex.Matches(window, @"'(.)'")
            .Select(m => m.Groups[1].Value[0])
            .Where(c => c is '-' or '–' or '—')
            .ToHashSet();
    }

    /// <summary>The member names of the C# <c>CitationShape</c> enum, lower-camelized to the TypeScript
    /// union's convention (<c>SubItem</c> → <c>subItem</c>) — the casing differs by language, the SET
    /// must not.</summary>
    internal static IReadOnlySet<string> ExtractServerShapes(string source)
    {
        var match = Regex.Match(source, @"enum\s+CitationShape\s*\{(?<body>[^}]*)\}");
        if (!match.Success) return new HashSet<string>(StringComparer.Ordinal);

        // Strip XML doc comments FIRST, then split on commas — deliberately NOT anchored to line starts.
        // The line-anchored version this replaces found only the first member of a single-line enum, which
        // the negative control caught: a future reformat onto fewer lines would have silently shrunk the
        // detected set and quietly disarmed the rule while it still reported green.
        var body = Regex.Replace(match.Groups["body"].Value, @"//.*?$", string.Empty, RegexOptions.Multiline);

        return body.Split(',')
            .Select(part => Regex.Match(part, @"\b(?<name>[A-Z][A-Za-z]*)\s*$"))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .Select(n => char.ToLowerInvariant(n[0]) + n[1..])
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The member names of the TypeScript <c>CitationShape</c> union.</summary>
    internal static IReadOnlySet<string> ExtractClientShapes(string source)
    {
        var match = Regex.Match(source, @"type\s+CitationShape\s*=(?<body>[^;]*);");
        if (!match.Success) return new HashSet<string>(StringComparer.Ordinal);

        return Regex.Matches(match.Groups["body"].Value, @"'(?<name>[A-Za-z]+)'")
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    // =================================================================================================
    // THE RULES
    // =================================================================================================

    [Fact(DisplayName = "#699: the citation vocabulary the two resolvers strip is identical")]
    public void TheLeadingLabelVocabularyIsIdenticalOnBothSides()
    {
        var server = ExtractQuotedListAfter(ReadRepoFile(ServerRelativePath), "LeadingLabelWords")
            .Select(w => w.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var client = ExtractQuotedListAfter(ReadRepoFile(ClientRelativePath), "LEADING_LABEL_WORDS")
            .Select(w => w.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        Assert.True(server.Count >= 8, $"Only {server.Count} label words parsed from the server resolver — " +
            "the extractor lost its anchor rather than the vocabulary shrinking. Fix the extractor.");

        var serverOnly = server.Except(client).OrderBy(w => w, StringComparer.Ordinal).ToList();
        var clientOnly = client.Except(server).OrderBy(w => w, StringComparer.Ordinal).ToList();

        Assert.True(
            serverOnly.Count == 0 && clientOnly.Count == 0,
            "The two citation resolvers no longer strip the same leading label words.\n\n"
            + "This is not cosmetic. `ComposeEditor.placeAdvisoryComments` anchors a review finding by its "
            + "`sectionRef` through the CLIENT resolver; a word the server tolerates and the client does not "
            + "makes that citation unparseable client-side, so the finding silently falls through to TEXT "
            + "search — which is the wrong-clause anchoring #699 exists to close.\n\n"
            + $"Server-only: [{string.Join(", ", serverOnly)}]\n"
            + $"Client-only: [{string.Join(", ", clientOnly)}]\n\n"
            + $"Fix by adding the word to the lagging side, and add a case to "
            + "tests/fixtures/compose-citation-parity/cases.json so the BEHAVIOUR is pinned too "
            + "(this rule only proves the vocabularies match, not that both parse it the same way).");
    }

    [Fact(DisplayName = "#699: the CitationShape vocabulary is identical on both sides")]
    public void TheCitationShapeSetIsIdenticalOnBothSides()
    {
        var server = ExtractServerShapes(ReadRepoFile(ServerRelativePath));
        var client = ExtractClientShapes(ReadRepoFile(ClientRelativePath));

        Assert.True(server.Count >= 4, $"Only {server.Count} shapes parsed from the server enum — the "
            + "extractor lost its anchor. Fix the extractor rather than the assertion.");

        Assert.True(
            server.SetEquals(client),
            "The `CitationShape` sets diverged. A shape one resolver classifies and the other does not is a "
            + "citation one side can act on and the other cannot.\n\n"
            + $"Server: [{string.Join(", ", server.OrderBy(s => s, StringComparer.Ordinal))}]\n"
            + $"Client: [{string.Join(", ", client.OrderBy(s => s, StringComparer.Ordinal))}]\n\n"
            + "A NEW shape needs four edits, and this rule catches only the first two: the server enum, the "
            + "client union, `ComposeCitationParityCorpusTests.NormalizeShape`, and at least one case in "
            + "tests/fixtures/compose-citation-parity/cases.json.");
    }

    [Fact(DisplayName = "#699: both resolvers accept the same range separators")]
    public void TheRangeSeparatorsAreIdenticalOnBothSides()
    {
        var server = ExtractRangeDashes(ReadRepoFile(ServerRelativePath), "TryParseRange");
        var client = ExtractRangeDashes(ReadRepoFile(ClientRelativePath), "tryParseRange");

        Assert.True(server.Count >= 3, $"Only {server.Count} dash characters parsed from the server range "
            + "parser — the extractor lost its anchor. Fix the extractor.");

        Assert.True(
            server.SetEquals(client),
            "The range separators diverged. \"Sections 4–7\" pasted from a Word document carries an EN DASH, "
            + "not a hyphen; a side that accepts only one of them silently fails to resolve the citation and "
            + "the finding falls back to text search.\n\n"
            + $"Server: [{string.Join(" ", server.Select(c => $"U+{(int)c:X4}"))}]\n"
            + $"Client: [{string.Join(" ", client.Select(c => $"U+{(int)c:X4}"))}]");
    }

    // =================================================================================================
    // CONTROLS — per tests/CLAUDE.md, a detector nobody has seen fail is a detector nobody knows works.
    // =================================================================================================

    [Fact(DisplayName = "#699 negative control: each extractor sees a seeded one-sided addition")]
    public void NegativeControl_TheExtractorsSeeASeededDivergence()
    {
        // Vocabulary: a word added to one side only must show up as a set difference.
        var seededServer = ExtractQuotedListAfter(
            "private static readonly string[] LeadingLabelWords = { \"section\", \"recital\", };",
            "LeadingLabelWords");
        var seededClient = ExtractQuotedListAfter(
            "const LEADING_LABEL_WORDS = [ 'section', ];",
            "LEADING_LABEL_WORDS");

        Assert.Equal(new[] { "section", "recital" }, seededServer);
        Assert.Equal(new[] { "section" }, seededClient);
        Assert.NotEqual(
            seededServer.ToHashSet(StringComparer.Ordinal).Count,
            seededClient.ToHashSet(StringComparer.Ordinal).Count);

        // Shapes: a shape added to the server enum only.
        var shapesServer = ExtractServerShapes(
            "internal enum CitationShape { Single, SubItem, Range, Exhibit, Unrecognized }");
        var shapesClient = ExtractClientShapes(
            "export type CitationShape = 'single' | 'subItem' | 'range' | 'unrecognized';");
        Assert.Contains("exhibit", shapesServer);
        Assert.False(shapesServer.SetEquals(shapesClient));

        // Range separators: a side that forgot the en/em dashes.
        var dashesRich = ExtractRangeDashes("bool TryParseRange(string s) { if (s[i] is '-' or '–' or '—') { } }", "TryParseRange");
        var dashesPoor = ExtractRangeDashes("function tryParseRange(s) { if (s[i] === '-') { } }", "tryParseRange");
        Assert.Equal(3, dashesRich.Count);
        Assert.False(dashesRich.SetEquals(dashesPoor));
    }

    [Fact(DisplayName = "#699 positive control: the extractors do not fire on the sanctioned shapes")]
    public void PositiveControl_TheExtractorsAgreeOnEquivalentSources()
    {
        // The two languages spell the SAME vocabulary differently (double vs single quotes, snake-case
        // const vs PascalCase field, trailing comma or not). A guard that flagged that would be deleted
        // rather than obeyed — which is the failure mode this control exists to prevent.
        var csharp = ExtractQuotedListAfter(
            "private static readonly string[] LeadingLabelWords =\n{\n    \"sections\", \"section\",\n};",
            "LeadingLabelWords").Select(w => w.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        var typescript = ExtractQuotedListAfter(
            "const LEADING_LABEL_WORDS = [\n  'sections',\n  'section',\n];",
            "LEADING_LABEL_WORDS").Select(w => w.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);

        Assert.True(csharp.SetEquals(typescript),
            "Equivalent vocabularies written in the two languages' own idioms must compare EQUAL.");

        // Likewise the shape sets: PascalCase enum members vs lower-camel union members.
        var shapesServer = ExtractServerShapes(
            "internal enum CitationShape\n{\n    Single,\n    SubItem,\n    Range,\n    Unrecognized,\n}");
        var shapesClient = ExtractClientShapes(
            "export type CitationShape = 'single' | 'subItem' | 'range' | 'unrecognized';");
        Assert.True(shapesServer.SetEquals(shapesClient),
            "`SubItem` and `subItem` are the same shape — the normalization must absorb the casing convention.");
    }

    // =================================================================================================

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate '{relativePath}' from '{AppContext.BaseDirectory}'. Both citation resolvers "
            + "must stay at their documented paths — this guard, the shared parity corpus and the two "
            + "resolvers' own `@see` headers all name them.");
    }
}
