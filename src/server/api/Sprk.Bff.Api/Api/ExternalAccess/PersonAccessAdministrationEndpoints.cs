using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// The person-level external-access administration routes: <c>POST /api/v1/external-access/remove-person</c> and
/// <c>POST /api/v1/external-access/reinstate-person</c>. Both are for external-access administrators only
/// (<see cref="ExternalAccessAdministratorAuthorizationFilter"/>).
/// </summary>
/// <remarks>
/// <para><b>The rule.</b> The removed marker alone refuses the person everywhere (composition, sign-in, invite, every grant
/// surface), whatever the contact's statecode. Removal writes the marker first and then revokes; it does not change the
/// contact's state and does not clear the binding. Reinstatement clears the marker only: it restores no grant (staff grant
/// again) and sends no invite.</para>
/// <para><b>Its own group.</b> The management group's delegation filter needs a record to authorize against, and these routes
/// name a person, not a record. So they sit on a group of their own under the same prefix, with the default (workforce)
/// authentication and the administrator filter instead.</para>
/// <para><b>CLAUDE.md §11.</b></para>
/// <list type="bullet">
/// <item><b>/remove-person.</b> Existing: <c>/revoke</c> ends one grant. Extension: <c>/revoke</c> is gated by Write on the
/// grant's record and has no person-level mode, so a person-wide removal under an administrator authority cannot sit behind
/// that filter. Cost of doing nothing: removing a person means revoking each grant by hand, with nothing that stops a later
/// grant, invite or Assigned-To pass from giving the access back.</item>
/// <item><b>/reinstate-person.</b> Existing: none clears the marker. Extension: a mode on <c>/remove-person</c> would put the
/// opposite operation behind one request shape; a separate route keeps each audit verb distinct. Cost of doing nothing:
/// there is no way to clear the marker under the same authority, so a removal could not be undone.</item>
/// </list>
/// </remarks>
public static class PersonAccessAdministrationEndpoints
{
    /// <summary>The audit verb of a removal (an event id on the audit trace).</summary>
    internal static readonly EventId RemovePersonAuditEvent = new(9821, "ExternalAccessPersonRemoved");

    /// <summary>The audit verb of a reinstatement (an event id on the audit trace).</summary>
    internal static readonly EventId ReinstatePersonAuditEvent = new(9822, "ExternalAccessPersonReinstated");

    /// <summary>The reason code of a removal that did not complete; the steps say which parts did. Retry the removal.</summary>
    internal const string RemoveIncompleteReasonCode = "sdap.access.remove_person.incomplete";

    /// <summary>The reason code of a reinstatement that did not complete. Retry it.</summary>
    internal const string ReinstateIncompleteReasonCode = "sdap.access.reinstate_person.incomplete";

    /// <summary>The reason code when the contact could not be read; nothing was written.</summary>
    internal const string ContactUnreadableReasonCode = "sdap.access.person.contact_unreadable";

    /// <summary>
    /// The reason code of a reinstatement refused because the contact is inactive; nothing was written. CLAUDE.md §11:
    /// Existing — the contact_unreadable and reinstate incomplete codes. Extension — neither names the action that unblocks
    /// the request, and an inactive contact is neither unreadable nor a failed write. Cost of doing nothing: a reinstated
    /// inactive contact is marked again by the next removal sweep, so the reinstatement undoes itself without telling staff.
    /// </summary>
    internal const string ContactInactiveReasonCode = "sdap.access.person.contact_inactive_reactivate_first";

    /// <summary>What staff are told when a reinstatement is refused because the contact is inactive.</summary>
    internal const string ContactInactiveStaffMessage =
        "This contact is inactive. Reactivate the contact first, then reinstate the person.";

    /// <summary>Registers both routes on a group of their own (see the class remarks).</summary>
    public static void MapPersonAccessAdministrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/external-access")
            .WithTags("External Access Management")
            .RequireAuthorization()
            .AddExternalAccessAdministratorAuthorizationFilter();

        // POST /api/v1/external-access/remove-person — an administrator removes a person's external access.
        group.MapPost("/remove-person", RemovePersonAsync)
            .WithName("RemoveExternalPerson")
            .WithSummary("Remove a person's external access (external-access administrators only)")
            .WithDescription(
                "Marks the contact as removed, then revokes every grant it holds (with the SPE cleanup), evicts the caches, and " +
                "clears its invite state, access permission and standing grant. The contact's state and binding are unchanged. " +
                "Idempotent; a partial removal says which steps completed and leaves the person refused.")
            .Produces<RemovePersonResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/v1/external-access/reinstate-person — an administrator clears the removed marker.
        group.MapPost("/reinstate-person", ReinstatePersonAsync)
            .WithName("ReinstateExternalPerson")
            .WithSummary("Reinstate a removed person (external-access administrators only)")
            .WithDescription(
                "Clears the contact's removed marker and nothing else: no grant is restored and no invite is sent. A contact " +
                "that is not removed is left unchanged. Only an active contact is reinstated: an inactive one is refused (409) " +
                "with nothing changed; reactivate it first.")
            .Produces<ReinstatePersonResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    /// <summary>
    /// Handles <c>POST /remove-person</c>. The removed marker alone refuses the person whatever the contact's statecode; removal
    /// writes it first and does not change the contact's state. Internal so the tests drive the production handler.
    /// </summary>
    internal static async Task<IResult> RemovePersonAsync(
        ExternalPersonRequest request,
        DataverseWebApiClient dataverseClient,
        SpeContainerMembershipService speContainerMembership,
        ExternalParticipationService participations,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        RecordContainerResolver containerResolver,
        IContactIdentityStore identityStore,
        IMembershipCacheInvalidator membershipInvalidator,
        TimeProvider timeProvider,
        HttpContext httpContext,
        ILogger<Program> logger,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (request.ContactId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ContactId is required and must be a valid GUID.");

        // The filter put the decision here; a handler reached without it (a wiring fault) refuses rather than acts.
        if (ExternalAccessAdministratorAuthorizationFilter.DecisionOf(httpContext) is not { IsAdministrator: true } admin)
            return ExternalAccessAdministratorAuthorizationFilter.Refusal(httpContext);

        var revocation = new ExternalGrantRevocationService(
            dataverseClient, speContainerMembership, participations, assignedAccess, containerResolver,
            timeProvider, identityStore, logger);
        var outcome = await new ExternalPersonRemovalService(
                dataverseClient, revocation, participations, identityStore, membershipInvalidator, timeProvider, logger)
            .RemoveAsync(request.ContactId, httpContext.TraceIdentifier, ct);

        Audit(loggerFactory, RemovePersonAuditEvent, "remove", admin, request.ContactId, outcome, httpContext.TraceIdentifier);

        switch (outcome.Status)
        {
            case PersonAdministrationStatus.NotFound:
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                    detail: "The contact was not found. Nothing was changed.");
            case PersonAdministrationStatus.ContactUnreadable:
                return ContactUnreadable(httpContext);
        }

        var notCompleted = outcome.GrantsNotCompleted ?? Array.Empty<Guid>();
        if (outcome.Status == PersonAdministrationStatus.Incomplete)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Removal incomplete",
                detail: outcome.Steps[0].Completed
                    ? "The person is marked as removed and is refused everywhere, but some steps did not complete. Retry the removal."
                    : "The person could not be marked as removed, so nothing else was changed. Retry the removal.",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = RemoveIncompleteReasonCode,
                    ["contactId"] = request.ContactId,
                    ["steps"] = outcome.Steps,
                    ["grantsRevoked"] = outcome.GrantsRevoked,
                    ["grantsNotCompleted"] = notCompleted,
                    ["traceId"] = httpContext.TraceIdentifier,
                });
        }

        return TypedResults.Ok(new RemovePersonResponse(request.ContactId, outcome.Steps, outcome.GrantsRevoked, notCompleted));
    }

    /// <summary>
    /// Handles <c>POST /reinstate-person</c>: clears the removed marker only. The marker alone refuses the person whatever the
    /// contact's statecode, and reinstatement does not change that state either; no grant is restored and no invite is sent.
    /// Only an active contact is reinstated: an inactive one is a 409 with nothing written (the audit still records the
    /// refusal), and a statecode that cannot be read is a 503.
    /// </summary>
    internal static async Task<IResult> ReinstatePersonAsync(
        ExternalPersonRequest request,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IContactIdentityStore identityStore,
        IMembershipCacheInvalidator membershipInvalidator,
        TimeProvider timeProvider,
        HttpContext httpContext,
        ILogger<Program> logger,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (request.ContactId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ContactId is required and must be a valid GUID.");

        if (ExternalAccessAdministratorAuthorizationFilter.DecisionOf(httpContext) is not { IsAdministrator: true } admin)
            return ExternalAccessAdministratorAuthorizationFilter.Refusal(httpContext);

        // A reinstatement revokes nothing, so the service is built without the revoke.
        var outcome = await new ExternalPersonRemovalService(
                dataverseClient, revocation: null, participations, identityStore, membershipInvalidator, timeProvider, logger)
            .ReinstateAsync(request.ContactId, ct);

        Audit(loggerFactory, ReinstatePersonAuditEvent, "reinstate", admin, request.ContactId, outcome, httpContext.TraceIdentifier);

        return outcome.Status switch
        {
            PersonAdministrationStatus.NotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: "The contact was not found. Nothing was changed."),
            PersonAdministrationStatus.ContactUnreadable => ContactUnreadable(httpContext),
            PersonAdministrationStatus.RefusedContactInactive => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Contact inactive",
                detail: ContactInactiveStaffMessage,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = ContactInactiveReasonCode,
                    ["staffMessage"] = ContactInactiveStaffMessage,
                    ["contactId"] = request.ContactId,
                    ["traceId"] = httpContext.TraceIdentifier,
                }),
            PersonAdministrationStatus.Incomplete => Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Reinstatement incomplete",
                detail: "The removed marker could not be cleared, so the person is still removed. Retry the reinstatement.",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = ReinstateIncompleteReasonCode,
                    ["contactId"] = request.ContactId,
                    ["steps"] = outcome.Steps,
                    ["traceId"] = httpContext.TraceIdentifier,
                }),
            _ => TypedResults.Ok(new ReinstatePersonResponse(request.ContactId, outcome.MarkerCleared)),
        };
    }

    private static IResult ContactUnreadable(HttpContext httpContext) => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Contact unavailable",
        detail: "The contact could not be read, so nothing was changed. Try again in a moment.",
        extensions: new Dictionary<string, object?>
        {
            ["reasonCode"] = ContactUnreadableReasonCode,
            ["traceId"] = httpContext.TraceIdentifier,
        });

    /// <summary>
    /// The audit trace of one removal or reinstatement: the administrator's systemuserid as WhoAmI answered it, how they hold
    /// the authority (direct or team), the contact, the statecode read at the start, every step's result and the correlation
    /// id. Never an email address. Slice C2c: written under <see cref="ExternalAccessAudit.Category"/> through
    /// <see cref="ExternalAccessAudit.Write"/>, so it is exported regardless of trace sampling.
    /// </summary>
    private static void Audit(
        ILoggerFactory loggerFactory, EventId verb, string action, AdministratorAuthorityDecision admin, Guid contactId,
        PersonAdministrationOutcome outcome, string correlationId)
        => ExternalAccessAudit.Write(loggerFactory.CreateLogger(ExternalAccessAudit.Category), audit => audit.LogInformation(verb,
            "[EXT-PERSON-AUDIT] {Action}: administrator {AdminSystemUserId} ({AuthorityLeg}), contact {ContactId}, statecode at " +
            "start {StateCode}, status {Status}, steps [{Steps}], grants revoked {GrantsRevoked}, grants not completed " +
            "[{GrantsNotCompleted}], correlation {CorrelationId}.",
            action, admin.SystemUserId, admin.Leg, contactId, outcome.StateCodeAtStart?.ToString() ?? "unknown",
            outcome.Status,
            string.Join("; ", outcome.Steps.Select(s => $"{s.Step}={(s.Completed ? "completed" : "not completed")}")),
            outcome.GrantsRevoked,
            string.Join(", ", outcome.GrantsNotCompleted ?? Array.Empty<Guid>()),
            correlationId));
}
