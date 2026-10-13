// -----------------------------------------------------------------------------
// IntegrationWiringOptions.cs
//
// Bound options for the H14 post-deploy integration wiring handler + its H14a
// Exchange sub-handler collaborators (H14b/H14c and their options removed,
// ISS-019). Loaded from the "IntegrationWiring" configuration
// section by Program.cs — runtime-configurable so the linux-x64 App Service
// publish layout can be honored without recompiling.
//
// PATTERN PARITY:
//   Mirrors Handlers/EntraAppReg/EntraAppRegOptions.cs (pwsh script path +
//   timeout shape) and Handlers/KvSecretsPopulation/KvSecretsPopulationOptions.cs
//   (az CLI executable + operation timeout shape).
//
// TASK 160 (Wave G-6): added KvReadTimeout for the new SecretClientKvReader
// collaborator (replaces AzCliKvSecretReader's `az keyvault secret show`
// shell-out — see that file's retirement banner) + a scoped NFR-05
// Validate() bounds-checking ONLY this new field, wired via
// IntegrationWiringModule's AddOptions&lt;T&gt;().Bind().Validate().ValidateOnStart()
// (parity with task 153's RuntimeReferencesOptions / task 151's
// AppConfigSeedOptions precedent). (The H14b/H14c-only fields GraphRequestTimeout,
// GraphSubscriptionExpirationMinutes, DataverseRequestTimeout and ServiceEndpoint*
// were removed with those handlers, ISS-019.)
//
// TASK 161 (Wave G-6): added 5 sidecar-client fields for the new
// ExchangePolicySidecarClient collaborator (replaced the pwsh shell-out applier,
// deleted by task 251): SidecarBaseUrl, SidecarRequestTimeout,
// SidecarTransientRetryDelay, SidecarSharedSecret{VaultName,SubscriptionId,Name}.
// Validate() extended in the SAME scoped-to-this-task's-own-fields posture
// task 160 established: bounds-checks the URL + two timeouts + a
// delay-fits-under-timeout invariant, deliberately leaves the 3 shared-secret
// strings unvalidated at boot (empty is legit in CI/dev where H14a is never
// dispatched; the runtime failure surfaces at first ApplyAsync with an
// explicit fail-loud diagnostic — never silent-empty-header).
//
// TASK 162 (Wave G-6 Batch G-6C): W3 CLEANUP — removed the 3 fossilized
// script-shell fields (PwshExecutable / ExchangePolicyScriptPath /
// ExchangeScriptTimeout) that task 161 deliberately deferred. They were only
// ever consumed by the retired ExchangePolicyScriptApplier (task 161 kept it
// on disk unregistered per the uniform "keep on disk with retirement banner"
// convention); their default values are now inlined as `private static
// readonly` constants inside that retired file itself — parity with task
// 160's AzCliKvSecretReader own KvSecretReadTimeout inline. The live options
// class therefore no longer exposes any configuration surface tied to the
// retired shell-out path, which prevents configuration authors from
// accidentally binding values that will silently do nothing.
//
// TASK 251: ExchangePolicyDescriptionPrefix (an ApplicationAccessPolicy
// description) replaced by ExchangeAdminAppId + ExchangeAssignmentNamePrefix —
// H14a now grants group-scoped Exchange roles (RBAC for Applications) and the
// Worker signs in to Exchange for the sidecar; both validated at boot.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Bound options for <see cref="H14IntegrationWiringHandler"/> + its
/// sub-handler collaborators. Configuration key: <c>IntegrationWiring</c>.
/// </summary>
public sealed class IntegrationWiringOptions
{
    // ---------- H14a Exchange mailbox access (task 251, owner D24 + D26) ----------

    /// <summary>
    /// Client id of the <c>Spaarke Exchange Admin</c> app registration. The Worker signs in as it
    /// through the federated identity credential that trusts the Worker's managed identity (ADR-028
    /// A4 -- no certificate, no secret) and sends the resulting Exchange Online token to the sidecar
    /// with each request; the sidecar holds no credential. Empty: H14a and the H13 T4 probe report
    /// "not configured" on first use (the Worker still boots). Validated as a GUID when set.
    /// </summary>
    public string ExchangeAdminAppId { get; set; } = "";

    /// <summary>
    /// Prefix of the Exchange role-assignment names H14a creates:
    /// <c>{prefix}-{customerId}-{role}</c> (64 characters max; longer names end in a hash).
    /// The names are H14a's idempotency key in Exchange, so changing the prefix after customers
    /// exist makes H14a see their existing assignments as Drift.
    /// </summary>
    public string ExchangeAssignmentNamePrefix { get; set; } = "Spaarke";

    // ---------- KV reader (task 160, SecretClientKvReader) ----------

    /// <summary>
    /// Maximum wall-clock time for a single Azure Key Vault
    /// <c>SecretClient.GetSecretAsync</c> call issued by
    /// <see cref="SecretClientKvReader"/> (task 160, Wave G-6 — SDK port
    /// replacing <see cref="AzCliKvSecretReader"/>'s `az keyvault secret
    /// show` shell-out). Defaults to 30 seconds.
    /// </summary>
    public TimeSpan KvReadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // ---------- Sidecar client (task 161, ExchangePolicySidecarClient) ----------

    /// <summary>
    /// Base URI of the H14a Exchange sidecar
    /// (task 114's Listener.ps1). Sitecontainer-private on the App Service's
    /// localhost namespace; the port is NOT exposed on the App Service's
    /// public front end per DS-1b §3 topology. Defaults to
    /// <c>http://127.0.0.1:8091/</c> matching Listener.ps1's own
    /// <c>SIDECAR_LISTEN_PREFIX</c> default. MUST be an absolute URI — enforced
    /// by <see cref="Validate"/>.
    /// </summary>
    public string SidecarBaseUrl { get; set; } = "http://127.0.0.1:8091/";

    /// <summary>
    /// Maximum wall-clock time for a single sidecar
    /// <c>POST /apply-mailbox-access</c> or <c>/read-mailbox-access</c> call: Exchange Online
    /// connect + the RBAC-for-Applications reads/writes, and margin. Defaults to 6 minutes.
    /// <c>timeoutSeconds</c> on the wire is ADVISORY; the HTTP client's <c>Timeout</c> is the
    /// real bound. Validated in <c>[30 s, 30 min]</c>.
    /// </summary>
    public TimeSpan SidecarRequestTimeout { get; set; } = TimeSpan.FromMinutes(6);

    /// <summary>
    /// Backoff delay between the initial sidecar call and the one retry
    /// permitted by <see cref="ExchangePolicySidecarClient"/> (DS-1b §3:
    /// "One retry with backoff inside the client for transient EXO
    /// throttling; everything else defers to the run-level §4C retry
    /// machinery"). Deliberately short — the whole point is that everything
    /// non-transient falls through to the reconciler. Defaults to 5 seconds.
    /// Validated in <c>[100 ms, 1 min]</c>.
    /// </summary>
    public TimeSpan SidecarTransientRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Platform Key Vault name (bare, e.g. <c>sprk-controlplane-dev-kv</c>)
    /// holding the per-boot sidecar shared secret. Listener.ps1 reads the
    /// same value from its own <c>SIDECAR_SHARED_SECRET</c> env var
    /// (App-Service-resolved KeyVault reference to the same secret) so both
    /// sides agree without either trusting the other's env exposure. Empty by
    /// default — a missing/whitespace value is NOT a boot-time failure (Worker
    /// still boots), but any H14a dispatch will return a
    /// <see cref="ExchangePolicyApplyOutcome.Failure"/> with an explicit
    /// "sidecar shared secret config missing" diagnostic (fail-loud at
    /// first-call, never a silent empty-header 401).
    /// </summary>
    public string SidecarSharedSecretVaultName { get; set; } = "";

    /// <summary>
    /// Azure subscription id scoping the platform Key Vault read for the
    /// sidecar shared secret. Same empty-default fail-loud posture as
    /// <see cref="SidecarSharedSecretVaultName"/>.
    /// </summary>
    public string SidecarSharedSecretSubscriptionId { get; set; } = "";

    /// <summary>
    /// Platform KV secret name holding the per-boot sidecar shared secret.
    /// Defaults to <c>Sidecar-Shared-Secret</c>. MUST match the App-Service
    /// KeyVault reference that populates the sidecar's
    /// <c>SIDECAR_SHARED_SECRET</c> env var — see Listener.ps1's
    /// <c>Test-SecretEqual</c> constant-time compare against
    /// <c>$env:SIDECAR_SHARED_SECRET</c>.
    /// </summary>
    public string SidecarSharedSecretName { get; set; } = "Sidecar-Shared-Secret";

    /// <summary>
    /// Startup validation applied by <see cref="IntegrationWiringModule.AddH14IntegrationWiringHandler"/>'s
    /// <c>AddOptions&lt;IntegrationWiringOptions&gt;().ValidateOnStart()</c>
    /// registration (task 160, NFR-05 parity with
    /// <c>RuntimeReferencesOptions.Validate</c> / <c>AppConfigSeedOptions.Validate</c>).
    /// Throws <see cref="InvalidOperationException"/> on an invalid value so
    /// a misconfigured Worker fails fast at boot rather than on H14a's
    /// first dispatch.
    ///
    /// Scoped to: <see cref="KvReadTimeout"/> (task 160) + the four bounded
    /// sidecar fields (task 161: <see cref="SidecarBaseUrl"/> absolute-URI,
    /// <see cref="SidecarRequestTimeout"/> + <see cref="SidecarTransientRetryDelay"/>
    /// numeric bounds, and the delay-must-fit-under-timeout invariant).
    /// Deliberately leaves <see cref="SidecarSharedSecretVaultName"/> /
    /// <see cref="SidecarSharedSecretSubscriptionId"/> /
    /// <see cref="SidecarSharedSecretName"/> unvalidated at boot — empty
    /// values are legitimate in CI/dev where H14a is never dispatched; the
    /// runtime failure surfaces at the first ApplyAsync with an explicit
    /// diagnostic (fail-loud, never silent-empty-header).
    /// </summary>
    internal void Validate()
    {
        if (KvReadTimeout < TimeSpan.FromSeconds(1) || KvReadTimeout > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:KvReadTimeout' must be between " +
                $"1 second and 5 minutes (actual: {KvReadTimeout}).");
        }

        if (!string.IsNullOrWhiteSpace(ExchangeAdminAppId) && !Guid.TryParse(ExchangeAdminAppId, out _))
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:ExchangeAdminAppId' must be the client id " +
                $"(a GUID) of the 'Spaarke Exchange Admin' app registration (actual: '{ExchangeAdminAppId}').");
        }

        if (string.IsNullOrWhiteSpace(ExchangeAssignmentNamePrefix)
            || ExchangeAssignmentNamePrefix.Length > 16
            || !ExchangeAssignmentNamePrefix.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:ExchangeAssignmentNamePrefix' must be 1-16 " +
                $"letters, digits or hyphens (actual: '{ExchangeAssignmentNamePrefix}').");
        }

        if (!Uri.TryCreate(SidecarBaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:SidecarBaseUrl' must be an absolute URI " +
                $"(actual: '{SidecarBaseUrl}').");
        }

        if (SidecarRequestTimeout < TimeSpan.FromSeconds(30) || SidecarRequestTimeout > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:SidecarRequestTimeout' must be between " +
                $"30 seconds and 30 minutes (actual: {SidecarRequestTimeout}).");
        }

        if (SidecarTransientRetryDelay < TimeSpan.FromMilliseconds(100) || SidecarTransientRetryDelay > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:SidecarTransientRetryDelay' must be between " +
                $"100 milliseconds and 1 minute (actual: {SidecarTransientRetryDelay}).");
        }

        if (SidecarTransientRetryDelay >= SidecarRequestTimeout)
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:SidecarTransientRetryDelay' " +
                $"({SidecarTransientRetryDelay}) must be strictly less than SidecarRequestTimeout " +
                $"({SidecarRequestTimeout}) — otherwise the retry cannot complete within the request budget.");
        }

        // ALL-OR-NONE shared-secret config drift check (task 161 code-review W1).
        // A partial config-layer T1 trap: if only ONE of the 3 shared-secret
        // fields is set (e.g. an operator rotates SidecarSharedSecretName but
        // forgets to update SidecarSharedSecretVaultName / SubscriptionId),
        // the client would fail at first ApplyAsync with a confusing
        // half-configured diagnostic. Boot-time detection of partial
        // configuration is safe (never fires for the empty-triple CI/dev case
        // — that's the whole-empty branch, not partial) and catches rotation
        // drift immediately at Worker startup.
        var vaultSet = !string.IsNullOrWhiteSpace(SidecarSharedSecretVaultName);
        var subscriptionSet = !string.IsNullOrWhiteSpace(SidecarSharedSecretSubscriptionId);
        var nameSet = !string.IsNullOrWhiteSpace(SidecarSharedSecretName)
            && !string.Equals(SidecarSharedSecretName, "Sidecar-Shared-Secret", StringComparison.Ordinal);
        var explicitlySet = vaultSet || subscriptionSet || nameSet;
        var allSet = vaultSet && subscriptionSet
            && !string.IsNullOrWhiteSpace(SidecarSharedSecretName);   // the default is a valid populated name
        if (explicitlySet && !allSet)
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:SidecarSharedSecret*' partial " +
                "configuration detected — some of {VaultName, SubscriptionId, Name} are populated but " +
                "others are empty. Either bind ALL THREE (production posture) or leave ALL THREE empty " +
                "(CI/dev where H14a is never dispatched). Partial configuration is a T1 silent-fail " +
                "trap — a config-rotation half-update would surface only at first ApplyAsync with a " +
                "confusing diagnostic. Current values: " +
                $"VaultName='{SidecarSharedSecretVaultName}', " +
                $"SubscriptionId='{SidecarSharedSecretSubscriptionId}', " +
                $"Name='{SidecarSharedSecretName}'.");
        }
    }
}
