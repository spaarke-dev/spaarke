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
/// Fails closed exactly as <see cref="SpikeGetProfileTool"/> (<c>CALLER_UNRESOLVED</c>, <c>NO_PROFILE</c>), before any Action is read or
/// run. A run that cannot complete is <c>REVIEW_FAILED</c>, never a review against another profile or the generic standard.
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

    private static async Task<CallToolResult> ReviewAsync(
        RequestContext<CallToolRequestParams> request,
        IHttpContextAccessor accessor,
        ISpecialistProfileSpikeAi spike,
        TimeProvider time,
        ILogger<SpikeReviewNdaTool> logger,
        CancellationToken cancellationToken)
    {
        var started = time.GetTimestamp();
        var http = accessor.HttpContext;
        SpikeToolCall.LogHeaderNames(logger, Name, http);
        var outcome = SpikeToolCall.Exception;
        string? version = null;
        try
        {
            var (result, profile) = await RunAsync(request.Params?.Arguments, http, spike, SpikeTestProfiles.Default, time, logger, cancellationToken);
            version = profile?.ProfileVersion;
            outcome = SpikeToolCall.OutcomeOf(result);
            return result;
        }
        finally
        {
            SpikeToolCall.LogCompleted(logger, Name, outcome, version, time.GetElapsedTime(started), http?.TraceIdentifier);
        }
    }

    /// <summary>The tool's work, with the profile set passed in (production: the embedded set).</summary>
    internal static async Task<(CallToolResult Result, SpikeTestProfile? Profile)> RunAsync(
        IDictionary<string, JsonElement>? arguments,
        HttpContext? http,
        ISpecialistProfileSpikeAi spike,
        SpikeTestProfileSet profiles,
        TimeProvider time,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var correlation = http?.TraceIdentifier;
        if (!SpikeToolCall.TryReadRequiredString(
                arguments, Name, DocumentTextArgument, SpecialistProfileSpike.MaxDocumentTextChars, correlation, out var documentText, out var argumentError))
        {
            return (argumentError!, null);
        }

        var (callerError, profile) = await SpikeGetProfileTool.ResolveProfileAsync(http, spike, profiles, SpikeTestProfiles.Nda, cancellationToken);
        if (callerError != null)
        {
            return (callerError, null);
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
        catch (Exception ex)
        {
            // The exception's type and message only (they name the Action, an input or the failing call; never the document or a token).
            logger.LogWarning(
                "MCP spike tool {ToolName}: the review did not run ({ExceptionType}: {ExceptionMessage}); correlation {CorrelationId}.",
                Name, ex.GetType().Name, ex.Message, correlation);
            return (SpikeToolCall.Error(SpikeToolCall.ReviewFailed,
                "The review could not be run. Tell the user it failed and give the reference; do not review the document another way.", 502, correlation), profile);
        }

        var latencyMs = (long)time.GetElapsedTime(runStarted).TotalMilliseconds;
        var text = $"NDA review against your profile {profile.ProfileVersion} (Action {run.ActionCode}, model tier {run.AppliedModelTier}, "
                   + $"{latencyMs} ms). The structured review follows as JSON.\n{run.Review.GetRawText()}";
        var structured = new SpikeReviewContent(profile.ProfileVersion, run.ActionCode, run.AppliedModelTier, latencyMs, run.Review);
        return (SpikeToolCall.Success(text, structured), profile);
    }
}

/// <summary><c>structuredContent</c> of <c>spike_review_nda</c>.</summary>
internal sealed record SpikeReviewContent(string ProfileVersion, string ActionCode, string ModelTier, long LatencyMs, JsonElement Review);
