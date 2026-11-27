using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;

// =====================================================================================================================
// SPIKE BRANCH ONLY: spike/specialist-agents-phase0 (specialist-legal-agents-r1 task 020/023; spec FR-06, G5). NEVER MERGED, never on
// master's catalogue.
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: spike_review_nda (SpikeReviewNdaTool) is the only G5 function tool on dev; SpikeToolCall holds the argument check and the
//     two log lines; SpikeGetProfileTool.ResolveCallerAsync the fail-closed caller lookup.
//   Extension: both stubs reuse all three. They are separate tools, not a mode of spike_review_nda, because G5 scores ROUTING by the
//     tool name the BFF logs for each prompt (BR-7): one tool per function is the thing under test.
//   Cost of doing nothing: G5's agreement-review and legal-hold prompts (24 of 36) cannot be scored, so FR-06 / D-A12 has no answer.
// =====================================================================================================================

/// <summary>
/// <c>spike_review_agreement</c> (G5 routing stub): the agreement-review function of the three-function test agent. It returns a fixed,
/// synthetic acknowledgement and reviews nothing; G5 scores which tool the model called, not a review.
/// </summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class SpikeReviewAgreementStubTool : McpToolDefinition
{
    public const string Name = "spike_review_agreement";

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    public const string Description =
        "Use for: reviewing a commercial agreement that is NOT an NDA (a services, master, licence, software, supply, vendor or consulting "
        + "agreement) against the attorney's positions in Spaarke. Pass the full text of the agreement as documentText, and present what "
        + "the tool returns. "
        + "Not for: NDAs or confidentiality agreements; legal hold or preservation notices; drafting, redlining, signing or sending a "
        + "document; questions with no document to review.";

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            StubAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "Review an agreement (Phase 0 routing stub)",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });
        tool.ProtocolTool.InputSchema = SpikeRoutingStub.InputSchema("agreement");
        return tool;
    }

    private static Task<CallToolResult> StubAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ISpecialistProfileSpikeAi spike,
        TimeProvider time,
        ILogger<SpikeReviewAgreementStubTool> logger,
        CancellationToken cancellationToken) =>
        SpikeRoutingStub.InvokeAsync(
            Name, SpikeRoutingStub.AgreementFunction, request.Params?.Arguments, accessor.HttpContext, spike, time, logger, cancellationToken);
}

/// <summary>
/// <c>spike_review_legal_hold</c> (G5 routing stub): the legal-hold-review function of the three-function test agent. It returns a fixed,
/// synthetic acknowledgement and reviews nothing; G5 scores which tool the model called, not a review.
/// </summary>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class SpikeReviewLegalHoldStubTool : McpToolDefinition
{
    public const string Name = "spike_review_legal_hold";

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    public const string Description =
        "Use for: reviewing a legal hold, litigation hold or preservation notice (its custodians, date range, data sources, preservation "
        + "instructions, acknowledgement and release) against the attorney's positions in Spaarke. Pass the full text of the notice as "
        + "documentText, and present what the tool returns. "
        + "Not for: NDAs or other agreements; explaining what a legal hold is; drafting, sending or releasing a hold; questions with no "
        + "notice to review.";

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            StubAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "Review a legal hold notice (Phase 0 routing stub)",
                Description = Description,
                ReadOnly = true,
                Idempotent = true,
                Destructive = false,
                OpenWorld = false,
            });
        tool.ProtocolTool.InputSchema = SpikeRoutingStub.InputSchema("legal hold notice");
        return tool;
    }

    private static Task<CallToolResult> StubAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ISpecialistProfileSpikeAi spike,
        TimeProvider time,
        ILogger<SpikeReviewLegalHoldStubTool> logger,
        CancellationToken cancellationToken) =>
        SpikeRoutingStub.InvokeAsync(
            Name, SpikeRoutingStub.LegalHoldFunction, request.Params?.Arguments, accessor.HttpContext, spike, time, logger, cancellationToken);
}

/// <summary>
/// What the two G5 routing stubs share. Each call: the closed one-argument check, then the fail-closed caller lookup (a stub answers
/// only a resolved Spaarke user, like the real spike tools), then a FIXED acknowledgement. No Action, model, Dataverse read or write and
/// no profile: the result depends only on the tool and the document length.
/// </summary>
internal static class SpikeRoutingStub
{
    public const string DocumentTextArgument = "documentText";

    public const string AgreementFunction = "agreement-review";

    public const string LegalHoldFunction = "legal-hold-review";

    /// <summary>The <c>stub</c> marker in <c>structuredContent</c>, so a tester never reads a stub result as a review.</summary>
    public const string StubMarker = "g5-routing-stub";

    /// <summary>The closed one-argument input schema: a required <c>documentText</c> string, nothing else.</summary>
    public static JsonElement InputSchema(string documentKind) =>
        JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                [DocumentTextArgument] = new JsonObject
                {
                    ["type"] = "string",
                    ["maxLength"] = SpecialistProfileSpike.MaxDocumentTextChars,
                    ["description"] = $"Required. The full text of the {documentKind} to review.",
                },
            },
            ["required"] = new JsonArray(DocumentTextArgument),
            ["additionalProperties"] = false,
        });

    /// <summary>One call with its two log lines (header names first, the telemetry line last, however the call ends).</summary>
    internal static async Task<CallToolResult> InvokeAsync(
        string toolName,
        string function,
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ISpecialistProfileSpikeAi spike,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        SpikeToolCall.LogHeaderNames(logger, toolName, http);
        var outcome = SpikeToolCall.Exception;
        SpikeToolRun? run = null;
        var documentChars = DocumentCharsOf(arguments);
        try
        {
            run = await RunAsync(toolName, function, arguments, http, spike, documentChars, cancellationToken);
            outcome = SpikeToolCall.OutcomeOf(run.Result);
            return run.Result;
        }
        finally
        {
            SpikeToolCall.LogCompleted(
                logger, toolName, SpikeToolCall.FinalOutcome(outcome, cancellationToken), run?.Reason, null, documentChars,
                time.GetElapsedTime(started), http?.TraceIdentifier);
        }
    }

    /// <summary>The length of this stub's <c>documentText</c> argument as received (before any check), or null when absent or not a string.</summary>
    internal static int? DocumentCharsOf(IDictionary<string, JsonElement>? arguments) =>
        arguments is not null
        && arguments.TryGetValue(DocumentTextArgument, out var element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString()!.Length
            : null;

    private static async Task<SpikeToolRun> RunAsync(
        string toolName,
        string function,
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ISpecialistProfileSpikeAi spike,
        int? documentChars,
        CancellationToken cancellationToken)
    {
        var correlation = http?.TraceIdentifier;
        if (!SpikeToolCall.TryReadRequiredString(
                arguments, toolName, DocumentTextArgument, SpecialistProfileSpike.MaxDocumentTextChars, correlation, out _, out var argumentError))
        {
            return new SpikeToolRun(argumentError!, null, null, documentChars);
        }

        var (callerError, _, reason) = await SpikeGetProfileTool.ResolveCallerAsync(http, spike, cancellationToken);
        if (callerError != null)
        {
            return new SpikeToolRun(callerError, null, reason, documentChars);
        }

        var text = $"Phase 0 routing test: the {function} function was reached ({toolName}, {documentChars} document characters "
                   + "received). This is a test stub: it did not review the document and returns no findings. Tell the user, in one "
                   + "sentence, that the request reached the " + function + " function and that this test does not produce a review. "
                   + "Do not review the document yourself and do not call another tool for it.";
        return new SpikeToolRun(
            SpikeToolCall.Success(text, new SpikeRoutingStubContent(StubMarker, function, toolName, documentChars ?? 0)), null, null, documentChars);
    }
}

/// <summary><c>structuredContent</c> of a G5 routing stub: the marker, the function reached, the tool, the document length received.</summary>
internal sealed record SpikeRoutingStubContent(string Stub, string Function, string Tool, int DocumentChars);
