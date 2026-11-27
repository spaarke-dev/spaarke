using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Workspace;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Portfolio;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 026 (spec FR-08).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over PortfolioService, the service GET /api/workspace/portfolio already calls, on the /mcp/legal-operations
// host: no package, no DI registration (the catalog finds the class by [McpArea]), no background work, no AI-internal type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/workspace/portfolio (Api/Workspace/WorkspaceEndpoints.cs) over PortfolioService.GetPortfolioSummaryAsync.
//   Extension: this IS the extension: PortfolioService is reused unchanged, and the caller id comes from the same CallerResolution call
//     the REST route's WorkspaceAuthorizationFilter makes; the only new logic is the five-line text summary.
//   Cost of doing nothing: Copilot users cannot ask for their matter portfolio summary (FR-08 fails).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_get_portfolio</c> (FR-08): the signed-in user's matter portfolio summary (spend, budget, utilization, matters at risk,
/// overdue events, active matters) for the model. Read-only, model-visible, no input, no widget.
/// </summary>
/// <remarks>
/// <para><b>Same answer as REST.</b> <c>structuredContent</c> is the <see cref="PortfolioSummaryResponse"/> that
/// <c>GET /api/workspace/portfolio</c> returns for the same caller, serialized with the same (web, camelCase) options; <c>content</c> is a
/// short text of the counts and totals (ADR-015: numbers only, no matter names or other record text).</para>
/// <para><b>Caller (ADR-028, FR-10).</b> The route's access check is not an endpoint-filter policy over a resource: the REST route's
/// <c>WorkspaceAuthorizationFilter</c> only resolves the caller's Entra object id (<see cref="CallerResolution.ResolveObjectId"/>) and
/// answers 401 when there is none; matter-level access is enforced inside <see cref="PortfolioService"/> (people-targeted set, read as the
/// caller). The tool makes the same <see cref="CallerResolution.ResolveObjectId"/> call on the request's principal and answers
/// <c>UserContextRequired</c> (401) when there is no signed-in caller, so nothing falls back to an app-only read and no authorization
/// logic is copied.</para>
/// <para><b>Failures.</b> A service fault is an <c>isError</c> result (<c>workspace.portfolio.unavailable</c>, status 500: the REST route
/// answers a ProblemDetails 500 with no <c>reasonCode</c>), never a transport error. The service's own graceful empty state (an
/// unresolvable caller or a failed read) is the same empty portfolio on both hosts.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetPortfolioTool : McpToolDefinition
{
    public const string Name = "spaarke_get_portfolio";

    /// <summary><c>reasonCode</c> of a call that carries any argument (the tool takes none).</summary>
    public const string InvalidParameterReason = "workspace.portfolio.invalid_parameter";

    /// <summary><c>reasonCode</c> of a service fault (REST: ProblemDetails 500).</summary>
    public const string UnavailableReason = "workspace.portfolio.unavailable";

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    public const string Description =
        "Use for: a summary of the signed-in user's matter portfolio in Spaarke: total spend, total budget, budget utilization percent, "
        + "how many active matters there are, how many are at risk (an overdue open task, or spend above 85 percent of budget) and how many "
        + "tasks are overdue across them. Covers the matters the user created, owns or is assigned to (of those, only the ones they can open), not every matter they can open. Takes no arguments. "
        + "Not for: the details of one matter, its invoices or its budget lines; listing or searching matters; the Decide lane (use "
        + "spaarke_show_decisions); changing anything.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject(),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            GetAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "Get my matter portfolio",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred: no properties, nothing else. RunAsync refuses any argument, so the schema is true.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    private static Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        PortfolioService portfolio,
        TimeProvider time,
        ILogger<GetPortfolioTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, Name, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, portfolio, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        PortfolioService portfolio,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var correlationId = http?.TraceIdentifier;

        if (arguments is { Count: > 0 })
        {
            return McpToolResults.FromError(
                new McpToolError(InvalidParameterReason, $"{Name} takes no arguments. Send the call again with no arguments.", StatusCodes.Status400BadRequest),
                correlationId);
        }

        // FR-10 / ADR-028: the REST route's whole access check, made on the request's principal. No caller, no read, no app-only fallback.
        var userId = http is null ? null : CallerResolution.ResolveObjectId(http.User);
        if (http == null || http.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(userId))
        {
            return McpToolResults.FromError(
                new McpToolError(DataverseUserClientErrorCodes.UserContextRequired, "The request has no signed-in caller.", StatusCodes.Status401Unauthorized),
                correlationId);
        }

        PortfolioSummaryResponse summary;
        try
        {
            summary = await portfolio.GetPortfolioSummaryAsync(userId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The same fault the REST route answers with a 500 (it logs the exception and returns fixed text). Nothing of the exception reaches the model.
            logger.LogError(ex, "Portfolio aggregation failed for the MCP tool. CorrelationId={CorrelationId}", correlationId);
            return McpToolResults.FromError(
                new McpToolError(UnavailableReason, "The portfolio could not be read. Try again shortly.", StatusCodes.Status500InternalServerError),
                correlationId);
        }

        return DecisionToolCall.Success(BuildText(summary), summary);
    }

    /// <summary>A five-line text of counts and totals only (ADR-015: no matter names, no record text). Invariant culture, so the text does not depend on the host's locale.</summary>
    internal static string BuildText(PortfolioSummaryResponse s)
    {
        var inv = CultureInfo.InvariantCulture;
        return string.Join(
            "\n",
            $"Active matters: {s.ActiveMatters.ToString(inv)}",
            $"Matters at risk: {s.MattersAtRisk.ToString(inv)}",
            $"Overdue tasks: {s.OverdueEvents.ToString(inv)}",
            $"Total spend: {s.TotalSpend.ToString("N2", inv)} of budget {s.TotalBudget.ToString("N2", inv)}",
            $"Budget utilization: {s.UtilizationPercent.ToString("0.##", inv)}%");
    }
}
