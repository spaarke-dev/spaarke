// Task 056 (spaarkeai-compose-r8, FR-A10 residual) — EMBEDDED OBJECTS carried through an edited paragraph.
//
// Owner decision 2026-08-25: "if we can carry them that's the better solution". An image, chart or OLE
// embed in the paragraph a user edits was being REMOVED from that paragraph and reported
// `complex-object-dropped`. This file is the evidence that it no longer is — and, more importantly, the
// evidence that carrying it does not author a document Word reports as damaged.
//
// THE LOAD-BEARING QUESTION, AND WHY THESE TESTS OPEN THE PACKAGE:
//
//   A `w:drawing` refers to its image by RELATIONSHIP id (`r:embed="rId7"`), resolved against the MAIN
//   DOCUMENT PART's relationships — and this save replaces that part's body. The renderer's own remarks
//   call the parts left behind "orphaned … inert weight", which is ambiguous between "present WITH its
//   relationship" and "the relationship was pruned". Those two differ by document corruption: if the
//   relationship were pruned, a carried drawing would point at nothing and Word would report the file as
//   damaged — STRICTLY WORSE than the honest drop it replaces.
//
//   So the assertions here do not stop at "the element came back". They OPEN THE SAVED PACKAGE and
//   RESOLVE the relationship against the main part. A content-model round trip cannot prove a document
//   opens; only resolving the reference in the saved bytes can. (Measured answer: relationships SURVIVE —
//   the body swap never touches `word/_rels/document.xml.rels`. Recorded in
//   `projects/spaarkeai-compose-r8/notes/056-object-carry-decisions.md` §1.)
//
// TWO CARRY PATHS ARE MEASURED, because they cover different real situations:
//
//   * The MODEL carry (`ComposeInlineRun.EmbeddedObject`) — the object's own subtree round-trips through
//     the content model, so its exact INTRA-PARAGRAPH POSITION survives. This is the path every
//     server-side model round trip takes (the projection, an AI edit batch, the merge's own baseline
//     re-projection).
//   * The BASE carry (`ComposeBlockMerge.CarryUnmodeledConstructs`) — the object is restored from the
//     block's BASE counterpart when the posted model does not carry it. That is what a KEYSTROKE edit
//     from the browser looks like today: the editor's mapper drops an opaque atom, so the object never
//     reaches the posted model at all. Without this path the carry would be a producer with no consumer.
//
// MAINTAIN-class (tests/integration/seam/** vertical-slice KEEP path, ADR-038).

using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeObjectCarrySeamTests
{
    private readonly ITestOutputHelper _output;

    public ComposeObjectCarrySeamTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string EditMarker = " [edited]";

    /// <summary>The OOXML relationships namespace. EVERY attribute in it is a relationship reference —
    /// `r:id`, `r:embed`, `r:link`, `r:pict`, and the vendor ones — which is why the integrity check below
    /// is keyed on the NAMESPACE rather than on a list of attribute names it would have to keep current.</summary>
    private const string RelationshipNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // Each fixture: the file, the block index of the paragraph carrying the object, the OOXML local name,
    // and the relationship ids the object references.
    public static IEnumerable<object?[]> ObjectFixtures() => new[]
    {
        new object?[] { "inline-image.docx", 2, "drawing", new[] { "rIdImg" } },
        new object?[] { "chart-embedded.docx", 2, "drawing", new[] { "rIdChart" } },
        new object?[] { "ole-embedded-object.docx", 2, "object", new[] { "rIdImg", "rIdOle" } },
    };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (1) THE DECISIVE TEST — the object survives an edit to its own paragraph AND the saved package's
    //     relationship still resolves. This is the acceptance criterion, asserted on the saved bytes.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(ObjectFixtures))]
    public void EditedParagraph_KeepsItsObject_AndTheSavedPackageStillResolvesItsRelationship(
        string fixture, int objectBlock, string localName, string[] relationshipIds)
    {
        var source = LoadCorpus(fixture);

        var saved = RenderWithEditAt(source, objectBlock, out var codes);

        CountIn(saved, localName).Should().Be(CountIn(source, localName),
            $"[{fixture}] the user edited the paragraph the object sits in — the object must survive the " +
            "save. Removing it leaves the document's own bytes for the image sitting unreferenced in the " +
            "package while the page it belonged on has a hole in it.");

        // …and the reference must RESOLVE. An element that came back pointing at nothing is worse than
        // no element at all: Word reports the file as damaged rather than merely missing a picture.
        var resolved = ResolveRelationships(saved);
        foreach (var id in relationshipIds)
        {
            resolved.Should().ContainKey(id,
                $"[{fixture}] the carried object references '{id}'. If the body swap had pruned that " +
                "relationship, this carry would author a package Word reports as damaged — the exact " +
                "silent-damage regression R8 exists to end, arriving as a 'fix'.");
        }

        codes.Should().NotContain("complex-object-dropped",
            $"[{fixture}] nothing was dropped, so nothing may be reported dropped — a warning for a loss " +
            "that did not happen trains users to ignore the ones that did");

        _output.WriteLine(
            $"{fixture,-32} edited@{objectBlock} · {localName} {CountIn(saved, localName)}/{CountIn(source, localName)} kept · " +
            $"rels: {string.Join(", ", relationshipIds.Select(i => $"{i}->{(resolved.TryGetValue(i, out var t) ? t : "DANGLING")}"))}");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (2) THE CORRUPTION GUARD, stated as a property rather than a case: no save, at any edit position,
    //     may leave a relationship reference in the body that the package cannot resolve.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(ObjectFixtures))]
    public void NoEditPosition_EverLeavesADanglingRelationshipInTheSavedBody(
        string fixture, int objectBlock, string localName, string[] relationshipIds)
    {
        _ = objectBlock;
        _ = localName;
        _ = relationshipIds;

        var source = LoadCorpus(fixture);
        var blockCount = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!.Blocks.Count;

        for (var i = 0; i < blockCount; i++)
        {
            var saved = RenderWithEditAt(source, i, out _);
            var dangling = DanglingRelationshipReferences(saved);
            dangling.Should().BeEmpty(
                $"[{fixture}] editing block {i} left {string.Join(", ", dangling)} pointing at nothing. " +
                "A package Word reports as damaged is not a degradation, it is data loss with a dialog " +
                "box — invariant 1 requires a DEFINED outcome, and 'the file will not open' is not one.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (3) THE BASE CARRY — a KEYSTROKE edit. The editor's mapper drops an opaque atom, so the posted
    //     model carries NO object at all. Without this path the carry would never fire from the browser.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(ObjectFixtures))]
    public void KeystrokeEdit_WhoseModelCarriesNoObject_StillKeepsTheObject_FromTheBaseBlock(
        string fixture, int objectBlock, string localName, string[] relationshipIds)
    {
        var source = LoadCorpus(fixture);

        // The posted model with EVERY object marker stripped — exactly what `docxBridge.ts` posts today,
        // because an opaque atom contributes nothing to the rebuilt paragraph.
        var saved = RenderWithEditAt(source, objectBlock, out var codes, stripObjectsFromModel: true);

        CountIn(saved, localName).Should().Be(CountIn(source, localName),
            $"[{fixture}] the object is absent from the posted model, so the ONLY thing that can put it " +
            "back is the base block — the same mechanism task 041 already uses for bookmarks and content-" +
            "control shells. Shipping the model carry without this would ship a producer with no consumer.");

        var resolved = ResolveRelationships(saved);
        foreach (var id in relationshipIds)
        {
            resolved.Should().ContainKey(id,
                $"[{fixture}] a base-carried object references the CARRIER's own relationship, so it " +
                "resolves by construction — asserted rather than assumed");
        }

        codes.Should().NotContain("complex-object-dropped");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (4) The control arm. A construct in a block the user did NOT touch is cloned, so this holds whether
    //     or not the carry exists — asserted so a regression in the carry cannot be mistaken for one here.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(ObjectFixtures))]
    public void UntouchedParagraph_KeepsItsObject_Unchanged(
        string fixture, int objectBlock, string localName, string[] relationshipIds)
    {
        _ = objectBlock;
        _ = relationshipIds;
        var source = LoadCorpus(fixture);

        var saved = RenderWithEditAt(source, 0, out _);

        CountIn(saved, localName).Should().Be(CountIn(source, localName));
        DanglingRelationshipReferences(saved).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (5) A POSTED model is CLIENT INPUT reaching OOXML authoring — the recurring 021-F1/022-F1/024-F1
    //     finding class in this renderer. The carry must not become a way to author an unopenable file.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    // `mustNotAppear` is the distinctive token of each hostile payload, so the "never string-injected"
    // assertion is real for every row rather than vacuously true for four of five (null for the two rows
    // that have no content to look for).
    [Theory]
    [InlineData("<w:drawing><not closed", "malformed XML", "not closed")]
    [InlineData("<w:p xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:r><w:t>forged</w:t></w:r></w:p>", "a wrong-root element", "forged")]
    [InlineData("<script>alert(1)</script>", "markup that is not OOXML at all", "alert(1)")]
    [InlineData("", "an empty carry", null)]
    [InlineData("   ", "a whitespace-only carry", null)]
    public void PostedObjectXmlThatFailsTheParseGate_NeverReachesThePackage(
        string xml, string why, string? mustNotAppear)
    {
        var saved = RenderWithPostedObject("inline-image.docx", xml, out var codes);

        // The file OPENS — the property that matters. Anything else is a preference.
        var act = () => CountIn(saved, "drawing");
        act.Should().NotThrow($"a posted object carrying {why} must never produce an unopenable package");

        if (mustNotAppear is not null)
        {
            SavedBodyXml(saved).Should().NotContain(mustNotAppear,
                $"{why} must never be string-injected into the package — the SDK parse gate is the boundary");
        }

        // …and the OUTCOME is better than a drop, which is worth asserting rather than assuming: the
        // renderer refuses the unusable payload, and the merge then restores the document's OWN object from
        // the base block. A client cannot destroy an image by posting junk in its place.
        CountIn(saved, "drawing").Should().Be(1,
            $"a posted object carrying {why} is refused, and the base block still has the real one — the " +
            "two gates compose into 'the user's document is unchanged' rather than 'the user's document " +
            "loses its picture because a payload was malformed'");
        codes.Should().NotContain("complex-object-dropped",
            "nothing was lost, so nothing may be reported lost");
        DanglingRelationshipReferences(saved).Should().BeEmpty();
    }

    [Fact]
    public void PostedObjectReferencingARelationshipTheCarrierDoesNotHave_IsRefused_NotAuthored()
    {
        // THE test this task exists for. A well-formed, schema-valid `w:drawing` whose `r:embed` names a
        // relationship the carrier does not have. Authoring it produces a package that opens to Word's
        // "unreadable content" repair prompt — strictly worse than the honest drop it would replace.
        const string danglingDrawing =
            "<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
            "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
            "<wp:extent cx=\"914400\" cy=\"914400\"/><wp:docPr id=\"9\" name=\"Forged\"/>" +
            "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
            "<pic:pic><pic:nvPicPr><pic:cNvPr id=\"9\" name=\"f.png\"/><pic:cNvPicPr/></pic:nvPicPr>" +
            "<pic:blipFill><a:blip r:embed=\"rIdNotInThisPackage\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
            "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"914400\" cy=\"914400\"/></a:xfrm>" +
            "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>" +
            "</a:graphicData></a:graphic></wp:inline></w:drawing>";

        var saved = RenderWithPostedObject("chart-embedded.docx", danglingDrawing, out var codes);

        DanglingRelationshipReferences(saved).Should().BeEmpty(
            "the carry MUST verify every relationship the subtree references against the carrier before " +
            "authoring it. This is the whole difference between a carry and a corruption.");
        SavedBodyXml(saved).Should().NotContain("rIdNotInThisPackage",
            "a forged reference must not appear in the saved package in ANY form — not even inertly, where " +
            "the next tool to read the file would trip over it");
        SavedBodyXml(saved).Should().Contain("rIdChart",
            "and the document's OWN chart comes back from the base block, so a forged payload cannot be " +
            "used to delete content the user never touched");
        codes.Should().NotContain("complex-object-dropped");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (6) PAYLOAD — a carried drawing subtree travels in the content model, and ADR-040 caps an inline
    //     session payload at 128 KB, ABOVE WHICH `ProjectComposeOutputs` SKIPS the entry entirely (the
    //     save would vanish from the read projection rather than degrade). Measured, not assumed.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(ObjectFixtures))]
    public void CarriedObjectPayload_StaysWellInsideTheAdr040InlineCap(
        string fixture, int objectBlock, string localName, string[] relationshipIds)
    {
        _ = objectBlock;
        _ = localName;
        _ = relationshipIds;

        var source = LoadCorpus(fixture);
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        var withoutObjects = model with
        {
            Blocks = model.Blocks
                .Select(b => b with { Runs = b.Runs.Select(r => r with { EmbeddedObject = null }).ToList() })
                .ToList(),
        };

        var carried = JsonSerializer.SerializeToUtf8Bytes(model).Length;
        var baseline = JsonSerializer.SerializeToUtf8Bytes(withoutObjects).Length;
        const int cap = 128 * 1024; // SessionLedgerEntries.InlinePayloadCapBytes

        _output.WriteLine(
            $"{fixture,-32} model {baseline} -> {carried} bytes (+{carried - baseline}) · " +
            $"{100.0 * carried / cap:F1}% of the {cap}-byte ADR-040 inline cap");

        carried.Should().BeGreaterThan(baseline,
            $"[{fixture}] the measurement is only meaningful if the carry is actually IN the model — an " +
            "arm that measures nothing would report generous headroom forever");
        carried.Should().BeLessThan(cap / 4,
            $"[{fixture}] a carried object subtree is XML, not image BYTES — the picture itself stays in " +
            "its own package part and only the reference travels. If this ever approached the cap, the " +
            "save would be SKIPPED rather than degraded, which is why the headroom is asserted and not " +
            "merely noted.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (7) What is NOT carried. A box that CARRIES TEXT is a different family: its text is accept-flattened
    //     into the paragraph as prose, so carrying the box as well would put the same words in the document
    //     twice. Measured here as the NEGATIVE — the projection must not model it — while the loss and its
    //     warning code are held by the `pictTextBox` row in ComposeResidualLossParityTests, whose synthetic
    //     document this test would otherwise have to duplicate.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void TextCarryingBox_IsNotModelledAsACarriedObject_SoItsTextIsNeverDuplicated()
    {
        var source = LoadCorpus("interior-text-boxes.docx");
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        model.Blocks.SelectMany(b => b.Runs).Should().NotContain(r => r.EmbeddedObject != null,
            "the box's visible text is already carried as prose. Carrying the box as well would put the " +
            "same sentence in the saved document twice — a 'fix' that corrupts the paragraph it was meant " +
            "to protect.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private static byte[] LoadCorpus(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private byte[] RenderWithEditAt(
        byte[] source, int blockIndex, out List<string> codes, bool stripObjectsFromModel = false)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);

        var model = projection.Model!;
        var blocks = model.Blocks.ToList();
        blocks.Count.Should().BeGreaterThan(blockIndex);

        if (stripObjectsFromModel)
        {
            for (var i = 0; i < blocks.Count; i++)
            {
                blocks[i] = blocks[i] with
                {
                    Runs = blocks[i].Runs.Where(r => r.EmbeddedObject is null).ToList(),
                };
            }
        }

        var runs = blocks[blockIndex].Runs.ToList();
        var firstText = runs.FindIndex(r => r.EmbeddedObject is null && r.Field is null);
        if (firstText < 0)
        {
            runs.Add(new ComposeInlineRun { Text = EditMarker.TrimStart() });
        }
        else
        {
            runs[firstText] = runs[firstText] with { Text = (runs[firstText].Text ?? string.Empty) + EditMarker };
        }
        blocks[blockIndex] = blocks[blockIndex] with { Runs = runs };

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "object-carry", degradations);

        codes = degradations.Select(d => d.Code).ToList();
        _output.WriteLine($"edit@{blockIndex}{(stripObjectsFromModel ? " (model stripped)" : string.Empty)} · codes: " +
                          (codes.Count == 0 ? "(none)" : string.Join(", ", codes)));
        return rendered;
    }

    /// <summary>Renders <paramref name="fixture"/> with the object block's runs replaced by a single
    /// posted <c>EmbeddedObject</c> carrying <paramref name="xml"/> — the hostile-client shape.</summary>
    private static byte[] RenderWithPostedObject(string fixture, string xml, out List<string> codes)
    {
        var source = LoadCorpus(fixture);
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        var blocks = model.Blocks.ToList();
        blocks[2] = blocks[2] with
        {
            Runs = new[]
            {
                new ComposeInlineRun { Text = "posted " },
                new ComposeInlineRun { EmbeddedObject = new ComposeEmbeddedObject { Xml = xml } },
            },
        };

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "object-carry", degradations);
        codes = degradations.Select(d => d.Code).ToList();
        return rendered;
    }

    /// <summary>Every relationship the saved package's MAIN DOCUMENT PART can resolve, id → target.</summary>
    private static Dictionary<string, string> ResolveRelationships(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var main = doc.MainDocumentPart!;
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in main.Parts)
        {
            if (pair.RelationshipId is { Length: > 0 } id) map[id] = pair.OpenXmlPart.Uri.ToString();
        }
        foreach (var rel in main.ExternalRelationships)
        {
            if (rel.Id is { Length: > 0 } id) map[id] = rel.Uri.ToString();
        }
        foreach (var rel in main.HyperlinkRelationships)
        {
            if (rel.Id is { Length: > 0 } id) map[id] = rel.Uri.ToString();
        }
        return map;
    }

    /// <summary>Relationship references in the saved BODY that the package cannot resolve. Empty is the
    /// only acceptable answer — a non-empty result is a document Word reports as damaged.</summary>
    private static List<string> DanglingRelationshipReferences(byte[] docx)
    {
        var resolved = ResolveRelationships(docx);
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return new List<string>();

        var dangling = new List<string>();
        foreach (var element in new[] { (OpenXmlElement)body }.Concat(body.Descendants()))
        {
            foreach (var attribute in element.GetAttributes())
            {
                if (!string.Equals(attribute.NamespaceUri, RelationshipNamespace, StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(attribute.Value)) continue;
                if (!resolved.ContainsKey(attribute.Value))
                {
                    dangling.Add($"{element.LocalName}/@{attribute.LocalName}={attribute.Value}");
                }
            }
        }
        return dangling;
    }

    private static string SavedBodyXml(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body?.OuterXml ?? string.Empty;
    }

    private static int CountIn(byte[] docx, string localName)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return 0;
        return new[] { (OpenXmlElement)body }.Concat(body.Descendants())
            .Count(e => string.Equals(e.LocalName, localName, StringComparison.Ordinal));
    }
}
