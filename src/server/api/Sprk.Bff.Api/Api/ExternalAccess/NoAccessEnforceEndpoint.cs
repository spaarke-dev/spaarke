using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// The body of <c>POST /api/v1/external-access/no-access/enforce</c> (task 143): ONLY the entry id. Everything else the
/// enforcement needs is read server-side from the active entry; any other field a client sends is ignored.
/// </summary>
public sealed record NoAccessEnforceRequest(Guid? EntryId);

/// <summary>
/// <c>POST /api/v1/external-access/no-access/enforce</c> — enforce one No Access entry NOW (unified-access-control-r2
/// task 143 · owner Q4; round 3 R3: "immediate on save — the form calls the BFF").
/// </summary>
/// <remarks>
/// <para><b>Who may call it.</b> The route joins the <c>/api/v1/external-access</c> group, so
/// <see cref="DelegationRuleFilter"/> gates it: the filter's target for this request is the <c>sprk_noaccessentries</c>
/// row itself, and the caller must hold Write on it, evaluated as the caller over OBO. Under owner O2 that is the
/// access-administrator role. An entry that does not exist and one the caller cannot write are the SAME 403 from the
/// filter — never a 404 from here (enumeration-safe, the filter's own rule).</para>
///
/// <para><b>What it can do.</b> Only REMOVE access, and only as far as an existing ACTIVE entry says — it accelerates
/// what <c>NoAccessShareReconciliationJob</c> would do on its next run and never grants. Whether a removal happens on a
/// record is the entry AUTHOR's authority, not the caller's (owner N5): the enforcer acts on a record only when the
/// entry's last modifier holds Write on it, and otherwise reports "not enforced: author lacks Write on record", which
/// the entry form shows as its post-save notice (task 154).</para>
///
/// <para><b>Answers.</b> 200 with the report when everything the entry covers was enforced or deliberately not
/// (removed shares; access a wall cannot remove per user — team, team ownership, role or business unit, owner N2;
/// records not enforced and why). 409 <c>entry_inactive</c> for an inactive entry and 422 <c>entry_malformed</c> for one
/// that names no single subject and object — both with no writes. 409 <c>last_person_on_secure_record</c> when a removal
/// would leave a secure record with nobody who can open it (owner S5), naming the record. 500
/// <c>enforcement_incomplete</c> with a message naming the user and record when a removal could not be confirmed or a
/// read failed — never a bare 500, never "removed". Every refusal is ProblemDetails with a stable reason code and the
/// trace id (ADR-019).</para>
/// </remarks>
public static class NoAccessEnforceEndpoint
{
    /// <summary>The entity set the delegation filter checks the caller's Write against.</summary>
    internal const string EntrySet = "sprk_noaccessentries";

    /// <summary>The table Write privilege the delegation filter asks: the table is organization-owned (see DelegationRuleFilter).</summary>
    internal const string EntryWritePrivilege = "prvWritesprk_noaccessentry";

    internal const string EntryRequiredReasonCode = "sdap.access.no_access.entry_required";
    internal const string EntryNotFoundReasonCode = "sdap.access.no_access.entry_not_found";
    internal const string EntryInactiveReasonCode = "sdap.access.no_access.entry_inactive";
    internal const string EntryMalformedReasonCode = "sdap.access.no_access.entry_malformed";
    internal const string EntryUnreadableReasonCode = "sdap.access.no_access.entry_unreadable";
    internal const string LastPersonReasonCode = "sdap.access.no_access.last_person_on_secure_record";
    internal const string IncompleteReasonCode = "sdap.access.no_access.enforcement_incomplete";

    private const string Title = "No Access entry not enforced";

    /// <summary>Registers the route on the external-access management group.</summary>
    public static RouteGroupBuilder MapNoAccessEnforceEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/no-access/enforce", EnforceAsync)
            .WithName("EnforceNoAccessEntry")
            .WithSummary("Enforce a No Access entry now: remove the direct shares it walls off on secure records")
            .WithDescription(
                "Reads the active No Access entry by id and removes the direct POA shares it walls off on the secure " +
                "records it covers, confirming each removal by read-back. Never grants, never removes a team or role " +
                "share (reported instead), never removes the last person who can see a secure record, and acts on a " +
                "record only when the entry's author holds Write on it. Requires Write on the entry.")
            .Produces<NoAccessEnforcementReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    /// <summary>Handles the route. Internal so a test can call it after the filter has run.</summary>
    internal static async Task<IResult> EnforceAsync(
        NoAccessEnforceRequest request,
        NoAccessShareEnforcer enforcer,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (request.EntryId is not { } entryId || entryId == Guid.Empty)
        {
            return Refused(httpContext, StatusCodes.Status400BadRequest, "Validation Error", EntryRequiredReasonCode,
                "EntryId is required and must be a valid GUID.", null);
        }

        // The walled user's cached set is cleared under the namespace their own reads write: the caller's tid (one
        // Entra tenant per environment) and, when it differs, the deployment's (the key the job uses).
        var tenants = new[]
            {
                ImpersonatedRootSetSource.CacheTenantFor(httpContext.User),
                ImpersonatedRootSetSource.DeploymentCacheTenant(configuration),
            }
            .Where(t => !string.IsNullOrWhiteSpace(t) && t != "anonymous")
            .Select(t => t!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var report = await enforcer.EnforceEntryAsync(entryId, tenants, ct);

        switch (report.Outcome)
        {
            case NoAccessEnforcementOutcome.NotFound:
                return Refused(httpContext, StatusCodes.Status404NotFound, Title, EntryNotFoundReasonCode,
                    "No No Access entry has this id, so nothing was removed.", null);

            case NoAccessEnforcementOutcome.Inactive:
                return Refused(httpContext, StatusCodes.Status409Conflict, Title, EntryInactiveReasonCode,
                    "This No Access entry is inactive, so it walls nobody off and nothing was removed. Deactivating an " +
                    "entry does not restore access it removed earlier; that takes a deliberate share.", null);

            case NoAccessEnforcementOutcome.Malformed:
                return Refused(httpContext, StatusCodes.Status422UnprocessableEntity, Title, EntryMalformedReasonCode,
                    "This No Access entry must name exactly one person, contact or organization, and exactly one record " +
                    "or organization. It walls nobody off as it is, so nothing was removed.", null);

            case NoAccessEnforcementOutcome.Failed:
                return Refused(httpContext, StatusCodes.Status500InternalServerError, Title, EntryUnreadableReasonCode,
                    report.Failures.FirstOrDefault()?.Message ?? "The No Access entry could not be read. Try again.", report);
        }

        if (report.Failures.Count > 0)
        {
            logger.LogError(
                "[NO-ACCESS-ENFORCE] Entry {EntryId} was not fully enforced: {Failures}",
                entryId, string.Join(" | ", report.Failures.Select(f => $"{f.Kind} {f.RecordType} {f.RecordId} {f.SystemUserId}")));
            return Refused(httpContext, StatusCodes.Status500InternalServerError, Title, IncompleteReasonCode,
                report.Failures[0].Message, report);
        }

        var lastPerson = report.NotEnforced
            .Where(n => n.Reason == NoAccessEnforcementReason.LastPersonOnSecureRecord)
            .ToList();
        if (lastPerson.Count > 0)
        {
            var records = string.Join(", ", lastPerson.Select(n => $"{n.RecordType} {n.RecordId}").Distinct());
            return Refused(httpContext, StatusCodes.Status409Conflict, Title, LastPersonReasonCode,
                $"This entry would leave a secure record with nobody who can open it ({records}), so that person's access " +
                "was kept. Share the record with someone else first, then save the entry again.", report);
        }

        return TypedResults.Ok(report);
    }

    private static IResult Refused(
        HttpContext httpContext, int statusCode, string title, string reasonCode, string detail,
        NoAccessEnforcementReport? report)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["traceId"] = httpContext.TraceIdentifier,
            ["reasonCode"] = reasonCode,
        };
        if (report is not null)
        {
            extensions["report"] = report;
        }

        return Results.Problem(statusCode: statusCode, title: title, detail: detail, extensions: extensions);
    }
}
