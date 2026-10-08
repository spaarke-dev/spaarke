using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.WorkAssignments;

namespace Sprk.Bff.Api.Api.WorkAssignments;

/// <summary>
/// spaarke-ontology-platform-r1 task 046 (D-21, D-59): the BFF create route for a work assignment, the server write path
/// the Create Work Assignment wizard switches to (WP-3) and the HTTP face of <see cref="WorkAssignmentCreateService"/>.
/// </summary>
/// <remarks>
/// <para><b>Route name is PROVISIONAL</b> pending unified-access-control-r2's choice (requested on issue #1355). It is NOT
/// <c>POST /api/v1/work-assignments</c>: uac-r2 task 166 retired that route and
/// <c>DeadRouteRetirementTests</c> pins it to 404.</para>
/// <para><b>Authorization</b> (census: <c>RouteAuthorizationGuardTests.Ledger.cs</c>, HandlerDecision): sign-in on the
/// route; a caller who cannot be resolved to a Dataverse user gets the single 403 (#1312, D-29) before anything is read;
/// then every decision is the service's, AS THE CALLER through <c>IDataverseUserClient</c> — Create/Append on the table and
/// AppendTo on every record the payload binds (an unappendable and a missing record are the same 404).</para>
/// </remarks>
public static class WorkAssignmentEndpoints
{
    /// <summary>The single 403 for a caller with no resolvable Dataverse user (the #1312 code, as on the event routes).</summary>
    internal const string CallerUnresolvedReasonCode = "sdap.access.deny.caller_unresolved";

    /// <summary>Registers the work-assignment create route.</summary>
    public static void MapWorkAssignmentCreateEndpoints(this WebApplication app)
    {
        app.MapPost("/api/v1/record-creation/workassignment", CreateAsync)
            .WithTags("Work Assignments")
            .WithName("CreateWorkAssignment")
            .WithSummary("Create a work assignment through the server write path")
            .WithDescription("Takes the Dataverse Web API payload the Create Work Assignment wizard builds. Checks AS THE " +
                "CALLER that they could create it (Create/Append on the table, AppendTo on every record it binds, no owner or " +
                "field-secured column); the application then creates it owned by the resolver's team with the caller as its " +
                "creator person, and a work assignment filed under a secure matter or project is created secure.")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization()
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    /// <summary>POST /api/v1/record-creation/workassignment.</summary>
    internal static async Task<IResult> CreateAsync(
        [FromBody] JsonElement body,
        [FromServices] ICallerSystemUserResolver callerResolver,
        [FromServices] WorkAssignmentCreateService creator,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        CallerSystemUserResolution caller;
        try
        {
            caller = await callerResolver.ResolveAsync(httpContext.User, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[WORK-ASSIGNMENT] the caller's systemuser lookup threw; refusing (fail closed)");
            caller = CallerSystemUserResolution.Unresolved("lookup-threw");
        }

        if (!caller.IsResolved)
        {
            logger.LogWarning("[WORK-ASSIGNMENT] create refused: the caller has no resolvable Dataverse user ({Reason})",
                caller.UnresolvedReason);
            return Results.Problem(
                title: "Forbidden",
                detail: "Your account could not be matched to a Dataverse user, so the work assignment was not created.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = CallerUnresolvedReasonCode,
                    ["correlationId"] = httpContext.TraceIdentifier,
                });
        }

        var result = await creator.CreateAsync(body, CallerObjectId(httpContext), httpContext.TraceIdentifier, ct)
            .ConfigureAwait(false);

        if (result.Id is { } id)
            return Results.Created($"/api/v1/record-creation/workassignment/{id:D}", new { id, isolated = result.Isolated, warnings = result.Warnings });

        var failure = result.Failure!;
        logger.LogWarning("[WORK-ASSIGNMENT] create refused: {Kind} {Code}", failure.Kind, failure.Code);
        var status = failure.Kind switch
        {
            WorkAssignmentCreateFailureKind.InvalidPayload => StatusCodes.Status400BadRequest,
            WorkAssignmentCreateFailureKind.ParentUnavailable => StatusCodes.Status404NotFound,
            WorkAssignmentCreateFailureKind.Denied => StatusCodes.Status403Forbidden,
            WorkAssignmentCreateFailureKind.SecureFilingRefused => StatusCodes.Status403Forbidden,
            WorkAssignmentCreateFailureKind.OwnerRefused => StatusCodes.Status409Conflict,
            WorkAssignmentCreateFailureKind.CallerFailure => failure.StatusCode ?? StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(
            statusCode: status,
            title: status switch
            {
                StatusCodes.Status400BadRequest => "Invalid request",
                StatusCodes.Status403Forbidden => "Not permitted",
                StatusCodes.Status404NotFound => "Not found",
                _ => "Work assignment not created",
            },
            detail: failure.Detail,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = failure.Code,
                ["correlationId"] = httpContext.TraceIdentifier,
            });
    }

    private static Guid? CallerObjectId(HttpContext httpContext) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(httpContext.User), out var oid) && oid != Guid.Empty ? oid : null;
}
