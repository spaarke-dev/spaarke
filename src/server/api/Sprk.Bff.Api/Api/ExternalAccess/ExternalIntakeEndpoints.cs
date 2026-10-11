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
//   2. The requester: the resolved principal's contact. A caller with none (a licensed systemuser not linked to a contact)
//      is refused 403 with a reason that says so — the row could not be filed in their name, and the Service Requests
//      module (scope sprk_requestedby = caller contact) could never show it to them.
//   3. Validation: requestType ∈ the four sprk_requesttype options; title required (→ sprk_name, ≤ 850); answers a JSON
//      object of ≤ 100 KB (UTF-8) → sprk_intakedata. Anything else is a 400 with an errorCode.
//   4. The create: app-only (no OBO) through ExternalDataService.CreateServiceRequestAsync — sprk_requestedby,
//      sprk_requesttype, sprk_intakedata, sprk_name, sprk_direction = Inbound. The request DTO is closed, so a requester,
//      owner or creator named in the body never binds.

using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// The Legal Front Door intake submit (FR-05/FR-06): <c>POST /api/v1/external/servicerequests</c>. See the file header.
/// </summary>
public static class ExternalIntakeEndpoints
{
    /// <summary>Deny code for a caller not entitled to the legal-front-door module (incl. a CIAM partner, switch off).</summary>
    public const string DenyModuleNotEntitled = "sdap.external.intake.module_not_entitled";

    /// <summary>Deny code for a caller whose principal carries no contact, so no requester can be recorded.</summary>
    public const string DenyNoContact = "sdap.external.intake.no_contact";

    /// <summary>Error code for a request type outside the four <c>sprk_requesttype</c> options (or none).</summary>
    public const string ErrorInvalidRequestType = "EXT_INTAKE_INVALID_REQUEST_TYPE";

    /// <summary>Error code for a missing, blank or over-long title.</summary>
    public const string ErrorInvalidTitle = "EXT_INTAKE_INVALID_TITLE";

    /// <summary>Error code for answers that are missing, not a JSON object, or over <see cref="MaxAnswersBytes"/>.</summary>
    public const string ErrorInvalidAnswers = "EXT_INTAKE_INVALID_ANSWERS";

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
            .WithSummary("Submit a Legal Front Door request as the caller")
            .WithDescription(
                "Creates one sprk_servicerequest from a Legal Front Door wizard: the request type (NDA Assessment, " +
                "Invention Submission, Policy Question or Trademark Search), a title and the answers (a JSON object of at " +
                "most 100 KB). The requester is the caller's contact. 403 unless the caller is entitled to the " +
                "legal-front-door module (CIAM partners only when the partner service-request switch is on) and has a " +
                "contact; 400 for an unknown type, a missing title or invalid answers.")
            // A write path: the same per-user throttle as the message post (task 034).
            .RequireRateLimiting("dataverse-query")
            .Accepts<SubmitServiceRequestRequest>("application/json")
            .Produces<ExternalServiceRequestCreatedDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return externalGroup;
    }

    private static async Task<IResult> SubmitServiceRequestAsync(
        SubmitServiceRequestRequest? request,
        HttpContext httpContext,
        ModuleEntitlementResolver entitlements,
        ExternalDataService dataService,
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

        // 2. The requester is the resolved principal's contact, never client input.
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
        var answersJson = request.Answers!.Value.GetRawText();

        // 4. The create — app-only, the five intake columns.
        try
        {
            var id = await dataService
                .CreateServiceRequestAsync(principal.ContactId, requestType, title, answersJson, ct)
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
