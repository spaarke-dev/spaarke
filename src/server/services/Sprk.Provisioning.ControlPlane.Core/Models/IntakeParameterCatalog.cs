// -----------------------------------------------------------------------------
// IntakeParameterCatalog.cs
//
// Task 245a (G25 — run-context contract). The CLOSED set of keys a provisioning
// run may carry in run.Parameters.NonSecret. That dictionary is written once, at
// POST /api/runs, from the request's nonSecretParameters — nothing else writes
// it. So every key in it is an operator/intake value; a value one handler
// produces for another belongs in InterStepState, never here.
//
// CreateRun rejects any key not listed (a typo such as `tenant_id`, or a
// handler output smuggled in as a parameter, fails at the edge instead of
// surfacing as "missing input" deep in the DAG). RunContextContractTests checks
// that every intake key a handler declares is listed here.
//
// Entries marked "(T245c)" are interim: the value is still read from intake
// today, and the owning task makes it a required intake field
// (notes/run-context-dataflow-gap.md). Task 245b removed the keys whose real
// source is L2 itself — artifact versions (computed from the artifacts), the
// SPE owner credential (Worker configuration), and the BFF URL / build (H9
// outputs) — so POST /api/runs now rejects them.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Models;

/// <summary>One accepted intake key.</summary>
/// <param name="Name">The <c>nonSecretParameters</c> key, exactly as handlers read it. (Named <c>Name</c>, not
/// <c>Key</c>: the FR-27 ArchTest treats a string property called <c>Key</c> as secret-shaped.)</param>
/// <param name="Description">What it is and who reads it.</param>
public sealed record IntakeParameter(string Name, string Description);

/// <summary>
/// The closed set of keys accepted in a run's <c>nonSecretParameters</c>.
/// </summary>
public static class IntakeParameterCatalog
{
    /// <summary>Entra tenant id — required at intake (§4D I1); read by every handler.</summary>
    public const string TenantId = "tenantId";

    /// <summary>Azure subscription of the customer stamp — required at intake for Model 2 (ADR-027 D4).</summary>
    public const string SubscriptionId = "subscriptionId";

    /// <summary>SPE container-type id for the environment (spaarke-constants.yaml) — H4, H8, H13.</summary>
    public const string ContainerTypeId = "containerTypeId";

    /// <summary>The customer stamp's environment segment — <c>customer.bicep</c> <c>environmentName</c>.</summary>
    public const string EnvironmentName = "environmentName";

    /// <summary>
    /// Value stored when intake omits <see cref="EnvironmentName"/>. Customer stamps are
    /// production-only for now (owner D6 — customer staging/dev stamps are out of scope).
    /// </summary>
    public const string DefaultEnvironmentName = "prod";

    /// <summary>Values <see cref="EnvironmentName"/> may take — <c>customer.bicep</c>'s <c>@allowed</c> list.</summary>
    public static readonly IReadOnlySet<string> AllowedEnvironmentNames =
        new HashSet<string>(StringComparer.Ordinal) { "dev", "staging", "prod" };

    private static readonly IntakeParameter[] Entries =
    [
        // --- Sent by /provision-environment Step 4.0 -------------------------------
        new(TenantId, "Entra tenant id (§4D I1). Required at intake. Every handler."),
        new(SubscriptionId, "Customer Azure subscription. Required at intake for Model 2. H0 probes, H1, H2a, H4, H4b, H9, H13, H14."),
        new("region", "Primary Azure region (H0 quota probes) / Dataverse region (H5)."),
        new("tier", "Cost tier (H0 envelope) / Dataverse environment SKU (H5)."),
        new("estimatedMonthlyUsd", "H0 cost-envelope input."),
        new("costEnvelopePolicy", "H0 cost-envelope policy (abortOnOverrun | warnAndProceed)."),
        new("openAiLocation", "Azure OpenAI region passed to customer.bicep (H2a)."),
        new(ContainerTypeId, "SPE container-type id for the environment (spaarke-constants.yaml). H4 (SPE-ContainerTypeId secret), H8, H13; selects the owning-app credential (SpeContainerOptions.ContainerTypeOwners) for H0, H8 and T6."),
        new("confirmationAcknowledgment", "Operator confirmation phrase (audit; part of the H0 idempotency hash)."),
        new("intakeFileSha256", "Batch intake file hash (audit; part of the H0 idempotency hash)."),
        new("operatorUpn", "Operator identity (audit; part of the H0 idempotency hash)."),

        // --- Stamp shape (defaults applied at CreateRun or in the handler) ----------
        new(EnvironmentName, "Customer stamp environment segment (dev | staging | prod). Absent → 'prod' stored at CreateRun. H2a, H2b, H4b."),
        new("location", "Primary Azure region for customer.bicep (H2a; default westus2)."),
        new("signalrEnabled", "Deploy SignalR (H2a; default false)."),
        new("requireSecretFreeIdentity", "Secret-free BFF identity flag passed to customer.bicep (H2a)."),
        new("requestedIndexes", "Subset of AI Search indexes to create (H2b; default all)."),
        new("dataverseDisplayName", "Dataverse environment display name (H5)."),
        new("speContainerDisplayName", "SPE root container display name (H8)."),
        new("healthCheckPath", "BFF health probe path (H9; default /healthz)."),
        new("buildId", "BFF build to deploy (H9 optional — defaults to latest.json). H13 reads the deployed build from H9's output."),

        // --- Upgrade mode (an existing stamp) ---------------------------------------
        new("provisionedOn", "Set on an upgrade run: when the stamp was first provisioned (H0, H2a, H4)."),
        new("currentBffVersion", "Upgrade run: deployed BFF version (H0 compatibility check)."),
        new("currentSolutionVersion", "Upgrade run: deployed solution version (H0 compatibility check)."),
        new("targetBffVersion", "Upgrade run: target BFF version (H0 compatibility check)."),
        new("targetSolutionVersion", "Upgrade run: target solution version (H0 compatibility check)."),
        new("rotate", "H4: rotate generated secrets on this run."),
        new("ficOmitSecretNames", "H4: secret names to omit when the stamp is secret-free."),

        // --- H0 probe tuning ---------------------------------------------------------
        new("minSlotsRequired", "H0 Dataverse capacity probe threshold."),
        new("rateWindowHours", "H0 Dataverse environment-creation rate window."),
        new("rateLimit", "H0 Dataverse environment-creation rate limit."),
        new("openaiPinFreshnessMinDays", "H0 OpenAI model-pin freshness threshold."),

        // --- Dataverse environment-variable values (H7) -------------------------------
        new("msalClientId", "H7 env-var value: MSAL client id for code pages."),
        new("shareLinkBaseUrl", "H7 env-var value: share-link base URL."),

        // --- Interim: still read from intake; T245c makes them required intake -----------
        new("identityPreset", "H11: B2BGuest | NativeAccount. (T245c → required intake)"),
        new("usersJson", "H11: users to provision (JSON array). (T245c → required intake)"),
        new("exchangePolicyScopeGroupId", "H14: Exchange application-access-policy scope group. (T245c → required intake)"),
        new("communicationGraphResource", "H14: Graph subscription resource for Communication. (T245c)"),
        new("emailGraphResource", "H14: Graph subscription resource for Email. (T245c)"),
    ];

    /// <summary>Every accepted intake key, by key (ordinal — keys are case-sensitive).</summary>
    public static IReadOnlyDictionary<string, IntakeParameter> All { get; } =
        Entries.ToDictionary(e => e.Name, StringComparer.Ordinal);

    /// <summary>
    /// The run's stamp environment: the stored <see cref="EnvironmentName"/> value, or
    /// <see cref="DefaultEnvironmentName"/> when absent (a run created before CreateRun stored the
    /// default). The ONE resolution every handler uses — H2a, H2b and H4b used to disagree (two
    /// silently defaulted, one failed).
    /// </summary>
    public static string ResolveEnvironmentName(IDictionary<string, string> nonSecretParameters)
    {
        ArgumentNullException.ThrowIfNull(nonSecretParameters);
        return nonSecretParameters.TryGetValue(EnvironmentName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : DefaultEnvironmentName;
    }

    /// <summary>True when <paramref name="key"/> is an accepted intake key (ordinal).</summary>
    public static bool IsKnown(string key) => All.ContainsKey(key);

    /// <summary>The keys in <paramref name="keys"/> that are not accepted, in input order.</summary>
    public static IReadOnlyList<string> UnknownKeys(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        return keys.Where(k => !IsKnown(k)).ToList();
    }
}
