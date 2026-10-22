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
/// Scanning scan result"). The scan-time key <see cref="ScanTimeTagKey"/> and its value format are NOT stated on Learn ("Scan time
/// (UTC)" only); the one observed sample (an <c>az storage blob tag list</c> output, nineliveszerotrust.com, April 2026) is
/// <c>2026-04-17 17:40:38Z</c>, which <see cref="ScanTimeFormats"/> accepts. An absent, unreadable or future scan time refuses
/// (Pending), so an unconfirmed key or format fails closed and is a live verification for the first environment with scanning on
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

    /// <summary>
    /// The closed set of scan-time formats (invariant culture), each ending in an offset designator: the round-trip form, ISO 8601
    /// with optional fractions, and the space-separated form Defender was observed writing (<c>2026-04-17 17:40:38Z</c>). A value
    /// without a designator is refused even though <c>K</c> would parse it (see <see cref="TryParseScanTime"/>).
    /// </summary>
    internal static readonly string[] ScanTimeFormats = ["O", "yyyy-MM-ddTHH:mm:ss.FFFFFFFK", "yyyy-MM-dd HH:mm:ssK"];

    /// <summary>How far a scan time may lie ahead of this host's clock and still count (clock skew between Defender and the host).</summary>
    internal static readonly TimeSpan ScanClockSkew = TimeSpan.FromMinutes(5);

    /// <summary>Blob metadata keys written at stage (lower case: metadata keys are C# identifiers on the wire).</summary>
    internal const string MetadataSha256 = "sha256", MetadataPurpose = "purpose", MetadataSubmission = "submissionid", MetadataEnvironment = "environment";

    /// <summary>The environment marker of the test constructor's default.</summary>
    internal const string TestEnvironment = "storedfile-test.crm.dynamics.com";

    private const int CopyBufferBytes = 81920;

    private readonly BlobGateway? _gateway;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;

    /// <summary>Production constructor: the bound <c>Finance:FileStore</c> section and the shared managed-identity credential.</summary>
    /// <exception cref="InvalidOperationException">A configured endpoint or tenant is unusable (start-up validation reports it first).</exception>
    public StoredFileBlobs(
        IOptions<FinanceOptions> options, IOptions<DataverseOptions> dataverse, TokenCredential credential, TimeProvider timeProvider,
        ILogger<StoredFileBlobs> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(credential);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        Environment = EnvironmentMarkerOf(dataverse.Value.EnvironmentUrl);

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

    /// <summary>Test constructor: a substitute gateway (<c>null</c> = not configured) and clock (<c>null</c> = the system clock).</summary>
    internal StoredFileBlobs(
        BlobGateway? gateway, Guid tenantId, bool? malwareScanningEnabled, ILogger logger, string? environment = TestEnvironment,
        TimeProvider? timeProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
        if (gateway is not null && tenantId == Guid.Empty)
        {
            throw new ArgumentException("A configured store needs a tenant.", nameof(tenantId));
        }

        _gateway = gateway;
        TenantId = tenantId;
        ScanningEnabled = malwareScanningEnabled != false;
        Environment = environment;
    }

    /// <summary>True when an endpoint is configured.</summary>
    public bool IsConfigured => _gateway is not null;

    /// <summary>
    /// This deployment's environment marker: the lower-case host of <c>Dataverse:EnvironmentUrl</c> (one Dataverse environment per
    /// BFF deployment), written into every staged blob's metadata. The tenant segment partitions names; it does not tell apart two
    /// environments that share an account and a tenant id, so the reconcile job deletes a row-less blob only when it carries THIS
    /// marker (<c>null</c> when the URL is unusable: then no row-less blob is ever deleted).
    /// </summary>
    public string? Environment { get; }

    /// <summary>True when <paramref name="metadata"/> carries this deployment's environment marker.</summary>
    internal bool IsThisEnvironments(IReadOnlyDictionary<string, string> metadata) =>
        Environment is not null
        && metadata.TryGetValue(MetadataEnvironment, out var marker)
        && string.Equals(marker, Environment, StringComparison.Ordinal);

    internal static string? EnvironmentMarkerOf(string? environmentUrl) =>
        Uri.TryCreate(environmentUrl?.Trim(), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host.ToLowerInvariant()
            : null;

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

            if (Environment is not null)
            {
                metadata[MetadataEnvironment] = Environment;
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
        return tags is null ? null : (ClassifyScan(tags, properties.LastModified, _time.GetUtcNow()), properties);
    }

    /// <summary>
    /// The download rule over a blob's index tags (section 4.2; uac-r2 condition 3, 091): <see cref="StoredFileScanState.Malicious"/>
    /// when any scan-result tag says "Malicious"; <see cref="StoredFileScanState.Clean"/> only when every scan-result tag says "No
    /// threats found" AND exactly one scan-time tag parses (<see cref="TryParseScanTime"/>) to a time no earlier than
    /// <paramref name="lastModified"/> and no later than <paramref name="now"/> plus <see cref="ScanClockSkew"/>;
    /// <see cref="StoredFileScanState.Pending"/> for everything else — no tag, another value ("Error", "Not scanned", a SAM code),
    /// conflicting tags, a missing or unparseable scan time, a scan older than the content (an overwrite after the scan) or one in
    /// the future (a time no scan could have finished at). Keys are matched ignoring case; values exactly.
    /// </summary>
    public static StoredFileScanState ClassifyScan(IReadOnlyDictionary<string, string> tags, DateTimeOffset lastModified, DateTimeOffset now)
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
        if (times.Count != 1 || !TryParseScanTime(times[0], out var scannedAt))
        {
            return StoredFileScanState.Pending;
        }

        return scannedAt >= lastModified && scannedAt <= now + ScanClockSkew ? StoredFileScanState.Clean : StoredFileScanState.Pending;
    }

    /// <summary>
    /// Parses a scan-time tag value: exactly one of <see cref="ScanTimeFormats"/> (invariant culture, no surrounding white space) AND
    /// an explicit designator at the end (<c>Z</c> or <c>+hh:mm</c> / <c>-hh:mm</c>). The designator is checked separately because no
    /// <see cref="DateTimeStyles"/> value refuses a missing <c>K</c>: without it a bare time would be read in the host's offset.
    /// </summary>
    internal static bool TryParseScanTime(string? value, out DateTimeOffset scannedAt)
    {
        scannedAt = default;
        return value is not null
            && HasOffsetDesignator(value)
            && DateTimeOffset.TryParseExact(value, ScanTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out scannedAt);
    }

    private static bool HasOffsetDesignator(string value) =>
        value.EndsWith('Z')
        || (value.Length >= 6
            && value[^6] is '+' or '-'
            && char.IsAsciiDigit(value[^5]) && char.IsAsciiDigit(value[^4])
            && value[^3] == ':'
            && char.IsAsciiDigit(value[^2]) && char.IsAsciiDigit(value[^1]));

    /// <summary>The blob's properties and metadata, or <c>null</c> when it does not exist (the reconcile job's environment check).</summary>
    /// <exception cref="ArgumentException"><paramref name="blobPath"/> is not a name of this tenant (nothing is called).</exception>
    /// <exception cref="RequestFailedException">A storage fault.</exception>
    internal Task<BlobPropertiesInfo?> GetPropertiesAsync(string container, string blobPath, CancellationToken ct) =>
        RequireOwnBlob(container, blobPath).GetPropertiesAsync(container, blobPath, ct);

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
