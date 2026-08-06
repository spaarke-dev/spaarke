// Task 011 (spaarkeai-compose-r6, FR-03) — the CARRIER render seam slice: acceptance verifier for
// ComposeDocumentRenderer.RenderIntoCarrier — the render-on-save author for IMPORTED documents
// (replace the body from the canonical model; preserve every other part). Pairs with task 020's
// ComposeCanonicalModelRoundTripSeamTests (the model half); the 010 cutover wires SaveAsync to this.
//
// What this slice proves, per corpus doc:
//   1. Preserve-parts: every carrier part except /word/document.xml (and the merged numbering part)
//      survives the body swap BYTE-IDENTICAL — styles, headers/footers, theme, settings, fonts. This
//      is the fidelity property that makes render-on-save safe for imported docs (the UAT #1A SEV-1
//      class: re-authoring from the thin model dropped headers/footers/styles — the carrier render
//      retains them by construction).
//   2. Round-trip: the carrier-rendered body re-projects with the SAME top-level block-kind sequence
//      as the model that was rendered (fixed point, carrier edition).
//   3. Final-section fidelity: the trailing body sectPr (page setup + header/footer references) is
//      preserved through the swap.
//   4. Collision-safe numbering merge: rendering ordered/bullet lists into a carrier that already
//      carries a NumberingDefinitionsPart allocates instance ids ABOVE the carrier's own, references
//      remapped abstracts, and leaves every carrier-owned abstract/instance untouched.
//   5. Unique paraIds on every rendered paragraph (E2 substrate; count-gate condition impossible).
//   6. Degenerate inputs: empty model renders a valid empty-body package; malformed carrier throws
//      ComposePatchException (never a corrupt package out).
//
// NEGATIVE (ADR-038 banned patterns): NO Mock<HttpMessageHandler>, NO DI-registration test, NO
// ctor-null test. Drives the pure production components directly (ComposeReadFidelityHarnessSeamTests
// precedent); corpus glob-enumerated via ComposeCorpusFixtureLocator.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeCarrierRenderSeamTests
{
    /// <summary>xUnit [MemberData] source — every corpus `.docx`, glob-enumerated (not hardcoded).</summary>
    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void RenderIntoCarrier_OnEveryCorpusDoc_PreservesAllNonBodyPartsByteIdentical(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);
        var builder = new ComposeDocxProjectionBuilder();

        var projection = builder.BuildContentModel(original);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' must project");

        var rendered = new ComposeDocumentRenderer().RenderIntoCarrier(original, projection.Model, author: "seam-test");

        // 1. Preserve-parts: every original part except document.xml (replaced body) and numbering.xml
        //    (legitimately merged when the model carries lists) must be present AND byte-identical.
        var originalParts = ReadPartBytes(original);
        var renderedParts = ReadPartBytes(rendered);
        var replaceable = new[] { "/word/document.xml", "/word/numbering.xml" };
        foreach (var (uri, bytes) in originalParts)
        {
            renderedParts.Should().ContainKey(uri, $"'{docName}' part '{uri}' must survive the body swap");
            if (!replaceable.Contains(uri, StringComparer.OrdinalIgnoreCase))
            {
                renderedParts[uri].AsSpan().SequenceEqual(bytes).Should().BeTrue(
                    $"'{docName}' part '{uri}' must be BYTE-IDENTICAL after the body swap (preserve-parts contract)");
            }
        }

        // 2. Round-trip fixed point (carrier edition): the rendered body re-projects with the same
        //    top-level block-kind sequence as the rendered model.
        var reprojection = builder.BuildContentModel(rendered);
        reprojection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' carrier render must re-project");
        reprojection.Model.Blocks.Select(b => b.Kind).Should().Equal(
            projection.Model.Blocks.Select(b => b.Kind),
            $"'{docName}' block-kind sequence must survive the carrier model→docx→model cycle");

        // 3. Final-section fidelity: the trailing body sectPr survives the swap outer-XML-identical.
        var originalSectPr = TrailingSectPrXml(original);
        if (originalSectPr is not null)
        {
            TrailingSectPrXml(rendered).Should().Be(originalSectPr,
                $"'{docName}' final-section page setup (incl. header/footer references) must survive the body swap");
        }

        // 5. Unique paraIds on every rendered paragraph.
        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var paraIds = doc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value?.ToUpperInvariant())
            .ToList();
        paraIds.Should().NotContainNulls($"'{docName}' — every rendered paragraph must carry a w14:paraId");
        paraIds.Should().OnlyHaveUniqueItems($"'{docName}' — the count-gate's duplicate-id condition cannot exist on this path");
    }

    [Fact]
    public void RenderIntoCarrier_WithListsIntoNumberedCarrier_MergesCollisionFree()
    {
        // A carrier that already carries numbering definitions: rendering NEW lists into it must
        // allocate ABOVE its ids and leave every carrier-owned definition untouched.
        var carrierPath = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .FirstOrDefault(p => HasNumberingPart(ComposeCorpusFixtureLocator.LoadVerifiedBytes(p)));
        carrierPath.Should().NotBeNull("the corpus contains numbering exemplars (R4.5) — at least one doc must carry a numbering part");
        var carrier = ComposeCorpusFixtureLocator.LoadVerifiedBytes(carrierPath!);

        (int maxNumId, int maxAbstractId, int instanceCount, int abstractCount) before = NumberingStats(carrier);

        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                OrderedItem("first item", startsNewList: true),
                OrderedItem("second item"),
                new ComposeBlock { Kind = ComposeBlockKind.Paragraph, Runs = new[] { new ComposeInlineRun { Text = "interruption" } } },
                OrderedItem("restarted item", startsNewList: true),
                new ComposeBlock { Kind = ComposeBlockKind.ListItem, Ordered = false, Runs = new[] { new ComposeInlineRun { Text = "bullet" } } },
            },
        };

        var rendered = new ComposeDocumentRenderer().RenderIntoCarrier(carrier, model, author: "seam-test");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var numbering = doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;

        // Every carrier-owned abstract/instance still present (count can only grow).
        numbering.Elements<NumberingInstance>().Count().Should().BeGreaterThan(before.instanceCount,
            "the merge appends new instances");
        numbering.Elements<AbstractNum>().Count().Should().Be(before.abstractCount + 2,
            "exactly the ordered + bullet renderer abstracts are appended");

        // New instances allocate strictly above the carrier's max numId and reference remapped abstracts
        // strictly above the carrier's max abstractNumId.
        var newInstances = numbering.Elements<NumberingInstance>()
            .Where(n => n.NumberID!.Value > before.maxNumId)
            .ToList();
        newInstances.Should().HaveCount(3, "two restart-scoped ordered lists + one shared bullet instance");
        newInstances.Should().OnlyContain(n => n.AbstractNumId!.Val!.Value > before.maxAbstractId,
            "merged instances must reference the REMAPPED renderer abstracts, never a carrier abstract");

        // Every rendered list paragraph references a merged (post-carrier) instance id.
        var bodyNumIds = doc.MainDocumentPart.Document!.Body!
            .Descendants<Paragraph>()
            .Select(p => p.ParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();
        bodyNumIds.Should().HaveCount(4, "three ordered items + one bullet item carry direct numPr");
        bodyNumIds.Should().OnlyContain(id => id > before.maxNumId,
            "a rendered list must never capture a carrier num definition (collision-safety contract)");
    }

    [Fact]
    public void RenderIntoCarrier_WithEmptyModel_ProducesValidEmptyBodyPackage()
    {
        var carrier = ComposeCorpusFixtureLocator.LoadVerifiedBytes(
            ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First());

        var rendered = new ComposeDocumentRenderer().RenderIntoCarrier(carrier, new ComposeContentModel(), author: "seam-test");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        body.Elements<Paragraph>().Should().BeEmpty("an empty model renders an empty body");
        body.Elements<SectionProperties>().Should().HaveCount(1, "the final section's page setup survives");
    }

    [Fact]
    public void RenderIntoCarrier_WithMalformedCarrier_ThrowsComposePatchException()
    {
        var renderer = new ComposeDocumentRenderer();
        var act = () => renderer.RenderIntoCarrier(new byte[] { 0x00, 0x01, 0x02, 0x03 }, new ComposeContentModel(), author: "x");
        act.Should().Throw<ComposePatchException>("a malformed carrier must fail loudly, never emit a corrupt package");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────

    private static ComposeBlock OrderedItem(string text, bool startsNewList = false) => new()
    {
        Kind = ComposeBlockKind.ListItem,
        Ordered = true,
        StartsNewList = startsNewList,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static Dictionary<string, byte[]> ReadPartBytes(byte[] package)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(package, writable: false), isEditable: false);
        var parts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in doc.Parts.SelectMany(p => Flatten(p.OpenXmlPart)).DistinctBy(p => p.Uri.ToString()))
        {
            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            parts[part.Uri.ToString()] = ms.ToArray();
        }
        return parts;
    }

    private static IEnumerable<OpenXmlPart> Flatten(OpenXmlPart part)
    {
        yield return part;
        foreach (var child in part.Parts.SelectMany(p => Flatten(p.OpenXmlPart)))
        {
            yield return child;
        }
    }

    private static string? TrailingSectPrXml(byte[] package)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(package, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body?.Elements<SectionProperties>().LastOrDefault()?.OuterXml;
    }

    private static bool HasNumberingPart(byte[] package)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(package, writable: false), isEditable: false);
        return doc.MainDocumentPart?.NumberingDefinitionsPart?.Numbering?.Elements<NumberingInstance>().Any() == true;
    }

    private static (int, int, int, int) NumberingStats(byte[] package)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(package, writable: false), isEditable: false);
        var numbering = doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;
        return (
            numbering.Elements<NumberingInstance>().Max(n => n.NumberID!.Value),
            numbering.Elements<AbstractNum>().Max(a => a.AbstractNumberId!.Value),
            numbering.Elements<NumberingInstance>().Count(),
            numbering.Elements<AbstractNum>().Count());
    }
}
