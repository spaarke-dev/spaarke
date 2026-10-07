// The SERVER half of the shared citation-parity corpus (#699, 2026-09-01).
//
// WHAT THIS ADDS OVER ComposeCitationResolverSeamTests. That suite proves the server resolver is
// correct. Its client mirror (`composeCitationResolver.ts`) had its own suite, whose parity cases were
// TRANSCRIBED from this one by hand. Two hand-kept copies of the same expectations cannot detect drift
// BETWEEN them: change one parser without touching the other and both suites stay green, because each
// only ever checks its own copy. This test and its TypeScript twin
// (`src/client/shared/Spaarke.Compose.Components/src/widgets/composeCitationResolver.parity.test.ts`)
// execute the SAME file — `tests/fixtures/compose-citation-parity/cases.json` — so a divergence fails
// on whichever side lags.
//
// WHY A PARSER GAP IS AN ANCHORING DEFECT, which is what #699 is actually about.
// `ComposeEditor.placeAdvisoryComments` tries the DETERMINISTIC leg first: the review model's
// `sectionRef` -> `resolveCitation` -> paraId -> the paragraph's live span. When the client parser
// cannot handle a citation shape the server can, that leg returns null and the finding falls through
// to TEXT search -- which is where an advisory note can land on the wrong clause. So the gap does not
// degrade gracefully into "slightly less precise"; it reopens #699 through a side door, and it does so
// precisely for the findings that were cited well enough to be safe.
//
// KEEP path: tests/integration/seam/** -- a vertical-slice seam across two runtimes that must agree.
// The seam here is not a dispatch spine but a CONTRACT between a C# parser and a TypeScript one, and
// the only way to test it is to make both read one artifact.
//
// Banned-pattern compliance (ADR-038 §4): no Mock<HttpMessageHandler>, no DI-registration test, no
// ctor-null test. Real resolver, real corpus file.

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public class ComposeCitationParityCorpusTests
{
    private sealed record CorpusEntry(int Index, string ParaId, string ComputedNumber, int[] ListPath);

    private sealed record CorpusCase(string Map, string Citation, string Shape, string[] ParaIds);

    private sealed record Corpus(Dictionary<string, CorpusEntry[]> Maps, CorpusCase[] Cases);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Corpus Cases = LoadCorpus();

    private static Corpus LoadCorpus()
    {
        // Walk up for the canonical repo-root marker rather than counting relative segments -- the same
        // resolution ComposeCorpusFixtureLocator uses, and the same one the TypeScript twin performs, so
        // moving either project cannot silently unhook the two halves from each other.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
            {
                var path = Path.Combine(dir.FullName, "tests", "fixtures", "compose-citation-parity", "cases.json");
                if (!File.Exists(path))
                {
                    throw new InvalidOperationException(
                        $"Resolved repo root '{dir.FullName}' but the shared citation-parity corpus is not at " +
                        "'tests/fixtures/compose-citation-parity/cases.json'. Both this test and its TypeScript " +
                        "twin read that file; moving it silently unhooks the parity check, so it must be moved " +
                        "in both readers or not at all.");
                }

                return JsonSerializer.Deserialize<Corpus>(File.ReadAllText(path), JsonOptions)
                       ?? throw new InvalidOperationException("The citation-parity corpus deserialized to null.");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root from test base directory '{AppContext.BaseDirectory}'.");
    }

    public static TheoryData<string, string, string, string> CorpusCases()
    {
        // Flattened to serializable primitives so xUnit can name each case in the runner output -- a
        // failure names the citation string that diverged, which is the whole diagnostic.
        var data = new TheoryData<string, string, string, string>();
        foreach (var c in Cases.Cases)
        {
            data.Add(c.Map, c.Citation, c.Shape, string.Join("|", c.ParaIds));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void ResolveCitation_OverTheSharedCorpus_MatchesTheClientResolver(
        string mapName, string citation, string expectedShape, string expectedParaIds)
    {
        var map = Cases.Maps[mapName]
            .Select(e => new ParaIdMapEntry(
                Index: e.Index,
                ParaId: e.ParaId,
                IsMinted: false,
                ComputedNumber: e.ComputedNumber,
                NumberingLevel: e.ListPath.Length - 1,
                ListPath: e.ListPath))
            .ToList();

        var result = CitationResolver.Resolve(citation, map);

        // The shape is asserted independently of what matched: two parsers can agree on "no match" while
        // disagreeing on WHY (unrecognized vs. parsed-but-absent), and that difference is exactly the kind
        // of drift that later turns into a behavioural gap.
        NormalizeShape(result.Shape).Should().Be(expectedShape,
            $"the client resolver classifies \"{citation}\" as '{expectedShape}'");

        var expected = expectedParaIds.Length == 0
            ? Array.Empty<string>()
            : expectedParaIds.Split('|');

        result.Matches.Select(m => m.ParaId).Should().Equal(expected,
            $"both resolvers must return the same paragraphs, in document order, for \"{citation}\"");
    }

    /// <summary>C# <c>CitationShape.SubItem</c> ↔ the TypeScript union member <c>'subItem'</c>. The
    /// casing convention differs by language; the member SET must not.</summary>
    private static string NormalizeShape(CitationShape shape) => shape switch
    {
        CitationShape.Single => "single",
        CitationShape.SubItem => "subItem",
        CitationShape.Range => "range",
        CitationShape.Unrecognized => "unrecognized",
        _ => throw new InvalidOperationException(
            $"CitationShape.{shape} has no TypeScript counterpart in this mapping. A new shape was added to " +
            "the server enum; add it to the client union, to this switch, and to the shared corpus."),
    };

    [Fact(DisplayName = "The shared citation-parity corpus is present and non-trivial")]
    public void TheCorpusIsPresentAndNonTrivial()
    {
        // Non-vacuity, mirrored on both sides. Every Theory above would pass over an EMPTY case list --
        // a corpus that failed to load, or was quietly emptied to make a red build green, must fail here
        // rather than report a wall of green nothing.
        Cases.Maps.Should().HaveCountGreaterThanOrEqualTo(5);
        Cases.Cases.Should().HaveCountGreaterThanOrEqualTo(40);
        Cases.Cases.Select(c => c.Map).Distinct().Should().OnlyContain(m => Cases.Maps.ContainsKey(m));

        // Every shape must be exercised, so the corpus cannot decay into single-label cases only while
        // still satisfying the count floor above.
        Cases.Cases.Select(c => c.Shape).Distinct().Should().BeEquivalentTo(
            new[] { "single", "subItem", "range", "unrecognized" });
    }
}
