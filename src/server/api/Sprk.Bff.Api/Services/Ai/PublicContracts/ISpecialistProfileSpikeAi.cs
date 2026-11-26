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
    /// profile's model tier applied to the Action explicitly. Throws when the Action is missing, a position is missing or did not reach
    /// the model input, the tier is unknown, or the run fails; the caller turns that into an <c>isError</c> tool result.
    /// </summary>
    Task<SpikeReviewResult> RunAgreementReviewAsync(SpikeReviewRequest request, CancellationToken cancellationToken);
}

/// <summary>The caller resolution: the <c>systemuserid</c> ("D" GUID) or an identifier-only reason.</summary>
public sealed record SpikeCallerResult(bool IsResolved, string? SystemUserId, string? UnresolvedReason)
{
    public static SpikeCallerResult Resolved(string systemUserId) => new(true, systemUserId, null);

    public static SpikeCallerResult Unresolved(string reason) => new(false, null, reason);
}

/// <summary>One review: the caller (for the binder's user slice and the tenant), the document text, the five positions by declared
/// input name, and the profile's model tier name (<c>Fast</c>, <c>Standard</c> or <c>Reasoning</c>).</summary>
public sealed record SpikeReviewRequest(
    ClaimsPrincipal Caller,
    string DocumentText,
    IReadOnlyDictionary<string, JsonElement> Positions,
    string ModelTier,
    string? CorrelationId);

/// <summary>The structured review, the Action code run, the tier applied and the input names that reached the model.</summary>
public sealed record SpikeReviewResult(JsonElement Review, string ActionCode, string AppliedModelTier, IReadOnlyList<string> BoundInputNames);

/// <summary>The spike's fixed contract with the DEV <c>agreement-review-spike</c> row (task 010).</summary>
public static class SpecialistProfileSpike
{
    /// <summary>The DEV-only Action, resolved by this code (never by row id; the row is recorded in task 010's action-check.md). Never the production row.</summary>
    public const string ActionCode = "agreement-review-spike";

    /// <summary>The linear-run consumer type (telemetry only; no routing row is read).</summary>
    public const string ConsumerType = "specialist-agents-spike";

    /// <summary>The operand input.</summary>
    public const string DocumentTextInput = "documentText";

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
/// ADR-032 P3 peer for the compound-AI-OFF path: the caller never resolves (so the tools answer <c>CALLER_UNRESOLVED</c>, never a
/// profile) and a review throws <see cref="Sprk.Bff.Api.Configuration.FeatureDisabledException"/>.
/// </summary>
public sealed class NullSpecialistProfileSpikeAi : ISpecialistProfileSpikeAi
{
    public Task<SpikeCallerResult> ResolveCallerAsync(ClaimsPrincipal? caller, CancellationToken cancellationToken) =>
        Task.FromResult(SpikeCallerResult.Unresolved("ai-disabled"));

    public Task<SpikeReviewResult> RunAgreementReviewAsync(SpikeReviewRequest request, CancellationToken cancellationToken) =>
        throw new Sprk.Bff.Api.Configuration.FeatureDisabledException("ai.disabled", "AI is not enabled in this environment.");
}
