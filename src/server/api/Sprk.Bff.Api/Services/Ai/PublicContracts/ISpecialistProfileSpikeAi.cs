using System.Security.Claims;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

// =====================================================================================================================
// SPIKE BRANCH ONLY: spike/specialist-agents-phase0 (specialist-legal-agents-r1 task 011; spec FR-02, FR-03). NEVER MERGED.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md section 10): the ADR-013 PublicContracts facade the two spike MCP tools
// (Api/Mcp/LegalOperations/Spike) call. Api/Mcp may not name an AI-internal type (RouteAuthorizationGuardTests.Mcp), so the caller
// resolution (ICallerSystemUserResolver) and the Action run (IContextBinder + IActionRunner) sit behind this seam. No package, no
// background work, no new route.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: ICallerSystemUserResolver (caller -> systemuserid), IActionRunner BoundInputs overload, IContextBinder (declared inputs),
//     AnalysisActionService.GetActionByCodeAsync; IFileSummarizeAi is the facade shape copied.
//   Extension: a thin wrapper over exactly those; the only new logic is checking that every profile position reached the model input
//     and applying the profile's model tier explicitly (#1934).
//   Cost of doing nothing: the spike tools could not resolve the caller or run the review without breaking the ADR-013 MCP guard, so
//     G1 variant (c) and G3 have no evidence.
// =====================================================================================================================

/// <summary>
/// SPIKE ONLY. The specialist-agents Phase 0 seam: resolves the MCP caller to a Dataverse <c>systemuserid</c> and runs the DEV-only
/// <c>agreement-review-spike</c> Action with the caller's test-profile positions bound as declared inputs (ADR-043).
/// </summary>
public interface ISpecialistProfileSpikeAi
{
    /// <summary>
    /// The caller's <c>systemuserid</c> through <c>ICallerSystemUserResolver</c> (oid claim -> active systemuser). Never a guess: an
    /// unresolvable caller is <see cref="SpikeCallerResult.IsResolved"/> false.
    /// </summary>
    Task<SpikeCallerResult> ResolveCallerAsync(ClaimsPrincipal? caller, CancellationToken cancellationToken);

    /// <summary>
    /// Runs <see cref="SpecialistProfileSpike.ActionCode"/> over the document with the five positions as declared inputs and the
    /// profile's model tier applied to the Action explicitly. The caller's own user context (stated profile, org, memory, preferences)
    /// is replaced by <see cref="SpecialistProfileSpike.NeutralUserFragment"/>, so the positions are the only per-user difference.
    /// Throws <see cref="SpikeModelTierUnavailableException"/> when the tier has no configured deployment (never a fallback model), and
    /// throws when the Action is missing, a position is missing or did not reach the model input, the tier is unknown, or the run
    /// fails; the caller turns that into an <c>isError</c> tool result.
    /// </summary>
    Task<SpikeReviewResult> RunAgreementReviewAsync(SpikeReviewRequest request, CancellationToken cancellationToken);
}

/// <summary>The caller resolution: the <c>systemuserid</c> ("D" GUID) or an identifier-only reason.</summary>
public sealed record SpikeCallerResult(bool IsResolved, string? SystemUserId, string? UnresolvedReason)
{
    /// <summary>The resolver's reason when the Dataverse lookup threw (a transient failure, not an unmatched user).</summary>
    public const string LookupFailedReason = "lookup-failed";

    /// <summary>The Null facade's reason: AI is switched off in this environment.</summary>
    public const string AiDisabledReason = "ai-disabled";

    public static SpikeCallerResult Resolved(string systemUserId) => new(true, systemUserId, null);

    public static SpikeCallerResult Unresolved(string reason) => new(false, null, reason);
}

/// <summary>
/// The profile's model tier has no configured deployment (for example <c>Reasoning</c> with <c>DocumentIntelligence:ReasoningModel</c>
/// empty). The review is refused rather than run on the resolver's fallback model while reporting the requested tier.
/// </summary>
public sealed class SpikeModelTierUnavailableException(string modelTier)
    : InvalidOperationException($"The model tier '{modelTier}' has no configured deployment in this environment; the review was not run.")
{
    /// <summary>The tier that was requested (a name such as <c>Reasoning</c>).</summary>
    public string ModelTier { get; } = modelTier;
}

/// <summary>One review: the caller (for the binder's user slice and the tenant), the document text, the five positions by declared
/// input name, and the profile's model tier name (<c>Fast</c>, <c>Standard</c> or <c>Reasoning</c>).</summary>
public sealed record SpikeReviewRequest(
    ClaimsPrincipal Caller,
    string DocumentText,
    IReadOnlyDictionary<string, JsonElement> Positions,
    string ModelTier,
    string? CorrelationId);

/// <summary>
/// The structured review, the Action code run, the tier the profile requested, the deployment that tier resolved to (the model actually
/// called) and the input names that reached the model.
/// </summary>
public sealed record SpikeReviewResult(
    JsonElement Review, string ActionCode, string RequestedModelTier, string ModelDeployment, IReadOnlyList<string> BoundInputNames);

/// <summary>The spike's fixed contract with the DEV <c>agreement-review-spike</c> row (task 010).</summary>
public static class SpecialistProfileSpike
{
    /// <summary>The DEV-only Action, resolved by this code (never by row id; the row is recorded in task 010's action-check.md). Never the production row.</summary>
    public const string ActionCode = "agreement-review-spike";

    /// <summary>The linear-run consumer type (telemetry only; no routing row is read).</summary>
    public const string ConsumerType = "specialist-agents-spike";

    /// <summary>The operand input.</summary>
    public const string DocumentTextInput = "documentText";

    /// <summary>
    /// The explicit User-slice fragment for every spike review. An explicit fragment wins over the binder's self-produced one, so the
    /// caller's own stated profile, org (business unit and teams), memory recall and preference hints never reach the review prompt:
    /// the five positions are the only per-user difference between two testers' runs.
    /// </summary>
    public const string NeutralUserFragment =
        "### Reviewer context\nNo personal profile, memory or preferences apply to this review. Apply only the positions given as inputs.";

    /// <summary>The largest document the row's input schema accepts.</summary>
    public const int MaxDocumentTextChars = 60000;

    /// <summary>The five profile positions, exactly as the row declares them (task 010 read-back).</summary>
    public static readonly IReadOnlyList<string> PositionInputs = ["term", "residuals", "nonSolicit", "governingLaw", "uncappedIndemnityFlag"];

    /// <summary>
    /// The declared input names of the DEV row, given to the binder so it admits the companions (ADR-043: only declared inputs reach the
    /// model). A mirror of the row's <c>sprk_inputschema</c> property names, because <c>GetActionByCodeAsync</c> does not read that
    /// column; the binder reads property names only.
    /// </summary>
    internal const string DeclaredInputSchemaJson =
        """{"type":"object","properties":{"documentText":{"type":"string"},"term":{"type":"string"},"residuals":{"type":"string"},"nonSolicit":{"type":"string"},"governingLaw":{"type":"string"},"uncappedIndemnityFlag":{"type":"boolean"}},"required":["documentText"]}""";
}

/// <summary>
/// ADR-032 P3 peer for the compound-AI-OFF path: the caller never resolves (reason <see cref="SpikeCallerResult.AiDisabledReason"/>, so
/// the tools answer <c>AI_DISABLED</c>, never a profile) and a review throws <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/>.
/// </summary>
public sealed class NullSpecialistProfileSpikeAi : ISpecialistProfileSpikeAi
{
    public Task<SpikeCallerResult> ResolveCallerAsync(ClaimsPrincipal? caller, CancellationToken cancellationToken) =>
        Task.FromResult(SpikeCallerResult.Unresolved(SpikeCallerResult.AiDisabledReason));

    public Task<SpikeReviewResult> RunAgreementReviewAsync(SpikeReviewRequest request, CancellationToken cancellationToken) =>
        throw new Sprk.Bff.Api.Configuration.FeatureDisabledException("ai.disabled", "AI is not enabled in this environment.");
}
