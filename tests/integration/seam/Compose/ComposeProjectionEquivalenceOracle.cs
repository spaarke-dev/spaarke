// ═══════════════════════════════════════════════════════════════════════════════════════════════
// TEMPORARY REFACTOR ORACLE — task 071 (Track D, ComposeDocxProjectionBuilder decomposition).
//
// THIS FILE IS SCAFFOLDING AND IS DELETED WHEN TASK 071 LANDS. It is not a behavioural assertion
// and must not become one: a stored-snapshot equality test over 25 real documents would fail on
// every legitimate future fidelity change (ADR-038 §7 B12 bans exactly that shape), and the
// projection is still being widened by Track A. What it proves is narrower and time-bound:
//
//     the decomposition of ComposeDocxProjectionBuilder.cs changed NO projection output.
//
// The POML makes that proof mandatory and empirical:
//   "Projection output MUST be byte-for-byte equivalent before and after, proven EMPIRICALLY over
//    the whole corpus (capture projections pre-refactor, compare post-refactor). Argument is not
//    proof for this property."
//
// WHY THE BAR IS THIS HIGH. The projection is binding invariant (3) — the ONLY coordinate system
// in the Compose architecture. Anchors, the save-side merge's unchanged-block detection, and every
// fidelity-gate measurement all resolve through it. A one-block difference in projection output is
// not a refactoring artefact; it silently invalidates all three at once, and no existing test would
// necessarily notice, because each of them measures agreement WITH the projection rather than the
// projection itself.
//
// HOW IT IS USED (two runs, one diff):
//     COMPOSE_ORACLE_OUT=<dir>/before   dotnet test --filter ComposeProjectionEquivalenceOracle
//     ... perform the decomposition ...
//     COMPOSE_ORACLE_OUT=<dir>/after    dotnet test --filter ComposeProjectionEquivalenceOracle
//     diff -r <dir>/before <dir>/after      → MUST be empty
//
// Without COMPOSE_ORACLE_OUT set, the test is inert (skips) — so it never runs in CI, never writes
// to a developer's tree, and never gates a build during the window it exists.
//
// DETERMINISM. Both entry points mint ids for constructs that have none (block atoms; paragraphs
// lacking a w14:paraId), and the production ctor mints from a CSPRNG — so two runs of the SAME code
// would differ and the diff would be meaningless. The internal ctor seam (already present for
// forced-collision fixtures) is used to inject a monotonic counter, making a run a pure function of
// the input bytes. A monotonic source also never collides, so MintUnique's retry loop is not
// exercised — that path is covered by its own existing fixtures, not by this oracle.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeProjectionEquivalenceOracle
{
    private const string OutDirVariable = "COMPOSE_ORACLE_OUT";

    private static readonly JsonSerializerOptions Dump = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void CaptureProjection_ForEveryCorpusDocument(string documentPath)
    {
        var outDir = Environment.GetEnvironmentVariable(OutDirVariable);
        if (string.IsNullOrWhiteSpace(outDir))
        {
            // Inert unless explicitly driven. Asserting nothing is the point: this is a capture
            // harness, and a no-op run must not read as a passing behavioural test.
            return;
        }

        Directory.CreateDirectory(outDir);
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(documentPath);
        var name = Path.GetFileNameWithoutExtension(documentPath);

        // A fresh counter per ENTRY POINT, not per document: each call is independently
        // deterministic, so a diff localises to the entry point that changed.
        var html = new ComposeDocxProjectionBuilder(Monotonic()).Build(bytes);
        var model = new ComposeDocxProjectionBuilder(Monotonic()).BuildContentModel(bytes);

        File.WriteAllText(Path.Combine(outDir, $"{name}.html.json"), Render(html), Encoding.UTF8);
        File.WriteAllText(Path.Combine(outDir, $"{name}.model.json"), Render(model), Encoding.UTF8);
    }

    /// <summary>
    /// Serialises the WHOLE projection envelope, not a summary. A hash or a shape-only digest would
    /// answer "did something change?" while withholding the one thing that makes a difference
    /// actionable during a 3,000-line restructure: which block, which run, which warning.
    /// </summary>
    private static string Render<T>(T projection) => JsonSerializer.Serialize(projection, Dump);

    private static Func<uint> Monotonic()
    {
        uint next = 1;
        return () => next++;
    }
}
