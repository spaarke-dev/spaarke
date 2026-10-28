using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// <c>GET /api/v1/external-access/record-grants?recordType=&amp;recordId=</c> — the record's active external grants, for
/// Manage Access's Current Access list (unified-access-control-r2 task 099a, amendment 4 / #1010(a)).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> <c>TrackingFieldTrio</c> read <c>sprk_externalrecordaccess</c> through the host's
/// <c>context.webAPI</c>, AS THE USER, so what Current Access showed was decided by Dataverse row depth on the GRANT ROW, not
/// by who may manage the record's access. The grant routes write app-only and never set <c>ownerid</c>, so the rows are
/// owned in the root business unit; Spaarke Basic User and Spaarke Core User read the table at Deep depth, and a
/// Write-holder in any child business unit read zero rows — "No active grants" on a record that had them, so they could
/// re-grant or fail to revoke (verified on dev 2026-10-16: 61 of 62 active rows root-owned). Team ownership (D-11, task
/// 099b) does not close this on its own: a secure record's grants are owned in the Secure Record business unit, and a
/// Write-holder through a share is often in another business unit.</para>
///
/// <para><b>The reader is exactly the dialog's audience.</b> This route is on the <c>/api/v1/external-access</c> management
/// group, so <see cref="DelegationRuleFilter"/> runs first with the SAME target case as <c>/can-manage-access</c>: Write on
/// THIS record and the Share privilege on its table, evaluated as the caller over OBO. A caller who may not manage the
/// record's access never reaches the read and gets the filter's 403; a caller who may sees every grant on it, whatever
/// business unit owns the grant row. The read itself is app-only (<see cref="ExternalGrantLifecycle.QueryActiveRowsForRootAsync"/>,
/// the grant core's own row read) — the gate is the authorization (project CLAUDE.md §2 fact 1).
/// <c>/can-manage-access</c> stays a pure yes/no answer: the rows are this route's, not the gate's.</para>
///
/// <para><b>Lapsed grants are listed and marked.</b> Every <c>statecode = 0</c> row is returned, and
/// <see cref="RecordGrant.IsExpired"/> is the read path's own rule
/// (<see cref="ExternalParticipationService.ConfersAccessOn"/>, UTC today), so the list never shows a lapsed share as live
/// and never hides one the user may want to renew or revoke.</para>
///
/// <para><b>A read fault is a typed 503, never an empty 200</b> (ADR-003, ADR-019). An empty list here means "no grants",
/// and the dialog would say so; a failed read must say that it failed. The ProblemDetails carries a human
/// <c>detail</c>, which the dialog shows verbatim, and a stable <c>reasonCode</c>.</para>
///
/// <para>ADR-001 Minimal API · ADR-008 authorization by the group's endpoint filter · ADR-019 refusals are ProblemDetails
/// with a stable reason code and the trace id.</para>
/// </remarks>
public static class RecordGrantsEndpoint
{
    /// <summary>
    /// The request named no record this handler can resolve. Unreachable through the route (the filter resolves the same
    /// root first and denies an unresolvable one with 403); checked anyway, on the sibling routes' discipline.
    /// </summary>
    internal const string RecordUnresolvedReasonCode = "sdap.access.record_grants.record_unresolved";

    /// <summary>The record's grants could not be read (503).</summary>
    internal const string ReadFailedReasonCode = "sdap.access.record_grants.read_failed";

    /// <summary>The 503's user-facing sentence — shown verbatim in Current Access.</summary>
    internal const string ReadFailedDetail =
        "The external access on this record could not be read, so Current Access cannot be shown. " +
        "Close Manage Access and open it again to retry.";

    /// <summary>
    /// The most grant rows one answer lists — the bound <c>/set-record-share-expiry</c> uses for the same rows. More are
    /// reported as <see cref="RecordGrantsResponse.Truncated"/>, never dropped silently.
    /// </summary>
    internal const int MaxGrantsListed = SetRecordShareExpiryEndpoint.MaxSharesPerRecord;

    /// <summary>Registers the route on the external-access management group.</summary>
    public static RouteGroupBuilder MapRecordGrantsEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/record-grants", Handle)
            .WithName("ListRecordExternalGrants")
            .WithSummary("List the active external grants on a record")
            .WithDescription(
                "Returns every active sprk_externalrecordaccess row on the record (contact and organization grants), each " +
                "with its grantee, level, issuer, granted date, expiry and isExpired. Requires Write on the record and the " +
                "Share privilege on its table, evaluated as the caller (the /can-manage-access rule); 403 otherwise. 503 " +
                "with reasonCode sdap.access.record_grants.read_failed when the grants could not be read.")
            .Produces<RecordGrantsResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    /// <summary>Handles <c>GET /api/v1/external-access/record-grants</c>.</summary>
    /// <returns>
    /// 200 with the record's active grants. 400 only for a record the root resolver rejects (unreachable through the
    /// route). 503 when the grants could not be read. Every denial is the group filter's 403 and never reaches here.
    /// </returns>
    internal static async Task<IResult> Handle(
        [AsParameters] RecordGrantsQuery query,
        DataverseWebApiClient dataverseClient,
        TimeProvider timeProvider,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(query.RecordType, query.RecordId);
        if (!root.Ok)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                RecordUnresolvedReasonCode, root.Error!);

        // Reaching this line means DelegationRuleFilter established Write and Share on this record, as the caller.
        List<ExternalGrantRow> rows;
        try
        {
            rows = await ExternalGrantLifecycle.QueryActiveRowsForRootAsync(
                dataverseClient, root.Type, root.Id, top: MaxGrantsListed + 1, ct, forListing: true);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[RECORD-GRANTS] The active grants of {RootType} {RootId} could not be read. TraceId={TraceId}",
                root.Type, root.Id, httpContext.TraceIdentifier);
            return Refused(httpContext, StatusCodes.Status503ServiceUnavailable, "Access not read",
                ReadFailedReasonCode, ReadFailedDetail);
        }

        var truncated = rows.Count > MaxGrantsListed;
        if (truncated)
        {
            logger.LogWarning(
                "[RECORD-GRANTS] {RootType} {RootId} has more than {Max} active grants; listing only that many and " +
                "reporting truncated = true.", root.Type, root.Id, MaxGrantsListed);
        }

        // A row without an id cannot be revoked or keyed; Dataverse always returns the primary key, so this is a malformed
        // answer — logged, not listed as a row the user would try and fail to act on.
        var unaddressable = rows.Count(r => r.Id == Guid.Empty);
        if (unaddressable > 0)
        {
            logger.LogWarning(
                "[RECORD-GRANTS] {Count} active grant row(s) of {RootType} {RootId} came back without an id and are not listed.",
                unaddressable, root.Type, root.Id);
        }

        // Task 193 G2: which of these are PAIRED with an internal user's share (display only — never a refusal).
        var pairings = await ReadDisplayPairingsAsync(dataverseClient, root.Type, root.Id, logger, ct);

        var today = ExternalGrantLifecycle.TodayUtc(timeProvider);
        var grants = rows
            .Where(r => r.Id != Guid.Empty)
            .Take(MaxGrantsListed)
            .Select(r => new RecordGrant(
                AccessRecordId: r.Id,
                ContactId: r.ContactId,
                ContactName: r.ContactName,
                OrganizationId: r.OrganizationId,
                OrganizationName: r.OrganizationName,
                AccessLevel: r.AccessLevel,
                GrantedByName: r.GrantedBySystemUserName,
                GrantedByContactName: r.GrantedByContactName,
                GrantedDate: r.GrantedDate,
                ExpiresDate: r.ExpiresDate,
                IsExpired: !ExternalParticipationService.ConfersAccessOn(r.ExpiresDate, today),
                PairedWith: pairings.TryGetValue(r.Id, out var pairedUser)
                    ? new RecordAccessPairing(pairedUser, RecordAccessPairing.ShareKind)
                    : null))
            .ToList();

        return TypedResults.Ok(new RecordGrantsResponse(root.Id, grants, truncated));
    }

    /// <summary>
    /// Task 193 G2: the record's ACTIVE grant rows that are paired with an internal user (row id → systemuser id), for
    /// Current Access's <c>pairedWith</c> on both lists (<c>/record-grants</c> and <c>/user-shares</c>). Display only, so it
    /// fails QUIET: a missing or unreadable column pairs nothing (logged at Warning) and never fails the list — no access
    /// decision reads it.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, Guid>> ReadDisplayPairingsAsync(
        DataverseWebApiClient dataverseClient, ExternalGrantRootType rootType, Guid rootId, ILogger logger, CancellationToken ct)
    {
        var read = await ExternalGrantLifecycle.ReadPairingsOnRecordAsync(
            dataverseClient, rootType, rootId, pairedWith: null, top: MaxGrantsListed + 1, ct);
        if (read.Status != GrantPairingReadStatus.Read)
        {
            logger.LogWarning(read.Fault,
                "[RECORD-GRANTS] The pairing of the grants on {RootType} {RootId} was not read ({Status}); Current Access shows no " +
                "pairing.", rootType, rootId, read.Status);
            return new Dictionary<Guid, Guid>();
        }

        return read.ByRow
            .Where(kv => kv.Value is { } user && user != Guid.Empty)
            .ToDictionary(kv => kv.Key, kv => kv.Value!.Value);
    }

    private static IResult Refused(HttpContext httpContext, int statusCode, string title, string reasonCode, string detail)
        => Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = httpContext.TraceIdentifier,
                ["reasonCode"] = reasonCode,
            });
}
