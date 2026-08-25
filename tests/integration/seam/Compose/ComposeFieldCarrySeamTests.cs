// Task 049 (spaarkeai-compose-r8, FR-A10 residual) — Word FIELDS carried through an edited paragraph.
//
// Owner decision 2026-08-25: the residual-loss list is NOT signed off with fields on it. A field in the
// paragraph a user edits was being flattened to the text it happened to be displaying, so a cross-reference
// stopped being a cross-reference and became a frozen number that goes quietly wrong the moment the document
// renumbers. This file is the evidence that it no longer does.
//
// WHAT IS MEASURED, AND WHY THIS SHAPE:
//
//   * Over the REAL corpus document (`ref-cross-references.docx`), through the REAL renderer. A hand-built
//     XML fragment can be made to pass by a carry that only handles the fragment; the corpus document is the
//     one a lawyer actually opened, with `w:noProof` on its result run and a `\r \h` switch pair on its
//     instruction. ADR-038: seam tests measure the real seam.
//
//   * BOTH field FORMS, because they are different constructs in the file: `w:fldSimple` (one element, the
//     instruction an attribute) and the `w:fldChar` begin/instrText/separate/result/end RUN sequence. A carry
//     that normalised one into the other would silently rewrite what the document contains — the `Symbol`
//     rule from task 048, applied to a bigger construct.
//
//   * The bookmark TARGET, in the same save. A carried `REF` is only an improvement if its target survives;
//     if it does not, Word shows broken-reference text where resolved prose stood, and freezing would have
//     been the better outcome. The renderer's own remarks claimed "the model does not carry bookmarks" —
//     that comment predates task 041 and this test is the measurement that settles it rather than assuming.
//
//   * The classes deliberately NOT carried (nested, unterminated, instruction-less), each degrading to
//     today's flatten + named warning and never to a refusal (ADR-049 invariant 1).
//
// MAINTAIN-class (tests/integration/seam/** vertical-slice KEEP path, ADR-038).

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeFieldCarrySeamTests
{
    private readonly ITestOutputHelper _output;

    public ComposeFieldCarrySeamTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string EditMarker = " [edited]";

    /// <summary>The corpus document that carries both field forms AND their shared bookmark target.</summary>
    private const string CrossReferenceFixture = "ref-cross-references.docx";

    // Block layout of `ref-cross-references.docx` (verified against the fixture's own document.xml):
    //   0 — "Section 4. Confidentiality."  wrapped in bookmarkStart/End `_Ref_Confidentiality`
    //   1 — "As provided in Section {REF _Ref_Confidentiality \r \h}, the receiving party ..."  (w:fldSimple)
    //   2 — "See also page {PAGEREF _Ref_Confidentiality \h} of this Agreement."                (w:fldChar)
    private const int BookmarkBlock = 0;
    private const int SimpleFieldBlock = 1;
    private const int ComplexFieldBlock = 2;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (1) The carry — over the real corpus document, through the real renderer.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EditedParagraph_KeepsItsSimpleField_AsAFieldNotAsFrozenText()
    {
        var source = LoadCorpus(CrossReferenceFixture);

        var saved = RenderWithEditAt(source, SimpleFieldBlock, out var codes);

        var fields = SimpleFieldsIn(saved);
        fields.Should().HaveCount(1,
            "the user edited the paragraph the REF field sits in — the field must survive the save AS A " +
            "FIELD. Flattening it to its cached '4' leaves a number that is silently wrong the moment the " +
            "document renumbers, which in an executed agreement is worse than a visible error.");

        fields[0].Instruction.Should().Be(" REF _Ref_Confidentiality \\r \\h ",
            "the INSTRUCTION is the field's identity — carrying only the resolved display would re-author " +
            "a look-alike, exactly what the task-048 Symbol rule forbids");
        fields[0].CachedResult.Should().Be("4",
            "the cached result is what the reader currently sees; carrying it means the save changes " +
            "nothing on screen while restoring the field's ability to update");

        codes.Should().NotContain("field-flattened-to-text",
            "nothing was flattened, so nothing may be reported flattened — a warning for a loss that did " +
            "not happen trains users to ignore the ones that did");
    }

    [Fact]
    public void EditedParagraph_KeepsItsComplexField_AsTheFldCharSequenceItWas()
    {
        var source = LoadCorpus(CrossReferenceFixture);

        var saved = RenderWithEditAt(source, ComplexFieldBlock, out var codes);

        var complex = ComplexFieldsIn(saved);
        complex.Should().HaveCount(1,
            "the PAGEREF field is authored as a w:fldChar begin/instrText/separate/result/end run sequence " +
            "and must come back as one — normalising it into a w:fldSimple would rewrite the construct the " +
            "document contains, not preserve it");

        complex[0].Instruction.Should().Be(" PAGEREF _Ref_Confidentiality \\h ");
        complex[0].CachedResult.Should().Be("1");

        // The three control characters are what make it a field at all.
        CountIn(saved, "fldChar").Should().Be(3, "begin, separate and end must all be re-emitted");
        CountIn(saved, "instrText").Should().Be(1);

        codes.Should().NotContain("field-flattened-to-text");
    }

    [Fact]
    public void UntouchedParagraph_KeepsItsFields_Unchanged()
    {
        // The control arm. A construct in a block the user did not touch is CLONED, so this holds whether or
        // not the carry exists — asserted so a regression in the carry cannot be mistaken for one here.
        var source = LoadCorpus(CrossReferenceFixture);

        var saved = RenderWithEditAt(source, BookmarkBlock, out _);

        CountIn(saved, "fldSimple").Should().Be(CountIn(source, "fldSimple"));
        CountIn(saved, "fldChar").Should().Be(CountIn(source, "fldChar"));
        CountIn(saved, "instrText").Should().Be(CountIn(source, "instrText"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (2) The carry is only an improvement if the TARGET survives. This is the measurement that
    //     replaces the renderer's stale "the model does not carry bookmarks" claim with a number.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EditedBookmarkParagraph_StillCarriesTheTarget_SoACarriedRefResolves()
    {
        var source = LoadCorpus(CrossReferenceFixture);

        // The WORST case for a carried REF: the user edits the paragraph the target bookmark lives in, so
        // the target is re-authored rather than cloned. If it did not survive here, carrying REF live would
        // trade resolved prose for Word's broken-reference text and freezing would be the better outcome.
        var saved = RenderWithEditAt(source, BookmarkBlock, out _);

        var names = BookmarkNamesIn(saved);
        names.Should().Contain("_Ref_Confidentiality",
            "task 041's CarryBookmarks restores the base block's bookmarks onto the rendered paragraph. " +
            "This is the evidence that the renderer's 011-P4/P9 remark ('the model does not carry " +
            "bookmarks') is STALE — and the whole basis for carrying REF/PAGEREF live rather than freezing " +
            "them.");

        // …and both fields, in the two blocks that were NOT edited, still point at it.
        SimpleFieldsIn(saved).Should().ContainSingle()
            .Which.Instruction.Should().Contain("_Ref_Confidentiality");
        ComplexFieldsIn(saved).Should().ContainSingle()
            .Which.Instruction.Should().Contain("_Ref_Confidentiality");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (3) The classes deliberately NOT carried. Each degrades to today's flatten + named warning.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NestedField_KeepsFlattening_BecauseTheInstructionCannotBeRecoveredIntact()
    {
        // `{ IF { PAGE } = 1 "First page" "Later page" }`. The projection's field scan folds an inner field
        // into the outer one's span, so the instruction text recoverable from the outer scan is a
        // CONCATENATION — re-emitting it would author a different field than the document contained. That is
        // the same defect as re-authoring a resolved glyph, so this class stays frozen and says so.
        var source = BuildSynthetic(
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + "<w:r><w:instrText xml:space=\"preserve\"> IF </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + "<w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
            + "<w:r><w:t>1</w:t></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"
            + "<w:r><w:instrText xml:space=\"preserve\"> = 1 \"First page\" \"Later page\" </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
            + "<w:r><w:t>First page</w:t></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>");

        var saved = RenderWithEditAt(source, 1, out var codes);

        codes.Should().Contain("field-flattened-to-text",
            "a class we do not carry must still be REPORTED — an undocumented loss is the failure mode " +
            "FR-A10 exists to end");
        CountIn(saved, "fldChar").Should().Be(0, "the nested field was flattened, not half-re-emitted");
        TextOf(saved).Should().Contain("First page",
            "flattening keeps the visible text — invariant 1, a defined outcome, never a refusal");
    }

    [Fact]
    public void FieldWithNoRecoverableInstruction_DegradesToFlatten_NeverToARefusal()
    {
        // A w:fldChar sequence whose code phase carries no w:instrText at all. There is no identity to
        // carry, so the cached result is kept as prose and the loss is named.
        var source = BuildSynthetic(
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
            + "<w:r><w:t>orphaned result</w:t></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>");

        var act = () => RenderWithEditAt(source, 1, out _);
        act.Should().NotThrow("every save terminates in a defined outcome (ADR-049 invariant 1)");

        RenderWithEditAt(source, 1, out var codes);
        codes.Should().Contain("field-flattened-to-text");
    }

    [Fact]
    public void UnterminatedField_KeepsFlattening_AndIsNamedOnBothSurfaces()
    {
        // A w:fldChar begin with no end in the same paragraph — the shape a TOC or INDEX takes, whose result
        // spans paragraph marks. The scan never closes, so there is no complete field to carry.
        //
        // The two warning surfaces are DIFFERENT and both matter: the PROJECTION reports the anomaly it saw
        // (`field-unterminated`, at read time), while the SAVE reports the outcome the user got
        // (`field-flattened-to-text`, counted by ComposeBlockMerge comparing base against rendered). This
        // test pins both, because a carry that quietly stopped one of them would look clean in the other.
        var source = BuildSynthetic(
            "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
            + "<w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h </w:instrText></w:r>"
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
            + "<w:r><w:t>Table of contents entry</w:t></w:r>");

        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(source, CancellationToken.None);
        projection.Warnings.Select(w => w.Code).Should().Contain("field-unterminated");

        var saved = RenderWithEditAt(source, 1, out var codes);

        codes.Should().Contain("field-flattened-to-text",
            "the user's outcome is a flattened field, and the save must say so");
        CountIn(saved, "fldChar").Should().Be(0, "there was no complete field to re-emit");
        TextOf(saved).Should().Contain("Table of contents entry",
            "flattening keeps the visible text — a defined outcome, never a refusal");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (4) `w:fldLock` — the one thing that must NOT become live.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void LockedField_StaysLocked_SoAFrozenFieldIsNotSilentlyMadeLive()
    {
        // The author set w:fldLock precisely so this field never updates. Carrying the instruction while
        // dropping the lock would convert a deliberately-frozen field into a live one — the exact hazard
        // the per-class decision exists to avoid, expressed in the document's own mechanism rather than ours.
        var source = BuildSynthetic(
            "<w:fldSimple w:instr=\" DATE \\@ &quot;d MMMM yyyy&quot; \" w:fldLock=\"true\">"
            + "<w:r><w:t>1 March 2026</w:t></w:r></w:fldSimple>");

        var saved = RenderWithEditAt(source, 1, out _);

        var fields = SimpleFieldsIn(saved);
        fields.Should().ContainSingle();
        fields[0].Locked.Should().BeTrue(
            "w:fldLock is part of the field's identity; dropping it re-authors a frozen field as a live one");
        fields[0].Instruction.Should().Be(" DATE \\@ \"d MMMM yyyy\" ");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // (5) A POSTED model is client input reaching OOXML authoring — the recurring 021-F1/022-F1/024-F1
    //     finding class in this renderer. The carry must not become a way to author an unopenable file.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("", "control characters")]
    [InlineData("", "an empty instruction")]
    [InlineData("   ", "a whitespace-only instruction")]
    public void PostedFieldWithAnUnusableInstruction_FlattensToItsResult_AndTheFileStillOpens(
        string instruction, string why)
    {
        var source = LoadCorpus(CrossReferenceFixture);
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        var blocks = model.Blocks.ToList();
        blocks[SimpleFieldBlock] = blocks[SimpleFieldBlock] with
        {
            Runs = new[]
            {
                new ComposeInlineRun { Text = "As provided in Section " },
                new ComposeInlineRun
                {
                    Field = new ComposeField { Instruction = instruction, CachedResult = "4" },
                },
            },
        };

        var degradations = new List<ComposeProjectionWarning>();
        var saved = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        // The file OPENS — the property that matters. An XML-illegal character written into w:instr makes
        // the package unreadable, which is an UNDEFINED outcome and ADR-049 invariant 1 forbids it.
        var act = () => TextOf(saved);
        act.Should().NotThrow($"a posted field carrying {why} must never produce an unopenable package");

        TextOf(saved).Should().Contain("4",
            "the cached result is kept as prose — today's flatten, a defined outcome, never a refusal");
        degradations.Select(d => d.Code).Should().Contain("field-flattened-to-text",
            "the merge's base-vs-rendered count reports the field as lost, so the user is told");
    }

    [Fact]
    public void PostedFieldWithAnAbsurdlyLongInstruction_Flattens_RatherThanBeingTruncated()
    {
        // Truncating would author a DIFFERENT field, which is the look-alike defect the carry exists to
        // avoid. Refusing the carry keeps the outcome honest and bounded.
        var source = LoadCorpus(CrossReferenceFixture);
        var model = new ComposeDocxProjectionBuilder()
            .BuildContentModel(source, CancellationToken.None).Model!;

        var blocks = model.Blocks.ToList();
        blocks[SimpleFieldBlock] = blocks[SimpleFieldBlock] with
        {
            Runs = new[]
            {
                new ComposeInlineRun
                {
                    Field = new ComposeField
                    {
                        Instruction = " REF " + new string('X', 8192) + " ",
                        CachedResult = "4",
                    },
                },
            },
        };

        var degradations = new List<ComposeProjectionWarning>();
        var saved = new ComposeDocumentRenderer()
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        SimpleFieldsIn(saved).Should().BeEmpty("an over-long instruction is refused, not shortened");
        TextOf(saved).Should().Contain("4");
        degradations.Select(d => d.Code).Should().Contain("field-flattened-to-text");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private readonly record struct FieldFacts(string Instruction, string CachedResult, bool Locked);

    private static byte[] LoadCorpus(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private byte[] RenderWithEditAt(byte[] source, int blockIndex, out List<string> codes)
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
            .RenderIntoCarrier(source, model with { Blocks = blocks }, "field-carry", degradations);

        codes = degradations.Select(d => d.Code).ToList();
        _output.WriteLine($"edit@{blockIndex} · codes: " +
                          (codes.Count == 0 ? "(none)" : string.Join(", ", codes)));
        return rendered;
    }

    /// <summary>Every <c>w:fldSimple</c> in the saved body, as instruction + cached display text.</summary>
    private static List<FieldFacts> SimpleFieldsIn(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return new List<FieldFacts>();

        return body.Descendants<SimpleField>()
            .Select(sf => new FieldFacts(
                sf.Instruction?.Value ?? string.Empty,
                string.Concat(sf.Descendants<Text>().Select(t => t.Text)),
                sf.FieldLock?.Value == true))
            .ToList();
    }

    /// <summary>
    /// Every complete <c>w:fldChar</c> begin/…/end sequence in the saved body, reassembled by walking each
    /// paragraph's direct children — the same shape the projection scans, so a sequence that came back
    /// half-formed reads as absent here rather than silently passing.
    /// </summary>
    private static List<FieldFacts> ComplexFieldsIn(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var body = doc.MainDocumentPart?.Document?.Body;
        var found = new List<FieldFacts>();
        if (body is null) return found;

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            var depth = 0;
            var instruction = new System.Text.StringBuilder();
            var result = new System.Text.StringBuilder();
            var locked = false;
            var inResult = false;

            foreach (var run in paragraph.Elements<Run>())
            {
                var fldChar = run.GetFirstChild<FieldChar>();
                if (fldChar is not null)
                {
                    var type = fldChar.FieldCharType?.Value;
                    if (type == FieldCharValues.Begin)
                    {
                        depth++;
                        locked |= fldChar.FieldLock?.Value == true;
                    }
                    else if (type == FieldCharValues.Separate)
                    {
                        inResult = true;
                    }
                    else if (type == FieldCharValues.End && depth > 0)
                    {
                        depth--;
                        if (depth == 0)
                        {
                            found.Add(new FieldFacts(instruction.ToString(), result.ToString(), locked));
                            instruction.Clear();
                            result.Clear();
                            locked = false;
                            inResult = false;
                        }
                    }
                    continue;
                }

                if (depth == 0) continue;
                if (inResult)
                {
                    foreach (var t in run.Elements<Text>()) result.Append(t.Text);
                }
                else
                {
                    foreach (var i in run.Elements<FieldCode>()) instruction.Append(i.Text);
                }
            }
        }

        return found;
    }

    private static List<string> BookmarkNamesIn(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body?
            .Descendants<BookmarkStart>()
            .Select(b => b.Name?.Value ?? string.Empty)
            .ToList() ?? new List<string>();
    }

    private static int CountIn(byte[] docx, string localName)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body is { } body
            ? body.Descendants().Count(e => e.LocalName == localName)
            : 0;
    }

    private static string TextOf(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart?.Document?.Body is { } body
            ? string.Concat(body.Descendants<Text>().Select(t => t.Text))
            : string.Empty;
    }

    /// <summary>
    /// A three-block package with <paramref name="inlineMarkup"/> in block[1]. Authored as a raw OPC package
    /// for the same reason <c>ComposeResidualLossParityTests</c> is: <c>Body.InnerXml</c> parses without the
    /// element's namespace declarations in scope, so prefixed markup cannot be injected through the SDK
    /// object model.
    /// </summary>
    private static byte[] BuildSynthetic(string inlineMarkup)
    {
        const string decl = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";

        static string Para(string paraId, string children) =>
            $"<w:p w14:paraId=\"{paraId}\" w14:textId=\"{paraId}\">{children}</w:p>";

        var body =
            Para("4B000001", "<w:r><w:t xml:space=\"preserve\">Opening paragraph.</w:t></w:r>")
            + Para("4B000002", "<w:r><w:t xml:space=\"preserve\">Carrier. </w:t></w:r>" + inlineMarkup)
            + Para("4B000003", "<w:r><w:t xml:space=\"preserve\">Closing paragraph.</w:t></w:r>")
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/>"
            + "<w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr>";

        var document = decl
            + "<w:document"
            + " xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\""
            + " xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\""
            + " xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\""
            + " xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\""
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
}
