// Task 024 (spaarkeai-compose-r6, FR-04) — HYPERLINKS + COMMENTS through the canonical model.
//
// HYPERLINKS: the R5 representation (ComposeInlineRun.Href → BuildRun sentinel →
// ResolveHyperlinkRelationships external relationship) is REUSED, not rebuilt — external http/https/mailto
// links already round-trip. Task 024 closed the SILENT-loss gaps by counting an unresolvable /
// protocol-neutralized target as `hyperlink-target-dropped`.
//
// AMENDED by UAT 2026-08-26 (D-1): task 024 also FLATTENED an internal bookmark link ("#anchor") and
// warned `internal-link-flattened`, reasoning that "bookmark targets are not model data". That was wrong
// on both halves. (a) `w:anchor` IS a self-contained scalar — nothing to re-derive, so ADR-049 I-2 says
// carry it; the bookmark survives independently via clone or CarryUnmodeledConstructs. (b) Nulling it in
// the MODEL while the read walk still emitted a live `#anchor` href into the EDITOR made
// `formattingUnchanged` unable to match, so an UNTOUCHED paragraph containing a cross-reference was
// re-authored on every save — breaching the "untouched blocks are preserved" invariant, and surfacing to
// the user as `hyperlink-target-dropped`. The internal link is now CARRIED. `internal-link-flattened` has
// no producer and is retired.
//
// COMMENTS were silently lost before this task (body anchors fell through default cases; the comments
// part survived the carrier but nothing referenced it). Now: ComposeContentModel.Comments carries the
// part's projection (id/author/initials/date/text) and dedicated ANCHOR MARKER runs
// (ComposeInlineRun.CommentAnchor Start/End — the IsPageBreak mechanism) carry the range positions;
// the End marker folds the w:commentReference (Word's canonical adjacency); a bare reference (point
// comment) projects as an adjacent Start+End pair. CARRIER mode: comments part preserved BYTE-IDENTICAL,
// anchors re-authored against its ids (the numbering identity pattern); blank-package synthesize authors
// the part from the model.
//
// NEGATIVE (ADR-038): NO Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeHyperlinkCommentSeamTests
{
    private readonly ComposeDocxProjectionBuilder _builder = new();
    private readonly ComposeDocumentRenderer _renderer = new();

    private const string ExternalUrl = "https://example.com/clause-library";

    // ── SDK-authored source: external + internal links, a ranged comment, a point comment ─────────

    private static byte[] BuildHyperlinkCommentSource()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();

            var commentsPart = main.AddNewPart<WordprocessingCommentsPart>();
            commentsPart.Comments = new Comments(
                new Comment(
                    new Paragraph(new Run(new Text("Please tighten this clause."))),
                    new Paragraph(new Run(new Text("See precedent 12(b)."))))
                { Id = "1", Author = "Reviewing Partner", Initials = "RP", Date = new DateTimeValue { InnerText = "2026-08-01T09:30:00Z" } },
                new Comment(
                    new Paragraph(new Run(new Text("Confirm defined term."))))
                { Id = "2", Author = "Associate", Initials = "AA" });
            commentsPart.Comments.Save();

            var rel = main.AddHyperlinkRelationship(new Uri(ExternalUrl), isExternal: true);

            main.Document = new Document(new Body(
                new Paragraph(
                    new Run(new Text("See the ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Hyperlink(new Run(new Text("clause library"))) { Id = rel.Id },
                    new Run(new Text(" and ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Hyperlink(new Run(new Text("Section 2"))) { Anchor = "Section2" },
                    new Run(new Text(".") { Space = SpaceProcessingModeValues.Preserve })),
                new Paragraph(
                    new Run(new Text("The parties ") { Space = SpaceProcessingModeValues.Preserve }),
                    new CommentRangeStart { Id = "1" },
                    new Run(new Text("shall negotiate in good faith") { Space = SpaceProcessingModeValues.Preserve }),
                    new CommentRangeEnd { Id = "1" },
                    new Run(new CommentReference { Id = "1" }),
                    new Run(new Text(" hereafter.") { Space = SpaceProcessingModeValues.Preserve })),
                new Paragraph(
                    new Run(new Text("Defined Terms apply.") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new CommentReference { Id = "2" })), // POINT comment — no range
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static Dictionary<string, int> ValidationErrorCounts(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(doc)
            .GroupBy(e => e.Description)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static byte[]? CommentsPartBytes(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        var part = doc.MainDocumentPart?.WordprocessingCommentsPart;
        if (part is null)
        {
            return null;
        }
        using var s = part.GetStream();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static IEnumerable<string> AnchorFacts(ComposeContentModel model) =>
        model.Blocks.Select(b => string.Join("|", b.Runs.Select(r =>
            r.CommentAnchor is { } a ? $"<{a.Kind}:{a.Id}>" : r.IsPageBreak ? "<BR>" : r.Text)));

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Projection: comments + anchors captured; link losses LOUD; external links carried.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildContentModel_OnHyperlinkCommentSource_CapturesCommentsAnchorsAndLinksLoudly()
    {
        var projection = _builder.BuildContentModel(BuildHyperlinkCommentSource());
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);

        // Comments list from the part.
        projection.Model.Comments.Should().HaveCount(2);
        var first = projection.Model.Comments.Single(c => c.Id == 1);
        first.Author.Should().Be("Reviewing Partner");
        first.Initials.Should().Be("RP");
        first.Date.Should().Be("2026-08-01T09:30:00Z", "the RAW authored date string round-trips");
        first.Text.Should().Be("Please tighten this clause.\nSee precedent 12(b).");
        projection.Model.Comments.Single(c => c.Id == 2).Author.Should().Be("Associate");

        // Ranged anchor at exact positions; the reference is folded into End.
        var clause = projection.Model.Blocks[1];
        AnchorFacts(projection.Model).ElementAt(1).Should().Be(
            "The parties |<Start:1>|shall negotiate in good faith|<End:1>| hereafter.",
            "range markers sit at the exact inline positions; the w:commentReference folds into End");

        // Point comment → adjacent Start+End pair.
        AnchorFacts(projection.Model).ElementAt(2).Should().Be("Defined Terms apply.|<Start:2>|<End:2>");

        // BOTH links carried (UAT 2026-08-26 / D-1).
        var linkPara = projection.Model.Blocks[0];
        linkPara.Runs.Single(r => r.Text == "clause library").Href.Should().Be(ExternalUrl);
        linkPara.Runs.Single(r => r.Text == "Section 2").Href.Should().Be("#Section2",
            "an internal cross-reference is a self-contained scalar (w:anchor) — carried, not re-derived. "
            + "Nulling it here is what made formattingUnchanged unable to match, re-authoring an UNTOUCHED "
            + "paragraph on every save.");
        projection.Warnings.Should().NotContain(w => w.Code == "internal-link-flattened",
            "the code is retired — nothing flattens an internal link any more");
        projection.Warnings.Should().NotContain(w => w.Code == "hyperlink-target-dropped",
            "both links resolved — nothing dropped");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. Carrier round-trip: comments part BYTE-IDENTICAL; anchors + reference re-authored; the
    //    external link resolves to the same target; no new schema errors.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenderIntoCarrier_HyperlinkCommentRoundTrip_PreservesPartAnchorsAndLinkTargets()
    {
        var source = BuildHyperlinkCommentSource();
        var projection = _builder.BuildContentModel(source);
        // Task 040: pinned to the RENDER path (mergeUnchangedBlocks: false). This test asserts how the
        // renderer RE-AUTHORS comment anchors and hyperlink relationships, and it posts the projection unmodified — so with the merge on
        // (the production default) every block is CLONED and the re-authoring never runs. Cloning is the
        // correct behaviour for an unedited block; this test's subject is the render path itself, which
        // still executes for every block the user actually changes. Merge-path coverage:
        // ComposeMergeSeamTests.
        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test", mergeUnchangedBlocks: false);

        CommentsPartBytes(rendered)!.AsSpan().SequenceEqual(CommentsPartBytes(source)!).Should().BeTrue(
            "the carrier's comments part is authoritative — preserved byte-identically");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        // The ranged comment's markup is fully re-authored with the carrier ids.
        var clause = body.Elements<Paragraph>().ElementAt(1);
        clause.Elements<CommentRangeStart>().Single().Id!.Value.Should().Be("1");
        clause.Elements<CommentRangeEnd>().Single().Id!.Value.Should().Be("1");
        clause.Descendants<CommentReference>().Single().Id!.Value.Should().Be("1");
        // Range brackets exactly the commented text.
        var clauseSequence = clause.ChildElements
            .Select(e => e switch
            {
                CommentRangeStart => "<S>",
                CommentRangeEnd => "<E>",
                Run r when r.GetFirstChild<CommentReference>() is not null => "<REF>",
                Run r => r.InnerText,
                _ => null,
            })
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
        clauseSequence.Should().ContainInOrder("The parties ", "<S>", "shall negotiate in good faith", "<E>", "<REF>", " hereafter.");

        // The point comment re-authors as adjacent start/end/reference.
        var pointPara = body.Elements<Paragraph>().ElementAt(2);
        pointPara.Elements<CommentRangeStart>().Single().Id!.Value.Should().Be("2");
        pointPara.Elements<CommentRangeEnd>().Single().Id!.Value.Should().Be("2");
        pointPara.Descendants<CommentReference>().Single().Id!.Value.Should().Be("2");

        // The external hyperlink resolves through a REAL relationship to the same target.
        var hyperlink = body.Descendants<Hyperlink>().Single(h => h.InnerText == "clause library");
        main.HyperlinkRelationships.Single(r => r.Id == hyperlink.Id!.Value).Uri.ToString()
            .Should().Be(ExternalUrl);
        // The internal link survives as a real hyperlink carrying w:anchor (UAT 2026-08-26 / D-1).
        var internalLink = body.Descendants<Hyperlink>().Single(h => h.Anchor?.Value is not null);
        internalLink.Anchor!.Value.Should().Be("Section2");
        internalLink.Id.Should().BeNull("an anchor link has no relationship — the sentinel id must not persist");
        internalLink.InnerText.Should().Contain("Section 2");
        body.Descendants<Hyperlink>().Should().HaveCount(2, "both the external and the internal link survive");

        var sourceErrors = ValidationErrorCounts(source);
        ValidationErrorCounts(rendered)
            .Where(kv => kv.Value > (sourceErrors.TryGetValue(kv.Key, out var had) ? had : 0))
            .Should().BeEmpty("no new schema errors (multiset)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Fixed point: anchors, comments, and link targets stable across model→docx→model.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CarrierRoundTrip_CommentAndLinkFacts_AreAFixedPoint()
    {
        var source = BuildHyperlinkCommentSource();
        var projection = _builder.BuildContentModel(source);
        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test");
        var reprojection = _builder.BuildContentModel(rendered);

        AnchorFacts(reprojection.Model).Should().Equal(AnchorFacts(projection.Model),
            "anchor positions + texts survive the cycle");
        reprojection.Model.Comments.Should().BeEquivalentTo(projection.Model.Comments,
            "the byte-identical comments part re-projects to the same comment list");

        static IEnumerable<string?> Hrefs(ComposeContentModel m) =>
            m.Blocks.SelectMany(b => b.Runs).Where(r => r.Text.Length > 0).Select(r => r.Href);
        Hrefs(reprojection.Model).Should().Equal(Hrefs(projection.Model), "link targets survive the cycle");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Blank-package synthesize: the comments part is AUTHORED from the model.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SynthesizeDocument_WithComments_AuthorsTheCommentsPartFromTheModel()
    {
        var source = BuildHyperlinkCommentSource();
        var model = _builder.BuildContentModel(source).Model;

        var rendered = _renderer.SynthesizeDocument(model, author: "seam-test");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var comments = doc.MainDocumentPart!.WordprocessingCommentsPart!.Comments!;
        var first = comments.Elements<Comment>().Single(c => c.Id!.Value == "1");
        first.Author!.Value.Should().Be("Reviewing Partner");
        first.Date!.InnerText.Should().Be("2026-08-01T09:30:00Z", "the raw date string re-emits verbatim");
        first.InnerText.Should().Contain("Please tighten this clause.").And.Contain("See precedent 12(b).");
        comments.Elements<Comment>().Single(c => c.Id!.Value == "2").Author!.Value.Should().Be("Associate");

        // Anchors reference the authored part.
        var reprojection = _builder.BuildContentModel(rendered);
        reprojection.Model.Comments.Should().HaveCount(2);
        AnchorFacts(reprojection.Model).Should().Contain(s => s.Contains("<Start:1>") && s.Contains("<End:1>"));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4b. Step-9.5 F7 — the loudness paths have POSITIVE coverage: dangling rel id + neutralized
    //     protocol both count hyperlink-target-dropped; a cross-paragraph range round-trips.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildContentModel_DanglingAndNeutralizedLinkTargets_CountHyperlinkTargetDropped()
    {
        byte[] source;
        using (var stream = new MemoryStream())
        {
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                var evil = main.AddHyperlinkRelationship(new Uri("javascript:alert(1)"), isExternal: true);
                main.Document = new Document(new Body(
                    new Paragraph(
                        new Hyperlink(new Run(new Text("dangling"))) { Id = "rIdDoesNotExist" },
                        new Hyperlink(new Run(new Text("neutralized"))) { Id = evil.Id }),
                    new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
                main.Document.Save();
            }
            source = stream.ToArray();
        }

        var projection = _builder.BuildContentModel(source);

        projection.Warnings.Should().ContainSingle(w => w.Code == "hyperlink-target-dropped")
            .Which.Count.Should().Be(2, "a dangling r:id and a protocol-neutralized target each count");
        var runs = projection.Model.Blocks[0].Runs;
        runs.Should().HaveCount(2);
        runs.Should().OnlyContain(r => r.Href == null, "both links drop; the TEXT survives");
        runs.Select(r => r.Text).Should().Equal("dangling", "neutralized");
    }

    [Fact]
    public void CarrierRoundTrip_CrossParagraphCommentRange_KeepsBothMarkersAndStaysValid()
    {
        byte[] source;
        using (var stream = new MemoryStream())
        {
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                var commentsPart = main.AddNewPart<WordprocessingCommentsPart>();
                commentsPart.Comments = new Comments(
                    new Comment(new Paragraph(new Run(new Text("Spans two paragraphs."))))
                    { Id = "5", Author = "Reviewer" });
                commentsPart.Comments.Save();
                main.Document = new Document(new Body(
                    new Paragraph(
                        new Run(new Text("Range opens ") { Space = SpaceProcessingModeValues.Preserve }),
                        new CommentRangeStart { Id = "5" },
                        new Run(new Text("here") { Space = SpaceProcessingModeValues.Preserve })),
                    new Paragraph(
                        new Run(new Text("and closes ") { Space = SpaceProcessingModeValues.Preserve }),
                        new CommentRangeEnd { Id = "5" },
                        new Run(new CommentReference { Id = "5" }),
                        new Run(new Text(" here.") { Space = SpaceProcessingModeValues.Preserve })),
                    new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
                main.Document.Save();
            }
            source = stream.ToArray();
        }

        var projection = _builder.BuildContentModel(source);
        AnchorFacts(projection.Model).Should().Equal(
            "Range opens |<Start:5>|here",
            "and closes |<End:5>| here.");

        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test");
        using var renderedDoc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var body = renderedDoc.MainDocumentPart!.Document!.Body!;
        body.Elements<Paragraph>().ElementAt(0).Elements<CommentRangeStart>().Single().Id!.Value.Should().Be("5");
        body.Elements<Paragraph>().ElementAt(1).Elements<CommentRangeEnd>().Single().Id!.Value.Should().Be("5");
        body.Elements<Paragraph>().ElementAt(1).Descendants<CommentReference>().Single().Id!.Value.Should().Be("5");

        var sourceErrors = ValidationErrorCounts(source);
        ValidationErrorCounts(rendered)
            .Where(kv => kv.Value > (sourceErrors.TryGetValue(kv.Key, out var had) ? had : 0))
            .Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4c. Step-9.5 F1/F2 — client-input hardening: junk anchors drop instead of dangling; comment
    //     strings are sanitized so the part always serializes; duplicates collapse; junk dates drop.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SynthesizeDocument_ClientJunkCommentsAndAnchors_RendersSafelyWithoutDanglingReferences()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 99 } }, // no matching comment
                        new ComposeInlineRun { Text = "annotated" },
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 99 } },
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.Start, Id = 7 } },
                        new ComposeInlineRun { Text = "valid range" },
                        new ComposeInlineRun { CommentAnchor = new ComposeCommentAnchor { Kind = ComposeCommentAnchorKind.End, Id = 7 } },
                    },
                },
            },
            Comments = new[]
            {
                new ComposeComment { Id = 7, Author = "AB", Date = "not-a-date", Text = "badtext\r\nline2" },
                new ComposeComment { Id = 7, Author = "Duplicate", Text = "must be ignored" },
            },
        };

        var rendered = _renderer.SynthesizeDocument(model, author: "seam-test");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        // The unmatched anchor (id 99) was DROPPED — no dangling reference anywhere.
        body.Descendants<CommentReference>().Select(r => r.Id!.Value).Should().Equal(new[] { "7" });
        body.Descendants<CommentRangeStart>().Select(r => r.Id!.Value).Should().Equal(new[] { "7" });
        body.InnerText.Should().Contain("annotated", "dropping the anchor never drops the text");

        // The part: single deduped entry, sanitized strings, CRLF normalized, junk date omitted.
        var comments = main.WordprocessingCommentsPart!.Comments!;
        var comment = comments.Elements<Comment>().Single();
        comment.Id!.Value.Should().Be("7");
        comment.Author!.Value.Should().Be("AB", "XML-illegal control chars are sanitized out");
        comment.Date.Should().BeNull("a date that is not xsd:dateTime is omitted, not emitted");
        comment.Elements<Paragraph>().Select(p => p.InnerText).Should().Equal("badtext", "line2");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. Corpus-wide: comments parts byte-identical + anchor counts and external link targets stable.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void EveryCorpusDoc_CommentAndLinkFidelity_SurvivesCarrierRoundTrip(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var projection = _builder.BuildContentModel(original);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' must project");

        var rendered = _renderer.RenderIntoCarrier(original, projection.Model, author: "seam-test");
        var reprojection = _builder.BuildContentModel(rendered);

        // Comments part (when present) is preserved byte-identically and re-projects identically.
        var sourceComments = CommentsPartBytes(original);
        if (sourceComments is not null)
        {
            CommentsPartBytes(rendered)!.AsSpan().SequenceEqual(sourceComments).Should().BeTrue(
                $"'{docName}' comments part must survive byte-identically");
        }
        reprojection.Model.Comments.Should().BeEquivalentTo(projection.Model.Comments, $"'{docName}' comment list stable");

        // Anchor marker multiset is stable.
        static List<string> Anchors(ComposeContentModel m) =>
            m.Blocks.SelectMany(b => b.Runs)
                .Where(r => r.CommentAnchor is not null)
                .Select(r => $"{r.CommentAnchor!.Kind}:{r.CommentAnchor.Id}")
                .ToList();
        Anchors(reprojection.Model).Should().Equal(Anchors(projection.Model), $"'{docName}' anchors stable");

        // External link target sequence is stable.
        static List<string> Links(ComposeContentModel m) =>
            m.Blocks.SelectMany(b => b.Runs).Where(r => r.Href is not null).Select(r => r.Href!).ToList();
        Links(reprojection.Model).Should().Equal(Links(projection.Model), $"'{docName}' link targets stable");
    }
}
