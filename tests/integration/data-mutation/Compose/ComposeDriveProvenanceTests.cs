// DRIVE PROVENANCE (#858 family) — a Compose write into an EXISTING drive item must land in the drive
// the owning `sprk_document` row RECORDS, not the drive the caller named.
//
// THE DEFECT. Both write paths into an existing item took the drive from the CALLER — `SaveAsync` from
// `request.DriveId` (the request body), `ApplyTemplateAsync` from a route parameter — while the
// authorized row already held `sprk_graphdriveid`. The server never consulted it, so the record could
// say "this document lives at drive X" while its bytes were written to drive Y, and nothing in the
// system noticed the disagreement.
//
// WHAT THIS IS *NOT*, because the shape is easy to mistake for its neighbours in the provenance census.
// This is not the app-only container hole that `Api/DocumentsEndpoints.cs` and the Office save path
// carry. Every Compose write is OBO: SPE authorizes it as the acting user, so a caller can only reach
// drives their own token already permits, and no privilege is gained by naming a different one. The
// defect is that the RECORD and the BYTES could diverge — an audit-trail defect. Writing the test as
// though it were an access-control test would assert the wrong thing and pass for the wrong reason.
//
// THE FALLBACK IS PART OF THE CONTRACT, NOT A GAP IN IT. A row that carries no `sprk_graphdriveid`
// falls back to the caller's value. Legacy rows predating the full-SPE-pointer stamp exist —
// `PromoteIfEphemeralAsync` documents that such a row makes downstream readers 409 "No file is
// attached" — so a hard fail-closed would break saves on real documents to close a hole OBO already
// closes. An attacker cannot make a row's drive id DISAPPEAR, so the fallback covers legacy data rather
// than an attack path. It is pinned here so a later reader cannot mistake it for an oversight, and so a
// change that silently removes it fails.
//
// KEEP path: tests/integration/data-mutation/** — "every new write path => >=1 integration test
// verifying rollback semantics" (tests/CLAUDE.md). The semantic under test is WHERE the write lands.

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.DataMutation.Compose;

public sealed class ComposeDriveProvenanceResolutionTests
{
    private const string DriveItemId = "spe-item-provenance-001";
    private const string RecordedDriveId = "drive-the-record-says";

    private static ComposeRecordResolution NewResolution(Mock<IGenericEntityService> dataverse) =>
        // `sessions` is only touched by RebindSessionDocumentIdAsync, which this slice never calls;
        // `dedupDetector` is the documented bare-ctor null. One mocked boundary, real logic.
        new(sessions: null!, dataverse.Object, NullLogger.Instance, dedupDetector: null);

    private static Entity Row(Guid id, string? driveId, int stateCode = 0, DateTime? createdOn = null)
    {
        var e = new Entity("sprk_document", id);
        e["sprk_documentid"] = id;
        e["statecode"] = new OptionSetValue(stateCode);
        e["createdon"] = createdOn ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (driveId is not null)
        {
            e["sprk_graphdriveid"] = driveId;
        }

        return e;
    }

    private static Mock<IGenericEntityService> DataverseReturning(Entity? row)
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row!);
        return dataverse;
    }

    [Fact]
    public async Task TryResolveRecordedDriveId_WhenTheRowRecordsADrive_ReturnsIt()
    {
        var dataverse = DataverseReturning(Row(Guid.NewGuid(), RecordedDriveId));

        var resolved = await NewResolution(dataverse)
            .TryResolveRecordedDriveIdAsync(DriveItemId, CancellationToken.None);

        resolved.Should().Be(RecordedDriveId);
    }

    [Fact]
    public async Task TryResolveRecordedDriveId_AsksForTheDriveColumn()
    {
        // The column set is the whole mechanism. `TryFindDocumentByGraphItemIdAsync` predates this fix
        // and fetched only the id plus the two FR-C3 dedup columns; a widened retrieve that quietly
        // narrows again would make every caller fall back to the client's claim while every other test
        // here still passed, because a row without the attribute and a row that was never asked for it
        // are indistinguishable from the return value alone.
        var dataverse = DataverseReturning(Row(Guid.NewGuid(), RecordedDriveId));

        await NewResolution(dataverse).TryResolveRecordedDriveIdAsync(DriveItemId, CancellationToken.None);

        dataverse.Verify(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document",
                It.IsAny<KeyAttributeCollection>(),
                It.Is<string[]>(cols => cols.Contains("sprk_graphdriveid")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryResolveRecordedDriveId_WhenTheRowCarriesNoDrive_ReturnsNullSoTheCallerCanFallBack()
    {
        // A legacy row with no SPE pointer. Null is the signal "the record cannot answer" — distinct
        // from "the record says drive X".
        var dataverse = DataverseReturning(Row(Guid.NewGuid(), driveId: null));

        var resolved = await NewResolution(dataverse)
            .TryResolveRecordedDriveIdAsync(DriveItemId, CancellationToken.None);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task TryResolveRecordedDriveId_WhenNoRowCarriesTheItem_ReturnsNull()
    {
        var dataverse = DataverseReturning(row: null);

        var resolved = await NewResolution(dataverse)
            .TryResolveRecordedDriveIdAsync(DriveItemId, CancellationToken.None);

        resolved.Should().BeNull();
    }

    [Fact]
    public async Task TryResolveRecordedDriveId_WhenTheIdentityKeyIsBroken_StillAnswersViaTheSelfHeal()
    {
        // Composition with #781, and the reason this routes through TryFindDocumentByGraphItemIdAsync
        // rather than issuing its own query. On `spaarkedev1` the `sprk_graphitemid_uk` key sat in
        // Failed over duplicated data; a private query here would have answered "no row" during that
        // outage and handed every write back to the caller's claim — silently reinstating the defect
        // exactly when the data was already known to be inconsistent.
        var canonical = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var duplicate = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Failed to retrieve sprk_document by alternate key: Found multiple records While trying " +
                "to resolve alternate key for the entity sprk_document."));
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity>
            {
                Row(duplicate, "drive-on-the-younger-duplicate", createdOn: new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc)),
                Row(canonical, RecordedDriveId, createdOn: new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc)),
            }));

        var resolved = await NewResolution(dataverse)
            .TryResolveRecordedDriveIdAsync(DriveItemId, CancellationToken.None);

        resolved.Should().Be(RecordedDriveId,
            "the canonical row (oldest active) is the one whose drive is authoritative — the same " +
            "deterministic rule #781 established, reused rather than re-decided");
    }

    [Fact]
    public async Task TryResolveRecordedDriveId_WhenTheSelfHealColumnQueryReturnsTheDriveColumn_ProvesTheQueryAsksForIt()
    {
        // The self-heal path has its OWN ColumnSet, separate from the alt-key path's. Widening one and
        // not the other would leave the fix working normally and failing precisely during a key outage.
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Failed to retrieve sprk_document by alternate key: sprk_document With Ids = ... Or Keys " +
                "= sprk_graphitemid are not defined as keys for the entity: sprk_graphitemid_uk (Not Active)."));

        QueryExpression? captured = null;
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => captured = q)
            .ReturnsAsync(new EntityCollection(new List<Entity> { Row(Guid.NewGuid(), RecordedDriveId) }));

        await NewResolution(dataverse).TryResolveRecordedDriveIdAsync(DriveItemId, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ColumnSet.Columns.Should().Contain("sprk_graphdriveid");
    }
}

/// <summary>
/// The guarantee that matters to an operator reading the audit trail: the bytes went where the record
/// says they went. Drives the REAL save route with the REAL <c>ComposeService</c>.
/// </summary>
public sealed class ComposeDriveProvenanceSaveRouteTests : IClassFixture<ComposeCreateOnSaveFixture>
{
    private readonly ComposeCreateOnSaveFixture _fixture;

    public ComposeDriveProvenanceSaveRouteTests(ComposeCreateOnSaveFixture fixture) => _fixture = fixture;

    private const string SpeItemId = "spe-item-provenance-save";
    private const string DriveTheCallerNames = "drive-the-caller-claims";
    private const string DriveTheRecordRecords = "drive-the-record-records";
    private static readonly byte[] DocxBytes = { 0x50, 0x4B, 0x03, 0x04, 0x33, 0x44 };

    /// <summary>Arranges Dataverse so the graphitemid key resolves to a row carrying <paramref name="recordedDriveId"/>
    /// (or, when null, a row with no SPE drive stamped — the legacy shape).</summary>
    private void ArrangeRowRecording(string? recordedDriveId)
    {
        _fixture.ResetBoundaries();

        var rowId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var row = new Entity("sprk_document", rowId);
        row["sprk_documentid"] = rowId;
        row["sprk_graphitemid"] = SpeItemId;
        if (recordedDriveId is not null)
        {
            row["sprk_graphdriveid"] = recordedDriveId;
        }

        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, KeyAttributeCollection key, string[] _, CancellationToken _) =>
                key.TryGetValue("sprk_graphitemid", out _) ? row : null!);

        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string driveId, string itemId, Stream _, CancellationToken _) =>
                new FileHandleDto(
                    Id: itemId,
                    Name: "contract.docx",
                    ParentId: null,
                    Size: DocxBytes.Length,
                    CreatedDateTime: DateTimeOffset.UtcNow,
                    LastModifiedDateTime: DateTimeOffset.UtcNow,
                    ETag: "\"v2\"",
                    IsFolder: false,
                    WebUrl: null,
                    DriveId: driveId));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
    }

    private async Task<HttpResponseMessage> SaveAsync()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.PostAsJsonAsync(
            $"/api/compose/documents/{SpeItemId}/save",
            new
            {
                driveId = DriveTheCallerNames,
                sessionId = Guid.NewGuid().ToString(),
                tenantId = ComposeCreateOnSaveFixture.TestTenantId,
                content = DocxBytes,
            });
    }

    [Fact]
    public async Task SaveDocument_WhenTheCallerNamesADifferentDrive_WritesToTheDriveTheRecordRecords()
    {
        ArrangeRowRecording(DriveTheRecordRecords);

        var response = await SaveAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "resolving provenance must not turn a working save into a failure — the whole point is that " +
            "the user never notices, only the audit trail changes");

        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheRecordRecords, SpeItemId,
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the record is the authority on where its own bytes live");

        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheCallerNames, It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "writing to the caller's claim while the row says otherwise IS the defect");
    }

    [Fact]
    public async Task SaveDocument_ResolvesProvenanceBeforeTheREADSToo_NotJustTheWrite()
    {
        // The resolution is folded onto the request at the top of SaveAsync rather than applied at the
        // write, so the pre-write metadata read (which also drives the PDF-target guard) addresses the
        // same drive as the write. A fix applied only at the sink would read one drive's metadata and
        // write to another's — a subtler divergence than the one it replaced, and one no write-side
        // assertion would catch.
        ArrangeRowRecording(DriveTheRecordRecords);

        await SaveAsync();

        _fixture.SpeMock.Verify(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheCallerNames, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no leg of the save may still address the caller's claim");
    }

    [Fact]
    public async Task SaveDocument_WhenTheRowRecordsNoDrive_FallsBackToTheCallerSuppliedDrive()
    {
        // The declared fallback, pinned. Legacy rows carry no SPE pointer; failing closed here would
        // break saves on real documents to close a hole OBO already closes.
        ArrangeRowRecording(recordedDriveId: null);

        var response = await SaveAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheCallerNames, SpeItemId,
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "a row that cannot answer must not block the save — the caller's value is used, and logged");
    }
}

/// <summary>
/// Apply-template is the sharpest case in this family, and the one the provenance census explicitly said
/// a fix "must convert too, not just the save replace branch". It is a read-merge-write: it downloads
/// bytes, merges a template into them, and writes the result back. Resolving the drive only at the write
/// would read one drive's document and overwrite a different drive's — a WORSE divergence than the one
/// being fixed, and one that no write-side assertion alone would notice. Both legs are asserted here.
/// </summary>
public sealed class ComposeDriveProvenanceApplyTemplateTests
{
    private const string SpeItemId = "spe-item-provenance-template";
    private const string DriveTheRouteNames = "drive-the-route-claims";
    private const string DriveTheRecordRecords = "drive-the-record-records";
    private const string PreMergeETag = "\"etag-at-T1\"";

    private readonly Mock<ISpeFileOperations> _spe = new(MockBehavior.Loose);
    private readonly Mock<IGenericEntityService> _dataverse = new(MockBehavior.Loose);
    private readonly Mock<IPostUploadIndexingEnqueuer> _indexing = new(MockBehavior.Loose);
    private readonly Mock<ChatSessionManager> _sessions = new(
        Mock.Of<ITenantCache>(),
        Mock.Of<IChatDataverseRepository>(),
        NullLogger<ChatSessionManager>.Instance,
        null!,
        null!);

    private ComposeService CreateSut() => new(
        _spe.Object,
        _sessions.Object,
        _dataverse.Object,
        _indexing.Object,
        NullLogger<ComposeService>.Instance,
        ComposeServiceCollaborators.Resolver(_dataverse.Object),
        ComposeServiceCollaborators.Probe().Object);

    /// <summary>Minimal real OOXML — the merge engine under the service is the REAL one, so the bytes
    /// have to be a valid package. Content is irrelevant to this test; only the drive is.</summary>
    private static byte[] BuildPackage(WordprocessingDocumentType type, string text)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, type, autoSave: true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text(text))),
                new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static FileHandleDto Handle(string driveId, string etag) => new(
        Id: SpeItemId,
        Name: "contract.docx",
        ParentId: null,
        Size: 1234,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: etag,
        IsFolder: false,
        WebUrl: null,
        DriveId: driveId);

    private void ArrangeRowRecording(string? recordedDriveId)
    {
        var rowId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var row = new Entity("sprk_document", rowId);
        row["sprk_documentid"] = rowId;
        row["sprk_graphitemid"] = SpeItemId;
        if (recordedDriveId is not null)
        {
            row["sprk_graphdriveid"] = recordedDriveId;
        }

        _dataverse
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                "sprk_document", It.IsAny<KeyAttributeCollection>(),
                It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        _spe.Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string driveId, string _, CancellationToken _) =>
                Handle(driveId, PreMergeETag));

        _spe.Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), SpeItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(
                BuildPackage(WordprocessingDocumentType.Document, "The parties agree.")));

        _spe.Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), SpeItemId,
                It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string driveId, string _, Stream _, string? _, CancellationToken _) =>
                Handle(driveId, "\"etag-merged\""));
    }

    private Task<ApplyComposeTemplateResult> ApplyAsync() => CreateSut().ApplyTemplateAsync(
        TestHttpContexts.Authenticated(),
        DriveTheRouteNames,
        SpeItemId,
        BuildPackage(WordprocessingDocumentType.Template, "TEMPLATE BOILERPLATE"),
        "Firm Standard",
        CancellationToken.None);

    [Fact]
    public async Task ApplyTemplate_WhenTheRouteNamesADifferentDrive_ReadsAndWritesTheDriveTheRecordRecords()
    {
        ArrangeRowRecording(DriveTheRecordRecords);

        var result = await ApplyAsync();

        result.DriveId.Should().Be(DriveTheRecordRecords);

        // Both legs, asserted separately: a fix that converted only the write would pass a write-side
        // check while merging a template into a DIFFERENT document than the one it overwrote.
        _spe.Verify(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheRecordRecords, SpeItemId, It.IsAny<CancellationToken>()),
            Times.Once, "the merge must read the document the record points at");
        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheRecordRecords, SpeItemId,
                It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once, "and write it back to the same place");

        _spe.Verify(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheRouteNames, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheRouteNames, It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ApplyTemplate_WhenTheRowRecordsNoDrive_FallsBackToTheRouteParameter()
    {
        ArrangeRowRecording(recordedDriveId: null);

        var result = await ApplyAsync();

        result.DriveId.Should().Be(DriveTheRouteNames);
        _spe.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveTheRouteNames, SpeItemId,
                It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
