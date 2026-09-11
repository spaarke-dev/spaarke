using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/set-record-share-expiry
///
/// Sets ONE expiry date on every active external share of a record — contact shares AND organization
/// shares — in a single all-or-nothing write (spec FR-33, task 098). This is the Manage Access toolbar's
/// "Expiration" (owner design 2026-09-10): one date per record that "applies to all sharing", stored on
/// each share's <c>sprk_expiresdate</c> rather than on the record itself (design note §11).
///
/// <para><b>Why one endpoint rather than N calls to /grant.</b> Calling <c>/grant</c> once per share from
/// the client is the non-atomic path: a failure part-way through while SHORTENING leaves some shares at the
/// later date — access that outlives the date the user just set. Here every row is written by ONE
/// <see cref="IGenericEntityService.BulkUpdateAsync"/>, which task 096 made a genuine
/// <c>ExecuteTransactionRequest</c>: every share gets the date, or none does.</para>
///
/// <para><b>Which rows.</b> Every <c>statecode = 0</c> row whose root lookup is this record, selected by ONE
/// server-side filter built from the request's own recordType + recordId. The client never supplies row ids,
/// so a caller with Write on record A cannot reach record B's shares — the task's escalation trigger was
/// evaluated against this and does not fire. A row already past its date is renewed too (owner decision
/// 2026-09-11, "Renew them too"). Revoked (inactive) rows are untouched, and standing-grant access has no
/// row at all, so it is out of scope by design (owner 2026-09-10, design note §12.1).</para>
///
/// <para><b>Authorization</b> is the group-level <see cref="DelegationRuleFilter"/>: Write on THIS record,
/// evaluated as the caller (OBO), before the handler runs. The filter and the handler resolve the target
/// through the same <see cref="ResolveRoot"/>, and this request has no legacy <c>projectId</c>, so the
/// record that was authorized and the rows that are written cannot diverge.</para>
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: authorization by the route group's endpoint filter.
/// ADR-009: the participation DATA cache is invalidated after the write; no decision is cached.
/// </summary>
public static class SetRecordShareExpiryEndpoint
{
    /// <summary>The request named no expiry. Unlike <c>/grant</c> there is nothing to default to: the date IS the request.</summary>
    internal const string ExpiryRequiredReasonCode = "sdap.access.share_expiry.expiry_required";

    /// <summary>The record's shares could not be read, so nothing was changed.</summary>
    internal const string EnumerationFailedReasonCode = "sdap.access.share_expiry.enumeration_failed";

    /// <summary>More shares than one transaction may carry, so nothing was changed.</summary>
    internal const string TooManySharesReasonCode = "sdap.access.share_expiry.too_many_shares";

    /// <summary>The single transaction failed. All-or-nothing: no share was left half-updated.</summary>
    internal const string WriteFailedReasonCode = "sdap.access.share_expiry.write_failed";

    /// <summary>
    /// The most shares one request will update.
    /// </summary>
    /// <remarks>
    /// <para>1,000 is the ceiling this project treats as <c>ExecuteTransactionRequest</c>'s limit (design
    /// note §12.2 — the documented <c>ExecuteMultiple</c> batch size; the transaction page states none of its
    /// own). The bound is also what keeps the read honest: <see cref="DataverseWebApiClient.QueryAsync{T}"/>
    /// reads ONE page and discards <c>@odata.nextLink</c>, so asking for <c>Max + 1</c> turns a silent
    /// truncation — which would leave the unread shares at their OLD, possibly later, date — into a refusal.</para>
    /// <para>Per-record share counts are far below this; it is a guard rail, not a live limit.</para>
    /// </remarks>
    internal const int MaxSharesPerRecord = 1000;

    /// <summary>Registers the route on the external-access management group.</summary>
    public static RouteGroupBuilder MapSetRecordShareExpiryEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/set-record-share-expiry", Handle)
            .WithName("SetRecordShareExpiry")
            .WithSummary("Set one expiry on every active external share of a record")
            .WithDescription(
                "Writes the expiry to every active sprk_externalrecordaccess row of the record (contact and " +
                "organization shares) in one all-or-nothing transaction, then invalidates the affected " +
                "contacts' participation cache. Requires Write on the record.")
            .Produces<SetRecordShareExpiryResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>
    /// Handles POST /api/v1/external-access/set-record-share-expiry.
    /// </summary>
    /// <returns>
    /// 200 with the number of shares now carrying the date (0 when the record has none — the client then keeps
    /// the date as the default for the next share it adds, task 099). 400 when the expiry is missing or before
    /// today. 422 when the record has more shares than one transaction may carry. 500, with a reason code and
    /// a message, when the shares could not be read or the transaction failed.
    /// </returns>
    public static async Task<IResult> Handle(
        SetRecordShareExpiryRequest request,
        DataverseWebApiClient dataverseClient,
        IDataverseService dataverseService,
        ITenantCache cache,
        TimeProvider timeProvider,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        // The delegation filter already resolved this root and would have denied an unresolvable one, so a
        // failure here is not reachable through the route. Checked anyway: the handler must not trust a
        // pipeline it cannot see.
        var root = ResolveRoot(request);
        if (!root.Ok)
            return ProblemDetailsHelper.ValidationError(root.Error!);

        if (request.ExpiryDate is not { } expiry)
            return ExpiryRequired(httpContext);

        // Same rule as /grant (task 097): Date Only, UTC "today", today itself valid.
        var today = ExternalGrantLifecycle.TodayUtc(timeProvider);
        if (GrantExternalAccessEndpoint.ValidateRequestedExpiry(expiry, today, httpContext) is { } pastExpiry)
            return pastExpiry;

        // ── Enumerate: every active share of THIS record, one server-side filter ──
        List<ExternalGrantRow> shares;
        try
        {
            shares = await ExternalGrantLifecycle.QueryActiveRowsForRootAsync(
                dataverseClient, root.Type, root.Id, top: MaxSharesPerRecord + 1, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[SHARE-EXPIRY] Could not read the active shares of {RootType} {RootId}. Nothing was changed.",
                root.Type, root.Id);
            return NotApplied(httpContext, StatusCodes.Status500InternalServerError, EnumerationFailedReasonCode,
                "The shares on this record could not be read, so no expiry was changed. Try again.");
        }

        if (shares.Count > MaxSharesPerRecord)
        {
            logger.LogError(
                "[SHARE-EXPIRY] {RootType} {RootId} has more than {Max} active shares — more than one " +
                "transaction may carry. Refusing rather than updating a subset.",
                root.Type, root.Id, MaxSharesPerRecord);
            return NotApplied(httpContext, StatusCodes.Status422UnprocessableEntity, TooManySharesReasonCode,
                $"This record has more than {MaxSharesPerRecord} active shares, which is more than one change " +
                "can update together. No expiry was changed.");
        }

        // A row without an id cannot be addressed by an update. Skipping it would leave that share at its old
        // — possibly LATER — date while reporting success, so the whole request is refused instead.
        if (shares.Any(s => s.Id == Guid.Empty))
        {
            logger.LogError(
                "[SHARE-EXPIRY] An active share of {RootType} {RootId} came back without an id. Nothing was changed.",
                root.Type, root.Id);
            return NotApplied(httpContext, StatusCodes.Status500InternalServerError, EnumerationFailedReasonCode,
                "One of the shares on this record could not be identified, so no expiry was changed.");
        }

        if (shares.Count == 0)
        {
            logger.LogInformation(
                "[SHARE-EXPIRY] {RootType} {RootId} has no active shares; nothing to update.", root.Type, root.Id);
            return TypedResults.Ok(new SetRecordShareExpiryResponse(UpdatedCount: 0, ExpiresDate: expiry));
        }

        // ── Write: ONE all-or-nothing transaction (task 096) ──────────────────
        var storedValue = ExternalGrantLifecycle.ToSdkDateOnly(expiry);
        var updates = shares
            .Select(s => (s.Id, new Dictionary<string, object> { ["sprk_expiresdate"] = storedValue }))
            .ToList();

        try
        {
            await dataverseService.BulkUpdateAsync(ExternalGrantLifecycle.EntityLogicalName, updates, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[SHARE-EXPIRY] The transaction setting {Count} shares of {RootType} {RootId} to {Expiry} failed.",
                shares.Count, root.Type, root.Id, expiry);

            // Truthful by construction: a transaction whose OUTCOME is unknown (e.g. a timeout after commit)
            // may have applied — so this does not claim "nothing changed", only "never half".
            return NotApplied(httpContext, StatusCodes.Status500InternalServerError, WriteFailedReasonCode,
                $"The expiry could not be confirmed as applied. The change is all-or-nothing: either every share on " +
                $"this record now ends on {expiry:yyyy-MM-dd} or none does. Reload to see which, then retry if needed.");
        }

        logger.LogInformation(
            "[SHARE-EXPIRY] Set {Count} active shares of {RootType} {RootId} to expire {Expiry} " +
            "({Renewed} of them had already lapsed and are renewed).",
            shares.Count, root.Type, root.Id, expiry, shares.Count(s => s.ExpiresDate is { } d && d < today));

        await InvalidateAffectedCachesAsync(shares, dataverseClient, cache, httpContext, logger, ct);

        return TypedResults.Ok(new SetRecordShareExpiryResponse(UpdatedCount: shares.Count, ExpiresDate: expiry));
    }

    /// <summary>
    /// The ONE record this request targets, from its explicit <c>recordType</c> + <c>recordId</c>. Shared with
    /// <see cref="DelegationRuleFilter"/>, so the record that is authorized is the record that is written.
    /// </summary>
    /// <remarks>
    /// Fail-closed: a missing or unknown type, or a missing id, returns <c>Ok == false</c>. There is no legacy
    /// <c>projectId</c> shorthand here, deliberately — a request carrying both could authorize one record and
    /// write another.
    /// </remarks>
    internal static GrantExternalAccessEndpoint.GrantRootResolution ResolveRoot(SetRecordShareExpiryRequest request)
    {
        if (!ExternalGrantRoot.TryParse(request.RecordType, out var type))
            return new GrantExternalAccessEndpoint.GrantRootResolution(false, default, Guid.Empty,
                "RecordType is required and must be one of: project, matter, workassignment.");

        if (request.RecordId is not { } recordId || recordId == Guid.Empty)
            return new GrantExternalAccessEndpoint.GrantRootResolution(false, default, Guid.Empty,
                "RecordId is required and must be a valid GUID.");

        return new GrantExternalAccessEndpoint.GrantRootResolution(true, type, recordId, null);
    }

    /// <summary>
    /// Clears the participation cache of every contact whose access came through one of the updated shares:
    /// the contact on a contact share, and every ACTIVE member of the organization on an organization share.
    /// Non-fatal throughout.
    /// </summary>
    /// <remarks>
    /// <para><b>Freshness, not a security boundary.</b> The cache holds WHICH grants a contact has, not their
    /// dates — expiry is applied by the read query's <c>$filter</c> when an entry is built (task 007). The new
    /// date is today or later, so the grantee is admitted today either way; the only window is the
    /// ≤60-second tail after an expiry midnight, which every grant already has on the read path and which
    /// invalidating at write time cannot shorten. What invalidation does buy is prompt RENEWAL: a lapsed share
    /// this request revives is visible on the next evaluation instead of up to 60 seconds later.</para>
    ///
    /// <para>Organization shares name no contact, so their members are expanded through the existing
    /// <see cref="ExternalOrganizationMembership"/> reader (task 020) rather than a new one. An organization
    /// over its bound, or one whose members cannot be read, is logged and left to the TTL.</para>
    /// </remarks>
    private static async Task InvalidateAffectedCachesAsync(
        IReadOnlyList<ExternalGrantRow> shares,
        DataverseWebApiClient dataverseClient,
        ITenantCache cache,
        HttpContext httpContext,
        ILogger logger,
        CancellationToken ct)
    {
        var tenantId = ExtractTenantId(httpContext);
        if (string.IsNullOrEmpty(tenantId))
        {
            logger.LogWarning(
                "[SHARE-EXPIRY] No tenant claim — skipping cache invalidation; affected contacts refresh within the participation TTL.");
            return;
        }

        var contactIds = new HashSet<Guid>(shares.Where(s => s.ContactId.HasValue).Select(s => s.ContactId!.Value));

        // A row with BOTH a contact and an organization is a person share whose firm is metadata — only a row
        // with NO contact is the organization's own share (ExternalGrantKey's rule).
        var organizationIds = shares
            .Where(s => s.ContactId is null && s.OrganizationId.HasValue)
            .Select(s => s.OrganizationId!.Value)
            .Distinct();

        foreach (var organizationId in organizationIds)
        {
            try
            {
                var members = await ExternalOrganizationMembership.QueryActiveMembersAsync(
                    dataverseClient, organizationId, ct);

                if (members.ExceededBound)
                {
                    logger.LogWarning(
                        "[SHARE-EXPIRY] Organization {OrganizationId} has more than {Bound} active members; " +
                        "their caches refresh within the participation TTL.",
                        organizationId, ExternalOrganizationMembership.MaxMembersPerSweep);
                    continue;
                }

                contactIds.UnionWith(members.ContactIds);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "[SHARE-EXPIRY] Could not read the members of Organization {OrganizationId}; their caches " +
                    "refresh within the participation TTL.", organizationId);
            }
        }

        foreach (var contactId in contactIds)
        {
            try
            {
                await cache.RemoveAsync(
                    tenantId, ExternalParticipationService.ExternalAccessResource, contactId.ToString(),
                    ExternalParticipationService.CacheVersion, ct: ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex,
                    "[SHARE-EXPIRY] Failed to invalidate the participation cache for Contact {ContactId}. Non-critical.",
                    contactId);
            }
        }

        logger.LogDebug("[SHARE-EXPIRY] Invalidated the participation cache for {Count} contact(s).", contactIds.Count);
    }

    private static IResult ExpiryRequired(HttpContext httpContext)
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation Error",
            detail: "ExpiryDate is required: it is the date every share on this record will end.",
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
                ["reasonCode"] = ExpiryRequiredReasonCode,
            });

    /// <summary>The single "not applied" shape: ProblemDetails with a reason code (ADR-003) and the trace id (ADR-019).</summary>
    private static IResult NotApplied(HttpContext httpContext, int statusCode, string reasonCode, string detail)
        => Results.Problem(
            statusCode: statusCode,
            title: "Expiry not applied",
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
                ["reasonCode"] = reasonCode,
            });

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the authenticated HttpContext.
    /// Returns null when no claim is present (in which case cache invalidation is skipped).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
        => httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
}
