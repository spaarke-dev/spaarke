// Task 025 (spaarkeai-compose-r6, FR-04) — TRACKED-CHANGES (redlines) through the canonical model.
//
// Before this task the model walk SETTLED every revision: w:ins flattened to plain prose
// (`tracked-insert-flattened`), w:del kept its text as settled prose (`tracked-delete-flattened-kept` —
// the no-text-loss reject direction), and a deleted paragraph MARK flattened
// (`tracked-paragraph-mark-flattened`). A render-from-model save therefore silently RESOLVED every
// pending redline in an imported legal document — a data-loss class for negotiation workflows.
//
// Now revisions are MODEL data: ComposeInlineRun.Revision carries the identity (kind/author/date) and the
// renderer re-authors Word-valid w:ins/w:del wrappers (GROUPING consecutive same-identity runs; deleted
// content as w:delText; ids ALWAYS server-minted, carrier-seeded). Paragraph-mark revisions carry as
// ComposeBlock.MarkRevision (w:pPr/w:rPr/w:ins|w:del); formatting-change history carries as
// ComposeFormatChange (w:pPrChange / w:rPrChange — identity + the previous properties as an OPAQUE
// server-set XML fragment, SDK-parse + schema-validation gated at render so client junk cannot reach the
// package). Move markup downgrades to plain ins/del LOUDLY (`tracked-move-downgraded`); stacked
// containers simplify to the innermost LOUDLY (`tracked-nested-revision-simplified` — the R4 "barfoo"
// warned baseline pending operator sign-off).
//
// CLIENT-INPUT HARDENING (the recurring 021-F1/022-F1/024-F1 review class, applied FROM THE START):
// revision authors sanitized + clamped (never empty — @w:author is schema-required), dates parse-gated,
// previous-properties XML never string-injected, revision ids never client-controlled.
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

public sealed class ComposeTrackedChangesSeamTests
{
    private readonly ComposeDocxProjectionBuilder _builder = new();
    private readonly ComposeDocumentRenderer _renderer = new();

    private static readonly string[] RetiredWarningCodes =
    {
        "tracked-insert-flattened",
        "tracked-delete-flattened-kept",
        "tracked-paragraph-mark-flattened",
    };

    // ── SDK-authored source: a negotiation-shaped redline document ─────────────────────────────────

    private static byte[] BuildRedlineSource()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                // P1: settled prose + a tracked insertion + a tracked deletion (the core redline shape).
                new Paragraph(
                    new Run(new Text("The parties agree ") { Space = SpaceProcessingModeValues.Preserve }),
                    new InsertedRun(
                        new Run(new Text("promptly and in good faith ") { Space = SpaceProcessingModeValues.Preserve }))
                    { Id = "101", Author = "Alice Redliner", Date = new DateTimeValue { InnerText = "2026-08-01T10:00:00Z" } },
                    new DeletedRun(
                        new Run(new DeletedText("within thirty (30) days ") { Space = SpaceProcessingModeValues.Preserve }))
                    { Id = "102", Author = "Bob Negotiator", Date = new DateTimeValue { InnerText = "2026-08-02T11:30:00Z" } },
                    new Run(new Text("to negotiate.") { Space = SpaceProcessingModeValues.Preserve })),
                // P2: a paragraph whose MARK is pending-deleted (accepting merges with the next paragraph).
                new Paragraph(
                    new ParagraphProperties(new ParagraphMarkRunProperties(
                        new Deleted { Id = "103", Author = "Alice Redliner", Date = new DateTimeValue { InnerText = "2026-08-01T10:05:00Z" } })),
                    new Run(new Text("This clause survives.") { Space = SpaceProcessingModeValues.Preserve })),
                new Paragraph(
                    new Run(new Text("Continuation clause text.") { Space = SpaceProcessingModeValues.Preserve })),
                // P3: a paragraph created while tracking (mark INSERTED) + a tracked paragraph-formatting
                // change (previously centered — rejecting the pPrChange restores jc=center).
                new Paragraph(
                    new ParagraphProperties(
                        new ParagraphMarkRunProperties(
                            new Inserted { Id = "104", Author = "Bob Negotiator", Date = new DateTimeValue { InnerText = "2026-08-02T12:00:00Z" } }),
                        new ParagraphPropertiesChange(
                            new ParagraphPropertiesExtended(new Justification { Val = JustificationValues.Center }))
                        { Id = "105", Author = "Alice Redliner", Date = new DateTimeValue { InnerText = "2026-08-01T10:10:00Z" } }),
                    new Run(new Text("Newly drafted indemnity paragraph.") { Space = SpaceProcessingModeValues.Preserve })),
                // P4: a run whose FORMATTING changed while tracking (was bold; rPrChange records it).
                new Paragraph(
                    new Run(
                        new RunProperties(
                            new RunPropertiesChange(
                                new PreviousRunProperties(new Bold()))
                            { Id = "106", Author = "Bob Negotiator", Date = new DateTimeValue { InnerText = "2026-08-02T12:15:00Z" } }),
                        new Text("Formerly bold defined term.") { Space = SpaceProcessingModeValues.Preserve })),
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static byte[] BuildMoveAndNestedSource()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                // Move markup: source half + destination half (same author/move operation).
                new Paragraph(
                    new MoveFromRun(
                        new Run(new Text("Relocated definition. ") { Space = SpaceProcessingModeValues.Preserve }))
                    { Id = "201", Author = "Mover", Date = new DateTimeValue { InnerText = "2026-08-03T09:00:00Z" } },
                    new Run(new Text("Anchor text. ") { Space = SpaceProcessingModeValues.Preserve }),
                    new MoveToRun(
                        new Run(new Text("Relocated definition. ") { Space = SpaceProcessingModeValues.Preserve }))
                    { Id = "202", Author = "Mover", Date = new DateTimeValue { InnerText = "2026-08-03T09:00:00Z" } }),
                // Stacked containers: text inserted by Alice then deleted by Bob (w:del inside w:ins).
                new Paragraph(
                    new InsertedRun(
                        new DeletedRun(
                            new Run(new DeletedText("inserted-then-struck") { Space = SpaceProcessingModeValues.Preserve }))
                        { Id = "204", Author = "Bob Negotiator", Date = new DateTimeValue { InnerText = "2026-08-04T09:30:00Z" } })
                    { Id = "203", Author = "Alice Redliner", Date = new DateTimeValue { InnerText = "2026-08-03T09:10:00Z" } }),
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    // ── shared oracles ─────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, int> ValidationErrorCounts(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(doc)
            .GroupBy(e => e.Description)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Per-paragraph revision fact string — the round-trip stability oracle (text + revision
    /// identity, mark revision, and format-change identities; the previous-properties XML compares by
    /// PRESENCE, not bytes — the SDK may normalize attribute serialization on re-author). A null date
    /// renders distinctly from an empty string (Step-9.5 F5 — the oracle must see empty→null drift).</summary>
    private static IEnumerable<string> RevisionFacts(ComposeContentModel model) =>
        model.Blocks.Select(b =>
            $"mark={(b.MarkRevision is { } m ? $"{m.Kind}:{m.Author}:{m.Date ?? "∅"}" : "-")}"
            + $";pchg={(b.PropertiesChange is { } p ? $"{p.Author}:{p.Date ?? "∅"}:{(p.PreviousPropertiesXml is null ? "noxml" : "xml")}" : "-")}"
            + ";runs=" + string.Join("|", b.Runs.Select(r =>
                (r.Revision is { } rev ? $"<{rev.Kind}:{rev.Author}:{rev.Date ?? "∅"}>" : "<->")
                + (r.FormatChange is { } fc ? $"<rchg:{fc.Author}:{(fc.PreviousPropertiesXml is null ? "noxml" : "xml")}>" : "")
                + (r.IsPageBreak ? "<BR>" : r.Text))));

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Projection: revisions are MODEL data; the three settle-flatten warnings are RETIRED.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Projection_CapturesInsertionsDeletions_MarkRevisions_AndFormatChanges()
    {
        var projection = _builder.BuildContentModel(BuildRedlineSource());

        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);
        projection.Warnings.Should().NotContain(w => RetiredWarningCodes.Contains(w.Code),
            "task 025 retires the tracked-change settle-flatten warnings");

        // P1: the redline runs carry kind + attribution; pending-deleted TEXT is carried (no text loss).
        var p1 = projection.Model.Blocks[0];
        p1.Runs.Should().HaveCount(4);
        p1.Runs[0].Revision.Should().BeNull();
        p1.Runs[1].Revision.Should().BeEquivalentTo(new ComposeRevision
        {
            Kind = ComposeRevisionKind.Inserted,
            Author = "Alice Redliner",
            Date = "2026-08-01T10:00:00Z",
        });
        p1.Runs[1].Text.Should().Be("promptly and in good faith ");
        p1.Runs[2].Revision.Should().BeEquivalentTo(new ComposeRevision
        {
            Kind = ComposeRevisionKind.Deleted,
            Author = "Bob Negotiator",
            Date = "2026-08-02T11:30:00Z",
        });
        p1.Runs[2].Text.Should().Be("within thirty (30) days ", "pending-deleted text is model TEXT");
        p1.Runs[3].Revision.Should().BeNull();

        // P2: deleted paragraph mark → MarkRevision (no longer a flatten warning).
        var p2 = projection.Model.Blocks[1];
        p2.MarkRevision.Should().BeEquivalentTo(new ComposeRevision
        {
            Kind = ComposeRevisionKind.Deleted,
            Author = "Alice Redliner",
            Date = "2026-08-01T10:05:00Z",
        });

        // P3: inserted mark + paragraph-formatting change with the previous pPr carried opaquely.
        var p4 = projection.Model.Blocks[3];
        p4.MarkRevision!.Kind.Should().Be(ComposeRevisionKind.Inserted);
        p4.PropertiesChange.Should().NotBeNull();
        p4.PropertiesChange!.Author.Should().Be("Alice Redliner");
        p4.PropertiesChange.PreviousPropertiesXml.Should().Contain("jc", "the previous centered alignment travels in the opaque carry");

        // P4: run-formatting change captured with the previous rPr.
        var p5 = projection.Model.Blocks[4];
        p5.Runs[0].FormatChange.Should().NotBeNull();
        p5.Runs[0].FormatChange!.Author.Should().Be("Bob Negotiator");
        p5.Runs[0].FormatChange!.PreviousPropertiesXml.Should().Contain("<w:b", "the previous bold travels in the opaque carry");
    }

    [Fact]
    public void Projection_MoveAndStackedContainers_DowngradeLoudly()
    {
        var projection = _builder.BuildContentModel(BuildMoveAndNestedSource());

        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);

        // Move halves downgrade to plain del/ins keeping attribution — counted once per container.
        var moveRuns = projection.Model.Blocks[0].Runs;
        moveRuns.Should().HaveCount(3);
        moveRuns[0].Revision!.Kind.Should().Be(ComposeRevisionKind.Deleted, "moveFrom is the deletion half");
        moveRuns[0].Revision!.Author.Should().Be("Mover");
        moveRuns[1].Revision.Should().BeNull();
        moveRuns[2].Revision!.Kind.Should().Be(ComposeRevisionKind.Inserted, "moveTo is the insertion half");
        projection.Warnings.Should().ContainSingle(w => w.Code == "tracked-move-downgraded")
            .Which.Count.Should().Be(2);

        // Stacked ins⊃del simplifies to the INNERMOST (Deleted, inner author) — counted, never silent.
        var nestedRun = projection.Model.Blocks[1].Runs.Single();
        nestedRun.Revision!.Kind.Should().Be(ComposeRevisionKind.Deleted);
        nestedRun.Revision!.Author.Should().Be("Bob Negotiator");
        nestedRun.Text.Should().Be("inserted-then-struck");
        projection.Warnings.Should().ContainSingle(w => w.Code == "tracked-nested-revision-simplified")
            .Which.Count.Should().Be(1);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. Round-trip: the rendered carrier authors WORD-VALID revision markup — real accept/reject
    //    material (w:ins wrappers, w:del/w:delText, attribution preserved, ids minted server-side).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_Carrier_AuthorsWordValidRevisionMarkup_NoSilentLoss()
    {
        var source = BuildRedlineSource();
        var projection = _builder.BuildContentModel(source);
        // Task 040: pinned to the RENDER path (mergeUnchangedBlocks: false). This test asserts how the
        // renderer RE-AUTHORS tracked-change revision markup (minting fresh ids ABOVE the carrier's), and it
        // posts the projection unmodified — so with the merge on (the production default) every block is
        // CLONED and the re-authoring never runs. Cloning is the correct behaviour for an unedited block, and
        // it preserves the carrier's own revision ids rather than minting new ones; this test's subject is the
        // render path itself, which still executes for every block the user actually changed.
        // Merge-path coverage: ComposeMergeSeamTests.
        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test", mergeUnchangedBlocks: false);

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        // The insertion survives as a real w:ins wrapper with attribution.
        var ins = body.Descendants<InsertedRun>().Should().ContainSingle().Subject;
        ins.Author!.Value.Should().Be("Alice Redliner");
        ins.Date!.InnerText.Should().Be("2026-08-01T10:00:00Z");
        ins.InnerText.Should().Be("promptly and in good faith ");

        // The deletion survives as w:del whose content is w:delText ONLY (Word rejects w:t here).
        var del = body.Descendants<DeletedRun>().Should().ContainSingle().Subject;
        del.Author!.Value.Should().Be("Bob Negotiator");
        del.Descendants<DeletedText>().Single().Text.Should().Be("within thirty (30) days ");
        del.Descendants<Text>().Should().BeEmpty("pending-deleted content must author as w:delText");

        // Mark revisions survive in pPr/rPr; the formatting changes re-author with their previous props.
        body.Descendants<ParagraphMarkRunProperties>().SelectMany(m => m.Elements<Deleted>()).Should().HaveCount(1);
        body.Descendants<ParagraphMarkRunProperties>().SelectMany(m => m.Elements<Inserted>()).Should().HaveCount(1);
        var pPrChange = body.Descendants<ParagraphPropertiesChange>().Should().ContainSingle().Subject;
        pPrChange.GetFirstChild<ParagraphPropertiesExtended>()!.GetFirstChild<Justification>()!.Val!.Value
            .Should().Be(JustificationValues.Center, "rejecting the change must restore the true previous alignment");
        body.Descendants<RunPropertiesChange>().Should().ContainSingle()
            .Which.GetFirstChild<PreviousRunProperties>()!.GetFirstChild<Bold>().Should().NotBeNull();

        // Revision ids: server-minted, all-distinct decimal values across every revision element.
        var ids = body.Descendants().Select(e => e switch
            {
                InsertedRun i => i.Id?.Value,
                DeletedRun d => d.Id?.Value,
                Inserted i => i.Id?.Value,
                Deleted d => d.Id?.Value,
                ParagraphPropertiesChange p => p.Id?.Value,
                RunPropertiesChange r => r.Id?.Value,
                _ => null,
            })
            .Where(v => v is not null)
            .Select(v => int.Parse(v!, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => id > 106, "ids seed ABOVE the carrier's existing revision ids (max 106)");

        // Word-validity oracle: no NEW schema errors vs the source (multiset).
        var sourceErrors = ValidationErrorCounts(source);
        ValidationErrorCounts(rendered)
            .Where(kv => kv.Value > (sourceErrors.TryGetValue(kv.Key, out var had) ? had : 0))
            .Should().BeEmpty("no new schema errors (multiset)");
    }

    [Fact]
    public void RoundTrip_FixedPoint_RevisionFactsStable()
    {
        var source = BuildRedlineSource();
        var first = _builder.BuildContentModel(source);
        var rendered = _renderer.RenderIntoCarrier(source, first.Model, author: "seam-test");
        var second = _builder.BuildContentModel(rendered);

        second.Status.Should().NotBe(ComposeProjectionStatus.Failed);
        second.Warnings.Should().NotContain(w => RetiredWarningCodes.Contains(w.Code));
        RevisionFacts(second.Model).Should().Equal(RevisionFacts(first.Model),
            "project → render → re-project must be a fixed point for revision facts");
    }

    [Fact]
    public void Render_GroupsConsecutiveSameIdentityRuns_AndSplitsOnIdentityChange()
    {
        var alice = new ComposeRevision { Kind = ComposeRevisionKind.Inserted, Author = "Alice", Date = "2026-08-01T10:00:00Z" };
        var bob = new ComposeRevision { Kind = ComposeRevisionKind.Inserted, Author = "Bob", Date = "2026-08-02T10:00:00Z" };
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun { Text = "one ", Revision = alice },
                        new ComposeInlineRun { Text = "two ", Bold = true, Revision = alice },
                        new ComposeInlineRun { Text = "three ", Revision = alice },
                        new ComposeInlineRun { Text = "four", Revision = bob },
                    },
                },
            },
        };

        var rendered = _renderer.SynthesizeDocument(model, author: "seam-test");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var wrappers = doc.MainDocumentPart!.Document!.Body!.Descendants<InsertedRun>().ToList();
        wrappers.Should().HaveCount(2, "consecutive same-identity runs share ONE wrapper");
        wrappers[0].Elements<Run>().Should().HaveCount(3);
        wrappers[0].Author!.Value.Should().Be("Alice");
        wrappers[1].Elements<Run>().Should().ContainSingle();
        wrappers[1].Author!.Value.Should().Be("Bob");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Client-input hardening FROM THE START (the recurring 021-F1/022-F1/024-F1 class): hostile
    //    authors sanitize, junk dates drop, malformed previous-properties XML NEVER reaches the package.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Render_SanitizesHostileClientRevisionInput()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    // Control bytes in the author, junk date — must sanitize, never throw.
                    Runs = new[]
                    {
                        new ComposeInlineRun
                        {
                            Text = "hostile insert",
                            Revision = new ComposeRevision { Kind = ComposeRevisionKind.Inserted, Author = "EvilAuthor", Date = "not-a-date" },
                        },
                        new ComposeInlineRun
                        {
                            Text = "anonymous delete",
                            Revision = new ComposeRevision { Kind = ComposeRevisionKind.Deleted, Author = "" },
                        },
                    },
                    // Malformed previous-pPr XML → the whole change record drops (never string-injected).
                    PropertiesChange = new ComposeFormatChange { Author = "X", PreviousPropertiesXml = "<w:pPr><oops" },
                    // Junk on the mark too.
                    MarkRevision = new ComposeRevision { Kind = ComposeRevisionKind.Deleted, Author = "", Date = "13/13/13" },
                },
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun
                        {
                            Text = "wrong-root formatting change",
                            // A pPr posted where an rPr is required — the SDK typed-parse ctor rejects the
                            // wrong root (ArgumentException, swallowed → record dropped).
                            FormatChange = new ComposeFormatChange { Author = "Y", PreviousPropertiesXml = "<w:pPr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>" },
                        },
                    },
                },
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun
                        {
                            // Step-9.5 F3: a TryParse-able but NON-xsd date ("08/01/2026") would be a
                            // schema-invalid @w:date — the lexical gate must omit it.
                            Text = "culture-format date",
                            Revision = new ComposeRevision { Kind = ComposeRevisionKind.Inserted, Author = "Carol", Date = "08/01/2026" },
                        },
                        new ComposeInlineRun
                        {
                            // Step-9.5 F7: WELL-FORMED but schema-INVALID previous-rPr (jc is not a run
                            // property) — must fail the OpenXmlValidator subtree gate, not just the ctor.
                            Text = "schema-invalid previous props",
                            FormatChange = new ComposeFormatChange
                            {
                                Author = "Dave",
                                PreviousPropertiesXml = "<w:rPr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:jc w:val=\"center\"/></w:rPr>",
                            },
                        },
                    },
                },
            },
        };

        var rendered = _renderer.SynthesizeDocument(model, author: "seam-test");

        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        var wrappers = body.Descendants<InsertedRun>().ToList();
        wrappers.Should().HaveCount(2);
        wrappers[0].Author!.Value.Should().Be("EvilAuthor", "control chars are stripped at authoring");
        wrappers[0].Date.Should().BeNull("a date that does not parse is omitted (@w:date is optional)");
        wrappers[1].Author!.Value.Should().Be("Carol");
        wrappers[1].Date.Should().BeNull("a TryParse-able but non-xsd date is still schema-invalid @w:date — omitted (F3)");

        // Task 012 (revision-author fallback): an EMPTY author now falls back to the SAVE-TIME author
        // param before the sanitizer's "Unknown" floor — the client mapper deliberately omits the author
        // on user-edit revision facts so the server attributes the authenticated saving user.
        var del = body.Descendants<DeletedRun>().Single();
        del.Author!.Value.Should().Be("seam-test", "@w:author is schema-required — an empty author falls back to the save-time author");
        del.Descendants<DeletedText>().Single().Text.Should().Be("anonymous delete");

        var markDel = body.Descendants<ParagraphMarkRunProperties>().SelectMany(m => m.Elements<Deleted>()).Single();
        markDel.Author!.Value.Should().Be("seam-test");
        markDel.Date.Should().BeNull();

        body.Descendants<ParagraphPropertiesChange>().Should().BeEmpty("malformed previous-pPr XML drops the record");
        body.Descendants<RunPropertiesChange>().Should().BeEmpty(
            "a wrong-root fragment fails the SDK typed-parse ctor and a well-formed-but-schema-invalid one fails the validator gate (F7)");

        // The hardened output is schema-clean from a blank package.
        ValidationErrorCounts(rendered).Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3b. Step-9.5 fixes F1/F2 — the two data-integrity shapes the first review proved broken.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RoundTrip_TrackedHyperlink_AuthorsWrapperInsideHyperlink()
    {
        // Word's canonical tracked-inserted link: w:hyperlink ⊃ w:ins ⊃ w:r. The reverse nesting
        // (w:ins ⊃ w:hyperlink) is schema-invalid (CT_RunTrackChange does not admit hyperlink).
        byte[] source;
        using (var stream = new MemoryStream())
        {
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                var rel = main.AddHyperlinkRelationship(new Uri("https://example.com/precedent"), isExternal: true);
                main.Document = new Document(new Body(
                    new Paragraph(
                        new Run(new Text("See ") { Space = SpaceProcessingModeValues.Preserve }),
                        new Hyperlink(
                            new InsertedRun(
                                new Run(new Text("the precedent library")))
                            { Id = "301", Author = "Alice Redliner", Date = new DateTimeValue { InnerText = "2026-08-05T08:00:00Z" } })
                        { Id = rel.Id },
                        new Run(new Text(".") { Space = SpaceProcessingModeValues.Preserve })),
                    new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
                main.Document.Save();
            }
            source = stream.ToArray();
        }

        var projection = _builder.BuildContentModel(source);
        var linked = projection.Model.Blocks[0].Runs.Single(r => r.Href is not null);
        linked.Revision!.Kind.Should().Be(ComposeRevisionKind.Inserted);

        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test");
        using var reopened = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var body = reopened.MainDocumentPart!.Document!.Body!;

        var hyperlink = body.Descendants<Hyperlink>().Should().ContainSingle().Subject;
        var insInsideLink = hyperlink.Elements<InsertedRun>().Should().ContainSingle("the wrapper nests INSIDE the hyperlink").Subject;
        insInsideLink.Author!.Value.Should().Be("Alice Redliner");
        insInsideLink.InnerText.Should().Be("the precedent library");
        body.Descendants<InsertedRun>().SelectMany(w => w.Elements<Hyperlink>())
            .Should().BeEmpty("w:ins ⊃ w:hyperlink is schema-invalid and must never be authored");

        var sourceErrors = ValidationErrorCounts(source);
        ValidationErrorCounts(rendered)
            .Where(kv => kv.Value > (sourceErrors.TryGetValue(kv.Key, out var had) ? had : 0))
            .Should().BeEmpty("no new schema errors (multiset)");
    }

    [Fact]
    public void Projection_ParagraphMarkMove_DowngradesLoudly()
    {
        // A moved paragraph's MARK (w:pPr/w:rPr/w:moveFrom) — Word's whole-paragraph-move shape. Must
        // downgrade to a Deleted mark revision with the loud move count, never vanish uncounted.
        byte[] source;
        using (var stream = new MemoryStream())
        {
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body(
                    new Paragraph(
                        new ParagraphProperties(new ParagraphMarkRunProperties(
                            new MoveFrom { Id = "401", Author = "Mover", Date = new DateTimeValue { InnerText = "2026-08-05T09:00:00Z" } })),
                        new MoveFromRun(
                            new Run(new Text("Relocated clause.") { Space = SpaceProcessingModeValues.Preserve }))
                        { Id = "402", Author = "Mover", Date = new DateTimeValue { InnerText = "2026-08-05T09:00:00Z" } }),
                    new Paragraph(
                        new ParagraphProperties(new ParagraphMarkRunProperties(
                            new MoveTo { Id = "403", Author = "Mover", Date = new DateTimeValue { InnerText = "2026-08-05T09:00:00Z" } })),
                        new MoveToRun(
                            new Run(new Text("Relocated clause.") { Space = SpaceProcessingModeValues.Preserve }))
                        { Id = "404", Author = "Mover", Date = new DateTimeValue { InnerText = "2026-08-05T09:00:00Z" } }),
                    new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
                main.Document.Save();
            }
            source = stream.ToArray();
        }

        var projection = _builder.BuildContentModel(source);

        projection.Model.Blocks[0].MarkRevision!.Kind.Should().Be(ComposeRevisionKind.Deleted, "a moveFrom mark is the deletion half");
        projection.Model.Blocks[0].MarkRevision!.Author.Should().Be("Mover");
        projection.Model.Blocks[1].MarkRevision!.Kind.Should().Be(ComposeRevisionKind.Inserted, "a moveTo mark is the insertion half");
        // 2 run-level + 2 mark-level downgrades, every one counted.
        projection.Warnings.Should().ContainSingle(w => w.Code == "tracked-move-downgraded")
            .Which.Count.Should().Be(4);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Corpus theory: every corpus doc round-trips with revision facts stable, retired warnings never
    //    reappear, and no new schema errors — the no-silent-loss floor for pre-existing redlines.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void EveryCorpusDoc_TrackedChangeFidelity_SurvivesCarrierRoundTrip(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var projection = _builder.BuildContentModel(original);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' must project");
        projection.Warnings.Should().NotContain(w => RetiredWarningCodes.Contains(w.Code),
            $"'{docName}': the retired settle-flatten warnings must never reappear");

        var rendered = _renderer.RenderIntoCarrier(original, projection.Model, author: "seam-test");
        var reprojection = _builder.BuildContentModel(rendered);
        reprojection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' must re-project");

        RevisionFacts(reprojection.Model).Should().Equal(RevisionFacts(projection.Model),
            $"'{docName}' revision facts must be stable across the round-trip");

        var sourceErrors = ValidationErrorCounts(original);
        ValidationErrorCounts(rendered)
            .Where(kv => kv.Value > (sourceErrors.TryGetValue(kv.Key, out var had) ? had : 0))
            .Should().BeEmpty($"'{docName}': no new schema errors (multiset)");
    }
}
