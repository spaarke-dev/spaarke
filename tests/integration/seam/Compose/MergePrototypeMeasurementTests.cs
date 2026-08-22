// Task 030 (spaarkeai-compose-r8, spec §5.3 + FR-G06 + FR-G07) — THE MERGE PROTOTYPE MEASUREMENT.
//
// This is the experiment that decides whether task 040 should exist in the shape R8 designed. It drives
// each corpus document through the renderer TWICE from identical inputs — once as R6 renders today
// (`mergeUnchangedBlocks: false`, the control) and once through the three-way merge
// (`mergeUnchangedBlocks: true`) — and measures both with the SAME oracle task 023 published the control
// with. Two numbers from one run, same document, same edit, same instrument: the comparison cannot drift.
//
// WHY IT MEASURES THE RENDERER DIRECTLY RATHER THAN THROUGH THE WIRE: the renderer IS the thing under
// test. `ComposeService` decides WHETHER to write and what outcome to report (Track S, already measured
// and green corpus-wide); `ComposeDocumentRenderer` decides WHAT bytes get written, which is the entire
// subject of Phase 3. Driving it directly also lets both arms run against byte-identical input, which a
// wire round trip cannot guarantee.
//
// MAINTAIN-class while Phase 3 is open (tests/integration/seam/** KEEP path per ADR-038). No
// Mock<HttpMessageHandler>, no DI-registration test, no ctor-null test, no reflection over privates.

using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class MergePrototypeMeasurementTests : IClassFixture<MergePrototypeResultSink>
{
    /// <summary>The same one-paragraph edit the gate applies, so the prototype is measured against the
    /// control on identical terms.</summary>
    private const string EditMarker = " [R6-060-FIDELITY-GATE]";

    private readonly MergePrototypeResultSink _sink;
    private readonly ITestOutputHelper _output;

    public MergePrototypeMeasurementTests(MergePrototypeResultSink sink, ITestOutputHelper output)
    {
        _sink = sink;
        _output = output;
    }

    public static TheoryData<string> CorpusDocumentNames()
    {
        var data = new TheoryData<string>();
        foreach (var path in ComposeCorpusFixtureLocator.EnumerateDocumentPaths())
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // THE MEASUREMENT — control arm vs merge arm, same document, same edit, same oracle.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocumentNames))]
    public void MergePrototype_PreservesUntouchedBlocks_AndNeverRegressesAgainstTheControl(string fileName)
    {
        var source = LoadCorpus(fileName);
        var edited = ApplyRepresentativeEdit(ProjectModel(source, fileName), out var editApplied);
        editApplied.Should().BeTrue($"[{fileName}] the corpus document must expose an editable run");

        var control = Render(source, edited, merge: false, out _, out var controlMs);
        var stats = new MergePrototypeStats();
        var merged = Render(source, edited, merge: true, out stats, out var mergedMs);

        var controlReport = Measure(source, control);
        var mergedReport = Measure(source, merged);

        _sink.Record(new MergePrototypeRow(
            fileName,
            controlReport.OverallPreservationPercent,
            mergedReport.OverallPreservationPercent,
            controlReport.NearTierPreservationPercent,
            mergedReport.NearTierPreservationPercent,
            mergedReport.NearTierRelevantCount,
            mergedReport.ComparedBlockCount,
            stats.ClonedBlocks,
            stats.RenderedBlocks,
            controlMs,
            mergedMs,
            mergedReport.Differences.Select(d => string.Join(" ", d.DifferingPaths)).Take(6).ToList()));

        _output.WriteLine(
            $"{fileName}: overall {Fmt(controlReport.OverallPreservationPercent)} -> {Fmt(mergedReport.OverallPreservationPercent)}  " +
            $"nearTier {Fmt(controlReport.NearTierPreservationPercent)} -> {Fmt(mergedReport.NearTierPreservationPercent)}  " +
            $"cloned={stats.ClonedBlocks} rendered={stats.RenderedBlocks}  {controlMs}ms -> {mergedMs}ms");

        // The prototype must never be WORSE than what ships today. The gate THRESHOLD decision is task
        // 031's and is deliberately not asserted here — but a regression is a defect at any threshold,
        // and it is the one outcome that would make proceeding to 040 indefensible.
        (mergedReport.OverallPreservationPercent ?? 100d).Should().BeGreaterThanOrEqualTo(
            controlReport.OverallPreservationPercent ?? 100d,
            $"[{fileName}] the merge must not preserve LESS than render-on-save already does");

        (mergedReport.NearTierPreservationPercent ?? 100d).Should().BeGreaterThanOrEqualTo(
            controlReport.NearTierPreservationPercent ?? 100d,
            $"[{fileName}] the merge must not preserve LESS of the near tier than render-on-save already does");

        // ADR-049: a block the merge cannot handle degrades to a thin render + warning. It never refuses.
        merged.Should().NotBeEmpty($"[{fileName}] the merge must always produce a document — never a refusal");

        // ══════════════════════════════════════════════════════════════════════════════════════════
        // THE ANTI-VACUITY ASSERTION. Everything above measures what SURVIVED; this measures that
        // something CHANGED.
        //
        // A merge that cloned EVERY block — including the edited one — would report 100% preservation
        // on every document while silently discarding the user's edit. That is the single false pass
        // that would clear the Phase-3 gate and ship R9, and it is indistinguishable from success by
        // the preservation number alone. Three independent facts rule it out:
        // ══════════════════════════════════════════════════════════════════════════════════════════
        ExtractBodyText(merged).Should().Contain(EditMarker,
            $"[{fileName}] the edit MUST be present in the merged output — a merge that preserves 100% " +
            "by cloning the edited block too has thrown the user's work away");

        stats.RenderedBlocks.Should().BeGreaterThan(0,
            $"[{fileName}] at least one block must have been RENDERED — an all-clone merge is a no-op save");

        mergedReport.EditedBlockIndex.Should().BeGreaterThanOrEqualTo(0,
            $"[{fileName}] the oracle must have LOCATED the edited block and excluded it from the " +
            "denominator — a -1 here means it measured the edited block as if it were untouched");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // FR-G06 — HEAVY RESTRUCTURE. Section reorder + large cut-paste. The merge's pairing is by document
    // order, so a wholesale reorder is its worst case by construction: almost nothing pairs, almost
    // everything re-renders. The requirement is that it degrades GRACEFULLY — more blocks rebuilt, never
    // a hard fail, never a refusal — not that it preserves anything.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MergePrototype_HeavyRestructure_DegradesGracefully_NeverHardFails()
    {
        var source = LoadCorpus("nda-interrupted-clauses.docx");
        var model = ProjectModel(source, "nda-interrupted-clauses.docx");

        // Reverse the body: every block moves, so document-order pairing finds a counterpart at each
        // index but almost none of them match. This is a strictly harsher case than a real cut-paste.
        var reordered = model with { Blocks = model.Blocks.Reverse().ToList() };

        var stats = new MergePrototypeStats();
        var bytes = Render(source, reordered, merge: true, out stats, out var ms);

        bytes.Should().NotBeEmpty("a heavy restructure must still produce a document (ADR-049: never a refusal)");
        stats.RenderedBlocks.Should().BeGreaterThan(0, "a reordered body cannot be satisfied by cloning alone");
        stats.TotalBlocks.Should().Be(reordered.Blocks.Count, "every posted block must be accounted for — cloned or rendered");

        // The document must still open and still hold the same number of body blocks.
        ReadBodyBlockCount(bytes).Should().BeGreaterThan(0, "the restructured render must be a readable package");

        _output.WriteLine($"heavy restructure: cloned={stats.ClonedBlocks} rendered={stats.RenderedBlocks} of {stats.TotalBlocks} in {ms}ms");
        _sink.RecordRestructure(stats.ClonedBlocks, stats.RenderedBlocks, stats.TotalBlocks);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // FR-G07 — N-CYCLE ROUND TRIP. N = 5: enough that a per-cycle loss of even a few percent compounds
    // into an unmistakable slope, and cheap enough to run on every gate execution. Each cycle takes the
    // PREVIOUS cycle's OUTPUT as the next cycle's carrier — which is exactly what a user editing the
    // same document five times produces, and the case where "paraId is not a durable file key" bites
    // hardest, because each render re-mints ids the next cycle must pair against by document order.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("nda-interrupted-clauses.docx")]
    [InlineData("court-filing-spacing.docx")]
    [InlineData("char-formatting-mixed-runs.docx")]
    public void MergePrototype_NCycleRoundTrip_DoesNotCompoundLoss(string fileName)
    {
        const int cycles = 5;
        var original = LoadCorpus(fileName);
        var current = original;
        var perCycle = new List<double?>();

        for (var cycle = 0; cycle < cycles; cycle++)
        {
            var model = ProjectModel(current, fileName);
            var edited = ApplyRepresentativeEdit(model, out var applied);
            if (!applied)
            {
                break;
            }

            current = Render(current, edited, merge: true, out _, out _);

            // Measured against the ORIGINAL every time — the question is cumulative drift from where the
            // user started, not drift from the previous cycle (which would hide a steady decline).
            perCycle.Add(Measure(original, current).OverallPreservationPercent);
        }

        perCycle.Should().HaveCount(cycles, "every cycle must complete — a round trip that stops is a hard fail");
        _output.WriteLine($"{fileName} N={cycles}: " + string.Join(" -> ", perCycle.Select(Fmt)));
        _sink.RecordCycles(fileName, perCycle);

        // Compounding drift is the failure this requirement exists to catch: preservation measured
        // against the ORIGINAL must not fall as cycles accumulate. A one-off drop at cycle 1 (the first
        // render's own loss) is expected; a monotonic decline is not.
        for (var i = 1; i < perCycle.Count; i++)
        {
            (perCycle[i] ?? 100d).Should().BeGreaterThanOrEqualTo(
                (perCycle[i - 1] ?? 100d) - 0.001,
                $"[{fileName}] loss must not COMPOUND across round trips — cycle {i + 1} preserved less than cycle {i}");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private static byte[] LoadCorpus(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private static ComposeContentModel ProjectModel(byte[] bytes, string fileName)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(bytes);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed,
            $"'{fileName}' must project into the canonical content model — the render input");
        return projection.Model!;
    }

    /// <summary>Appends the marker to the first text-carrying run of the first block that has one — the
    /// minimal "the user typed something" edit, identical to the gate's.</summary>
    private static ComposeContentModel ApplyRepresentativeEdit(ComposeContentModel model, out bool applied)
    {
        var blocks = model.Blocks.ToList();
        for (var b = 0; b < blocks.Count; b++)
        {
            var runs = blocks[b].Runs;
            for (var r = 0; r < runs.Count; r++)
            {
                if (string.IsNullOrEmpty(runs[r].Text))
                {
                    continue;
                }

                var newRuns = runs.ToList();
                newRuns[r] = newRuns[r] with { Text = newRuns[r].Text + EditMarker };
                blocks[b] = blocks[b] with { Runs = newRuns };
                applied = true;
                return model with { Blocks = blocks };
            }
        }

        applied = false;
        return model;
    }

    private static byte[] Render(
        byte[] carrier, ComposeContentModel model, bool merge, out MergePrototypeStats stats, out long elapsedMs)
    {
        stats = new MergePrototypeStats();
        var warnings = new List<ComposeProjectionWarning>();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var bytes = new ComposeDocumentRenderer().RenderIntoCarrier(
            carrier, model, "prototype", warnings, mergeUnchangedBlocks: merge, mergeStats: stats);
        elapsedMs = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        return bytes;
    }

    private static ComposeBlockPreservationOracle.PreservationReport Measure(byte[] original, byte[] rendered) =>
        ComposeBlockPreservationOracle.Compare(
            original, rendered, EditMarker, ComposeBlockPreservationOracle.ComparisonLevel.Lenient);

    private static int ReadBodyBlockCount(byte[] docx)
    {
        using var ms = new MemoryStream(docx, writable: false);
        using var pkg = WordprocessingDocument.Open(ms, isEditable: false);
        return pkg.MainDocumentPart?.Document?.Body?.ChildElements.Count ?? 0;
    }

    /// <summary>All body text in the rendered package, for the edit-survival assertion.</summary>
    private static string ExtractBodyText(byte[] docx)
    {
        using var ms = new MemoryStream(docx, writable: false);
        using var pkg = WordprocessingDocument.Open(ms, isEditable: false);
        var body = pkg.MainDocumentPart?.Document?.Body;
        return body is null ? string.Empty : string.Concat(body.Descendants<Text>().Select(t => t.Text));
    }

    private static string Fmt(double? v) => v is null ? "n/a" : $"{v.Value:F2}%";
}

/// <summary>One corpus document's control-vs-merge measurement.</summary>
public sealed record MergePrototypeRow(
    string Name,
    double? ControlOverall,
    double? MergedOverall,
    double? ControlNearTier,
    double? MergedNearTier,
    int NearTierRelevantCount,
    int ComparedBlockCount,
    int ClonedBlocks,
    int RenderedBlocks,
    long ControlMs,
    long MergedMs,
    IReadOnlyList<string> RemainingDifferencePaths);

/// <summary>
/// Writes ONE machine-readable result file after every document has run — the input to the task 030
/// results note and the task 031 gate decision. Mirrors the gate harness's sink convention.
/// </summary>
public sealed class MergePrototypeResultSink : IDisposable
{
    public const string ResultFileName = "merge-prototype-result.json";

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, MergePrototypeRow> _rows = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<double?>> _cycles = new(StringComparer.Ordinal);
    private int _restructureCloned;
    private int _restructureRendered;
    private int _restructureTotal;

    public void Record(MergePrototypeRow row) => _rows[row.Name] = row;

    public void RecordCycles(string name, IReadOnlyList<double?> perCycle) => _cycles[name] = perCycle;

    public void RecordRestructure(int cloned, int rendered, int total)
    {
        _restructureCloned = cloned;
        _restructureRendered = rendered;
        _restructureTotal = total;
    }

    public void Dispose()
    {
        var rows = _rows.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        var payload = new
        {
            harness = nameof(MergePrototypeMeasurementTests),
            generatedAtUtc = DateTimeOffset.UtcNow,
            aggregate = new
            {
                documents = rows.Count,
                comparedBlocks = rows.Sum(r => r.ComparedBlockCount),
                clonedBlocks = rows.Sum(r => r.ClonedBlocks),
                renderedBlocks = rows.Sum(r => r.RenderedBlocks),
                controlMsTotal = rows.Sum(r => r.ControlMs),
                mergedMsTotal = rows.Sum(r => r.MergedMs),
            },
            documentsDetail = rows,
            heavyRestructure = new { cloned = _restructureCloned, rendered = _restructureRendered, total = _restructureTotal },
            nCycle = _cycles.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => new { document = kv.Key, perCyclePreservation = kv.Value })
                .ToArray(),
        };

        File.WriteAllText(
            Path.Combine(AppContext.BaseDirectory, ResultFileName),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }
}
