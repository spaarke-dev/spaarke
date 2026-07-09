using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Canonical orchestration implementation of <see cref="IComposeService"/> for the Compose
/// drafting workspace. Load/Save/Promote against SPE + Dataverse.
/// </summary>
/// <remarks>
/// <para>
/// Consumes <see cref="ISpeFileOperations"/> for SPE plumbing (Graph OBO Load/Save),
/// <see cref="ChatSessionManager"/> for ChatSession binding, and
/// <see cref="IGenericEntityService"/> for the FR-06 first-Save promotion.
/// </para>
/// <para>
/// FR-06 idempotent promotion: <see cref="PromoteIfEphemeralAsync"/> looks up an existing
/// <c>sprk_document</c> row by SPE drive-item id (alternate key <c>sprk_graphitemid_uk</c>)
/// BEFORE attempting create. If a row is found, the existing id is returned. Concurrent
/// callers resolve via Dataverse alternate-key uniqueness — the second create surfaces as
/// InvalidOperationException, caught + re-resolved via alternate-key lookup.
/// </para>
/// <para>
/// FR-07 ChatSession rebind: on promotion, the session's <c>DocumentId</c> is rebound
/// from the SPE drive-item id to the new <c>sprk_documentid</c> via
/// <see cref="ChatSessionManager.UpdateSessionCacheAsync"/>.
/// </para>
/// </remarks>
public class ComposeService : IComposeService
{
    private const string DocumentLogicalName = "sprk_document";
    private const string DocumentIdAttribute = "sprk_documentid";
    private const string GraphItemIdAttribute = "sprk_graphitemid";
    private const string DisplayNameAttribute = "sprk_documentname";
    private const string FileNameAttribute = "sprk_filename";

    private readonly ISpeFileOperations _spe;
    private readonly ChatSessionManager _sessions;
    private readonly IGenericEntityService _dataverse;
    private readonly DocxAnnotationWriter _annotationWriter;
    private readonly ILogger<ComposeService> _logger;

    public ComposeService(
        ISpeFileOperations spe,
        ChatSessionManager sessions,
        IGenericEntityService dataverse,
        DocxAnnotationWriter annotationWriter,
        ILogger<ComposeService> logger)
    {
        _spe = spe;
        _sessions = sessions;
        _dataverse = dataverse;
        _annotationWriter = annotationWriter;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<UploadComposeDocumentResult> UploadAsync(
        UploadComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "Compose upload routes through the existing Assistant upload pipeline in R1; " +
            "see spec §10.5 Placement Justification. Use LoadAsync with the resulting " +
            "SPE drive-item id. This method is reserved for R2+ inline upload.");
    }

    /// <inheritdoc />
    public async Task<LoadComposeDocumentResult> LoadAsync(
        LoadComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DriveId))
            throw new ArgumentException("DriveId is required for SPE drive-item access.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));

        _logger.LogInformation(
            "Compose load: tenant={TenantId} drive={DriveId} driveItem={DocumentSpeId} record={DocumentRecordId}",
            request.TenantId, request.DriveId, request.DocumentSpeId, request.DocumentRecordId);

        // 1) Fetch metadata (name/size/etag). Missing → NotFound.
        var metadata = await _spe.GetFileMetadataAsUserAsync(httpContext, request.DriveId, request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);
        if (metadata is null)
        {
            throw new InvalidOperationException(
                $"SPE drive-item not found: drive={request.DriveId} item={request.DocumentSpeId}");
        }

        // 2) Fetch content stream. Graph returns non-seekable HttpBaseStream → buffer to
        //    MemoryStream so Length/Seek work for downstream consumers.
        var stream = await _spe.DownloadFileAsUserAsync(httpContext, request.DriveId, request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"SPE drive-item content unavailable: drive={request.DriveId} item={request.DocumentSpeId}");
        }

        ReadOnlyMemory<byte> content;
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream(capacity: (int)Math.Min(metadata.Size ?? 0, int.MaxValue));
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            content = buffer.ToArray();
        }

        // 3) Ensure a ChatSession bound to the document. For Path A (Document row present),
        //    bind to sprk_documentid; for Path B continuation, bind to the SPE drive-item id.
        var bindingId = request.DocumentRecordId.HasValue
            ? request.DocumentRecordId.Value.ToString()
            : request.DocumentSpeId;

        var session = await _sessions.CreateSessionAsync(
                tenantId: request.TenantId,
                documentId: bindingId,
                playbookId: null,
                hostContext: null,
                ct: cancellationToken)
            .ConfigureAwait(false);

        return new LoadComposeDocumentResult
        {
            DocumentSpeId = request.DocumentSpeId,
            DriveId = request.DriveId,
            SessionId = session.SessionId,
            DocumentRecordId = request.DocumentRecordId,
            Content = content,
            ETag = metadata.ETag,
            FileName = metadata.Name,
            Size = metadata.Size,
        };
    }

    /// <inheritdoc />
    public async Task<SaveComposeDocumentResult> SaveAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DriveId))
            throw new ArgumentException("DriveId is required for SPE drive-item access.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("SessionId is required for first-Save promotion rebind.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));

        _logger.LogInformation(
            "Compose save: tenant={TenantId} drive={DriveId} driveItem={DocumentSpeId} session={SessionId} record={DocumentRecordId} size={SizeBytes}",
            request.TenantId, request.DriveId, request.DocumentSpeId, request.SessionId,
            request.DocumentRecordId, request.Content.Length);

        // 1) PUT the new content to the existing drive-item. Commits a new SPE version.
        //    Failure here MUST short-circuit before promotion so a broken save never leaves
        //    a half-promoted Document record.
        using var contentStream = new MemoryStream(request.Content.ToArray(), writable: false);

        var saved = await _spe.ReplaceFileContentAsUserAsync(
                httpContext, request.DriveId, request.DocumentSpeId, contentStream, cancellationToken)
            .ConfigureAwait(false);

        if (saved is null || string.IsNullOrEmpty(saved.Id))
        {
            throw new InvalidOperationException(
                $"SPE save failed: drive-item not found or version not returned. drive={request.DriveId} item={request.DocumentSpeId}");
        }

        // 2) First-Save promotion (FR-06). Idempotent — repeated saves see existing row.
        var promoteRequest = new PromoteComposeDocumentRequest
        {
            DocumentSpeId = request.DocumentSpeId,
            SessionId = request.SessionId,
            TenantId = request.TenantId,
            DisplayName = request.DisplayName,
        };

        var promotion = await PromoteIfEphemeralAsync(promoteRequest, httpContext, cancellationToken)
            .ConfigureAwait(false);

        return new SaveComposeDocumentResult
        {
            DocumentSpeId = request.DocumentSpeId,
            DriveId = request.DriveId,
            SessionId = promotion.SessionId,
            DocumentRecordId = promotion.DocumentRecordId,
            VersionId = saved.Id,
            ETag = saved.ETag,
            Size = saved.Size,
            WasPromotedThisSave = promotion.WasCreated,
        };
    }

    /// <inheritdoc />
    public async Task<PromoteComposeDocumentResult> PromoteIfEphemeralAsync(
        PromoteComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("SessionId is required for the ephemeral→promoted rebind.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));

        // 1) Idempotency check by SPE drive-item id (alt key sprk_graphitemid_uk).
        var existingId = await TryFindDocumentByGraphItemIdAsync(request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);

        if (existingId.HasValue)
        {
            _logger.LogDebug(
                "Compose promote: existing sprk_document {DocumentRecordId} found for driveItem={DocumentSpeId} — idempotent no-op",
                existingId.Value, request.DocumentSpeId);

            await RebindSessionDocumentIdAsync(
                    tenantId: request.TenantId,
                    sessionId: request.SessionId,
                    currentDocumentId: request.DocumentSpeId,
                    newDocumentId: existingId.Value.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);

            return new PromoteComposeDocumentResult
            {
                DocumentSpeId = request.DocumentSpeId,
                SessionId = request.SessionId,
                DocumentRecordId = existingId.Value,
                WasCreated = false,
            };
        }

        // 2) Create the sprk_document row.
        var entity = new Entity(DocumentLogicalName);
        entity[GraphItemIdAttribute] = request.DocumentSpeId;
        var effectiveDisplayName = !string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.DisplayName!
            : $"Compose document ({request.DocumentSpeId})";
        entity[DisplayNameAttribute] = effectiveDisplayName;
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            entity[FileNameAttribute] = request.DisplayName!;
        }

        Guid newId;
        try
        {
            newId = await _dataverse.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Compose promote: created sprk_document {DocumentRecordId} for driveItem={DocumentSpeId}",
                newId, request.DocumentSpeId);
        }
        catch (InvalidOperationException ex)
        {
            // Narrow race — concurrent Save promoted first. Re-resolve.
            _logger.LogWarning(ex,
                "Compose promote: create failed for driveItem={DocumentSpeId} — likely concurrent promotion. Re-resolving via alternate key.",
                request.DocumentSpeId);

            var raceWinnerId = await TryFindDocumentByGraphItemIdAsync(request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false);

            if (!raceWinnerId.HasValue)
            {
                throw;
            }

            newId = raceWinnerId.Value;
        }

        // 3) Rebind the ChatSession DocumentId from SPE id → new sprk_documentid (FR-07).
        await RebindSessionDocumentIdAsync(
                tenantId: request.TenantId,
                sessionId: request.SessionId,
                currentDocumentId: request.DocumentSpeId,
                newDocumentId: newId.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return new PromoteComposeDocumentResult
        {
            DocumentSpeId = request.DocumentSpeId,
            SessionId = request.SessionId,
            DocumentRecordId = newId,
            WasCreated = true,
        };
    }

    /// <inheritdoc />
    public async Task<PushAnnotationsResult> PushAnnotationsAsync(
        PushAnnotationsRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DriveId))
            throw new ArgumentException("DriveId is required for SPE drive-item access.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.IfMatch))
            throw new ArgumentException("IfMatch (load-time ETag) is required — a blind overwrite is not offered on the push-annotations path.", nameof(request));
        if (request.Annotations is null || request.Annotations.Count == 0)
            throw new ArgumentException("At least one annotation is required.", nameof(request));

        _logger.LogInformation(
            "Compose push-annotations: tenant={TenantId} drive={DriveId} driveItem={DocumentSpeId} annotations={AnnotationCount}",
            request.TenantId, request.DriveId, request.DocumentSpeId, request.Annotations.Count);

        // 1) Download the CURRENT bytes via the facade (ADR-007 — no Graph type here).
        var stream = await _spe.DownloadFileAsUserAsync(httpContext, request.DriveId, request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"SPE drive-item not found or unreadable: drive={request.DriveId} item={request.DocumentSpeId}");
        }

        byte[] sourceBytes;
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            sourceBytes = buffer.ToArray();
        }

        // 2) Render annotations into native OOXML markup. Pure — no I/O, no AI (ADR-013).
        //    DocxAnnotationException (malformed / target-not-found) propagates to the endpoint,
        //    which maps it to 400 / 422 ProblemDetails. This runs BEFORE the write, so a bad
        //    annotation batch never leaves a partial SPE version.
        var annotatedBytes = _annotationWriter.Annotate(sourceBytes, request.Annotations);

        // 3) Persist with optimistic concurrency (If-Match). A drive-item that moved under the
        //    caller (Word autosave) surfaces as EtagPreconditionFailedException (412); an open
        //    Word co-authoring session surfaces as DocumentLockedByWordException (423). Both
        //    propagate to the endpoint. Nothing partially writes.
        using var annotatedStream = new MemoryStream(annotatedBytes, writable: false);
        var saved = await _spe.ReplaceFileContentAsUserAsync(
                httpContext, request.DriveId, request.DocumentSpeId, annotatedStream, request.IfMatch, cancellationToken)
            .ConfigureAwait(false);

        if (saved is null || string.IsNullOrEmpty(saved.Id))
        {
            throw new InvalidOperationException(
                $"SPE annotated-write failed: drive-item not found or version not returned. drive={request.DriveId} item={request.DocumentSpeId}");
        }

        return new PushAnnotationsResult
        {
            DocumentSpeId = request.DocumentSpeId,
            DriveId = request.DriveId,
            VersionId = saved.Id,
            ETag = saved.ETag,
            Size = saved.Size,
            AnnotationCount = request.Annotations.Count,
        };
    }

    /// <summary>
    /// FR-07 idempotent rebind of a ChatSession's DocumentId. Handles three cases:
    /// (a) current==new (no-op), (b) session missing (returns null), (c) stored already at
    /// target (no-op), (d) rebind applied via ChatSessionManager's cache-write path.
    /// </summary>
    private async Task<ChatSession?> RebindSessionDocumentIdAsync(
        string tenantId,
        string sessionId,
        string currentDocumentId,
        string newDocumentId,
        CancellationToken ct)
    {
        // (a) Caller asked for a no-op.
        if (string.Equals(currentDocumentId, newDocumentId, StringComparison.Ordinal))
        {
            return await _sessions.GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        }

        var session = await _sessions.GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning(
                "Compose: rebind called for non-existent session {SessionId} (tenant={TenantId})",
                sessionId, tenantId);
            return null;
        }

        // (c) Stored binding already at target.
        if (string.Equals(session.DocumentId, newDocumentId, StringComparison.Ordinal))
        {
            return session;
        }

        // Out-of-order race: caller-asserted currentDocumentId differs from stored.
        // Proceed with new-value-wins semantics but emit a Warning for operator visibility.
        if (!string.IsNullOrWhiteSpace(currentDocumentId) &&
            !string.Equals(session.DocumentId, currentDocumentId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Compose rebind: caller-asserted currentDocumentId ({CallerCurrent}) differs from stored DocumentId ({StoredCurrent}) for session {SessionId} (tenant={TenantId}); proceeding with rebind to {NewDocumentId} (new-value-wins).",
                currentDocumentId, session.DocumentId, sessionId, tenantId, newDocumentId);
        }

        _logger.LogInformation(
            "Compose: rebinding session {SessionId} DocumentId {From} -> {To} (tenant={TenantId})",
            sessionId, session.DocumentId, newDocumentId, tenantId);

        var rebound = session with
        {
            DocumentId = newDocumentId,
            LastActivity = DateTimeOffset.UtcNow,
        };

        await _sessions.UpdateSessionCacheAsync(rebound, ct).ConfigureAwait(false);
        return rebound;
    }

    /// <summary>
    /// Looks up an existing <c>sprk_document</c> row by SPE drive-item id via the
    /// <c>sprk_graphitemid_uk</c> alternate key. Returns the <c>sprk_documentid</c> or
    /// <c>null</c> when no row exists.
    /// </summary>
    private async Task<Guid?> TryFindDocumentByGraphItemIdAsync(
        string driveItemId,
        CancellationToken cancellationToken)
    {
        var key = new KeyAttributeCollection
        {
            { GraphItemIdAttribute, driveItemId },
        };

        try
        {
            var entity = await _dataverse.RetrieveByAlternateKeyAsync(
                DocumentLogicalName,
                key,
                new[] { DocumentIdAttribute },
                cancellationToken).ConfigureAwait(false);

            return entity?.Id;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex,
                "Compose promote alt-key lookup threw InvalidOperationException for driveItem={DocumentSpeId} — treating as not-found",
                driveItemId);
            return null;
        }
    }

    // =========================================================================
    // FR-31 action history — READ-ONLY ledger query (task 061). See design.md §8:
    // the 2026-07-03 draft's `actionLog: ComposeAction[]` stored structure is DELETED —
    // "it IS the session ledger". This adds no new stored surface; it projects the
    // existing ChatSession.Outputs (SessionOutput) + ChatSession.ToolChains
    // (SessionToolChain) ledger collections into a Compose action-history view.
    // =========================================================================

    /// <summary>
    /// FR-31 read-only action-history query: projects Compose's prior actions for a session
    /// directly from the session ledger — <see cref="ChatSession.Outputs"/> (<see cref="SessionOutput"/>
    /// entries, addressable by <c>{bindingId}@t{n}</c>) correlated with
    /// <see cref="ChatSession.ToolChains"/> (<see cref="SessionToolChain"/> entries) for a
    /// best-effort args summary. This is a QUERY over the existing ledger — never a second
    /// stored structure (ADR-040 / FR-31 / design.md §8: the action log IS the session ledger).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Supersession (ADR-040)</b>: within the same <see cref="SessionOutput.BindingId"/>, the
    /// highest-<see cref="SessionOutput.Turn"/> entry is CURRENT; earlier same-binding entries are
    /// SUPERSEDED (<see cref="ComposeActionHistoryEntry.IsSuperseded"/> = <c>true</c>). This
    /// generalizes the compose-disposition undo/replace pattern already established by
    /// <see cref="Ai.PublicContracts.ComposeDisposition.ResolveCurrent"/> (which resolves the
    /// current <c>compose</c>-disposition output for one binding) to EVERY disposition — any
    /// Binding's output can be re-produced within a session (retries, refinements), and this
    /// query always reflects CURRENT ledger state, never a stale copy of it (ADR-040 constraint;
    /// spec FR-31 acceptance criterion 3).
    /// </para>
    /// <para>
    /// <b>Args (best-effort)</b>: <see cref="SessionToolCall.ArgsSummary"/> values are correlated
    /// to an output by matching <see cref="SessionToolChain.Turn"/> to
    /// <see cref="SessionOutput.Turn"/>. This is a best-effort correlation, NOT a guaranteed 1:1
    /// link — the two ledger collections use independently-allocated per-session ordinals (see
    /// <see cref="Ai.OutputRouter"/> remarks on Turn numbering) — so <see cref="ComposeActionHistoryEntry.Args"/>
    /// is <c>null</c> when no ToolChain entry shares the output's turn (e.g. a loop-native output).
    /// </para>
    /// <para>
    /// <b>ADR-013 facade boundary</b>: pure projection over <see cref="ChatSession"/> data already
    /// in hand — no AI executor/routing types, no DI, no I/O. Callers obtain the session via the
    /// existing <see cref="Ai.Chat.ChatSessionManager.GetSessionAsync"/> seam; this method never
    /// reaches into AI internals.
    /// </para>
    /// <para>
    /// <b>ADR-015</b>: no new retention policy — entries live and expire with the session's
    /// existing Tier 3 ledger lifetime (ADR-015 / ADR-040). This method only reads what is
    /// already persisted; it persists nothing itself.
    /// </para>
    /// </remarks>
    /// <param name="session">The Compose session whose ledger to query.</param>
    /// <param name="bindingId">
    /// Optional filter to one Binding's action history. Null (default) returns every binding's
    /// action history recorded in the session.
    /// </param>
    /// <returns>Action-history entries ordered oldest-first by <see cref="SessionOutput.Turn"/>.</returns>
    public static IReadOnlyList<ComposeActionHistoryEntry> GetActionHistory(
        ChatSession session,
        string? bindingId = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        IEnumerable<SessionOutput> outputs = session.Outputs ?? Array.Empty<SessionOutput>();
        if (!string.IsNullOrWhiteSpace(bindingId))
        {
            outputs = outputs.Where(o => string.Equals(o.BindingId, bindingId, StringComparison.Ordinal));
        }

        var materializedOutputs = outputs.ToList();

        // ADR-040 supersession: the highest-Turn entry per BindingId is CURRENT; every
        // earlier same-binding entry is superseded. Computed over ALL outputs for the
        // binding (not just the filtered set) would require the unfiltered collection —
        // but since a bindingId filter already narrows to one binding, computing over
        // materializedOutputs is equivalent whether filtered or not.
        var currentTurnByBinding = materializedOutputs
            .GroupBy(o => o.BindingId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(o => o.Turn), StringComparer.Ordinal);

        var toolChainsByTurn = (session.ToolChains ?? Array.Empty<SessionToolChain>())
            .ToLookup(tc => tc.Turn);

        var entries = new List<ComposeActionHistoryEntry>(materializedOutputs.Count);
        foreach (var output in materializedOutputs)
        {
            var argsSummary = toolChainsByTurn[output.Turn]
                .SelectMany(tc => tc.Calls)
                .Select(c => c.ArgsSummary)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();

            var isCurrent = currentTurnByBinding.TryGetValue(output.BindingId, out var maxTurn)
                && maxTurn == output.Turn;

            entries.Add(new ComposeActionHistoryEntry
            {
                OutputRef = output.Key,
                BindingId = output.BindingId,
                UcId = output.UcId,
                Disposition = output.Disposition,
                Turn = output.Turn,
                Args = argsSummary.Count > 0 ? argsSummary : null,
                CreatedAt = output.CreatedAt,
                IsSuperseded = !isCurrent,
            });
        }

        return entries
            .OrderBy(e => e.Turn)
            .ToList();
    }
}

/// <summary>
/// FR-31 read-only projection of one ledger action (a <see cref="SessionOutput"/> entry,
/// optionally correlated with a <see cref="SessionToolChain"/> entry's args) for Compose's
/// action-history view. This is a QUERY RESULT, never a stored structure — produced by
/// <see cref="ComposeService.GetActionHistory"/>. There is no persisted <c>actionLog</c> or
/// <c>derivedInsight</c> type anywhere in this codebase (design.md §8 / ADR-040) — this record
/// is transient, constructed fresh from the ledger on every call.
/// </summary>
public sealed record ComposeActionHistoryEntry
{
    /// <summary>Addressable ledger key (<c>{bindingId}@t{n}</c>) of the underlying <see cref="SessionOutput"/>.</summary>
    public required string OutputRef { get; init; }

    /// <summary>Binding (<c>sprk_playbookconsumer</c>) id that produced the output.</summary>
    public required string BindingId { get; init; }

    /// <summary>Stable use-case vocabulary id (<see cref="SessionOutput.UcId"/>).</summary>
    public required string UcId { get; init; }

    /// <summary>
    /// Rendering-contract disposition the output was routed under (<c>informational</c> |
    /// <c>work_product</c> | <c>overlay</c> | <c>email</c> | <c>record</c> | <c>notification</c> |
    /// <c>compose</c>) — see <see cref="SessionOutput.Disposition"/>.
    /// </summary>
    public required string Disposition { get; init; }

    /// <summary>1-based session turn (output ordinal) the action was produced on.</summary>
    public required int Turn { get; init; }

    /// <summary>
    /// Best-effort args summary correlated from a <see cref="SessionToolChain"/> entry sharing
    /// the same Turn (see <see cref="ComposeService.GetActionHistory"/> remarks). Null when no
    /// ToolChain entry correlates with this action's turn.
    /// </summary>
    public IReadOnlyList<string>? Args { get; init; }

    /// <summary>UTC timestamp the underlying output was written to the ledger.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// True when a later-turn <see cref="SessionOutput"/> for the SAME <see cref="BindingId"/>
    /// exists in the session ledger — i.e., this action has been superseded (ADR-040 undo/replace
    /// semantics). The highest-turn entry per binding is CURRENT and authoritative
    /// (<c>IsSuperseded == false</c>).
    /// </summary>
    public required bool IsSuperseded { get; init; }
}
