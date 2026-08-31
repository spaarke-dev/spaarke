// ═══════════════════════════════════════════════════════════════════════════════════════════════
// TEMPORARY REFACTOR ORACLE — task 072 (Track D, ComposeDocumentRenderer decomposition).
//
// THIS FILE IS SCAFFOLDING AND IS DELETED WHEN TASK 072 LANDS — same contract, and same reasons, as
// task 071's projection oracle (see notes/071-projection-builder-seam-map.md §1). A stored-snapshot
// equality test over 25 rendered documents would fail on every legitimate future fidelity change, which
// ADR-038 §7 B12 bans, and Track A is still widening what the renderer carries.
//
// WHAT IT PROVES: the decomposition of ComposeDocumentRenderer.cs changed NO rendered output.
//
// WHY THE BAR IS THIS HIGH. This file is the ONE body author (ADR-049 I-5) — the invariant R8 exists to
// make literally true. The task's own escalation trigger says the line target must never be met by
// extracting something that writes body children. But an extraction can also break fidelity WITHOUT
// touching the body: numbering definitions, styles, comments and relationship resolution all live in
// other package parts, and a document whose numbering.xml lost a level still renders — just wrongly.
// So the oracle compares the WHOLE package, part by part, not just the body.
//
// TWO ENTRY POINTS, DELIBERATELY. `SynthesizeDocument` authors a document from nothing (the
// born-in-editor path). `RenderIntoCarrier` re-projects a retained baseline and clones the blocks the
// user never touched (the R8 save path, ADR-049 invariant 2). They exercise largely different code and
// a control that ran only the first would say nothing about the merge path.
//
// USAGE (two runs, one diff — the proof is that the diff is empty):
//     COMPOSE_RENDER_ORACLE_OUT=<dir>/before  dotnet test --filter ComposeRenderEquivalenceOracle
//     ...decompose...
//     COMPOSE_RENDER_ORACLE_OUT=<dir>/after   dotnet test --filter ComposeRenderEquivalenceOracle
//     diff -r <dir>/before <dir>/after        # MUST be empty
//
// Unset the variable and the test is inert, so it never runs in CI and never gates a build.
//
// NORMALISATION. A .docx is a ZIP, and ZIP entry timestamps move on every write, so the CONTAINER bytes
// are not comparable and comparing them would produce a permanently-red instrument. Each part's CONTENT
// is compared instead, entry by entry in sorted order. `AddCoreProperties` writes no timestamp (only
// creator + description), so core.xml is already deterministic and needs no special handling — verified
// rather than assumed, because a normaliser that silently masks a real difference is worse than none.
// The remaining nondeterminism is the paraId mint, injected via the existing internal ctor seam.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

using System.IO.Compression;
using System.Text;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeRenderEquivalenceOracle
{
    private const string OutDirVariable = "COMPOSE_RENDER_ORACLE_OUT";
    private const string Author = "Oracle Author";

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void CaptureRender_ForEveryCorpusDocument(string documentPath)
    {
        var outDir = Environment.GetEnvironmentVariable(OutDirVariable);
        if (string.IsNullOrWhiteSpace(outDir))
        {
            // Inert unless explicitly driven. Asserting nothing is the point — a no-op run must not read
            // as a passing behavioural test.
            return;
        }

        Directory.CreateDirectory(outDir);
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(documentPath);
        var name = Path.GetFileNameWithoutExtension(documentPath);

        // The model is produced by the (already-decomposed, already-proven-equivalent) projector, so any
        // difference this oracle reports is attributable to the RENDERER.
        var projection = new ComposeDocxProjectionBuilder(Monotonic()).BuildContentModel(bytes);
        if (projection.Status == ComposeProjectionStatus.Failed)
        {
            File.WriteAllText(Path.Combine(outDir, $"{name}.SKIPPED.txt"),
                "projection failed: " + string.Join(",", projection.Warnings.Select(w => w.Code)));
            return;
        }

        Capture(outDir, $"{name}.synth", () =>
        {
            var degradations = new List<ComposeProjectionWarning>();
            var docx = new ComposeDocumentRenderer(Monotonic())
                .SynthesizeDocument(projection.Model, Author, degradations);
            return (docx, Describe(degradations), null);
        });

        Capture(outDir, $"{name}.carrier", () =>
        {
            var degradations = new List<ComposeProjectionWarning>();
            var stats = new ComposeMergeStats();
            var docx = new ComposeDocumentRenderer(Monotonic())
                .RenderIntoCarrier(bytes, WithOneEditedBlock(projection.Model), Author, degradations,
                                   mergeUnchangedBlocks: true, mergeStats: stats);
            // Field-by-field, not ToString() — ComposeMergeStats does not override it, so ToString()
            // yields the type name and would have made every merge look identical in the diff.
            var summary =
                $"cloned={stats.ClonedBlocks} rendered={stats.RenderedBlocks} " +
                $"renderedWithoutCounterpart={stats.RenderedWithoutCounterpart} " +
                $"baselineUnavailable={stats.BaselineUnavailable} baselineUnaligned={stats.BaselineUnaligned} " +
                $"alignmentDegraded={stats.AlignmentDegraded}";
            return (docx, Describe(degradations), summary);
        });
    }

    /// <summary>
    /// Edits the text of the first block that has any, so the merge sees ONE changed block among many
    /// unchanged ones.
    /// </summary>
    /// <remarks>
    /// WHY THIS IS NOT OPTIONAL. Handing `RenderIntoCarrier` the model projected from the very same bytes
    /// makes every block unchanged, so the merge clones all of them and the RENDER half of the carrier
    /// path never executes. Measured: a seeded mutation in `ApplyAlignment` (shared by both paths) showed
    /// up in 3 `.synth` captures and **zero** `.carrier` captures — the carrier control was proving only
    /// that cloning works, while reading as though it covered the save path.
    /// <para>
    /// One edited block among many unchanged ones is also the shape the R8 save path actually takes
    /// (ADR-049 invariant 2: untouched blocks are preserved), so this exercises clone AND render AND the
    /// property-inheritance step that pairs a rendered block with its baseline element.
    /// </para>
    /// </remarks>
    private static ComposeContentModel WithOneEditedBlock(ComposeContentModel model)
    {
        var blocks = model.Blocks.ToArray();
        for (var i = 0; i < blocks.Length; i++)
        {
            var runs = blocks[i].Runs;
            if (runs is null || runs.Count == 0 || string.IsNullOrEmpty(runs[0].Text)) continue;

            var edited = runs.ToArray();
            edited[0] = edited[0] with { Text = edited[0].Text + " [oracle-edit]" };
            blocks[i] = blocks[i] with { Runs = edited };
            return model with { Blocks = blocks };
        }
        return model; // no textual block (e.g. tables/atoms only) — clone-only is the honest behaviour here
    }

    private static void Capture(string outDir, string stem, Func<(byte[] Docx, string Degradations, string? Stats)> render)
    {
        string dump;
        try
        {
            var (docx, degradations, stats) = render();
            dump = Unpack(docx, degradations, stats);
        }
        catch (Exception ex)
        {
            // A throw is itself observable behaviour worth diffing: if the refactor changes WHICH
            // documents throw, that is exactly the regression this oracle exists to catch. Swallowing it
            // would turn a behaviour change into a missing file.
            dump = $"THREW {ex.GetType().FullName}: {ex.Message}";
        }
        File.WriteAllText(Path.Combine(outDir, stem + ".txt"), dump, Encoding.UTF8);
    }

    private static string Describe(IReadOnlyCollection<ComposeProjectionWarning> degradations) =>
        degradations.Count == 0
            ? "(none)"
            : string.Join("\n", degradations.Select(d => $"{d.Code} x{d.Count} {d.Detail}").OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>
    /// Renders the package as sorted per-part CONTENT. Comparing container bytes would compare ZIP entry
    /// timestamps, which move on every write.
    /// </summary>
    private static string Unpack(byte[] docx, string degradations, string? stats)
    {
        var sb = new StringBuilder();
        sb.Append("── degradations ──\n").Append(degradations).Append('\n');
        if (stats is not null) sb.Append("── merge stats ──\n").Append(stats).Append('\n');

        using var ms = new MemoryStream(docx);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            sb.Append("── part: ").Append(entry.FullName).Append(" ──\n");
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            // One element per line. OOXML is written without pretty-printing, so a whole part is a single
            // enormous line and a line-granular diff would report "this part changed" and nothing more.
            sb.Append(reader.ReadToEnd().Replace("><", ">\n<")).Append('\n');
        }
        return CanonicaliseSdkRelationshipIds(sb.ToString());
    }

    /// <summary>
    /// Rewrites OpenXML-SDK-minted relationship ids (<c>R</c> + 16 hex, GUID-derived) to <c>R#1</c>,
    /// <c>R#2</c>, … in order of first appearance.
    /// </summary>
    /// <remarks>
    /// These ids are assigned by <c>DocumentFormat.OpenXml</c> when a part is added, NOT by our code, and
    /// they are random on every run — so without this the oracle is permanently red and proves nothing.
    /// This is the one normalisation the render side needs that the projection side did not.
    /// <para>
    /// It deliberately canonicalises rather than deletes. The ids are REFERENCED from
    /// <c>document.xml</c> (<c>r:id</c> on hyperlinks, images, carried objects), so renumbering them
    /// consistently across the whole dump preserves the relationship GRAPH: if a refactor made a
    /// hyperlink point at a different relationship, or dropped one, the canonical numbering shifts and
    /// the diff still fires. Erasing the ids would have hidden exactly that class of regression.
    /// </para>
    /// <para>
    /// Word's own <c>rId1</c>/<c>rId2</c> form is left alone — those come from the source document and
    /// are already deterministic.
    /// </para>
    /// </remarks>
    private static string CanonicaliseSdkRelationshipIds(string dump)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(dump, @"\bR[0-9a-f]{16}\b", m =>
        {
            if (!map.TryGetValue(m.Value, out var canonical))
            {
                canonical = "R#" + (map.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                map[m.Value] = canonical;
            }
            return canonical;
        });
    }

    private static Func<uint> Monotonic()
    {
        uint next = 1;
        return () => next++;
    }
}
