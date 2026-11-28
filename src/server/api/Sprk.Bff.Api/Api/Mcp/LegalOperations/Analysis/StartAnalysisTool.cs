using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Analysis;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 031 (spec FR-06, NFR-05, NFR-06, NFR-07; ADR-002, ADR-013, ADR-015, ADR-028).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md; spec "Placement & New Components"). A thin MCP
// adapter inside the BFF over IAnalysisStartAi (task 037a; the route's create decision and the route's one create, formerly
// AnalysisEndpoints.StartAnalysisAsCallerAsync) on the /mcp/legal-operations host: no package, no DI registration (found by [McpArea]),
// no background work, no Services/Ai type outside PublicContracts (the Analysis Workspace link comes from
// AnalysisEndpoints.AnalysisWorkspaceLink, ADR-013 Path C).
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: POST /api/ai/analysis/create (AnalysisAuthorizationFilter's create decision, then RecordCreationService.CreateAsync), both
//     shared with this tool through IAnalysisStartAi (AnalysisStartService); McpIdempotency, the retry key every model-callable write
//     uses (ITenantCache, IdempotencyFilter's store and lock pattern).
//   Extension: the decision and the create are reused unchanged; this tool adds only the argument check, the retry entry's shape and the
//     slim result.
//   Cost of doing nothing: Copilot users cannot start an analysis on a document (FR-06 fails).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_start_analysis</c> (FR-06): creates an analysis on a document for the signed-in caller, through the same decision and the
/// same create <c>POST /api/ai/analysis/create</c> uses, and returns its id and a link into the Analysis Workspace. It does NOT run the
/// analysis. Model-callable reversible write (NFR-06): not destructive, idempotent with a required <c>idempotencyKey</c>.
/// </summary>
/// <remarks>
/// <para><b>One decision, one create.</b> The tool calls <see cref="IAnalysisStartAi.StartAsCallerAsync"/> with exactly
/// <c>IHttpContextAccessor.HttpContext</c> and copies no check: the caller's Create privilege on <c>sprk_analysis</c>, Read and AppendTo
/// on the document, and use of the playbook are the route filter's decision; the row is the route's one create. A refusal is an
/// <c>isError</c> result with the route's <c>reasonCode</c> and status; nothing is written.</para>
/// <para><b>Retry key.</b> <see cref="McpIdempotency"/> owns it: per caller (oid) and key, the first created analysis is kept for 24 hours
/// with a fingerprint of the inputs (the same key with the same inputs returns that analysis again without a second create; the same key
/// with different inputs is refused; a start still in progress under the key is refused with "try again shortly"). Only a created analysis
/// is kept, so a refused start can be retried with the same key. The limits (D-031-1, #3231: the lock is not atomic; a cache fault fails
/// open) are stated there.</para>
/// <para><b>Result.</b> <c>structuredContent</c> is <c>{ analysisId, documentId, name, link, replayed, warningCount }</c> (ADR-015: no
/// record text beyond the name the caller gave; a replay carries no <c>warningCount</c>, the first call's count is not kept). The analysis
/// is created, not run: the link opens it in the Analysis Workspace.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class StartAnalysisTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_start_analysis";

    internal const string DocumentIdArgument = "documentId";

    internal const string NameArgument = "name";

    internal const string PlaybookIdArgument = "playbookId";

    internal const string IdempotencyKeyArgument = McpToolAuthorization.IdempotencyKeyArgument;

    /// <summary>The longest retry key accepted, in characters.</summary>
    internal const int IdempotencyKeyMaxLength = 200;

    internal const string RetryResource = "mcp-start-analysis";

    internal const string LockResource = "mcp-start-analysis-lock";

    /// <summary>The <c>reasonCode</c> of a retry key reused with different inputs: this host's own (<see cref="McpIdempotency"/>).</summary>
    internal const string KeyReusedReasonCode = McpIdempotency.KeyReusedReasonCode;

    /// <summary>The <c>reasonCode</c> of a retry key whose first start has not finished: this host's own (<see cref="McpIdempotency"/>).</summary>
    internal const string KeyInProgressReasonCode = McpIdempotency.KeyInProgressReasonCode;

    private static readonly McpIdempotencyScope Scope = new(ToolName, RetryResource, LockResource);

    private static readonly string[] Arguments = [DocumentIdArgument, NameArgument, PlaybookIdArgument, IdempotencyKeyArgument];

    internal static McpToolError CreateFailed { get; } = new(
        AnalysisEndpoints.CreateFailedReasonCode, "The analysis could not be started. Try again shortly.", StatusCodes.Status500InternalServerError);

    /// <summary>
    /// The text of a start refused because the access check could not be made (the route's <c>system_failure</c> 403): a system fault, not a
    /// refusal of the caller, so it must not read as "you may not".
    /// </summary>
    internal const string SystemFailureMessage = "Spaarke could not check access right now. Try again shortly.";

    /// <summary>
    /// The description the model reads (at most 1,024 characters, "use for / not for"; D-209 qualifies "playbook": a JPS playbook is an AI
    /// analysis recipe, not a legal or contract-review playbook).
    /// </summary>
    internal const string Description =
        "Use for: starting a Spaarke analysis of one document the signed-in user may work on, optionally with a JPS playbook (AI analysis "
        + "recipe). It creates the analysis record and returns its id and a link that opens it in the Spaarke Analysis Workspace, where the "
        + "user runs it; it does not run the analysis or return results. Needs the document's id (from spaarke_list_documents or "
        + "spaarke_search_documents), a name for the analysis, and an idempotencyKey you make up once per start and send again unchanged on "
        + "a retry, so a retry never creates a second analysis. A playbookId (a JPS playbook) comes from spaarke_list_playbooks. Not for: "
        + "reading an analysis (use spaarke_get_analysis); running a playbook; summarising a document; legal or contract-review playbooks "
        + "(Markdown or Microsoft contract-review playbooks).";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [DocumentIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The id (GUID) of the document to analyse, from spaarke_list_documents or spaarke_search_documents.",
            },
            [NameArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "A short name for the analysis, for example 'Analysis - NDA.pdf'.",
            },
            [PlaybookIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Optional: the id (GUID) of the JPS playbook (AI analysis recipe) to analyse with, from spaarke_list_playbooks.",
            },
            [IdempotencyKeyArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = IdempotencyKeyMaxLength,
                ["description"] = "A value you make up once for this start (for example a GUID) and send again unchanged on a retry.",
            },
        },
        ["required"] = new JsonArray(DocumentIdArgument, NameArgument, IdempotencyKeyArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            StartAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Start an analysis",
                Description = Description,
                ReadOnly = false,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        // The input schema is stated, not inferred; the handler reads the raw arguments against the same closed set.
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = 1 when an analysis was returned).</summary>
    internal static Task<CallToolResult> StartAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ITenantCache cache,
        IAnalysisStartAi starts,
        IOptions<DataverseOptions> dataverse,
        TimeProvider time,
        ILogger<StartAnalysisTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, cache, starts, dataverse.Value.EnvironmentUrl, logger, cancellationToken),
            result => result.IsError == true ? 0 : 1);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ITenantCache cache,
        IAnalysisStartAi starts,
        string? environmentUrl,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no write. Checked before anything else, with the test every tool applies; the decision checks it again.
        if (!PlanAndLookupToolSupport.HasCaller(http))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!TryReadArguments(arguments, http, out var input, out var argumentError))
        {
            return argumentError!;
        }

        var baseUrl = DecisionsTextSummary.ConsoleBaseUrl(environmentUrl);

        // The retry key (McpIdempotency): the first created analysis answers every retry of this key with the same inputs.
        return await McpIdempotency.RunOnceAsync<RetryEntry>(
            Scope, cache, http!, input.IdempotencyKey, FingerprintOf(input),
            replay: earlier => ResultOf(earlier.AnalysisId, earlier.DocumentId, input.Name, warningCount: null, replayed: true, baseUrl),
            run: async () =>
            {
                // The route's decision and the route's create (IAnalysisStartAi, task 037a); nothing is run.
                var started = await starts.StartAsCallerAsync(http, input.Name, input.DocumentId, input.PlaybookId, cancellationToken);
                if (started is not { Outcome: AnalysisStartOutcome.Created, AnalysisId: { } analysisId })
                {
                    return (DocumentToolSupport.Failure(ErrorOf(started), http), null);
                }

                return (
                    ResultOf(analysisId, input.DocumentId, input.Name, started.WarningCount, replayed: false, baseUrl),
                    new RetryEntry(analysisId, input.DocumentId, FingerprintOf(input)));
            },
            logger, cancellationToken);
    }

    /// <summary>The tool's answer for a start that did not create: the route's code and status, with fixed text (ADR-015).</summary>
    internal static McpToolError ErrorOf(AnalysisStartResult started) =>
        started.Outcome switch
        {
            AnalysisStartOutcome.UserContextRequired => DocumentToolSupport.UserContextRequired,
            AnalysisStartOutcome.Invalid => new McpToolError(
                DocumentToolSupport.InvalidArgumentReasonCode,
                string.IsNullOrWhiteSpace(started.Message) ? "The start request is not valid. Check the arguments and send it again." : started.Message,
                StatusCodes.Status400BadRequest),
            // A fault while deciding is a system failure, not a refusal of the caller: its own text.
            AnalysisStartOutcome.Refused when started.ReasonCode == FinanceAuthorizationFilter.SystemFailureReasonCode
                                              && started.Status == StatusCodes.Status403Forbidden => new McpToolError(
                FinanceAuthorizationFilter.SystemFailureReasonCode, SystemFailureMessage, StatusCodes.Status403Forbidden),
            AnalysisStartOutcome.Refused when started.ReasonCode is { } code && started.Status == StatusCodes.Status403Forbidden => new McpToolError(
                code, "The signed-in user may not start an analysis on this document, or may not use this JPS playbook (AI analysis recipe).", StatusCodes.Status403Forbidden),
            AnalysisStartOutcome.Refused when started.ReasonCode is { } code => new McpToolError(
                code, "The analysis was not saved: Spaarke could not decide who owns it. Contact your administrator.", started.Status),
            _ => CreateFailed,
        };

    /// <summary>
    /// The success result: the new (or earlier) analysis, its one text line and its Analysis Workspace link. Pure. A replay
    /// (<paramref name="replayed"/>) says nothing about whether the analysis has been run since, and carries no <paramref name="warningCount"/>
    /// (<c>null</c>): only the first call knew it.
    /// </summary>
    internal static CallToolResult ResultOf(Guid analysisId, Guid documentId, string name, int? warningCount, bool replayed, string? baseUrl)
    {
        // ADR-013 Path C: the link helper is reached through AnalysisEndpoints, never named here.
        var link = AnalysisEndpoints.AnalysisWorkspaceLink(baseUrl, analysisId, documentId);
        var body = new StartBody(analysisId.ToString("D"), documentId.ToString("D"), name, link, replayed, warningCount);
        var text = replayed
            ? $"Analysis already started with this key: {McpText.SingleLine(name)} ({body.AnalysisId}). Open it to see its status."
              + (link is { } openEarlier ? $"\nOpen it in the Spaarke Analysis Workspace: {openEarlier}" : string.Empty)
            : $"Analysis started: {McpText.SingleLine(name)} ({body.AnalysisId}). It has not been run yet."
              + (link is { } open ? $"\nOpen it in the Spaarke Analysis Workspace to run it: {open}" : string.Empty);
        return DecisionToolCall.Success(text, body);
    }

    /// <summary><c>structuredContent</c> of <c>spaarke_start_analysis</c>.</summary>
    internal sealed record StartBody(string AnalysisId, string DocumentId, string Name, string? Link, bool Replayed, int? WarningCount);

    /// <summary>What the retry key keeps: the analysis it created and the fingerprint of the inputs that created it (no key, no name).</summary>
    internal sealed record RetryEntry(Guid AnalysisId, Guid DocumentId, string Fingerprint) : IMcpRetryEntry;

    internal readonly record struct StartInput(Guid DocumentId, string Name, Guid? PlaybookId, string IdempotencyKey);

    /// <summary>
    /// The closed argument set: <c>documentId</c>, <c>name</c> and <c>idempotencyKey</c> required non-empty strings, <c>playbookId</c>
    /// an optional string (absent or JSON null means none). Any other argument is refused with the fix, never ignored.
    /// </summary>
    internal static bool TryReadArguments(
        IDictionary<string, JsonElement>? arguments, HttpContext? http, out StartInput input, out CallToolResult? error)
    {
        input = default;
        error = null;
        var fix = $"{ToolName} takes {DocumentIdArgument}, {NameArgument} and {IdempotencyKeyArgument} (all text, required) and an optional "
                  + $"{PlaybookIdArgument}. Send the call again with just those.";
        if (arguments is null || arguments.Keys.Any(k => !Arguments.Contains(k, StringComparer.Ordinal)))
        {
            error = DocumentToolSupport.InvalidArgument(fix, http);
            return false;
        }

        if (!TryReadText(arguments, DocumentIdArgument, out var documentText)
            || !TryReadText(arguments, NameArgument, out var name)
            || !TryReadText(arguments, IdempotencyKeyArgument, out var key))
        {
            error = DocumentToolSupport.InvalidArgument(fix, http);
            return false;
        }

        if (!Guid.TryParse(documentText, out var documentId) || documentId == Guid.Empty)
        {
            error = DocumentToolSupport.Failure(new McpToolError(
                DocumentToolSupport.InvalidIdReasonCode, $"'{DocumentIdArgument}' must be the document's id (a GUID).", StatusCodes.Status400BadRequest), http);
            return false;
        }

        if (key.Length > IdempotencyKeyMaxLength)
        {
            error = DocumentToolSupport.InvalidArgument($"'{IdempotencyKeyArgument}' must be at most {IdempotencyKeyMaxLength} characters.", http);
            return false;
        }

        Guid? playbookId = null;
        if (arguments.TryGetValue(PlaybookIdArgument, out var playbook) && playbook.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (playbook.ValueKind != JsonValueKind.String || !Guid.TryParse(playbook.GetString(), out var parsed) || parsed == Guid.Empty)
            {
                error = DocumentToolSupport.Failure(new McpToolError(
                    DocumentToolSupport.InvalidIdReasonCode, $"'{PlaybookIdArgument}' must be the JPS playbook's id (a GUID), or left out.", StatusCodes.Status400BadRequest), http);
                return false;
            }

            playbookId = parsed;
        }

        input = new StartInput(documentId, name, playbookId, key);
        return true;
    }

    private static bool TryReadText(IDictionary<string, JsonElement> arguments, string name, out string value)
    {
        value = string.Empty;
        if (!arguments.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            return false;
        }

        value = element.GetString()!.Trim();
        return true;
    }

    // ---- the retry key (McpIdempotency) -------------------------------------------------------------------------------------------

    /// <summary>The cache id of a key (<see cref="McpIdempotency.RetryIdOf"/>): the caller's oid and a hash of the key.</summary>
    internal static string RetryIdOf(HttpContext http, string idempotencyKey) => McpIdempotency.RetryIdOf(http, idempotencyKey);

    /// <summary>The inputs a retry must repeat: the document, the playbook and the name.</summary>
    internal static string FingerprintOf(StartInput input) =>
        McpIdempotency.FingerprintOf(input.DocumentId.ToString("D"), input.PlaybookId?.ToString("D") ?? string.Empty, input.Name);
}
