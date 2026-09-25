using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
// `EntityReference` is ambiguous here (Sprk.Bff.Api.Models.Office declares its own) — alias the Dataverse one,
// matching the alias the class under test uses.
using XrmEntityReference = Microsoft.Xrm.Sdk.EntityReference;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Office;

/// <summary>
/// NFR-08 / finding F-h (task 028): the Office save path must apply the EDITABLE <b>link/graduate</b> dedup
/// mode to editable content and keep the IMMUTABLE <b>suppress</b> mode for archival captures — keyed on
/// <see cref="SaveContentType"/>, which is host-neutral, and never on which Office host called.
/// </summary>
/// <remarks>
/// <para>Before this fix, <see cref="OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync"/> ran the
/// suppress path for EVERY content type: a byte-identical <c>Document</c> save skipped the create entirely and
/// returned the pre-existing canonical id, so two genuinely different drafts that happened to be byte-identical
/// at save time collapsed into one record — silently. These tests exercise the real persistence decision through
/// the public entry point; the only things mocked are actual boundaries (the Dataverse document service, the
/// generic entity seam, and the detector's own <c>virtual</c> SPE/notification seams).</para>
/// <para>Sibling of <see cref="OfficeDocumentPersistenceDedupTests"/>, which covers the FR-C3 immutable wiring.
/// The immutable-suppress theory below is the deliberate BOUNDARY PIN for this change: it fails if the
/// Email/Attachment behavior email-communication-intelligence-r2 Pillar C depends on ever drifts.</para>
/// </remarks>
public class OfficeDocumentPersistenceEditableDedupTests
{
    private const string DriveId = "drive-1";
    private const string ItemId = "item-2";
    private const string FileName = "draft.docx";
    private const string OwnerOid = "11111111-1111-1111-1111-111111111111";

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────

    private static Mock<ContentDedupDetector> Detector() =>
        new(MockBehavior.Loose, null!, null!, null!, NullLogger<ContentDedupDetector>.Instance);

    /// <summary>The editable seam: what the detector resolved for the just-uploaded item.</summary>
    private static void ResolvesIdentity(Mock<ContentDedupDetector> detector, string? hash, Guid? canonicalId) =>
        detector
            .Setup(d => d.ResolveContentIdentityAsync(DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((hash, canonicalId));

    private static Mock<IDocumentDataverseService> DocumentService(Guid newDocId, Action<UpdateDocumentRequest>? capture = null)
    {
        var mock = new Mock<IDocumentDataverseService>();
        mock.Setup(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(newDocId.ToString());
        mock.Setup(d => d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, UpdateDocumentRequest, CancellationToken>((_, u, _) => capture?.Invoke(u))
            .Returns(Task.CompletedTask);
        return mock;
    }

    /// <summary>
    /// The generic seam. <paramref name="existingRowForItem"/> is what the <c>sprk_graphitemid_uk</c> alternate
    /// key resolves for the uploaded drive-item: <c>null</c> models the ordinary "no row yet" case, which the
    /// real Dataverse client signals by THROWING — modelled here with the same throw so the guard is exercised.
    /// </summary>
    private static Mock<IGenericEntityService> GenericService(
        Entity? existingRowForItem,
        List<(Guid Id, Dictionary<string, object> Fields)> writes)
    {
        var mock = new Mock<IGenericEntityService>();

        var altKey = mock.Setup(g => g.RetrieveByAlternateKeyAsync(
            "sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()));
        if (existingRowForItem is null)
        {
            altKey.ThrowsAsync(new InvalidOperationException("Entity sprk_document not found with provided alternate key values"));
        }
        else
        {
            altKey.ReturnsAsync(existingRowForItem);
        }

        mock.Setup(g => g.UpdateAsync("sprk_document", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, id, f, _) => writes.Add((id, f)))
            .Returns(Task.CompletedTask);

        return mock;
    }

    private static OfficeDocumentPersistence Sut(
        Mock<IDocumentDataverseService> docs, Mock<ContentDedupDetector> detector, Mock<IGenericEntityService>? generic) =>
        new(docs.Object,
            Mock.Of<IProcessingJobService>(),
            detector.Object,
            NullLogger<OfficeDocumentPersistence>.Instance,
            Mock.Of<ICommunicationDataverseService>(),
            generic?.Object);

    private static SaveRequest DocumentSave() => new()
    {
        ContentType = SaveContentType.Document,
        Document = new DocumentMetadata { FileName = FileName, Title = "Draft" },
    };

    private static Entity LinkedCopyRow(Guid rowId, Guid canonicalId, string linkedHash)
    {
        var row = new Entity("sprk_document", rowId);
        row["sprk_canonicaldocument"] = new XrmEntityReference("sprk_document", canonicalId);
        row["sprk_canonicalhash"] = linkedHash;
        return row;
    }

    // ── The defect: an editable byte-identical save must LINK, never suppress ─────────────────────

    [Fact]
    public async Task EditableSave_ByteIdenticalToExistingCanonical_CreatesItsOwnDocumentAndLinksIt()
    {
        // Two genuinely different drafts that are byte-identical RIGHT NOW are still two documents.
        var canonical = Guid.NewGuid();
        var newDocId = Guid.NewGuid();
        var detector = Detector();
        ResolvesIdentity(detector, "hash-identical", canonical);

        UpdateDocumentRequest? update = null;
        var docs = DocumentService(newDocId, u => update = u);
        var writes = new List<(Guid Id, Dictionary<string, object> Fields)>();
        var generic = GenericService(existingRowForItem: null, writes);

        var result = await Sut(docs, detector, generic).CreateDocumentWithSpePointersAsync(
            DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        // 1. A SECOND record exists — the create was NOT skipped (this is the F-h defect).
        result.DocumentId.Should().Be(newDocId, "an editable save owns its own sprk_document, never the canonical's id");
        result.WasContentDuplicate.Should().BeFalse(
            "suppression is the immutable mode; reporting a duplicate here would make the caller delete this draft's blob and skip finalization");
        docs.Verify(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Once,
            "the editable path must create its own document even when the bytes match an existing canonical (NFR-08)");

        // 2. Both records carry the SAME content hash.
        update.Should().NotBeNull();
        update!.CanonicalHash.Should().Be("hash-identical", "the copy stamps the same content identity as its canonical");

        // 3. The second record is LINKED to the first via sprk_canonicaldocument.
        var link = writes.Should().ContainSingle(w => w.Fields.ContainsKey("sprk_canonicaldocument")).Subject;
        link.Id.Should().Be(newDocId, "the link is written on the COPY, not on the canonical");
        link.Fields["sprk_canonicaldocument"].Should().BeOfType<XrmEntityReference>()
            .Which.Id.Should().Be(canonical);

        // 4. Never silent: the saver is told this draft was linked and will graduate on first edit.
        detector.Verify(d => d.NotifyLinkedCopyAsync(OwnerOid, canonical, FileName, It.IsAny<CancellationToken>()), Times.Once);

        // 5. The immutable suppress seam is not on the editable path at all.
        detector.Verify(d => d.ReconcileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "ReconcileAsync suppresses; the editable path must use the pure ResolveContentIdentityAsync seam");
    }

    [Fact]
    public async Task EditableSave_FirstWriter_CreatesDocumentStampsHashAndLinksNothing()
    {
        var newDocId = Guid.NewGuid();
        var detector = Detector();
        ResolvesIdentity(detector, "hash-first", canonicalId: null);

        UpdateDocumentRequest? update = null;
        var docs = DocumentService(newDocId, u => update = u);
        var writes = new List<(Guid Id, Dictionary<string, object> Fields)>();
        var generic = GenericService(existingRowForItem: null, writes);

        var result = await Sut(docs, detector, generic).CreateDocumentWithSpePointersAsync(
            DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        result.WasContentDuplicate.Should().BeFalse();
        update!.CanonicalHash.Should().Be("hash-first", "the first writer stamps its own content identity");
        writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_canonicaldocument"),
            "a first writer IS the canonical — there is nothing to link it to");
        detector.Verify(d => d.NotifyLinkedCopyAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EditableSave_HashUnavailable_StillCreatesTheDocumentAndLinksNothing()
    {
        // NFR-04: no content identity → no dedup is possible, and the save must proceed regardless.
        var newDocId = Guid.NewGuid();
        var detector = Detector();
        ResolvesIdentity(detector, hash: null, canonicalId: null);

        UpdateDocumentRequest? update = null;
        var docs = DocumentService(newDocId, u => update = u);
        var writes = new List<(Guid Id, Dictionary<string, object> Fields)>();

        var result = await Sut(docs, detector, GenericService(null, writes)).CreateDocumentWithSpePointersAsync(
            DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        result.DocumentId.Should().Be(newDocId);
        result.WasContentDuplicate.Should().BeFalse();
        update!.CanonicalHash.Should().BeNull();
        writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_canonicaldocument"));
    }

    // ── Graduate on divergence ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EditableSave_LinkedCopyContentDiverged_SeversLinkAndStampsItsOwnHash()
    {
        // The row recorded for this SPE drive-item was a hash-linked copy at "hash-old". Its live content is
        // now "hash-new" — it is no longer identical to anything, so it graduates to its own canonical.
        var copyId = Guid.NewGuid();
        var canonical = Guid.NewGuid();
        var detector = Detector();
        ResolvesIdentity(detector, "hash-new", canonicalId: null);

        var writes = new List<(Guid Id, Dictionary<string, object> Fields)>();
        var generic = GenericService(LinkedCopyRow(copyId, canonical, "hash-old"), writes);

        await Sut(DocumentService(Guid.NewGuid()), detector, generic).CreateDocumentWithSpePointersAsync(
            DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        var graduation = writes.Should().ContainSingle(w => w.Id == copyId).Subject;
        graduation.Fields.Should().ContainKey("sprk_canonicaldocument")
            .WhoseValue.Should().Be(DBNull.Value, "the link is severed with the DBNull clear-sentinel");
        graduation.Fields.Should().ContainKey("sprk_canonicalhash")
            .WhoseValue.Should().Be("hash-new", "the graduated document stamps its own diverged content identity");
    }

    [Fact]
    public async Task EditableSave_LinkedCopyStillByteIdentical_LeavesTheLinkIntact()
    {
        var copyId = Guid.NewGuid();
        var detector = Detector();
        ResolvesIdentity(detector, "hash-same", canonicalId: null);

        var writes = new List<(Guid Id, Dictionary<string, object> Fields)>();
        var generic = GenericService(LinkedCopyRow(copyId, Guid.NewGuid(), "hash-same"), writes);

        await Sut(DocumentService(Guid.NewGuid()), detector, generic).CreateDocumentWithSpePointersAsync(
            DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        writes.Should().NotContain(w => w.Id == copyId,
            "content that has not diverged is still a faithful copy — graduating it early would lose the link for no reason");
    }

    [Fact]
    public async Task EditableSave_ExistingRowIsATrueCanonical_IsNotGraduated()
    {
        // A true canonical carries no sprk_canonicaldocument, so there is no link to sever — and its hash must
        // NOT be rewritten by this path (that is the create/update path's job, not graduation's).
        var canonicalRowId = Guid.NewGuid();
        var trueCanonical = new Entity("sprk_document", canonicalRowId);
        trueCanonical["sprk_canonicalhash"] = "hash-old";

        var detector = Detector();
        ResolvesIdentity(detector, "hash-new", canonicalId: null);

        var writes = new List<(Guid Id, Dictionary<string, object> Fields)>();
        var generic = GenericService(trueCanonical, writes);

        await Sut(DocumentService(Guid.NewGuid()), detector, generic).CreateDocumentWithSpePointersAsync(
            DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        writes.Should().NotContain(w => w.Id == canonicalRowId, "only a hash-linked COPY can graduate");
    }

    [Fact]
    public async Task EditableSave_GraduationWriteFails_DoesNotFailTheSave()
    {
        // NFR-04: graduation is best-effort metadata repair. A failure is re-evaluated on the next save.
        var copyId = Guid.NewGuid();
        var detector = Detector();
        ResolvesIdentity(detector, "hash-new", canonicalId: null);

        var generic = new Mock<IGenericEntityService>();
        generic.Setup(g => g.RetrieveByAlternateKeyAsync("sprk_document", It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LinkedCopyRow(copyId, Guid.NewGuid(), "hash-old"));
        generic.Setup(g => g.UpdateAsync("sprk_document", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse write failed"));

        var newDocId = Guid.NewGuid();
        var act = async () => await Sut(DocumentService(newDocId), detector, generic).CreateDocumentWithSpePointersAsync(
            DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.DocumentId.Should().Be(newDocId);
    }

    [Fact]
    public async Task EditableSave_WithoutTheGenericSeam_StillCreatesTheDocument()
    {
        // The bare-constructor availability gate: no generic seam → no link and no graduation, but the record
        // is still created. Metadata may be lost; a record never is.
        var newDocId = Guid.NewGuid();
        var detector = Detector();
        ResolvesIdentity(detector, "hash-identical", Guid.NewGuid());

        var result = await Sut(DocumentService(newDocId), detector, generic: null).CreateDocumentWithSpePointersAsync(
            DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        result.DocumentId.Should().Be(newDocId);
        result.WasContentDuplicate.Should().BeFalse("an unavailable link seam must never re-open the suppress path");
    }

    [Fact]
    public async Task EditableSave_IdentityResolutionThrows_StillCreatesTheDocument()
    {
        // NFR-04: an editable save must never be the one path where a dedup hiccup costs the user a draft.
        var newDocId = Guid.NewGuid();
        var detector = Detector();
        detector.Setup(d => d.ResolveContentIdentityAsync(DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SPE hash read exploded"));

        UpdateDocumentRequest? update = null;
        var writes = new List<(Guid Id, Dictionary<string, object> Fields)>();

        var act = async () => await Sut(DocumentService(newDocId, u => update = u), detector, GenericService(null, writes))
            .CreateDocumentWithSpePointersAsync(
                DocumentSave(), DriveId, ItemId, "https://spe/web", FileName, 4096, OwnerOid, CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.DocumentId.Should().Be(newDocId);
        update!.CanonicalHash.Should().BeNull("no identity could be resolved, so nothing is stamped");
        writes.Should().NotContain(w => w.Fields.ContainsKey("sprk_canonicaldocument"),
            "a failed resolution must not produce a link to a canonical it never confirmed");
    }

    // ── BOUNDARY PIN: immutable captures keep the shipped suppress behavior ───────────────────────

    [Theory]
    [InlineData(SaveContentType.Email)]
    [InlineData(SaveContentType.Attachment)]
    public async Task ImmutableSave_ByteIdentical_StillSuppressesTheSecondDocument(SaveContentType contentType)
    {
        // PIN (task 028 constraint): Email/Attachment are archival captures that never diverge, so suppress
        // remains correct and email-communication-intelligence-r2 Pillar C depends on it. This test FAILS if
        // the NFR-08 editable fix ever leaks into the immutable path.
        var canonical = Guid.NewGuid();
        var detector = Detector();
        detector.Setup(d => d.ReconcileAsync(DriveId, ItemId, OwnerOid, FileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DedupDecision("hash1", IsDuplicate: true, CanonicalDocumentId: canonical));

        var docs = new Mock<IDocumentDataverseService>(MockBehavior.Strict); // strict → any create/update fails the test
        var request = new SaveRequest
        {
            ContentType = contentType,
            Email = new EmailMetadata { Subject = "pinned", SenderEmail = "sender@x.com" },
            Attachment = new AttachmentMetadata { AttachmentId = "att-1", FileName = FileName },
        };

        var result = await Sut(docs, detector, GenericService(null, new List<(Guid, Dictionary<string, object>)>()))
            .CreateDocumentWithSpePointersAsync(
                request, DriveId, ItemId, "https://spe/web", FileName, 1024, OwnerOid, CancellationToken.None);

        result.DocumentId.Should().Be(canonical, "an immutable byte-identical capture resolves to the existing canonical");
        result.WasContentDuplicate.Should().BeTrue("the caller must skip finalization and clean up the transient blob");
        docs.Verify(d => d.CreateDocumentAsync(It.IsAny<CreateDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        detector.Verify(d => d.NotifyLinkedCopyAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "a suppressed immutable copy gets the duplicate notification, not the linked-copy one");
        detector.Verify(d => d.ResolveContentIdentityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the immutable path must keep using ReconcileAsync — the editable seam is not on it");
    }

    // ── The classification axis itself (host-neutral by construction) ─────────────────────────────

    [Theory]
    [InlineData(SaveContentType.Document, true)]
    [InlineData(SaveContentType.Email, false)]
    [InlineData(SaveContentType.Attachment, false)]
    [InlineData((SaveContentType)999, true)] // fail-safe default: link/graduate never discards a record
    public void IsEditableContent_ClassifiesOnContentTypeAlone(SaveContentType contentType, bool expected)
    {
        // The NFR-08 axis is SaveContentType — host-neutral by construction. Word and Outlook reach the same
        // decision through the same shared persistence path; nothing here can observe which host called.
        OfficeDocumentPersistence.IsEditableContent(contentType).Should().Be(expected);
    }
}
