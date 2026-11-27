using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r2 task 037b (D-037-results, D-037-engine; spec FR-05, FR-16, NFR-02, NFR-05; ADR-013, ADR-015, ADR-028).
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10; .claude/constraints/bff-extensions.md). A thin, app-only, read-only MCP adapter inside the
// BFF over IPlaybookRunAi.ReadResultsAsCallerAsync (Services/Ai/PublicContracts): no package, no DI registration (found by [McpArea]), no
// background work, no Services/Ai type other than the public contract.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: spaarke_get_playbook_run (status and counts only, model-visible: ADR-015 keeps its output minimal and frozen);
//     spaarke_get_analysis (model-visible, a 500-character summary, read through the route's app-only loader).
//   Extension: neither can carry a run's full output: both are model-visible, and the status tool's text is frozen. The results go to the
//     widget alone through an app-only tool, read as the caller, with the status tool's not-found and state vocabulary.
//   Cost of doing nothing: a Copilot user who runs a playbook learns that it finished but not what it found (owner Q17).
// =====================================================================================================================

/// <summary>
/// <c>spaarke_get_playbook_run_results</c> (task 037b): the results of a JPS playbook run the signed-in caller started, for the playbook run
/// widget only. App-only (<c>_meta.ui.visibility</c> exactly <c>["app"]</c>; <see cref="McpToolAuthorization.AppOnlyTools"/> refuses
/// startup otherwise) and read-only. Another caller's run, an unknown id, a run whose analysis or subject the caller cannot read: the same
/// not-found as <c>spaarke_get_playbook_run</c>.
/// </summary>
/// <remarks>
/// <para><b>structuredContent</b> (part c's widget reads it; null members are omitted):
/// <c>{ runId, status, outcome, results, resultsReason?, playbookId, playbookName?, progress?, reasonCode?, analysisId?, sections[],
/// truncated, moreLink?, consoleLink?, documents[], moreDocuments? }</c>. <c>status</c> is the status tool's (<c>running</c>,
/// <c>completed</c>, <c>failed</c>), <c>outcome</c> too (<c>running</c>, <c>completed</c>, <c>failed</c>, <c>cancelled</c>,
/// <c>interrupted</c>). <c>results</c> is <c>pending</c> (still running: no content), <c>available</c> (sections hold the analysis's
/// working document), <c>none</c> (finished with nothing to show) or <c>unsaved</c> (task 037 review F-A: completed, but saving its
/// output failed; <c>resultsReason</c> is <see cref="UnsavedReason"/>). Each section is <c>{ heading?, text, truncated }</c>: the working
/// document split at its Markdown headings, or one section without a heading. Each document is <c>{ documentId, name, link? }</c>; at most
/// <see cref="PlaybookRunLimits.MaxDocuments"/> are listed and <c>moreDocuments</c> counts the rest (F-D).</para>
/// <para><b>Bounded (NFR-02).</b> Every result is under 64 KiB: the sections share <see cref="SectionsBudgetBytes"/> of worst-case JSON
/// (every character the encoder may escape counted as six bytes); past it the cut section is <c>truncated</c>, later sections are dropped,
/// <c>truncated</c> is true and <c>moreLink</c> opens the full analysis in Spaarke.</para>
/// <para><b>Logs (ADR-015, NFR-05).</b> The tool name, outcome, section count, latency and correlation id (the per-call line), and the
/// result's size, section count and truncation; never the working document, a name or an id of a record.</para>
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class GetPlaybookRunResultsTool : McpToolDefinition
{
    internal const string ToolName = "spaarke_get_playbook_run_results";

    internal const string RunIdArgument = "runId";

    /// <summary>The worst-case JSON bytes all sections (headings, text and their member names) may take together.</summary>
    internal const int SectionsBudgetBytes = 36 * 1024;

    /// <summary>The most sections a result carries; later ones are dropped and the result is <c>truncated</c>.</summary>
    internal const int MaxSections = 50;

    /// <summary>The longest heading, in characters (one line).</summary>
    internal const int HeadingMaxChars = 200;

    /// <summary>The longest document name, in characters (one line): at 20 documents and six bytes a character, under 10 KiB.</summary>
    internal const int DocumentNameMaxChars = 80;

    /// <summary>What one section's member names and punctuation cost, in JSON bytes, beyond its heading and text.</summary>
    private const int SectionOverheadBytes = 64;

    /// <summary>A cut section shorter than this is dropped rather than shown as a stub.</summary>
    private const int MinClippedSectionBytes = 256;

    /// <summary>A read that faulted: the status tool's own code, so the widget handles one vocabulary.</summary>
    internal static McpToolError GetFailed => GetPlaybookRunTool.GetFailed;

    internal const string Description =
        "Use for: the Spaarke playbook run widget only. Returns the results of a JPS playbook (AI analysis recipe) run that the signed-in "
        + "user started with spaarke_run_playbook: the run's state, then, once it has finished, the analysis text it saved (in sections, "
        + "bounded, with a link to the full analysis in Spaarke) and the source documents the user can open. Needs the runId that "
        + "spaarke_run_playbook returned. A run that is still running returns its state with no content. Not for: the model (it is "
        + "app-only); the status of a run (use spaarke_get_playbook_run); starting a run (use spaarke_run_playbook); reading an analysis by "
        + "id (use spaarke_get_analysis). A run that does not exist, another user's run and a run the user can no longer read return the "
        + "same not-found answer.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [RunIdArgument] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The runId (a GUID) that spaarke_run_playbook returned.",
            },
        },
        ["required"] = new JsonArray(RunIdArgument),
        ["additionalProperties"] = false,
    });

    /// <summary>
    /// The <c>resultsReason</c> of an <c>unsaved</c> result (task 037 review F-A): the run completed, but saving its output to the analysis
    /// failed, so the analysis has no text and was not finalized.
    /// </summary>
    internal const string UnsavedReason = "analysis_save_failed";

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            GetAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = ToolName,
                Title = "Get a JPS playbook run's results",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });

        tool.ProtocolTool.InputSchema = InputSchema;

        // ADR-015: app-only. The host keeps it out of the model's tool set; McpToolAuthorization.AppOnlyTools refuses startup otherwise.
        tool.ProtocolTool.Meta = new JsonObject { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("app") } };
        return tool;
    }

    /// <summary>The handler the SDK binds: <see cref="RunAsync"/> inside the one per-call telemetry line (NFR-05; rows = the section count).</summary>
    internal static Task<CallToolResult> GetAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        IPlaybookRunAi runs,
        TimeProvider time,
        ILogger<GetPlaybookRunResultsTool> logger,
        CancellationToken cancellationToken) =>
        DecisionToolCall.LogAroundAsync(
            logger, time, ToolName, accessor.HttpContext,
            () => RunAsync(request.Params?.Arguments, accessor.HttpContext, runs, time, logger, cancellationToken),
            SectionCount);

    internal static async Task<CallToolResult> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        IPlaybookRunAi runs,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // ADR-028 A10: no caller, no read; never an app-only fallback.
        if (!PlanAndLookupToolSupport.HasCaller(http) || string.IsNullOrEmpty(CallerResolution.ResolveObjectId(http!.User)))
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.UserContextRequired, http);
        }

        if (!DocumentToolSupport.TryReadStringArguments(arguments, ToolName, [RunIdArgument], http, out var values, out var argumentError))
        {
            return argumentError!;
        }

        if (!Guid.TryParse(values[0], out var runId) || runId == Guid.Empty)
        {
            return DocumentToolSupport.Failure(
                new McpToolError(DocumentToolSupport.InvalidIdReasonCode, $"'{RunIdArgument}' must be the run's id (a GUID).", StatusCodes.Status400BadRequest), http);
        }

        PlaybookRunResults? results;
        try
        {
            results = await runs.ReadResultsAsCallerAsync(http!, runId, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ADR-015: the exception type only. A fault is "could not answer", never the not-found.
            logger.LogWarning("MCP tool {ToolName}: the read faulted ({ExceptionType}). CorrelationId={CorrelationId}",
                ToolName, ex.GetType().Name, http!.TraceIdentifier);
            return DocumentToolSupport.Failure(GetFailed, http);
        }

        // Another caller's run, an unknown id, an unreadable analysis or subject: the one uniform not-found (no existence oracle).
        if (results is null)
        {
            return DocumentToolSupport.Failure(DocumentToolSupport.NotFound, http);
        }

        // Task 037 review F-C: building the result is pure, but a fault in it (any input it was not written for) is the read's own
        // isError, never a transport-level error the widget cannot read.
        try
        {
            var result = ResultOf(results, time.GetUtcNow());
            var body = result.StructuredContent!.Value;
            logger.LogInformation(
                "MCP tool {ToolName}: results {Results}, {Bytes} bytes, {Sections} sections, truncated {Truncated}. CorrelationId={CorrelationId}",
                ToolName,
                body.GetProperty("results").GetString(),
                Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions)),
                body.GetProperty("sections").GetArrayLength(),
                body.GetProperty("truncated").GetBoolean(),
                http!.TraceIdentifier);
            return result;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // ADR-015: the exception type only.
            logger.LogWarning("MCP tool {ToolName}: building the result faulted ({ExceptionType}). CorrelationId={CorrelationId}",
                ToolName, ex.GetType().Name, http!.TraceIdentifier);
            return DocumentToolSupport.Failure(GetFailed, http);
        }
    }

    /// <summary>The result for <paramref name="results"/> (pure): the bounded body and one fixed text line with no content.</summary>
    internal static CallToolResult ResultOf(PlaybookRunResults results, DateTimeOffset now)
    {
        var run = results.Run;
        var interrupted = GetPlaybookRunTool.IsInterrupted(run, now);
        var (status, outcome) = interrupted
            ? ("failed", "interrupted")
            : run.Phase switch
            {
                PlaybookRunPhase.Running => ("running", "running"),
                PlaybookRunPhase.Completed => ("completed", "completed"),
                PlaybookRunPhase.Cancelled => ("failed", "cancelled"),
                _ => ("failed", "failed"),
            };

        // A run lost to a restart reads Running from its record: it will never finish, so it has nothing to show, not "pending".
        var state = interrupted && results.State == PlaybookRunResultsState.Pending ? PlaybookRunResultsState.None : results.State;
        var (sections, truncated) = state == PlaybookRunResultsState.Available
            ? Sections(results.WorkingDocument)
            : (Array.Empty<SectionBody>(), false);
        if (state == PlaybookRunResultsState.Available && sections.Count == 0)
        {
            state = PlaybookRunResultsState.None;
        }

        var reasonCode = interrupted
            ? PlaybookRunReasonCodes.Interrupted
            : string.IsNullOrWhiteSpace(run.ReasonCode) ? null : SearchToolSupport.Clip(McpText.SingleLine(run.ReasonCode).Trim(), 64);

        var body = new ResultsBody(
            run.RunId.ToString("D"),
            status,
            outcome,
            state switch
            {
                PlaybookRunResultsState.Pending => "pending",
                PlaybookRunResultsState.Available => "available",
                PlaybookRunResultsState.Unsaved => "unsaved",
                _ => "none",
            },
            state == PlaybookRunResultsState.Unsaved ? UnsavedReason : null,
            run.PlaybookId.ToString("D"),
            string.IsNullOrWhiteSpace(results.PlaybookName) ? null : SearchToolSupport.Clip(McpText.SingleLine(results.PlaybookName).Trim(), HeadingMaxChars),
            status == "running" ? run.Progress : null,
            reasonCode,
            results.AnalysisId?.ToString("D"),
            sections,
            truncated,
            truncated ? results.ConsoleLink : null,
            results.ConsoleLink,
            // F-D: the reader lists at most PlaybookRunLimits.MaxDocuments; this bound keeps the 64 KiB result whatever it is handed.
            results.Documents
                .Take(PlaybookRunLimits.MaxDocuments)
                .Select(d => new DocumentBody(
                    d.DocumentId.ToString("D"),
                    SearchToolSupport.Clip(McpText.SingleLine(d.Name).Trim(), DocumentNameMaxChars) ?? string.Empty,
                    d.ConsoleLink))
                .ToArray(),
            results.MoreDocuments > 0 ? results.MoreDocuments : null);

        var line = body.Results switch
        {
            "pending" => $"JPS playbook run {body.RunId} is still running; its results are not ready.",
            "available" => $"JPS playbook run {body.RunId} ({body.Outcome}): {sections.Count} result section(s){(truncated ? ", shortened" : string.Empty)}.",
            "unsaved" => $"JPS playbook run {body.RunId} ({body.Outcome}) finished, but its results could not be saved.",
            _ => $"JPS playbook run {body.RunId} ({body.Outcome}) has no results to show here.",
        };
        return DecisionToolCall.Success(line, body);
    }

    /// <summary>
    /// The working document as sections, within <see cref="SectionsBudgetBytes"/> and <see cref="MaxSections"/> (pure). Split at Markdown
    /// headings; text before the first heading, or a document with none, is a section without a heading. Returns whether anything was cut.
    /// </summary>
    internal static (IReadOnlyList<SectionBody> Sections, bool Truncated) Sections(string? workingDocument)
    {
        var parsed = Parse(workingDocument ?? string.Empty);
        var sections = new List<SectionBody>();
        var budget = SectionsBudgetBytes;
        var truncated = false;
        foreach (var (heading, text) in parsed)
        {
            if (sections.Count == MaxSections)
            {
                truncated = true;
                break;
            }

            var cost = SectionOverheadBytes + JsonCost(heading) + JsonCost(text);
            if (cost <= budget)
            {
                sections.Add(new SectionBody(heading, text, false));
                budget -= cost;
                continue;
            }

            truncated = true;
            var room = budget - SectionOverheadBytes - JsonCost(heading);
            if (room >= MinClippedSectionBytes)
            {
                sections.Add(new SectionBody(heading, ClipToJsonBytes(text, room), true));
            }

            break;
        }

        return (sections, truncated);
    }

    private static List<(string? Heading, string Text)> Parse(string document)
    {
        var sections = new List<(string? Heading, string Text)>();
        string? heading = null;
        var text = new StringBuilder();

        void Flush()
        {
            var body = text.ToString().Trim('\r', '\n');
            if (heading is not null || !string.IsNullOrWhiteSpace(body))
            {
                sections.Add((heading, body));
            }

            text.Clear();
        }

        var inFence = false;
        foreach (var line in document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
            }

            if (!inFence && HeadingText(line)?.Trim() is { Length: > 0 } title)
            {
                Flush();
                heading = SearchToolSupport.Clip(McpText.SingleLine(title), HeadingMaxChars);
                continue;
            }

            text.Append(line).Append('\n');
        }

        Flush();
        return sections;
    }

    /// <summary>
    /// The text of a Markdown ATX heading line, or <c>null</c> when <paramref name="line"/> is not one (pure, linear). The same match as the
    /// pattern <c>^ {0,3}#{1,6}[ \t]+(?&lt;text&gt;.*?)(?:[ \t]+#+)?[ \t]*$</c>: up to three spaces, one to six '#', at least one space or
    /// tab, the text, then an optional closing run of '#' after a space or tab, and trailing spaces or tabs. Task 037 review F-C: that
    /// pattern backtracked quadratically on a long run of spaces or tabs (50,000 spaces hit its one-second timeout), so it is a scan.
    /// </summary>
    internal static string? HeadingText(string line)
    {
        var i = 0;
        while (i < line.Length && i < 3 && line[i] == ' ')
        {
            i++;
        }

        var hashes = 0;
        while (i + hashes < line.Length && line[i + hashes] == '#')
        {
            hashes++;
        }

        if (hashes is 0 or > 6)
        {
            return null;
        }

        i += hashes;
        if (i >= line.Length || line[i] is not (' ' or '\t'))
        {
            return null;
        }

        // The opening run of spaces and tabs is taken whole (the pattern's greedy [ \t]+ never needs to give any back).
        while (i < line.Length && line[i] is ' ' or '\t')
        {
            i++;
        }

        // Trailing spaces and tabs, then a closing run of '#' only when a space or tab comes before it inside the text.
        var end = line.Length;
        while (end > i && line[end - 1] is ' ' or '\t')
        {
            end--;
        }

        var closing = end;
        while (closing > i && line[closing - 1] == '#')
        {
            closing--;
        }

        if (closing < end && closing > i && line[closing - 1] is ' ' or '\t')
        {
            end = closing - 1;
            while (end > i && line[end - 1] is ' ' or '\t')
            {
                end--;
            }
        }

        return line[i..end];
    }

    /// <summary>
    /// The worst-case JSON bytes of <paramref name="value"/> as a string body: a printable ASCII character the default encoder leaves alone
    /// is one byte; anything it may escape (control, quote, backslash, HTML-sensitive, non-ASCII, each surrogate half) is six (<c>\uXXXX</c>).
    /// </summary>
    internal static int JsonCost(string? value)
    {
        if (value is null)
        {
            return 4;
        }

        var cost = 0;
        foreach (var c in value)
        {
            cost += CharCost(c);
        }

        return cost;
    }

    private static int CharCost(char c) =>
        c is >= ' ' and < (char)0x7F and not ('"' or '\\' or '<' or '>' or '&' or '\'' or '+' or '`') ? 1 : 6;

    private static string ClipToJsonBytes(string value, int maxBytes)
    {
        var cost = 0;
        var end = 0;
        while (end < value.Length && cost + CharCost(value[end]) <= maxBytes)
        {
            cost += CharCost(value[end]);
            end++;
        }

        if (end > 0 && end < value.Length && char.IsHighSurrogate(value[end - 1]))
        {
            end--;
        }

        return value[..end];
    }

    private static int SectionCount(CallToolResult result) =>
        result.IsError != true
        && result.StructuredContent is { ValueKind: JsonValueKind.Object } body
        && body.TryGetProperty("sections", out var sections)
        && sections.ValueKind == JsonValueKind.Array
            ? sections.GetArrayLength()
            : 0;

    /// <summary><c>structuredContent</c> of a results call (see the class remarks for the shape).</summary>
    internal sealed record ResultsBody(
        string RunId,
        string Status,
        string Outcome,
        string Results,
        string? ResultsReason,
        string PlaybookId,
        string? PlaybookName,
        int? Progress,
        string? ReasonCode,
        string? AnalysisId,
        IReadOnlyList<SectionBody> Sections,
        bool Truncated,
        string? MoreLink,
        string? ConsoleLink,
        IReadOnlyList<DocumentBody> Documents,
        int? MoreDocuments);

    /// <summary>One section of the working document: its heading (none for text before the first heading), its text, whether it was cut.</summary>
    internal sealed record SectionBody(string? Heading, string Text, bool Truncated);

    /// <summary>One source document the caller can read, with its "open in Spaarke" link.</summary>
    internal sealed record DocumentBody(string DocumentId, string Name, string? Link);
}
