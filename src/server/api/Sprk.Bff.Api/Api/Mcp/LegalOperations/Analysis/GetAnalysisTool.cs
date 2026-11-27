using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Analysis;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 031 (spec FR-06 read half, FR-15, NFR-02, NFR-05, NFR-07; ADR-013, ADR-015, ADR-028).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md). A thin MCP adapter inside the BFF over
// AnalysisEndpoints.ReadAnalysisAsCallerAsync (the route filter's analysis-read rule, then the route handler's read) on the
// /mcp/legal-operations host: no package, no DI registration (found by [McpArea]), no background work, no Services/Ai type.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: GET /api/ai/analysis/{analysisId} (AnalysisAuthorizationFilter's read rule, exposed for non-route hosts as
//     IsAnalysisReadableAsync, then IAnalysisOrchestrationService.GetAnalysisAsync), reached through ReadAnalysisAsCallerAsync.
//   Extension: the rule and the read are reused unchanged; the only new logic is the slim projection and the link.
//   Cost of doing nothing: Copilot users cannot see whether an analysis they started is done, or open it (FR-06 read half fails).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_get_analysis</c> (FR-06): one Spaarke analysis the signed-in caller may read, by id. Read-only, model-visible, no widget.
/// </summary>
/// <remarks>
/// <para><b>One decision, one answer.</b> The tool calls <see cref="AnalysisEndpoints.ReadAnalysisAsCallerAsync"/> with exactly
/// <c>IHttpContextAccessor.HttpContext</c> and copies no check. An analysis the caller may not read, one the external narrowing hides,
/// and one that does not exist give the same <c>isError</c> not-found (<see cref="DocumentToolSupport.NotFound"/>, REST's uniform 404).</para>
/// <para><b>Result.</b> <c>structuredContent</c> is <c>{ analysisId, documentId, actionName, startedOn, summary, link }</c>. ADR-015: of the
/// analysis output, only <c>summary</c> (at most <see cref="SummaryMaxChars"/> characters of the final output, or of the working document
/// when there is none, on one line); no chat history, no token usage.</para>
/// <para><b>No status, no completion time, no document name (verifier fix F1; D-031-2).</b> The route's read, on an uncached analysis,
/// answers <c>Status = "Completed"</c> for EVERY row (a row that was never run included), the analysis's own name as <c>DocumentName</c>
/// and its last-modified time as <c>CompletedOn</c> (<c>AnalysisDocumentLoader.GetOrReloadFromDataverseAsync</c>; a REST contract this
/// task does not change). The tool cannot get the real status instead: nothing in the server writes a status to <c>sprk_analysis</c> after
/// the create (it stays Active), and the repo defines no Draft, In progress, Completed or Failed value for it. So the tool says none of
/// the three, and says so in its text: the model must not tell a user that an analysis is complete. The link opens it, and the Analysis
/// Workspace shows its real state. <c>startedOn</c> is the row's creation time.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetAnalysisTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_get_analysis";

    internal const string AnalysisIdArgument = "analysisId";

    /// <summary>ADR-015 (task 031 constraint): the most analysis output text a result carries.</summary>
    internal const int SummaryMaxChars = 500;

    /// <summary>The longest document or action name a result carries, in characters.</summary>
    internal const int NameMaxChars = 200;

    /// <summary>The <c>reasonCode</c> of a read that faulted (REST: a bare 500): this host's own.</summary>
    internal const string GetFailedReasonCode = "analysis.get_failed";

    internal static McpToolError GetFailed { get; } =
        new(GetFailedReasonCode, "The analysis could not be read. Try again shortly.", StatusCodes.Status500InternalServerError);

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    internal const string Description =
        "Use for: looking up one Spaarke analysis the signed-in user may read, by its id: the document it analyses, the action it ran, "
        + "when it was created, a short summary of its output, and a link that opens it in the Spaarke Analysis Workspace. It does not "
        + "report whether the analysis has run or finished; the link shows that. Needs the analysis id, from spaarke_start_analysis. "
        + "Not for: starting an analysis (use spaarke_start_analysis); the full analysis output (open the link); documents. An analysis "
        + "that does not exist and one the user may not read return the same not-found answer.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [AnalysisIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the analysis, from spaarke_start_analysis.",
            },
        },
        ["required"] = new JsonArray(AnalysisIdArgument),
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
                Title = "Get an analysis",
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

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when an analysis was returned).</summary>
    internal static Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IOptions<DataverseOptions> dataverse,
        TimeProvider time,
        ILogger<GetAnalysisTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, dataverse.Value.EnvironmentUrl, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        string? environmentUrl,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no read. Checked before anything else; AnalysisEndpoints checks it again.
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!DocumentToolSupport.TryReadStringArguments(arguments, ToolName, [AnalysisIdArgument], http, out var values, out var argumentError))
        {
            return argumentError!;
        }

        if (!Guid.TryParse(values[0], out var analysisId) || analysisId == Guid.Empty)
        {
            return DocumentToolSupport.Failure(
                new McpToolError(DocumentToolSupport.InvalidIdReasonCode, $"'{AnalysisIdArgument}' must be the analysis's id (a GUID).", StatusCodes.Status400BadRequest), http);
        }

        AnalysisReadResult read;
        try
        {
            read = await AnalysisEndpoints.ReadAnalysisAsCallerAsync(http, analysisId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ADR-015: the exception type only. A caller who has gone away rethrows.
            logger.LogWarning("MCP tool {ToolName}: the read faulted ({ExceptionType}).", ToolName, ex.GetType().Name);
            return DocumentToolSupport.Failure(GetFailed, http);
        }

        return read switch
        {
            { Outcome: AnalysisReadOutcome.Found, Analysis: { } analysis } => ResultOf(analysis, DecisionsTextSummary.ConsoleBaseUrl(environmentUrl)),
            { Outcome: AnalysisReadOutcome.UserContextRequired } => DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http),
            _ => DocumentToolSupport.Failure(DocumentToolSupport.NotFound, http),
        };
    }

    /// <summary>The success result for the route's analysis: the slim analysis, its text lines and its link. Pure.</summary>
    internal static CallToolResult ResultOf(AnalysisDetailResult analysis, string? consoleBaseUrl)
    {
        // Status, CompletedOn and DocumentName are NOT read: the route's loader fabricates them for an uncached row (class remarks, D-031-2).
        var actionName = Short(analysis.Action?.Name, NameMaxChars);
        var summary = Short(string.IsNullOrWhiteSpace(analysis.FinalOutput) ? analysis.WorkingDocument : analysis.FinalOutput, SummaryMaxChars);
        var body = new GetBody(
            analysis.Id.ToString("D"),
            analysis.DocumentId == Guid.Empty ? null : analysis.DocumentId.ToString("D"),
            actionName,
            Timestamp(analysis.StartedOn),
            summary,
            AnalysisEndpoints.AnalysisWorkspaceLink(consoleBaseUrl, analysis.Id, analysis.DocumentId));

        var text = $"Analysis {body.AnalysisId}. Its status is not reported here: open it to see whether it has run."
                   + (actionName is { } action ? $"\nAction: {action}" : string.Empty)
                   + (summary is { } text1 ? $"\nSummary: {text1}" : string.Empty)
                   + (body.Link is { } link ? $"\nOpen in the Spaarke Analysis Workspace: {link}" : string.Empty);
        return DecisionToolCall.Success(text, body);
    }

    /// <summary>One line of at most <paramref name="maxChars"/> characters; null when empty.</summary>
    private static string? Short(string? value, int maxChars) =>
        string.IsNullOrWhiteSpace(value) ? null : SearchToolSupport.Clip(McpText.SingleLine(value).Trim(), maxChars);

    // Dataverse times are UTC; an unspecified kind is read as UTC, never shifted by the server's zone.
    private static string? Timestamp(DateTime? value) =>
        value is not { } time
            ? null
            : (time.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(time, DateTimeKind.Utc) : time.ToUniversalTime())
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary><c>structuredContent</c> of <c>spaarke_get_analysis</c>: the slim analysis and its link.</summary>
    internal sealed record GetBody(
        string AnalysisId, string? DocumentId, string? ActionName, string? StartedOn, string? Summary, string? Link);
}
