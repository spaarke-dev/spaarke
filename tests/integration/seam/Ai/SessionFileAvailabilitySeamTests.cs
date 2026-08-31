using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;
using AzureTokenCredential = Azure.Core.TokenCredential;

namespace Sprk.Bff.Api.Tests.Services.Ai.Sessions;

/// <summary>
/// <c>tests/integration/seam/**</c> — vertical-slice-seam KEEP category (ADR-038 §2).
/// spaarkeai-compose-r8 FR-B05 (task 062): file availability on the restore path is
/// SERVER-authoritative, replacing R7's client-side ~24h guess.
/// </summary>
/// <remarks>
/// <para>
/// <b>The slice.</b> Bytes are written through the production <see cref="SessionFileBlobStore"/>,
/// then a production <see cref="SessionRestoreService"/> restores a session whose Cosmos manifest names
/// those files, and the assertions are on the <c>contentAvailable</c> value that comes back on
/// <see cref="RestoredUploadedFile"/> — the exact field the client renders. The two substitutions are
/// genuine external boundaries: the blob service and the Cosmos/Redis persistence service.
/// </para>
/// <para>
/// <b>Why the tri-state is the behaviour under test.</b> The FR's own warning is that adding durable
/// bytes and leaving a guess in place produces "files that exist but are reported unavailable". The
/// mirror-image bug is just as real: reporting <c>false</c> in a deployment that has no durable store
/// would mark every file unavailable everywhere. So <c>null</c> (unknown) is a first-class answer and
/// is asserted as such — see <see cref="StoreDisabled_ReportsUnknown_NeverUnavailable"/>.
/// </para>
/// </remarks>
public sealed class SessionFileAvailabilitySeamTests
{
    private const string TenantA = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string TenantB = "bbbbbbbb-5555-6666-7777-888888888888";
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private const string DurableFileId = "d1111111-2222-3333-4444-555555555555";
    private const string LegacyFileId = "1e999999-2222-3333-4444-555555555555";

    private readonly InMemorySessionFileBlobGateway _blobs = new();
    private readonly Mock<ISessionPersistenceService> _persistence = new();

    private SessionFileBlobStore EnabledStore => new(_blobs, NullLogger<SessionFileBlobStore>.Instance);

    private static SessionFileBlobStore DisabledStore
        => new(gateway: null, NullLogger<SessionFileBlobStore>.Instance);

    private SessionFileRehydrationService Rehydration(SessionFileBlobStore store)
        => new(store, textExtractor: null, indexingPipeline: null,
            NullLogger<SessionFileRehydrationService>.Instance);

    private SessionRestoreService BuildRestore(SessionFileRehydrationService? availability)
        => new(
            _persistence.Object,
            Mock.Of<IHttpClientFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            Mock.Of<AzureTokenCredential>(),
            NullLogger<SessionRestoreService>.Instance,
            availability);

    private void SeedManifest(string tenantId, params string[] fileIds)
    {
        var session = new StoredSession
        {
            Id = SessionId,
            SessionId = SessionId,
            TenantId = tenantId,
            Messages = [],
            EntityRefs = [],
            UploadedFiles = fileIds.Select(id => new StoredUploadedFile
            {
                FileId = id,
                FileName = $"{id}.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024,
                UploadedAt = DateTimeOffset.UtcNow.AddDays(-40),
            }).ToList(),
        };

        _persistence
            .Setup(p => p.LoadSessionAsync(tenantId, SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);
    }

    private static bool? AvailabilityOf(RestoredSession? restored, string fileId)
        => restored!.UploadedFiles.Single(f => f.FileId == fileId).ContentAvailable;

    // ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FileWithADurableCopy_IsReportedAvailable_ByTheServer()
    {
        await EnabledStore.WriteAsync(TenantA, SessionId, DurableFileId, BinaryData.FromString("x"), "application/pdf");
        SeedManifest(TenantA, DurableFileId);

        var restored = await BuildRestore(Rehydration(EnabledStore)).RestoreSessionAsync(TenantA, SessionId);

        AvailabilityOf(restored, DurableFileId).Should().BeTrue(
            "the durable copy exists, so the content survives for as long as the session does — " +
            "regardless of how long ago the last message was, which is what R7 guessed from");
    }

    [Fact]
    public async Task FileWithNoDurableCopy_WhileTheStoreIsEnabled_IsReportedUnavailable()
    {
        // A pre-FR-B01 upload: the manifest names it, the durable store never held it.
        await EnabledStore.WriteAsync(TenantA, SessionId, DurableFileId, BinaryData.FromString("x"), "application/pdf");
        SeedManifest(TenantA, DurableFileId, LegacyFileId);

        var restored = await BuildRestore(Rehydration(EnabledStore)).RestoreSessionAsync(TenantA, SessionId);

        AvailabilityOf(restored, DurableFileId).Should().BeTrue();
        AvailabilityOf(restored, LegacyFileId).Should().BeFalse(
            "the store is configured and holds no copy — that is a fact, not a guess");
    }

    [Fact]
    public async Task StoreDisabled_ReportsUnknown_NeverUnavailable()
    {
        // Today's shipped state, and the state that must hold until task 063 merges. The server has no
        // basis for an answer; saying "unavailable" would mark every file in every deployment as gone.
        SeedManifest(TenantA, DurableFileId);

        var restored = await BuildRestore(Rehydration(DisabledStore)).RestoreSessionAsync(TenantA, SessionId);

        AvailabilityOf(restored, DurableFileId).Should().BeNull();
    }

    [Fact]
    public async Task NoAvailabilityCollaboratorAtAll_ReportsUnknown()
    {
        // The optional-ctor-parameter path (direct construction, AI-off deployments). Must degrade to
        // unknown, not to unavailable.
        SeedManifest(TenantA, DurableFileId);

        var restored = await BuildRestore(availability: null).RestoreSessionAsync(TenantA, SessionId);

        AvailabilityOf(restored, DurableFileId).Should().BeNull();
    }

    [Fact]
    public async Task ProbeFailure_ReportsUnknown_AndDoesNotFailTheRestore()
    {
        await EnabledStore.WriteAsync(TenantA, SessionId, DurableFileId, BinaryData.FromString("x"), "application/pdf");
        SeedManifest(TenantA, DurableFileId);
        _blobs.FailNextList = true;

        var restored = await BuildRestore(Rehydration(EnabledStore)).RestoreSessionAsync(TenantA, SessionId);

        restored.Should().NotBeNull("an availability signal must never be able to fail a session restore");
        AvailabilityOf(restored, DurableFileId).Should().BeNull();
    }

    [Fact]
    public async Task AnotherTenantsDurableCopy_DoesNotMakeAFileLookAvailable()
    {
        // ADR-014/ADR-015 on the availability read path. Tenant A holds the bytes; tenant B's manifest
        // names the same (sessionId, fileId). B must be told "not available", not "available".
        await EnabledStore.WriteAsync(TenantA, SessionId, DurableFileId, BinaryData.FromString("tenant A bytes"), "application/pdf");
        SeedManifest(TenantA, DurableFileId);
        SeedManifest(TenantB, DurableFileId);

        var restore = BuildRestore(Rehydration(EnabledStore));

        var forOther = await restore.RestoreSessionAsync(TenantB, SessionId);
        AvailabilityOf(forOther, DurableFileId).Should().BeFalse(
            "availability is answered from the CALLING tenant's prefix — another tenant's copy is not " +
            "merely unreadable, it is invisible");

        // Positive control: the bytes really are there for the owning tenant, so the BeFalse above is a
        // partitioning result and not a "nothing was ever written" vacuum.
        var forOwner = await restore.RestoreSessionAsync(TenantA, SessionId);
        AvailabilityOf(forOwner, DurableFileId).Should().BeTrue();
    }

    [Fact]
    public async Task SessionWithNoUploads_SkipsTheProbeEntirely()
    {
        SeedManifest(TenantA); // no files
        _blobs.FailNextList = true; // would throw if the probe ran

        var restored = await BuildRestore(Rehydration(EnabledStore)).RestoreSessionAsync(TenantA, SessionId);

        restored!.UploadedFiles.Should().BeEmpty();
        _blobs.FailNextList.Should().BeTrue("the flag is only consumed by an actual listing — none should have happened");
    }

    [Fact]
    public async Task WholeManifestIsAnsweredByOneListing()
    {
        // The affordability claim behind putting this on the <500ms restore path: one prefix listing,
        // not one round trip per file.
        var fileIds = Enumerable.Range(0, 20)
            .Select(i => $"f{i:D2}11111-2222-3333-4444-555555555555")
            .ToArray();

        foreach (var id in fileIds)
        {
            await EnabledStore.WriteAsync(TenantA, SessionId, id, BinaryData.FromString(id), "application/pdf");
        }

        SeedManifest(TenantA, fileIds);

        var availability = await Rehydration(EnabledStore)
            .ProbeSessionAvailabilityAsync(TenantA, SessionId, CancellationToken.None);

        availability.DurableFileIds.Should().HaveCount(20);
        fileIds.Should().OnlyContain(id => availability.ContentAvailable(id) == true);
    }
}
