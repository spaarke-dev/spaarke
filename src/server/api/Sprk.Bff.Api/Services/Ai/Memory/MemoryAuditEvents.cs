using Sprk.Bff.Api.Services.Ai.Audit;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Builds Tier-2 compliance <see cref="AuditEntry"/> records for memory governance events (task
/// AIR2-052, NFR-07), reusing the EXISTING <see cref="IAuditLogService"/> — no new audit component
/// (operator ruling 2026-07-09).
///
/// <para>
/// <b>NFR-07 invariant (identifiers/counts only)</b>: a memory audit record carries the tenant id,
/// the acting user id, the subject key (userId or entityId), the affected item ids, and a count —
/// and NOTHING ELSE. The memory fact Key/Value, the prompt, and any narrative content are NEVER
/// emitted. This is the single source of truth for that invariant so the store write-path and the
/// endpoint delete/erase paths cannot diverge.
/// </para>
/// </summary>
public static class MemoryAuditEvents
{
    /// <summary>Audit action for a first-capture memory write.</summary>
    public const string ActionWrite = "memory_write";

    /// <summary>Audit action for an upsert-by-key supersession (a repeated capture replacing a prior fact).</summary>
    public const string ActionSupersede = "memory_supersede";

    /// <summary>Audit action for a user-initiated point-delete of one memory item.</summary>
    public const string ActionDelete = "memory_delete";

    /// <summary>Audit action for a GDPR Art. 17 erasure of an entire subject's memory.</summary>
    public const string ActionErase = "memory_erase";

    /// <summary>Fallback user id when the acting principal is a system/background writer (no user context).</summary>
    private const string SystemUser = "system";

    /// <summary>
    /// Builds a memory-governance <see cref="AuditEntry"/> carrying identifiers/counts ONLY (NFR-07).
    /// </summary>
    /// <param name="action">One of the <c>Action*</c> constants.</param>
    /// <param name="tenantId">Tenant id (Cosmos audit partition). Falls back to <see cref="SystemUser"/> when unknown.</param>
    /// <param name="userId">The acting user id (systemuserid / oid / CreatedBy). Falls back to <see cref="SystemUser"/> when null.</param>
    /// <param name="subjectKey">The memory subject partition value — userId (User scope) or entityId (Record scope).</param>
    /// <param name="itemIds">The affected memory item ids (identifiers only). May be empty for a bulk erase.</param>
    /// <param name="count">The number of affected items (defaults to <paramref name="itemIds"/> count).</param>
    public static AuditEntry Build(
        string action,
        string? tenantId,
        string? userId,
        string subjectKey,
        IReadOnlyList<string> itemIds,
        int? count = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        ArgumentNullException.ThrowIfNull(itemIds);

        var affected = count ?? itemIds.Count;

        // ResponseHash is a required field on the AI-interaction audit shape. Memory events have no
        // AI-response text, so we hash a deterministic IDENTIFIER descriptor (action + subject + ids +
        // count) — an integrity marker, never content. This keeps NFR-07 (no content) intact while
        // satisfying the existing AuditEntry contract.
        var descriptor = $"{action}:{subjectKey}:{string.Join(',', itemIds)}:{affected}";

        return new AuditEntry
        {
            TenantId = string.IsNullOrWhiteSpace(tenantId) ? SystemUser : tenantId,
            UserId = string.IsNullOrWhiteSpace(userId) ? SystemUser : userId,
            // SessionId is required; memory governance is not session-bound, so the subject key is the
            // correlation identifier (an id, not content).
            SessionId = subjectKey,
            Action = action,
            // Item ids are ADR-015 Tier-2 "identifiers allowed" — safe to persist for the audit trail.
            DocumentsAccessed = itemIds,
            ResponseHash = AuditHashHelper.HashResponse(descriptor),
            MatterContext = subjectKey,
        };
    }
}
