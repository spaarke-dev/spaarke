using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using ModelContextProtocol.Protocol;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Search;

// COMPONENT JUSTIFICATION (CLAUDE.md section 11), task 022.
//   Existing: DocumentToolSupport (closed-argument shape, InvalidArgument, UserContextRequired, Failure), DecisionToolCall (success result,
//     telemetry), DecisionsTextSummary.ConsoleBaseUrl (the open-link base), McpText.SingleLine (control characters in a line of text),
//     SemanticSearchAuthorizationFilter / RecordSearchAuthorizationFilter (the access decisions, task 027).
//   Extension: this class calls all of them. It adds only what no one has: the argument set the two search tools share (query, limit,
//     offset), the refusal-to-error mapping for the two typed access outcomes, the paging text, the slim hit projection and the size
//     budget. No Services/Ai type appears in Api/Mcp: the two filters resolve the search index themselves (ADR-013, Path C).
//   Cost of doing nothing: the two search tools would each carry a copy of the argument bounds, the outcome table and the row budget, and
//     the two could drift (one clamping a limit the other refuses; one leaking an outcome's text the other hides).

/// <summary>
/// What <see cref="SearchDocumentsTool"/> and <see cref="FindRecordsTool"/> share (spaarkeai-mcp-bff-api-r2 task 022, spec FR-02, NFR-02,
/// NFR-05, NFR-07, ADR-015). It holds no access decision: the decisions are the shared methods of
/// <see cref="SemanticSearchAuthorizationFilter"/> and <see cref="RecordSearchAuthorizationFilter"/> (task 027), called by the tools.
/// </summary>
internal static partial class SearchToolSupport
{
    /// <summary>The argument every search carries.</summary>
    internal const string QueryArgument = "query";

    internal const string LimitArgument = "limit";

    internal const string OffsetArgument = "offset";

    /// <summary>The query's longest accepted length: the REST routes' <c>[StringLength(1000)]</c> (<see cref="RecordSearchRequest.MaxQueryLength"/>).</summary>
    internal const int MaxQueryLength = RecordSearchRequest.MaxQueryLength;

    /// <summary>
    /// The page bounds, REST's own: <c>options.limit</c> 1 to 50 (default 20) and <c>options.offset</c> 0 to 1,000. The record route refuses
    /// a value outside them with <c>INVALID_LIMIT</c> / <c>INVALID_OFFSET</c> (400); the document route's <c>[Range]</c> is metadata only
    /// (no validation filter runs) and the shared method does not check shape, so a negative limit would throw on its <c>scope=all</c>
    /// path. The tools refuse the same values with the same codes.
    /// </summary>
    internal const int MinLimit = RecordSearchOptions.MinLimit;

    internal const int MaxLimit = RecordSearchOptions.MaxLimit;

    internal const int DefaultLimit = 20;

    internal const int MinOffset = RecordSearchOptions.MinOffset;

    internal const int MaxOffset = RecordSearchOptions.MaxOffset;

    /// <summary>Spec NFR-02: every result under 64 KiB.</summary>
    internal const int ResultLimitBytes = 64 * 1024;

    /// <summary>
    /// The bytes the hits may take in the result (their <c>structuredContent</c> rows plus their lines of text), as the default
    /// (escape-everything) encoder writes them. The envelope and the summary are far below the 8 KiB left to 64 KiB.
    /// </summary>
    internal const int RowBudgetBytes = 56 * 1024;

    /// <summary>The longest name in a hit (characters, the ellipsis included).</summary>
    internal const int NameChars = 150;

    /// <summary>The longest parent record name in a hit (characters).</summary>
    internal const int ParentNameChars = 100;

    /// <summary>The longest excerpt in a hit (characters, the ellipsis included): ADR-015, task 022.</summary>
    internal const int SnippetChars = 200;

    /// <summary>The most reference numbers a record hit carries, and the longest of them (characters).</summary>
    internal const int MaxReferenceNumbers = 3;

    internal const int ReferenceNumberChars = 40;

    /// <summary>The <c>reasonCode</c> REST gives a limit outside the bounds (<see cref="SearchErrorCodes.InvalidLimit"/>).</summary>
    internal const string InvalidLimitReasonCode = SearchErrorCodes.InvalidLimit;

    /// <summary>The <c>reasonCode</c> REST gives an offset outside the bounds (<see cref="SearchErrorCodes.InvalidOffset"/>).</summary>
    internal const string InvalidOffsetReasonCode = SearchErrorCodes.InvalidOffset;

    private const string Ellipsis = "…";

    /// <summary>The Dataverse table of a document, for its open link.</summary>
    internal const string DocumentTable = "sprk_document";

    // ---- arguments -------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The arguments both tools share: a required <c>query</c> (text, at most <see cref="MaxQueryLength"/> characters), an optional whole
    /// <c>limit</c> and <c>offset</c> inside REST's bounds, and nothing outside <paramref name="extraNames"/> and those three. The schema
    /// says <c>additionalProperties: false</c>; this is what makes it true, so an invented argument is refused with the fix, never
    /// ignored. An error names arguments and bounds only, never a value.
    /// </summary>
    internal static bool TryReadCommon(
        IDictionary<string, JsonElement>? arguments, string toolName, string[] extraNames, HttpContext? http,
        out string query, out int limit, out int offset, out CallToolResult? error)
    {
        query = string.Empty;
        limit = DefaultLimit;
        offset = MinOffset;
        error = null;

        string[] allowed = [QueryArgument, LimitArgument, OffsetArgument, .. extraNames];
        if (arguments is null || arguments.Keys.Any(k => !allowed.Contains(k, StringComparer.Ordinal)))
        {
            error = DocumentToolSupport.InvalidArgument(
                $"{toolName} takes only these arguments: {string.Join(", ", allowed)}; '{QueryArgument}' is required. Send the call again with just those.", http);
            return false;
        }

        // The REST routes' own codes: QUERY_REQUIRED for a missing, non-text or blank query, QUERY_TOO_LONG for one past the bound.
        if (!arguments.TryGetValue(QueryArgument, out var queryElement)
            || queryElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(queryElement.GetString()))
        {
            error = DocumentToolSupport.Failure(
                new McpToolError(
                    SearchErrorCodes.QueryRequired,
                    $"'{QueryArgument}' is required: the words to search for, as text of at most {MaxQueryLength} characters.",
                    StatusCodes.Status400BadRequest),
                http);
            return false;
        }

        if (queryElement.GetString()!.Trim().Length > MaxQueryLength)
        {
            error = DocumentToolSupport.Failure(
                new McpToolError(
                    SearchErrorCodes.QueryTooLong,
                    $"'{QueryArgument}' is too long: send at most {MaxQueryLength} characters.",
                    StatusCodes.Status400BadRequest),
                http);
            return false;
        }

        query = queryElement.GetString()!.Trim();

        if (!TryReadWholeNumber(arguments, LimitArgument, MinLimit, MaxLimit, ref limit))
        {
            error = DocumentToolSupport.Failure(
                new McpToolError(
                    InvalidLimitReasonCode,
                    $"'{LimitArgument}' must be a whole number from {MinLimit} to {MaxLimit}. Leave it out for {DefaultLimit}.",
                    StatusCodes.Status400BadRequest),
                http);
            return false;
        }

        if (!TryReadWholeNumber(arguments, OffsetArgument, MinOffset, MaxOffset, ref offset))
        {
            error = DocumentToolSupport.Failure(
                new McpToolError(
                    InvalidOffsetReasonCode,
                    $"'{OffsetArgument}' must be a whole number from {MinOffset} to {MaxOffset}. Leave it out for the first page.",
                    StatusCodes.Status400BadRequest),
                http);
            return false;
        }

        return true;
    }

    /// <summary>Absent or JSON null leaves <paramref name="value"/> as it is; any other value must be a whole number inside the bounds.</summary>
    private static bool TryReadWholeNumber(IDictionary<string, JsonElement> arguments, string name, int min, int max, ref int value)
    {
        if (!arguments.TryGetValue(name, out var element) || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var read) || read < min || read > max)
        {
            return false;
        }

        value = read;
        return true;
    }

    // ---- refusals and faults ---------------------------------------------------------------------------------------------------------

    /// <summary>The <c>reasonCode</c> of a search that faulted, as the REST route answers it (500): fixed text, never the exception's.</summary>
    internal static CallToolResult Fault(Exception exception, string toolName, string reasonCode, HttpContext? http, ILogger logger)
    {
        // NFR-05, ADR-015: the exception TYPE and the correlation id only. Never its message (an Azure AI Search or Dataverse message can
        // carry an endpoint, an index name or a query fragment) and never the query.
        logger.LogError(
            "MCP tool {ToolName}: the search failed ({ExceptionType}). CorrelationId={CorrelationId}",
            toolName, exception.GetType().Name, http?.TraceIdentifier);
        return DocumentToolSupport.Failure(
            new McpToolError(reasonCode, "The search could not be completed right now. Try again shortly.", StatusCodes.Status500InternalServerError), http);
    }

    private const string SignInMessage = "Spaarke could not confirm your sign-in for this search. Sign in again and retry.";

    /// <summary>
    /// The error for a refused document search: the REST <c>reasonCode</c>, the REST status and a fixed message. The shared method's own
    /// <c>Detail</c> is never used (it echoes request values), and the unreadable and the missing parent are one answer, as on the route.
    /// </summary>
    internal static McpToolError ErrorOf(SemanticSearchAccess access)
    {
        var code = access.ReasonCode;
        return access.Outcome switch
        {
            SemanticSearchAccessOutcome.UserContextRequired => DocumentToolSupport.UserContextRequired,

            SemanticSearchAccessOutcome.MissingTenantIdentity
                or SemanticSearchAccessOutcome.MissingCallerIdentity
                or SemanticSearchAccessOutcome.MissingCallerToken
                => new McpToolError(code ?? SearchErrorCodes.MissingCallerIdentity, SignInMessage, StatusCodes.Status401Unauthorized),

            SemanticSearchAccessOutcome.CallerContextUnavailable
                => new McpToolError(code ?? SearchErrorCodes.MissingCallerIdentity, SignInMessage, StatusCodes.Status500InternalServerError),

            SemanticSearchAccessOutcome.EntityTypeNotAuthorizable
                => new McpToolError(code ?? SearchErrorCodes.EntityTypeNotAuthorizable,
                    "Documents can be searched inside a matter, project or invoice only.", StatusCodes.Status403Forbidden),

            SemanticSearchAccessOutcome.EntityAccessDenied
                => new McpToolError(code ?? SearchErrorCodes.EntityAccessDenied,
                    "You can't search that record. It may not exist, or you may not have access to it.", StatusCodes.Status403Forbidden),

            SemanticSearchAccessOutcome.NoReadableDocuments or SemanticSearchAccessOutcome.ScopeAllNotPermitted
                => new McpToolError(code ?? SearchErrorCodes.NoReadableDocuments,
                    "That document search is not available to you.", StatusCodes.Status403Forbidden),

            SemanticSearchAccessOutcome.RequestBodyRequired
                or SemanticSearchAccessOutcome.InvalidScope
                or SemanticSearchAccessOutcome.EntityTypeRequired
                or SemanticSearchAccessOutcome.EntityIdRequired
                or SemanticSearchAccessOutcome.InvalidEntityId
                or SemanticSearchAccessOutcome.DocumentIdsRequired
                or SemanticSearchAccessOutcome.TooManyDocumentIds
                or SemanticSearchAccessOutcome.InvalidDocumentIds
                => new McpToolError(code ?? DocumentToolSupport.InvalidArgumentReasonCode,
                    "The search scope is not valid. Send entityType and entityId together, or neither to search every document you can open.",
                    StatusCodes.Status400BadRequest),

            // Allowed never reaches here; an outcome this table does not know fails closed as a fault, never as a pass.
            _ => new McpToolError(SearchErrorCodes.SemanticSearchFailed, "The search could not be completed right now. Try again shortly.",
                StatusCodes.Status500InternalServerError),
        };
    }

    /// <summary>The error for a refused record search; see <see cref="ErrorOf(SemanticSearchAccess)"/>.</summary>
    internal static McpToolError ErrorOf(RecordSearchAccess access)
    {
        var code = access.ReasonCode;
        return access.Outcome switch
        {
            RecordSearchAccessOutcome.UserContextRequired => DocumentToolSupport.UserContextRequired,

            RecordSearchAccessOutcome.MissingTenantIdentity
                or RecordSearchAccessOutcome.MissingCallerIdentity
                or RecordSearchAccessOutcome.MissingCallerToken
                => new McpToolError(code ?? SearchErrorCodes.MissingCallerIdentity, SignInMessage, StatusCodes.Status401Unauthorized),

            RecordSearchAccessOutcome.CallerContextUnavailable
                => new McpToolError(code ?? SearchErrorCodes.MissingCallerIdentity, SignInMessage, StatusCodes.Status500InternalServerError),

            RecordSearchAccessOutcome.RecordTypeNotAuthorizable
                => new McpToolError(code ?? SearchErrorCodes.EntityTypeNotAuthorizable,
                    "Those record types can't be searched.", StatusCodes.Status403Forbidden),

            RecordSearchAccessOutcome.RequestBodyRequired
                or RecordSearchAccessOutcome.InvalidRecordTypes
                or RecordSearchAccessOutcome.RecordTypesRequired
                => new McpToolError(code ?? DocumentToolSupport.InvalidArgumentReasonCode,
                    "recordTypes can hold matter, project and invoice only.", StatusCodes.Status400BadRequest),

            _ => new McpToolError(SearchErrorCodes.RecordSearchFailed, "The search could not be completed right now. Try again shortly.",
                StatusCodes.Status500InternalServerError),
        };
    }

    // ---- text ------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="value"/> as one clean line: markup tags (the index's highlight tags) removed, every run of white space and control
    /// characters one space, trimmed and cut at <paramref name="maxChars"/> characters with an ellipsis. Null or empty becomes null.
    /// </summary>
    internal static string? CleanText(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var plain = McpText.SingleLine(Markup().Replace(value, " "));
        var cleaned = new StringBuilder(plain.Length);
        var pendingSpace = false;
        foreach (var c in plain)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = cleaned.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                cleaned.Append(' ');
                pendingSpace = false;
            }

            cleaned.Append(c);
        }

        return cleaned.Length == 0 ? null : Clip(cleaned.ToString(), maxChars);
    }

    /// <summary><paramref name="value"/> cut at <paramref name="maxChars"/> characters, the ellipsis included. Null stays null. A surrogate pair is not split.</summary>
    internal static string? Clip(string? value, int maxChars)
    {
        if (value is null || value.Length <= maxChars)
        {
            return value;
        }

        var end = maxChars - 1;
        if (end > 0 && char.IsHighSurrogate(value[end - 1]))
        {
            end--;
        }

        return string.Concat(value.AsSpan(0, end), Ellipsis);
    }

    [GeneratedRegex("<[^>]{1,40}>")]
    private static partial Regex Markup();

    // ---- links -----------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The link that opens a record in Spaarke: the record form on <paramref name="baseUrl"/> (<see cref="DecisionsTextSummary.ConsoleBaseUrl"/>
    /// of <c>Dataverse:EnvironmentUrl</c>), the shape the task tools use. Null without a base, or when <paramref name="id"/> is not a GUID or
    /// <paramref name="table"/> is not a plain table name, so a value read from the index can never shape the URL.
    /// </summary>
    internal static string? OpenLink(string? baseUrl, string? table, string? id) =>
        baseUrl is not null
        && table is { Length: > 0 and <= 64 }
        && table.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
        && Guid.TryParse(id, out var guid)
        && guid != Guid.Empty
            ? $"{baseUrl}/main.aspx?pagetype=entityrecord&etn={table}&id={guid:D}"
            : null;

    // ---- paging ----------------------------------------------------------------------------------------------------------------------

    /// <summary>The advice for a full page of a search whose <c>offset</c> does not continue the list (a document search over every record).</summary>
    internal const string NarrowToOneRecordAdvice = "For more, search inside one matter, project or invoice (entityType + entityId).";

    /// <summary>
    /// What the paging facts of one result say: the lead sentence of the text, <c>hasMore</c> and <c>nextOffset</c>.
    /// </summary>
    /// <param name="Header">The first line of the text (before the hits).</param>
    /// <param name="HasMore">The page came back full AND an offset would continue the list. Never "there is more": a short page can hide more, and a
    /// full page of a list whose offset does not continue says <c>false</c> too. Nothing is counted (#1533).</param>
    /// <param name="NextOffset">The offset the text advises, or null when it advises none.</param>
    internal sealed record PageNote(string Header, bool HasMore, int? NextOffset);

    /// <summary>
    /// The lead sentence, <c>hasMore</c> and <c>nextOffset</c> for one page. Pure.
    /// <list type="bullet">
    /// <item>No rows at an offset past zero: "No more {noun}s after the first {offset}." (at offset 0: <paramref name="noMatchText"/>).</item>
    /// <item>Rows: "Showing N {noun}(s), best match first." then, when the page came back <b>full</b> or the size bound <b>cut</b> it: the offset to send
    /// (<c>offset + limit</c> for a full page, <c>offset + kept</c> after a cut, so no cut hit is skipped), or, when <paramref name="offsetContinues"/>
    /// is false, <see cref="NarrowToOneRecordAdvice"/> and no offset. A page that came back short and whole gets the hedge "There may be more than this."
    /// because rows are authorized after the index ranks them, so a short page does not mean the end of the list.</item>
    /// </list>
    /// </summary>
    /// <param name="noun">"document" or "record".</param>
    /// <param name="noMatchText">The text of an empty first page.</param>
    /// <param name="extra">Text that follows the "Showing" sentence (the index's "may be incomplete" note), or empty.</param>
    /// <param name="returned">The rows the authorized response held, before the size cut.</param>
    /// <param name="kept">The rows kept after the size cut.</param>
    /// <param name="truncated">Whether the size cut dropped rows.</param>
    /// <param name="offsetContinues">Whether <c>offset + limit</c> is the next page of THIS list. False for a document search over every record: the
    /// shared method reads a wider candidate window from <c>offset</c> (3 times the limit) than it returns, so the next page would repeat hits.</param>
    internal static PageNote DescribePage(
        string noun, string noMatchText, string extra, int returned, int kept, bool truncated, int limit, int offset, bool offsetContinues)
    {
        if (returned == 0)
        {
            return new PageNote(offset > 0 ? $"No more {noun}s after the first {offset}." : noMatchText, HasMore: false, NextOffset: null);
        }

        var full = returned >= limit;
        var text = new StringBuilder($"Showing {kept} {noun}(s), best match first.").Append(extra);
        if (truncated)
        {
            text.Append(" The list was cut to fit.");
        }

        int? next = null;
        if (full || truncated)
        {
            if (!offsetContinues)
            {
                text.Append(' ').Append(NarrowToOneRecordAdvice);
            }
            else if (kept > 0)
            {
                // kept == 0 cannot happen (one hit is far under the row budget); it would advise an offset that repeats this page.
                next = truncated ? offset + kept : offset + limit;
                text.Append(truncated ? $" Send offset {next} for the next page." : $" The page was full; send offset {next} for the next page.");
            }
        }
        else
        {
            text.Append(" There may be more than this.");
        }

        return new PageNote(text.ToString(), HasMore: full && offsetContinues, next);
    }

    // ---- result ----------------------------------------------------------------------------------------------------------------------

    /// <summary>The number of hits in a success result, for the telemetry line (a count only, never a hit).</summary>
    internal static int RowCount(CallToolResult result) =>
        result.IsError != true
        && result.StructuredContent is { ValueKind: JsonValueKind.Object } body
        && body.TryGetProperty("items", out var items)
        && items.ValueKind == JsonValueKind.Array
            ? items.GetArrayLength()
            : 0;

    /// <summary>
    /// Keeps hits, in order, while the result stays inside <see cref="RowBudgetBytes"/>: each hit costs its JSON as <c>structuredContent</c>
    /// plus its line of text, both as the default encoder writes them, so the result is under 64 KiB whatever the text.
    /// </summary>
    internal static (List<T> Kept, string Lines, bool Truncated) Fit<T>(IReadOnlyList<T> hits, Func<T, string> lineOf)
    {
        var kept = new List<T>(hits.Count);
        var lines = new StringBuilder();
        var used = 0;
        foreach (var hit in hits)
        {
            var line = lineOf(hit);
            var cost = JsonSerializer.SerializeToUtf8Bytes(hit, DecisionToolCall.Json).Length + JsonSerializer.SerializeToUtf8Bytes(line, DecisionToolCall.Json).Length;
            if (used + cost > RowBudgetBytes)
            {
                break;
            }

            used += cost;
            kept.Add(hit);
            lines.Append(line);
        }

        return (kept, lines.ToString(), kept.Count < hits.Count);
    }
}
