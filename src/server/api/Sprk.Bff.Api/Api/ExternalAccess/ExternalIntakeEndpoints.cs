// spaarke-SPA-external-access-platform-r3 task 035 (2026-10-11) — FR-05 server half, FR-06 intake half.
//
// POST /api/v1/external/servicerequests creates one sprk_servicerequest from a Legal Front Door wizard submission (NDA
// Assessment, Invention Submission, Policy Question, Trademark Search). Mounted UNDER the shared /api/v1/external group
// (ExternalAccessEndpoints), so it inherits the ExternalCollaboration dual-scheme policy AND the group-level
// CallerPrincipalAuthorizationFilter; the handler receives a plane-agnostic CallerPrincipal and never branches on plane.
//
// Order, each step before any Dataverse write:
//   1. Tier-1 module entitlement (ADR-028 A3, server-side): the caller must be entitled to legal-front-door, asked through
//      ModuleEntitlementResolver.IsEntitledAsync — the ONE check that /me/entitlements reports and that reads the same
//      ExternalAccessOptions value as the "service-requests" descriptor (review 011 K2). So a CIAM partner is refused
//      (403) while ExternalAccess:PartnerServiceRequestsEnabled is off, and admitted when it is on, with no plane test here.
//   1b. The intake TYPE (owner decision 2026-10-12): Invention Submission is workforce-only; a CIAM partner gets it only when
//      ExternalAccess:PartnerInventionIntakeEnabled is on, else 403 (reasonCode = errorCode = DenyInventionNotAvailable).
//      Asked of ModuleEntitlementResolver.IsIntakeTypeAvailable, the rule /me/entitlements reports as intakeRequestTypes.
//   2. The requester: the resolved principal's contact. A caller whose contact could not be READ (ContactUnreadable) is told
//      503 to retry. A caller with none (a licensed systemuser not linked to a contact) is refused 403 with a reason that says so — the row could not be filed in their name, and the Service Requests
//      module (scope sprk_requestedby = caller contact) could never show it to them.
//   3. Validation: requestType ∈ the four sprk_requesttype options; title required (→ sprk_name, ≤ 850); answers a JSON
//      object of ≤ 100 KB (UTF-8) → sprk_intakedata. Anything else is a 400 with an errorCode.
//   4. The owner (owner decision 2026-10-11, I-6): the default owner team of the business unit that owns the requester's
//      contact, resolved through IRecordOwnershipResolver (target = the contact) BEFORE the create. A resolver read fault is a
//      503 (EXT_INTAKE_DATAVERSE_UNAVAILABLE); a team that cannot be resolved REFUSES (500 EXT_INTAKE_OWNER_UNRESOLVED, no
//      exception text, nothing created) - never an application-owned row.
//   4b. The requester's department (owner decision 2026-10-12): the contact's standard department column, read app-only and
//      stored in the answers as the SYSTEM key requesterDepartment (a client-sent key of that name is dropped; no value =
//      key omitted; a read fault = 503, nothing created).
//   5. The create: app-only (no OBO) through ExternalDataService.CreateServiceRequestAsync — sprk_requestedby,
//      sprk_requesttype, sprk_intakedata, sprk_name, sprk_direction = Inbound, ownerid = the team from step 4. A Dataverse transport failure, timeout,
//      429 or 5xx is a 503 (EXT_INTAKE_DATAVERSE_UNAVAILABLE, nothing submitted); any other failure is a 500. The route caps the
//      whole body at 256 KB (413 above it). The request DTO is closed, so a requester,
//      owner or creator named in the body never binds.

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// The Legal Front Door intake submit (FR-05/FR-06): <c>POST /api/v1/external/servicerequests</c>. See the file header.
/// </summary>
public static class ExternalIntakeEndpoints
{
    /// <summary>Deny code for a caller not entitled to the legal-front-door module (incl. a CIAM partner, switch off).</summary>
    public const string DenyModuleNotEntitled = "sdap.external.intake.module_not_entitled";

    /// <summary>
    /// Deny code (also sent as <c>errorCode</c>) for a CIAM partner submitting Invention Submission while
    /// <c>ExternalAccess:PartnerInventionIntakeEnabled</c> is off (owner decision 2026-10-12). Distinct from
    /// <see cref="DenyModuleNotEntitled"/>: the partner may use the other intake types.
    /// </summary>
    public const string DenyInventionNotAvailable = "sdap.external.intake.invention_not_available";

    /// <summary>
    /// The SYSTEM key the route writes into the stored answers: the requester contact's <c>department</c> (owner decision
    /// 2026-10-12). Never taken from the client - any key of this name the client sends is removed. Omitted when the contact
    /// has no department.
    /// </summary>
    public const string RequesterDepartmentKey = "requesterDepartment";

    /// <summary>Deny code for a caller whose principal carries no contact, so no requester can be recorded.</summary>
    public const string DenyNoContact = "sdap.external.intake.no_contact";

    /// <summary>Error code for a request type outside the four <c>sprk_requesttype</c> options (or none).</summary>
    public const string ErrorInvalidRequestType = "EXT_INTAKE_INVALID_REQUEST_TYPE";

    /// <summary>Error code for a missing, blank or over-long title.</summary>
    public const string ErrorInvalidTitle = "EXT_INTAKE_INVALID_TITLE";

    /// <summary>Error code for answers that are missing, not a JSON object, or over <see cref="MaxAnswersBytes"/>.</summary>
    public const string ErrorInvalidAnswers = "EXT_INTAKE_INVALID_ANSWERS";

    /// <summary>Error code for a caller whose derived contact could not be READ (a Dataverse fault): retry, not a final deny.</summary>
    public const string ErrorContactUnreadable = "EXT_INTAKE_CONTACT_UNREADABLE";

    /// <summary>
    /// Error code for a submission whose owning team could not be resolved (the requester's business unit has no single
    /// default owner team, or the contact has no owning business unit): refused, nothing created (I-6).
    /// </summary>
    public const string ErrorOwnerUnresolved = "EXT_INTAKE_OWNER_UNRESOLVED";

    /// <summary>Error code for a create that Dataverse could not take right now (transport, timeout, 429, 5xx).</summary>
    public const string ErrorDataverseUnavailable = "EXT_INTAKE_DATAVERSE_UNAVAILABLE";

    /// <summary>
    /// The whole-request body cap for the route: 256 KB. Minimal API binds the body before any handler gate, and the Kestrel
    /// default is 30 MB, so without this a caller could make the BFF buffer and parse tens of MB only to be told 400.
    /// Answers between 100 KB (<see cref="MaxAnswersBytes"/>) and this cap still get the handler's 400
    /// (<see cref="ErrorInvalidAnswers"/>); a body above the cap gets the platform's 413.
    /// </summary>
    public const int MaxRequestBodyBytes = 256 * 1024;

    /// <summary>The largest <c>answers</c> object accepted: 100 KB of UTF-8 JSON (spec FR-05 / task 035).</summary>
    public const int MaxAnswersBytes = 100 * 1024;

    /// <summary>The length of <c>sprk_servicerequest.sprk_name</c> (NVARCHAR(850)).</summary>
    public const int MaxTitleLength = 850;

    /// <summary>
    /// Registers the intake submit on the shared external collaboration group. The group's policy and
    /// CallerPrincipalAuthorizationFilter are inherited; this adds the route only.
    /// </summary>
    public static RouteGroupBuilder MapExternalIntakeEndpoints(this RouteGroupBuilder externalGroup)
    {
        externalGroup.MapPost("/servicerequests", SubmitServiceRequestAsync)
            .WithName("SubmitExternalServiceRequest")
            .RequireWorkforceCeiling(WorkforceRoleCeilingGate.SubmitServiceRequest)
            .WithSummary("Submit a Legal Front Door request as the caller")
            // Minimal API binds the whole body before any gate; cap it (precedent: ComposeSaveEndpoints).
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithDescription(
                "Creates one sprk_servicerequest from a Legal Front Door wizard: the request type (NDA Assessment, " +
                "Invention Submission, Policy Question or Trademark Search), a title and the answers (a JSON object of at " +
                "most 100 KB). The requester is the caller's contact. 403 unless the caller is entitled to the " +
                "legal-front-door module (CIAM partners only when the partner service-request switch is on), 403 " +
                "sdap.external.intake.invention_not_available for a CIAM partner submitting Invention Submission while the " +
                "partner invention switch is off, and 403 unless the caller has a " +
                "contact; 400 for an unknown type, a missing title or invalid answers; 413 for a request body over 256 KB " +
                "(answers between 100 KB and that cap are a 400); 503 when the caller's contact cannot be read, the owning team " +
                "cannot be read, or Dataverse cannot take the create right now (nothing was submitted; try again); 500 " +
                "EXT_INTAKE_OWNER_UNRESOLVED when the requester's business unit has no resolvable default owner team " +
                "(nothing was submitted).")
            // A write path: the same per-user throttle as the message post (task 034).
            .RequireRateLimiting("dataverse-query")
            .Accepts<SubmitServiceRequestRequest>("application/json")
            .Produces<ExternalServiceRequestCreatedDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return externalGroup;
    }

    private static async Task<IResult> SubmitServiceRequestAsync(
        SubmitServiceRequestRequest? request,
        HttpContext httpContext,
        ModuleEntitlementResolver entitlements,
        ExternalDataService dataService,
        IRecordOwnershipResolver ownership,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        if (httpContext.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Authentication context not available — ensure AddCallerPrincipalAuthorizationFilter is applied");
        }

        // 1. Tier-1, server-side: the one shared check (see the file header).
        if (!await entitlements
                .IsEntitledAsync(principal, httpContext.User, ModuleEntitlementResolver.LegalFrontDoorModule, ct)
                .ConfigureAwait(false))
        {
            logger.LogWarning(
                "[EXT-INTAKE] DENY submit for contact {ContactId} / systemuser {SystemUserId}: not entitled to {Module}.",
                principal.ContactId, principal.SystemUserId, ModuleEntitlementResolver.LegalFrontDoorModule);
            return ProblemDetailsHelper.Forbidden(
                DenyModuleNotEntitled, "You are not entitled to submit Legal Front Door requests.");
        }

        // 1b. The intake TYPE (owner decision 2026-10-12): Invention Submission is for workforce callers, and for a CIAM
        //     partner only behind ExternalAccess:PartnerInventionIntakeEnabled. The same rule /me/entitlements reports as
        //     intakeRequestTypes. Only a KNOWN type is gated here; an unknown or missing one is the 400 below.
        if (request?.RequestType is { } requestedType
            && ExternalDataService.ServiceRequestTypes.ContainsKey(requestedType)
            && !entitlements.IsIntakeTypeAvailable(principal, requestedType))
        {
            logger.LogWarning(
                "[EXT-INTAKE] DENY submit of type {RequestType} for contact {ContactId}: the type is not available to this caller.",
                requestedType, principal.ContactId);
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: "This request type is not available to you.",
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = DenyInventionNotAvailable,
                    ["errorCode"] = DenyInventionNotAvailable,
                });
        }

        // 2. The requester is the resolved principal's contact, never client input. A contact that could not be READ is
        //    unknown, not absent: ContactId is Guid.Empty in both cases, so the flag decides. A linked lawyer hit by a
        //    Dataverse 429 is told to retry, not that their account is unlinked (task 035 review F1, fail closed).
        if (principal.ContactUnreadable && principal.ContactId == Guid.Empty)
        {
            logger.LogWarning(
                "[EXT-INTAKE] Submit refused for systemuser {SystemUserId}: the derived contact could not be read.",
                principal.SystemUserId);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
                detail: "Your account could not be checked right now. Nothing was submitted; try again shortly.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorContactUnreadable });
        }

        if (principal.ContactId == Guid.Empty)
        {
            logger.LogWarning(
                "[EXT-INTAKE] DENY submit for systemuser {SystemUserId}: the caller has no linked contact, so no requester " +
                "can be recorded.", principal.SystemUserId);
            return ProblemDetailsHelper.Forbidden(
                DenyNoContact,
                "Your account is not linked to a contact record, so a request cannot be filed in your name. " +
                "Ask your administrator to link your account to a contact.");
        }

        // 3. Validation.
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var requestType = request!.RequestType!.Value;
        var title = request.Title!.Trim();
        var clientAnswers = request.Answers!.Value;

        // 4. The owner (owner decision 2026-10-11, I-6): the default owner team of the business unit that owns the requester's
        //    contact, through the ONE resolver. Resolved before anything is written; unresolved REFUSES.
        RecordOwnerResolution owner;
        try
        {
            owner = await ownership
                .ResolveOwnerAsync(
                    new RecordOwnershipContext { TargetEntityLogicalName = "contact", TargetRecordId = principal.ContactId }, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A Dataverse read fault is not an answer (the resolver propagates it): retryable, nothing was created, and no
            // exception text leaves.
            logger.LogWarning(ex,
                "[EXT-INTAKE] Dataverse unavailable resolving the owning team for contact {ContactId}.", principal.ContactId);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
                detail: "Nothing was submitted; try again.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorDataverseUnavailable });
        }

        if (!owner.IsOwned)
        {
            logger.LogError(
                "[EXT-INTAKE] REFUSED submit for contact {ContactId}: no owning team could be resolved ({RefusalCode}: {Reason}).",
                principal.ContactId, owner.RefusalCode, owner.Reason);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "Your request could not be filed because its owning team could not be determined. " +
                        "Nothing was submitted; contact your administrator.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorOwnerUnresolved });
        }

        // 4b. The requester's department (owner decision 2026-10-12): read from the contact record app-only and stored as the
        //     system key requesterDepartment - never from the client. A read FAULT is retryable (nothing was submitted); a
        //     missing value just omits the key.
        string? department;
        try
        {
            department = await dataService.GetContactDepartmentAsync(principal.ContactId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-INTAKE] Dataverse unavailable reading the department of contact {ContactId}.", principal.ContactId);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
                detail: "Nothing was submitted; try again.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorDataverseUnavailable });
        }

        var answersJson = WithSystemKeys(clientAnswers, department);

        // 5. The create — app-only, the five intake columns and the owner.
        try
        {
            var id = await dataService
                .CreateServiceRequestAsync(principal.ContactId, owner.OwningTeamId!.Value, requestType, title, answersJson, ct)
                .ConfigureAwait(false);

            logger.LogInformation(
                "[EXT-INTAKE] Created service request {ServiceRequestId} (type {RequestType}) for contact {ContactId}.",
                id, requestType, principal.ContactId);

            return Results.Created(
                (string?)null, new ExternalServiceRequestCreatedDto { SprkServicerequestid = id.ToString("D") });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ExternalDataService.DataverseUnavailableException ex)
        {
            // Transport failure, timeout, 429 or 5xx: nothing was created, so a retry is safe. No exception text leaves.
            logger.LogWarning(ex,
                "[EXT-INTAKE] Dataverse unavailable creating a service request (type {RequestType}) for contact {ContactId}.",
                requestType, principal.ContactId);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
                detail: "Nothing was submitted; try again.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorDataverseUnavailable });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-INTAKE] Create service request (type {RequestType}) failed for contact {ContactId}.",
                requestType, principal.ContactId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError, title: "Internal Server Error",
                detail: "An unexpected error occurred submitting the request.",
                extensions: new Dictionary<string, object?> { ["errorCode"] = "EXT_INTAKE_INTERNAL_ERROR" });
        }
    }

    /// <summary>
    /// The text stored in <c>sprk_intakedata</c>: the client's answers with the SYSTEM keys applied. Any client key named
    /// <see cref="RequesterDepartmentKey"/> (compared case-insensitively, so no reader can be fooled by a differently-cased
    /// copy) is dropped, then the key is added when <paramref name="requesterDepartment"/> has a value. When neither applies
    /// the client's raw text is stored verbatim. Every other property is copied in order, duplicates included.
    /// </summary>
    internal static string WithSystemKeys(JsonElement clientAnswers, string? requesterDepartment)
    {
        var hasDepartment = !string.IsNullOrWhiteSpace(requesterDepartment);
        var hasClientKey = clientAnswers.EnumerateObject()
            .Any(p => string.Equals(p.Name, RequesterDepartmentKey, StringComparison.OrdinalIgnoreCase));
        if (!hasDepartment && !hasClientKey)
        {
            return clientAnswers.GetRawText();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
               }))
        {
            writer.WriteStartObject();
            foreach (var property in clientAnswers.EnumerateObject())
            {
                if (string.Equals(property.Name, RequesterDepartmentKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            if (hasDepartment)
            {
                writer.WriteString(RequesterDepartmentKey, requesterDepartment!.Trim());
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The 400 for the first invalid field, or <c>null</c> when the submission is valid.</summary>
    internal static IResult? Validate(SubmitServiceRequestRequest? request)
    {
        if (request?.RequestType is not { } requestType || !ExternalDataService.ServiceRequestTypes.ContainsKey(requestType))
        {
            return BadRequest(ErrorInvalidRequestType,
                "requestType must be one of 100000000 (NDA Assessment), 100000001 (Invention Submission), " +
                "100000002 (Policy Question) or 100000003 (Trademark Search).");
        }

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > MaxTitleLength)
        {
            return BadRequest(ErrorInvalidTitle, $"title is required and may be at most {MaxTitleLength} characters.");
        }

        if (request.Answers is not { ValueKind: JsonValueKind.Object } answers)
        {
            return BadRequest(ErrorInvalidAnswers, "answers must be a JSON object.");
        }

        if (Encoding.UTF8.GetByteCount(answers.GetRawText()) > MaxAnswersBytes)
        {
            return BadRequest(ErrorInvalidAnswers, $"answers may be at most {MaxAnswersBytes / 1024} KB.");
        }

        return null;
    }

    private static IResult BadRequest(string errorCode, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });
}
