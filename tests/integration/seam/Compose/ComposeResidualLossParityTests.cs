// Task 045 (spaarkeai-compose-r8, FR-A10) — the PARITY CHECK behind the published residual-loss list.
//
// The POML's word is "demonstrate the parity, do not assert it", and it is the whole value of the
// deliverable: a residual list that drifts from what the code does is a promise nobody is keeping. So this
// file does not read a table out of the source and compare it to a table in a document — that would only
// prove two lists match each other. It MEASURES each construct family through the real renderer and then
// holds the published document to what it measured, in BOTH directions:
//
//   • every family the merge degrades must be named in the document, with the exact code it emits
//   • every code the document names must be one the measurement actually produced (no phantom entries)
//   • every family the merge PRESERVES must NOT be listed as lost
//
// The third one is the direction people forget. A residual list that over-claims loss is not "safely
// conservative" — it tells users we damage things we do not, which is how a document stops being read.
//
// Each family is measured twice, because the merge's contract is per-block and the two halves have
// different answers: the construct in an UNTOUCHED block (cloned byte-verbatim, always preserved) and the
// same construct in the block the user EDITED (rendered — the only place loss can occur).
//
// MAINTAIN-class (tests/integration/seam/** vertical-slice KEEP path, ADR-038). This is the forcing
// function that keeps docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md honest as the renderer changes.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeResidualLossParityTests
{
    private readonly ITestOutputHelper _output;

    public ComposeResidualLossParityTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string EditMarker = " [edited]";

    /// <summary>The published write-side residual list. Path is asserted to exist — a parity check whose
    /// document is missing must fail, not silently pass.</summary>
    private const string PublishedListRelativePath = "docs/architecture/COMPOSE-WRITE-RESIDUAL-LOSS.md";

    /// <summary>
    /// One row per construct family: the key that selects its synthetic body, the OOXML local-name to
    /// count, and the degradation code the merge is expected to emit when the family's own block is
    /// edited — or <c>null</c> for a family that survives even that.
    /// </summary>
    public static IEnumerable<object?[]> Families() => new[]
    {
        // Task 049: both field forms are now CARRIED through an edit to their own block — the INSTRUCTION
        // round-trips as ComposeInlineRun.Field (with its cached result, so the display is unchanged), and
        // the FORM the document used is re-emitted rather than normalised. Kept in the table with a null
        // code so the preserve direction is enforced: a regression fails this row instead of going quiet.
        new object?[] { "fldSimple", "fldSimple", null },
        new object?[] { "fldChar", "fldChar", null },
        // …but NOT every field. A NESTED field's instruction cannot be recovered intact (the outer scan
        // folds the inner one in, so the string is a concatenation that would author neither field), so it
        // keeps flattening and keeps saying so. This row is what stops the retirement above from turning
        // `field-flattened-to-text` into a code the document names and the renderer never emits — the
        // accretion failure direction B exists to catch.
        new object?[] { "fldNested", "fldChar", "field-flattened-to-text" },
        // Task 056: all three embedded-object forms are now CARRIED through an edit to their own block. The
        // subtree round-trips as ComposeInlineRun.EmbeddedObject (opaque OuterXml, SDK-parse-gated) and,
        // when a posted model does not carry it — which is what a keystroke edit from the browser looks
        // like — ComposeBlockMerge restores it from the BASE block. Null code so the preserve direction is
        // enforced: a regression fails this row instead of going quiet.
        new object?[] { "drawing", "drawing", null },
        new object?[] { "object", "object", null },
        new object?[] { "pict", "pict", null },
        // …but NOT every object. A box that CARRIES TEXT has its text accept-flattened into the paragraph
        // as prose, so carrying the box as well would put the same sentence in the document twice. It keeps
        // today's outcome and keeps saying so. This row is what stops the three retirements above from
        // turning `complex-object-dropped` into a code the published document names and the renderer can no
        // longer emit — the accretion failure direction B exists to catch, exactly as `fldNested` does for
        // fields.
        new object?[] { "pictTextBox", "pict", "complex-object-dropped" },
        // Task 047b — the SAME loss, in a document where the edited block has an identically-projecting
        // TWIN. Every row above sits in a document whose three blocks all read differently, so the merge's
        // alignment is unambiguous and the loss report always has a base to diff against. Real documents are
        // not like that: consecutive empty paragraphs, repeated signature lines and duplicated callouts all
        // project to the same model, and there the longest common subsequence has SEVERAL maximum-length
        // answers. The one the traceback used to pick left the edited block with no base counterpart at all,
        // and the report — which diffs the render against its base — then had nothing to diff, so the box
        // vanished in COMPLETE SILENCE while the identical row above passed. A parity check that only ever
        // measures unambiguous documents cannot see that, which is why this row exists: it holds the
        // published list's never-silent promise at a block position where it was actually being broken.
        new object?[] { "pictTextBoxTwin", "pict", "complex-object-dropped" },
        new object?[] { "footnoteReference", "footnoteReference", "unrepresented-footnote-reference" },
        new object?[] { "endnoteReference", "endnoteReference", "unrepresented-endnote-reference" },
        // Task 046: a soft line break is now CARRIED, not lost — it round-trips as an IsLineBreak
        // marker run. Kept in the table with a null code so the preserve direction is enforced: if a
        // future change starts dropping breaks again, this row fails rather than going quiet.
        new object?[] { "br", "br", null },
        // Task 048: both are now CARRIED through an edit to their own block, like `br` above — a tab
        // round-trips as an IsTab marker run and a symbol as ComposeInlineRun.Symbol (font + code point
        // verbatim, NOT the glyph the reader resolved for display). Kept in the table with a null code so
        // the preserve direction is enforced: a regression fails this row instead of going quiet, and the
        // doc-parity half below fails if the published list still calls either one lost.
        new object?[] { "sym", "sym", null },
        new object?[] { "tab", "tab", null },
        // Preserved even in the EDITED block — task 041's two carries. Listing them here is what makes the
        // over-claim direction testable: if the published document ever calls these lost, parity fails.
        new object?[] { "bookmark", "bookmarkStart", null },
        new object?[] { "sdt", "sdt", "hard-tier-sdt-flattened" },
    };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (1) The measurement: what each family actually does, in both block positions.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(Families))]
    public void ConstructFamily_BehavesAsTheResidualListDescribes(
        string familyKey, string localName, string? expectedCode)
    {
        var source = BuildDocument(familyKey);
        CountIn(source, localName).Should().BeGreaterThan(0,
            $"[{familyKey}] the synthetic document must actually contain <w:{localName}> — a parity check " +
            "over an absent construct proves nothing");
        AssertTwinProjectsIdentically(familyKey, source);

        // ── Half 1: the construct sits in an UNTOUCHED block. Always preserved, byte-verbatim. ──
        var untouched = RenderWithEditAt(source, blockIndex: 0, out var untouchedCodes);
        CountIn(untouched, localName).Should().Be(CountIn(source, localName),
            $"[{familyKey}] a construct in a block the user did not touch is CLONED, so it survives " +
            "whether or not the renderer understands it — this is the invariant the whole residual list " +
            "is scoped by, and it holds for every family without exception");
        untouchedCodes.Should().NotContain(c => c == expectedCode,
            $"[{familyKey}] nothing was lost, so nothing may be reported lost — a warning here would train " +
            "the user to ignore the warnings that matter");

        // ── Half 2: the construct sits in the block the user EDITED. The only place loss can occur. ──
        var edited = RenderWithEditAt(source, blockIndex: 1, out var editedCodes);
        var after = CountIn(edited, localName);
        var before = CountIn(source, localName);

        _output.WriteLine(
            $"{familyKey,-20} untouched: {CountIn(untouched, localName)}/{before} kept · " +
            $"edited: {after}/{before} kept · codes: " +
            (editedCodes.Count == 0 ? "(none)" : string.Join(", ", editedCodes)));

        if (expectedCode is null)
        {
            after.Should().Be(before,
                $"[{familyKey}] the residual list does NOT name this family as lost, so the merge must " +
                "still carry it through an edit to its own block (task 041's carry)");
        }
        else
        {
            after.Should().BeLessThan(before,
                $"[{familyKey}] the residual list names this family as lost on an edited block — if the " +
                "merge now preserves it, the list OVER-CLAIMS and must be corrected");
            editedCodes.Should().Contain(expectedCode,
                $"[{familyKey}] the loss must be reported as '{expectedCode}'. A loss the user is not told " +
                "about is the failure mode this project exists to end.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (2) The parity: the published document against the measurement, in both directions.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PublishedResidualList_IsInParityWithTheMeasuredBehaviour()
    {
        var path = Path.Combine(RepoRoot(), PublishedListRelativePath);
        File.Exists(path).Should().BeTrue(
            $"the published residual list must exist at {PublishedListRelativePath} — FR-A10's deliverable " +
            "is the document, and a parity check with nothing to check against passes vacuously");

        var published = File.ReadAllText(path);

        var measuredLossCodes = new SortedSet<string>(StringComparer.Ordinal);
        var preservedFamilies = new List<string>();

        foreach (var row in Families())
        {
            var familyKey = (string)row[0]!;
            var localName = (string)row[1]!;
            var expectedCode = (string?)row[2];

            var source = BuildDocument(familyKey);
            RenderWithEditAt(source, blockIndex: 1, out var codes);

            if (expectedCode is not null)
            {
                codes.Should().Contain(expectedCode);
                measuredLossCodes.Add(expectedCode);
            }
            else
            {
                preservedFamilies.Add(localName);
            }
        }

        // Direction A — every measured loss is documented, by its exact code.
        foreach (var code in measuredLossCodes)
        {
            published.Should().Contain(code,
                $"the merge emits '{code}' but the published residual list never names it. An undocumented " +
                "loss is exactly what FR-A10 exists to prevent — the list is the contract users rely on.");
        }

        // Direction B — every code the document names is one the merge actually emits. This is the
        // direction that catches a list which drifts by ACCRETION: codes retired from the renderer but left
        // in the document, which quietly turn the contract into fiction.
        foreach (var documented in ExtractDegradationCodes(published))
        {
            measuredLossCodes.Should().Contain(documented,
                $"the published list names '{documented}', but no measured family produces it. Either the " +
                "renderer stopped emitting it (the list is stale) or it was never real (the list " +
                "over-claims). Both make the document less trustworthy than no document.");
        }

        _output.WriteLine(
            $"parity: {measuredLossCodes.Count} measured loss code(s) documented · " +
            $"{preservedFamilies.Count} preserved family(ies) not listed as lost · " +
            $"codes: {string.Join(", ", measuredLossCodes)}");
    }

    /// <summary>
    /// Pulls degradation codes out of the published markdown. They are written as inline code spans in the
    /// residual table (`` `code-name` ``); this reads exactly the kebab-case ones that look like warning
    /// codes, so prose and file names in backticks do not become false parity failures.
    /// </summary>
    private static IEnumerable<string> ExtractDegradationCodes(string markdown)
    {
        var known = new[]
        {
            "field-flattened-to-text", "complex-object-dropped", "unrepresented-footnote-reference",
            "unrepresented-endnote-reference", "edited-paragraph-line-break-dropped", "symbol-flattened",
            "tab-flattened", "hard-tier-sdt-flattened", "content-control-flattened",
        };
        // Only TABLE ROWS count as a claim. The document also discusses codes in prose — including, by
        // design, the story of a code that USED to be emitted and no longer is — and scanning prose would
        // make an honest explanation of a fixed loss read as a fresh claim of it. (Found the hard way: the
        // paragraph explaining that `edited-paragraph-line-break-dropped` had been fixed failed this check
        // by containing the words it was retiring. Same shape as the I-7 audit tripping on a comment.)
        var rows = markdown.Split('\n').Where(l => l.TrimStart().StartsWith('|')).ToList();
        return known.Where(k => rows.Any(r => r.Contains(k, StringComparison.Ordinal)));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Synthetic documents — three blocks, with the construct in block[1].
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private static string BodyFor(string familyKey) => familyKey switch
    {
        "fldSimple" => "<w:fldSimple w:instr=\" PAGE \"><w:r><w:t>1</w:t></w:r></w:fldSimple>",
        "fldChar" => "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
                     + "<w:r><w:instrText xml:space=\"preserve\"> REF _Ref1 \\h </w:instrText></w:r>"
                     + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>",
        // `{ IF { PAGE } = 1 "First" "Later" }` — a field whose instruction contains another field.
        "fldNested" => "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
                       + "<w:r><w:instrText xml:space=\"preserve\"> IF </w:instrText></w:r>"
                       + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
                       + "<w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>"
                       + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
                       + "<w:r><w:t>1</w:t></w:r>"
                       + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
                       + "<w:r><w:instrText xml:space=\"preserve\"> = 1 \"First\" \"Later\" </w:instrText></w:r>"
                       + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
                       + "<w:r><w:t>First</w:t></w:r>"
                       + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>",
        "drawing" => "<w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">"
                     + "<wp:extent cx=\"914400\" cy=\"914400\"/><wp:docPr id=\"1\" name=\"P\"/>"
                     + "<a:graphic><a:graphicData uri=\"x\"/></a:graphic></wp:inline></w:drawing></w:r>",
        "object" => "<w:r><w:object w:dxaOrig=\"100\" w:dyaOrig=\"100\">"
                    + "<v:shape id=\"s1\" style=\"width:10pt;height:10pt\"/></w:object></w:r>",
        "pict" => "<w:r><w:pict><v:shape id=\"s2\" style=\"width:10pt;height:10pt\"/></w:pict></w:r>",
        // A VML shape wrapping a TEXT BOX — the shape `interior-text-boxes.docx` takes. Its interior text is
        // accept-flattened into the paragraph, so the box itself is deliberately not carried.
        "pictTextBox" or "pictTextBoxTwin" => PictTextBox("s3", "4B000001"),
        "footnoteReference" => "<w:r><w:footnoteReference w:id=\"2\"/></w:r>",
        "endnoteReference" => "<w:r><w:endnoteReference w:id=\"2\"/></w:r>",
        "br" => "<w:r><w:t xml:space=\"preserve\">before</w:t><w:br/>"
                + "<w:t xml:space=\"preserve\">after</w:t></w:r>",
        "sym" => "<w:r><w:sym w:font=\"Symbol\" w:char=\"F0A7\"/></w:r>",
        "tab" => "<w:r><w:t xml:space=\"preserve\">left</w:t><w:tab/>"
                 + "<w:t xml:space=\"preserve\">right</w:t></w:r>",
        "sdt" => "<w:sdt><w:sdtPr><w:alias w:val=\"Effective Date\"/><w:id w:val=\"77\"/>"
                 + "<w:date/></w:sdtPr><w:sdtContent>"
                 + "<w:r><w:t xml:space=\"preserve\">1 March 2026</w:t></w:r>"
                 + "</w:sdtContent></w:sdt>",
        "bookmark" => "<w:bookmarkStart w:id=\"1\" w:name=\"_Ref1\"/>"
                      + "<w:r><w:t xml:space=\"preserve\">anchored text</w:t></w:r>"
                      + "<w:bookmarkEnd w:id=\"1\"/>",
        _ => throw new ArgumentOutOfRangeException(nameof(familyKey), familyKey, "unknown construct family"),
    };

    private static string PictTextBox(string shapeId, string interiorParaId) =>
        "<w:r><w:pict><v:shape id=\"" + shapeId + "\" type=\"#_x0000_t202\" "
        + "style=\"width:200pt;height:60pt\"><v:textbox><w:txbxContent>"
        + "<w:p w14:paraId=\"" + interiorParaId + "\" w14:textId=\"" + interiorParaId + "\">"
        + "<w:r><w:t xml:space=\"preserve\">Boxed line.</w:t></w:r></w:p>"
        + "</w:txbxContent></v:textbox></v:shape></w:pict></w:r>";

    /// <summary>
    /// An extra block, inserted directly AFTER the construct block, that projects to exactly the same content
    /// model as it while being a different block in the file. Empty for every family that does not need one.
    /// </summary>
    /// <remarks>
    /// The twin's own shape id and interior <c>paraId</c> differ, which is what makes a wrongly-cloned twin
    /// detectable in the saved package — but neither reaches the content model (the box's prose is
    /// accept-flattened and <c>paraId</c> is stripped from the merge's comparison key by design), so the two
    /// blocks are genuinely indistinguishable to the alignment. That indistinguishability IS the condition
    /// under test; <c>TwinBlocksProjectIdentically</c> asserts it rather than assuming it.
    /// </remarks>
    private static string TwinBlockFor(string familyKey) => familyKey switch
    {
        "pictTextBoxTwin" => Para(
            "4A000004",
            "<w:r><w:t xml:space=\"preserve\">Carrier. </w:t></w:r>" + PictTextBox("s3-twin", "4B000002")),
        _ => string.Empty,
    };

    private static byte[] BuildDocument(string familyKey)
    {
        // Written as a raw package rather than through the SDK object model. `Body.InnerXml` is parsed
        // without the element's own namespace declarations in scope, so prefixed markup (w14, wp, a, v)
        // cannot be injected that way — the same reason the corpus fixture generators author their
        // packages directly. Declaring every prefix once on <w:document> is what makes the bodies below
        // simple enough to read as the construct they are testing.
        var body =
            Para("4A000001", "<w:r><w:t xml:space=\"preserve\">Opening paragraph.</w:t></w:r>")
            + Para("4A000002",
                   "<w:r><w:t xml:space=\"preserve\">Carrier. </w:t></w:r>" + BodyFor(familyKey))
            + TwinBlockFor(familyKey)
            + Para("4A000003", "<w:r><w:t xml:space=\"preserve\">Closing paragraph.</w:t></w:r>")
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>"
            + "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr>";

        const string decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
        var document = decl
            + "<w:document"
            + " xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
            + " xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\""
            + " xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\""
            + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""
            + " xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\""
            + " xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\""
            + " xmlns:v=\"urn:schemas-microsoft-com:vml\""
            + " mc:Ignorable=\"w14\">"
            + "<w:body>" + body + "</w:body></w:document>";

        const string contentTypes = decl
            + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-"
            + "officedocument.wordprocessingml.document.main+xml\"/></Types>";

        const string rootRels = decl
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/"
            + "relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";

        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
                   ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(zip, "[Content_Types].xml", contentTypes);
            WriteEntry(zip, "_rels/.rels", rootRels);
            WriteEntry(zip, "word/document.xml", document);
        }
        return ms.ToArray();
    }

    private static void WriteEntry(System.IO.Compression.ZipArchive zip, string name, string content)
    {
        using var stream = zip.CreateEntry(name).Open();
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(content);
    }

    private static string Para(string paraId, string children) =>
        $"<w:p w14:paraId=\"{paraId}\" w14:textId=\"{paraId}\">{children}</w:p>";

    /// <summary>
    /// Anti-vacuity guard for a twin family (task 047b): the twin must really be indistinguishable from the
    /// construct block in the projected model. If a projection change ever gave the two blocks different
    /// content, the alignment would stop being ambiguous and the row would keep passing while measuring the
    /// ordinary case a row above it already covers — a test that quietly stops testing what it names.
    /// </summary>
    private static void AssertTwinProjectsIdentically(string familyKey, byte[] source)
    {
        if (TwinBlockFor(familyKey).Length == 0)
        {
            return;
        }

        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        projection.Model.Should().NotBeNull($"[{familyKey}] the twin document must project");

        var blocks = projection.Model!.Blocks;
        blocks.Count.Should().BeGreaterThan(2, $"[{familyKey}] the twin document must have a block[2] to be the twin");

        string TextOf(int index) => string.Concat(blocks[index].Runs.Select(r => r.Text));

        TextOf(2).Should().Be(TextOf(1),
            $"[{familyKey}] block[2] must project to the same content as block[1] — the alignment ambiguity " +
            "is the whole condition this family exists to exercise");
    }

    private static byte[] RenderWithEditAt(byte[] source, int blockIndex, out List<string> codes)
    {
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);

        var model = projection.Model!;
        var blocks = model.Blocks.ToList();
        blocks.Count.Should().BeGreaterThan(blockIndex);

        var runs = blocks[blockIndex].Runs.ToList();
        if (runs.Count == 0)
        {
            runs.Add(new ComposeInlineRun { Text = EditMarker.TrimStart() });
        }
        else
        {
            runs[0] = runs[0] with { Text = (runs[0].Text ?? string.Empty) + EditMarker };
        }
        blocks[blockIndex] = blocks[blockIndex] with { Runs = runs };

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "residual-parity", degradations);

        codes = degradations.Select(d => d.Code).ToList();
        return rendered;
    }

    private static int CountIn(byte[] docx, string localName)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body is { } body
            ? body.Descendants().Count(e => e.LocalName == localName)
            : 0;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null &&
               !File.Exists(Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("the repo root must be resolvable from the test output directory");
        return dir!.FullName;
    }
}
