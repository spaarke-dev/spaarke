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
// Task 245b removed the keys whose real source is L2 itself — artifact versions
// (computed from the artifacts), the SPE owner credential (Worker
// configuration), and the BFF URL / build (H9 outputs) — so POST /api/runs now
// rejects them. Task 245c made the operator-owned H11 / H14 / Communication
// values required intake, validated at POST /api/runs with the rules the
// handlers apply (UserProvisioningIntake for H11; non-blank / at-least-one for
// H14) — so a payload a handler would refuse is refused before anything is
// created. No known gaps remain (RunContextContractTests).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.Preflight;

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

    /// <summary>
    /// The customer's own Azure subscription — required at intake for every model, a GUID (ADR-027: one subscription per
    /// customer; T228: the operator creates it, nothing defaults it).
    /// </summary>
    public const string SubscriptionId = "subscriptionId";

    /// <summary>SPE container-type id for the environment (spaarke-constants.yaml) — required GUID (T228 / G19); H0, H4b, H8, H13.</summary>
    public const string ContainerTypeId = "containerTypeId";

    /// <summary>
    /// T228: URL of the Dataverse environment the operator created for this customer — required, checked by
    /// <see cref="Sprk.Provisioning.ControlPlane.Core.Models.DataverseEnvironmentUrlRule"/> and stored in its canonical
    /// form. H5 adopts it; nothing creates an environment.
    /// </summary>
    public const string DataverseEnvUrl = "dataverseEnvUrl";

    /// <summary>
    /// OPTIONAL monthly Azure OpenAI spend limit of the stamp in USD (task 254, owner G37). Absent = no limit (the default).
    /// Rule: <see cref="Sprk.Provisioning.ControlPlane.Core.Models.OpenAiMonthlyLimitRule"/>. H4b writes
    /// <c>AiSpendLimit__MonthlyLimitUsd</c> from it (PerEnvSourceCatalog <c>openai_monthly_limit_usd</c>).
    /// </summary>
    public const string OpenAiMonthlyLimitUsd = "openAiMonthlyLimitUsd";

    /// <summary>The customer stamp's environment segment — <c>customer.bicep</c> <c>environmentName</c>.</summary>
    public const string EnvironmentName = "environmentName";

    /// <summary>
    /// Value stored when intake omits <see cref="EnvironmentName"/>. Customer stamps are
    /// production-only for now (owner D6 — customer staging/dev stamps are out of scope).
    /// </summary>
    public const string DefaultEnvironmentName = "prod";

    /// <summary>H11 identity preset — <c>B2BGuest</c> | <c>NativeAccount</c> (design.md D6). Required.</summary>
    public const string IdentityPreset = "identityPreset";

    /// <summary>H11 user list — a JSON array of <c>{firstName, lastName, email?, companyName?}</c>. Required, ≥ 1 entry.</summary>
    public const string UsersJson = "usersJson";

    /// <summary>
    /// H11 (task 232) — object id of the customer environment's security group (<c>sprk-{customerId}-users</c>),
    /// created by the operator and set on the Dataverse environment before the run (prereqs.yaml <c>PRQ-C-10</c>).
    /// Required for identityPreset <c>B2BGuest</c> (every Model 1 run); H11 adds each guest to it.
    /// </summary>
    public const string EnvironmentSecurityGroupId = "environmentSecurityGroupId";

    /// <summary>
    /// H14a — the mail-enabled security group that scopes the Exchange ApplicationAccessPolicy (which mailboxes the
    /// BFF app + UAMI may use). Created by the Exchange admin of the stamp's tenant (the customer's for Model 2,
    /// Spaarke's for Model 1) before the run (prereqs.yaml <c>PRQ-C-08</c>; owner decision D14). Required.
    /// </summary>
    public const string ExchangePolicyScopeGroupId = "exchangePolicyScopeGroupId";

    /// <summary>H14b — Graph subscription resource for the Communication module. At least one of this and <see cref="EmailGraphResource"/>.</summary>
    public const string CommunicationGraphResource = "communicationGraphResource";

    /// <summary>H14b — Graph subscription resource for the Email module. At least one of this and <see cref="CommunicationGraphResource"/>.</summary>
    public const string EmailGraphResource = "emailGraphResource";

    /// <summary>H4 — the Communication module's default mailbox address (KV <c>Communication-DefaultMailbox</c>). Required.</summary>
    public const string CommunicationDefaultMailbox = "communicationDefaultMailbox";

    /// <summary>
    /// Shape of <see cref="CommunicationDefaultMailbox"/>: <c>local@domain.tld</c>, no whitespace. A shape check, not
    /// deliverability — it stops a name or a blank reaching the BFF's <c>Communication:DefaultMailbox</c>.
    /// <c>intake.schema.json</c> carries the same pattern and <see cref="MaxMailboxAddressLength"/>
    /// (IntakeSchemaProfileParityTests).
    /// </summary>
    public const string MailboxAddressPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";

    /// <summary>Longest accepted mailbox address (RFC 5321 path limit) — also bounds the pattern's backtracking.</summary>
    public const int MaxMailboxAddressLength = 254;

    private static readonly System.Text.RegularExpressions.Regex MailboxAddress =
        new(MailboxAddressPattern, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// True when <paramref name="value"/> is at most <see cref="MaxMailboxAddressLength"/> characters and matches
    /// <see cref="MailboxAddressPattern"/> as a whole (.NET's <c>$</c> also matches before a final newline, so the match
    /// must cover the entire value).
    /// </summary>
    public static bool IsMailboxAddress(string? value)
        => value is not null
            && value.Length <= MaxMailboxAddressLength
            && MailboxAddress.Match(value) is { Success: true } m
            && m.Length == value.Length;

    /// <summary>Values <see cref="EnvironmentName"/> may take — <c>customer.bicep</c>'s <c>@allowed</c> list.</summary>
    public static readonly IReadOnlySet<string> AllowedEnvironmentNames =
        new HashSet<string>(StringComparer.Ordinal) { "dev", "staging", "prod" };

    private static readonly IntakeParameter[] Entries =
    [
        // --- Sent by /provision-environment Step 4.0 -------------------------------
        new(TenantId, "Entra tenant id (§4D I1). Required at intake. Every handler."),
        new(SubscriptionId, "The customer's own Azure subscription (GUID), created by the operator. Required at intake for every model (ADR-027, T228). H0 probes, H1, H2a, H4, H4b, H9, H13, H14."),
        new(DataverseEnvUrl, "URL of the Dataverse environment the operator created (https://spaarke-{customerId}[-{environmentName}].crm[N].dynamics.com/). Required (T228); H5 adopts it."),
        new("region", "Primary Azure region (H0 quota probes)."),
        new(CostEnvelopeIntake.TierParameterKey, "Cost tier whose monthly ceiling H0 compares the estimate with: smb | enterprise | dedicated (budget classes for one dedicated stamp; T229). Required; validated at POST /api/runs (CostEnvelopeIntake)."),
        new(CostEnvelopeIntake.EstimatedMonthlyUsdParameterKey, "Projected monthly Azure spend of the stamp in USD (invariant decimal ≥ 0). Required; validated at POST /api/runs (CostEnvelopeIntake); H0 refuses a run whose estimate exceeds its tier ceiling (T229)."),
        new(OpenAiMonthlyLimitUsd, "OPTIONAL monthly Azure OpenAI spend limit of the stamp in USD (task 254, owner G37). Absent = no limit (the default). Validated at POST /api/runs (OpenAiMonthlyLimitRule); H4b writes AiSpendLimit__MonthlyLimitUsd on both slots only when present. Change or remove later with scripts/Set-AiSpendLimit.ps1."),
        new("openAiLocation", "Azure OpenAI region passed to customer.bicep (H2a) and checked by H0's OpenAI quota + pin probes (default westus3)."),
        new(ContainerTypeId, "SPE container-type id for the environment (spaarke-constants.yaml). Required GUID (T228 / G19). H4b (SharePointEmbedded__ContainerTypeId setting), H8, H13; selects the owning-app credential (SpeContainerOptions.ContainerTypeOwners) for H0, H8 and T6."),
        new(IdentityPreset, "H11 identity preset: B2BGuest | NativeAccount (design.md D6); a Model1 run takes only B2BGuest (owner D2, T232). Required; validated at POST /api/runs (UserProvisioningIntake)."),
        new(UsersJson, "H11 users to provision: JSON array of {firstName, lastName, email, companyName} — names required for NativeAccount, email for B2BGuest; 1 to 500 entries. Required; validated at POST /api/runs (UserProvisioningIntake). Stored in the run document (owner decision D15)."),
        new(EnvironmentSecurityGroupId, "H11 (T232): object id of the customer environment's security group sprk-{customerId}-users, created by the operator and set on the environment before the run (prereqs.yaml PRQ-C-10). Required for B2BGuest (every Model 1 run); validated at POST /api/runs (UserProvisioningIntake). H11 adds each guest to it."),
        new(ExchangePolicyScopeGroupId, "H14a: mail-enabled security group scoping the Exchange ApplicationAccessPolicy — created by the Exchange admin of the stamp's tenant before the run (prereqs.yaml PRQ-C-08). Required."),
        new(CommunicationGraphResource, "H14b: Graph subscription resource for the Communication module. At least one of this and emailGraphResource."),
        new(EmailGraphResource, "H14b: Graph subscription resource for the Email module. At least one of this and communicationGraphResource."),
        new(CommunicationDefaultMailbox, "H4: Communication module default mailbox address (KV Communication-DefaultMailbox). Required."),
        new("confirmationAcknowledgment", "Operator confirmation phrase (audit; part of the H0 idempotency hash)."),
        new("intakeFileSha256", "Batch intake file hash (audit; part of the H0 idempotency hash)."),
        new("operatorUpn", "Operator identity (audit; part of the H0 idempotency hash)."),

        // --- Stamp shape (defaults applied at CreateRun or in the handler) ----------
        new(EnvironmentName, "Customer stamp environment segment (dev | staging | prod). Absent → 'prod' stored at CreateRun. H2a, H2b, H4b."),
        new("location", "Primary Azure region for customer.bicep (H2a; default westus2)."),
        new("signalrEnabled", "Deploy SignalR (H2a; default false)."),
        new("requestedIndexes", "Subset of AI Search indexes to create (H2b; default all)."),
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
        new("openaiPinFreshnessMinDays", "H0 OpenAI model-pin freshness threshold."),

        // --- Dataverse environment-variable values (H7) -------------------------------
        new("msalClientId", "H7 env-var value: MSAL client id for code pages."),
        new("shareLinkBaseUrl", "H7 env-var value: share-link base URL."),
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
