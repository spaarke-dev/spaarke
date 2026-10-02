using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// API endpoints for matter and project performance scorecard calculation.
/// Provides on-demand recalculation of KPI grades for matters and projects.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API pattern (no controllers).
/// Follows ADR-008: Endpoint filters for authorization.
/// Follows ADR-019: ProblemDetails for error responses.
///
/// <para><b>Per-record authorization (unified-access-control-r2 task 130, defect C8).</b> These routes had
/// the same shape as FinanceRollupEndpoints (which was copied from this file): a bare RequireAuthorization()
/// in front of an APP-ONLY read of the parent's KPI assessments and an APP-ONLY write of six grade fields, so
/// any signed-in user could recalculate — and read the grades of — any matter or project. Each route now
/// carries a FinanceAuthorizationFilter requiring Read on the parent record AS THE CALLER before any
/// Dataverse call, and denies with the uniform 404 shared with the finance recalculate routes, so the status
/// cannot be used to probe for record existence.</para>
/// </remarks>
public static class ScorecardCalculatorEndpoints
{
    /// <summary>
    /// Registers scorecard calculator endpoints with the application.
    /// </summary>
    public static void MapScorecardCalculatorEndpoints(this WebApplication app)
    {
        // Matter scorecard endpoints
        // Requires authentication via Azure AD bearer token (ADR-008 compliant).
        // Rate limiting provides additional abuse protection.
        var matterGroup = app.MapGroup("/api/matters")
            .WithTags("Scorecard")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        matterGroup.MapPost("/{matterId:guid}/recalculate-grades", RecalculateMatterGradesAsync)
            .AddFinanceAuthorizationFilter(
                "finance.read", FinanceAuthorizationFilter.MatterEntitySet, routeKey: "matterId",
                FinanceDenial.UniformNotFound)
            .WithName("RecalculateMatterGrades")
            .WithSummary("Recalculate performance scorecard grades for a matter")
            .WithDescription(
                "Queries KPI assessments linked to the matter, " +
                "then computes current grades, rolling averages, and historical trends for each KPI dimension.")
            .Produces<RecalculateGradesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Project scorecard endpoints
        // Requires authentication via Azure AD bearer token (ADR-008 compliant).
        var projectGroup = app.MapGroup("/api/projects")
            .WithTags("Scorecard")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        projectGroup.MapPost("/{projectId:guid}/recalculate-grades", RecalculateProjectGradesAsync)
            .AddFinanceAuthorizationFilter(
                "finance.read", FinanceAuthorizationFilter.ProjectEntitySet, routeKey: "projectId",
                FinanceDenial.UniformNotFound)
            .WithName("RecalculateProjectGrades")
            .WithSummary("Recalculate performance scorecard grades for a project")
            .WithDescription(
                "Queries KPI assessments linked to the project, " +
                "then computes current grades, rolling averages, and historical trends for each KPI dimension.")
            .Produces<RecalculateGradesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    private static async Task<IResult> RecalculateMatterGradesAsync(
        Guid matterId,
        ScorecardCalculatorService scorecardCalculatorService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        return await RecalculateGradesAsync(
            matterId, "matter", scorecardCalculatorService,
            (svc, id, token) => svc.RecalculateGradesAsync(id, token),
            logger, context, ct);
    }

    private static async Task<IResult> RecalculateProjectGradesAsync(
        Guid projectId,
        ScorecardCalculatorService scorecardCalculatorService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        return await RecalculateGradesAsync(
            projectId, "project", scorecardCalculatorService,
            (svc, id, token) => svc.RecalculateProjectGradesAsync(id, token),
            logger, context, ct);
    }

    private static async Task<IResult> RecalculateGradesAsync(
        Guid entityId,
        string entityLabel,
        ScorecardCalculatorService scorecardCalculatorService,
        Func<ScorecardCalculatorService, Guid, CancellationToken, Task<RecalculateGradesResponse>> calculate,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Recalculating scorecard grades. Entity={Entity}, Id={EntityId}, CorrelationId={CorrelationId}",
            entityLabel, entityId, traceId);

        try
        {
            var response = await calculate(scorecardCalculatorService, entityId, ct);

            logger.LogDebug(
                "Scorecard grades calculated. Entity={Entity}, Id={EntityId}, " +
                "GuidelineCurrent={GuidelineCurrent}, BudgetCurrent={BudgetCurrent}, OutcomeCurrent={OutcomeCurrent}",
                entityLabel, entityId, response.GuidelineCurrent, response.BudgetCurrent, response.OutcomeCurrent);

            return TypedResults.Ok(response);
        }
        catch (KeyNotFoundException)
        {
            // The record vanished between the authorization check and the compute (the no-create write
            // refused to recreate it). Same response as "absent" and "unreadable" — see
            // FinanceAuthorizationFilter.UniformRecordNotFound. Neither the id nor the entity label is echoed.
            return FinanceAuthorizationFilter.UniformRecordNotFound(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error recalculating scorecard grades. Entity={Entity}, Id={EntityId}, CorrelationId={CorrelationId}",
                entityLabel, entityId, traceId);

            return Results.Problem(
                detail: "An error occurred while recalculating scorecard grades",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = traceId });
        }
    }
}
