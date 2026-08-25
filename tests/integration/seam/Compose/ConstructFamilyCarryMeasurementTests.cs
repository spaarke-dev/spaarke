// Task 043 (spaarkeai-compose-r8, FR-A07) — what happens to a hard construct when the user edits the
// block that CARRIES it.
//
// WHY THIS FILE EXISTS. The corpus scan for task 043 found six construct families with ZERO coverage, so
// the gate's headline "zero hard-fails" said nothing about them. Four fixtures now close that gap
// (`generators/make-untested-construct-families.py`). But the standing harness applies its representative
// edit to the FIRST run with text — which in those fixtures is ordinary prose, NOT the construct's own
// paragraph. So the standing measurement proves only the easy half: a construct in an untouched block is
// cloned byte-verbatim, which it is, and which is unsurprising because the merge never parses it.
//
// The half that decides FR-A07 is the other one: edit the paragraph the construct lives in, where the
// renderer — not the cloner — authors the output. That is the only place a construct can actually be lost,
// and it is exactly where a capability gate would claim to protect the user. Measuring the easy half and
// reporting "all clear" would be the fourth near-vacuous measurement this project has had to catch.
//
// This file MEASURES and REPORTS. It asserts only the honest floor — the save produced a readable package
// and the user's edit landed — because a threshold fitted to whatever the code already does is not a gate.
// The numbers feed `notes/capability-gate-triggers.md` and the owner's FR-A07 decision.
//
// MAINTAIN-class (tests/integration/seam/** vertical-slice KEEP path, ADR-038).

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ConstructFamilyCarryMeasurementTests
{
    private readonly ITestOutputHelper _output;

    public ConstructFamilyCarryMeasurementTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string EditMarker = " [edited in the construct's own block]";

    /// <summary>
    /// Each row: the fixture, the 0-based body-block index of the paragraph that CARRIES the construct,
    /// the local-name of the construct element, and the package part it depends on (null = body-only).
    /// The indices are fixed by the generator and are asserted below rather than assumed — a fixture edit
    /// that shifts them must fail loudly, not silently measure the wrong paragraph.
    /// </summary>
    public static IEnumerable<object[]> Families() => new[]
    {
        new object[] { "ole-embedded-object.docx", 2, "object", "word/embeddings/oleObject1.bin" },
        new object[] { "chart-embedded.docx", 2, "drawing", "word/charts/chart1.xml" },
        new object[] { "endnote-references.docx", 1, "endnoteReference", "word/endnotes.xml" },
        new object[] { "embedded-font.docx", 0, "", "word/fonts/font1.odttf" },
    };

    [Theory]
    [MemberData(nameof(Families))]
    public void EditingTheBlockThatCarriesTheConstruct_IsMeasuredAndReported(
        string fileName, int blockIndex, string constructLocalName, string dependentPart)
    {
        var source = ComposeCorpusFixtureLocator.LoadVerifiedBytes(
            ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
                .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase)));

        // The construct must be in the source to begin with — otherwise everything below measures nothing.
        if (constructLocalName.Length > 0)
        {
            CountElements(source, constructLocalName).Should().BeGreaterThan(0,
                $"[{fileName}] the fixture must actually contain <w:{constructLocalName}> — a measurement " +
                "over an absent construct is the vacuity this file exists to avoid");
        }
        PartExists(source, dependentPart).Should().BeTrue(
            $"[{fileName}] the fixture must actually carry {dependentPart}");

        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed,
            $"[{fileName}] a fixture that cannot be projected would be gated by the EXISTING read-only " +
            "path, not by any new construct trigger");

        var model = projection.Model!;
        model.Blocks.Count.Should().BeGreaterThan(blockIndex,
            $"[{fileName}] block index {blockIndex} must exist — if the generator changed, this row is stale");

        var edited = EditBlock(model, blockIndex);

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, edited, "construct-carry-measurement", degradations);

        // ── The floor, asserted: a defined outcome and the user's edit present (ADR-049 invariant 1). ──
        rendered.Should().NotBeNullOrEmpty();
        // TrimStart: a run-less block (a paragraph carrying only an object) gets the marker as its FIRST
        // run, without the leading separator space that an append would need.
        ExtractBodyText(rendered).Should().Contain(EditMarker.TrimStart(),
            $"[{fileName}] the edit must land — otherwise this measures nothing");

        // A readable package is a low bar; the saved document must also be SCHEMA-VALID. Dropping an
        // element while leaving a dangling relationship behind would still open here and still be broken,
        // and "no hard fail" has to mean more than "the bytes parsed".
        using (var saved = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
                .Validate(saved)
                .Select(e => $"{e.Path?.XPath}: {e.Description}")
                .Take(5)
                .ToList();
            errors.Should().BeEmpty(
                $"[{fileName}] the SAVED document must be schema-valid after the construct's own block was " +
                "edited — an invalid package is a hard fail even when it happens to open");
        }

        // ── The measurement, reported: what survived, and what was said about it. ──
        var before = constructLocalName.Length > 0 ? CountElements(source, constructLocalName) : 0;
        var after = constructLocalName.Length > 0 ? CountElements(rendered, constructLocalName) : 0;
        var partSurvived = PartExists(rendered, dependentPart);
        var codes = degradations.Count == 0
            ? "(none)"
            : string.Join(", ", degradations.Select(d => $"{d.Code}×{d.Count}"));

        _output.WriteLine(
            $"{fileName}: block[{blockIndex}] edited · " +
            (constructLocalName.Length > 0 ? $"<w:{constructLocalName}> {before} → {after} · " : "") +
            $"{dependentPart} {(partSurvived ? "SURVIVED" : "LOST")} · warnings: {codes}");

        // ── The one thing that would be a defect either way: silence about a loss. ──
        if (constructLocalName.Length > 0 && after < before)
        {
            degradations.Should().NotBeEmpty(
                $"[{fileName}] the edited block lost <w:{constructLocalName}> ({before} → {after}) and said " +
                "NOTHING. A silent loss is the failure mode this whole project exists to end — task 044's " +
                "taxonomy must name it.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Appends the marker to the block's first run, or gives a run-less block one — a paragraph
    /// carrying only an object has no text, and typing into it is a legitimate user action.</summary>
    private static ComposeContentModel EditBlock(ComposeContentModel model, int blockIndex)
    {
        var blocks = model.Blocks.ToList();
        var runs = blocks[blockIndex].Runs.ToList();

        // Task 056: append to the first PROSE run, not simply to `runs[0]`. Since embedded objects are
        // carried, a paragraph holding only an image projects as one MARKER run — and a marker run ignores
        // `Text` by contract, so setting it there made the edit vanish and this measurement silently stopped
        // measuring anything (caught by its own "the edit must land" floor, which is what that assertion is
        // for). A block with no prose run gets a new one, exactly as an empty block did before.
        var proseIndex = runs.FindIndex(r =>
            r.EmbeddedObject is null && r.Field is null && r.Symbol is null
            && r.CommentAnchor is null && !r.IsTab && !r.IsPageBreak && !r.IsLineBreak);

        if (proseIndex < 0)
        {
            runs.Add(new ComposeInlineRun { Text = EditMarker.TrimStart() });
        }
        else
        {
            runs[proseIndex] = runs[proseIndex] with
            {
                Text = (runs[proseIndex].Text ?? string.Empty) + EditMarker,
            };
        }

        blocks[blockIndex] = blocks[blockIndex] with { Runs = runs };
        return model with { Blocks = blocks };
    }

    private static int CountElements(byte[] docx, string localName)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body is { } body
            ? body.Descendants().Count(e => e.LocalName == localName)
            : 0;
    }

    private static bool PartExists(byte[] docx, string partPath)
    {
        using var archive = new System.IO.Compression.ZipArchive(
            new MemoryStream(docx, writable: false), System.IO.Compression.ZipArchiveMode.Read);
        return archive.Entries.Any(e => string.Equals(e.FullName, partPath, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractBodyText(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body is { } body
            ? string.Concat(body.Descendants<Text>().Select(t => t.Text))
            : string.Empty;
    }
}
