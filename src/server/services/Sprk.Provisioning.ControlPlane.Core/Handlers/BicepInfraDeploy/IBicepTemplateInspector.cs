// -----------------------------------------------------------------------------
// IBicepTemplateInspector.cs
//
// Structural checks against the Bicep template SOURCE (not deployed state):
//
//   1. Redis presence check — spec.md MUST rule + Q-E FR-12 v3.2: no
//      per-customer Redis. If the active template references a Redis resource
//      (e.g. `module redis 'modules/redis.bicep' = {`), H2a MUST fail deploy
//      with distinct rejection code (POML acceptance §5).
//
//   2. Model deployment version pinning check — ADR-020: `openai.bicep`
//      model deployments MUST specify pinned versions (`2024-08-06`,
//      `2024-07-18`, `1` — see design.md §7.4). "latest" or an empty version
//      literal is a MUST-NOT (POML constraint).
//
// STRUCTURAL (not runtime) enforcement:
//   These checks catch template drift BEFORE the ARM deploy fires — cheaper
//   to fail here than post-deploy. They also cover cases where the
//   template's default parameter gets accidentally reintroduced (grep-scan
//   the on-disk template rather than trusting the deploy runner's
//   input-parameter set).
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls: <see cref="FileBicepTemplateInspector"/> reads the on-disk
//   Bicep source; unit tests substitute stubs that return canned
//   <see cref="BicepTemplateInspectionResult"/> instances.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>
/// Structural inspector for the active Bicep template — runs BEFORE the
/// deploy to catch known-forbidden constructs (Redis provisioning; unpinned
/// model versions).
/// </summary>
public interface IBicepTemplateInspector
{
    /// <summary>
    /// Inspects the template selected by
    /// <see cref="BicepDeployRequest.TenancyModel"/>. Domain outcomes never
    /// throw; a missing-file or parse fault throws so the handler classifies
    /// per §4C.
    /// </summary>
    Task<BicepTemplateInspectionResult> InspectAsync(
        BicepDeployRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of <see cref="IBicepTemplateInspector.InspectAsync"/>. Both flags
/// map to distinct rejection codes on the handler; the handler chooses the
/// FIRST failing constraint so the operator diagnostic is unambiguous
/// (Redis-presence takes precedence over model-pin because Redis is a
/// spec.md MUST rule, not an ADR-only constraint).
/// </summary>
/// <param name="ContainsRedisResource">
/// <c>true</c> when the template's module composition references a Redis
/// resource of any kind (per-customer Redis violates Q-E FR-12).
/// </param>
/// <param name="RedisReference">
/// Human-readable citation of where Redis was referenced (file:line or
/// module name) — empty when <see cref="ContainsRedisResource"/> is <c>false</c>.
/// </param>
/// <param name="HasUnpinnedModelDeployment">
/// <c>true</c> when the template contains a model deployment version of
/// <c>latest</c> or an unpinned literal (violates ADR-020).
/// </param>
/// <param name="UnpinnedModelReference">
/// Human-readable citation of the unpinned deployment (deployment name +
/// observed version literal) — empty when the flag is <c>false</c>.
/// </param>
/// <param name="HasInvalidKvRefIdentity">
/// HANDLER-10 (Wave 2 pre-dispatch remediation 2026-08-27) — F16 verbatim.
/// <c>true</c> when the template contains a literal
/// <c>keyVaultReferenceIdentity: 'SystemAssigned'</c> assignment. Spaarke's
/// convention (ADR-028 + spec.md FR-33 T1) is UAMI-scoped kvRefIdentity;
/// SystemAssigned combined with UAMI-only identity attached silently
/// breaks every <c>@Microsoft.KeyVault(...)</c> ref at runtime.
/// </param>
/// <param name="KvRefIdentityReference">
/// Human-readable citation of where the invalid kvRefIdentity was found
/// (file:line + observed literal). Empty when the flag is <c>false</c>.
/// </param>
public sealed record BicepTemplateInspectionResult(
    bool ContainsRedisResource,
    string RedisReference,
    bool HasUnpinnedModelDeployment,
    string UnpinnedModelReference,
    bool HasInvalidKvRefIdentity = false,
    string KvRefIdentityReference = "");
