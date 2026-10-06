using Sprk.Bff.Api.Api.Filters;
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
///   - RequireAuthorization() — Azure AD bearer token required (ADR-028 / ADR-008).
///     These endpoints WRITE derived financial fields to Dataverse under the BFF app
///     identity, so they MUST NOT be anonymous. The web-resource caller
///     (sprk_subgrid_parent_rollup.js) acquires a token via @spaarke/auth (MSAL silent SSO).
///   - RequireRateLimiting("dataverse-query") for additional abuse protection
///   - ProblemDetails for error responses (ADR-019)
///
/// <para><b>Per-record authorization (unified-access-control-r2 task 130, defect C8).</b>
/// RequireAuthorization() alone only asks "are you anyone?" — neither the default policy nor the authorization
/// FallbackPolicy (an authenticated user, UAC-r2 task 167) asks anything more.
/// The service reads invoices/budgets APP-ONLY and writes the rollup APP-ONLY, so before this task any
/// signed-in user could read a walled-off matter's spend, budget and 12-month timeline and force a write to
/// it. Each route now carries a FinanceAuthorizationFilter requiring Read on the parent record AS THE
/// CALLER, before any Dataverse read or write. A record the caller cannot read and a record that does not
/// exist return the SAME 404 (<see cref="FinanceAuthorizationFilter.UniformRecordNotFound"/>), and so does a
/// record deleted between the check and the compute. The check is deliberately at the ENDPOINT, never in
/// <see cref="FinanceRollupService"/>, which SpendSnapshotGenerationJobHandler calls with no caller.</para>
/// </remarks>
public static class FinanceRollupEndpoints
{
    public static void MapFinanceRollupEndpoints(this WebApplication app)
    {
        // Matter rollup endpoints
        // Requires authentication via Azure AD bearer token (ADR-028 / ADR-008 compliant) —
        // these endpoints write derived financial fields to Dataverse. Rate limiting
        // provides additional abuse protection.
        var matterGroup = app.MapGroup("/api/finance/matters")
            .WithTags("FinanceRollup")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        matterGroup.MapPost("/{matterId:guid}/recalculate", RecalculateMatterAsync)
            .AddFinanceAuthorizationFilter(
                "finance.read", FinanceAuthorizationFilter.MatterEntitySet, routeKey: "matterId",
                FinanceDenial.UniformNotFound)
            .WithName("RecalculateMatterFinance")
            .WithSummary("Recalculate financial rollup fields for a matter")
            .WithDescription(
                "Queries invoices and budgets linked to the matter, " +
                "computes all denormalized financial fields (total spend, budget utilization, " +
                "velocity, timeline), and writes them back to the Matter record.")
            .Produces<RecalculateFinanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Project rollup endpoints
        // Requires authentication via Azure AD bearer token (ADR-028 / ADR-008 compliant).
        var projectGroup = app.MapGroup("/api/finance/projects")
            .WithTags("FinanceRollup")
            .RequireRateLimiting("dataverse-query")
            .RequireAuthorization();

        projectGroup.MapPost("/{projectId:guid}/recalculate", RecalculateProjectAsync)
            .AddFinanceAuthorizationFilter(
                "finance.read", FinanceAuthorizationFilter.ProjectEntitySet, routeKey: "projectId",
                FinanceDenial.UniformNotFound)
            .WithName("RecalculateProjectFinance")
            .WithSummary("Recalculate financial rollup fields for a project")
            .WithDescription(
                "Queries invoices and budgets linked to the project, " +
                "computes all denormalized financial fields (total spend, budget utilization, " +
                "velocity, timeline), and writes them back to the Project record.")
            .Produces<RecalculateFinanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
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
            // The record vanished between the authorization check and the compute (the no-create write
            // refused to recreate it). Same response as "absent" and "unreadable" — see
            // FinanceAuthorizationFilter.UniformRecordNotFound. Neither the id nor the entity label is echoed.
            return FinanceAuthorizationFilter.UniformRecordNotFound(context);
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
