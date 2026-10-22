using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Storage;

namespace Sprk.Bff.Api.Services.Finance.Files;

/// <summary>
/// The blob half of the Finance file store (ebilling-r1 task 090; procedure-and-file-model section 4.2): write-once staging under the
/// tenant-partitioned name, streamed SHA-256 verification, the malware-scan verdict from the Defender index tag, pinned reads,
/// deletes and the listing the reconcile job walks. It holds no Dataverse logic; <see cref="StoredFileService"/> owns the rows.
/// </summary>
/// <remarks>
/// <para><b>Placement and lifetime.</b> Singleton: one managed-identity <see cref="BlobGateway"/> over the shared
/// <see cref="TokenCredential"/>, no per-request state. Not configured (empty <c>Finance:FileStore:BlobEndpoint</c>) is a state, not
/// a crash: <see cref="IsConfigured"/> is false and every operation refuses or reports it.</para>
/// <para><b>Logs carry ids only</b> (container, blob name = tenant/table/ids): never file names, content types or bytes. No SAS or
/// storage URL is produced anywhere in this type.</para>
/// <para><b>Scan tags</b> (researcher, Microsoft Learn 2026-10-22): Defender for Storage writes the index tag
/// <see cref="ScanResultTagKey"/> with "No threats found" or "Malicious" (other values: "Error", "Not scanned", SAM error strings),
/// and a scan time tag. The key is matched ignoring case (Learn spells "Malware scanning scan result", the portal lab "Malware
/// Scanning scan result"). The scan-time key <see cref="ScanTimeTagKey"/> is NOT confirmed on Learn: an absent or unreadable scan
/// time refuses (Pending), so an unconfirmed key fails closed and is a live verification for the first environment with scanning on
/// (notes/090-file-store-service.md). This type never writes tags (091: tag writes are refused by the role anyway).</para>
/// </remarks>
public sealed class StoredFileBlobs
{
    /// <summary>The Defender for Storage scan-result index tag (matched ignoring case).</summary>
    public const string ScanResultTagKey = "Malware Scanning scan result";

    /// <summary>The Defender for Storage scan-time index tag (matched ignoring case; unconfirmed on Learn, see remarks).</summary>
    public const string ScanTimeTagKey = "Malware Scanning scan time UTC";

    /// <summary>The clean verdict value (compared exactly).</summary>
    public const string ScanCleanValue = "No threats found";

    /// <summary>The malicious verdict value (compared exactly).</summary>
    public const string ScanMaliciousValue = "Malicious";

    /// <summary>Blob metadata keys written at stage (lower case: metadata keys are C# identifiers on the wire).</summary>
    internal const string MetadataSha256 = "sha256", MetadataPurpose = "purpose", MetadataSubmission = "submissionid";

    private const int CopyBufferBytes = 81920;

    private readonly BlobGateway? _gateway;
    private readonly ILogger _logger;

    /// <summary>Production constructor: the bound <c>Finance:FileStore</c> section and the shared managed-identity credential.</summary>
    /// <exception cref="InvalidOperationException">A configured endpoint or tenant is unusable (start-up validation reports it first).</exception>
    public StoredFileBlobs(IOptions<FinanceOptions> options, TokenCredential credential, ILogger<StoredFileBlobs> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credential);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var fileStore = options.Value.FileStore ?? new FinanceFileStoreOptions();
        ScanningEnabled = fileStore.ScanningEnabled;
        if (!fileStore.IsConfigured)
        {
            return;
        }

        var endpoint = BlobEndpointRules.ParseManagedIdentityEndpoint(fileStore.BlobEndpoint, FinanceFileStoreOptions.SectionPath + ":BlobEndpoint");
        if (!Guid.TryParse(fileStore.TenantId?.Trim(), out var tenant) || tenant == Guid.Empty)
        {
            throw new InvalidOperationException($"'{FinanceFileStoreOptions.SectionPath}:TenantId' must be a non-empty GUID when the endpoint is set.");
        }

        TenantId = tenant;
        _gateway = new AzureBlobGateway(endpoint, credential);
    }

    /// <summary>Test constructor: a substitute gateway (<c>null</c> = not configured).</summary>
    internal StoredFileBlobs(BlobGateway? gateway, Guid tenantId, bool? malwareScanningEnabled, ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (gateway is not null && tenantId == Guid.Empty)
        {
            throw new ArgumentException("A configured store needs a tenant.", nameof(tenantId));
        }

        _gateway = gateway;
        TenantId = tenantId;
        ScanningEnabled = malwareScanningEnabled != false;
    }

    /// <summary>True when an endpoint is configured.</summary>
    public bool IsConfigured => _gateway is not null;

    /// <summary>The tenant segment of every name (<see cref="Guid.Empty"/> when not configured).</summary>
    public Guid TenantId { get; }

    /// <summary>False only when <c>MalwareScanningEnabled</c> is explicitly <c>false</c>.</summary>
    public bool ScanningEnabled { get; }

    /// <summary>
    /// Stages <paramref name="content"/> under <paramref name="fileId"/> (section 4.2): the length is checked FIRST (a length that
    /// does not fit <c>sprk_sizebytes</c> refuses before any storage call), then the SHA-256 is computed, then one write-once create
    /// (<c>If-None-Match: *</c>) with the metadata. An existing blob with the same SHA-256 and length is reused (a retry); one with
    /// different bytes refuses and is left unchanged. A non-seekable stream is buffered to a temporary file first (bounded by the
    /// same limit). The stream is read from its current position.
    /// </summary>
    /// <exception cref="RequestFailedException">A storage fault (the caller retries; nothing is half-written: the create is atomic).</exception>
    public async Task<StageResult> StageAsync(
        StoredFilePurpose purpose, StoredFileParent parent, Guid fileId, Stream content, string contentType, Guid? submissionId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var gateway = _gateway;
        if (gateway is null)
        {
            return StageResult.Refused(StoredFileRefusal.NotConfigured);
        }

        if (!Enum.IsDefined(purpose) || StoredFileSchema.ParentKindOf(purpose) != parent.Kind || fileId == Guid.Empty || parent.Id == Guid.Empty
            || (submissionId is { } s && s == Guid.Empty))
        {
            return StageResult.Refused(StoredFileRefusal.InvalidRequest);
        }

        var container = StoredFileSchema.ContainerOf(purpose);
        var name = StoredFileBlobName.Build(TenantId, parent, fileId);

        // The finished name is re-checked against the tenant before any storage call (section 4.2).
        if (!StoredFileBlobName.TryParseForTenant(name, TenantId, out var parsedParent, out var parsedFile)
            || parsedParent != parent || parsedFile != fileId)
        {
            return StageResult.Refused(StoredFileRefusal.InvalidRequest);
        }

        FileStream? buffered = null;
        try
        {
            var source = content;
            if (!content.CanSeek)
            {
                buffered = await BufferAsync(content, ct).ConfigureAwait(false);
                if (buffered is null)
                {
                    LogSizeRefusal(container, name, length: null);
                    return StageResult.Refused(StoredFileRefusal.SizeOutOfRange);
                }

                source = buffered;
            }

            var start = source.Position;
            var length = source.Length - start;
            if (!StoredFileSize.TryToColumnValue(length, out _))
            {
                LogSizeRefusal(container, name, length);
                return StageResult.Refused(StoredFileRefusal.SizeOutOfRange);
            }

            var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(source, ct).ConfigureAwait(false));
            source.Position = start;

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [MetadataSha256] = sha256,
                [MetadataPurpose] = ((int)purpose).ToString(CultureInfo.InvariantCulture),
            };
            if (submissionId is { } submission)
            {
                metadata[MetadataSubmission] = submission.ToString("D");
            }

            var outcome = await gateway.CreateIfNotExistsAsync(container, name, source, contentType, metadata, ct).ConfigureAwait(false);
            if (outcome == BlobCreateOutcome.Created)
            {
                return StageResult.Staged(new StagedBlob(container, name, sha256, length, Reused: false));
            }

            var existing = await gateway.GetPropertiesAsync(container, name, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Blob {container}/{name} existed at create but not at the read that followed; retry.");

            if (existing.Metadata.TryGetValue(MetadataSha256, out var existingSha)
                && string.Equals(existingSha, sha256, StringComparison.Ordinal)
                && existing.ContentLength == length)
            {
                return StageResult.Staged(new StagedBlob(container, name, sha256, length, Reused: true));
            }

            _logger.LogWarning(
                "Stored file stage refused: blob {Container}/{BlobName} already holds different bytes; left unchanged ({Code})",
                container, name, StoredFileRefusal.ContentConflict);
            return StageResult.Refused(StoredFileRefusal.ContentConflict);
        }
        finally
        {
            if (buffered is not null)
            {
                await buffered.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>What a streamed verification found.</summary>
    internal enum Verification
    {
        Missing,
        Mismatch,
        Match,
    }

    /// <summary>Recomputes the blob's SHA-256 by streaming and compares it with <paramref name="expectedSha256"/> (ordinal, lower-case hex).</summary>
    /// <exception cref="RequestFailedException">A storage fault.</exception>
    internal async Task<Verification> VerifyAsync(string container, string blobPath, string? expectedSha256, CancellationToken ct)
    {
        var gateway = RequireOwnBlob(container, blobPath);
        await using var stream = await gateway.OpenReadAsync(container, blobPath, ifMatch: null, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return Verification.Missing;
        }

        var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        return string.Equals(actual, expectedSha256, StringComparison.Ordinal) ? Verification.Match : Verification.Mismatch;
    }

    /// <summary>
    /// The blob's properties and its scan verdict, or <c>null</c> when the blob does not exist. The verdict is
    /// <see cref="StoredFileScanState.NotScanned"/> only when scanning is explicitly off; otherwise <see cref="ClassifyScan"/> over
    /// the blob's index tags and its Last-Modified.
    /// </summary>
    /// <exception cref="RequestFailedException">A storage fault (including a tag read the role does not allow).</exception>
    internal async Task<(StoredFileScanState State, BlobPropertiesInfo Properties)?> GetScanStateAsync(
        string container, string blobPath, CancellationToken ct)
    {
        var gateway = RequireOwnBlob(container, blobPath);
        var properties = await gateway.GetPropertiesAsync(container, blobPath, ct).ConfigureAwait(false);
        if (properties is null)
        {
            return null;
        }

        if (!ScanningEnabled)
        {
            return (StoredFileScanState.NotScanned, properties);
        }

        var tags = await gateway.GetTagsAsync(container, blobPath, ct).ConfigureAwait(false);
        return tags is null ? null : (ClassifyScan(tags, properties.LastModified), properties);
    }

    /// <summary>
    /// The download rule over a blob's index tags (section 4.2; uac-r2 condition 3, 091): <see cref="StoredFileScanState.Malicious"/>
    /// when any scan-result tag says "Malicious"; <see cref="StoredFileScanState.Clean"/> only when every scan-result tag says "No
    /// threats found" AND exactly one scan-time tag parses (invariant culture, UTC) to a time no earlier than
    /// <paramref name="lastModified"/>; <see cref="StoredFileScanState.Pending"/> for everything else — no tag, another value
    /// ("Error", "Not scanned", a SAM code), conflicting tags, a missing or unparseable scan time, or a scan older than the content
    /// (an overwrite after the scan). Keys are matched ignoring case; values exactly.
    /// </summary>
    public static StoredFileScanState ClassifyScan(IReadOnlyDictionary<string, string> tags, DateTimeOffset lastModified)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var results = tags.Where(t => string.Equals(t.Key, ScanResultTagKey, StringComparison.OrdinalIgnoreCase)).Select(t => t.Value).ToList();
        if (results.Any(v => string.Equals(v, ScanMaliciousValue, StringComparison.Ordinal)))
        {
            return StoredFileScanState.Malicious;
        }

        if (results.Count == 0 || !results.All(v => string.Equals(v, ScanCleanValue, StringComparison.Ordinal)))
        {
            return StoredFileScanState.Pending;
        }

        var times = tags.Where(t => string.Equals(t.Key, ScanTimeTagKey, StringComparison.OrdinalIgnoreCase)).Select(t => t.Value).ToList();
        if (times.Count != 1
            || !DateTimeOffset.TryParse(times[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var scannedAt))
        {
            return StoredFileScanState.Pending;
        }

        return scannedAt >= lastModified ? StoredFileScanState.Clean : StoredFileScanState.Pending;
    }

    /// <summary>
    /// A read stream over the blob pinned to <paramref name="etag"/> (the version whose verdict was read), or <c>null</c> when it is
    /// gone. A blob changed since then fails with <see cref="RequestFailedException"/> status 412.
    /// </summary>
    internal Task<Stream?> OpenReadAsync(string container, string blobPath, ETag etag, CancellationToken ct) =>
        RequireOwnBlob(container, blobPath).OpenReadAsync(container, blobPath, etag, ct);

    /// <summary>The whole blob pinned to <paramref name="etag"/>, or <c>null</c> when it is gone (jobs only: at most 2 GiB by the size rule).</summary>
    internal async Task<BinaryData?> ReadAllAsync(string container, string blobPath, ETag etag, CancellationToken ct)
    {
        await using var stream = await OpenReadAsync(container, blobPath, etag, ct).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, CopyBufferBytes, ct).ConfigureAwait(false);
        return new BinaryData(buffer.ToArray());
    }

    /// <summary>Deletes one blob of this tenant (with snapshots); <c>false</c> when it was already gone.</summary>
    /// <exception cref="ArgumentException"><paramref name="blobPath"/> is not a name of this tenant (nothing is called).</exception>
    public Task<bool> DeleteAsync(string container, string blobPath, CancellationToken ct) =>
        RequireOwnBlob(container, blobPath).DeleteIfExistsAsync(container, blobPath, ct);

    /// <summary>
    /// The gateway, after checking that <paramref name="blobPath"/> is a stored-file name of THIS tenant in one of the three
    /// containers. Every blob-level read and delete goes through it, so no caller of this class can reach another tenant's bytes
    /// even when it skips the service's own-path check.
    /// </summary>
    /// <exception cref="ArgumentException">Not a stored-file blob of this tenant (nothing is called).</exception>
    private BlobGateway RequireOwnBlob(string container, string? blobPath)
    {
        var gateway = RequireGateway();
        if (!StoredFileSchema.Containers.Contains(container) || !StoredFileBlobName.TryParseForTenant(blobPath, TenantId, out _, out _))
        {
            throw new ArgumentException("Not a stored-file blob of this tenant.", nameof(blobPath));
        }

        return gateway;
    }

    /// <summary>
    /// Every blob of this tenant in the three containers, with its parsed parent and file id and its metadata. Names that do not
    /// parse as this tenant's are skipped (never deleted by the reconcile job) and counted in a debug log.
    /// </summary>
    internal async IAsyncEnumerable<StoredBlobListing> ListAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var gateway = RequireGateway();
        var prefix = TenantId.ToString("D") + "/";
        foreach (var container in StoredFileSchema.Containers)
        {
            var skipped = 0;
            await foreach (var item in gateway.ListAsync(container, prefix, includeMetadata: true, ct).ConfigureAwait(false))
            {
                if (!StoredFileBlobName.TryParseForTenant(item.Name, TenantId, out var parent, out var fileId))
                {
                    skipped++;
                    continue;
                }

                yield return new StoredBlobListing(container, item.Name, parent, fileId, item.CreatedOn, item.LastModified, item.Metadata);
            }

            if (skipped > 0)
            {
                _logger.LogDebug("Stored file listing skipped {Count} names in {Container} that are not stored-file names", skipped, container);
            }
        }
    }

    private BlobGateway RequireGateway() =>
        _gateway ?? throw new InvalidOperationException($"The Finance file store is not configured ({StoredFileRefusal.NotConfigured}).");

    private void LogSizeRefusal(string container, string name, long? length) =>
        _logger.LogWarning(
            "Stored file stage refused: length {Length} of {Container}/{BlobName} is outside 0..{Max} bytes; nothing written ({Code})",
            length?.ToString(CultureInfo.InvariantCulture) ?? "over the limit", container, name, StoredFileSize.MaxBytes, StoredFileRefusal.SizeOutOfRange);

    /// <summary>Copies a non-seekable stream to a delete-on-close temporary file; <c>null</c> when it exceeds the size limit.</summary>
    private static async Task<FileStream?> BufferAsync(Stream content, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), "sprk-storedfile-" + Path.GetRandomFileName());
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, CopyBufferBytes,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            var buffer = new byte[CopyBufferBytes];
            long total = 0;
            int read;
            while ((read = await content.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > StoredFileSize.MaxBytes)
                {
                    await file.DisposeAsync().ConfigureAwait(false);
                    return null;
                }

                await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}

/// <summary>One blob of this tenant, as the reconcile job sees it.</summary>
internal sealed record StoredBlobListing(
    string Container,
    string BlobPath,
    StoredFileParent Parent,
    Guid FileId,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? LastModified,
    IReadOnlyDictionary<string, string> Metadata);
