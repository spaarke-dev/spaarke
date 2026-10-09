// -----------------------------------------------------------------------------
// H13AcceptanceOptions.cs
//
// Bound options for the H13 E2E acceptance-gate handler + its 5 collaborator
// seams (E2E validation runner + trap verifier + invariant verifier + cost
// envelope + registry status updater). Loaded from the "E2EAcceptance"
// configuration section.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-18 (H13
//     acceptance) + SC #5 (extended validate script) + SC #6 (traps re-verified)
//     + §15 #14 (cost envelope).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H13 row +
//     §4B (T1–T7 trap catalog) + §4C (Quarantined semantics) + §4D (I1–I5
//     tenant-isolation invariants; H13 verifies I2–I5 at runtime).
//
// Task 230a: the retired shell-out settings DELETED (PwshExecutable,
// AzCliExecutable, ValidateDeployedEnvironmentScriptPath / ValidateScriptTimeout,
// NamingConformanceScriptPath / NamingConformanceTimeout, CostQueryTimeout) —
// nothing read them once the collaborators became pure-C# / SDK ports — along
// with ProvisioningScriptsDirectory (the runtime I1 probe's scan root).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Bound options for <see cref="H13E2EAcceptanceGateHandler"/> collaborators.
/// Configuration key: <c>E2EAcceptance</c>.
/// </summary>
public sealed class H13AcceptanceOptions
{
    /// <summary>
    /// Maximum time to wait for a single trap verifier probe (T1–T7). Defaults
    /// to 3 minutes — each verifier is a bounded Graph/ARM/Dataverse REST call.
    /// </summary>
    public TimeSpan TrapVerifierTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Maximum time to wait for a single invariant verifier probe (I2–I5).
    /// Defaults to 2 minutes.
    /// </summary>
    public TimeSpan InvariantVerifierTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum time to wait for the customer BFF's keyless proof (<see cref="E2EValidationRunner"/>, task 230b). The
    /// BFF runs its ten service probes concurrently, each limited to 30 seconds; 90 seconds leaves room for a cold
    /// managed-identity token and a first connection. (Replaces SampleWorkloadCheckTimeout — the four user-workflow
    /// sample checks it bounded were removed by task 230b.)
    /// </summary>
    public TimeSpan KeylessProofTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Expected monthly cost of an EMPTY dedicated customer stamp in whole USD — one envelope for both tenancy
    /// models (spec.md §15 #14; task 229). Model 1 and Model 2 deploy the same <c>customer.bicep</c> (D19), each into
    /// the customer's own subscription (T228), so the subscription-scope Cost Management query H13 runs measures
    /// exactly one stamp.
    /// Re-derived 2026-10-06 from the Azure Retail Prices API (westus2, 730 h/month, list prices): App Service plan S1
    /// Linux $58.40 + AI Search Standard S1 (1 replica × 1 partition) $245.28 + Azure Managed Redis Balanced_B0 with
    /// high availability (2 × $0.016/h) $23.36 + Service Bus Standard base $10.00 = $337.04 fixed. Cosmos DB
    /// (serverless), Storage, Key Vault, Log Analytics / Application Insights, Azure OpenAI, Document Intelligence and
    /// Content Safety are consumption-billed and near zero while the stamp is empty; $400 leaves room for them.
    /// Re-derive when customer.bicep's SKUs change. Note the drift check is two-sided (|drift| &gt; threshold): an empty
    /// stamp's fixed cost is about 16 % below the envelope, and Cost Management lags by up to a day, so a stamp checked
    /// on its first day can read far below — an advisory, not a fault (CostDriftFailsRun defaults to false). (Replaces Model1MarginalEnvelopeUsd — a "marginal share of a
    /// shared platform" that no longer exists — and Model2EmptyEnvelopeUsd.)
    /// </summary>
    public decimal DedicatedStampEnvelopeUsd { get; set; } = 400m;

    /// <summary>
    /// Cost drift fraction above which H13 emits an advisory warning per
    /// spec.md §15 #14. Defaults to 0.20 (20%). Per POML mandatory pre-work
    /// note: cost drift is currently AMBISUOUS — implemented as advisory-warn
    /// by default, NOT a fail-run classification, per the deviation note.
    /// </summary>
    public decimal CostDriftAdvisoryThreshold { get; set; } = 0.20m;

    /// <summary>
    /// When true, cost drift above <see cref="CostDriftAdvisoryThreshold"/>
    /// FAILS H13 QuarantineRequired instead of emitting an advisory warning.
    /// Defaults to <c>false</c> per deviation note (owner-configurable per
    /// spec.md Unresolved Questions).
    /// </summary>
    public bool CostDriftFailsRun { get; set; } = false;

    /// <summary>
    /// Deployment slot name (production URL vs staging) for the sample E2E
    /// checks — defaults to <c>production</c> so the extended validate script
    /// targets the live post-H9-swap slot.
    /// </summary>
    public string TargetSlotName { get; set; } = "production";

    /// <summary>
    /// Startup validation (E2EAcceptanceModule — ValidateOnStart). Throws
    /// <see cref="InvalidOperationException"/> naming the invalid setting.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TargetSlotName))
        {
            throw new InvalidOperationException("E2EAcceptance:TargetSlotName must not be blank (default: production).");
        }
        if (DedicatedStampEnvelopeUsd <= 0m)
        {
            // Task 229: a non-positive envelope makes the drift fraction 0, so H13's cost check would always pass.
            throw new InvalidOperationException(
                $"E2EAcceptance:DedicatedStampEnvelopeUsd must be a positive number of USD (got {DedicatedStampEnvelopeUsd}; default 400).");
        }
    }

    /// <summary>
    /// When true, H13 short-circuits + returns Success WITHOUT invoking any
    /// collaborator seam if the run's registry Setup Status is already
    /// <c>Ready</c> AND the level-3 idempotency key matches. Defaults to
    /// <c>true</c> per project constraint acceptance criterion 8.
    /// </summary>
    public bool HonorRegistryStatusReadyShortCircuit { get; set; } = true;
}
