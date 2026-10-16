using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Api.Ontology;

/// <summary>
/// The Ontology admin route group, <c>/api/v1/ontology/admin</c> (ontology platform R1, D-22, FR-56). Task 104 adds the capability
/// probe, <c>GET /capabilities</c>; the admin pages' own routes (task 100 Rules, task 101 authoring) are added to this group.
/// </summary>
/// <remarks>
/// <para><b>What the probe is, and what it is not (D-22).</b> It answers ONE question, AS THE CALLER: does the caller hold
/// <c>prvCreatesprk_PolicyVersion</c>? The Console registers the "Ontology admin" workspace tab only when the answer is true. The
/// answer decides what is SHOWN, never what is ALLOWED: every admin write route still runs as the caller through
/// <see cref="IDataverseUserClient"/>, so Dataverse enforces the Spaarke Ontology Administrator role on each write and the audit names
/// the human. Nothing in the BFF reads this answer to authorize anything; a route that did would make the probe the security
/// boundary, which D-22 forbids.</para>
/// <para><b>Where the probe lives.</b> In <c>OntologyAdminCapabilityProbe.cs</c> (how it asks, the per-caller cache and its TTL
/// bound). This file reaches it through ONE call, <c>OntologyAdminCapabilityProbe.ResolveAsync</c>, made once by
/// <see cref="GetCapabilitiesAsync"/>. <c>OntologyAdminCallerBoundaryGuardTests</c> (rule A) fails when this file, or any other
/// route file, names the probe, its cache helpers or its cache resource anywhere else, or makes that call a second time.</para>
/// <para><b>Authorization (NFR-10, D-29).</b> <c>RequireAuthorization</c> on the group (no token: 401). The route takes no id; it
/// reads only the caller's own privilege set, keyed by the server-derived caller (census: Permanent <c>CallerScopedOnly</c>). Caller
/// unresolved (no user context, OBO not configured or refused, WhoAmI refused or no id) gets #1312's single 403; any other fault is
/// 503. The Console treats both as "no tab".</para>
/// <para><b>Placement (CLAUDE.md section 10; .claude/constraints/bff-extensions.md).</b> In the BFF: it must ask Dataverse as the
/// caller through the OBO client the BFF already owns, and no client-side role check exists anywhere (#42). It adds no service, DI
/// registration or package: the handler uses <see cref="IDataverseUserClient"/> (registered unconditionally in AddSpaarkeCore) and
/// <see cref="ITenantCache"/>, so it maps unconditionally with no asymmetric registration.</para>
/// </remarks>
public static class OntologyAdminEndpoints
{
    /// <summary>503 reason code: the caller was resolved but their privilege could not be read.</summary>
    internal const string CapabilityUnavailableReasonCode = "ontology.admin.capability_unavailable";

    public static void MapOntologyAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/ontology/admin")
            .WithTags("Ontology admin")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        group.MapGet("/capabilities", GetCapabilitiesAsync)
            .WithName("GetOntologyAdminCapabilities")
            .WithSummary("Whether the caller can author ontology rules (shows or hides the Ontology admin tab)")
            .WithDescription("Asks Dataverse, as the caller, whether they hold prvCreatesprk_PolicyVersion. The answer only decides "
                + "whether the Console shows the Ontology admin tab; every admin write is authorized by Dataverse as the caller. "
                + "Caller unresolved (no user context, OBO not configured or refused, WhoAmI refused or no id) -> 403; "
                + "any other fault -> 503.")
            .Produces<OntologyAdminCapabilities>(200)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Extension point: tasks 100 (Rules) and 101 (authoring) map their routes on this group. Each authorizes itself as the
        // caller; none may consult the capability answer above (rule A of OntologyAdminCallerBoundaryGuardTests enforces it).

        // Task 103: the Classification area declares its own group under the same URL prefix (its routes are in their own file).
        app.MapOntologyAdminClassificationEndpoints();
        group.MapOntologyAdminRulesEndpoints(); // task 100: the Rules list, rule page, On/Off and Retire
        OntologyAdminAuthoringEndpoints.MapOntologyAdminAuthoringEndpoints(group);
    }

    internal static async Task<IResult> GetCapabilitiesAsync(
        HttpContext httpContext,
        IDataverseUserClient user,
        ITenantCache cache,
        TimeProvider clock,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger(typeof(OntologyAdminEndpoints));
        var resolution = await OntologyAdminCapabilityProbe.ResolveAsync(httpContext.User, user, cache, clock, logger, ct).ConfigureAwait(false);

        switch (resolution.Outcome)
        {
            case OntologyAdminProbeOutcome.CallerUnresolved:
                return Results.Problem(
                    title: "Forbidden",
                    detail: "The caller could not be resolved to a Dataverse user.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?>
                    {
                        ["reasonCode"] = DecisionPlanEndpoints.CallerUnresolvedReasonCode,
                        ["correlationId"] = httpContext.TraceIdentifier,
                    });

            case OntologyAdminProbeOutcome.Answered:
                return Results.Ok(resolution.Capabilities);

            default:
                return Results.Problem(
                    title: "Service Unavailable",
                    detail: "Your Ontology admin access could not be checked. Try again.",
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    extensions: new Dictionary<string, object?>
                    {
                        ["reasonCode"] = CapabilityUnavailableReasonCode,
                        ["correlationId"] = httpContext.TraceIdentifier,
                    });
        }
    }
}
