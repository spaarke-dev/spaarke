// -----------------------------------------------------------------------------
// IntegrationWiringRejectionCodes.cs
//
// Machine-stable rejection codes + gate identifiers emitted by
// H14IntegrationWiringHandler + its 3 sub-handlers (task 073, wave C4 Batch
// 3F). T4 silent-fail trap owner (Exchange role-assignment drift — RBAC for Applications since task 251).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-19 (H14
//     acceptance) + FR-33 (T4 silent-fail trap).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H14 row
//     + §4B T4 trap catalog + §4C rollback taxonomy.
//
// STABILITY: codes are string constants used by external tools. Do NOT rename;
// add new codes as needed and mark old ones [Obsolete] on removal.
//
// PATTERN PARITY: mirrors Handlers/DataverseAppUserGraphParity/H10Rejections.cs
// and Handlers/KvSecretsPopulation/KvSecretsPopulationRejectionCodes.cs — one
// const per failure branch + lowercase kebab-case for greppability.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Machine-stable rejection codes for <see cref="H14IntegrationWiringHandler"/>
/// (parent) failures. Sub-handler-specific codes live in
/// <see cref="H14aRejections"/> / <see cref="H14bRejections"/> /
/// <see cref="H14cRejections"/>.
/// </summary>
public static class H14Rejections
{
    /// <summary>Envelope resolved no ProvisioningRun document in the customer partition.</summary>
    public const string RunNotFound = "h14-run-not-found";

    /// <summary>Run parameter <c>tenantId</c> missing (§4D I1 no-hardcoded-tenant).</summary>
    public const string MissingTenantId = "h14-missing-tenant-id";

    /// <summary><c>InterStepState.keyVaultName</c> (the CUSTOMER vault, an H2a output) missing — H14b/H14c both read the HMAC signing key from this vault.</summary>
    public const string MissingKeyVaultName = "h14-missing-kv-name";

    /// <summary>Run parameter <c>subscriptionId</c> missing — H14b/H14c need it to scope the KV signing-key read.</summary>
    public const string MissingSubscriptionId = "h14-missing-subscription-id";

    /// <summary>
    /// Retired by task 251 (H14a grants only the stamp identity) — kept so a run record carrying it still
    /// reads. InterStepState.bffAppRegId missing.
    /// </summary>
    public const string MissingBffAppRegId = "h14-missing-bff-appreg-id";

    /// <summary>InterStepState.miClientId missing — H2a (uami.bicep) has not completed yet (upstream dependency for H14a).</summary>
    public const string MissingUamiClientId = "h14-missing-uami-client-id";

    /// <summary>InterStepState.miObjectId missing — H2a (uami.bicep) has not completed yet (H14a registers the identity in Exchange by it).</summary>
    public const string MissingUamiObjectId = "h14-missing-uami-object-id";

    /// <summary>InterStepState.dataverseEnvUrl missing — H5/H6 has not completed yet (upstream dependency for H14c).</summary>
    public const string MissingDataverseEnvUrl = "h14-missing-dataverse-env-url";

    /// <summary>InterStepState.BffApiUrl (H9 output) missing — H14b/H14c both need it to construct the receiver URL (task 245b).</summary>
    public const string MissingWebhookNotificationBaseUrl = "h14-missing-webhook-notification-base-url";

    /// <summary>
    /// Aggregate failure — one or more of the 3 DAG-parallel sub-steps
    /// failed. The Diagnostic enumerates every failing sub-step's own code +
    /// message so the operator sees the full picture in one place rather
    /// than fail-fast on the first observed failure.
    /// </summary>
    public const string SubStepFailed = "h14-substep-failed";

    /// <summary>Race with a concurrent Cosmos writer — reconciler will observe winning state.</summary>
    public const string ConcurrentWriteConflict = "h14-concurrent-write-conflict";

    /// <summary>ProvisioningRun row was deleted while H14 was in flight.</summary>
    public const string RunDeletedDuringWiring = "h14-run-deleted-during-wiring";
}

/// <summary>Machine-stable rejection codes for H14a (Exchange mailbox access — RBAC for Applications).</summary>
public static class H14aRejections
{
    /// <summary>Run parameter <c>exchangePolicyScopeGroupId</c> missing — the customer's mail-enabled security group H14a scopes the Exchange roles to.</summary>
    public const string MissingPolicyScopeGroupId = "h14a-missing-policy-scope-group-id";

    /// <summary>
    /// T4 SILENT-FAIL TRAP (spec.md FR-33): the stamp identity's Exchange role assignments differ
    /// from the expected group-scoped set (a named assignment with another role / app / scope, or
    /// one of these roles held under another name). Quarantine-required — H14a MUST NOT silently
    /// overwrite; the diagnostic lists every conflict.
    /// </summary>
    public const string TrapT4Drift = "h14a-trap-T4-drift";

    /// <summary>
    /// No conclusive Applied/Drift outcome (Exchange sign-in, sidecar, throttle or transport
    /// failure). Resumable — the apply is get-before-set, so a re-run creates only what is missing.
    /// </summary>
    public const string ApplyFailed = "h14a-apply-failed";
}

/// <summary>Machine-stable rejection codes for H14b (Graph webhook subscriptions).</summary>
public static class H14bRejections
{
    /// <summary>Neither <c>communicationGraphResource</c> nor <c>emailGraphResource</c> run parameters were supplied — H14b has nothing to subscribe to.</summary>
    public const string NoWebhookTargetsConfigured = "h14b-no-webhook-targets-configured";

    /// <summary>The <c>Communication-Webhook-SigningKey</c> KV secret (H4-provisioned per task 047) was not found on the target vault.</summary>
    public const string MissingSigningKey = "h14b-missing-signing-key";

    /// <summary>The KV secret read for the HMAC signing key failed for an infrastructure reason (not NotFound).</summary>
    public const string SigningKeyReadFailed = "h14b-signing-key-read-failed";

    /// <summary>
    /// One or more Graph subscription create/renew calls failed.
    /// RetryableWithCleanup — the create-or-renew seam is itself idempotent
    /// (list-then-create-or-patch), so a full re-run safely completes the
    /// partial state.
    /// </summary>
    public const string SubscriptionCreateFailed = "h14b-subscription-create-failed";
}

/// <summary>Machine-stable rejection codes for H14c (Dataverse service-endpoint webhooks).</summary>
public static class H14cRejections
{
    /// <summary>The <c>Communication-Webhook-SigningKey</c> KV secret was not found on the target vault.</summary>
    public const string MissingSigningKey = "h14c-missing-signing-key";

    /// <summary>The KV secret read for the HMAC signing key failed for an infrastructure reason (not NotFound).</summary>
    public const string SigningKeyReadFailed = "h14c-signing-key-read-failed";

    /// <summary>
    /// The Dataverse serviceendpoint upsert (list-then-create-or-patch)
    /// failed. RetryableWithCleanup — the registrar seam is itself idempotent,
    /// so a full re-run safely completes the partial state.
    /// </summary>
    public const string RegistrationFailed = "h14c-registration-failed";
}

/// <summary>
/// Well-known gate identifiers written to <c>ProvisioningRun.GateStates</c> by
/// H14 + its sub-handlers. Kept as string constants so grep across the
/// codebase finds every read/write of the same gate name (design.md §6.2).
/// </summary>
public static class H14Gates
{
    /// <summary>T4 gate — flips to Verified once H14a has read back every group-scoped mailbox role with no drift. (Value kept for run-record compatibility.)</summary>
    public const string ExchangePolicyApplied = "h14a-exchange-policy-applied";

    /// <summary>Flips to Verified once all configured Graph webhook subscriptions are created/renewed.</summary>
    public const string GraphWebhooksWired = "h14b-graph-webhooks-wired";

    /// <summary>Flips to Verified once the Dataverse service-endpoint webhook is registered.</summary>
    public const string DataverseWebhookWired = "h14c-dataverse-webhook-wired";
}
