using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Azure;
using Azure.Core;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
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
/// <para><b>The name segment is the Dataverse environment's OrganizationId</b> (uac-r2 review round 1). <c>Finance:FileStore:TenantId</c>
/// is set per environment and must equal the OrganizationId of the environment this deployment serves, so a customer's prod and UAT
/// never share a prefix. Start-up validation cannot call Dataverse, so the first operation reads the <c>organization</c> row ONCE
/// (app-only) and compares: a match is cached for the process; a mismatch refuses every operation for the process (an error log
/// names both ids); a fault is not cached (the next operation asks again). The segment is configuration, never read per call, so a
/// row's path stays stable for its lifetime.</para>
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

    /// <summary>
    /// ebilling-r1 task 094 (from-email): the staged file's SOURCE, recorded at stage so the job that writes the row in another process
    /// reads it back (<see cref="StoredFileService.ReadStagedSourceAsync"/>): the source document, its communication, its version, the
    /// attachment identity, and, on a from-email Invoice part only, the ordinals of the Supporting parts staged with it and a file
    /// extension. Written only when the spec carries them; values are ASCII.
    /// </summary>
    internal const string MetadataSourceDocument = "sourcedocument", MetadataSourceCommunication = "sourcecommunication",
        MetadataSourceVersion = "sourceversion", MetadataSourceAttachment = "sourceattachment", MetadataEmlParts = "emlparts",
        MetadataExtension = "extension";

    /// <summary>The environment marker of the test constructor's default.</summary>
    internal const string TestEnvironment = "storedfile-test.crm.dynamics.com";

    private const int CopyBufferBytes = 81920;

    private readonly BlobGateway? _gateway;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Func<CancellationToken, Task<Guid>>? _organizationId;
    private readonly object _environmentGate = new();
    private Task<bool>? _environmentCheck;

    /// <summary>
    /// Production constructor: the bound <c>Finance:FileStore</c> section, the shared managed-identity credential, and the app-only
    /// Dataverse service the one OrganizationId check reads through.
    /// </summary>
    /// <exception cref="InvalidOperationException">A configured endpoint or tenant is unusable (start-up validation reports it first).</exception>
    public StoredFileBlobs(
        IOptions<FinanceOptions> options, IOptions<DataverseOptions> dataverse, TokenCredential credential, IGenericEntityService entities,
        TimeProvider timeProvider, ILogger<StoredFileBlobs> logger)
    {
        ArgumentNullException.ThrowIfNull(entities);
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
        _organizationId = ct => ReadOrganizationIdAsync(entities, ct);
    }

    /// <summary>
    /// Test constructor: a substitute gateway (<c>null</c> = not configured), clock (<c>null</c> = the system clock) and OrganizationId
    /// source (<c>null</c> = no check).
    /// </summary>
    internal StoredFileBlobs(
        BlobGateway? gateway, Guid tenantId, bool? malwareScanningEnabled, ILogger logger, string? environment = TestEnvironment,
        TimeProvider? timeProvider = null, Func<CancellationToken, Task<Guid>>? organizationId = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = timeProvider ?? TimeProvider.System;
        _organizationId = organizationId;
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

    /// <summary>
    /// The first segment of every name: this Dataverse environment's OrganizationId (<c>Finance:FileStore:TenantId</c>, checked against
    /// Dataverse before the first operation, see remarks; <see cref="Guid.Empty"/> when not configured).
    /// </summary>
    public Guid TenantId { get; }

    /// <summary>
    /// Throws unless <see cref="TenantId"/> is confirmed to be this environment's OrganizationId (see remarks). The reconcile job calls
    /// it first, so a mismatch fails the run (no progress possible) rather than each unit.
    /// </summary>
    /// <exception cref="InvalidOperationException">The configured segment is not this environment's OrganizationId.</exception>
    internal async Task EnsureEnvironmentAsync(CancellationToken ct)
    {
        if (!await EnvironmentConfirmedAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"'{FinanceFileStoreOptions.SectionPath}:TenantId' is not this Dataverse environment's OrganizationId; the file store refuses every operation.");
        }
    }

    /// <summary>
    /// True when the segment is this environment's OrganizationId (or no check is configured). One read per process: the answer is
    /// shared by concurrent callers and cached unless the read faulted (a fault throws to this caller and the next call asks again).
    /// </summary>
    private Task<bool> EnvironmentConfirmedAsync(CancellationToken ct)
    {
        if (_organizationId is null)
        {
            return Task.FromResult(true);
        }

        Task<bool> check;
        lock (_environmentGate)
        {
            if (_environmentCheck is null || _environmentCheck.IsFaulted || _environmentCheck.IsCanceled)
            {
                _environmentCheck = CheckEnvironmentAsync(_organizationId);
            }

            check = _environmentCheck;
        }

        return check.WaitAsync(ct);
    }

    private async Task<bool> CheckEnvironmentAsync(Func<CancellationToken, Task<Guid>> organizationId)
    {
        // Not the caller's token: the answer is shared and cached for the process.
        var organization = await organizationId(CancellationToken.None).ConfigureAwait(false);
        if (organization == TenantId)
        {
            return true;
        }

        _logger.LogError(
            "Finance file store refused: {Key} is {Configured} but this Dataverse environment's OrganizationId is {OrganizationId}; "
            + "every file-store operation refuses until the setting is corrected ({Code})",
            FinanceFileStoreOptions.SectionPath + ":TenantId", TenantId, organization, StoredFileRefusal.NotConfigured);
        return false;
    }

    /// <summary>The OrganizationId of the environment <paramref name="entities"/> is connected to (its one <c>organization</c> row).</summary>
    /// <exception cref="InvalidOperationException">The row did not come back.</exception>
    internal static async Task<Guid> ReadOrganizationIdAsync(IGenericEntityService entities, CancellationToken ct)
    {
        var query = new QueryExpression("organization") { ColumnSet = new ColumnSet("organizationid"), TopCount = 1 };
        var rows = await entities.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        var id = rows.Entities.Count == 1
            ? rows.Entities[0].GetAttributeValue<Guid?>("organizationid") ?? rows.Entities[0].Id
            : Guid.Empty;
        return id != Guid.Empty ? id : throw new InvalidOperationException("The Dataverse organization row could not be read.");
    }

    /// <summary>False only when <c>MalwareScanningEnabled</c> is explicitly <c>false</c>.</summary>
    public bool ScanningEnabled { get; }

    /// <summary>
    /// Stages <paramref name="content"/> under <paramref name="fileId"/> (section 4.2): the length is checked FIRST (a length that
    /// does not fit <c>sprk_sizebytes</c> refuses before any storage call), then the SHA-256 is computed, then one write-once create
    /// (<c>If-None-Match: *</c>) with the metadata. An existing blob with the same SHA-256, length and source (document, communication
    /// and attachment identity, task 094) is reused (a retry); one with different bytes or another source refuses and is left unchanged. A non-seekable stream is buffered to a temporary file first (bounded by the
    /// same limit). The stream is read from its current position.
    /// </summary>
    /// <exception cref="RequestFailedException">A storage fault (the caller retries; nothing is half-written: the create is atomic).</exception>
    public async Task<StageResult> StageAsync(
        StoredFilePurpose purpose, StoredFileParent parent, Guid fileId, Stream content, string contentType, Guid? submissionId,
        CancellationToken ct, IReadOnlyDictionary<string, string>? sourceMetadata = null)
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

        if (!await EnvironmentConfirmedAsync(ct).ConfigureAwait(false))
        {
            return StageResult.Refused(StoredFileRefusal.NotConfigured);
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

            foreach (var (key, value) in sourceMetadata ?? new Dictionary<string, string>())
            {
                // Task 094: the source facts never replace the stage's own keys.
                metadata.TryAdd(key, value);
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
                if (SameSource(existing.Metadata, metadata))
                {
                    return StageResult.Staged(new StagedBlob(container, name, sha256, length, Reused: true));
                }

                // Task 094 (review round 5): the same bytes staged from ANOTHER source (document, communication or eml part) are not
                // this request's retry; reusing them would write a row whose source facts the blob does not carry.
                _logger.LogWarning(
                    "Stored file stage refused: blob {Container}/{BlobName} was staged from another source; left unchanged ({Code})",
                    container, name, StoredFileRefusal.ContentConflict);
                return StageResult.Refused(StoredFileRefusal.ContentConflict);
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

    /// <summary>The source keys a reused stage must carry exactly as the request does (absent on one side and present on the other differs).</summary>
    private static readonly string[] SourceIdentityKeys = [MetadataSourceDocument, MetadataSourceCommunication, MetadataSourceAttachment];

    /// <summary>True when the existing blob's source document, communication and attachment (eml part) identity equal the request's.</summary>
    private static bool SameSource(IReadOnlyDictionary<string, string> existing, IReadOnlyDictionary<string, string> requested) =>
        SourceIdentityKeys.All(key => string.Equals(
            existing.TryGetValue(key, out var had) ? had : null,
            requested.TryGetValue(key, out var wants) ? wants : null,
            StringComparison.Ordinal));

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
        var gateway = await RequireOwnBlobAsync(container, blobPath, ct).ConfigureAwait(false);
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
        var gateway = await RequireOwnBlobAsync(container, blobPath, ct).ConfigureAwait(false);
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
    internal async Task<BlobPropertiesInfo?> GetPropertiesAsync(string container, string blobPath, CancellationToken ct) =>
        await (await RequireOwnBlobAsync(container, blobPath, ct).ConfigureAwait(false)).GetPropertiesAsync(container, blobPath, ct).ConfigureAwait(false);

    /// <summary>
    /// A read stream over the blob pinned to <paramref name="etag"/> (the version whose verdict was read), or <c>null</c> when it is
    /// gone. A blob changed since then fails with <see cref="RequestFailedException"/> status 412.
    /// </summary>
    internal async Task<Stream?> OpenReadAsync(string container, string blobPath, ETag etag, CancellationToken ct) =>
        await (await RequireOwnBlobAsync(container, blobPath, ct).ConfigureAwait(false)).OpenReadAsync(container, blobPath, etag, ct).ConfigureAwait(false);

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

    /// <summary>
    /// Deletes one blob of this tenant (with snapshots); <c>false</c> when it was already gone. With <paramref name="ifMatch"/>
    /// (uac-r2 round 2, K1) the delete applies only to the blob that ETag describes: a blob changed since it was judged throws
    /// <see cref="RequestFailedException"/> 412 and is left unchanged.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="blobPath"/> is not a name of this tenant (nothing is called).</exception>
    public async Task<bool> DeleteAsync(string container, string blobPath, ETag? ifMatch, CancellationToken ct) =>
        await (await RequireOwnBlobAsync(container, blobPath, ct).ConfigureAwait(false))
            .DeleteIfExistsAsync(container, blobPath, ifMatch, ct).ConfigureAwait(false);

    /// <summary>Deletes one blob of this tenant whatever its ETag (the upload's own clean-up of a blob it just staged).</summary>
    /// <exception cref="ArgumentException"><paramref name="blobPath"/> is not a name of this tenant (nothing is called).</exception>
    public Task<bool> DeleteAsync(string container, string blobPath, CancellationToken ct) => DeleteAsync(container, blobPath, ifMatch: null, ct);

    /// <summary>
    /// The gateway, after checking that <paramref name="blobPath"/> is a stored-file name of THIS tenant in one of the three
    /// containers and that the tenant segment is this environment's OrganizationId. Every blob-level read and delete goes through
    /// it, so no caller of this class can reach another tenant's bytes even when it skips the service's own-path check.
    /// </summary>
    /// <exception cref="ArgumentException">Not a stored-file blob of this tenant (nothing is called).</exception>
    /// <exception cref="InvalidOperationException">Not configured, or the segment is not this environment's (nothing is called).</exception>
    private async Task<BlobGateway> RequireOwnBlobAsync(string container, string? blobPath, CancellationToken ct)
    {
        var gateway = RequireGateway();
        if (!StoredFileSchema.Containers.Contains(container) || !StoredFileBlobName.TryParseForTenant(blobPath, TenantId, out _, out _))
        {
            throw new ArgumentException("Not a stored-file blob of this tenant.", nameof(blobPath));
        }

        await EnsureEnvironmentAsync(ct).ConfigureAwait(false);
        return gateway;
    }

    /// <summary>
    /// Every blob of this tenant in the three containers, with its parsed parent and file id and its metadata. Names that do not
    /// parse as this tenant's are skipped (never deleted by the reconcile job) and counted in a debug log.
    /// </summary>
    internal async IAsyncEnumerable<StoredBlobListing> ListAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var gateway = RequireGateway();
        await EnsureEnvironmentAsync(ct).ConfigureAwait(false);
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

                yield return new StoredBlobListing(container, item.Name, parent, fileId, item.CreatedOn, item.LastModified, item.Metadata, item.ETag);
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
    IReadOnlyDictionary<string, string> Metadata,
    ETag? ETag);
