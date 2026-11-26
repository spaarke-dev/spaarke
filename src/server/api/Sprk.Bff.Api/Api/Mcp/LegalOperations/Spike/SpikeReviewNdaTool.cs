using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Spike;

// =====================================================================================================================
// SPIKE BRANCH ONLY: spike/specialist-agents-phase0 (specialist-legal-agents-r1 task 011; spec FR-02, FR-04 variant (c), NFR-04). NEVER
// MERGED, never on master's catalogue. Placement and component justification: as SpikeGetProfileTool (notes/reuse-scan.md "Server-side
// review with a profile": IActionRunner, the action-based model, behind ISpecialistProfileSpikeAi).
// =====================================================================================================================

/// <summary>
/// <c>spike_review_nda</c> (FR-02, G1 variant (c)): reviews the NDA text the model passes against the signed-in caller's own NDA test
/// profile, server-side. The DEV-only <c>agreement-review-spike</c> Action runs through <c>IActionRunner</c> with the five positions as
/// declared inputs and the profile's model tier applied explicitly; the result carries the review, <c>profileVersion</c> and latency.
/// Read-only and model-visible.
/// </summary>
/// <remarks>
/// Fails closed exactly as <see cref="SpikeGetProfileTool"/> (<c>CALLER_UNRESOLVED</c>, <c>CALLER_LOOKUP_FAILED</c>, <c>AI_DISABLED</c>,
/// <c>NO_PROFILE</c>), before any Action is read or run. A profile tier with no configured deployment is <c>MODEL_TIER_UNAVAILABLE</c>
/// (never a run on a fallback model). A run that cannot complete is <c>REVIEW_FAILED</c> with the reference in its text, never a review
/// against another profile or the generic standard.
/// </remarks>
[McpArea(McpEndpoints.LegalOperationsArea)]
[Authorize]
internal sealed class SpikeReviewNdaTool : McpToolDefinition
{
    public const string Name = "spike_review_nda";

    public const string DocumentTextArgument = "documentText";

    /// <summary>The description the model reads (at most 1,024 characters, "use for / not for").</summary>
    public const string Description =
        "Use for: reviewing a mutual NDA (non-disclosure agreement) against the signed-in attorney's own NDA positions in Spaarke. Pass "
        + "the full text of the NDA as documentText. Spaarke applies the attorney's profile and returns the structured review (findings "
        + "with risk levels and recommendations) and the profileVersion it used; present that result to the user. If it returns "
        + "NO_PROFILE or CALLER_UNRESOLVED, stop and say so. "
        + "Not for: other agreement types; drafting, redlining, signing or sending a document; changing the attorney's positions.";

    private static readonly JsonElement InputSchema = JsonSerializer.SerializeToElement(new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            [DocumentTextArgument] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = SpecialistProfileSpike.MaxDocumentTextChars,
                ["description"] = "Required. The full text of the NDA to review.",
            },
        },
        ["required"] = new JsonArray(DocumentTextArgument),
        ["additionalProperties"] = false,
    });

    public override McpServerTool Create(IServiceProvider services)
    {
        var tool = McpServerTool.Create(
            ReviewAsync,
            new McpServerToolCreateOptions
            {
                Services = services,
                Name = Name,
                Title = "Review an NDA against my profile (Phase 0 spike)",
                Description = Description,
                ReadOnly = true,
                Idempotent = false,
                Destructive = false,
                OpenWorld = false,
            });
        tool.ProtocolTool.InputSchema = InputSchema;
        return tool;
    }

    private static Task<CallToolResult> ReviewAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ISpecialistProfileSpikeAi spike,
        TimeProvider time,
        ILogger<SpikeReviewNdaTool> logger,
        CancellationToken cancellationToken) =>
        InvokeAsync(request.Params?.Arguments, accessor.HttpContext, spike, SpikeTestProfiles.Default, time, logger, cancellationToken);

    /// <summary>One call with its two log lines (header names first, the telemetry line last, however the call ends).</summary>
    internal static async Task<CallToolResult> InvokeAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ISpecialistProfileSpikeAi spike,
        SpikeTestProfileSet profiles,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        SpikeToolCall.LogHeaderNames(logger, Name, http);
        var outcome = SpikeToolCall.Exception;
        SpikeToolRun? run = null;
        try
        {
            run = await RunAsync(arguments, http, spike, profiles, time, logger, cancellationToken);
            outcome = SpikeToolCall.OutcomeOf(run.Result);
            return run.Result;
        }
        finally
        {
            SpikeToolCall.LogCompleted(
                logger, Name, SpikeToolCall.FinalOutcome(outcome, cancellationToken), run?.Reason, run?.Profile?.ProfileVersion,
                run?.DocumentChars ?? DocumentCharsOf(arguments), time.GetElapsedTime(started), http?.TraceIdentifier);
        }
    }

    /// <summary>The tool's work, with the profile set passed in (production: the embedded set).</summary>
    internal static async Task<SpikeToolRun> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ISpecialistProfileSpikeAi spike,
        SpikeTestProfileSet profiles,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var correlation = http?.TraceIdentifier;
        var documentChars = DocumentCharsOf(arguments);
        if (!SpikeToolCall.TryReadRequiredString(
                arguments, Name, DocumentTextArgument, SpecialistProfileSpike.MaxDocumentTextChars, correlation, out var documentText, out var argumentError))
        {
            return new SpikeToolRun(argumentError!, null, null, documentChars);
        }

        var (callerError, profile, reason) = await SpikeGetProfileTool.ResolveProfileAsync(http, spike, profiles, SpikeTestProfiles.Nda, cancellationToken);
        if (callerError != null)
        {
            return new SpikeToolRun(callerError, null, reason, documentChars);
        }

        var runStarted = time.GetTimestamp();
        SpikeReviewResult run;
        try
        {
            run = await spike.RunAgreementReviewAsync(
                new SpikeReviewRequest(http!.User, documentText, profile!.ToReviewInputs(), profile.ModelTier, correlation),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SpikeModelTierUnavailableException ex)
        {
            logger.LogWarning(ex,
                "MCP spike tool {ToolName}: the review did not run, model tier {ModelTier} has no configured deployment; correlation {CorrelationId}.",
                Name, ex.ModelTier, correlation);
            return new SpikeToolRun(SpikeToolCall.Error(SpikeToolCall.ModelTierUnavailable,
                $"The review was not run: the profile's model tier '{ex.ModelTier}' is not configured in this Spaarke environment, and the spike "
                + $"never runs on a substitute model. Tell the user, and report it with the reference {correlation ?? "none"}; do not review the "
                + "document another way.", 503, correlation), profile, ex.ModelTier, documentChars);
        }
        catch (Exception ex)
        {
            // The exception object, so the stack trace is kept. Its message names the Action, an input or the failing call; the facade
            // never puts the document or a token in one.
            logger.LogWarning(ex,
                "MCP spike tool {ToolName}: the review did not run ({ExceptionType}); correlation {CorrelationId}.",
                Name, ex.GetType().Name, correlation);
            return new SpikeToolRun(SpikeToolCall.Error(SpikeToolCall.ReviewFailed,
                "The review could not be run. Tell the user it failed and give the reference; do not review the document another way. "
                + $"Reference: {correlation ?? "none"}.", 502, correlation), profile, ex.GetType().Name, documentChars);
        }

        var latencyMs = (long)time.GetElapsedTime(runStarted).TotalMilliseconds;
        var text = $"NDA review against your profile {profile.ProfileVersion} (Action {run.ActionCode}, model tier {run.RequestedModelTier}, "
                   + $"deployment {run.ModelDeployment}, {latencyMs} ms, {documentChars} document characters received). "
                   + $"The structured review follows as JSON.\n{run.Review.GetRawText()}";
        var structured = new SpikeReviewContent(
            profile.ProfileVersion, run.ActionCode, run.RequestedModelTier, run.ModelDeployment, latencyMs, documentChars ?? 0, run.Review);
        return new SpikeToolRun(SpikeToolCall.Success(text, structured), profile, null, documentChars);
    }

    /// <summary>The length of the <c>documentText</c> argument as received (before any check), or null when it is absent or not a string.</summary>
    internal static int? DocumentCharsOf(IDictionary<string, JsonElement>? arguments) =>
        arguments is not null
        && arguments.TryGetValue(DocumentTextArgument, out var element)
        && element.ValueKind == JsonValueKind.String
            ? element.GetString()!.Length
            : null;
}

/// <summary>
/// <c>structuredContent</c> of <c>spike_review_nda</c>: the profile's requested tier, the deployment actually called, and the document
/// length received (so a tester can spot truncated input).
/// </summary>
internal sealed record SpikeReviewContent(
    string ProfileVersion, string ActionCode, string ModelTier, string ModelDeployment, long LatencyMs, int DocumentChars, JsonElement Review);
