// -----------------------------------------------------------------------------
// PreflightCheckNames.cs
//
// Machine-stable check-name string constants for the four H0 preflight probes.
// The names match the script basenames under scripts/preflight/ (task 016)
// verbatim — they double as (a) IPreflightQuotaProbe.CheckName routing keys
// and (b) stable prefixes for the <see cref="HandlerResult.Failure.RejectionCode"/>
// values the H0 handler emits.
//
// DESIGN REF:
//   - scripts/preflight/README.md § Script Inventory: 4 checks + underlying APIs.
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-01 + NFR-12.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// String constants for the four H0 preflight check names. Kept as a
/// dedicated class so a new preflight check is added in one place (add the
/// const here + add the probe registration in <see cref="Sprk.Provisioning.ControlPlane.Modules.HandlersModule"/>).
/// </summary>
public static class PreflightCheckNames
{
    /// <summary>Azure OpenAI regional TPM headroom (per-model 150+200+30+350 sum per NFR-12).</summary>
    public const string AzureOpenAiTpmHeadroom = "AzureOpenAiTpmHeadroom";

    /// <summary>Dataverse environment-creation rate slots in the current hourly bucket (~4/hr typical per tenant).</summary>
    public const string DataverseEnvCreationRate = "DataverseEnvCreationRate";

    /// <summary>Subscription vCPU quota headroom per SKU family per region.</summary>
    public const string SubscriptionVCpuQuota = "SubscriptionVCpuQuota";

    /// <summary>SPE cert-bootstrap secret exists in the platform Key Vault (FR-11 T6).</summary>
    public const string SpeCertBootstrap = "SpeCertBootstrap";
}
