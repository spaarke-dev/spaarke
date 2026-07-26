using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.LinearConsumers;

/// <summary>
/// Resolves an <see cref="AiModelTier"/> (the catalog vocabulary, ADR-016) to a concrete Azure OpenAI
/// deployment name (the runtime routing surface — config, not catalog per ADR-016). Completes the
/// dead-ended model-tier selection: previously <c>ActionRunner</c> passed <c>model: null</c>
/// unconditionally, which fell through <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/> to
/// <see cref="DocumentIntelligenceOptions.SummarizeModel"/> (<c>gpt-4o-mini</c>) regardless of the
/// Action's declared tier.
/// </summary>
/// <remarks>
/// <para>
/// ai-advanced-capabilities-nda-r1 task 010. A pure, stateless config lookup — deliberately NOT a
/// DI-registered service (no external calls, no state): mirrors how the sibling <c>temperature</c>
/// plumbing in <see cref="ActionRunner"/> is a plain inline computation, not a separate service. This
/// keeps the addition free of ADR-032 Null-Object / DI-symmetry concerns — there is nothing to gate,
/// because <see cref="ActionRunner"/> itself (the sole caller) is already gated behind the compound AI
/// kill switch with its own Null-Object peer (<see cref="NullActionRunner"/>).
/// </para>
/// <para>
/// <b>Single routing surface (ADR-016 / ADR-039)</b>: this is the ONLY tier→deployment mapping on the
/// platform. Do not introduce a second lookup (e.g. a per-consumer override table) — task 011's runtime
/// picker (<c>sprk_playbookconsumer.sprk_modeltieroverride</c> / <c>Binding.EffectiveModelTier</c>)
/// composes with this same resolver rather than replacing it.
/// </para>
/// </remarks>
public static class ModelTierDeploymentResolver
{
    /// <summary>
    /// Resolve <paramref name="tier"/> to a deployment name. Null/unspecified defaults to the Standard
    /// tier (the platform default per ADR-016). Reasoning falls back to
    /// <see cref="DocumentIntelligenceOptions.StandardModel"/> when
    /// <see cref="DocumentIntelligenceOptions.ReasoningModel"/> is not yet configured (interim state
    /// until the o-series deployment lands — see ai-advanced-capabilities-nda-r1 task 013), so a
    /// Reasoning-tagged Action still executes rather than 404ing against an undeployed model.
    /// </summary>
    public static string Resolve(AiModelTier? tier, DocumentIntelligenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return tier switch
        {
            AiModelTier.Fast => options.FastModel,
            AiModelTier.Reasoning => !string.IsNullOrWhiteSpace(options.ReasoningModel)
                ? options.ReasoningModel!
                : options.StandardModel,
            AiModelTier.Standard => options.StandardModel,
            _ => options.StandardModel,
        };
    }
}
