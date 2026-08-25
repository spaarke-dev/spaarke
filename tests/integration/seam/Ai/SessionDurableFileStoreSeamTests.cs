using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Sessions;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// <c>tests/integration/seam/**</c> — vertical-slice seam for spaarkeai-compose-r8 FR-B01
/// (task 060): a real multipart upload, through the real endpoint handler, must leave a durable
/// tenant-partitioned byte copy behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a seam test and not a contract test.</b> A contract-shape assertion ("the endpoint returns
/// 202", "the store has a WriteAsync method") is exactly the kind of green that let the original
/// defect ship: every layer was individually correct and the file still evaporated after the cache
/// TTL. This suite drives the wire — <c>POST /api/ai/chat/sessions/{id}/documents</c> with a real
/// multipart body — and then observes the STORED artefact, keyed by the blob name the production
/// store actually composed.
/// </para>
/// <para>
/// <b>Real path, one boundary faked.</b> The endpoint handler, <see cref="SessionFileBlobStore"/>,
/// its name construction and its tenant assertion are the production types. Only the Azure Blob SDK
/// call is substituted (<c>InMemorySessionFileBlobGateway</c>) — this session has no storage account.
/// </para>
/// <para>
/// <b>Observed to fail before it passed.</b> With step 9c removed from
/// <c>ChatDocumentEndpoints.UploadDocumentAsync</c> (i.e. the upload still 202s, still writes Redis,
/// still indexes, still updates the manifest — the pre-task-060 world),
/// <see cref="Upload_WritesADurableByteCopyAtUploadTime"/>,
/// <see cref="Upload_DurableCopyIsReadableByTheUploadingTenant"/> and
/// <see cref="Upload_DurableCopy_IsNotReadableByAnotherTenant"/> all fail. Recorded in
/// <c>projects/spaarkeai-compose-r8/notes/track-b-placement-justification.md</c> §6.
/// </para>
/// </remarks>
public sealed class SessionDurableFileStoreSeamTests : IClassFixture<ChatDocumentEndpointsTestFixture>
{
    private const string TenantA = "00000000-0000-0000-0000-000000000abc";
    private const string TenantB = "ffffffff-eeee-dddd-cccc-bbbbbbbbbbbb";
    private const string SessionId = "11111111-2222-3333-4444-555555555555";

    private readonly ChatDocumentEndpointsTestFixture _fx;

    public SessionDurableFileStoreSeamTests(ChatDocumentEndpointsTestFixture fx) => _fx = fx;

    // ─────────────────────────────────────────────────────────────────────────
    // FR-B01 — the write happens at upload time, on the real path.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_WritesADurableByteCopyAtUploadTime()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(SessionId);

        var body = "Durable content that must outlive the four-hour session cache.";
        var response = await UploadAsync("evidence.pdf", body);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        _fx.DurableBlobs.Count.Should().Be(1,
            "the upload endpoint must write exactly one durable byte copy per uploaded file, at upload " +
            "time — not lazily, and not derived later from content that may already have expired");

        // The manifest entry and the durable blob must agree on the identity of the file. If they
        // drifted, the day-60 recall this feature exists for would look up bytes that were never stored.
        var fileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;
        _fx.DurableBlobs.TryPeek(
                SessionFileBlobStore.BuildBlobName(TenantA, SessionId, fileId), out var stored)
            .Should().BeTrue("the durable copy must be addressable by the SAME (tenant, session, file) " +
                             "identity that the session manifest records");
        Encoding.UTF8.GetString(stored!.ToArray()).Should().Be(body,
            "the durable copy must be the ORIGINAL bytes, not the extracted text");
    }

    [Fact]
    public async Task Upload_DurableCopyIsReadableByTheUploadingTenant()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(SessionId);

        var response = await UploadAsync("readback.pdf", "Read me back on day sixty.");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var fileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        // Read through the production store, exactly as task 061's lazy re-index will.
        var readBack = await _fx.DurableFileStore.ReadAsync(TenantA, SessionId, fileId);

        readBack.Should().NotBeNull("a durable copy that cannot be read back is not durable");
        readBack!.Content.ToString().Should().Be("Read me back on day sixty.");
        readBack.ContentType.Should().Be("application/pdf",
            "the stored content type must match the manifest entry's — they are computed once, together");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ADR-014 / ADR-015 — isolation observed through the wire, not just at the store API.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_DurableCopy_IsNotReadableByAnotherTenant()
    {
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(SessionId);

        await UploadAsync("privileged.pdf", "Tenant A privileged content.");
        var fileId = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        // Another tenant, holding the leaked session id + file id, attempts the same read.
        var crossTenant = await _fx.DurableFileStore.ReadAsync(TenantB, SessionId, fileId);

        crossTenant.Should().BeNull(
            "bytes uploaded under one tenant must be unreachable from another, even with the exact " +
            "identifiers (ADR-014 / ADR-015)");
    }

    [Fact]
    public async Task TwoTenantsUploadingToTheSameSessionId_ProduceSeparateDurableCopies()
    {
        _fx.Reset();

        // Tenant A uploads.
        _fx.Sessions.Session = BuildSession(SessionId);
        (await UploadAsync("a.pdf", "content A")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var fileIdA = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        // Tenant B authenticates as a different tenant and uploads to the same session id.
        _fx.Auth.TenantId = TenantB;
        _fx.Sessions.Session = BuildSession(SessionId, TenantB);
        _fx.Sessions.PersistedSession = null;
        (await UploadAsync("b.pdf", "content B")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var fileIdB = _fx.Sessions.PersistedSession!.UploadedFiles!.Single().FileId;

        _fx.DurableBlobs.Count.Should().Be(2, "each tenant's upload gets its own durable blob");
        _fx.DurableBlobs.BlobNames.Should().OnlyContain(
            n => n.StartsWith(TenantA + "/", StringComparison.Ordinal)
              || n.StartsWith(TenantB + "/", StringComparison.Ordinal),
            "every durable blob written through the wire must sit under its own tenant's prefix");

        (await _fx.DurableFileStore.ReadAsync(TenantA, SessionId, fileIdA))!.Content.ToString()
            .Should().Be("content A");
        (await _fx.DurableFileStore.ReadAsync(TenantB, SessionId, fileIdB))!.Content.ToString()
            .Should().Be("content B");

        // The decisive direction: tenant A must not reach tenant B's file id and vice versa.
        (await _fx.DurableFileStore.ReadAsync(TenantA, SessionId, fileIdB)).Should().BeNull();
        (await _fx.DurableFileStore.ReadAsync(TenantB, SessionId, fileIdA)).Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The pre-existing behaviour this must not disturb.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_StillWritesTheSessionCacheAndManifest_AlongsideTheDurableCopy()
    {
        // FR-B05 reuses R7's re-attach layer rather than rebuilding it, so the manifest and the hot
        // cache writes must survive this change untouched.
        _fx.Reset();
        _fx.Sessions.Session = BuildSession(SessionId);

        (await UploadAsync("both.pdf", "content")).StatusCode.Should().Be(HttpStatusCode.Accepted);

        _fx.CacheCalls.Should().Contain("doc-upload-binary",
            "the 4h hot binary cache is still the fast path — the durable copy is added, not substituted");
        _fx.Sessions.PersistedSession!.UploadedFiles.Should().HaveCount(1,
            "the session manifest write is unchanged");
        _fx.DurableBlobs.Count.Should().Be(1);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<HttpResponseMessage> UploadAsync(string filename, string content)
    {
        var client = _fx.CreateAuthenticatedClient();
        using var form = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(Encoding.UTF8.GetBytes(content));
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(byteContent, "file", filename);

        return await client.PostAsync($"/api/ai/chat/sessions/{SessionId}/documents", form);
    }

    private static ChatSession BuildSession(string sessionId, string tenantId = TenantA)
        => new(
            SessionId: sessionId,
            TenantId: tenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: null);
}
