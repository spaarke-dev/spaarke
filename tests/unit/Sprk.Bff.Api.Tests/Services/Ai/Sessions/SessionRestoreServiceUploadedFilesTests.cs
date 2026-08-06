using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;
using AzureTokenCredential = Azure.Core.TokenCredential;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// Unit tests for the FR-D5 uploaded-files projection in
/// <see cref="SessionRestoreService.RestoreSessionAsync"/>
/// (spaarkeai-assistant-enhancements-r2 task 036).
///
/// Behavior under test: a restored session projects its persisted
/// <see cref="StoredSession.UploadedFiles"/> manifest onto
/// <see cref="RestoredSession.UploadedFiles"/> so the client can rehydrate the attachment chip
/// on reopen — carrying identifier/display metadata ONLY (fileId, fileName, contentType,
/// sizeBytes) and NEVER the enriched fields (SummaryText / Sections / Citations / ExtractedText),
/// per ADR-015 Tier-2 minimisation. ADR-040: the projection reads the ALREADY-persisted manifest,
/// no new store / no new query.
///
/// The persistence boundary (Cosmos/Redis) is mocked at <see cref="ISessionPersistenceService"/>
/// — the genuine external boundary — to feed a <see cref="StoredSession"/>; every assertion is on
/// the observable projected output, not interaction shape. Sessions carry NO entity refs so the
/// Dataverse ETag staleness check short-circuits (the HTTP factory / credential are never used).
/// </summary>
public class SessionRestoreServiceUploadedFilesTests
{
    private const string TenantId = "tenant-abc";
    private const string SessionId = "session-xyz";

    private readonly Mock<ISessionPersistenceService> _persistenceMock = new();
    private readonly SessionRestoreService _sut;

    public SessionRestoreServiceUploadedFilesTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        _sut = new SessionRestoreService(
            _persistenceMock.Object,
            Mock.Of<IHttpClientFactory>(),
            configuration,
            Mock.Of<AzureTokenCredential>(),
            Mock.Of<ILogger<SessionRestoreService>>());
    }

    private void SetupLoad(StoredSession session) =>
        _persistenceMock
            .Setup(p => p.LoadSessionAsync(TenantId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

    private static StoredSession NewSession() => new()
    {
        Id = SessionId,
        SessionId = SessionId,
        TenantId = TenantId,
        Messages = [],
        EntityRefs = [],
    };

    [Fact]
    public async Task RestoreSessionAsync_WhenSessionHasUploadedFiles_ProjectsMinimalManifest()
    {
        var uploadedAt = DateTimeOffset.Parse("2026-08-06T10:00:00Z");
        var session = NewSession();
        session.UploadedFiles =
        [
            new StoredUploadedFile
            {
                FileId = "file-1",
                FileName = "NDA.pdf",
                ContentType = "application/pdf",
                SizeBytes = 12345,
                UploadedAt = uploadedAt,
                // Enriched fields present on the stored manifest — MUST NOT leak into the projection.
                SummaryText = "This is a mutual NDA between…",
                ClassifiedDocType = "NDA",
                SearchDocumentIdsCsv = "file-1_s_0,file-1_s_1",
            },
        ];
        SetupLoad(session);

        var restored = await _sut.RestoreSessionAsync(TenantId, SessionId);

        restored.Should().NotBeNull();
        restored!.UploadedFiles.Should().ContainSingle();
        var projected = restored.UploadedFiles[0];
        projected.FileId.Should().Be("file-1");
        projected.FileName.Should().Be("NDA.pdf");
        projected.ContentType.Should().Be("application/pdf");
        projected.SizeBytes.Should().Be(12345);
        // ADR-015 Tier-2 minimisation: the projected record type carries no enriched surface at all
        // (RestoredUploadedFile has exactly four members). This asserts the contract stays minimal.
        typeof(RestoredUploadedFile).GetProperties().Select(p => p.Name)
            .Should().BeEquivalentTo("FileId", "FileName", "ContentType", "SizeBytes");
    }

    [Fact]
    public async Task RestoreSessionAsync_WhenSessionHasNoUploadedFiles_ProjectsEmptyList()
    {
        SetupLoad(NewSession());

        var restored = await _sut.RestoreSessionAsync(TenantId, SessionId);

        restored.Should().NotBeNull();
        restored!.UploadedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoreSessionAsync_PreservesManifestOrder()
    {
        var session = NewSession();
        session.UploadedFiles =
        [
            new StoredUploadedFile { FileId = "a", FileName = "first.txt", ContentType = "text/plain", SizeBytes = 1 },
            new StoredUploadedFile { FileId = "b", FileName = "second.docx", ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document", SizeBytes = 2 },
        ];
        SetupLoad(session);

        var restored = await _sut.RestoreSessionAsync(TenantId, SessionId);

        restored!.UploadedFiles.Select(f => f.FileId).Should().ContainInOrder("a", "b");
    }
}
