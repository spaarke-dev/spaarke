// Task 070 (Track D) — cluster 2b, extracted from `ComposeService`.
//
// WHY THIS IS ITS OWN COMPONENT. Every member here answers one question: GIVEN some external
// identifier — a Graph drive-item id, a transient (name + size) key, a session's document id — WHICH
// `sprk_document` row does it mean, and how do we keep that binding correct as the document moves?
// Its reason to change is Dataverse record IDENTITY: the alternate keys we resolve by, the dedup
// signal, and the rebind/graduate rules. That is independent of WHEN a draft is promoted
// (cluster 2a, create-on-save lifecycle) and of how bytes are stored (cluster 3).
//
// ADR-010 — NO NEW DI REGISTRATION. This is an `internal sealed class` collaborator constructed in
// the `ComposeService` constructor from dependencies it ALREADY holds. `Program.cs` and
// `Infrastructure/DI/` are untouched by this extraction, which is verified by an empty `git diff`
// over those paths rather than asserted.
//
// The members below are moved VERBATIM — the only edit is `private` -> `internal` on the five
// declarations so `ComposeService` can still reach them. No logic, guard order, or branch was
// touched; the extraction is a move, and the mutation pass in the seam map is what proves it.

using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Documents;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 2b — Dataverse record RESOLUTION for Compose: which `sprk_document` row an external
/// identifier denotes, and keeping that binding correct as the document changes.
/// </summary>
/// <remarks>
/// Constructed by <see cref="ComposeService"/> from fields it already holds; never DI-registered
/// (ADR-010). <paramref name="logger"/> is taken as the non-generic <see cref="ILogger"/> so this
/// collaborator does not re-open a second log category — every line it writes stays attributed to
/// `ComposeService`, which is what an operator reading the logs expects of an extraction.
/// </remarks>
internal sealed class ComposeRecordResolution
{
    private readonly ChatSessionManager _sessions;
    private readonly IGenericEntityService _dataverse;
    private readonly ILogger _logger;
    private readonly ContentDedupDetector? _dedupDetector;

    internal ComposeRecordResolution(
        ChatSessionManager sessions,
        IGenericEntityService dataverse,
        ILogger logger,
        ContentDedupDetector? dedupDetector)
    {
        _sessions = sessions;
        _dataverse = dataverse;
        _logger = logger;
        _dedupDetector = dedupDetector;
    }

    /// <summary>
    /// FR-07 idempotent rebind of a ChatSession's DocumentId. Handles three cases:
    /// (a) current==new (no-op), (b) session missing (returns null), (c) stored already at
    /// target (no-op), (d) rebind applied via ChatSessionManager's cache-write path.
    /// </summary>
    internal async Task<ChatSession?> RebindSessionDocumentIdAsync(
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
    /// <summary>
    /// FR-C3 graduate-on-divergence (email-communication-intelligence-r2): when a subsequent Compose save
    /// routes through <see cref="PromoteIfEphemeralAsync"/>'s idempotent existing-row branch, check whether the
    /// row is a hash-linked COPY (<c>sprk_canonicaldocument</c> set) whose LIVE content has diverged from the
    /// hash it was linked at (<c>sprk_canonicalhash</c>). If so, sever the link (clear
    /// <c>sprk_canonicaldocument</c> via the <see cref="DBNull"/> clear-sentinel) and stamp the new content hash
    /// — the copy graduates to its own canonical. The row's dedup columns are already in hand from the idempotent
    /// alt-key lookup (no extra retrieve). Best-effort / non-fatal (NFR-04): every failure logs and leaves the
    /// row unchanged (re-evaluated on the next save); never fails the save. No-op when the detector is absent
    /// (bare test ctor), the drive id is unknown, or the row is a true canonical (no link to sever).
    /// </summary>
    internal async Task GraduateLinkedCopyIfDivergedAsync(
        Entity existingRow,
        PromoteComposeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (_dedupDetector is null || string.IsNullOrWhiteSpace(request.GraphDriveId))
            return;

        // Only a hash-linked COPY can graduate — a true canonical has no sprk_canonicaldocument link.
        if (existingRow.GetAttributeValue<EntityReference>(ComposeService.CanonicalDocumentAttribute) is null)
            return;

        try
        {
            var linkedHash = existingRow.GetAttributeValue<string>(ComposeService.CanonicalHashAttribute);
            var (liveHash, _) = await _dedupDetector
                .ResolveContentIdentityAsync(request.GraphDriveId!, request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false);

            // No live hash (unavailable) OR still identical → not diverged; leave the link intact.
            if (string.IsNullOrWhiteSpace(liveHash) || string.Equals(liveHash, linkedHash, StringComparison.Ordinal))
                return;

            await _dataverse.UpdateAsync(
                    ComposeService.DocumentLogicalName,
                    existingRow.Id,
                    new Dictionary<string, object>
                    {
                        [ComposeService.CanonicalDocumentAttribute] = DBNull.Value, // sever the link (DBNull clear-sentinel)
                        [ComposeService.CanonicalHashAttribute] = liveHash!,        // stamp the diverged content's own identity
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Compose content-dedup: sprk_document {DocumentId} diverged from its linked canonical; graduated to its own document.",
                existingRow.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose content-dedup (graduate) failed (non-fatal) for document {DocumentId}; leaving link intact.",
                existingRow.Id);
        }
    }

    internal async Task<Entity?> TryFindDocumentByGraphItemIdAsync(
        string driveItemId,
        CancellationToken cancellationToken)
    {
        var key = new KeyAttributeCollection
        {
            { ComposeService.GraphItemIdAttribute, driveItemId },
        };

        try
        {
            // Fetch the FR-C3 dedup columns alongside the id so the idempotent branch can evaluate
            // graduate-on-divergence WITHOUT a second Dataverse round-trip on the save hot path.
            var entity = await _dataverse.RetrieveByAlternateKeyAsync(
                ComposeService.DocumentLogicalName,
                key,
                new[] { ComposeService.DocumentIdAttribute, ComposeService.CanonicalDocumentAttribute, ComposeService.CanonicalHashAttribute },
                cancellationToken).ConfigureAwait(false);

            return entity;
        }
        catch (InvalidOperationException ex) when (ComposeCreateOnSavePromoter.IsDataverseIdentityKeyFault(ex))
        {
            // #781 item 2 — SELF-HEAL ON TOUCH. The alternate key could not answer, for one of the two
            // reasons the key itself can fail: the value is DUPLICATED across rows ("Found multiple
            // records"), or the unique index never built over that duplicate data and is therefore not
            // Active ("not defined as keys"). Both are the SAME underlying condition seen from two
            // angles, and both used to fall through as "not found" — which sent an EXISTING document
            // into the create branch, whose upsert then failed on the very same key. A user's save 500'd
            // on a document that was sitting right there.
            //
            // A plain column query does not use the alternate key, so it answers in both states. That is
            // the whole trick: resolving here lands the save on the IDEMPOTENT branch, which updates by
            // record id and never touches the alternate key — so touching a duplicated (or key-broken)
            // document heals it instead of failing, and no third row is ever minted.
            //
            // Scope of the heal, stated plainly: this fixes saves of documents that ALREADY have a row.
            // A genuinely NEW document still needs the key, because the create branch's atomic upsert is
            // what closes the FR-07(d) TOCTOU race — that case still surfaces as the honest 409/503 the
            // endpoint maps. Fixing the read is not a licence to weaken the write.
            _logger.LogWarning(ex,
                "Compose promote: sprk_graphitemid_uk could not resolve driveItem={DocumentSpeId} " +
                "(duplicate values, or the unique index is not Active over them). Falling back to a " +
                "column query to self-heal this save. Run scripts/Verify-ComposeIdentityKey.ps1 and the " +
                "retroactive dedup tool — the key needs repairing at the data layer.",
                driveItemId);

            return await ResolveDuplicatedDocumentByGraphItemIdAsync(driveItemId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex,
                "Compose promote alt-key lookup threw InvalidOperationException for driveItem={DocumentSpeId} — treating as not-found",
                driveItemId);
            return null;
        }
    }

    /// <summary>
    /// Number of duplicate rows the self-heal query will look at. Far above any real duplication (the
    /// 2026-08-17 dev incident's worst graphitemid had a handful) and low enough that a pathological
    /// table cannot turn one save into an unbounded read.
    /// </summary>
    private const int MaxDuplicateRowsInspected = 50;

    /// <summary>
    /// #781 item 2: resolves a <c>sprk_graphitemid</c> to a single canonical <c>sprk_document</c> row by
    /// COLUMN QUERY, for the case where the <c>sprk_graphitemid_uk</c> alternate key cannot answer.
    /// Returns <c>null</c> only when no row carries the value at all (a genuine first save).
    /// </summary>
    /// <remarks>
    /// <para><b>The canonical rule is: active before inactive, then OLDEST <c>createdon</c>, then lowest
    /// <c>sprk_documentid</c>.</b> Every term is there for a reason:</para>
    /// <list type="bullet">
    /// <item><description><b>Active first</b> — a deactivated row must never win over a live one; matches
    /// the <c>statecode = 0</c> convention used elsewhere on this table (`ContentDedupDetector`,
    /// `AttachmentDocumentAssociationRung`). It RANKS rather than FILTERS, so an all-inactive duplicate
    /// set still resolves to something instead of returning "not found" and minting a third row — the
    /// one outcome this method exists to prevent.</description></item>
    /// <item><description><b>Oldest createdon</b>, deliberately NOT the issue's suggested "newest
    /// modifiedon". `modifiedon` moves every time a row is touched, so two concurrent saves could pick
    /// DIFFERENT canonicals and diverge — reintroducing exactly the split-brain the unique key exists to
    /// prevent. `createdon` never changes, so the choice is stable across callers and across time. The
    /// oldest row is also the one most likely to carry the accumulated associations (matter links,
    /// regarding, activity history) that downstream records already point at.</description></item>
    /// <item><description><b>Record id last</b> — a total order, so the rule stays deterministic even for
    /// rows created inside the same millisecond. "Deterministic" is the load-bearing property here; any
    /// rule that can return different answers to two callers is not a fix.</description></item>
    /// </list>
    /// <para><b>Nothing is deleted here.</b> The losing rows are logged, not removed: this runs on a
    /// user's save, and quietly deleting rows that may carry associations is not a side effect a save is
    /// allowed to have. Cleanup is a deliberate, reviewable admin operation (#781 item 3).</para>
    /// </remarks>
    private async Task<Entity?> ResolveDuplicatedDocumentByGraphItemIdAsync(
        string driveItemId,
        CancellationToken cancellationToken)
    {
        try
        {
            var query = new QueryExpression(ComposeService.DocumentLogicalName)
            {
                // Same columns the alt-key path returns (the idempotent branch reads the FR-C3 dedup
                // columns off this entity), plus the two ranking columns.
                ColumnSet = new ColumnSet(
                    ComposeService.DocumentIdAttribute,
                    ComposeService.CanonicalDocumentAttribute,
                    ComposeService.CanonicalHashAttribute,
                    "statecode",
                    "createdon"),
                TopCount = MaxDuplicateRowsInspected,
            };
            query.Criteria.AddCondition(ComposeService.GraphItemIdAttribute, ConditionOperator.Equal, driveItemId);

            var found = await _dataverse.RetrieveMultipleAsync(query, cancellationToken).ConfigureAwait(false);
            var rows = found?.Entities;
            if (rows is null || rows.Count == 0)
            {
                // The key was broken, but this drive item genuinely has no row — a real first save.
                // Returning null sends it to the create branch, which is correct.
                _logger.LogInformation(
                    "Compose promote: no sprk_document carries driveItem={DocumentSpeId}; treating as a first save.",
                    driveItemId);
                return null;
            }

            var canonical = rows
                .OrderBy(e => e.GetAttributeValue<OptionSetValue>("statecode")?.Value == 0 ? 0 : 1)
                .ThenBy(e => e.GetAttributeValue<DateTime?>("createdon") ?? DateTime.MaxValue)
                .ThenBy(e => e.Id)
                .First();

            if (rows.Count > 1)
            {
                _logger.LogWarning(
                    "Compose promote: driveItem={DocumentSpeId} resolves to {DuplicateCount} sprk_document rows. " +
                    "Selected canonical {CanonicalId} (active-first, then oldest createdon, then lowest id); " +
                    "the other rows are LEFT IN PLACE and are not written to: {OtherIds}. " +
                    "They must be reconciled by the retroactive dedup tool before sprk_graphitemid_uk can build.",
                    driveItemId,
                    rows.Count,
                    canonical.Id,
                    string.Join(", ", rows.Where(e => e.Id != canonical.Id).Select(e => e.Id)));
            }

            return canonical;
        }
        catch (Exception ex)
        {
            // The heal is best-effort: if the fallback query ALSO fails, report not-found exactly as the
            // pre-#781 code did. That leaves the save on its previous path (create branch → upsert →
            // honest 409/503) rather than converting one fault into a different, less recognisable one.
            _logger.LogWarning(ex,
                "Compose promote: self-heal column query failed for driveItem={DocumentSpeId}; " +
                "falling back to the pre-existing not-found behaviour.",
                driveItemId);
            return null;
        }
    }

    /// <summary>
    /// G7 (FR-06, task 022): the resolved dedup identity for a transient key — the <c>sprk_document</c> row id
    /// plus the SPE pointer (<c>sprk_graphitemid</c> + <c>sprk_graphdriveid</c>) needed to REPLACE its content
    /// in place instead of minting a duplicate. <see cref="SpeId"/>/<see cref="DriveId"/> are <c>null</c> only
    /// for a row that somehow carries a transient key but no SPE pointer (a G7-created row always has both) —
    /// the caller then falls back to minting.
    /// </summary>
    internal sealed record TransientKeyMatch(Guid RecordId, string? SpeId, string? DriveId);

    /// <summary>
    /// G7 (FR-06, task 022): looks up an existing <c>sprk_document</c> row by the client-minted transient key
    /// via the <c>sprk_composetransientkey_uk</c> alternate key, returning its id + SPE pointer (so the caller
    /// can replace in place). Returns <c>null</c> when no row carries the key (the first save of this draft,
    /// or a Save-New fork). Resolves by KEY, never by content (I-7/NFR-02). Mirrors
    /// <see cref="TryFindDocumentByGraphItemIdAsync"/> exactly (same best-effort not-found on a thrown
    /// InvalidOperationException).
    /// </summary>
    internal async Task<TransientKeyMatch?> TryFindDocumentByTransientKeyAsync(
        string transientKey,
        CancellationToken cancellationToken)
    {
        var key = new KeyAttributeCollection
        {
            { ComposeService.ComposeTransientKeyAttribute, transientKey },
        };

        try
        {
            var entity = await _dataverse.RetrieveByAlternateKeyAsync(
                ComposeService.DocumentLogicalName,
                key,
                new[] { ComposeService.DocumentIdAttribute, ComposeService.GraphItemIdAttribute, ComposeService.GraphDriveIdAttribute },
                cancellationToken).ConfigureAwait(false);

            if (entity is null)
            {
                return null;
            }

            var speId = entity.Contains(ComposeService.GraphItemIdAttribute) ? entity[ComposeService.GraphItemIdAttribute] as string : null;
            var driveId = entity.Contains(ComposeService.GraphDriveIdAttribute) ? entity[ComposeService.GraphDriveIdAttribute] as string : null;
            return new TransientKeyMatch(entity.Id, speId, driveId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex,
                "Compose transient-key alt-key lookup threw InvalidOperationException for transientKey — treating as not-found");
            return null;
        }
    }
}
