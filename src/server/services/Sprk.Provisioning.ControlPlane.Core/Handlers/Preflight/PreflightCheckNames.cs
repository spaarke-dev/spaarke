// -----------------------------------------------------------------------------
// PreflightCheckNames.cs
//
// Machine-stable check-name string constants for the three H0 preflight probes.
// The names match the script basenames under scripts/preflight/ (task 016)
// verbatim — they double as (a) IPreflightQuotaProbe.CheckName routing keys
// and (b) stable prefixes for the <see cref="HandlerResult.Failure.RejectionCode"/>
// values the H0 handler emits.
//
// DESIGN REF:
//   - scripts/preflight/README.md § Script Inventory: the script-backed checks + underlying APIs.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-01 + NFR-12.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// String constants for the three H0 preflight check names. Kept as a
/// dedicated class so a new preflight check is added in one place (add the
/// const here + add the probe registration in <see cref="Sprk.Provisioning.ControlPlane.Modules.HandlersModule"/>).
/// </summary>
public static class PreflightCheckNames
{
    /// <summary>Azure OpenAI regional TPM headroom (per-model 150+200+30+350 sum per NFR-12).</summary>
    public const string AzureOpenAiTpmHeadroom = "AzureOpenAiTpmHeadroom";

    // The Microsoft.Compute vCPU-family check (SubscriptionVCpuQuota / quota-subscription-vcpu) was REMOVED
    // 2026-10-15 (T186 L17): the stamp deploys no Microsoft.Compute resources, App Service plans draw on their own
    // per-SKU worker quota, and no supported API reads that quota. A real App Service shortfall surfaces at H2a.

    /// <summary>
    /// The run's SPE container type has an owner entry, L2 can sign in as that owning app (Worker UAMI
    /// federated credential) and the container type is registered in the tenant (FR-11 T6; task 248
    /// replaced the retired SpeCertBootstrap check).
    /// </summary>
    public const string SpeOwnerCredential = "SpeOwnerCredential";

    /// <summary>
    /// HANDLER-03 (pre-dispatch audit 2026-08-27, Wave 2 remediation) — F1
    /// verbatim: pinned Azure OpenAI model versions (ADR-020 catalog) are
    /// still GA / not-Deprecating in the target region + subscription.
    /// Failing HERE saves the 20-30 min H2a window a `ServiceModelDeprecated`
    /// deploy would otherwise waste.
    /// </summary>
    public const string OpenAiPinFreshness = "OpenAiPinFreshness";
}
