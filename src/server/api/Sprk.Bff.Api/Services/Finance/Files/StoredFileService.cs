using System.Data;
using System.Globalization;
using System.Security.Claims;
using Azure;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Files;

/// <summary>
/// The Finance file store (ebilling-r1 task 090; procedure-and-file-model sections 4.1-4.3, 9.1 steps 2 and 4-6; EB-D-148): the ONE
/// owner of <c>sprk_storedfile</c> rows and their blobs. Bytes are staged to Azure Blob under a deterministic id, the row is created
/// Not yet servable, and it is flipped to Servable only after the blob is proven present with the row's SHA-256. The download
/// (<c>StoredFileAccessFilter</c>, then <see cref="JudgeServableAsync"/>) and the job read (<see cref="OpenForProcessingAsync"/>) serve
/// only a Servable row whose path is its own and whose scan verdict is clean.
/// </summary>
/// <remarks>
/// <para><b>Component justification (root CLAUDE.md §11).</b> Existing: <c>SessionFileBlobStore</c> stores AI chat bytes keyed by
/// session under AI retention; <c>sprk_document</c> is SPE document management (ruled out, O-2, O-8). Extension: the blob gateway is
/// shared (moved to <c>Infrastructure/Storage</c>), and FinanceOptions, FinanceModule and the scheduler are extended; a new service is
/// needed because nothing writes business files keyed by a Dataverse parent with a servable status. Cost of doing nothing: invoice
/// PDFs, Procedure sources and export files have nowhere to live once the SPE path retires (092, 150).</para>
/// <para><b>Placement (ADR-052 B2, B3).</b> In the BFF: request-scoped authorization over the caller's token and short app-only
/// writes; no new process. Scoped: per request, like the typed-HttpClient <see cref="CallerRecordAccessProbe"/> (transient) and the
/// request <see cref="IServiceProvider"/> it takes; <see cref="IGenericEntityService"/> is a singleton (GraphModule).</para>
/// <para><b>ADR-013.</b> No Services.Ai type is referenced: step 0 comes through <see cref="FinanceAuthorizationFilter.EvaluateStepZeroAsync"/>.</para>
/// <para><b>Authorization.</b> The download's caller checks (step 0, the caller's own Read on the parent and the source document) are
/// made in the route's endpoint filter (ADR-008), which then asks <see cref="JudgeServableAsync"/>. The caller upload makes its own:
/// step 0 and Write on the parent, before any write.</para>
/// <para><b>Logs carry ids only</b>: file, parent and submission ids, blob names (tenant/table/ids) and codes; never file names.</para>
/// </remarks>
public sealed class StoredFileService
{
    private readonly IGenericEntityService _entities;
    private readonly StoredFileBlobs _blobs;
    private readonly CallerRecordAccessProbe _probe;
    private readonly IRecordOwnershipResolver _owners;
    private readonly IServiceProvider _services;
    private readonly ILogger<StoredFileService> _logger;

    public StoredFileService(
        IGenericEntityService entities,
        StoredFileBlobs blobs,
        CallerRecordAccessProbe probe,
        IRecordOwnershipResolver owners,
        IServiceProvider services,
        ILogger<StoredFileService> logger)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _owners = owners ?? throw new ArgumentNullException(nameof(owners));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =============================================================================================================================
    // Write path: stage → row → flip
    // =============================================================================================================================

    /// <summary>
    /// Stages the bytes of <paramref name="spec"/> (9.1 step 2): a refused spec, a length outside 0..int.MaxValue and different bytes
    /// under an existing id are refusals with nothing written; the same bytes under an existing id are reused (a retry).
    /// </summary>
    /// <exception cref="RequestFailedException">A storage fault.</exception>
    public Task<StageResult> StageAsync(StoredFileRowSpec spec, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(content);
        if (spec.Invalid() is { } reason)
        {
            _logger.LogWarning("Stored file {FileId} refused before staging: {Reason} ({Code})", spec.FileId, reason, StoredFileRefusal.InvalidRequest);
            return Task.FromResult(StageResult.Refused(StoredFileRefusal.InvalidRequest));
        }

        return _blobs.StageAsync(spec.Purpose, spec.Parent, spec.FileId, content, spec.ContentType, spec.SubmissionId, ct);
    }

    /// <summary>
    /// The create of a Stored File row for the caller's own transaction (9.1 step 4; uac-r2 F2-1): status Not yet servable and every
    /// authorization column — purpose, Firm-visible, the parent lookup, the source ids, both SHA-256 values, size, content type and
    /// path — with the owner written through <paramref name="owner"/> and the creator person stamped. Nothing is sent.
    /// </summary>
    /// <param name="spec">The row's facts; must be valid.</param>
    /// <param name="blob">The stage's answer for this spec (its path must be the spec's own).</param>
    /// <param name="owner">The resolved owner (never a refusal).</param>
    /// <param name="creatorPersonId">The person who asked (<c>sprk_createdbyperson</c>); <c>null</c> when a job acts for nobody.</param>
    /// <exception cref="ArgumentException">An invalid spec, a blob that is not the spec's, a size that does not fit, or a refused owner.</exception>
    public CreateRequest BuildRow(StoredFileRowSpec spec, StagedBlob blob, RecordOwnerResolution owner, Guid? creatorPersonId)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(blob);
        ArgumentNullException.ThrowIfNull(owner);
        if (spec.Invalid() is { } reason)
        {
            throw new ArgumentException($"Invalid Stored File spec: {reason}.", nameof(spec));
        }

        if (owner.IsRefused)
        {
            throw new ArgumentException($"A refused owner ({owner.RefusalCode}) cannot own a Stored File.", nameof(owner));
        }

        if (!string.Equals(blob.Container, StoredFileSchema.ContainerOf(spec.Purpose), StringComparison.Ordinal)
            || !string.Equals(blob.BlobPath, StoredFileBlobName.Build(_blobs.TenantId, spec.Parent, spec.FileId), StringComparison.Ordinal)
            || !StoredFileRowSpec.IsSha256Hex(blob.Sha256))
        {
            throw new ArgumentException("The staged blob is not this spec's.", nameof(blob));
        }

        // 088 retype: the size is written through the CHECKED conversion; the stage already refused a length that does not fit.
        if (!StoredFileSize.TryToColumnValue(blob.SizeBytes, out var sizeBytes))
        {
            throw new ArgumentOutOfRangeException(nameof(blob), blob.SizeBytes, $"A stored file is at most {StoredFileSize.MaxBytes} bytes.");
        }

        var row = new Entity(StoredFileSchema.Entity, spec.FileId)
        {
            [StoredFileSchema.Name] = spec.Name.Length <= StoredFileSchema.NameMaxLength ? spec.Name : spec.Name[..StoredFileSchema.NameMaxLength],
            [StoredFileSchema.Purpose] = new OptionSetValue((int)spec.Purpose),
            [StoredFileSchema.FirmVisible] = spec.FirmVisible,
            [StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.NotYetServable),
            [spec.Parent.Table] = new EntityReference(spec.Parent.Table, spec.Parent.Id),
            [StoredFileSchema.Sha256] = blob.Sha256,
            [StoredFileSchema.ContentType] = spec.ContentType,
            [StoredFileSchema.SizeBytes] = sizeBytes,
            [StoredFileSchema.BlobPath] = blob.BlobPath,
        };

        if (spec.SourceDocumentId is { } document)
            row[StoredFileSchema.SourceDocument] = new EntityReference(StoredFileSchema.DocumentTable, document);
        if (spec.SourceCommunicationId is { } communication)
            row[StoredFileSchema.SourceCommunication] = new EntityReference(StoredFileSchema.CommunicationTable, communication);
        if (spec.SourceAttachmentIdentity is { } identity)
            row[StoredFileSchema.SourceAttachmentIdentity] = identity;
        if (spec.SourceVersion is { } version)
            row[StoredFileSchema.SourceVersion] = StoredFileSourceVersion.ToCanonical(version);
        if (spec.SourceSha256 is { } sourceSha)
            row[StoredFileSchema.SourceSha256] = sourceSha;

        owner.ApplyTo(row);
        if (creatorPersonId is { } person && person != Guid.Empty)
        {
            // sprk_storedfile carries sprk_createdbyperson (088) but is not yet in RecordCreatorPerson.StampedChildTables (086 adds
            // it), so ApplyTo does not stamp it; stamp it here. Once 086 lists the table both write the same column.
            RecordCreatorPerson.Stamp(row, person);
        }

        return new CreateRequest { Target = row };
    }

    /// <summary>
    /// Flips a Not yet servable row to Servable (9.1 step 5): the row's path must be its own, the blob must exist and its SHA-256,
    /// recomputed by streaming, must equal <c>sprk_sha256</c>; then ONE update of <c>sprk_filestatus</c> with
    /// <c>IfRowVersionMatches</c>. Missing blob, mismatch or a row that is not flippable → <see cref="StoredFileFlipOutcome.Definitive"/>
    /// with no write; a row changed since the read → <see cref="StoredFileFlipOutcome.Concurrency"/>; a fault throws.
    /// </summary>
    public async Task<StoredFileFlipResult> FlipToServableAsync(Guid fileId, CancellationToken ct)
    {
        var row = await ReadRowAsync(fileId, ct).ConfigureAwait(false);
        if (row is null)
        {
            return Definitive(fileId, StoredFileRefusal.RowNotFlippable);
        }

        if (row.Status == StoredFileStatus.Servable)
        {
            return new StoredFileFlipResult(StoredFileFlipOutcome.AlreadyServable);
        }

        if (row.Status != StoredFileStatus.NotYetServable || !PathIsOwn(row) || row.Sha256 is null)
        {
            return Definitive(fileId, StoredFileRefusal.RowNotFlippable);
        }

        var rowVersion = row.RowVersion ?? throw new InvalidOperationException($"Stored file {fileId} was read without a row version.");
        var verification = await _blobs.VerifyAsync(row.Container!, row.BlobPath!, row.Sha256, ct).ConfigureAwait(false);
        if (verification == StoredFileBlobs.Verification.Missing)
        {
            return Definitive(fileId, StoredFileRefusal.BlobMissing);
        }

        if (verification == StoredFileBlobs.Verification.Mismatch)
        {
            return Definitive(fileId, StoredFileRefusal.HashMismatch);
        }

        try
        {
            await _entities.UpdateIfRowVersionMatchesAsync(
                StoredFileSchema.Entity, fileId, rowVersion,
                new Dictionary<string, object> { [StoredFileSchema.Status] = new OptionSetValue((int)StoredFileStatus.Servable) },
                ct).ConfigureAwait(false);
        }
        catch (DBConcurrencyException)
        {
            _logger.LogInformation("Stored file {FileId} flip skipped: the row changed since it was read ({Code})", fileId, StoredFileRefusal.Concurrency);
            return new StoredFileFlipResult(StoredFileFlipOutcome.Concurrency, StoredFileRefusal.Concurrency);
        }

        return new StoredFileFlipResult(StoredFileFlipOutcome.Flipped);
    }

    /// <summary>
    /// A caller's upload (section 4.3; the HTTP route waits for O-14): step 0 → the caller's own Write on the parent (gate variant) →
    /// owner → stage under <see cref="StoredFileIds.New"/> → row → flip. Refused before any read or write for a caller step 0
    /// refuses; refused before any storage call without Write. A failed row write deletes the staged blob and rethrows. Internal
    /// uploads are never Firm-visible and carry no source.
    /// </summary>
    public async Task<StoredFileCreateResult> UploadForCallerAsync(
        ClaimsPrincipal user, string? callerBearerToken, StoredFilePurpose purpose, StoredFileParent parent, string name,
        string contentType, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(content);

        var stepZero = await FinanceAuthorizationFilter.EvaluateStepZeroAsync(user, _services, _logger, ct).ConfigureAwait(false);
        if (!stepZero.IsInternal)
        {
            _logger.LogWarning("Stored file upload refused at step 0 ({Reason})", stepZero.RefusalReasonCode);
            return StoredFileCreateResult.Refused(StoredFileRefusal.CallerNotInternal);
        }

        var spec = new StoredFileRowSpec
        {
            FileId = StoredFileIds.New(),
            Purpose = purpose,
            Parent = parent,
            Name = name,
            ContentType = contentType,
            FirmVisible = false,
        };
        if (spec.Invalid() is { } reason)
        {
            _logger.LogWarning("Stored file upload refused: {Reason} ({Code})", reason, StoredFileRefusal.InvalidRequest);
            return StoredFileCreateResult.Refused(StoredFileRefusal.InvalidRequest);
        }

        if (!_blobs.IsConfigured)
        {
            return StoredFileCreateResult.Refused(StoredFileRefusal.NotConfigured);
        }

        var rights = await _probe.GetCallerRightsForAuthorizationGateAsync(callerBearerToken, parent.EntitySet, parent.Id, ct).ConfigureAwait(false);
        if (!rights.HasFlag(AccessRights.Write))
        {
            _logger.LogWarning("Stored file upload refused: no Write on {ParentTable} {ParentId} ({Code})",
                parent.Table, parent.Id, StoredFileRefusal.NoWriteOnParent);
            return StoredFileCreateResult.Refused(StoredFileRefusal.NoWriteOnParent);
        }

        return await StageCreateFlipAsync(spec, content, RecordRequester.OfCaller(user), stepZero.SystemUserId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// stage → row → flip for a caller that has ALREADY authorized the work (150's export files): no caller checks here. The owner
    /// is resolved from the parent (content rule); <paramref name="requestedBy"/> is recorded as the creator person when given. A
    /// retry with a deterministic id meets the row and blob of the first attempt and completes it.
    /// </summary>
    public Task<StoredFileCreateResult> CreateForAppAsync(StoredFileRowSpec spec, Stream content, Guid? requestedBy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(content);
        if (spec.Invalid() is { } reason)
        {
            _logger.LogWarning("Stored file {FileId} refused: {Reason} ({Code})", spec.FileId, reason, StoredFileRefusal.InvalidRequest);
            return Task.FromResult(StoredFileCreateResult.Refused(StoredFileRefusal.InvalidRequest));
        }

        return StageCreateFlipAsync(spec, content, RecordRequester.Of(systemUserId: requestedBy), requestedBy, ct);
    }

    private async Task<StoredFileCreateResult> StageCreateFlipAsync(
        StoredFileRowSpec spec, Stream content, RecordRequester? requester, Guid? creatorPersonId, CancellationToken ct)
    {
        var owner = await _owners.ResolveOwnerAsync(
            RecordOwnershipContext.ContentOf(spec.Parent.Table, spec.Parent.Id) with { RequestedBy = requester }, ct).ConfigureAwait(false);
        if (owner.IsRefused)
        {
            _logger.LogWarning("Stored file {FileId} refused: no owner under {ParentTable} {ParentId} ({OwnerCode})",
                spec.FileId, spec.Parent.Table, spec.Parent.Id, owner.RefusalCode);
            return StoredFileCreateResult.Refused(StoredFileRefusal.OwnerUnresolved);
        }

        var staged = await StageAsync(spec, content, ct).ConfigureAwait(false);
        if (staged.Blob is not { } blob)
        {
            return StoredFileCreateResult.Refused(staged.RefusalCode!);
        }

        var create = BuildRow(spec, blob, owner, creatorPersonId);
        try
        {
            await _entities.CreateAsync(create.Target, ct).ConfigureAwait(false);
        }
        catch (Exception createFault) when (createFault is not OperationCanceledException)
        {
            // A retry of a deterministic id meets the row the first attempt wrote: that is not a failure.
            var (known, existing) = await TryReadRowAsync(spec.FileId, ct).ConfigureAwait(false);
            if (existing is not null
                && string.Equals(existing.BlobPath, blob.BlobPath, StringComparison.Ordinal)
                && string.Equals(existing.Sha256, blob.Sha256, StringComparison.Ordinal))
            {
                _logger.LogInformation("Stored file {FileId} row already existed with the same blob; continuing to the flip", spec.FileId);
            }
            else
            {
                // Delete only bytes THIS attempt wrote, and only on a definite "no row": a reused blob may be another attempt's
                // (whose row is still coming), and a faulted re-read may hide a committed row. Anything kept that really is an
                // orphan is removed by the reconcile job after the grace.
                if (!blob.Reused && known && existing is null)
                {
                    await DeleteStagedBlobAsync(blob, spec.FileId).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning(
                        "Stored file {FileId}: the row write failed; the blob {BlobName} is kept (reused={Reused}, rowKnown={Known}) for the reconcile job",
                        spec.FileId, blob.BlobPath, blob.Reused, known);
                }

                throw;
            }
        }

        var flip = await FlipToServableAsync(spec.FileId, ct).ConfigureAwait(false);
        return flip.Outcome is StoredFileFlipOutcome.Flipped or StoredFileFlipOutcome.AlreadyServable
            ? StoredFileCreateResult.Created(spec.FileId)
            : StoredFileCreateResult.Refused(flip.RefusalCode ?? StoredFileRefusal.RowNotFlippable);
    }

    private async Task DeleteStagedBlobAsync(StagedBlob blob, Guid fileId)
    {
        try
        {
            // Not the request's token: the blob must go even when the caller went away (otherwise the reconcile job removes it).
            await _blobs.DeleteAsync(blob.Container, blob.BlobPath, CancellationToken.None).ConfigureAwait(false);
            _logger.LogWarning("Stored file {FileId}: the row write failed, so the staged blob {BlobName} was deleted", fileId, blob.BlobPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stored file {FileId}: the row write failed and deleting the staged blob {BlobName} failed too; the reconcile job will remove it",
                fileId, blob.BlobPath);
        }
    }

    // =============================================================================================================================
    // Read paths
    // =============================================================================================================================

    /// <summary>
    /// Step (5) of the download rule (section 4.3; uac-r2 Q5), for a row whose caller checks (step 0, Read on the parent, the copy
    /// rule) <c>StoredFileAccessFilter</c> has already passed: Servable, path parent equals row parent, scan Clean (or NotScanned when
    /// scanning is off) → serve; Malicious, not Servable, Removed, a path mismatch or a missing blob → 404; Pending → 409; the store
    /// not configured or a storage fault → 503.
    /// </summary>
    internal async Task<StoredFileDownloadDecision> JudgeServableAsync(StoredFileRow row, Guid callerSystemUserId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Parent is not { } parent || row.Status != StoredFileStatus.Servable)
        {
            return StoredFileDownloadDecision.NotFound;
        }

        // Before the path check: an unconfigured store has no tenant, so no path could prove its own (an operator fault, not a 404).
        if (!_blobs.IsConfigured)
        {
            return StoredFileDownloadDecision.Unavailable;
        }

        if (!PathIsOwn(row))
        {
            return StoredFileDownloadDecision.NotFound;
        }

        var fileId = row.FileId;

        (StoredFileScanState State, Infrastructure.Storage.BlobPropertiesInfo Properties)? scan;
        try
        {
            scan = await _blobs.GetScanStateAsync(row.Container!, row.BlobPath!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Stored file {FileId} download: the blob read faulted (503)", fileId);
            return StoredFileDownloadDecision.Unavailable;
        }

        return scan switch
        {
            null => StoredFileDownloadDecision.NotFound,
            { State: StoredFileScanState.Malicious } => LogMalicious(fileId),
            { State: StoredFileScanState.Pending } => StoredFileDownloadDecision.ScanPending,
            { } verdict => StoredFileDownloadDecision.Serve(new StoredFileServable(
                fileId, parent, row.Container!, row.BlobPath!, verdict.Properties.ETag, row.ContentType, callerSystemUserId)),
        };
    }

    /// <summary>
    /// The bytes of a file for a JOB (section 4.3): only when the row is Servable or staged (Not yet servable with its blob present),
    /// the path is its own, the bytes match <c>sprk_sha256</c> and the scan is Clean (NotScanned when scanning is off). Otherwise a
    /// typed <see cref="StoredFileProcessingOutcome.Pending"/>, <see cref="StoredFileProcessingOutcome.Malicious"/> or
    /// <see cref="StoredFileProcessingOutcome.Missing"/>. A fault throws (the job retries).
    /// </summary>
    public async Task<StoredFileProcessingRead> OpenForProcessingAsync(Guid fileId, CancellationToken ct)
    {
        var row = await ReadRowAsync(fileId, ct).ConfigureAwait(false);
        if (row is null || row.Status is not (StoredFileStatus.Servable or StoredFileStatus.NotYetServable) || !PathIsOwn(row) || row.Sha256 is null)
        {
            return new StoredFileProcessingRead(StoredFileProcessingOutcome.Missing);
        }

        var scan = await _blobs.GetScanStateAsync(row.Container!, row.BlobPath!, ct).ConfigureAwait(false);
        switch (scan)
        {
            case null:
                return new StoredFileProcessingRead(StoredFileProcessingOutcome.Missing);
            case { State: StoredFileScanState.Malicious }:
                return new StoredFileProcessingRead(StoredFileProcessingOutcome.Malicious);
            case { State: StoredFileScanState.Pending }:
                return new StoredFileProcessingRead(StoredFileProcessingOutcome.Pending);
        }

        BinaryData? bytes;
        try
        {
            bytes = await _blobs.ReadAllAsync(row.Container!, row.BlobPath!, scan.Value.Properties.ETag, ct).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status412PreconditionFailed)
        {
            // Changed since its verdict was read: the new content has no verdict yet.
            return new StoredFileProcessingRead(StoredFileProcessingOutcome.Pending);
        }

        if (bytes is null)
        {
            return new StoredFileProcessingRead(StoredFileProcessingOutcome.Missing);
        }

        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes.ToMemory().Span));
        if (!string.Equals(sha, row.Sha256, StringComparison.Ordinal))
        {
            _logger.LogWarning("Stored file {FileId}: the blob's bytes do not match the row's SHA-256; not processed ({Code})", fileId, StoredFileRefusal.HashMismatch);
            return new StoredFileProcessingRead(StoredFileProcessingOutcome.Missing);
        }

        return new StoredFileProcessingRead(StoredFileProcessingOutcome.Ok, bytes, row.ContentType);
    }

    /// <summary>Opens the stream of a file <see cref="JudgeServableAsync"/> decided to serve, pinned to the version it judged.</summary>
    internal Task<Stream?> OpenServableAsync(StoredFileServable file, CancellationToken ct) =>
        _blobs.OpenReadAsync(file.Container, file.BlobPath, file.ETag, ct);

    // =============================================================================================================================
    // Row reads
    // =============================================================================================================================

    /// <summary>The row, or <c>null</c> when Dataverse answered not-found. Any other fault throws.</summary>
    internal async Task<StoredFileRow?> ReadRowAsync(Guid fileId, CancellationToken ct)
    {
        if (fileId == Guid.Empty)
        {
            return null;
        }

        Entity entity;
        try
        {
            entity = await _entities.RetrieveAsync(StoredFileSchema.Entity, fileId, StoredFileSchema.ReadColumns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (StoredFileNotFound.Is(ex))
        {
            return null;
        }

        return StoredFileRow.From(entity, _blobs.TenantId);
    }

    /// <summary>The re-read after a failed create: <c>Known</c> is false when the read itself faulted (the row may exist).</summary>
    private async Task<(bool Known, StoredFileRow? Row)> TryReadRowAsync(Guid fileId, CancellationToken ct)
    {
        try
        {
            return (true, await ReadRowAsync(fileId, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Stored file {FileId}: the row could not be re-read after a failed create", fileId);
            return (false, null);
        }
    }

    /// <summary>The row's path parses for this tenant, names the row's own parent and file id, and the row has a container.</summary>
    private bool PathIsOwn(StoredFileRow row) =>
        row.Parent is { } parent
        && row.Container is not null
        && StoredFileBlobName.TryParseForTenant(row.BlobPath, _blobs.TenantId, out var pathParent, out var pathFile)
        && pathParent == parent
        && pathFile == row.FileId;

    private StoredFileFlipResult Definitive(Guid fileId, string code)
    {
        _logger.LogWarning("Stored file {FileId} not flipped: {Code}", fileId, code);
        return new StoredFileFlipResult(StoredFileFlipOutcome.Definitive, code);
    }

    private StoredFileDownloadDecision LogMalicious(Guid fileId)
    {
        _logger.LogWarning("Stored file {FileId} download refused: the scan verdict is Malicious", fileId);
        return StoredFileDownloadDecision.NotFound;
    }
}

/// <summary>A Stored File row as the store's rules read it.</summary>
internal sealed record StoredFileRow
{
    public required Guid FileId { get; init; }

    /// <summary>The purpose, or <c>null</c> for an unknown value.</summary>
    public StoredFilePurpose? Purpose { get; init; }

    public StoredFileStatus? Status { get; init; }

    /// <summary>The ONE parent lookup set, when exactly one is set and it is the purpose's kind; <c>null</c> otherwise.</summary>
    public StoredFileParent? Parent { get; init; }

    /// <summary>True when no parent lookup is set at all (every parent was deleted: RemoveLink).</summary>
    public bool NoParentLookups { get; init; }

    public Guid? SourceDocumentId { get; init; }

    /// <summary>The <c>sprk_sourcecommunication</c> lookup (a copy of an email attachment).</summary>
    public Guid? SourceCommunicationId { get; init; }

    public string? Sha256 { get; init; }

    public string? ContentType { get; init; }

    public string? BlobPath { get; init; }

    /// <summary>The purpose's container, or <c>null</c> for an unknown purpose.</summary>
    public string? Container { get; init; }

    /// <summary>The row version for <c>IfRowVersionMatches</c>.</summary>
    public string? RowVersion { get; init; }

    public DateTime? CreatedOn { get; init; }

    public static StoredFileRow From(Entity entity, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(entity);
        StoredFilePurpose? purpose = entity.GetAttributeValue<OptionSetValue>(StoredFileSchema.Purpose) is { } p && StoredFileSchema.IsKnownPurpose(p.Value)
            ? (StoredFilePurpose)p.Value
            : null;
        StoredFileStatus? status = entity.GetAttributeValue<OptionSetValue>(StoredFileSchema.Status) is { } s && Enum.IsDefined(typeof(StoredFileStatus), s.Value)
            ? (StoredFileStatus)s.Value
            : null;

        var setParents = StoredFileSchema.ParentLookups
            .Select(column => (column, reference: entity.GetAttributeValue<EntityReference>(column)))
            .Where(x => x.reference is not null && x.reference.Id != Guid.Empty)
            .ToList();

        StoredFileParent? parent = null;
        if (purpose is { } knownPurpose && setParents.Count == 1
            && StoredFileSchema.TryKindOfTable(setParents[0].column, out var kind)
            && kind == StoredFileSchema.ParentKindOf(knownPurpose))
        {
            parent = new StoredFileParent(kind, setParents[0].reference!.Id);
        }

        var rowVersion = entity.RowVersion;
        if (string.IsNullOrEmpty(rowVersion) && entity.Attributes.TryGetValue(StoredFileSchema.VersionNumber, out var raw) && raw is long number)
        {
            rowVersion = number.ToString(CultureInfo.InvariantCulture);
        }

        return new StoredFileRow
        {
            FileId = entity.Id,
            Purpose = purpose,
            Status = status,
            Parent = parent,
            NoParentLookups = setParents.Count == 0,
            SourceDocumentId = entity.GetAttributeValue<EntityReference>(StoredFileSchema.SourceDocument)?.Id,
            SourceCommunicationId = entity.GetAttributeValue<EntityReference>(StoredFileSchema.SourceCommunication)?.Id,
            Sha256 = entity.GetAttributeValue<string>(StoredFileSchema.Sha256),
            ContentType = entity.GetAttributeValue<string>(StoredFileSchema.ContentType),
            BlobPath = entity.GetAttributeValue<string>(StoredFileSchema.BlobPath),
            Container = purpose is { } c ? StoredFileSchema.ContainerOf(c) : null,
            RowVersion = rowVersion,
            CreatedOn = entity.GetAttributeValue<DateTime?>(StoredFileSchema.CreatedOn),
        };
    }
}

/// <summary>A file the download rule decided to serve: what the handler opens and what the audit event records.</summary>
public sealed record StoredFileServable(
    Guid FileId, StoredFileParent Parent, string Container, string BlobPath, ETag ETag, string? ContentType, Guid CallerSystemUserId);

/// <summary>The download rule's answer.</summary>
public sealed record StoredFileDownloadDecision
{
    private StoredFileDownloadDecision(StoredFileDownloadOutcome outcome, string? reasonCode = null, StoredFileServable? file = null)
    {
        Outcome = outcome;
        ReasonCode = reasonCode;
        File = file;
    }

    public StoredFileDownloadOutcome Outcome { get; }

    /// <summary>The 403 reason code (step 0 only).</summary>
    public string? ReasonCode { get; }

    /// <summary>The file to serve (on <see cref="StoredFileDownloadOutcome.Serve"/> only).</summary>
    public StoredFileServable? File { get; }

    public static readonly StoredFileDownloadDecision NotFound = new(StoredFileDownloadOutcome.NotFound);
    public static readonly StoredFileDownloadDecision ScanPending = new(StoredFileDownloadOutcome.ScanPending);
    public static readonly StoredFileDownloadDecision Unavailable = new(StoredFileDownloadOutcome.Unavailable);

    public static StoredFileDownloadDecision Forbidden(string reasonCode) => new(StoredFileDownloadOutcome.Forbidden, reasonCode);

    public static StoredFileDownloadDecision Serve(StoredFileServable file) =>
        new(StoredFileDownloadOutcome.Serve, file: file ?? throw new ArgumentNullException(nameof(file)));
}

/// <summary>What the download answers.</summary>
public enum StoredFileDownloadOutcome
{
    /// <summary>200 with the bytes.</summary>
    Serve,

    /// <summary>403: step 0 refused the caller.</summary>
    Forbidden,

    /// <summary>The uniform 404.</summary>
    NotFound,

    /// <summary>409 <c>file_scan_pending</c>.</summary>
    ScanPending,

    /// <summary>503 <c>file_store_unavailable</c>.</summary>
    Unavailable,
}

/// <summary>
/// The download audit event (section 4.3, O-29): ONE structured log event per served file, written before the first byte, with ids
/// only. No Dataverse audit row. Shared with 093's firm route (route kind <c>firm</c>, caller kind <c>contact</c>).
/// </summary>
public static class StoredFileAudit
{
    /// <summary>The event's fixed id.</summary>
    public static readonly EventId Downloaded = new(9090, "StoredFileDownloaded");

    /// <summary>The internal route's kind.</summary>
    public const string RouteInternal = "internal";

    /// <summary>The firm route's kind (093).</summary>
    public const string RouteFirm = "firm";

    /// <summary>A systemuser caller (internal route).</summary>
    public const string CallerSystemUser = "systemuser";

    /// <summary>A contact caller (firm route, 093).</summary>
    public const string CallerContact = "contact";

    /// <summary>Writes the event.</summary>
    public static void LogDownloaded(
        ILogger logger, Guid fileId, string parentTable, Guid parentId, string callerKind, Guid callerId, string routeKind)
    {
        ArgumentNullException.ThrowIfNull(logger);
        logger.LogInformation(
            Downloaded,
            "StoredFileDownloaded file {FileId} parent {ParentTable} {ParentId} caller {CallerKind} {CallerId} route {RouteKind}",
            fileId, parentTable, parentId, callerKind, callerId, routeKind);
    }
}
