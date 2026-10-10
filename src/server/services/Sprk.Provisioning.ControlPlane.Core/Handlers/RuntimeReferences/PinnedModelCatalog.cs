// -----------------------------------------------------------------------------
// PinnedModelCatalog.cs
//
// The Azure OpenAI deployments every customer stamp creates — the C# mirror of
// infrastructure/bicep/modules/openai.bicep's `deployments` default and
// customer.bicep's `openAiLocation` default. ONE list, read by:
//   - H0  azure-openai-tpm-headroom (quota per SKU + model, in the OpenAI region)
//   - H0  openai-pin-freshness      (each model version deployable in that region)
//   - H12c runtime references        (one sprk_aimodeldeployment row per deployment NAME)
// ArmTemplateInspectorTests.PinnedModelCatalog_MatchesTheCompiledCustomerTemplate fails when
// this file and customer.json disagree (task 247; before it, H0 checked a model the template
// no longer deployed, in the wrong region, and an outdated gpt-4o version).
//
// DEPLOYMENT NAME vs MODEL (task 247, owner 2026-10-06): the BFF addresses deployments by NAME
// (gpt-4o, gpt-4o-mini, text-embedding-3-large — hard-coded across its AI code). gpt-4o-mini
// 2024-07-18 is Deprecating (Azure blocks new deployments), so the `gpt-4o-mini` deployment runs
// gpt-4.1-mini 2025-04-14 — same API shape, no BFF change. SKU DataZoneStandard (owner: prompts
// are processed inside the US data zone); fresh subscriptions auto-grant enough of it.
//
// DESIGN REF:
//   - ADR-020 (versioning) — model deployments MUST be pinned to specific
//     versions, never "latest". H2a's ArmTemplateInspector asserts the
//     deployed template pins every version (BicepDeployRejectionCodes.ModelVersionNotPinned).
//   - Parity with H2b's ICanonicalIndexCatalog
//     pattern (a single source of truth other handlers can diff against).
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-17 acceptance:
//     "Endpoint URIs reference pinned model deployment versions per ADR-020."
//     H12c satisfies this by writing runtime-reference rows for EXACTLY this
//     3-model catalog — no ad-hoc model names, no unpinned "latest" entries.
//
// LIVE DATAVERSE SCHEMA (verified via mcp__dataverse__describe,
// 2026-08-17 — see notes/task-072-h12c-schema-verification.md): the
// `sprk_aimodeldeployment` table's `sprk_capability` choice column has
// integer values Chat=0 / Completion=1 / Embedding=2 and `sprk_provider`
// has AzureOpenAI=0 / OpenAI=1 / Anthropic=2 — the enums below mirror those
// integer values exactly so the writer can serialize them directly as
// choice-field ints in the Dataverse Web API payload.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;

/// <summary>
/// Mirrors <c>sprk_aimodeldeployment.sprk_capability</c> choice values
/// verbatim (Chat=0 / Completion=1 / Embedding=2, verified against the live
/// Dataverse schema).
/// </summary>
public enum ModelCapability
{
    Chat = 0,
    Completion = 1,
    Embedding = 2,
}

/// <summary>
/// Mirrors <c>sprk_aimodeldeployment.sprk_provider</c> choice values verbatim
/// (AzureOpenAI=0 / OpenAI=1 / Anthropic=2, verified against the live
/// Dataverse schema). Spaarke's r1 provisioning scope only ever writes
/// <see cref="AzureOpenAI"/> rows — the other two members exist purely to
/// keep the enum a faithful mirror of the choice column.
/// </summary>
public enum ModelProvider
{
    AzureOpenAI = 0,
    OpenAI = 1,
    Anthropic = 2,
}

/// <summary>
/// A distinct pinned model (model + ADR-020 version) that the stamp deployments run — what H0's
/// pin-freshness probe checks against the region's model list.
/// </summary>
/// <param name="ModelId">Azure OpenAI model identifier (ARM model-list <c>model.name</c>), e.g. <c>gpt-4.1-mini</c>.</param>
/// <param name="PinnedVersion">ADR-020-pinned model version string (e.g. <c>2025-04-14</c>, or <c>1</c> for the embedding model's initial GA version).</param>
/// <param name="Capability">Model capability — drives <c>sprk_capability</c>.</param>
public sealed record PinnedModel(string ModelId, string PinnedVersion, ModelCapability Capability);

/// <summary>
/// One OpenAI model deployment a customer stamp creates (a row of openai.bicep's <c>deployments</c> default).
/// </summary>
/// <param name="Name">Deployment name — what the BFF calls and H12c writes as <c>sprk_name</c>.</param>
/// <param name="ModelId">The Azure OpenAI model the deployment runs (ARM model-list <c>model.name</c>).</param>
/// <param name="Version">Pinned model version (ADR-020).</param>
/// <param name="Sku">Deployment SKU, e.g. <c>DataZoneStandard</c>.</param>
/// <param name="Capacity">Capacity in thousands of tokens per minute.</param>
/// <param name="QuotaName">
/// Azure's regional quota (usage) <c>name.value</c> for this SKU + model — spelled by Azure, not derived:
/// e.g. <c>OpenAI.DataZoneStandard.gpt4.1-mini</c> (no hyphen after "gpt"), <c>OpenAI.DataZoneStandard.gpt-4o</c>.
/// </param>
/// <param name="Capability">Model capability — drives <c>sprk_capability</c>.</param>
public sealed record PinnedDeployment(
    string Name, string ModelId, string Version, string Sku, int Capacity, string QuotaName, ModelCapability Capability);

/// <summary>
/// The customer-stamp OpenAI deployment set and region (task 247 — see the file header).
/// </summary>
public static class PinnedModelCatalog
{
    /// <summary>customer.bicep's <c>openAiLocation</c> default — the region a stamp's OpenAI account deploys to.</summary>
    public const string DefaultOpenAiLocation = "westus3";

    /// <summary>The deployments, in openai.bicep order.</summary>
    public static readonly IReadOnlyList<PinnedDeployment> Deployments = new[]
    {
        new PinnedDeployment("gpt-4o", "gpt-4o", "2024-11-20", "DataZoneStandard", 150,
            "OpenAI.DataZoneStandard.gpt-4o", ModelCapability.Chat),
        new PinnedDeployment("gpt-4o-mini", "gpt-4.1-mini", "2025-04-14", "DataZoneStandard", 200,
            "OpenAI.DataZoneStandard.gpt4.1-mini", ModelCapability.Chat),
        new PinnedDeployment("text-embedding-3-large", "text-embedding-3-large", "1", "DataZoneStandard", 350,
            "OpenAI.DataZoneStandard.text-embedding-3-large", ModelCapability.Embedding),
    };

    /// <summary>The distinct models (and pinned versions) the deployments run — what H0's freshness probe checks.</summary>
    public static readonly IReadOnlyList<PinnedModel> Models = Deployments
        .GroupBy(d => (d.ModelId, d.Version))
        .Select(g => new PinnedModel(g.Key.ModelId, g.Key.Version, g.First().Capability))
        .ToArray();

    /// <summary>Requested TPM per Azure quota name — the summed capacity of the deployments that draw on it.</summary>
    public static readonly IReadOnlyDictionary<string, int> RequestedTpmByQuotaName = Deployments
        .GroupBy(d => d.QuotaName, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.Sum(d => d.Capacity), StringComparer.OrdinalIgnoreCase);
}
