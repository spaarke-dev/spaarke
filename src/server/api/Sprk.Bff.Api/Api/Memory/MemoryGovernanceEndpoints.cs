using System.Security.Claims;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Ai.Audit;
using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Memory;

/// <summary>
/// Minimal memory-governance surface (task AIR2-052, FR-B-03 — RESCOPED to MINIMAL by operator ruling
/// 2026-07-09). Exposes the ADR-015 Tier-3 user controls over the generalized
/// <see cref="IMemoryItemStore"/> (task 050) plus a record-authorization-aligned record-memory read.
///
/// <para><b>Routes</b> (4):</para>
/// <list type="bullet">
///   <item><c>GET    /api/memory/user</c> — review the caller's OWN User-scope memory (what's remembered).</item>
///   <item><c>DELETE /api/memory/user/{itemId}</c> — point-delete one User-scope item (GDPR erasure preserved).</item>
///   <item><c>DELETE /api/memory/user</c> — GDPR Art. 17 erasure of ALL the caller's User-scope memory.</item>
///   <item><c>GET    /api/memory/records/{entityLogicalName}/{id}</c> — read Record-scope memory, gated
///   by the caller's OWN record read access (FR-B-03; no parallel memory ACL).</item>
/// </list>
///
/// <para>
/// <b>Ownership is STRUCTURAL for User scope</b>: the subject partition IS the caller's canonical
/// <c>systemuserid</c> (resolved from the token oid via <see cref="IMemoryAccessAuthorizer"/>), so a
/// caller can only ever list/delete their OWN memory — cross-user access is impossible by construction,
/// no ownership comparison needed.
/// </para>
///
/// <para>
/// <b>Record-read authorization is STRUCTURAL</b>: <see cref="IMemoryAccessAuthorizer.CanCallerReadRecordAsync"/>
/// derives allow/deny from the caller's own Dataverse read access. GRANULARITY: entity-type Read
/// privilege today; true per-row ethical-wall enforcement is DEFERRED to the governance project
/// (see the authorizer docs). Litigation-hold is NOT implemented (deferred 2026-07-08) — no hold code
/// path gates any read or delete here.
/// </para>
///
/// <para>
/// <b>Audit (NFR-07)</b>: every DELETE + ERASE emits a Tier-2 audit event via the EXISTING
/// <see cref="IAuditLogService"/> carrying identifiers/counts ONLY — never memory content. (Memory
/// WRITES are audited at the store chokepoint; see <see cref="MemoryItemStore"/>.)
/// </para>
///
/// <para>
/// <b>INERT envelope fields</b>: <c>sensitivity</c> and <c>deletionPolicy</c> are surfaced in the review
/// DTO for transparency but participate in NO deny path here (operator ruling 2026-07-09 — no
/// enforcement machinery).
/// </para>
///
/// <para><b>Placement</b> (CLAUDE.md §10): consumes memory plumbing + existing authorization only — zero
/// AI-internal types, zero new CRUD→AI dep. <b>Wiring</b> (ADR-010): mapped via
/// <c>EndpointMappingExtensions.MapDomainEndpoints</c>; auth = group-level <c>RequireAuthorization</c> +
/// per-handler claim/subject resolution (mirrors <see cref="PinnedMemoryEndpoints"/>).</para>
/// </summary>
public static class MemoryGovernanceEndpoints
{
    /// <summary>Registers the four memory-governance endpoints. Called from <c>EndpointMappingExtensions.MapDomainEndpoints</c>.</summary>
    public static IEndpointRouteBuilder MapMemoryGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/memory")
            .RequireAuthorization()
            .RequireRateLimiting("ai-context")
            .WithTags("Memory Governance");

        group.MapGet("/user", ListUserMemoryAsync)
            .WithName("ListUserMemory")
            .WithSummary("Review the caller's own User-scope memory (FR-B-03).")
            .WithDescription("Returns every User-scope MemoryItem for the caller (subject = the caller's systemuserid). Read is free (no audit); the response shows what the AI has remembered about the user.")
            .Produces<MemoryListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapDelete("/user/{itemId}", DeleteUserMemoryItemAsync)
            .WithName("DeleteUserMemoryItem")
            .WithSummary("Delete one of the caller's User-scope memory items (GDPR erasure).")
            .WithDescription("Idempotent point-delete. The subject partition is the caller's systemuserid, so only the caller's own memory can be reached. Emits a Tier-2 audit event (identifiers only).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapDelete("/user", EraseUserMemoryAsync)
            .WithName("EraseUserMemory")
            .WithSummary("Erase ALL of the caller's User-scope memory (GDPR Art. 17).")
            .WithDescription("Hard-deletes every User-scope MemoryItem in the caller's subject partition. Emits a Tier-2 audit event (identifiers + count only).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/records/{entityLogicalName}/{id:guid}", ReadRecordMemoryAsync)
            .WithName("ReadRecordMemory")
            .WithSummary("Read a record's memory, gated by the caller's own record read access (FR-B-03).")
            .WithDescription("Returns Record-scope memory for (entityLogicalName, id) IFF the caller has read access to the record's entity. A caller without record read access is denied (403) — no parallel memory ACL.")
            .Produces<MemoryListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    // =========================================================================
    // GET /api/memory/user
    // =========================================================================
    internal static async Task<IResult> ListUserMemoryAsync(
        HttpContext httpContext,
        IMemoryItemStore store,
        IMemoryAccessAuthorizer authorizer,
        ILogger<MemoryListResponse> logger,
        CancellationToken ct)
    {
        if (!TryGetOid(httpContext, out _, out var oidProblem))
        {
            return oidProblem!;
        }

        var subject = await authorizer.ResolveCallerUserSubjectAsync(httpContext.User, ct);
        if (subject is null)
        {
            return NotADataverseUser();
        }

        var items = await store.GetForUserAsync(subject.Value.ToString(), ct);
        logger.LogDebug("[MEMORY-GOV] user list subject={Subject} count={Count}", subject, items.Count);

        return Results.Ok(MemoryListResponse.From(items));
    }

    // =========================================================================
    // DELETE /api/memory/user/{itemId}
    // =========================================================================
    internal static async Task<IResult> DeleteUserMemoryItemAsync(
        string itemId,
        HttpContext httpContext,
        IMemoryItemStore store,
        IMemoryAccessAuthorizer authorizer,
        IAuditLogService auditLog,
        ILogger<MemoryListResponse> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "'itemId' route parameter is required.");
        }

        if (!TryGetTenantAndOid(httpContext, out var tenantId, out var oid, out var problem))
        {
            return problem!;
        }

        var subject = await authorizer.ResolveCallerUserSubjectAsync(httpContext.User, ct);
        if (subject is null)
        {
            return NotADataverseUser();
        }

        var subjectKey = subject.Value.ToString();

        // Structural ownership: the partition IS the caller's subject — only the caller's own item is reachable.
        await store.DeleteItemAsync(subjectKey, itemId, ct);

        await auditLog.LogInteractionAsync(
            MemoryAuditEvents.Build(MemoryAuditEvents.ActionDelete, tenantId, oid, subjectKey, new[] { itemId }), ct);

        logger.LogInformation("[MEMORY-GOV] user delete subject={Subject} itemId={ItemId}", subjectKey, itemId);
        return Results.NoContent();
    }

    // =========================================================================
    // DELETE /api/memory/user  (GDPR erase all)
    // =========================================================================
    internal static async Task<IResult> EraseUserMemoryAsync(
        HttpContext httpContext,
        IMemoryItemStore store,
        IMemoryAccessAuthorizer authorizer,
        IAuditLogService auditLog,
        ILogger<MemoryListResponse> logger,
        CancellationToken ct)
    {
        if (!TryGetTenantAndOid(httpContext, out var tenantId, out var oid, out var problem))
        {
            return problem!;
        }

        var subject = await authorizer.ResolveCallerUserSubjectAsync(httpContext.User, ct);
        if (subject is null)
        {
            return NotADataverseUser();
        }

        var subjectKey = subject.Value.ToString();

        // Capture the affected ids BEFORE erasure so the audit trail records identifiers + count (NFR-07).
        var existing = await store.GetForUserAsync(subjectKey, ct);
        var itemIds = existing.Select(i => i.Id).ToList();

        await store.EraseSubjectAsync(subjectKey, ct);

        await auditLog.LogInteractionAsync(
            MemoryAuditEvents.Build(MemoryAuditEvents.ActionErase, tenantId, oid, subjectKey, itemIds), ct);

        logger.LogInformation("[MEMORY-GOV] user erase subject={Subject} count={Count}", subjectKey, itemIds.Count);
        return Results.NoContent();
    }

    // =========================================================================
    // GET /api/memory/records/{entityLogicalName}/{id}
    // =========================================================================
    internal static async Task<IResult> ReadRecordMemoryAsync(
        string entityLogicalName,
        Guid id,
        HttpContext httpContext,
        IMemoryItemStore store,
        IMemoryAccessAuthorizer authorizer,
        ILogger<MemoryListResponse> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName) || id == Guid.Empty)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request",
                detail: "Both 'entityLogicalName' and a non-empty 'id' are required.");
        }

        if (!TryGetOid(httpContext, out _, out var oidProblem))
        {
            return oidProblem!;
        }

        // STRUCTURAL: read access to record memory derives from the caller's own record read access.
        var allowed = await authorizer.CanCallerReadRecordAsync(httpContext.User, entityLogicalName, id, ct);
        if (!allowed)
        {
            logger.LogWarning("[MEMORY-GOV] record read DENIED entity={Entity} record={RecordId}", entityLogicalName, id);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Caller does not have read access to this record; its memory cannot be read.");
        }

        // Record memory is keyed by (subjectType, subjectId) = (entityLogicalName, recordId) — the
        // Dataverse-native record identity (forward contract; legacy migration-era "matter" subjectType
        // is not exposed via this surface, and no live docs were migrated per the fresh-container ruling).
        var items = await store.GetForRecordAsync(entityLogicalName.Trim().ToLowerInvariant(), id.ToString(), ct);
        logger.LogDebug("[MEMORY-GOV] record read entity={Entity} record={RecordId} count={Count}", entityLogicalName, id, items.Count);

        return Results.Ok(MemoryListResponse.From(items));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static bool TryGetOid(HttpContext httpContext, out string oid, out IResult? problem)
        => TryGetTenantAndOid(httpContext, out _, out oid, out problem);

    private static bool TryGetTenantAndOid(HttpContext httpContext, out string tenantId, out string oid, out IResult? problem)
    {
        tenantId = string.Empty;
        oid = string.Empty;
        problem = null;

        var tid = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        var oidValue = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(oidValue))
        {
            problem = Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity ('oid' claim) not found in authentication token.");
            return false;
        }

        tenantId = tid ?? string.Empty;
        oid = oidValue;
        return true;
    }

    private static IResult NotADataverseUser() => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden,
        title: "Forbidden",
        detail: "Caller is not a Dataverse user in this environment; no memory subject could be resolved.");
}

// =========================================================================
// DTOs — the review contract (the caller sees their own memory content by design)
// =========================================================================

/// <summary>One memory item as surfaced to the review/delete UI. Includes the INERT governance fields for transparency.</summary>
public sealed record MemoryItemDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("subjectType")] string? SubjectType,
    [property: JsonPropertyName("subjectId")] string? SubjectId,
    [property: JsonPropertyName("factType")] string FactType,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("confirmedByUser")] bool ConfirmedByUser,
    [property: JsonPropertyName("sensitivity")] string? Sensitivity,
    [property: JsonPropertyName("retentionClass")] string? RetentionClass,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset? UpdatedAt)
{
    /// <summary>Projects a <see cref="MemoryItem"/> to the review DTO.</summary>
    public static MemoryItemDto From(MemoryItem item) => new(
        Id: item.Id,
        Scope: item.Scope,
        SubjectType: item.SubjectType,
        SubjectId: item.SubjectId,
        FactType: item.Fact.Type.ToString(),
        Key: item.Fact.Key,
        Value: item.Fact.Value,
        Source: item.Source,
        Confidence: item.Fact.Confidence,
        ConfirmedByUser: item.Fact.ConfirmedByUser,
        Sensitivity: item.Sensitivity,
        RetentionClass: item.RetentionClass,
        CreatedAt: item.CreatedAt,
        UpdatedAt: item.UpdatedAt);
}

/// <summary>List response for the review + record-read endpoints.</summary>
public sealed record MemoryListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MemoryItemDto> Items,
    [property: JsonPropertyName("count")] int Count)
{
    /// <summary>Projects a set of <see cref="MemoryItem"/> into the list response.</summary>
    public static MemoryListResponse From(IReadOnlyList<MemoryItem> items)
    {
        var dtos = items.Select(MemoryItemDto.From).ToList();
        return new MemoryListResponse(dtos, dtos.Count);
    }
}
