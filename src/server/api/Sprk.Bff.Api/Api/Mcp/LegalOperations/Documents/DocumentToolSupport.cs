using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;

// COMPONENT JUSTIFICATION (CLAUDE.md section 11), task 023.
//   Existing: DecisionToolCall (success result, per-call telemetry, outcome), PlanAndLookupToolSupport.HasCaller, McpToolResults.FromError,
//     ProblemDetailsHelper.RecordUnavailableReasonCode (the REST uniform not-found), WorklistItemSummary.Clip.
//   Extension: this class calls all of them and adds only what no one has: the closed multi-argument check (TryReadStringArguments reads
//     two named arguments; DecisionToolCall.TryReadSingleArgument reads one), the document row projection and the one uniform not-found.
//   Cost of doing nothing: the two document tools would each carry a copy of the argument check, the row projection and the not-found
//     text, and a forbidden and a missing document could drift to different answers (the FR-03 negative case).

/// <summary>
/// What <see cref="ListDocumentsTool"/> and <see cref="GetDocumentTool"/> share (spaarkeai-mcp-bff-api-r2 task 023, spec FR-03,
/// NFR-02, NFR-05, NFR-07, ADR-015).
/// </summary>
internal static class DocumentToolSupport
{
    /// <summary>Spec NFR-02: every result under 64 KiB.</summary>
    internal const int ResultLimitBytes = 64 * 1024;

    /// <summary>
    /// The longest document name a row carries, as JSON bytes (a name is free text up to 850 characters in Dataverse; at 50 rows an
    /// unbounded name could pass the 64 KiB result limit, and the default encoder writes a non-ASCII character as up to six bytes).
    /// </summary>
    internal const int NameBytes = 400;

    /// <summary>The <c>reasonCode</c> of a call whose arguments are wrong: this host's own, like <c>mcp.disabled</c>.</summary>
    internal const string InvalidArgumentReasonCode = "mcp.invalid_argument";

    /// <summary>The <c>reasonCode</c> the REST file routes give a document id that is not a GUID (<c>invalid_id</c>, 400).</summary>
    internal const string InvalidIdReasonCode = "invalid_id";

    /// <summary>The REST uniform not-found's detail (<c>ProblemDetailsHelper.UniformRecordNotFound</c>), word for word.</summary>
    internal const string NotFoundMessage = "The requested record was not found.";

    /// <summary>
    /// THE not-found of both tools: the same <c>reasonCode</c> (<see cref="ProblemDetailsHelper.RecordUnavailableReasonCode"/>), message
    /// and status for a record that does not exist, a document or matter the caller may not read, and a document the external narrowing
    /// hides, so nothing here tells them apart (spec FR-03, NFR-07: no existence oracle). It names no id.
    /// </summary>
    internal static McpToolError NotFound { get; } =
        new(ProblemDetailsHelper.RecordUnavailableReasonCode, NotFoundMessage, StatusCodes.Status404NotFound);

    /// <summary>No signed-in caller: nothing was read and nothing falls back to an app-only client (ADR-028 A10, FR-15).</summary>
    internal static McpToolError UserContextRequired { get; } =
        new(DataverseUserClientErrorCodes.UserContextRequired, "The request has no signed-in caller.", StatusCodes.Status401Unauthorized);

    /// <summary>The <c>reasonCode</c> of a read that faulted (Dataverse, a decision service or a timeout): this host's own, never a REST code.</summary>
    internal const string UnavailableReasonCode = "document.unavailable";

    /// <summary>
    /// An unexpected fault while reading documents: fixed text, no exception text (ADR-015: a Dataverse or Graph message can carry ids), 503.
    /// It is NOT the not-found: a fault is "could not answer", which the model should retry, not read as "no such document".
    /// </summary>
    internal static McpToolError Unavailable { get; } =
        new(UnavailableReasonCode, "The documents could not be read right now. Try again shortly.", StatusCodes.Status503ServiceUnavailable);

    /// <summary>
    /// The result for an unexpected fault in <paramref name="step"/>: logs the exception TYPE and the correlation id only (never its
    /// message or stack: NFR-05, ADR-015) and answers <see cref="Unavailable"/>. Callers catch with
    /// <c>when (!cancellationToken.IsCancellationRequested)</c>, so a caller's own cancellation still propagates while a timeout that is
    /// not the caller's (an <c>HttpClient</c> <c>TaskCanceledException</c>) is mapped here.
    /// </summary>
    internal static CallToolResult Fault(Exception exception, string toolName, string step, HttpContext? http, ILogger logger)
    {
        logger.LogError(
            "MCP tool {ToolName}: {Step} failed ({ExceptionType}). CorrelationId={CorrelationId}",
            toolName, step, exception.GetType().Name, http?.TraceIdentifier);
        return Failure(Unavailable, http);
    }

    /// <summary>An <c>isError</c> result for <paramref name="error"/>, with the request's correlation id.</summary>
    internal static CallToolResult Failure(McpToolError error, HttpContext? http) =>
        McpToolResults.FromError(error, http?.TraceIdentifier);

    /// <summary>A wrong-arguments error whose <paramref name="message"/> says how to fix the call (names arguments and values only).</summary>
    internal static CallToolResult InvalidArgument(string message, HttpContext? http) =>
        Failure(new McpToolError(InvalidArgumentReasonCode, message, StatusCodes.Status400BadRequest), http);

    /// <summary>
    /// The string value of each of <paramref name="names"/>, in order, or an error result when the call carries any other argument,
    /// leaves one out, or sends one that is not a non-empty string. The tool's schema says <c>additionalProperties: false</c>; this is
    /// what makes it true, so an invented argument is refused with the fix, never ignored.
    /// </summary>
    internal static bool TryReadStringArguments(
        IDictionary<string, JsonElement>? arguments, string toolName, string[] names, HttpContext? http, out string[] values, out CallToolResult? error)
    {
        values = [];
        error = null;
        var fix = $"{toolName} takes exactly these arguments, all text: {string.Join(", ", names)}. Send the call again with just those.";
        if (arguments is null || arguments.Keys.Any(k => !names.Contains(k, StringComparer.Ordinal)))
        {
            error = InvalidArgument(fix, http);
            return false;
        }

        var read = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            if (!arguments.TryGetValue(names[i], out var element)
                || element.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(element.GetString()))
            {
                error = InvalidArgument(fix, http);
                return false;
            }

            read[i] = element.GetString()!.Trim();
        }

        values = read;
        return true;
    }

    /// <summary>The file type of a document: its file name's extension, lower case, without the dot; null when it has none.</summary>
    internal static string? FileTypeOf(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        var dot = fileName.LastIndexOf('.');
        return dot >= 0 && dot < fileName.Length - 1 ? fileName[(dot + 1)..].ToLowerInvariant() : null;
    }

    /// <summary>The slim, model-visible row of one document (ADR-015, NFR-07): no storage pointer, no description, no summary, no content.</summary>
    internal static DocumentRow RowOf(DocumentEntity document) =>
        new(
            document.Id,
            WorklistItemSummary.Clip(document.Name, NameBytes) ?? string.Empty,
            FileTypeOf(document.FileName),
            document.FileSize,
            document.ModifiedOn == default ? null : document.ModifiedOn,
            document.HasFile);

    /// <summary>A bounded one-line text for the model: name, type, size, modified date and id.</summary>
    internal static string LineOf(DocumentRow row)
    {
        var parts = new List<string>(3);
        if (row.FileType is not null)
        {
            parts.Add(McpText.SingleLine(row.FileType));
        }

        if (row.SizeBytes is { } size)
        {
            parts.Add(SizeText(size));
        }

        if (row.ModifiedOn is { } modified)
        {
            parts.Add($"modified {modified:yyyy-MM-dd}");
        }

        // The name and the file type (the extension of a user-chosen file name) are user-authored: on one line, so neither can start a row
        // or a link line of its own. Structured content keeps them as they are.
        var name = McpText.SingleLine(row.Name);
        return parts.Count == 0 ? $"{name} (id {row.Id})" : $"{name} ({string.Join(", ", parts)}; id {row.Id})";
    }

    private static string SizeText(long bytes) =>
        bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
        };
}

/// <summary>One document as the document tools return it (id, name, file type, size, modified date, whether a file is attached).</summary>
internal sealed record DocumentRow(string Id, string Name, string? FileType, long? SizeBytes, DateTime? ModifiedOn, bool HasFile);

/// <summary>A record a document is filed under: its type and id. No display name (D-023-names): the shared document mapping never fills one, so the tools do not advertise it.</summary>
internal sealed record RegardingRecord(string EntityType, string Id);
