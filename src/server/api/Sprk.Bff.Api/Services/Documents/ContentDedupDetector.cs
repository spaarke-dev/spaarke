using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services;

namespace Sprk.Bff.Api.Services.Documents;

/// <summary>
/// Outcome of a content-dedup reconciliation (FR-C3 Tier-1). <see cref="CanonicalHash"/> is the SPE
/// <c>quickXorHash</c> when it could be read (null when unavailable → no dedup possible). When
/// <see cref="IsDuplicate"/> is true, <see cref="CanonicalDocumentId"/> is the existing <c>sprk_document</c>
/// the content already lives in — the caller MUST NOT create a second canonical document; when false, the
/// caller creates the document and stamps <see cref="CanonicalHash"/> so future uploads dedup against it.
/// </summary>
public sealed record DedupDecision(string? CanonicalHash, bool IsDuplicate, Guid? CanonicalDocumentId)
{
    /// <summary>No dedup could be performed (hash unavailable or lookup failed) — the caller proceeds normally.</summary>
    public static readonly DedupDecision NoDedup = new(null, false, null);
}

/// <summary>
/// SPE content de-duplication detector (FR-C3 Tier-1, exact <c>quickXorHash</c> equality). Absorbs the
/// <c>sdap-file-duplication-detector-r1</c> design. On upload (gate-AFTER-write, owner 2026-08-05) it reads the
/// persisted item's <c>quickXorHash</c> via the <see cref="SpeFileStore"/> facade (ADR-007 — no direct Graph in
/// callers), reconciles it against the indexed <c>sprk_document.sprk_canonicalhash</c> cross-container authority
/// (task 023), and on a byte-identical hit NOTIFIES the user (never silent) and reports the canonical document
/// so the caller suppresses the second one. A brief transient duplicate SPE blob is accepted (gate-after-write).
/// </summary>
/// <remarks>
/// <para>Tier-1 (exact-equality) ONLY — Tier-2 near-dup (<c>documentVector3072</c>) is deferred OUT of R2.</para>
/// <para>Best-effort / non-fatal (NFR-04): any hashing / lookup / notification failure logs + degrades to
/// <see cref="DedupDecision.NoDedup"/> (upload proceeds; no document is erroneously blocked). Never throws out of
/// the upload path. Registered as a concrete scoped service (ADR-010); <see cref="ReconcileAsync"/> is
/// <c>virtual</c> so callers' hooks are substitutable at the module boundary in tests (codebase idiom, cf.
/// <see cref="SpeFileStore.UploadSmallAsync"/>).</para>
/// </remarks>
public class ContentDedupDetector
{
    private readonly SpeFileStore _speFileStore;
    private readonly IGenericEntityService _genericEntityService;
    private readonly NotificationService _notificationService;
    private readonly ILogger<ContentDedupDetector> _logger;

    public ContentDedupDetector(
        SpeFileStore speFileStore,
        IGenericEntityService genericEntityService,
        NotificationService notificationService,
        ILogger<ContentDedupDetector> logger)
    {
        _speFileStore = speFileStore;
        _genericEntityService = genericEntityService;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Reads the just-uploaded item's <c>quickXorHash</c> and reconciles it against the
    /// <c>sprk_document.sprk_canonicalhash</c> index. Returns a <see cref="DedupDecision"/>: on a hit, the
    /// canonical document id (+ a user notification is emitted); otherwise the hash for the caller to stamp on
    /// the new document. Never throws (NFR-04) — any failure degrades to <see cref="DedupDecision.NoDedup"/>.
    /// </summary>
    /// <param name="driveId">SPE drive id of the persisted item.</param>
    /// <param name="itemId">SPE drive-item id of the persisted item.</param>
    /// <param name="ownerOid">Best-available AAD object-id of the uploader (for the duplicate notification); a
    /// value that does not resolve to a system user degrades to an audit log (still not silent).</param>
    /// <param name="fileName">Display name for the notification / logs.</param>
    public virtual async Task<DedupDecision> ReconcileAsync(
        string driveId, string itemId, string? ownerOid, string? fileName, CancellationToken ct = default)
    {
        // Pure content-identity resolution (hash read + canonical lookup); side-effect-free + non-fatal.
        var (hash, canonicalId) = await ResolveContentIdentityAsync(driveId, itemId, ct);
        if (string.IsNullOrWhiteSpace(hash))
            return DedupDecision.NoDedup;          // no identity → cannot dedup; caller proceeds
        if (canonicalId is null)
            return new DedupDecision(hash, false, null); // first writer (or lookup failed) → caller stamps + creates

        // Byte-identical hit for an IMMUTABLE caller (email-attachment / Assistant persist): NOTIFY (never
        // silent) + report the canonical so the caller SUPPRESSES the second document. An archival copy never
        // diverges, so suppress-forever is correct here (contrast the Compose LINK path — see ComposeService).
        _logger.LogInformation(
            "Content duplicate detected for {FileName}: quickXorHash matches canonical sprk_document {CanonicalId}; suppressing the second document.",
            fileName, canonicalId);
        await NotifyDuplicateAsync(ownerOid, canonicalId.Value, fileName, ct);
        return new DedupDecision(hash, true, canonicalId.Value);
    }

    /// <summary>
    /// Pure content-identity resolution (FR-C3): reads the item's <c>quickXorHash</c> via the
    /// <see cref="SpeFileStore"/> facade (ADR-007) and resolves the current CANONICAL <c>sprk_document</c> for
    /// that content (hash-linked copies excluded — see <see cref="FindCanonicalByHashAsync"/>). NO notification,
    /// NO suppression — the caller decides suppress (immutable, <see cref="ReconcileAsync"/>) vs. link
    /// (editable Compose, <c>ComposeService.PromoteIfEphemeralAsync</c>). Never throws (NFR-04): a hash-read
    /// failure returns <c>(null, null)</c>; a lookup failure returns <c>(hash, null)</c> so the caller still
    /// stamps the known hash. <c>virtual</c> for substitution at the module boundary in tests.
    /// </summary>
    /// <returns><c>Hash</c> = the content identity (null when unavailable); <c>CanonicalId</c> = the existing
    /// canonical <c>sprk_document</c> whose content this matches, or null when this is the first writer.</returns>
    public virtual async Task<(string? Hash, Guid? CanonicalId)> ResolveContentIdentityAsync(
        string driveId, string itemId, CancellationToken ct = default)
    {
        string? hash;
        try
        {
            hash = await _speFileStore.GetQuickXorHashAsync(driveId, itemId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content-dedup hash read failed (non-fatal) for item {ItemId}.", itemId);
            return (null, null);
        }
        if (string.IsNullOrWhiteSpace(hash))
            return (null, null); // no identity → cannot dedup

        try
        {
            var canonicalId = await FindCanonicalByHashAsync(hash, ct);
            return (hash, canonicalId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Content-dedup lookup failed (non-fatal) for item {ItemId}.", itemId);
            return (hash, null); // hash known → caller still stamps it; no suppression
        }
    }

    private async Task<Guid?> FindCanonicalByHashAsync(string hash, CancellationToken ct)
    {
        var query = new QueryExpression("sprk_document")
        {
            ColumnSet = new ColumnSet("sprk_documentid"),
            TopCount = 1,
        };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.Criteria.AddCondition("sprk_canonicalhash", ConditionOperator.Equal, hash);
        // Graduate-on-divergence (FR-C3): a hash-linked COPY (sprk_canonicaldocument set) is NOT a canonical —
        // exclude it so dedup always resolves to the TRUE canonical (never links a third upload to a copy that
        // is about to graduate). A true canonical has sprk_canonicaldocument null.
        query.Criteria.AddCondition("sprk_canonicaldocument", ConditionOperator.Null);
        var result = await _genericEntityService.RetrieveMultipleAsync(query, ct);
        return result.Entities.Count > 0 ? result.Entities[0].Id : null;
    }

    private async Task NotifyDuplicateAsync(string? ownerOid, Guid canonicalId, string? fileName, CancellationToken ct)
    {
        try
        {
            if (!Guid.TryParse(ownerOid, out var oid))
            {
                _logger.LogWarning(
                    "Content duplicate of {FileName} suppressed (canonical {CanonicalId}) but no resolvable uploader oid to notify.",
                    fileName, canonicalId);
                return;
            }
            var systemUserId = await _notificationService.ResolveSystemUserIdAsync(oid, ct);
            if (systemUserId is null)
            {
                _logger.LogWarning(
                    "Content duplicate of {FileName} suppressed (canonical {CanonicalId}) but oid {Oid} did not resolve to a system user.",
                    fileName, canonicalId, oid);
                return;
            }

            await _notificationService.CreateNotificationAsync(
                userId: systemUserId.Value,
                title: "Duplicate document detected",
                body: $"\"{fileName}\" matches the content of an existing document, so it was not filed again. Open the existing document instead.",
                category: "documents",
                regardingId: canonicalId,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Duplicate-detected notification failed (non-fatal) for {FileName}.", fileName);
        }
    }

    /// <summary>
    /// Graduate-on-divergence (FR-C3, Compose LINK path): NOTIFY (never silent) that a just-saved EDITABLE
    /// document is currently byte-identical to an existing canonical and was recorded as a hash-linked copy —
    /// it becomes its own document the moment the user edits it. Distinct from <see cref="NotifyDuplicateAsync"/>
    /// (which announces a SUPPRESSED immutable copy). Best-effort / non-fatal (NFR-04): never throws out of the
    /// save path. <c>virtual</c> so it is substitutable at the module boundary in tests.
    /// </summary>
    public virtual async Task NotifyLinkedCopyAsync(string? ownerOid, Guid canonicalId, string? fileName, CancellationToken ct = default)
    {
        try
        {
            if (!Guid.TryParse(ownerOid, out var oid))
            {
                _logger.LogWarning(
                    "Content of {FileName} is byte-identical to canonical {CanonicalId} (linked copy) but no resolvable uploader oid to notify.",
                    fileName, canonicalId);
                return;
            }
            var systemUserId = await _notificationService.ResolveSystemUserIdAsync(oid, ct);
            if (systemUserId is null)
            {
                _logger.LogWarning(
                    "Content of {FileName} is byte-identical to canonical {CanonicalId} (linked copy) but oid {Oid} did not resolve to a system user.",
                    fileName, canonicalId, oid);
                return;
            }

            await _notificationService.CreateNotificationAsync(
                userId: systemUserId.Value,
                title: "Linked to an existing document",
                body: $"\"{fileName}\" is currently identical to an existing document, so it was linked. It becomes its own document as soon as you edit it.",
                category: "documents",
                regardingId: canonicalId,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Linked-copy notification failed (non-fatal) for {FileName}.", fileName);
        }
    }
}
