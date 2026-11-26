using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Finance;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 033 (spec FR-11; ebilling-r1 reviews).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over IFinanceSummaryService, the service GET /api/finance/matters/{matterId}/summary already calls, on the
// /mcp/legal-operations host: no package, no DI registration (the catalog finds the class by [McpArea]), no background work, no
// AI-internal type (ADR-013).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: FinanceSummaryService.GetSummaryAsync behind the REST summary route; FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync
//     (task 008, CAPABILITY-INDEX row 52), the one statement of that route's rule (step 0, then finance.read on the matter).
//   Extension: both are reused unchanged; the tool adds no authorization check. The only new logic is the argument read, the mapping of
//     each refusal to an isError result with fixed text, and the slim projection of the summary.
//   Cost of doing nothing: Copilot users cannot ask for a matter's finance position (FR-11 fails).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_get_matter_finance</c> (FR-11): one matter's finance position (current spend, budget, variance, velocity and the active
/// spend signals) for a caller whom the REST summary route admits. Read-only, model-visible, no widget. The tool offers no invoice
/// workflow: it reads one summary and nothing else (EB-D-194).
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The tool calls <see cref="FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync"/> with exactly
/// <c>IHttpContextAccessor.HttpContext</c> and adds no check of its own: step 0 (internal callers only) and <c>finance.read</c> on the
/// matter, as the caller, the rule the REST route runs. A refusal is an <c>isError</c> result carrying the <c>reasonCode</c> the route's
/// 403 carries (ADR-019), with fixed text; the summary service is not called. A caller with no systemuser row gets the route's
/// <c>sdap.access.deny.internal_callers_only</c>, not <c>UserContextRequired</c> (owner decision 2026-11-26, ADR-028 A10 Path A); a caller
/// without Read on the matter gets 403 <c>insufficient_rights</c>, as REST does, not a 404.</para>
/// <para><b>Token.</b> The route admits only an ADR-028 A10 token (delegated <c>user_impersonation</c>, the BFF audience, a user):
/// <see cref="McpToolAuthorization.A10DenyReason"/> runs in the route's filter before this tool is reached, and the tool's bare
/// <c>[Authorize]</c> is the SDK's per-tool check, so the bearer the shared method reads is always an A10 bearer.</para>
/// <para><b>Service.</b> <see cref="IFinanceSummaryService.GetSummaryAsync"/> is called directly after the method allows. The REST handler is
/// not reused: it is a private method of the route, and the route's pipeline items (the step-0 verdict item) are not set for a tool.</para>
/// <para><b>Result (ADR-015).</b> <c>structuredContent</c> is totals and period figures only: <c>{ matterId, currentSpend, budget,
/// budgetVariance, velocityPct?, activeSignals[{ signalType, severity, generatedAt }], generatedAt }</c>. The signal message and the
/// recent-invoice rows (numbers, vendors) of the REST body are not model-visible. The figures are the REST body's. <c>content</c> is a
/// short text of the same totals. Logs carry the tool name, outcome or reason code, timing and correlation id, never an amount.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetMatterFinanceTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_get_matter_finance";

    internal const string MatterIdArgument = "matterId";

    /// <summary>Most signals a result carries (the REST body is unbounded; the model never needs more, and the result stays far under 64 KiB).</summary>
    internal const int MaxSignals = 20;

    /// <summary><c>reasonCode</c> of a matter with no financial data (REST: a 404 ProblemDetails with no <c>reasonCode</c>).</summary>
    internal const string NotFoundReasonCode = "finance.summary.not_found";

    /// <summary><c>reasonCode</c> of a service fault (REST: a 500 ProblemDetails with no <c>reasonCode</c>).</summary>
    internal const string UnavailableReasonCode = "finance.summary.unavailable";

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    internal const string Description =
        "Use for: the finance position of one matter in Spaarke: its current spend, budget and budget variance, the month-over-month "
        + "spend velocity, and the active spend signals (budget exceeded, budget warning, velocity spike) with their severity. Needs the "
        + "matter's id. Available to internal Spaarke users who can read the matter. "
        + "Not for: invoices, invoice review or approval, or searching invoices; matters across a portfolio (use spaarke_get_portfolio); "
        + "changing anything. A user who may not read the matter's finance information is refused with a reason.";

    private static readonly McpToolError InternalCallersOnly = new(
        FinanceAuthorizationFilter.InternalCallersOnlyReasonCode,
        "Matter finance is available to internal Spaarke users only.",
        StatusCodes.Status403Forbidden);

    private static readonly McpToolError InsufficientRights = new(
        FinanceAuthorizationFilter.InsufficientRightsReasonCode,
        "You do not have access to this matter's finance information.",
        StatusCodes.Status403Forbidden);

    private static readonly McpToolError NoTarget = new(
        FinanceAuthorizationFilter.NoTargetReasonCode,
        $"'{MatterIdArgument}' must be the id of a matter.",
        StatusCodes.Status403Forbidden);

    private static readonly McpToolError SystemFailure = new(
        FinanceAuthorizationFilter.SystemFailureReasonCode,
        "Access to this matter's finance information could not be checked right now. Try again shortly.",
        StatusCodes.Status403Forbidden);

    private static readonly McpToolError NotFound = new(
        NotFoundReasonCode,
        "No financial data exists for this matter.",
        StatusCodes.Status404NotFound);

    private static readonly McpToolError Unavailable = new(
        UnavailableReasonCode,
        "The matter's finance summary could not be read. Try again shortly.",
        StatusCodes.Status500InternalServerError);

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [MatterIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the matter.",
            },
        },
        ["required"] = new JsonArray(MatterIdArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            GetAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Get a matter's finance position",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when a summary was returned).</summary>
    internal static Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IFinanceSummaryService summaries,
        TimeProvider time,
        ILogger<GetMatterFinanceTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, summaries, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        IFinanceSummaryService summaries,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no read. The test every tool applies, before anything else; the shared method makes it again (and
        // refuses a principal with no Entra oid).
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!DocumentToolSupport.TryReadStringArguments(arguments, ToolName, [MatterIdArgument], http, out var values, out var argumentError))
        {
            return argumentError!;
        }

        // Only a value that is not a GUID is refused here. The empty GUID goes to the shared method, which refuses it as the route does (no_target).
        if (!Guid.TryParse(values[0], out var matterId))
        {
            return DocumentToolSupport.Failure(
                new McpToolError(DocumentToolSupport.InvalidIdReasonCode, $"'{MatterIdArgument}' must be the matter's id (a GUID).", StatusCodes.Status400BadRequest), http);
        }

        // The whole authorization: THE rule of the REST summary route (task 008). The tool adds nothing and copies nothing.
        var authorization = await FinanceAuthorizationFilter.AuthorizeMatterFinanceReadAsync(http, matterId, cancellationToken);
        if (!authorization.IsAllowed)
        {
            return DocumentToolSupport.Failure(RefusalOf(authorization), http);
        }

        FinanceSummaryDto? summary;
        try
        {
            summary = await summaries.GetSummaryAsync(matterId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // The exception type only (ADR-015: a Dataverse message can carry ids and amounts). A caller who has gone away rethrows.
            logger.LogError(
                "MCP tool {ToolName}: the finance summary read failed ({ExceptionType}). CorrelationId={CorrelationId}",
                ToolName, ex.GetType().Name, http!.TraceIdentifier);
            return DocumentToolSupport.Failure(Unavailable, http);
        }

        return summary is null
            ? DocumentToolSupport.Failure(NotFound, http)
            : ResultOf(summary);
    }

    /// <summary>
    /// The error for a refusal of the shared method: the route's <c>reasonCode</c> with fixed text and the route's status. A code this map
    /// does not know (none exists today) is refused as <see cref="FinanceAuthorizationFilter.SystemFailureReasonCode"/>, never allowed.
    /// </summary>
    internal static McpToolError RefusalOf(MatterFinanceReadAuthorization authorization)
    {
        if (authorization.Outcome == MatterFinanceReadOutcome.UserContextRequired)
        {
            return DocumentToolSupport.UserContextRequired;
        }

        return authorization.ReasonCode switch
        {
            FinanceAuthorizationFilter.InternalCallersOnlyReasonCode => InternalCallersOnly,
            FinanceAuthorizationFilter.InsufficientRightsReasonCode => InsufficientRights,
            FinanceAuthorizationFilter.NoTargetReasonCode => NoTarget,
            _ => SystemFailure,
        };
    }

    /// <summary>The success result for the service's summary: the slim body and its text of totals. Pure.</summary>
    internal static CallToolResult ResultOf(FinanceSummaryDto summary)
    {
        var body = new FinanceBody(
            summary.MatterId, summary.CurrentSpend, summary.Budget, summary.BudgetVariance, summary.VelocityPct,
            summary.ActiveSignals.Take(MaxSignals).Select(s => new SignalRow(s.SignalType, s.Severity, s.GeneratedAt)).ToList(),
            summary.GeneratedAt);
        return DecisionToolCall.Success(BuildText(summary), body);
    }

    /// <summary>Lines of totals and counts only (ADR-015). Invariant culture, so the text does not depend on the host's locale.</summary>
    internal static string BuildText(FinanceSummaryDto s)
    {
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string>();

        // FinanceSummaryService reports a matter with no budget record as Budget 0 and BudgetVariance 0, so that pair is "no budget", not
        // "under budget". structuredContent keeps the raw figures (REST parity) and carries no label.
        if (s.Budget == 0m && s.BudgetVariance == 0m)
        {
            lines.Add($"Current spend: {s.CurrentSpend.ToString("N2", inv)}");
            lines.Add("No budget is recorded for this matter.");
        }
        else
        {
            var position = s.BudgetVariance > 0m ? "under" : s.BudgetVariance < 0m ? "over" : "on";
            lines.Add($"Current spend: {s.CurrentSpend.ToString("N2", inv)} of budget {s.Budget.ToString("N2", inv)}");
            lines.Add($"Budget variance: {s.BudgetVariance.ToString("N2", inv)} ({position} budget)");
        }

        if (s.VelocityPct is { } velocity)
        {
            lines.Add($"Spend velocity: {velocity.ToString("0.##", inv)}% month over month");
        }

        lines.Add($"Active spend signals: {s.ActiveSignals.Count.ToString(inv)}");
        return string.Join("\n", lines);
    }

    /// <summary><c>structuredContent</c> of <c>spaarke_get_matter_finance</c>: the REST summary's figures, without its narrative or invoice rows.</summary>
    internal sealed record FinanceBody(
        Guid MatterId, decimal CurrentSpend, decimal Budget, decimal BudgetVariance, decimal? VelocityPct,
        IReadOnlyList<SignalRow> ActiveSignals, DateTime GeneratedAt);

    /// <summary>One active spend signal: its type and severity (fixed vocabularies) and when it was raised; no message text.</summary>
    internal sealed record SignalRow(string SignalType, string Severity, DateTime GeneratedAt);
}
