using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// The body of <c>POST /api/v1/external-access/assigned-access/sync</c> (task 142): ONLY the record. The subjects are read
/// server-side from the record's own registry columns; any other field a client sends is ignored and never trusted.
/// </summary>
/// <param name="RecordType"><c>project</c> | <c>matter</c> | <c>workassignment</c>.</param>
/// <param name="RecordId">The record.</param>
public sealed record AssignedAccessSyncRequest(string? RecordType, Guid? RecordId);

/// <summary>Query string of <c>GET /api/v1/external-access/assigned-access</c> (task 142): the record.</summary>
public sealed record AssignedAccessListQuery(string? RecordType, Guid? RecordId);

/// <summary>
/// The body of <c>POST /api/v1/external-access/assigned-access/dismiss</c> (task 142, owner A3): the record and the
/// suggestion (ledger entry) to dismiss.
/// </summary>
public sealed record AssignedAccessDismissRequest(string? RecordType, Guid? RecordId, Guid? EntryId);

/// <summary>
/// What <c>/assigned-access/sync</c> did: the Assigned-To materialization, the No Access re-application and the Restricted
/// rule for users flagged external.
/// </summary>
/// <param name="AssignedAccess">The Assigned-To outcome for the record (task 142).</param>
/// <param name="NoAccess">Task 143's record-scoped No Access enforcement (owner R3: "Update Access" also re-applies No
/// Access) — one report per covering entry.</param>
public sealed record AssignedAccessSyncResponse(
    AssignedAccessOutcome AssignedAccess,
    IReadOnlyList<NoAccessEnforcementReport> NoAccess)
{
    /// <summary>
    /// Task 114 (owner round 67): on a Restricted record, the shares of users flagged external that were removed, and
    /// whether that left a secure record with no internal reader or with an external owner. <c>null</c> only from a caller that did not run the rule. Additive: an older
    /// client that does not read it is unaffected.
    /// </summary>
    public RestrictedExternalShareReport? RestrictedExternal { get; init; }
}

/// <summary>The record's live Assigned-To ledger entries (Manage Access suggestions and provenance).</summary>
public sealed record AssignedAccessListResponse(IReadOnlyList<AssignedAccessListEntry> Entries);

/// <summary>The result of a dismiss.</summary>
public sealed record AssignedAccessDismissResponse(Guid EntryId, int Declined);

/// <summary>
/// The Assigned-To routes (unified-access-control-r2 task 142 · owner round 2 item 5 + Q5, round 3 R3 "immediate on save"
/// and "Update Access", round 3 A3 "prompt on Secure"):
/// <list type="bullet">
/// <item><c>POST /assigned-access/sync</c> — called by the MDA form's post-save script
/// (<c>sprk_assignedaccess_postsave.js</c>), by every client writer after its create (Create Matter / Project / Work
/// Assignment wizards) and by the "Update Access" ribbon command. Re-applies the record's No Access entries (task 143's
/// record-scoped enforcer), removes the shares of users flagged external when the record is Restricted (task 114 — the
/// save is where a record BECOMES Restricted) and then materializes its Assigned-To access — ONE call.</item>
/// <item><c>GET /assigned-access</c> — the record's ledger entries for Manage Access: suggestions on a secure record
/// (PendingConfirmation, naming the source field) and the residual read-time access an auto grant's subject keeps.</item>
/// <item><c>POST /assigned-access/dismiss</c> — Dismiss a suggestion: Declined, which no trigger re-creates while the
/// assignment persists.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>Who may call them.</b> They join the <c>/api/v1/external-access</c> group, so <see cref="DelegationRuleFilter"/>
/// gates each on Write on the RECORD, evaluated as the caller over OBO, before the handler runs — through a case for each
/// request type that resolves the record with <see cref="GrantExternalAccessEndpoint.ResolveExplicitRoot"/> (the
/// <c>/share-user</c> case). An unknown record id and a record the caller cannot write are the filter's identical 403
/// (enumeration-safe); the handler never distinguishes them. The ribbon's enable rule is a convenience only.</para>
/// <para><b>What the sync can do.</b> Only what the owner's rule says, from the record's own columns: it grants
/// Collaborate (or shares to a linked internal user), suggests on a secure record, removes the unmodified auto access of
/// an assignment that ended, and never re-creates what an operator removed. It never grants anything a body names.</para>
/// <para><b>Answers</b> (ADR-019: ProblemDetails with a stable reason code, a human message and the trace id — never a
/// bare 500). 200 with both outcomes. 503 <c>flags_unreadable</c> when the record's access settings could not be read
/// (nothing written). 500 <c>sync_failed</c> when the record or its ledger could not be read, <c>sync_incomplete</c>
/// when a write could not be confirmed, <c>no_access_incomplete</c> when a No Access removal could not be confirmed,
/// <c>restricted_external_incomplete</c> when an external user's share on a Restricted record could not be removed or
/// confirmed gone — each carrying the outcome.</para>
/// </remarks>
public static class AssignedAccessSyncEndpoint
{
    internal const string RecordUnresolvedReasonCode = "sdap.access.assigned.record_unresolved";
    internal const string NotFoundReasonCode = "sdap.access.assigned.record_not_found";
    internal const string FlagsUnreadableReasonCode = "sdap.access.assigned.flags_unreadable";
    internal const string SyncFailedReasonCode = "sdap.access.assigned.sync_failed";
    internal const string SyncIncompleteReasonCode = "sdap.access.assigned.sync_incomplete";
    internal const string NoAccessIncompleteReasonCode = "sdap.access.assigned.no_access_incomplete";
    internal const string RestrictedExternalIncompleteReasonCode = "sdap.access.assigned.restricted_external_incomplete";
    internal const string EntryRequiredReasonCode = "sdap.access.assigned.entry_required";
    internal const string EntryNotPendingReasonCode = "sdap.access.assigned.entry_not_pending";
    internal const string ListFailedReasonCode = "sdap.access.assigned.list_failed";

    private const string Title = "Assigned access not updated";

    /// <summary>Registers the three routes on the external-access management group.</summary>
    public static RouteGroupBuilder MapAssignedAccessEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/assigned-access/sync", SyncAsync)
            .WithName("SyncAssignedAccess")
            .WithSummary("Apply the Assigned-To access rule (and the No Access list) to one record now")
            .WithDescription(
                "Reads the record's 'Assigned *' columns and gives each named contact or organization Collaborate access " +
                "(a removable grant, or a share for a linked internal user), suggests it on a secure record, removes the " +
                "unmodified automatic access of an assignment that ended, and never re-creates access an operator removed. " +
                "Re-applies the record's No Access entries first. The body carries only the record. Requires Write on it.")
            .Produces<AssignedAccessSyncResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/assigned-access", ListAsync)
            .WithName("ListAssignedAccess")
            .WithSummary("List a record's Assigned-To access entries (suggestions and provenance)")
            .WithDescription(
                "Returns the record's live Assigned-To ledger entries: suggestions waiting on a secure record (naming the " +
                "source field), automatic grants and shares, declined and skipped entries, and the read-time access " +
                "(standing or organization grant) a contact keeps if its grant is removed. Requires Write on the record.")
            .Produces<AssignedAccessListResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/assigned-access/dismiss", DismissAsync)
            .WithName("DismissAssignedAccess")
            .WithSummary("Dismiss an Assigned-To suggestion on a secure record")
            .WithDescription(
                "Marks a suggestion Declined: the Assigned-To rule will not suggest or grant it again while the assignment " +
                "persists. A manual grant of the same person still succeeds. Requires Write on the record.")
            .Produces<AssignedAccessDismissResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>Handles the sync. Internal so a test can call it after the filter has run.</summary>
    internal static async Task<IResult> SyncAsync(
        AssignedAccessSyncRequest request,
        AssignedAccessMaterializer materializer,
        NoAccessShareEnforcer noAccessEnforcer,
        RestrictedExternalShareRemover restrictedExternal,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(request.RecordType, request.RecordId);
        if (!root.Ok)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error", RecordUnresolvedReasonCode, root.Error!, null);

        var tenants = CacheTenants(httpContext, configuration);
        var logical = ExternalGrantRoot.LogicalNameFor(root.Type);

        // (1) Task 143 first (owner R3: "Update Access" also re-applies No Access): a walled share is removed before the
        //     materializer decides — and the materializer consults the same guard, so it never re-creates one.
        var noAccess = await noAccessEnforcer.EnforceForRecordAsync(logical, root.Id, tenants, ct);

        // (1b) Task 114 (owner round 67 item 4): a Restricted record keeps no direct share of a user flagged external. Run
        //      BEFORE the materializer, so it records a share removed here as Restricted (given back when the record stops
        //      being Restricted), never as an operator's removal.
        var restricted = await restrictedExternal.RemoveForRecordAsync(root.Type, root.Id, tenants, ct);

        // (2) The Assigned-To invariant, from the record's own columns. The caller's oid becomes sprk_grantedby (A1).
        var outcome = await materializer.MaterializeAsync(
            new AssignedAccessRequest(
                root.Type, root.Id, AssignedAccessTrigger.Sync,
                CallerResolution.ResolveObjectId(httpContext.User),
                RevokeOnChange: true,
                CacheTenants: tenants),
            ct);

        var response = new AssignedAccessSyncResponse(outcome, noAccess) { RestrictedExternal = restricted };

        switch (outcome.Status)
        {
            case AssignedAccessStatus.NotFound:
                // Unreachable through the route (the filter's probe has no Write on a record that does not exist).
                return Refused(httpContext, StatusCodes.Status404NotFound, Title, NotFoundReasonCode,
                    "No record has this id, so nothing was changed.", response);

            case AssignedAccessStatus.FlagsUnreadable:
                return Refused(httpContext, StatusCodes.Status503ServiceUnavailable, Title, FlagsUnreadableReasonCode,
                    "The record's access settings could not be read, so nobody's access was changed. Try again in a moment.",
                    response);

            case AssignedAccessStatus.Failed:
                return Refused(httpContext, StatusCodes.Status500InternalServerError, Title, SyncFailedReasonCode,
                    outcome.Failures.FirstOrDefault()?.Message ?? "The record's assigned access could not be updated. Try again.",
                    response);
        }

        if (outcome.Failures.Count > 0)
        {
            logger.LogError(
                "[ASSIGNED-ACCESS] Sync of {Type} {RecordId} incomplete: {Failures}",
                root.Type, root.Id, string.Join(" | ", outcome.Failures.Select(f => $"{f.Kind} {f.SubjectKind} {f.SubjectId}")));
            return Refused(httpContext, StatusCodes.Status500InternalServerError, Title, SyncIncompleteReasonCode,
                outcome.Failures[0].Message, response);
        }

        var noAccessFailure = noAccess.SelectMany(r => r.Failures).FirstOrDefault();
        if (noAccessFailure is not null)
        {
            return Refused(httpContext, StatusCodes.Status500InternalServerError, "No Access not fully re-applied",
                NoAccessIncompleteReasonCode, noAccessFailure.Message, response);
        }

        if (restricted.Failures.FirstOrDefault() is { } restrictedFailure)
        {
            logger.LogError(
                "[ASSIGNED-ACCESS] Sync of {Type} {RecordId}: the Restricted rule for external users is incomplete: {Failures}",
                root.Type, root.Id, string.Join(" | ", restricted.Failures.Select(f => $"{f.Kind} {f.SystemUserId}")));
            return Refused(httpContext, StatusCodes.Status500InternalServerError, "Restricted access not fully applied",
                RestrictedExternalIncompleteReasonCode, restrictedFailure.Message, response);
        }

        return TypedResults.Ok(response);
    }

    /// <summary>Handles the list. Internal so a test can call it after the filter has run.</summary>
    internal static async Task<IResult> ListAsync(
        [AsParameters] AssignedAccessListQuery query,
        AssignedAccessMaterializer materializer,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(query.RecordType, query.RecordId);
        if (!root.Ok)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error", RecordUnresolvedReasonCode, root.Error!, null);

        try
        {
            return TypedResults.Ok(new AssignedAccessListResponse(await materializer.ListAsync(root.Type, root.Id, ct)));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[ASSIGNED-ACCESS] The ledger of {Type} {RecordId} could not be listed.", root.Type, root.Id);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, "Assigned access not read", ListFailedReasonCode,
                "The record's assigned-access entries could not be read. Try again.", null);
        }
    }

    /// <summary>Handles a dismiss. Internal so a test can call it after the filter has run.</summary>
    internal static async Task<IResult> DismissAsync(
        AssignedAccessDismissRequest request,
        AssignedAccessMaterializer materializer,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(request.RecordType, request.RecordId);
        if (!root.Ok)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error", RecordUnresolvedReasonCode, root.Error!, null);

        if (request.EntryId is not { } entryId || entryId == Guid.Empty)
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error", EntryRequiredReasonCode,
                "EntryId is required and must be a valid GUID.", null);

        int? declined;
        try
        {
            declined = await materializer.DismissAsync(root.Type, root.Id, entryId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "[ASSIGNED-ACCESS] Dismiss of {EntryId} on {Type} {RecordId} failed.", entryId, root.Type, root.Id);
            return Refused(httpContext, StatusCodes.Status500InternalServerError, "Suggestion not dismissed", SyncFailedReasonCode,
                "The suggestion could not be dismissed. Try again.", null);
        }

        if (declined is null)
            return Refused(httpContext, StatusCodes.Status409Conflict, "Suggestion not dismissed", EntryNotPendingReasonCode,
                "This suggestion is no longer waiting on this record (it was granted, dismissed or the assignment changed). " +
                "Reload Manage Access.", null);

        return TypedResults.Ok(new AssignedAccessDismissResponse(entryId, declined.Value));
    }

    /// <summary>
    /// The tenant namespaces a shared user's root-set cache is cleared under: the caller's <c>tid</c> (one Entra tenant
    /// per environment) and the deployment's — never "anonymous" (task 143's rule, the enforce route's shape).
    /// </summary>
    internal static IReadOnlyCollection<string> CacheTenants(HttpContext httpContext, IConfiguration configuration)
        => new[]
            {
                ImpersonatedRootSetSource.CacheTenantFor(httpContext.User),
                ImpersonatedRootSetSource.DeploymentCacheTenant(configuration),
            }
            .Where(t => !string.IsNullOrWhiteSpace(t) && t != "anonymous")
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <param name="outcome">The sync's <see cref="AssignedAccessSyncResponse"/> when there is one (typed <c>object</c>:
    /// it is only serialized into the ProblemDetails extensions, and an outbound type must not look like an inbound
    /// handler parameter to <c>InboundBodyDtoMappingGuardTests</c>).</param>
    private static IResult Refused(
        HttpContext httpContext, int statusCode, string title, string reasonCode, string detail, object? outcome)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier,
            ["reasonCode"] = reasonCode,
        };
        if (outcome is not null)
            extensions["outcome"] = outcome;

        return Results.Problem(statusCode: statusCode, title: title, detail: detail, extensions: extensions);
    }
}
