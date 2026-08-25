using System.Text.RegularExpressions;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Outcome of a durable session-file byte write.
/// </summary>
/// <remarks>
/// Deliberately NOT a bool. A silent no-op write is the exact failure mode this component exists to
/// remove (the manifest keeps pointing at content that is gone), so the caller is forced to see the
/// difference between "the bytes are durable" and "this deployment has no durable store configured".
/// A genuine failure is NOT represented here — it throws, so a configured deployment can never
/// report success for bytes it did not store.
/// </remarks>
public enum SessionFileStoreOutcome
{
    /// <summary>The byte copy was written to the durable store.</summary>
    Written,

    /// <summary>
    /// No blob endpoint is configured for this deployment, so nothing was written. Behaviour is
    /// identical to the pre-FR-B01 world (bytes live only in the 4h Redis cache). Callers SHOULD
    /// log this, never treat it as success.
    /// </summary>
    StoreDisabled
}

/// <summary>Bytes read back from the durable session-file store, with the content type they were stored under.</summary>
public sealed record SessionFileBytes(BinaryData Content, string? ContentType);

/// <summary>
/// FR-B01 (spaarkeai-compose-r8 Track B) — the durable, tenant-partitioned byte copy of every chat
/// session upload.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect this closes.</b> A chat session lives 90 days (Cosmos <c>sessions</c> manifest,
/// ADR-015 Tier 3) but the content that makes its files usable did not: extracted text and the
/// original binary were written only to Redis with a 4h TTL
/// (<c>ChatDocumentEndpoints.UploadDocumentTtl</c>), and the AI-Search chunks are swept by
/// <c>SessionFilesCleanupJob</c> once the session's 24h Redis key expires. A conversation reopened on
/// day 2 therefore held a manifest pointing at content that no longer existed. This store keeps the
/// bytes.
/// </para>
/// <para>
/// <b>Placement.</b> Blob, not Cosmos (Cosmos holds JSON documents, not bytes) and not SPE (ADR-007
/// <c>SpeFileStore</c> is the matter/BU-scoped DMS — routing per-user chat scratch through it would
/// inherit its permission and retention model and pollute the DMS). No new Azure resource and no new
/// NuGet: <c>Azure.Storage.Blobs</c> was already referenced and the storage account, containers and
/// role assignment are already defined in <c>infrastructure/bicep/modules/storage-account.bicep</c>.
/// </para>
/// <para>
/// <b>Tenant isolation (ADR-014 / ADR-015 — the single most important property here).</b> The tenant
/// is the FIRST segment of every blob name, and it is the only segment a caller cannot reach past:
/// <c>{tenantId}/session-files/{sessionId}/{fileId}</c>. Every segment is validated against
/// <see cref="SafeSegment"/> before it is concatenated, so a crafted <c>sessionId</c> or
/// <c>fileId</c> cannot introduce a path separator and cannot land inside another tenant's prefix.
/// <see cref="AssertTenantPartitioned"/> then re-checks the finished name against the caller's tenant
/// on BOTH the read and the write path, so a future edit to <see cref="BuildBlobName"/> that dropped
/// the tenant segment fails loudly instead of silently sharing bytes. This is enforced by
/// <c>tests/integration/tenant/Ai/SessionFileBlobStoreTenantIsolationTests.cs</c>, which reads back
/// through the same gateway a write went through — a genuine reachability check, not an assertion
/// about a string.
/// </para>
/// <para>
/// <b>Authentication.</b> Managed identity only. The constructor takes a <see cref="TokenCredential"/>
/// (the DI singleton pinned to the UAMI clientId — see <c>ManagedIdentityCredentialFactory</c>) and a
/// bare blob endpoint URI. It is structurally impossible to pass an account key or connection string,
/// and <see cref="RejectSecretBearingEndpoint"/> fails fast at construction if someone configures one
/// anyway (root CLAUDE.md §9).
/// </para>
/// <para>
/// <b>Not in scope here.</b> Lazy re-index on recall and the cleanup-job scope change are task 061;
/// retention/availability is task 062; erasure is task 063. This type deliberately exposes no delete
/// so the 24h cleanup sweep has no code path that can reach durable bytes (spec FR-B03).
/// </para>
/// </remarks>
public sealed class SessionFileBlobStore
{
    /// <summary>
    /// The provisioned container this store writes into by default.
    /// </summary>
    /// <remarks>
    /// <c>ai-chunks</c> is provisioned in all three deployment stacks (<c>customer.bicep:45</c>,
    /// <c>stacks/model1-shared.bicep:141</c>, <c>stacks/model2-full.bicep:153</c>) and has no other
    /// consumer in the codebase, so there is no key collision. It is chosen over <c>temp-files</c> and
    /// <c>document-processing</c> because those names promise a lifecycle this content must NOT have.
    /// A dedicated <c>session-files</c> container would read better, but adding one is a bicep change
    /// (an owner decision, per the task's "no new Azure resource" constraint) — hence the override.
    /// This store NEVER creates a container.
    /// </remarks>
    public const string DefaultContainerName = "ai-chunks";

    /// <summary>Configuration key holding the storage account's blob endpoint (no key, no SAS).</summary>
    public const string BlobEndpointConfigKey = "SessionFileStore:BlobEndpoint";

    /// <summary>Configuration key overriding <see cref="DefaultContainerName"/>.</summary>
    public const string ContainerNameConfigKey = "SessionFileStore:ContainerName";

    /// <summary>Fixed path segment between the tenant and the session, so the container can host other per-tenant content later.</summary>
    internal const string SessionFilesPathSegment = "session-files";

    private const int MaxSegmentLength = 200;

    /// <summary>
    /// Blob-name segments are restricted to an identifier alphabet. Every value that reaches this
    /// store is a GUID in practice (tenant <c>tid</c> claim, server-minted session id, server-minted
    /// file id); anything else is rejected rather than sanitised, because a sanitised value could
    /// still collide with a legitimate one in a different tenant.
    /// </summary>
    /// <remarks>
    /// Anchored with <c>\A</c>/<c>\z</c>, NOT <c>^</c>/<c>$</c>. In .NET, <c>$</c> also matches
    /// immediately before a trailing <c>\n</c>, so <c>"abc\n"</c> would satisfy <c>^…$</c> and a
    /// newline would reach the blob name (and any header or log line derived from it). <c>\z</c> is
    /// the true end-of-string anchor.
    /// </remarks>
    private static readonly Regex SafeSegment = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SessionFileBlobGateway? _gateway;
    private readonly ILogger<SessionFileBlobStore> _logger;
    private int _disabledNoticeLogged;

    /// <summary>
    /// Production constructor. Wired from <c>AiPersistenceModule</c> with values read from
    /// configuration; the credential is the shared managed-identity <see cref="TokenCredential"/>.
    /// </summary>
    /// <param name="blobEndpoint">
    /// The storage account's blob endpoint, e.g. <c>https://sprkspaarkedevsa.blob.core.windows.net</c>
    /// (bicep output <c>storagePrimaryEndpoint</c>). Null/blank leaves the store DISABLED — writes
    /// return <see cref="SessionFileStoreOutcome.StoreDisabled"/> rather than throwing, so local dev
    /// and test hosts behave exactly as they did before this component existed.
    /// </param>
    /// <param name="containerName">Container override; null/blank uses <see cref="DefaultContainerName"/>.</param>
    /// <param name="credential">Managed-identity credential (DI singleton). Required.</param>
    /// <param name="logger">Logger. ADR-015: identifiers and sizes only, never content.</param>
    public SessionFileBlobStore(
        string? blobEndpoint,
        string? containerName,
        TokenCredential credential,
        ILogger<SessionFileBlobStore> logger)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(blobEndpoint))
        {
            _gateway = null;
            return;
        }

        RejectSecretBearingEndpoint(blobEndpoint);

        if (!Uri.TryCreate(blobEndpoint.Trim(), UriKind.Absolute, out var endpointUri))
        {
            throw new InvalidOperationException(
                $"'{BlobEndpointConfigKey}' is not an absolute URI. Expected the storage account's blob " +
                "endpoint, e.g. https://<account>.blob.core.windows.net");
        }

        var resolvedContainer = string.IsNullOrWhiteSpace(containerName)
            ? DefaultContainerName
            : containerName.Trim();

        var serviceClient = new BlobServiceClient(endpointUri, credential);
        _gateway = new AzureBlobSessionFileGateway(serviceClient.GetBlobContainerClient(resolvedContainer));

        _logger.LogInformation(
            "Durable session-file store enabled. Container={Container}, Endpoint={Endpoint}, Auth=ManagedIdentity",
            resolvedContainer, endpointUri.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>
    /// Test constructor — substitutes the blob boundary only. The name construction, the tenant
    /// assertion and every other behaviour under test remain the production ones.
    /// </summary>
    internal SessionFileBlobStore(SessionFileBlobGateway? gateway, ILogger<SessionFileBlobStore> logger)
    {
        _gateway = gateway;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>True when a blob endpoint is configured for this deployment.</summary>
    public bool IsEnabled => _gateway is not null;

    /// <summary>
    /// Writes the durable byte copy for one session upload.
    /// </summary>
    /// <returns>
    /// <see cref="SessionFileStoreOutcome.Written"/> when the bytes are durable, or
    /// <see cref="SessionFileStoreOutcome.StoreDisabled"/> when this deployment has no blob endpoint
    /// configured. Any other failure throws — a configured deployment must never report success for
    /// bytes it did not store.
    /// </returns>
    /// <exception cref="ArgumentException">A segment is missing or is not a safe blob-name segment.</exception>
    public async Task<SessionFileStoreOutcome> WriteAsync(
        string tenantId,
        string sessionId,
        string fileId,
        BinaryData content,
        string? contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var blobName = BuildBlobName(tenantId, sessionId, fileId);
        AssertTenantPartitioned(blobName, tenantId);

        if (_gateway is null)
        {
            LogDisabledOnce();
            return SessionFileStoreOutcome.StoreDisabled;
        }

        await _gateway.UploadAsync(blobName, content, contentType, cancellationToken).ConfigureAwait(false);

        // ADR-015: identifiers + size only. Never the file name, never the bytes.
        _logger.LogInformation(
            "Durable session-file copy written. TenantId={TenantId}, SessionId={SessionId}, FileId={FileId}, SizeBytes={SizeBytes}",
            tenantId, sessionId, fileId, content.ToMemory().Length);

        return SessionFileStoreOutcome.Written;
    }

    /// <summary>
    /// Reads the durable byte copy back, or <c>null</c> when this tenant has no such file (including
    /// when the file exists under a DIFFERENT tenant — that is the isolation guarantee, and it is
    /// indistinguishable from "does not exist" by design).
    /// </summary>
    /// <exception cref="ArgumentException">A segment is missing or is not a safe blob-name segment.</exception>
    public async Task<SessionFileBytes?> ReadAsync(
        string tenantId,
        string sessionId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var blobName = BuildBlobName(tenantId, sessionId, fileId);
        AssertTenantPartitioned(blobName, tenantId);

        if (_gateway is null)
        {
            LogDisabledOnce();
            return null;
        }

        return await _gateway.DownloadAsync(blobName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the tenant-partitioned blob name: <c>{tenantId}/session-files/{sessionId}/{fileId}</c>.
    /// </summary>
    /// <remarks>
    /// The tenant is FIRST deliberately: it makes the tenant boundary a prefix, so any future
    /// prefix-scoped operation (listing, a scoped SAS, a lifecycle rule, a GDPR prefix delete) is
    /// tenant-scoped by construction rather than by remembering to add a filter.
    /// </remarks>
    internal static string BuildBlobName(string tenantId, string sessionId, string fileId)
        => string.Concat(
            RequireSafeSegment(tenantId, nameof(tenantId)), "/",
            SessionFilesPathSegment, "/",
            RequireSafeSegment(sessionId, nameof(sessionId)), "/",
            RequireSafeSegment(fileId, nameof(fileId)));

    private static string RequireSafeSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"'{parameterName}' is required to build a tenant-partitioned session-file blob name.",
                parameterName);
        }

        if (value.Length > MaxSegmentLength || !SafeSegment.IsMatch(value) || value.Contains("..", StringComparison.Ordinal))
        {
            // The rejected value is attacker-influenceable, so it is NOT echoed into the log/message.
            throw new ArgumentException(
                $"'{parameterName}' is not a safe blob-name segment. Session-file identifiers must match " +
                "[A-Za-z0-9][A-Za-z0-9._-]* and must not contain path separators or '..'.",
                parameterName);
        }

        return value;
    }

    /// <summary>
    /// Belt-and-braces: the finished name MUST sit under the calling tenant's prefix. This exists so a
    /// future edit to <see cref="BuildBlobName"/> that reordered or dropped the tenant segment fails
    /// immediately instead of quietly making every tenant's bytes mutually reachable.
    /// </summary>
    private static void AssertTenantPartitioned(string blobName, string tenantId)
    {
        if (!blobName.StartsWith(tenantId + "/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Session-file blob name is not tenant-partitioned. This is an ADR-014/ADR-015 isolation " +
                "invariant: the tenant id must be the first path segment of every session-file blob.");
        }
    }

    private void LogDisabledOnce()
    {
        if (Interlocked.Exchange(ref _disabledNoticeLogged, 1) == 0)
        {
            _logger.LogWarning(
                "Durable session-file store is DISABLED — '{Key}' is not configured. Uploaded session files " +
                "will remain available only for the lifetime of the session cache, which is the pre-FR-B01 " +
                "behaviour a reopened conversation cannot rely on.",
                BlobEndpointConfigKey);
        }
    }

    /// <summary>
    /// Root CLAUDE.md §9 — a storage secret must never reach configuration. The endpoint setting is a
    /// bare URI; anything shaped like a connection string, account key or SAS is refused at startup
    /// rather than used.
    /// </summary>
    private static void RejectSecretBearingEndpoint(string blobEndpoint)
    {
        var probe = blobEndpoint.Trim();
        var carriesSecret =
            probe.Contains("AccountKey=", StringComparison.OrdinalIgnoreCase) ||
            probe.Contains("SharedAccessSignature", StringComparison.OrdinalIgnoreCase) ||
            probe.StartsWith("DefaultEndpointsProtocol=", StringComparison.OrdinalIgnoreCase) ||
            probe.Contains("?sig=", StringComparison.OrdinalIgnoreCase) ||
            probe.Contains("&sig=", StringComparison.OrdinalIgnoreCase);

        if (carriesSecret)
        {
            throw new InvalidOperationException(
                $"'{BlobEndpointConfigKey}' must be a bare blob endpoint URI. Connection strings, account " +
                "keys and SAS tokens are not accepted — the durable session-file store authenticates with " +
                "managed identity only (root CLAUDE.md §9, ADR-028).");
        }
    }
}

/// <summary>
/// The blob boundary, isolated so tests can substitute it without mocking the Azure SDK surface.
/// </summary>
/// <remarks>
/// Internal on purpose: this is an internal collaborator of <see cref="SessionFileBlobStore"/>, NOT a
/// DI seam. ADR-010 forbids introducing an interface (or a second registration) that has no genuine
/// multi-implementation requirement in production — there is exactly one production implementation,
/// and the store constructs it itself.
/// </remarks>
internal abstract class SessionFileBlobGateway
{
    public abstract Task UploadAsync(string blobName, BinaryData content, string? contentType, CancellationToken cancellationToken);

    public abstract Task<SessionFileBytes?> DownloadAsync(string blobName, CancellationToken cancellationToken);
}

/// <summary>Managed-identity-authenticated Azure Blob implementation of <see cref="SessionFileBlobGateway"/>.</summary>
internal sealed class AzureBlobSessionFileGateway : SessionFileBlobGateway
{
    private readonly BlobContainerClient _container;

    public AzureBlobSessionFileGateway(BlobContainerClient container)
        => _container = container ?? throw new ArgumentNullException(nameof(container));

    public override async Task UploadAsync(string blobName, BinaryData content, string? contentType, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);

        // No Conditions => overwrite. Re-uploading the same (tenant, session, file) is idempotent,
        // which matters because the upload endpoint may be retried by the client.
        var options = new BlobUploadOptions();
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            options.HttpHeaders = new BlobHttpHeaders { ContentType = contentType };
        }

        await blob.UploadAsync(content, options, cancellationToken).ConfigureAwait(false);
    }

    public override async Task<SessionFileBytes?> DownloadAsync(string blobName, CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);

        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            return new SessionFileBytes(response.Value.Content, response.Value.Details?.ContentType);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            // Missing blob AND missing container both surface as 404. Both mean "this tenant has no
            // durable copy of that file", which is exactly what the caller needs to know.
            return null;
        }
    }
}
