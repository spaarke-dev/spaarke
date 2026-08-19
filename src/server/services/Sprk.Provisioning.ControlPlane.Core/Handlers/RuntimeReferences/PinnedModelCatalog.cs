// -----------------------------------------------------------------------------
// PinnedModelCatalog.cs
//
// The 3 ADR-020-pinned Azure OpenAI model deployments H12c writes runtime
// references for, on EVERY customer environment regardless of tenancy model.
//
// DESIGN REF:
//   - ADR-020 (versioning) — model deployments MUST be pinned to specific
//     versions, never "latest": gpt-4o 2024-08-06, gpt-4o-mini 2024-07-18,
//     text-embedding-3-large 1. H2a's IBicepTemplateInspector already asserts
//     the deployed Bicep template pins these same 3 versions
//     (BicepDeployRejectionCodes.ModelVersionNotPinned) — this catalog is the
//     C#-side mirror consumed at H12c runtime-reference-write time, parity
//     with H6's CanonicalSolutionCatalog / H2b's ICanonicalIndexCatalog
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
/// A single pinned model entry: the model identifier as it appears in both
/// <c>sprk_aimodeldeployment.sprk_name</c> (the alternate-key-style lookup
/// field <c>scripts/Deploy-Playbook.ps1</c> already queries by) and
/// <c>sprk_modelid</c>, plus its ADR-020-pinned version + capability.
/// </summary>
/// <param name="ModelId">
/// Azure OpenAI model identifier, e.g. <c>gpt-4o</c>. Written to BOTH
/// <c>sprk_name</c> and <c>sprk_modelid</c> — <c>sprk_name</c> is the field
/// every existing seeder/playbook-deploy script looks up by
/// (<c>scripts/Deploy-Playbook.ps1</c> line 635); keeping the two fields
/// identical avoids introducing a second addressing scheme.
/// </param>
/// <param name="PinnedVersion">ADR-020-pinned model version string (e.g. <c>2024-08-06</c>, or <c>1</c> for the embedding model's initial GA version).</param>
/// <param name="Capability">Model capability — drives <c>sprk_capability</c>.</param>
public sealed record PinnedModel(string ModelId, string PinnedVersion, ModelCapability Capability);

/// <summary>
/// The ADR-020 canonical 3-model catalog. Every customer environment (Model 1
/// or Model 2) gets exactly these 3 <c>sprk_aimodeldeployment</c> rows —
/// H12c does not write ad-hoc or unpinned entries.
/// </summary>
public static class PinnedModelCatalog
{
    public static readonly IReadOnlyList<PinnedModel> Models = new[]
    {
        new PinnedModel("gpt-4o", "2024-08-06", ModelCapability.Chat),
        new PinnedModel("gpt-4o-mini", "2024-07-18", ModelCapability.Chat),
        new PinnedModel("text-embedding-3-large", "1", ModelCapability.Embedding),
    };
}
