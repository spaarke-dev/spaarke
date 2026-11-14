using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

/// <summary>
/// <c>spaarke_search_decision_lookup</c> (spaarkeai-mcp-bff-api-r1 task 023, FR-07; design §7.1): the app-only, read-only search the
/// decision wizard's type-ahead runs for a Person or Lookup parameter. It is <see cref="IDecisionLookupSearch"/> as the caller, the call
/// <c>GET /api/v1/signals/{id}/lookup</c> makes: the decision plan, not the caller, names the table and its scope (a budget must belong
/// to the Signal's matter, D-18), and a row the caller cannot read never appears.
/// </summary>
/// <remarks>
/// <para><b>Read-only on purpose.</b> It is marked <c>readOnly</c> so typing in the wizard does not raise a host consent prompt per
/// keystroke (design §7.1).</para>
/// <para><b>One caller (FR-10, option C).</b> The tool passes exactly <see cref="IHttpContextAccessor.HttpContext"/> to the search; a null
/// context or an unauthenticated caller is <c>UserContextRequired</c> before the search is called. Every domain failure is an
/// <c>isError</c> result through <see cref="McpToolResults.FromError"/> with the REST route's <c>reasonCode</c> (ADR-019), e.g. a
/// parameter the plan does not offer is <c>ontology.decision.invalid_parameter</c>.</para>
/// <para><b>Placement and component justification (CLAUDE.md sections 10, 11).</b> A thin adapter inside the BFF over the lookup service
/// the REST route also calls; no package, no AI-internal type, no DI registration. Existing = the REST lookup route; extension = this is
/// a second host over the same service; cost of doing nothing = six action types cannot resolve a person, budget or firm from Copilot.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class SearchDecisionLookupTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_search_decision_lookup";

    public override McpServerTool Create(IServiceProvider services) =>
        McpServerTool.Create(
            HandleAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Search decision lookup",
                ReadOnly = true,
                Meta = PlanAndLookupToolSupport.AppOnlyMeta(),
                Description =
                    "Use for: finding the person, budget or firm to pick for a parameter of a Work Item's decision plan while the Spaarke decision "
                    + "wizard is open. Returns at most 25 candidates (id, name and, for a person, email) read as the signed-in user, from the "
                    + "table and scope the item's decision plan names, so a budget of another matter or a record the user cannot read never "
                    + "appears. Not for: general record search, listing Work Items (use spaarke_show_decisions) or recording a decision "
                    + "(use spaarke_complete_decision). Called by the Spaarke decisions widget, not by the model.",
            });

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows: the candidate count).</summary>
    internal static Task<CallToolResult> HandleAsync(
        [Description("The id (GUID) of the Work Item whose decision plan names the parameter.")] Guid signalId,
        IHttpContextAccessor accessor,
        IDecisionLookupSearch search,
        TimeProvider time,
        ILogger<SearchDecisionLookupTool> logger,
        CancellationToken cancellationToken,
        [Description("The code of a person or lookup parameter of an action in the item's decision plan.")] string? parameterCode = null,
        [Description("The text typed so far; at most 100 characters.")] string? query = null) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(signalId, accessor, search, cancellationToken, parameterCode, query),
            result => result.StructuredContent is { ValueKind: JsonValueKind.Object } body
                      && body.TryGetProperty("candidates", out var candidates)
                      && candidates.ValueKind == JsonValueKind.Array
                ? candidates.GetArrayLength()
                : 0);

    internal static async Task<CallToolResult> RunAsync(
        Guid signalId,
        IHttpContextAccessor accessor,
        IDecisionLookupSearch search,
        CancellationToken cancellationToken,
        string? parameterCode = null,
        string? query = null)
    {
        var http = accessor.HttpContext;
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return McpToolResults.FromError(DecisionResultMapper.ToToolError(DecisionLookupResult.UserContextRequired)!, http?.TraceIdentifier);
        }

        var result = await search.SearchAsync(http, signalId, parameterCode, query, cancellationToken);
        var error = DecisionResultMapper.ToToolError(result);
        if (error != null)
        {
            return McpToolResults.FromError(error, http!.TraceIdentifier);
        }

        // The REST body unchanged; the summary is a count only (ADR-015).
        return PlanAndLookupToolSupport.Success($"{result.Response!.Candidates.Count} candidate(s) found.", result.Response);
    }
}
