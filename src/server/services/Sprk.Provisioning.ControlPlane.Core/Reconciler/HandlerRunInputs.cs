// -----------------------------------------------------------------------------
// HandlerRunInputs.cs
//
// Task 245a (G25 — run-context contract). The declared inputs of every
// provisioning handler, and where each one comes from:
//
//   Intake  — an IntakeParameterCatalog key in run.Parameters.NonSecret
//             (written once, at POST /api/runs)
//   Output  — a typed InterStepState property, written by its [ProducedBy]
//             handler
//   Gap     — an input whose source is still being fixed by a named task.
//             None remain since task 245c; the (empty) set is pinned by
//             RunContextContractTests, so a new one can only be added
//             deliberately.
//
// NOT run inputs (task 245b): L2 configuration (validated options — e.g. the SPE
// owning-app credentials, the L2 principal H4 grants, the vendor-key vault) and
// artifact versions L2 computes from the artifacts it applies (ArtifactVersion).
//
// WHY: until task 245a about twenty required handler inputs had no producer at
// all — handlers read them from run parameters nothing wrote, and unit tests
// seeded them by hand (notes/run-context-dataflow-gap.md). RunContextContractTests
// now proves, for every handler: each required Output is produced by a handler
// that runs strictly before it (DagAdvancer.HandlerDependencies); each Intake
// key is an accepted intake key; and the handler's source reads nothing it has
// not declared here (a source scan of its Handlers/ folder).
//
// MAINTENANCE: adding a handler input = add it here, with its real source. If
// the contract test fails, fix the data flow — do not add a Gap to make it pass.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <summary>Where a handler input comes from.</summary>
public enum RunInputSource
{
    /// <summary>An accepted intake key (<see cref="IntakeParameterCatalog"/>) in <c>run.Parameters.NonSecret</c>.</summary>
    Intake = 1,

    /// <summary>A typed <see cref="InterStepState"/> property written by its <see cref="ProducedByAttribute"/> handler.</summary>
    Output = 2,

    /// <summary>An input whose correct source is still owed by a named follow-up task.</summary>
    Gap = 3,
}

/// <summary>One declared handler input.</summary>
/// <param name="Source">Where it comes from.</param>
/// <param name="Name">The intake key, or the <see cref="InterStepState"/> property name, or the gap's key.</param>
/// <param name="Required">True when the handler cannot do its job without it.</param>
/// <param name="OwningTask">For <see cref="RunInputSource.Gap"/>: the task that fixes the source.</param>
public sealed record RunInput(RunInputSource Source, string Name, bool Required, string? OwningTask = null)
{
    /// <summary>An intake key.</summary>
    public static RunInput Intake(string key, bool required = true) => new(RunInputSource.Intake, key, required);

    /// <summary>A typed InterStepState output of another handler.</summary>
    public static RunInput Output(string interStepStateProperty, bool required = true)
        => new(RunInputSource.Output, interStepStateProperty, required);

    /// <summary>A known gap (still read from intake today), owned by <paramref name="owningTask"/>.</summary>
    public static RunInput Gap(string key, string owningTask) => new(RunInputSource.Gap, key, true, owningTask);
}

/// <summary>The declared inputs of every provisioning handler (task 245a run-context contract).</summary>
public static class HandlerRunInputs
{
    // Intake keys used by many handlers.
    private static readonly RunInput Tenant = RunInput.Intake(IntakeParameterCatalog.TenantId);
    private static readonly RunInput Subscription = RunInput.Intake(IntakeParameterCatalog.SubscriptionId);

    /// <summary>Declared inputs by handler id (<see cref="HandlerIds"/>).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<RunInput>> ByHandler { get; } =
        new Dictionary<string, IReadOnlyList<RunInput>>(StringComparer.Ordinal)
        {
            [HandlerIds.H0] =
            [
                Tenant, Subscription,
                RunInput.Intake("region"),
                RunInput.Intake(CostEnvelopeIntake.TierParameterKey),                    // T229: required (CostEnvelopeIntake)
                RunInput.Intake(CostEnvelopeIntake.EstimatedMonthlyUsdParameterKey),
                RunInput.Intake("openaiPinFreshnessMinDays", required: false),
                RunInput.Intake("openAiLocation", required: false),   // T247: H0's OpenAI probes (else westus3)
                RunInput.Intake("provisionedOn", required: false),
                RunInput.Intake("currentBffVersion", required: false),
                RunInput.Intake("currentSolutionVersion", required: false),
                RunInput.Intake("targetBffVersion", required: false),
                RunInput.Intake("targetSolutionVersion", required: false),
                // Selects the SPE owning app the SpeOwnerCredential probe signs in as (task 245b / 248).
                RunInput.Intake(IntakeParameterCatalog.ContainerTypeId),
            ],
            [HandlerIds.H05] = [],
            [HandlerIds.H1] = [Tenant, Subscription],
            [HandlerIds.H2a] =
            [
                Tenant, Subscription,
                RunInput.Intake(IntakeParameterCatalog.EnvironmentName, required: false),
                RunInput.Intake("location", required: false),
                RunInput.Intake("openAiLocation", required: false),
                RunInput.Intake("provisionedOn", required: false),
                RunInput.Intake("signalrEnabled", required: false),
            ],
            [HandlerIds.H2b] =
            [
                Tenant,
                RunInput.Intake(IntakeParameterCatalog.EnvironmentName, required: false),
                RunInput.Intake("requestedIndexes", required: false),
                RunInput.Output(nameof(InterStepState.AiSearchEndpoint)),
            ],
            [HandlerIds.H3] =
            [
                Tenant,
                RunInput.Intake(IntakeParameterCatalog.DataverseEnvUrl),   // T240a: the code pages' SPA redirect
                RunInput.Intake(IntakeParameterCatalog.EnvironmentName, required: false),   // T240a: the URL rule's domain
                RunInput.Output(nameof(InterStepState.KeyVaultName)),
                RunInput.Output(nameof(InterStepState.MiObjectId)),
                RunInput.Output(nameof(InterStepState.S2SAppRegId), required: false),   // refused if present
            ],
            [HandlerIds.H4] =
            [
                Tenant, Subscription,
                RunInput.Intake(IntakeParameterCatalog.CommunicationDefaultMailbox),   // T245c: KV Communication-DefaultMailbox
                RunInput.Intake("provisionedOn", required: false),
                RunInput.Intake("rotate", required: false),
                RunInput.Intake("ficOmitSecretNames", required: false),
                RunInput.Output(nameof(InterStepState.KeyVaultName)),
                RunInput.Output(nameof(InterStepState.ResourceGroupName)),
                RunInput.Output(nameof(InterStepState.AppServiceName)),
                RunInput.Output(nameof(InterStepState.MiResourceId)),
                RunInput.Output(nameof(InterStepState.AppServiceStagingSlotName), required: false),
                // Not inputs: the cleartext-leak guard reflects over every InterStepState string property
                // (whatever is present), so it depends on no particular producer.
            ],
            [HandlerIds.H4b] =
            [
                Tenant, Subscription,
                RunInput.Intake(IntakeParameterCatalog.EnvironmentName, required: false),
                RunInput.Intake(IntakeParameterCatalog.ContainerTypeId),
                RunInput.Intake(IntakeParameterCatalog.OpenAiMonthlyLimitUsd, required: false),   // T254: optional spend limit (G37)
                RunInput.Output(nameof(InterStepState.KeyVaultName)),
                RunInput.Output(nameof(InterStepState.ResourceGroupName)),
                RunInput.Output(nameof(InterStepState.AppServiceName)),
                // PerEnvSourceCatalog sources:
                RunInput.Output(nameof(InterStepState.KeyVaultUri)),
                RunInput.Output(nameof(InterStepState.CosmosEndpoint)),
                RunInput.Output(nameof(InterStepState.MiClientId)),
                RunInput.Output(nameof(InterStepState.ServiceBusFullyQualifiedNamespace)),
                RunInput.Output(nameof(InterStepState.RedisEndpoint)),     // T242: Redis__Endpoint
                RunInput.Output(nameof(InterStepState.ContentSafetyEndpoint)),   // T246: AiSafety__ContentSafety__Endpoint
                RunInput.Output(nameof(InterStepState.BffAppRegId)),
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),   // T245b: Dataverse__ServiceUrl / __EnvironmentUrl
                RunInput.Output(nameof(InterStepState.SpeContainerId)),    // T227c: EmailProcessing__DefaultContainerId / Communication__ArchiveContainerId
                // (+ intake tenantId / containerTypeId above; customer_id reads run.CustomerId — run
                //  identity, which RunInputSource has no kind for and needs no declaration.)
            ],
            [HandlerIds.H5] =
            [
                Tenant,
                // T228: the environment the operator created — H5 adopts it, never creates one.
                RunInput.Intake(IntakeParameterCatalog.DataverseEnvUrl),
                RunInput.Intake(IntakeParameterCatalog.EnvironmentName, required: false),
            ],
            [HandlerIds.H6] =
            [
                Tenant,
                // T218b: managed (default, stored at CreateRun) | unmanaged — explicit instruction only.
                RunInput.Intake(IntakeParameterCatalog.SolutionPackageType, required: false),
                RunInput.Output(nameof(InterStepState.BffAppRegId)),
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),
            ],
            [HandlerIds.H7] =
            [
                Tenant,
                RunInput.Intake("msalClientId", required: false),
                RunInput.Intake("shareLinkBaseUrl", required: false),
                RunInput.Output(nameof(InterStepState.BffAppRegId)),
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),
                RunInput.Output(nameof(InterStepState.OpenAiEndpoint), required: false),
                RunInput.Output(nameof(InterStepState.SpeContainerId)),
                RunInput.Output(nameof(InterStepState.BffApiUrl)),   // T245b: the stamp's own BFF (H9) — no platform default
            ],
            [HandlerIds.H8] =
            [
                Tenant,
                // Also selects the owning-app credential (SpeContainerOptions.ContainerTypeOwners — T245b).
                RunInput.Intake(IntakeParameterCatalog.ContainerTypeId),
                RunInput.Intake("speContainerDisplayName", required: false),
                // T227b: H8 grants these two on the container-type registration before creating the container.
                RunInput.Output(nameof(InterStepState.MiClientId)),
                RunInput.Output(nameof(InterStepState.BffAppRegId)),
                // unified-access-control-r2 task 165, owner round 35 item 1: the container is bound to this environment's
                // ROOT business unit (read before anything is created) — H5 output; H8 depends on H5. T227e: H8 also reads
                // that environment's recorded container (sprk_SharePointEmbeddedContainerId) to reuse it on a later run.
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),
            ],
            [HandlerIds.H9] =
            [
                Tenant, Subscription,
                RunInput.Intake("buildId", required: false),
                RunInput.Intake("healthCheckPath", required: false),
                RunInput.Output(nameof(InterStepState.ResourceGroupName)),
                RunInput.Output(nameof(InterStepState.AppServiceName)),
                RunInput.Output(nameof(InterStepState.AppServiceStagingSlotName), required: false),
            ],
            [HandlerIds.H10] =
            [
                Tenant,
                RunInput.Output(nameof(InterStepState.BffAppRegId)),
                RunInput.Output(nameof(InterStepState.MiClientId)),
                RunInput.Output(nameof(InterStepState.MiObjectId)),
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),
            ],
            [HandlerIds.H11] =
            [
                Tenant,
                // T245c: required intake, validated at POST /api/runs by UserProvisioningIntake (H11's own rules).
                RunInput.Intake(IntakeParameterCatalog.IdentityPreset),
                RunInput.Intake(IntakeParameterCatalog.UsersJson),
                // T232: required for B2BGuest only (every Model 1 run) — UserProvisioningIntake enforces that at intake.
                RunInput.Intake(IntakeParameterCatalog.EnvironmentSecurityGroupId, required: false),
                // T232: each guest becomes a Dataverse user of the environment H5 adopted (H5 → H10 → H11).
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),
            ],
            [HandlerIds.H12a] = [Tenant, RunInput.Output(nameof(InterStepState.DataverseEnvUrl))],
            [HandlerIds.H12b] = [Tenant, RunInput.Output(nameof(InterStepState.DataverseEnvUrl))],
            [HandlerIds.H12c] =
            [
                Tenant,
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),
                RunInput.Output(nameof(InterStepState.OpenAiEndpoint)),
            ],
            [HandlerIds.H13] =
            [
                Tenant, Subscription,
                // Registry column + T6's owning-app credential selector (T245b).
                RunInput.Intake(IntakeParameterCatalog.ContainerTypeId, required: false),
                RunInput.Output(nameof(InterStepState.BffBuildId)),   // T245b: idempotency key + sprk_bffversion
                RunInput.Output(nameof(InterStepState.BffApiUrl)),    // T245b: health / E2E target
                RunInput.Output(nameof(InterStepState.ImportedSolutions), required: false),   // T245b: sprk_solutionversion
                RunInput.Output(nameof(InterStepState.ResourceGroupName)),
                RunInput.Output(nameof(InterStepState.AppServiceName)),
                RunInput.Output(nameof(InterStepState.KeyVaultName)),
                RunInput.Output(nameof(InterStepState.AiSearchEndpoint)),
                RunInput.Output(nameof(InterStepState.CosmosEndpoint)),
                RunInput.Output(nameof(InterStepState.BffAppRegId)),
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),
                RunInput.Output(nameof(InterStepState.MiClientId)),
                RunInput.Output(nameof(InterStepState.MiObjectId)),
                // T248: T6 looks for H8's container in the owning app's app-only listing (absent → T6 InfraFault).
                // T227c: I4 compares the BFF's container settings with it (absent → I4 InfraFault).
                RunInput.Output(nameof(InterStepState.SpeContainerId), required: false),
                RunInput.Intake(IntakeParameterCatalog.ExchangePolicyScopeGroupId, required: false),   // T251: T4 scope
            ],
            [HandlerIds.H14] =
            [
                Tenant, Subscription,
                RunInput.Output(nameof(InterStepState.BffApiUrl)),   // T245b: webhook receiver base (H9)
                // T245c: required intake (scope group created by the customer's Exchange admin, PRQ-C-08); the two
                // Graph resources are each optional, but POST /api/runs requires at least one (H14b's rule).
                RunInput.Intake(IntakeParameterCatalog.ExchangePolicyScopeGroupId),
                RunInput.Intake(IntakeParameterCatalog.CommunicationGraphResource, required: false),
                RunInput.Intake(IntakeParameterCatalog.EmailGraphResource, required: false),
                RunInput.Output(nameof(InterStepState.KeyVaultName)),
                RunInput.Output(nameof(InterStepState.DataverseEnvUrl)),
                RunInput.Output(nameof(InterStepState.MiClientId)),
                RunInput.Output(nameof(InterStepState.MiObjectId)),   // T251: H14a registers the identity in Exchange
            ],
        };
}
