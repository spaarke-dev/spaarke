using Sprk.Bff.Api.Api.ExternalAccess.Dtos;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// <c>GET /api/v1/external-access/can-manage-access?recordType=&amp;recordId=</c> — lets a client ASK the
/// delegation question instead of GUESSING at it: <i>may I change who can access this record?</i>
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b> (unified-access-control-r2 task 118, owner decision D-1 option C). The
/// Manage Access affordance in <c>TrackingFieldTrio</c> gated on a Dataverse TABLE-level privilege —
/// <c>hasEntityPrivilege('sprk_externalrecordaccess', Create, Global)</c> — which is a different question
/// from the one this server enforces, with the opposite fail direction. The server asks "do you hold Write
/// on THIS record" and denies what it cannot evaluate; the client asked "may you create rows in that table
/// anywhere" and ALLOWED what it could not evaluate. A caller with the table privilege but no Write on a
/// confidential matter was offered the button and then refused by the server — the affordance promised an
/// action the caller could not perform, on exactly the records where that matters most.</para>
///
/// <para><b>The status code IS the answer, and that is deliberate.</b> This route is registered on the
/// <c>/api/v1/external-access</c> management group, so <see cref="DelegationRuleFilter"/> — the SAME filter
/// that gates every grant, revoke, share and expiry change — runs before this handler. A caller without
/// Write never reaches the code below; they get the filter's 403. So the client is not told a mirror of the
/// rule, computed somewhere a mirror could drift from the original: it is told the OUTCOME OF THE RULE
/// ITSELF, produced by the one implementation of it. Two gates that cannot disagree, because there is still
/// only one gate.</para>
///
/// <para><b>What this endpoint is not.</b> It is not an enforcement point and must never become one — no
/// write is permitted here, and no other route may consult it in place of the filter (ADR-008: resource
/// authorization lives in the filter, at route registration). It is also not a permissions API: it answers
/// about ONE record for the CALLER, and deliberately does not return the rights mask. Handing a client the
/// raw rights would invite it to re-derive authorization rules client-side, which is the pattern this
/// project exists to remove (CLAUDE.md §11 — one component that answers one question well).</para>
///
/// <para><b>It adds no second <c>RetrievePrincipalAccess</c> caller.</b> The rights come from
/// <see cref="Infrastructure.ExternalAccess.CallerRecordAccessProbe"/>, invoked once by the filter on the
/// caller's OBO token. This handler re-probes nothing — unlike
/// <see cref="InternalShareEndpoints.ShareAsync"/>, which re-probes because it needs the rights THEMSELVES to
/// intersect a requested level. Here the decision is the whole answer, so a second probe would double the
/// Dataverse round trips on a form-load path and buy nothing.</para>
///
/// <para><b>🔴 Removing the group filter silently inverts this endpoint's meaning.</b> This handler has no
/// rights logic of its own — by design — so detached from <c>AddDelegationRuleFilter()</c> it would answer
/// <c>true</c> to everyone, and every client gate built on it would fail OPEN: the exact defect task 118
/// closed. Equally, deleting this route's <c>case</c> from <c>DelegationRuleFilter.ResolveTargetAsync</c>
/// would send it to the default DENY branch and hide the affordance from everyone. Both directions are
/// pinned by <c>RecordAccessGateTests</c> (<c>tests/integration/auth/UnifiedAccessControl/</c>), whose
/// negatives each have a positive twin differing only in the caller's rights.</para>
///
/// <para>ADR-001 Minimal API · ADR-008 authorization by the group's endpoint filter · ADR-019 refusals are
/// ProblemDetails with a stable reason code and the trace id.</para>
/// </remarks>
public static class RecordAccessGateEndpoint
{
    /// <summary>The request named no record this handler can resolve.</summary>
    /// <remarks>
    /// Unreachable through the route — the filter resolves the same root first and denies an unresolvable
    /// one with 403. Checked anyway, on the sibling routes' discipline: a handler must not trust a pipeline
    /// it cannot see.
    /// </remarks>
    internal const string RecordUnresolvedReasonCode = "sdap.access.gate.record_unresolved";

    /// <summary>Registers the route on the external-access management group.</summary>
    public static RouteGroupBuilder MapRecordAccessGateEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/can-manage-access", Handle)
            .WithName("GetCallerRecordAccessGate")
            .WithSummary("Whether the caller may change who can access this record")
            .WithDescription(
                "Answers the delegation question (spec FR-07 / owner decision B-14) for one record: 200 with " +
                "canManageAccess = true when the caller holds Write on it, 403 with a " +
                "sdap.access.deny.delegation_* reason code when they do not, or when it could not be " +
                "established. Clients gate the Manage Access affordance on this and MUST treat anything other " +
                "than 200 + canManageAccess = true as a denial.")
            .Produces<RecordAccessGateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
    }

    /// <summary>Handles <c>GET /api/v1/external-access/can-manage-access</c>.</summary>
    /// <returns>
    /// 200 with <c>canManageAccess = true</c>. 400 only for a record the root resolver rejects. Every denial
    /// is the group filter's 403 and never reaches here.
    /// </returns>
    internal static IResult Handle(
        [AsParameters] RecordAccessGateQuery query,
        HttpContext httpContext)
    {
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(query.RecordType, query.RecordId);
        if (!root.Ok)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error",
                detail: root.Error!,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = httpContext.TraceIdentifier,
                    ["reasonCode"] = RecordUnresolvedReasonCode,
                });
        }

        // Reaching this line IS the answer: DelegationRuleFilter established Write on this record, as the
        // caller, over OBO. Nothing here re-decides it.
        return TypedResults.Ok(new RecordAccessGateResponse(root.Id, CanManageAccess: true));
    }
}
