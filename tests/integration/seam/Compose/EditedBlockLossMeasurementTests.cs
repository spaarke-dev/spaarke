// Task 041 (spaarkeai-compose-r8) — WHAT THE EDITED BLOCK LOSES.
//
// The Phase-3 gate measures UNTOUCHED blocks and excludes the edited one by construction, so it reports
// 100% on a save that still damages the only paragraph the user typed in. This is the complement: the same
// corpus, the same edit, the same instrument — pointed at the block the gate cannot see.
//
// It is a MEASUREMENT first and a gate second. Task 023 established the control before proposing the merge;
// this establishes the edited-block baseline before proposing atom carry, so "we shipped FR-A05" can be
// checked against a number rather than asserted.
//
// MAINTAIN-class (tests/integration/seam/** KEEP path per ADR-038).

using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class EditedBlockLossMeasurementTests : IClassFixture<EditedBlockLossSink>
{
    private const string EditMarker = " [R6-060-FIDELITY-GATE]";

    private readonly EditedBlockLossSink _sink;
    private readonly ITestOutputHelper _output;

    public EditedBlockLossMeasurementTests(EditedBlockLossSink sink, ITestOutputHelper output)
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

    [Theory]
    [MemberData(nameof(CorpusDocumentNames))]
    public void EditedBlock_LossIsMeasuredAndRecorded(string fileName)
    {
        var source = LoadCorpus(fileName);
        var model = ProjectModel(source, fileName);
        var edited = ApplyRepresentativeEdit(model, out var applied);
        applied.Should().BeTrue($"[{fileName}] the corpus document must expose an editable run");

        var rendered = Render(source, edited);

        var difference = ComposeBlockPreservationOracle.CompareEditedBlock(
            source, rendered, EditMarker, ComposeBlockPreservationOracle.ComparisonLevel.Lenient);

        difference.Should().NotBeNull(
            $"[{fileName}] the edited block must be locatable in the saved document — a null here means the " +
            "edit did not land, or the block count drifted, and either is a defect, not a clean measurement");

        var paths = difference!.DifferingPaths;
        _output.WriteLine(paths.Count == 0
            ? $"{fileName}: edited block INTACT"
            : $"{fileName}: edited block differs at [{string.Join(", ", paths.Take(8))}]{(paths.Count > 8 ? " …" : string.Empty)}");

        _sink.Record(fileName, difference.Index, paths.ToList(), difference.IsNearTier);

        // NOT a threshold assertion. Task 041 sets its bar from this measurement; asserting a number here
        // before the number exists is how a gate gets fitted to whatever the code already does.
        // What IS asserted: the measurement is real — the edit landed and the block was found.
        ExtractBodyText(rendered).Should().Contain(
            EditMarker, $"[{fileName}] the user's edit must be present — otherwise this measures nothing");
    }

    /// <summary>
    /// The single fact this file will defend after task 041 lands: an edit inside a formatted run must not
    /// strip the paragraph's own formatting. Asserted on the corpus rather than a synthetic fixture.
    /// </summary>
    [Theory]
    [InlineData("court-filing-spacing.docx")]
    [InlineData("char-formatting-mixed-runs.docx")]
    public void EditedBlock_DoesNotLoseParagraphLevelFormatting(string fileName)
    {
        var source = LoadCorpus(fileName);
        var model = ProjectModel(source, fileName);
        var edited = ApplyRepresentativeEdit(model, out var applied);
        applied.Should().BeTrue();

        var rendered = Render(source, edited);
        var difference = ComposeBlockPreservationOracle.CompareEditedBlock(
            source, rendered, EditMarker, ComposeBlockPreservationOracle.ComparisonLevel.Lenient);

        difference.Should().NotBeNull();
        difference!.DifferingPaths.Should().NotContain(
            p => p.EndsWith("/ind", StringComparison.Ordinal) || p.EndsWith("/spacing", StringComparison.Ordinal),
            $"[{fileName}] task 040's property inheritance carries the base paragraph's indentation and " +
            "spacing onto the edited block — losing them is the regression this test exists to catch");
    }


    /// <summary>
    /// Every corpus fixture must be schema-valid WordprocessingML.
    /// </summary>
    /// <remarks>
    /// Added by task 041 after the edited-block measurement flagged a `jc|spacing` ordering difference on
    /// `court-filing-spacing.docx` that appeared only once the renderer started emitting ECMA-376-correct
    /// `w:pPr` child order. Two readings were possible — the renderer is wrong, or the FIXTURE is — and the
    /// measurement alone cannot distinguish them. This asks the SDK validator instead of trusting either.
    ///
    /// <para>A corpus fixture that is itself invalid makes every measurement taken against it ambiguous, so
    /// this is a real invariant rather than a diagnostic.</para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(CorpusDocumentNames))]
    public void CorpusFixture_IsSchemaValidWordprocessingML(string fileName)
    {
        var bytes = LoadCorpus(fileName);
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);

        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(doc)
            .Select(e => $"{e.Path?.XPath}: {e.Description}")
            .Take(10)
            .ToList();

        _output.WriteLine(errors.Count == 0
            ? $"{fileName}: schema-valid"
            : $"{fileName}: {errors.Count} error(s) :: " + string.Join(" :: ", errors));

        errors.Should().BeEmpty(
            $"[{fileName}] a corpus fixture that is itself schema-invalid makes every measurement taken " +
            "against it ambiguous");
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
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{fileName}' must project");
        return projection.Model!;
    }

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

    private static byte[] Render(byte[] carrier, ComposeContentModel model) =>
        new ComposeDocumentRenderer().RenderIntoCarrier(carrier, model, "loss-measurement");

    private static string ExtractBodyText(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.InnerText;
    }
}

/// <summary>
/// Collects the per-document edited-block result and writes one JSON file at the end of the class run, the
/// same shape the merge measurement uses. The note is written from this, not from scrollback.
/// </summary>
public sealed class EditedBlockLossSink : IDisposable
{
    private const string ResultFileName = "edited-block-loss.json";

    private readonly List<object> _rows = new();
    private readonly Dictionary<string, int> _pathFrequency = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Record(string document, int blockIndex, List<string> differingPaths, bool isNearTier)
    {
        lock (_gate)
        {
            _rows.Add(new
            {
                document,
                blockIndex,
                intact = differingPaths.Count == 0,
                isNearTier,
                differingPaths,
            });

            foreach (var leaf in differingPaths.Select(LeafOf).Distinct(StringComparer.Ordinal))
            {
                _pathFrequency.TryGetValue(leaf, out var count);
                _pathFrequency[leaf] = count + 1;
            }
        }
    }

    private static string LeafOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    public void Dispose()
    {
        lock (_gate)
        {
            var payload = new
            {
                harness = "task-041 edited-block loss",
                documents = _rows.Count,
                intact = _rows.Count(r => (bool)r.GetType().GetProperty("intact")!.GetValue(r)!),
                lossesByConstruct = _pathFrequency.OrderByDescending(kv => kv.Value)
                    .Select(kv => new { construct = kv.Key, documents = kv.Value })
                    .ToArray(),
                detail = _rows,
            };

            File.WriteAllText(
                Path.Combine(AppContext.BaseDirectory, ResultFileName),
                JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
