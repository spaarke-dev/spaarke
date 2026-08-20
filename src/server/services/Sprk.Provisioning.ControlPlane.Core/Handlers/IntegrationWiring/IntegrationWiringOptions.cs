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
// validate the 7 pre-existing fields below (PwshExecutable,
// ExchangePolicyScriptPath, ExchangeScriptTimeout, GraphRequestTimeout,
// DataverseRequestTimeout, ServiceEndpoint*) — those belong to H14a/H14b/H14c,
// which this task's constraint explicitly leaves UNCHANGED; widening the
// boot-time gate to fields this task doesn't own would be unreviewed scope
// creep, not a KV-reader swap.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Bound options for <see cref="H14IntegrationWiringHandler"/> + its 3
/// sub-handler collaborators. Configuration key: <c>IntegrationWiring</c>.
/// </summary>
public sealed class IntegrationWiringOptions
{
    // ---------- H14a Exchange (script shell-out) ----------

    /// <summary>
    /// Path to the pwsh executable. Defaults to <c>pwsh</c> (resolved via
    /// PATH). Parity with <see cref="EntraAppReg.EntraAppRegOptions.PwshExecutable"/>.
    /// </summary>
    public string PwshExecutable { get; set; } = "pwsh";

    /// <summary>
    /// Absolute path to <c>scripts/Set-ExchangeApplicationAccessPolicy.ps1</c>.
    /// Defaults to <c>scripts/Set-ExchangeApplicationAccessPolicy.ps1</c>
    /// relative to <see cref="AppContext.BaseDirectory"/>; production
    /// deployments should override via app-setting so the linux-x64 publish
    /// layout is honored.
    /// </summary>
    public string ExchangePolicyScriptPath { get; set; }
        = Path.Combine(AppContext.BaseDirectory, "scripts", "Set-ExchangeApplicationAccessPolicy.ps1");

    /// <summary>
    /// Maximum time to wait for a single Set-ExchangeApplicationAccessPolicy.ps1
    /// invocation. Defaults to 5 minutes — Exchange Online connect + policy
    /// list/create typically completes in under a minute, but EXO throttling
    /// can extend it.
    /// </summary>
    public TimeSpan ExchangeScriptTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Description prefix applied to every <c>New-ApplicationAccessPolicy</c>
    /// call so policies created by H14 are greppable in
    /// <c>Get-ApplicationAccessPolicy</c> output. Full description is
    /// <c>{prefix}-{appId}</c>.
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

    /// <summary>
    /// Startup validation applied by <see cref="IntegrationWiringModule.AddH14IntegrationWiringHandler"/>'s
    /// <c>AddOptions&lt;IntegrationWiringOptions&gt;().ValidateOnStart()</c>
    /// registration (task 160, NFR-05 parity with
    /// <c>RuntimeReferencesOptions.Validate</c> / <c>AppConfigSeedOptions.Validate</c>).
    /// Throws <see cref="InvalidOperationException"/> on an invalid value so
    /// a misconfigured Worker fails fast at boot rather than on H14b/H14c's
    /// first dispatch. Scoped to ONLY <see cref="KvReadTimeout"/> — see this
    /// file's header for why the 7 pre-existing H14a/b/c fields are
    /// deliberately left out of this task's validation surface.
    /// </summary>
    internal void Validate()
    {
        if (KvReadTimeout < TimeSpan.FromSeconds(1) || KvReadTimeout > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"Configuration '{IntegrationWiringModule.ConfigSection}:KvReadTimeout' must be between " +
                $"1 second and 5 minutes (actual: {KvReadTimeout}).");
        }
    }
}
