using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// API endpoints for recalculating denormalized financial fields on Matter/Project records.
/// Called by the subgrid parent rollup web resource (sprk_subgrid_parent_rollup.js)
/// when Invoices or Budgets are added/changed via the form.
/// </summary>
/// <remarks>
/// Follows ScorecardCalculatorEndpoints pattern exactly:
///   - AllowAnonymous (web resources cannot acquire Azure AD tokens)
///   - RequireRateLimiting("dataverse-query") for abuse protection
///   - ProblemDetails for error responses (ADR-019)
/// </remarks>
public static class FinanceRollupEndpoints
{
    public static void MapFinanceRollupEndpoints(this WebApplication app)
    {
        // Matter rollup endpoints
        // NOTE: AllowAnonymous because Dataverse web resources cannot acquire
        // Azure AD tokens for the BFF API. Rate limiting provides abuse protection.
        var matterGroup = app.MapGroup("/api/finance/matters")
            .WithTags("FinanceRollup")
            .RequireRateLimiting("dataverse-query");

        matterGroup.MapPost("/{matterId:guid}/recalculate", RecalculateMatterAsync)
            .AllowAnonymous()
            .WithName("RecalculateMatterFinance")
            .WithSummary("Recalculate financial rollup fields for a matter")
            .WithDescription(
                "Queries invoices and budgets linked to the matter, " +
                "computes all denormalized financial fields (total spend, budget utilization, " +
                "velocity, timeline), and writes them back to the Matter record.")
            .Produces<RecalculateFinanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Project rollup endpoints
        var projectGroup = app.MapGroup("/api/finance/projects")
            .WithTags("FinanceRollup")
            .RequireRateLimiting("dataverse-query");

        projectGroup.MapPost("/{projectId:guid}/recalculate", RecalculateProjectAsync)
            .AllowAnonymous()
            .WithName("RecalculateProjectFinance")
            .WithSummary("Recalculate financial rollup fields for a project")
            .WithDescription(
                "Queries invoices and budgets linked to the project, " +
                "computes all denormalized financial fields (total spend, budget utilization, " +
                "velocity, timeline), and writes them back to the Project record.")
            .Produces<RecalculateFinanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    private static async Task<IResult> RecalculateMatterAsync(
        Guid matterId,
        FinanceRollupService financeRollupService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        return await RecalculateAsync(
            matterId, "matter", financeRollupService,
            (svc, id, token) => svc.RecalculateMatterAsync(id, token),
            logger, context, ct);
    }

    private static async Task<IResult> RecalculateProjectAsync(
        Guid projectId,
        FinanceRollupService financeRollupService,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        return await RecalculateAsync(
            projectId, "project", financeRollupService,
            (svc, id, token) => svc.RecalculateProjectAsync(id, token),
            logger, context, ct);
    }

    private static async Task<IResult> RecalculateAsync(
        Guid entityId,
        string entityLabel,
        FinanceRollupService financeRollupService,
        Func<FinanceRollupService, Guid, CancellationToken, Task<RecalculateFinanceResponse>> calculate,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        var traceId = context.TraceIdentifier;

        logger.LogInformation(
            "Recalculating finance rollup. Entity={Entity}, Id={EntityId}, CorrelationId={CorrelationId}",
            entityLabel, entityId, traceId);

        try
        {
            var response = await calculate(financeRollupService, entityId, ct);

            logger.LogDebug(
                "Finance rollup calculated. Entity={Entity}, Id={EntityId}, " +
                "TotalSpend={TotalSpend:C}, Invoices={InvoiceCount}, Utilization={Utilization:F1}%",
                entityLabel, entityId, response.TotalSpendToDate, response.InvoiceCount,
                response.BudgetUtilizationPercent);

            return TypedResults.Ok(response);
        }
        catch (KeyNotFoundException)
        {
            return Results.Problem(
                detail: $"{entityLabel} with ID '{entityId}' was not found.",
                statusCode: StatusCodes.Status404NotFound,
                title: $"{entityLabel} Not Found",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                extensions: new Dictionary<string, object?> { ["correlationId"] = traceId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Error recalculating finance rollup. Entity={Entity}, Id={EntityId}, CorrelationId={CorrelationId}",
                entityLabel, entityId, traceId);

            return Results.Problem(
                detail: "An error occurred while recalculating financial fields",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                extensions: new Dictionary<string, object?> { ["correlationId"] = traceId });
        }
    }
}
