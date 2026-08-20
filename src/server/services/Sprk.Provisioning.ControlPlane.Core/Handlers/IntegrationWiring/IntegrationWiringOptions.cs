// -----------------------------------------------------------------------------
// IntegrationWiringOptions.cs
//
// Bound options for the H14 post-deploy integration wiring handler + its 3
// DAG-parallel sub-handler collaborators (H14a Exchange, H14b Graph webhooks,
// H14c Dataverse webhooks). Loaded from the "IntegrationWiring" configuration
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
// AppConfigSeedOptions precedent). Deliberately does NOT retroactively
// validate the pre-existing H14a/b/c fields (ExchangePolicyDescriptionPrefix,
// GraphRequestTimeout, DataverseRequestTimeout, ServiceEndpoint*) — those
// belong to H14a/H14b/H14c, which this task's constraint explicitly leaves
// UNCHANGED; widening the boot-time gate to fields this task doesn't own
// would be unreviewed scope creep, not a KV-reader swap.
//
// TASK 161 (Wave G-6): added 5 sidecar-client fields for the new
// ExchangePolicySidecarClient collaborator (replaces ExchangePolicyScriptApplier's
// pwsh + Set-ExchangeApplicationAccessPolicy.ps1 shell-out — see that file's
// retirement banner): SidecarBaseUrl, SidecarRequestTimeout,
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
// accidentally binding values that will silently do nothing. Note: the sidecar
// client STILL uses ExchangePolicyDescriptionPrefix below (sent as
// descriptionPrefix on the wire — Listener.ps1 line 23), so it is NOT dead.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Bound options for <see cref="H14IntegrationWiringHandler"/> + its 3
/// sub-handler collaborators. Configuration key: <c>IntegrationWiring</c>.
/// </summary>
public sealed class IntegrationWiringOptions
{
    // ---------- H14a Exchange policy naming (sidecar wire) ----------

    /// <summary>
    /// Description prefix applied to every <c>New-ApplicationAccessPolicy</c>
    /// call inside the sidecar so policies created by H14 are greppable in
    /// <c>Get-ApplicationAccessPolicy</c> output. Full description is
    /// <c>{prefix}-{appId}</c>. Sent to the sidecar on the wire as
    /// <c>descriptionPrefix</c> (Listener.ps1 line 23). Empty value causes
    /// the sidecar to fall back to its own server-side default
    /// (<c>Spaarke-Provisioning-AppAccessPolicy</c>); see
    /// <see cref="ExchangePolicySidecarClient.BuildWireRequest"/> for the
    /// omit-when-empty envelope behavior.
    /// </summary>
    public string ExchangePolicyDescriptionPrefix { get; set; } = "Spaarke-Provisioning-AppAccessPolicy";

    // ---------- H14b Graph webhooks ----------

    /// <summary>
    /// Timeout for a single Microsoft Graph REST call (list/create/patch
    /// subscription). Defaults to 30 seconds — parity with
    /// <see cref="DataverseAppUserGraphParity.H10DataverseAppUserGraphParityOptions"/>-style
    /// Graph collaborators elsewhere in L2.
    /// </summary>
    public TimeSpan GraphRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Subscription lifetime requested on Graph webhook create/renew, in
    /// minutes. Defaults to 4230 minutes (~2.94 days) — the Microsoft Graph
    /// documented maximum for most resource types as of the last verified
    /// check (2026-08). Subscription RENEWAL (a recurring background task
    /// that re-PATCHes <c>expirationDateTime</c> before it lapses) is
    /// explicitly OUT OF SCOPE for H14 (a provisioning-time handler, not a
    /// background service) — tracked as r2 follow-on work.
    /// </summary>
    public int GraphSubscriptionExpirationMinutes { get; set; } = 4230;

    // ---------- H14c Dataverse service-endpoint webhooks ----------

    /// <summary>
    /// Timeout for a single Dataverse Web API call (list/create/patch
    /// serviceendpoint). Defaults to 30 seconds — parity with
    /// <see cref="DataverseAppUserGraphParity.H10DataverseAppUserGraphParityOptions"/>.
    /// </summary>
    public TimeSpan DataverseRequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Dataverse <c>serviceendpoint.contract</c> option-set value to use when
    /// creating the webhook-shaped service endpoint. Exposed as a
    /// configuration knob (rather than a hardcoded magic number) because the
    /// exact numeric value is environment/SDK-version sensitive and this
    /// path is NOT exercised by the CI unit suite (real Dataverse Web API
    /// call — parity with every other H-series live-REST collaborator).
    /// Default 8 matches the Microsoft Dataverse SDK's documented
    /// <c>ServiceEndpoint.Contract</c> "WebHook" enum member as of the last
    /// verified check (2026-08) — RECONFIRM against the target environment's
    /// <c>serviceendpoint</c> entity metadata before a production customer
    /// stamp (H0 preflight / operator runbook item).
    /// </summary>
    public int ServiceEndpointContractValue { get; set; } = 8;

    /// <summary>
    /// Dataverse <c>serviceendpoint.messageformat</c> option-set value.
    /// Default 2 (JSON) per Microsoft Dataverse SDK documentation as of the
    /// last verified check (2026-08). Same RECONFIRM caveat as
    /// <see cref="ServiceEndpointContractValue"/>.
    /// </summary>
    public int ServiceEndpointMessageFormatValue { get; set; } = 2;

    /// <summary>
    /// Dataverse <c>serviceendpoint.authtype</c> option-set value. Default 5
    /// ("None" — the HMAC signing key travels as a custom header the
    /// receiving BFF endpoint verifies, not via the serviceendpoint's own
    /// SAS-key auth path) per the same RECONFIRM caveat.
    /// </summary>
    public int ServiceEndpointAuthTypeValue { get; set; } = 5;

    // ---------- KV reader (task 160, SecretClientKvReader) ----------

    /// <summary>
    /// Maximum wall-clock time for a single Azure Key Vault
    /// <c>SecretClient.GetSecretAsync</c> call issued by
    /// <see cref="SecretClientKvReader"/> (task 160, Wave G-6 — SDK port
    /// replacing <see cref="AzCliKvSecretReader"/>'s `az keyvault secret
    /// show` shell-out). Defaults to 30 seconds — parity with
    /// <see cref="GraphRequestTimeout"/> / <see cref="DataverseRequestTimeout"/>.
    /// </summary>
    public TimeSpan KvReadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // ---------- Sidecar client (task 161, ExchangePolicySidecarClient) ----------

    /// <summary>
    /// Base URI of the H14a Exchange ApplicationAccessPolicy sidecar
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
    /// <c>POST /apply-policy</c> HTTP call. Encompasses sidecar cold-start
    /// (~5-10 s), the <c>Set-ExchangeApplicationAccessPolicy.ps1</c> execution
    /// (Exchange Online connect + Get-/New-ApplicationAccessPolicy — the
    /// script's own upper bound is 5 minutes, matched by Listener.ps1's
    /// <c>timeoutSeconds</c> default of 300), and margin. Defaults to 6
    /// minutes. Sidecar-side enforcement is currently caller-side per
    /// Listener.ps1's Invoke-ExchangePolicyScript comment (<c>timeoutSeconds</c>
    /// on the wire is ADVISORY; the HTTP client's <c>Timeout</c> is the real
    /// bound). Validated in <c>[30 s, 30 min]</c>.
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
    /// a misconfigured Worker fails fast at boot rather than on H14b/H14c/H14a's
    /// first dispatch.
    ///
    /// Scoped to: <see cref="KvReadTimeout"/> (task 160) + the four bounded
    /// sidecar fields (task 161: <see cref="SidecarBaseUrl"/> absolute-URI,
    /// <see cref="SidecarRequestTimeout"/> + <see cref="SidecarTransientRetryDelay"/>
    /// numeric bounds, and the delay-must-fit-under-timeout invariant).
    /// Deliberately does NOT retroactively validate the pre-existing
    /// H14a/b/c fields (ExchangePolicyDescriptionPrefix / Graph* /
    /// Dataverse* / ServiceEndpoint*) — same rationale as task 160's
    /// file-header note (widening the boot-time gate to fields this wave's
    /// tasks don't own would be unreviewed scope creep). Also
    /// deliberately leaves <see cref="SidecarSharedSecretVaultName"/> /
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
