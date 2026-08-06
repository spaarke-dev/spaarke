// R5 task 012 (G12 / ET-2 — spec FR-11) — seam proof for ComposeShadowPatchEngine's acceptRevision /
// rejectRevision appliers: reconciling a PRE-EXISTING imported tracked change (native w:ins/w:del) by its
// native OOXML w:id so accepting/rejecting one and SAVING no longer refuses with
// TrackedChangeReconciliationUnsupported (the R4 task-030 boundary / the 2026-07-28 UAT 422).
//
// Because R4's 8-doc corpus is track-changes-CLEAN (the engine's own note: "not exercised by the corpus —
// track-changes-clean"), the ET-2 "corpus doc with pre-existing revisions" is built by INJECTING two
// revisions with KNOWN ids into a real corpus fixture (a w:ins id=9001 + a w:del id=9002), then driving the
// PRODUCTION engine over it. This keeps the resolve-BY-ID contract honest (the test controls the ids) and
// keeps the untouched-subtree byte-identity assertion meaningful (a real multi-part .docx base). Reuses the
// task-004 ComposeOoxmlPackagePartComparer + ComposeCorpusFixtureLocator unchanged — mirrors
// ComposeAlignmentApplierSeamTests' shape.
//
// Banned-pattern clean: no Mock<HttpMessageHandler>, no DI-registration test, no ctor-null test.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeRevisionReconciliationSeamTests
{
    private const string InsRevisionId = "9001";
    private const string DelRevisionId = "9002";
    private const string InsertedText = "INSERTED-BY-IMPORT";
    private const string DeletedText = "DELETED-BY-IMPORT";

    private static readonly DateTimeOffset When = new(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
    private readonly ComposeShadowPatchEngine _engine = new();

    // -- The headline ET-2 assertions: accept-then-save / reject-then-save must SUCCEED (no 422) ----------

    [Fact]
    public void AcceptRevision_OnAnImportedInsertion_ThenSave_Succeeds_NoTrackedChangeReconciliation422()
    {
        var (bytes, insParaId, _) = BuildDocWithRevisions();

        var log = SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = insParaId,
            Scope = ComposeRevisionScope.Single,
            RevisionId = InsRevisionId,
        });

        // The whole point of G12: this used to throw TrackedChangeReconciliationUnsupported (→ 422).
        var act = () => _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        act.Should().NotThrow<ComposePatchException>();

        var patched = _engine.Apply(bytes, log, author: "Seam", timestamp: When);

        using var doc = OpenReadOnly(patched);
        var para = ResolveParagraph(doc, insParaId);
        // accept-ins: the w:ins WRAPPER is stripped, but the inserted run STAYS as settled content.
        para.Descendants<InsertedRun>().Should().BeEmpty("accepting an insertion strips the w:ins wrapper");
        ParagraphText(para).Should().Contain(InsertedText, "the accepted insertion survives as normal text");
        AssertWordValid(patched);
    }

    [Fact]
    public void RejectRevision_OnAnImportedDeletion_ThenSave_Succeeds_NoTrackedChangeReconciliation422()
    {
        var (bytes, _, delParaId) = BuildDocWithRevisions();

        var log = SingleOpLog(new RejectRevisionOperation
        {
            ParaId = delParaId,
            Scope = ComposeRevisionScope.Single,
            RevisionId = DelRevisionId,
        });

        var act = () => _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        act.Should().NotThrow<ComposePatchException>();

        var patched = _engine.Apply(bytes, log, author: "Seam", timestamp: When);

        using var doc = OpenReadOnly(patched);
        var para = ResolveParagraph(doc, delParaId);
        // reject-del: the w:del wrapper is stripped and the deleted text is RESTORED as settled content
        // (w:delText → w:t) — Word-valid revision state (NFR-07).
        para.Descendants<DeletedRun>().Should().BeEmpty("rejecting a deletion strips the w:del wrapper");
        para.Descendants<DeletedText>().Should().BeEmpty("the restored run carries w:t, not w:delText");
        ParagraphText(para).Should().Contain(DeletedText, "the rejected deletion is restored as normal text");
        AssertWordValid(patched);
    }

    // -- The four native accept/reject cases (accept-ins / reject-ins / accept-del / reject-del) ----------

    [Fact]
    public void RejectRevision_OnAnImportedInsertion_RemovesTheInsertedRun()
    {
        var (bytes, insParaId, _) = BuildDocWithRevisions();
        var patched = _engine.Apply(bytes, SingleOpLog(new RejectRevisionOperation
        {
            ParaId = insParaId,
            Scope = ComposeRevisionScope.Single,
            RevisionId = InsRevisionId,
        }), author: "Seam", timestamp: When);

        using var doc = OpenReadOnly(patched);
        var para = ResolveParagraph(doc, insParaId);
        para.Descendants<InsertedRun>().Should().BeEmpty();
        ParagraphText(para).Should().NotContain(InsertedText, "rejecting an insertion removes the inserted run entirely");
        AssertWordValid(patched);
    }

    [Fact]
    public void AcceptRevision_OnAnImportedDeletion_RemovesTheRunEntirely()
    {
        var (bytes, _, delParaId) = BuildDocWithRevisions();
        var patched = _engine.Apply(bytes, SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = delParaId,
            Scope = ComposeRevisionScope.Single,
            RevisionId = DelRevisionId,
        }), author: "Seam", timestamp: When);

        using var doc = OpenReadOnly(patched);
        var para = ResolveParagraph(doc, delParaId);
        para.Descendants<DeletedRun>().Should().BeEmpty();
        para.Descendants<DeletedText>().Should().BeEmpty();
        ParagraphText(para).Should().NotContain(DeletedText, "accepting a deletion removes the run entirely");
        AssertWordValid(patched);
    }

    // -- Invariants: resolve BY ID (no text-search), untouched subtrees byte-identical -------------------

    [Fact]
    public void AcceptRevision_LeavesEveryUntouchedPackagePartByteIdentical()
    {
        var (bytes, insParaId, _) = BuildDocWithRevisions();
        var patched = _engine.Apply(bytes, SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = insParaId,
            Scope = ComposeRevisionScope.Single,
            RevisionId = InsRevisionId,
        }), author: "Seam", timestamp: When);

        // NFR-01: reconciliation legitimately edits document.xml; every OTHER part stays byte-identical.
        var comparison = ComposeOoxmlPackagePartComparer.Compare(bytes, patched, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(comparison.DescribeMismatches());

        // Every paragraph EXCEPT the reconciled one is byte-identical (I-4 untouched subtree).
        OtherParagraphOuterXml(patched, insParaId).Should().Equal(OtherParagraphOuterXml(bytes, insParaId));
    }

    [Fact]
    public void AcceptRevision_WhenRevisionIdNotFound_ThrowsRevisionNotFound_NeverTextSearches()
    {
        var (bytes, insParaId, _) = BuildDocWithRevisions();
        var log = SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = insParaId,
            Scope = ComposeRevisionScope.Single,
            RevisionId = "404404", // a well-formed but non-existent id → resolution is id-only (I-7)
        });

        var act = () => _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.RevisionNotFound);
    }

    [Fact]
    public void AcceptRevision_WhenParaIdNotFound_ThrowsParagraphNotFound()
    {
        var (bytes, _, _) = BuildDocWithRevisions();
        var log = SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = "DEADBEEF",
            Scope = ComposeRevisionScope.Single,
            RevisionId = InsRevisionId,
        });

        var act = () => _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.ParagraphNotFound);
    }

    // -- G12 BATCH (accept-all / reject-all, scope: All) — task 013 -------------------------------------
    //
    // Batch reconciles EVERY imported w:ins/w:del in the document in deterministic DOCUMENT PREORDER, composing
    // task 012's single-by-id primitives. The corpus is track-changes-clean, so the batch fixture INJECTS
    // several revisions with known ids across multiple paragraphs, then drives the PRODUCTION engine's All scope.

    [Fact]
    public void AcceptAll_OnAMultiRevisionDoc_ThenSave_Succeeds_NoTrackedChangeReconciliation422()
    {
        var (bytes, presenceParaId, _) = BuildDocWithManyRevisions();

        var log = SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = presenceParaId,
            Scope = ComposeRevisionScope.All,
            RevisionId = null,
        });

        var act = () => _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        act.Should().NotThrow<ComposePatchException>("accept-all reconciles every revision — no ET-2 422");

        var patched = _engine.Apply(bytes, log, author: "Seam", timestamp: When);

        using var doc = OpenReadOnly(patched);
        var body = doc.MainDocumentPart!.Document!.Body!;
        // accept-all: EVERY w:ins stripped (text kept), EVERY w:del removed (text gone) — document-wide.
        body.Descendants<InsertedRun>().Should().BeEmpty("accept-all strips every insertion wrapper");
        body.Descendants<DeletedRun>().Should().BeEmpty("accept-all removes every deletion");
        body.Descendants<DeletedText>().Should().BeEmpty("no w:delText survives accept-all");
        var text = string.Concat(body.Descendants<Text>().Select(t => t.Text));
        text.Should().Contain(InsertedText, "accepted insertions survive as settled text");
        text.Should().NotContain(DeletedText, "accepted deletions are gone");
        AssertWordValid(patched);
    }

    [Fact]
    public void RejectAll_OnAMultiRevisionDoc_ThenSave_Succeeds_NoTrackedChangeReconciliation422()
    {
        var (bytes, presenceParaId, _) = BuildDocWithManyRevisions();

        var log = SingleOpLog(new RejectRevisionOperation
        {
            ParaId = presenceParaId,
            Scope = ComposeRevisionScope.All,
            RevisionId = null,
        });

        var act = () => _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        act.Should().NotThrow<ComposePatchException>("reject-all reconciles every revision — no ET-2 422");

        var patched = _engine.Apply(bytes, log, author: "Seam", timestamp: When);

        using var doc = OpenReadOnly(patched);
        var body = doc.MainDocumentPart!.Document!.Body!;
        // reject-all: EVERY w:ins removed (text gone), EVERY w:del restored (w:delText → w:t) — document-wide.
        body.Descendants<InsertedRun>().Should().BeEmpty("reject-all removes every insertion");
        body.Descendants<DeletedRun>().Should().BeEmpty("reject-all strips every deletion wrapper");
        body.Descendants<DeletedText>().Should().BeEmpty("restored deletions carry w:t, not w:delText");
        var text = string.Concat(body.Descendants<Text>().Select(t => t.Text));
        text.Should().NotContain(InsertedText, "rejected insertions are gone");
        text.Should().Contain(DeletedText, "rejected deletions are restored as settled text");
        AssertWordValid(patched);
    }

    [Fact]
    public void AcceptAll_RunTwiceOnTheSameInput_ProducesByteIdenticalOutput_Deterministic()
    {
        var (bytes, presenceParaId, _) = BuildDocWithManyRevisions();
        var log = SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = presenceParaId,
            Scope = ComposeRevisionScope.All,
            RevisionId = null,
        });

        // Determinism is the graded acceptance criterion: batch ordering (document preorder) is a pure function
        // of the input bytes, so repeated accept-all on the SAME input MUST yield byte-identical output.
        var first = _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        var second = _engine.Apply(bytes, log, author: "Seam", timestamp: When);

        second.Should().Equal(first, "accept-all reconciliation order is deterministic (document preorder)");
    }

    [Fact]
    public void RejectAll_RunTwiceOnTheSameInput_ProducesByteIdenticalOutput_Deterministic()
    {
        var (bytes, presenceParaId, _) = BuildDocWithManyRevisions();
        var log = SingleOpLog(new RejectRevisionOperation
        {
            ParaId = presenceParaId,
            Scope = ComposeRevisionScope.All,
            RevisionId = null,
        });

        var first = _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        var second = _engine.Apply(bytes, log, author: "Seam", timestamp: When);

        second.Should().Equal(first, "reject-all reconciliation order is deterministic (document preorder)");
    }

    [Fact]
    public void AcceptAll_LeavesEveryUntouchedPackagePartByteIdentical()
    {
        var (bytes, presenceParaId, _) = BuildDocWithManyRevisions();
        var patched = _engine.Apply(bytes, SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = presenceParaId,
            Scope = ComposeRevisionScope.All,
            RevisionId = null,
        }), author: "Seam", timestamp: When);

        // NFR-01: batch legitimately edits document.xml; every OTHER package part stays byte-identical.
        var comparison = ComposeOoxmlPackagePartComparer.Compare(bytes, patched, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(comparison.DescribeMismatches());
    }

    [Fact]
    public void AcceptAll_WithANonNullRevisionId_IsRefused_NeverGuesses()
    {
        // An All-scope op MUST carry a null revisionId (design §3.5.3) — a non-null id is a contradiction.
        var (bytes, presenceParaId, _) = BuildDocWithManyRevisions();
        var log = SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = presenceParaId,
            Scope = ComposeRevisionScope.All,
            RevisionId = "9001",
        });

        var act = () => _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.RevisionNotFound);
    }

    [Fact]
    public void AcceptAll_OnADocWithNoRevisions_IsAnIdempotentSuccess_NoOp()
    {
        // Zero tracked revisions → accept-all is a no-op success (idempotent), never an error (design §3.5.3).
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(
            ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First());
        var presenceParaId = FirstAddressableParaId(bytes);

        var log = SingleOpLog(new AcceptRevisionOperation
        {
            ParaId = presenceParaId,
            Scope = ComposeRevisionScope.All,
            RevisionId = null,
        });

        byte[] patched = Array.Empty<byte>();
        var act = () => patched = _engine.Apply(bytes, log, author: "Seam", timestamp: When);
        act.Should().NotThrow<ComposePatchException>("accept-all on a revision-free doc is an idempotent no-op");

        using var doc = OpenReadOnly(patched);
        var body = doc.MainDocumentPart!.Document!.Body!;
        body.Descendants<InsertedRun>().Should().BeEmpty();
        body.Descendants<DeletedRun>().Should().BeEmpty();
        AssertWordValid(patched);
    }

    // -- helpers ----------------------------------------------------------------------------------------

    private static ComposeOperationLog SingleOpLog(ComposeOperation op) =>
        new() { Operations = new[] { op } };

    /// <summary>Injects a known w:ins (id 9001) into the first addressable paragraph and a known w:del
    /// (id 9002) into a second addressable paragraph of the first corpus fixture — the ET-2 "doc with
    /// pre-existing revisions". Both are APPENDED (never converting existing content), so every other
    /// subtree of the base doc stays a stable byte-identity baseline for the untouched-part assertions.</summary>
    private static (byte[] Bytes, string InsParaId, string DelParaId) BuildDocWithRevisions()
    {
        var baseBytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(
            ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First());

        using var ms = new MemoryStream();
        ms.Write(baseBytes, 0, baseBytes.Length);
        ms.Position = 0;

        string insParaId;
        string delParaId;
        using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            var paras = body.Descendants<Paragraph>()
                .Where(p => !string.IsNullOrEmpty(p.ParagraphId?.Value))
                .ToList();
            paras.Should().HaveCountGreaterThanOrEqualTo(2,
                "the ET-2 fixture needs two addressable paragraphs to host an injected w:ins + w:del");

            var insPara = paras[0];
            insParaId = insPara.ParagraphId!.Value!;
            var ins = new InsertedRun { Id = InsRevisionId, Author = "Word Author", Date = When.UtcDateTime };
            ins.AppendChild(new Run(new Text(InsertedText) { Space = SpaceProcessingModeValues.Preserve }));
            insPara.AppendChild(ins);

            var delPara = paras[1];
            delParaId = delPara.ParagraphId!.Value!;
            var del = new DeletedRun { Id = DelRevisionId, Author = "Word Author", Date = When.UtcDateTime };
            del.AppendChild(new Run(new DeletedText(DeletedText) { Space = SpaceProcessingModeValues.Preserve }));
            delPara.AppendChild(del);

            doc.MainDocumentPart!.Document!.Save();
        }

        return (ms.ToArray(), insParaId, delParaId);
    }

    /// <summary>Injects SEVERAL known revisions across multiple paragraphs (para[0]: w:ins 9001 + w:del 9002;
    /// para[1]: w:ins 9003 + w:del 9004) into the first corpus fixture — the G12 BATCH "multi-revision doc".
    /// Returns the presence-anchor paraId (para[0], document order) an accept-all/reject-all op carries. All
    /// revisions are APPENDED (never converting existing content), so untouched subtrees stay a stable baseline.</summary>
    private static (byte[] Bytes, string PresenceParaId, int RevisionCount) BuildDocWithManyRevisions()
    {
        var baseBytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(
            ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First());

        using var ms = new MemoryStream();
        ms.Write(baseBytes, 0, baseBytes.Length);
        ms.Position = 0;

        string presenceParaId;
        using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
        {
            var body = doc.MainDocumentPart!.Document!.Body!;
            var paras = body.Descendants<Paragraph>()
                .Where(p => !string.IsNullOrEmpty(p.ParagraphId?.Value))
                .ToList();
            paras.Should().HaveCountGreaterThanOrEqualTo(2,
                "the G12 batch fixture needs two addressable paragraphs to host revisions in >1 paragraph");

            presenceParaId = paras[0].ParagraphId!.Value!;

            AppendInsertion(paras[0], "9001");
            AppendDeletion(paras[0], "9002");
            AppendInsertion(paras[1], "9003");
            AppendDeletion(paras[1], "9004");

            doc.MainDocumentPart!.Document!.Save();
        }

        return (ms.ToArray(), presenceParaId, 4);
    }

    private static void AppendInsertion(Paragraph para, string id)
    {
        var ins = new InsertedRun { Id = id, Author = "Word Author", Date = When.UtcDateTime };
        ins.AppendChild(new Run(new Text(InsertedText) { Space = SpaceProcessingModeValues.Preserve }));
        para.AppendChild(ins);
    }

    private static void AppendDeletion(Paragraph para, string id)
    {
        var del = new DeletedRun { Id = id, Author = "Word Author", Date = When.UtcDateTime };
        del.AppendChild(new Run(new DeletedText(DeletedText) { Space = SpaceProcessingModeValues.Preserve }));
        para.AppendChild(del);
    }

    private static string FirstAddressableParaId(byte[] bytes)
    {
        using var doc = OpenReadOnly(bytes);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => !string.IsNullOrEmpty(p.ParagraphId?.Value)).ParagraphId!.Value!;
    }

    private static WordprocessingDocument OpenReadOnly(byte[] bytes) =>
        WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);

    private static Paragraph ResolveParagraph(WordprocessingDocument doc, string paraId) =>
        doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));

    /// <summary>All settled + tracked text of a paragraph (w:t and w:delText), for content assertions.</summary>
    private static string ParagraphText(Paragraph para) =>
        string.Concat(para.Descendants<Text>().Select(t => t.Text))
        + string.Concat(para.Descendants<DeletedText>().Select(t => t.Text));

    private static List<string> OtherParagraphOuterXml(byte[] bytes, string excludedParaId)
    {
        using var doc = OpenReadOnly(bytes);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => !string.Equals(p.ParagraphId?.Value, excludedParaId, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.OuterXml)
            .ToList();
    }

    /// <summary>Re-open the patched bytes as a WordprocessingML package (Word-valid package check, NFR-07).</summary>
    private static void AssertWordValid(byte[] bytes)
    {
        using var doc = OpenReadOnly(bytes);
        doc.MainDocumentPart.Should().NotBeNull();
        doc.MainDocumentPart!.Document!.Body.Should().NotBeNull();
    }
}
