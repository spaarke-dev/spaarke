using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// GET /api/v1/external/me
///
/// Called by the Power Pages SPA on startup to retrieve the authenticated portal user's
/// project access information. Returns the Contact's project list with access levels so
/// the SPA can build its navigation and enforce client-side access restrictions.
///
/// Authentication: Power Pages portal JWT (ExternalCallerAuthorizationFilter).
/// The filter validates the token and loads participations from Redis / Dataverse.
///
/// Follows ADR-001: Minimal API — no controllers.
/// Follows ADR-008: ExternalCallerAuthorizationFilter applied per-endpoint.
/// Follows ADR-009: Redis-first caching — participation data is cached by the filter.
/// </summary>
public static class ExternalUserContextEndpoint
{
    /// <summary>
    /// Handles GET /api/v1/external/me.
    ///
    /// The CallerPrincipalAuthorizationFilter (applied at the group level in ExternalAccessEndpoints.cs,
    /// teams-app-r1 task 025) has already authenticated the caller on EITHER plane (CIAM external
    /// contact or workforce user) and stored the resolved CallerPrincipal on HttpContext.Items before
    /// this handler runs.
    /// </summary>
    /// <param name="httpContext">The current HTTP context (used to retrieve the CallerPrincipal).</param>
    /// <param name="logger">Logger for request tracing.</param>
    /// <returns>
    /// 200 OK with ExternalUserContextResponse containing the projects the caller can READ.
    /// 401 Unauthorized if the portal token is missing or invalid (returned by filter).
    /// 403 Forbidden if the Contact has no active participation records (returned by filter).
    /// </returns>
    public static IResult Handle(
        HttpContext httpContext,
        ILogger<Program> logger)
    {
        // CallerPrincipalAuthorizationFilter (task 025) has already run and set the principal —
        // for EITHER plane (CIAM external contact or workforce user). Principal-agnostic: this handler
        // does not branch on plane; it projects the resolved principal's project access uniformly.
        var caller = httpContext.Items[CallerPrincipal.HttpContextItemsKey] as CallerPrincipal;

        if (caller is null)
        {
            // Should not happen if the filter is correctly applied, but guard defensively
            logger.LogError(
                "[EXT-ME] CallerPrincipal not found in HttpContext.Items. " +
                "Ensure AddCallerPrincipalAuthorizationFilter() is applied to this group. TraceId={TraceId}",
                httpContext.TraceIdentifier);

            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Authentication context not available",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }

        // ONLY projects the caller can READ (unified-access-control-r2 task 136 · defect C2).
        //
        // This used to project every ProjectAccess entry and emit the level string "None" for an entry with
        // no rights. That told the client the GUID of a record the caller holds nothing on — a Secure project
        // reached only through an organization grant, for instance — which is a disclosure in itself. The
        // principal no longer carries such entries (both strategies prune them), and ReadableProjects filters
        // again here, so no level below can be null and "None" is never emitted.
        //
        // The /me contract is a level STRING, so this is the one place the lossy rights→level display
        // projection is used (task 033 — CallerProjectAccess stores AccessRights and derives the level;
        // see ExternalAccessLevels.ToDisplayLevel). Read alone projects to ViewOnly.
        var projects = new List<ProjectAccessEntry>();
        foreach (var project in caller.ReadableProjects)
        {
            if (project.AccessLevel is { } level)
            {
                projects.Add(new ProjectAccessEntry(project.ProjectId, level.ToString()));
            }
        }

        logger.LogInformation(
            "[EXT-ME] Caller {ContactId} ({Plane}) requested context: {Count} readable projects. TraceId={TraceId}",
            caller.ContactId, caller.Plane, projects.Count, httpContext.TraceIdentifier);

        var response = new ExternalUserContextResponse(
            caller.ContactId,
            caller.Email,
            projects,
            // The caller's own systemuser id (same property IsCreatedByCaller reads). Null for a CIAM or
            // contact-only caller; an empty id is never emitted.
            caller.SystemUserId is { } systemUserId && systemUserId != Guid.Empty ? systemUserId : null,
            // Task 036 fix round 1 (F4): the levels above are the composed rights. For a caller whose access Dataverse
            // decides they say only what the caller may see; write affordances are then asked of the record (its list's
            // canCreate). No per-record call here. Omitted (false) for every other caller.
            WriteAccessPerRecord: caller.DataverseConfirmsWrites);

        return Results.Ok(response);
    }

    /// <summary>
    /// The handler mapped to GET /api/v1/external/me: <see cref="Handle"/> unchanged, plus the additive
    /// <see cref="ExternalUserContextResponse.Firms"/> list (ebilling-r1 task 051, owner decision 2026-10-17: the SPA's invoice
    /// submission has a "Choose firm" step when the caller belongs to several firms).
    /// </summary>
    /// <remarks>
    /// <para>The firms are the caller contact's CONFERRING organization memberships, read through
    /// <see cref="ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync"/> — the one definition the submission routes validate
    /// <c>firmId</c> against — so the SPA is never offered a firm the submission would refuse as foreign. Only the caller's own ids are ever
    /// looked up, and only id and name leave.</para>
    /// <para><b>Cost.</b> With eBilling off (<c>Firms = null</c>, omitted from the JSON) the extra work is the flag read and nothing else: the
    /// firm services are resolved from the request's service provider only AFTER the flag check, so they are not built, and nothing is read.
    /// With eBilling on, a caller with a contact costs one live contact-state read (remembered for the request: the principal's own composition
    /// already made it), one membership read, and — when there is at least one firm — ONE query for all the firm names. A workforce systemuser
    /// costs nothing extra (<c>Firms = []</c>: internal users submit through the internal route). A fault in
    /// the membership or name read leaves <c>null</c> — an additive field never fails /me, and "unknown" is not "no firm".</para>
    /// </remarks>
    public static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        ILogger<Program> logger,
        Microsoft.Extensions.Options.IOptionsMonitor<Sprk.Bff.Api.Configuration.EbillingOptions> ebilling,
        CancellationToken ct)
    {
        // Fully qualified: a `using` here would shift the line numbers the route ledger cites (:45, :74).
        var result = Handle(httpContext, logger);
        if (result is not Microsoft.AspNetCore.Http.HttpResults.Ok<ExternalUserContextResponse> { Value: { } response }
            || !ebilling.CurrentValue.Enabled
            || httpContext.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal caller)
        {
            return result;
        }

        // A systemuser is refused on the external submission routes (ExternalInvoiceSubmissionAuthorizationFilter), so it is offered no firm.
        if (ExternalInvoiceSubmissionAuthorizationFilter.IsSystemUser(caller))
        {
            return Results.Ok(response with { Firms = Array.Empty<ExternalFirmEntry>() });
        }

        IReadOnlyList<ExternalFirmEntry>? firms = null;
        try
        {
            // Resolved here, after the flag check: with eBilling off nothing below is built (the services are not parameters of the handler).
            var participations = httpContext.RequestServices.GetRequiredService<ExternalParticipationService>();
            var (firmIds, unreadable) = await ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync(caller, participations, logger, ct);
            if (!unreadable)
            {
                var named = firmIds.Count == 0
                    ? Array.Empty<(Guid FirmId, string Name)>()
                    : await httpContext.RequestServices
                        .GetRequiredService<Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService>()
                        .ReadActiveFirmNamesAsync(firmIds, ct);
                firms = named?.Select(f => new ExternalFirmEntry(f.FirmId, f.Name)).ToList();
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[EXT-ME] Firm list unavailable. TraceId={TraceId}", httpContext.TraceIdentifier);
        }

        return Results.Ok(response with { Firms = firms });
    }
}
