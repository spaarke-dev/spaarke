using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;
using Sprk.Bff.Api.Api.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;

// SPIKE BRANCH ONLY (specialist-legal-agents-r1 task 011; spec FR-02, FR-03, NFR-05). NEVER MERGED.
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: McpToolResults.FromError (the isError shape) and DecisionToolCall (closed arguments, one telemetry line) in Decisions/.
//   Extension: it reuses McpToolResults.FromError; DecisionToolCall's argument check takes one optional argument and its errors use the
//     decision slice's reason codes, while the spike needs one REQUIRED argument and the FR-02 codes NO_PROFILE / CALLER_UNRESOLVED.
//   Cost of doing nothing: FR-02's fail-closed codes and FR-03's header-name record would not exist.

/// <summary>What the two spike tools share: their reason codes, the closed one-argument check, the header-name line and the telemetry line.</summary>
internal static class SpikeToolCall
{
    /// <summary>FR-02: the caller did not resolve to an active systemuser (or there is no signed-in caller).</summary>
    public const string CallerUnresolved = "CALLER_UNRESOLVED";

    /// <summary>FR-02: the caller resolved, but no test profile is keyed to that systemuserid for this function.</summary>
    public const string NoProfile = "NO_PROFILE";

    /// <summary>The call's arguments are not the tool's one argument.</summary>
    public const string InvalidArgument = "INVALID_ARGUMENT";

    /// <summary>The review could not run (Action missing, a position not bound, the model call failed, AI switched off).</summary>
    public const string ReviewFailed = "REVIEW_FAILED";

    public const string Ok = "ok";
    public const string Exception = "exception";

    /// <summary>Most header names one line records; the rest are counted, not named.</summary>
    public const int MaxHeaderNames = 64;

    /// <summary>Longest header name recorded; a longer one is cut.</summary>
    public const int MaxHeaderNameChars = 80;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>An <c>isError</c> result with <paramref name="reasonCode"/> and a fix-the-call message (fixed text; never a value).</summary>
    public static CallToolResult Error(string reasonCode, string message, int status, string? correlationId) =>
        McpToolResults.FromError(new DecisionToolError(reasonCode, message, status), correlationId);

    /// <summary>A successful result: <paramref name="text"/> as <c>content</c>, <paramref name="structured"/> as <c>structuredContent</c>.</summary>
    public static CallToolResult Success(string text, object structured) =>
        new()
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(structured, Json),
        };

    /// <summary>
    /// The value of the one REQUIRED string argument <paramref name="name"/>, or an <see cref="InvalidArgument"/> error when it is
    /// absent, not a non-blank string, longer than <paramref name="maxChars"/>, or accompanied by any other argument.
    /// </summary>
    public static bool TryReadRequiredString(
        IDictionary<string, JsonElement>? arguments, string toolName, string name, int maxChars, string? correlationId, out string value, out CallToolResult? error)
    {
        value = string.Empty;
        error = null;
        if (arguments is null || arguments.Keys.Any(k => !string.Equals(k, name, StringComparison.Ordinal)))
        {
            error = Error(InvalidArgument, $"{toolName} takes exactly one argument, '{name}'. Send the call again with only '{name}'.", 400, correlationId);
            return false;
        }

        if (!arguments.TryGetValue(name, out var element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
        {
            error = Error(InvalidArgument, $"{toolName} needs '{name}' as a non-empty string.", 400, correlationId);
            return false;
        }

        value = element.GetString()!;
        if (value.Length > maxChars)
        {
            error = Error(InvalidArgument, $"'{name}' is longer than {maxChars} characters.", 400, correlationId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// FR-03 / NFR-05: the NAMES of the request's headers, never a value. Sorted (ordinal, case-insensitive), capped at
    /// <see cref="MaxHeaderNames"/>, each cut to <see cref="MaxHeaderNameChars"/> and reduced to header-token characters, so the line
    /// carries nothing a caller can smuggle. <c>Authorization</c> and <c>Cookie</c> appear as names only, like any other header.
    /// </summary>
    public static IReadOnlyList<string> HeaderNames(HttpContext? http)
    {
        if (http is null)
        {
            return [];
        }

        return http.Request.Headers.Keys
            .Select(SafeName)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The one header-name line per call (FR-03). Names only; the count includes any names beyond the cap.</summary>
    public static void LogHeaderNames(ILogger logger, string toolName, HttpContext? http)
    {
        var names = HeaderNames(http);
        logger.LogInformation(
            "MCP spike tool {ToolName} request header names ({HeaderCount}): {HeaderNames}; correlation {CorrelationId}.",
            toolName,
            names.Count,
            string.Join(",", names.Take(MaxHeaderNames)),
            http?.TraceIdentifier);
    }

    /// <summary>The one telemetry line per call: tool, outcome, profileVersion (a test label, not an identity), latency, correlation.</summary>
    public static void LogCompleted(ILogger logger, string toolName, string outcome, string? profileVersion, TimeSpan elapsed, string? correlationId) =>
        logger.LogInformation(
            "MCP spike tool {ToolName} completed: outcome {Outcome}, profileVersion {ProfileVersion}, {ElapsedMs} ms, correlation {CorrelationId}.",
            toolName, outcome, profileVersion ?? "none", (long)elapsed.TotalMilliseconds, correlationId);

    /// <summary>The <c>outcome</c> of <paramref name="result"/>: <see cref="Ok"/> or the error's <c>reasonCode</c>.</summary>
    public static string OutcomeOf(CallToolResult result) =>
        result.IsError == true
        && result.StructuredContent is { ValueKind: JsonValueKind.Object } body
        && body.TryGetProperty("reasonCode", out var code)
        && code.ValueKind == JsonValueKind.String
            ? code.GetString()!
            : Ok;

    private static string SafeName(string name)
    {
        var kept = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        return kept.Length > MaxHeaderNameChars ? kept[..MaxHeaderNameChars] : kept;
    }
}
