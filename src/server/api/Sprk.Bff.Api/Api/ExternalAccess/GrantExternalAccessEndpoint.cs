using System.Security.Claims;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/grant
///
/// Grants an external Contact access to a Secure Project by:
///   1. Creating a sprk_externalrecordaccess record in Dataverse.
///   2. Invalidating the contact's participation cache in Redis.
///
/// Broker-only (ADR-028 Amendment A1): external users never authenticate to SPE
/// directly — all external SPE access is app-only via the BFF — so no synthetic
/// SPE container permission is written on grant.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-009: Redis cache invalidation after grant (key: sdap:external:access:{contactId}).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class GrantExternalAccessEndpoint
{
    private const string EntitySet = "sprk_externalrecordaccesses";
    // Resource identifier for ITenantCache (FR-05). Tenant scope is derived from the caller's
    // 'tid' claim. The cached value is a list of active participations per Contact — not an
    // authorization decision.
    private const string ExternalAccessResource = "external-access-grant";
    private const int CacheVersion = 1;

    /// <summary>
    /// Registers the grant endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapGrantExternalAccessEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/grant", GrantAccessAsync)
            .WithName("GrantExternalAccess")
            .WithSummary("Grant external access to a Contact for a Secure Project")
            .WithDescription(
                "Creates a sprk_externalrecordaccess record and invalidates the contact's Redis " +
                "participation cache after granting. External SPE access is app-only (broker-only).")
            .Produces<GrantAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> GrantAccessAsync(
        GrantAccessRequest request,
        DataverseWebApiClient dataverseClient,
        ITenantCache cache,
        HttpContext httpContext,
        ILogger<Program> logger,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (request.ContactId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ContactId is required and must be a valid GUID.");

        // Resolve the polymorphic grant root (project|matter|workassignment) or the legacy ProjectId
        // shorthand. Fail-closed: a missing/unknown root is rejected 400 and NO row is written.
        var root = ResolveGrantRoot(request);
        if (!root.Ok)
            return ProblemDetailsHelper.ValidationError(root.Error!);

        if (!Enum.IsDefined(typeof(ExternalAccessLevel), request.AccessLevel))
            return ProblemDetailsHelper.ValidationError(
                $"AccessLevel must be one of: {string.Join(", ", Enum.GetNames<ExternalAccessLevel>())}.");

        // ── Resolve caller identity for granted-by reference ─────────────────
        var callerSystemUserId = ResolveCallerSystemUserId(httpContext);

        logger.LogInformation(
            "[EXT-GRANT] Granting {AccessLevel} access to Contact {ContactId} for {RootType} {RootId}",
            request.AccessLevel, request.ContactId, root.Type, root.Id);

        // ── Create the access record (Dataverse) + invalidate cache ──────────
        Guid accessRecordId;
        try
        {
            accessRecordId = await CreateGrantAsync(request, root.Type, root.Id, callerSystemUserId, dataverseClient, cache, httpContext, logger, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-GRANT] Failed to create Dataverse access record for Contact {ContactId} / {RootType} {RootId}",
                request.ContactId, root.Type, root.Id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create external access record in Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // Broker-only: no synthetic SPE container membership is granted on the external path.
        return TypedResults.Ok(new GrantAccessResponse(accessRecordId, SpeContainerMembershipGranted: false));
    }

    // =========================================================================
    // Reusable core (shared with the invite-and-grant orchestration, task 029)
    // =========================================================================

    /// <summary>
    /// Creates a <c>sprk_externalrecordaccess</c> grant (grantee = the Contact, audited via
    /// <c>sprk_grantedby</c>) and invalidates the Contact's Redis participation cache. Throws on the
    /// Dataverse create failure; cache invalidation failure is non-fatal. Shared by <c>/grant</c> and
    /// <c>/invite-and-grant</c> (task 029) so both write an identical, audited grant.
    /// </summary>
    internal static async Task<Guid> CreateGrantAsync(
        GrantAccessRequest request,
        ExternalGrantRootType rootType,
        Guid rootId,
        string? callerOid,
        DataverseWebApiClient dataverseClient,
        ITenantCache cache,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        // sprk_grantedby is a systemuser lookup — its target is a Dataverse systemuserid, which is
        // DISTINCT from the caller's Azure AD object id (oid). Resolve the systemuserid from the oid;
        // if the caller has no matching systemuser, omit grantedby (an audit field must never 400 the grant).
        var grantedBySystemUserId = await ResolveGrantedBySystemUserIdAsync(dataverseClient, callerOid, logger, ct);

        var payload = BuildGrantPayload(request, rootType, rootId, grantedBySystemUserId);
        var accessRecordId = await dataverseClient.CreateAsync(EntitySet, payload, ct);

        logger.LogInformation(
            "[EXT-GRANT] Created access record {AccessRecordId} for Contact {ContactId} / {RootType} {RootId}",
            accessRecordId, request.ContactId, rootType, rootId);

        // Invalidate Redis participation cache (non-fatal).
        try
        {
            var tenantId = ExtractTenantId(httpContext);
            if (!string.IsNullOrEmpty(tenantId))
            {
                await cache.RemoveAsync(
                    tenantId, ExternalAccessResource, request.ContactId.ToString(), CacheVersion, ct: ct);
                logger.LogDebug("[EXT-GRANT] Invalidated cache for Contact {ContactId}", request.ContactId);
            }
            else
            {
                logger.LogWarning(
                    "[EXT-GRANT] No tenant claim found — skipping cache invalidation for Contact {ContactId}",
                    request.ContactId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to invalidate Redis cache for Contact {ContactId}. Non-critical.",
                request.ContactId);
        }

        return accessRecordId;
    }

    /// <summary>
    /// Resolves the caller's Azure AD object id (<c>oid</c>) — the input to
    /// <see cref="ResolveGrantedBySystemUserIdAsync"/>, which maps it to the Dataverse systemuserid the
    /// audited <c>sprk_grantedby</c> lookup requires. NOTE: the oid is NOT itself a systemuserid.
    /// </summary>
    internal static string? ResolveCallerSystemUserId(HttpContext httpContext)
        => httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Maps the caller's Azure AD object id (<paramref name="callerOid"/>) to their Dataverse
    /// <c>systemuserid</c> for the audited <c>sprk_grantedby</c> lookup. The systemuserid is DISTINCT
    /// from the AAD oid — binding the raw oid as a systemuserid fails Dataverse validation (400).
    /// Returns <c>null</c> when the oid is absent/unparseable or has no matching active systemuser, in
    /// which case <c>sprk_grantedby</c> is omitted (an audit field must never block the grant).
    /// </summary>
    internal static async Task<string?> ResolveGrantedBySystemUserIdAsync(
        DataverseWebApiClient dataverseClient, string? callerOid, ILogger logger, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(callerOid) || !Guid.TryParse(callerOid, out _))
            return null;

        try
        {
            var rows = await dataverseClient.QueryAsync<SystemUserRow>(
                "systemusers",
                filter: $"azureactivedirectoryobjectid eq {callerOid}",
                select: "systemuserid",
                top: 1,
                cancellationToken: ct);

            return rows.Count > 0 ? rows[0].systemuserid?.ToString() : null;
        }
        catch (Exception ex)
        {
            // Non-fatal: grantedby is audit metadata. Log and omit rather than fail the grant.
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to resolve grantedby systemuser for oid {Oid} — omitting the audit field.",
                callerOid);
            return null;
        }
    }

    private sealed class SystemUserRow
    {
        public Guid? systemuserid { get; set; }
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Result of resolving the polymorphic grant root from a <see cref="GrantAccessRequest"/>.
    /// <c>Ok == false</c> carries a caller-safe validation <see cref="Error"/> (→ 400, no write).
    /// </summary>
    internal readonly record struct GrantRootResolution(bool Ok, ExternalGrantRootType Type, Guid Id, string? Error);

    /// <summary>
    /// Resolves the ONE grant root a request targets. Precedence: an explicit
    /// <c>RecordType</c> + <c>RecordId</c> wins; otherwise the legacy <c>ProjectId</c> shorthand maps to a
    /// project root. Fail-closed (NFR-08): an unknown <c>RecordType</c>, an explicit <c>RecordType</c> with
    /// an empty <c>RecordId</c>, or no root at all (incl. a bare <c>RecordId</c> without <c>RecordType</c>)
    /// returns <c>Ok == false</c> — the caller rejects 400 and writes NO row.
    /// </summary>
    internal static GrantRootResolution ResolveGrantRoot(GrantAccessRequest request)
    {
        // Explicit polymorphic root takes precedence over the legacy shorthand.
        if (!string.IsNullOrWhiteSpace(request.RecordType))
        {
            if (!ExternalGrantRoot.TryParse(request.RecordType, out var type))
                return new GrantRootResolution(false, default, Guid.Empty,
                    "RecordType must be one of: project, matter, workassignment.");

            var explicitId = request.RecordId ?? Guid.Empty;
            if (explicitId == Guid.Empty)
                return new GrantRootResolution(false, default, Guid.Empty,
                    "RecordId is required and must be a valid GUID when RecordType is specified.");

            return new GrantRootResolution(true, type, explicitId, null);
        }

        // Legacy shorthand: a bare ProjectId maps to the project root (back-compat until task 071).
        if (request.ProjectId != Guid.Empty)
            return new GrantRootResolution(true, ExternalGrantRootType.Project, request.ProjectId, null);

        // Fail-closed: no usable root (also covers RecordId supplied without RecordType).
        return new GrantRootResolution(false, default, Guid.Empty,
            "A grant root is required: provide recordType + recordId, or the legacy projectId.");
    }

    /// <summary>
    /// Builds the <c>sprk_externalrecordaccess</c> create payload. Internal (not private) so the test
    /// assembly (<c>InternalsVisibleTo("Sprk.Bff.Api.Tests")</c>) can assert the typed-lookup bind
    /// contract directly — a wrong <c>@odata.bind</c> key silently breaks the grant.
    /// </summary>
    internal static object BuildGrantPayload(
        GrantAccessRequest request, ExternalGrantRootType rootType, Guid rootId, string? grantedBySystemUserId)
    {
        // Bind exactly ONE typed root lookup per record type (never two). A project root binds
        // sprk_projectid@odata.bind — byte-identical to the pre-070 project grant (back-compat).
        var (navigationProperty, entitySet) = ExternalGrantRoot.BindFor(rootType);

        var payload = new Dictionary<string, object?>
        {
            ["sprk_contactid@odata.bind"] = $"/contacts({request.ContactId})",
            [$"{navigationProperty}@odata.bind"] = $"/{entitySet}({rootId})",
            ["sprk_accesslevel"] = (int)request.AccessLevel,
            ["sprk_granteddate"] = DateTime.UtcNow.ToString("o")
        };

        // grantedBySystemUserId is already a resolved Dataverse systemuserid (see
        // ResolveGrantedBySystemUserIdAsync) — NOT the caller's raw AAD oid. Omitted when unresolved.
        if (!string.IsNullOrEmpty(grantedBySystemUserId) &&
            Guid.TryParse(grantedBySystemUserId, out var systemUserId))
        {
            payload["sprk_grantedby@odata.bind"] = $"/systemusers({systemUserId})";
        }

        if (request.ExpiryDate.HasValue)
        {
            // Bug fix (task 070): the grant table's expiry field is sprk_expiresdate (verified live via
            // describe), NOT sprk_expirydate — the prior name would 400 any grant that carries an expiry.
            payload["sprk_expiresdate"] = request.ExpiryDate.Value.ToString("o");
        }

        if (request.AccountId.HasValue)
        {
            payload["sprk_accountid@odata.bind"] = $"/accounts({request.AccountId.Value})";
        }

        return payload;
    }

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the authenticated HttpContext.
    /// Returns null when no claim is present (in which case cache invalidation is skipped).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
        => httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
}
