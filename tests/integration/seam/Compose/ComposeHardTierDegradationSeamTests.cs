// Task 026 (spaarkeai-compose-r6, FR-04) — HARD-TIER GRACEFUL DEGRADATION: the guarantee that makes
// render-on-save safe to ship. Text boxes / drawings / fields / content controls — exactly what broke the
// surgical patcher on AppligentNDA_Signed.docx and produced the 422 — must ACCEPT-FLATTEN (visible
// text/content preserved in a degraded form) with a surfaced warning, and MUST NOT hard-fail.
//
// What 026 adds on top of the 020-025 loud-flatten baseline:
//  - TEXT-BOX VISIBLE TEXT is extracted (DrawingML wps:txbx / VML v:textbox / mc:AlternateContent —
//    the NDA's signature blocks): previously the box text dropped WITH the box chrome (counted but
//    content-lost via `complex-object-dropped`); now the text lands as a degraded run/paragraph at the
//    anchor position with the new `text-box-flattened` warning. mc:AlternateContent extracts exactly ONE
//    branch (Choice preferred) — the Fallback duplicates the same box (and its w14:paraIds — the NDA's
//    duplicate-id class), so a two-branch extraction would double the text.
//  - The RENDER-side degradation sink (ListRenderState.Warn → the public methods' optional
//    out-collection): filtered comment anchors (`comment-anchor-dropped` — the 024-routed loud counter),
//    failed format-change parse gates (`tracked-format-change-dropped` — 025-F4/F7 routing), and
//    unresolvable hrefs (`hyperlink-target-dropped`) are now COUNTED, and SaveAsync surfaces them as
//    success-with-warnings (SaveComposeDocumentResult.DegradationWarnings → response
//    `degradationWarnings` → the client's dismissible banner). NEVER a 422.
//
// NEGATIVE (ADR-038): NO Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using V = DocumentFormat.OpenXml.Vml;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeHardTierDegradationSeamTests
{
    private readonly ComposeDocxProjectionBuilder _builder = new();
    private readonly ComposeDocumentRenderer _renderer = new();

    private static Dictionary<string, int> ValidationErrorCounts(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(doc)
            .GroupBy(e => e.Description)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static string AllModelText(ComposeContentModel model)
    {
        static IEnumerable<string> BlockTexts(IEnumerable<ComposeBlock> blocks) =>
            blocks.SelectMany(b => b.Runs.Select(r => r.Text)
                .Concat(b.Table?.Rows.SelectMany(row => row.Cells.SelectMany(c => BlockTexts(c.Blocks)))
                    ?? Enumerable.Empty<string>()));
        return string.Join("\n", BlockTexts(model.Blocks));
    }

    // ── SDK-authored hard-tier fixtures ────────────────────────────────────────────────────────────

    private static byte[] BuildTextBoxSource()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();

            static Run VmlTextBoxRun(params string[] paragraphTexts) =>
                new(new Picture(new V.Shape(new V.TextBox(new TextBoxContent(
                    paragraphTexts.Select(t => (OpenXmlElement)new Paragraph(new Run(new Text(t)))).ToArray())))));

            main.Document = new Document(new Body(
                // P1: a VML text box directly in a run (w:pict > v:shape > v:textbox > w:txbxContent) —
                // IsComplexObjectRun's Picture arm, previously a full content drop.
                new Paragraph(
                    new Run(new Text("Anchor prose ") { Space = SpaceProcessingModeValues.Preserve }),
                    VmlTextBoxRun("By: ____________", "Name: A. Signer")),
                // P2: an INLINE mc:AlternateContent whose Choice AND Fallback carry the SAME box —
                // extraction must take exactly ONE branch or the text doubles (the NDA shape).
                new Paragraph(
                    new AlternateContent(
                        new AlternateContentChoice(VmlTextBoxRun("Choice-branch signature text")) { Requires = "wps" },
                        new AlternateContentFallback(VmlTextBoxRun("Choice-branch signature text")))),
                // P3: a TEXT-FREE drawing (pure image/shape) — keeps the established loud content drop.
                new Paragraph(
                    new Run(new Text("Logo: ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new Drawing())),
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Text-box visible text ACCEPT-FLATTENS (no content loss); text-free objects keep the loud drop.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Projection_TextBoxes_AcceptFlattenVisibleText_OneBranchOnly()
    {
        var projection = _builder.BuildContentModel(BuildTextBoxSource());

        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);
        var text = AllModelText(projection.Model);

        // P1: the VML box's text is PRESERVED at the anchor position (space-joined degraded form).
        text.Should().Contain("By: ____________");
        text.Should().Contain("Name: A. Signer");

        // P2: AlternateContent extracts exactly ONE branch — the duplicated Fallback must not double it.
        var occurrences = text.Split("Choice-branch signature text").Length - 1;
        occurrences.Should().Be(1, "Choice and Fallback duplicate the SAME box — extraction takes one branch");

        // Warnings: two text-carrying boxes still flatten LOUDLY — their text is preserved as prose, so
        // carrying the box as well would put the same words in the document twice.
        projection.Warnings.Should().ContainSingle(w => w.Code == "text-box-flattened")
            .Which.Count.Should().Be(2);

        // Task 056 CHANGED the third one. The text-free drawing is no longer dropped: its subtree is
        // carried verbatim as a ComposeInlineRun.EmbeddedObject, so there is nothing to warn about and
        // warning anyway would be a false alarm on a construct that survived. This assertion was inverted
        // deliberately — the two halves are what make the pair meaningful, because "text boxes flatten" is
        // only interesting alongside "text-free objects do not".
        projection.Warnings.Should().NotContain(w => w.Code == "complex-object-dropped",
            "a text-free drawing is CARRIED since task 056 — reporting it dropped would be a warning for a " +
            "loss that did not happen");
        projection.Model!.Blocks.SelectMany(b => b.Runs)
            .Should().Contain(r => r.EmbeddedObject != null,
                "…and it is carried, not silently omitted — the positive half of the same claim");
    }

    [Fact]
    public void RoundTrip_TextBoxSource_RendersAndReprojectsWithoutHardFail()
    {
        var source = BuildTextBoxSource();
        var projection = _builder.BuildContentModel(source);

        var carrier = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test");
        var synthesized = _renderer.SynthesizeDocument(projection.Model, author: "seam-test");

        foreach (var rendered in new[] { carrier, synthesized })
        {
            var reprojection = _builder.BuildContentModel(rendered);
            reprojection.Status.Should().NotBe(ComposeProjectionStatus.Failed);
            AllModelText(reprojection.Model).Should().Contain("By: ____________",
                "the degraded box text must survive the full round-trip");
        }
    }

    [Fact]
    public void Projection_NestedAndMixedShapes_NoDuplication_NoSilentDirectTextLoss()
    {
        // Step-9.5 F1/F3 pins: (a) a branch that wraps its box runs in paragraphs (paragraph-in-scope
        // nesting) must not double any line; (b) a transitional MIXED run (own w:t + a text-carrying
        // pict) must keep BOTH its direct text and the box text.
        byte[] source;
        using (var stream = new MemoryStream())
        {
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                // Box-in-box: outer txbxContent paragraph hosts a run carrying ANOTHER textbox.
                var innerBoxRun = new Run(new Picture(new V.Shape(new V.TextBox(new TextBoxContent(
                    new Paragraph(new Run(new Text("INNER-LINE"))))))));
                var outerBox = new Run(new Picture(new V.Shape(new V.TextBox(new TextBoxContent(
                    new Paragraph(new Run(new Text("OUTER-LINE ") { Space = SpaceProcessingModeValues.Preserve }), innerBoxRun))))));
                // Mixed run: direct w:t + a text-carrying pict in the SAME run.
                var mixedRun = new Run(
                    new Text("DIRECT-TEXT ") { Space = SpaceProcessingModeValues.Preserve },
                    new Picture(new V.Shape(new V.TextBox(new TextBoxContent(
                        new Paragraph(new Run(new Text("MIXED-BOX-TEXT"))))))));

                main.Document = new Document(new Body(
                    new Paragraph(outerBox),
                    new Paragraph(mixedRun),
                    new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
                main.Document.Save();
            }
            source = stream.ToArray();
        }

        var projection = _builder.BuildContentModel(source);
        var text = AllModelText(projection.Model);

        (text.Split("INNER-LINE").Length - 1).Should().Be(1, "a nested box's line must not be extracted twice (F1)");
        (text.Split("OUTER-LINE").Length - 1).Should().Be(1);
        text.Should().Contain("DIRECT-TEXT", "a mixed run's own text must survive alongside the box text (F3)");
        (text.Split("MIXED-BOX-TEXT").Length - 1).Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. THE NDA — the exact document whose hard-tier constructs produced the 422. Its signature-box
    //    text must survive as degraded prose, project → render → re-project must never hard-fail, and
    //    the rendered output must carry unique paraIds (the count-gate's duplicate-id trigger is gone
    //    by construction on this path).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Nda_HardTierConstructs_AcceptFlattenWithVisibleTextPreserved_Never422()
    {
        var ndaPath = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => Path.GetFileName(p).StartsWith("AppligentNDA", StringComparison.OrdinalIgnoreCase));
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(ndaPath);

        // Projection: fail-closed API — never throws; the NDA must NOT be Failed (the no-422 analog).
        var projection = _builder.BuildContentModel(original);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, "a hard-tier construct must never hard-fail");

        // Task 026: the signature-box text (task 004's confirmed breaker content) is now MODEL text.
        // "For: Appligent, Inc." exists ONLY inside the text boxes (verified against the fixture) — the
        // body prose has "Appligent, Inc." in a sentence and the counterparty's "For: IPM Solutions,
        // Inc." as plain text, so this exact string is the box-extraction oracle.
        var text = AllModelText(projection.Model);
        text.Should().Contain("For: Appligent, Inc.", "the signature box's visible text must be preserved (accept-flatten)");
        projection.Warnings.Should().Contain(w => w.Code == "text-box-flattened",
            "each flattened text box is surfaced as a user-visible warning");

        // One branch only: the AlternateContent Choice/Fallback duplication must not double the text.
        (text.Split("For: Appligent, Inc.").Length - 1).Should().Be(1,
            "the box text must appear exactly once — Choice/Fallback are the same box");

        // Step-9.5 F2 pin: the UNCHOSEN Fallback branch's paragraphs are represented via the chosen
        // branch — they must not fire a false unrendered-paragraphs loss report (the guard fired ×3 on
        // exactly the NDA's Fallback paragraphs before the fix).
        projection.Warnings.Should().NotContain(w => w.Code == "unrendered-paragraphs",
            "the dedup'd Fallback branch is preserved content, not lost content");

        // Render both paths; reopen; re-project. No exception anywhere = no hard-fail on the save path.
        var carrier = _renderer.RenderIntoCarrier(original, projection.Model, author: "seam-test");
        var synthesized = _renderer.SynthesizeDocument(projection.Model, author: "seam-test");
        _builder.BuildContentModel(carrier).Status.Should().NotBe(ComposeProjectionStatus.Failed);
        _builder.BuildContentModel(synthesized).Status.Should().NotBe(ComposeProjectionStatus.Failed);

        // The 422's root trigger — duplicate w14:paraId — cannot exist in the rendered output.
        foreach (var rendered in new[] { carrier, synthesized })
        {
            using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
            var ids = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
                .Select(p => p.ParagraphId?.Value)
                .Where(v => v is not null)
                .ToList();
            ids.Should().OnlyHaveUniqueItems("the renderer's AssignParaIds dedup pass removes the NDA's duplicate-id class");
        }

        // Word-validity floor: rendering must not introduce NEW schema errors over the source (multiset).
        var sourceErrors = ValidationErrorCounts(original);
        ValidationErrorCounts(carrier)
            .Where(kv => kv.Value > (sourceErrors.TryGetValue(kv.Key, out var had) ? had : 0))
            .Should().BeEmpty("no new schema errors (multiset)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. The render-side degradation SINK: every render drop is counted and surfaced — the
    //    success-with-warnings channel SaveAsync returns (never a 422, never silent).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_DegradationSink_CountsAnchorFormatChangeAndHrefDrops()
    {
        var model = new ComposeContentModel
        {
            Comments = new[] { new ComposeComment { Id = 1, Author = "Reviewer", Text = "kept" } },
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        // Valid ranged comment (kept — not counted).
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 1 } },
                        new ComposeInlineRun { Text = "commented " },
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 1 } },
                        // DANGLING anchor pair (id 99 has no comment) — dropped, now COUNTED.
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 99 } },
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 99 } },
                        // Junk format change — record dropped by the parse gate, now COUNTED.
                        new ComposeInlineRun { Text = "styled", FormatChange = new ComposeFormatChange { Author = "X", PreviousPropertiesXml = "<w:rPr><oops" } },
                        // Unresolvable href — link dropped (text kept), now COUNTED.
                        new ComposeInlineRun { Text = "linked", Href = "not an absolute uri" },
                    },
                },
            },
        };

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = _renderer.SynthesizeDocument(model, author: "seam-test", degradations);

        degradations.Should().ContainSingle(w => w.Code == "comment-anchor-dropped").Which.Count.Should().Be(2);
        degradations.Should().ContainSingle(w => w.Code == "tracked-format-change-dropped").Which.Count.Should().Be(1);
        degradations.Should().ContainSingle(w => w.Code == "hyperlink-target-dropped").Which.Count.Should().Be(1);

        // The degraded output is still schema-clean and content-complete: nothing 422s, nothing vanishes.
        ValidationErrorCounts(rendered).Should().BeEmpty();
        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var innerText = doc.MainDocumentPart!.Document!.Body!.InnerText;
        innerText.Should().Contain("styled").And.Contain("linked").And.Contain("commented");
    }

    [Fact]
    public void Render_CleanModel_ReportsNoDegradations()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock { Kind = ComposeBlockKind.Paragraph, Runs = new[] { new ComposeInlineRun { Text = "clean prose" } } },
            },
        };

        var degradations = new List<ComposeProjectionWarning>();
        _renderer.SynthesizeDocument(model, author: "seam-test", degradations);
        degradations.Should().BeEmpty("a clean save must not report warnings");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Comments-part date gate (025-F3 same-class fix): a TryParse-able culture date is still a
    //    schema-invalid @w:date — the xsd lexical gate must omit it; valid xsd dates keep the raw form.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_CommentDates_XsdLexicalGate()
    {
        var model = new ComposeContentModel
        {
            Comments = new[]
            {
                new ComposeComment { Id = 1, Author = "A", Date = "2026-08-01T09:30:00Z", Text = "valid date kept" },
                new ComposeComment { Id = 2, Author = "B", Date = "08/01/2026", Text = "culture date omitted" },
            },
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 1 } },
                        new ComposeInlineRun { Text = "text" },
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 1 } },
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 2 } },
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 2 } },
                    },
                },
            },
        };

        var rendered = _renderer.SynthesizeDocument(model, author: "seam-test");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var comments = doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.Elements<Comment>().ToList();
        comments.Single(c => c.Id!.Value == "1").Date!.InnerText.Should().Be("2026-08-01T09:30:00Z");
        comments.Single(c => c.Id!.Value == "2").Date.Should().BeNull("a culture-format date is schema-invalid @w:date");
        ValidationErrorCounts(rendered).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. Corpus floor: every corpus doc (hard-tier exemplars included) projects + renders + re-projects
    //    without hard-fail — the no-422 guarantee across the whole representative set.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void EveryCorpusDoc_HardTierDegradation_NeverHardFails(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var projection = _builder.BuildContentModel(original);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' must never hard-fail");

        var degradations = new List<ComposeProjectionWarning>();
        var rendered = _renderer.RenderIntoCarrier(original, projection.Model, author: "seam-test", degradations);
        _builder.BuildContentModel(rendered).Status.Should().NotBe(ComposeProjectionStatus.Failed,
            $"'{docName}' rendered output must re-project");
    }
}
