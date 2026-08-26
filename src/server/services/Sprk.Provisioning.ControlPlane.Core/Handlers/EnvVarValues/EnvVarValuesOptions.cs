// -----------------------------------------------------------------------------
// EnvVarValuesOptions.cs
//
// Bound options for the H7 handler's Dataverse Web API writer collaborator.
// Loaded from the "EnvVarValues" configuration section (SectionName below) by
// Program.cs. Parity with SolutionImportOptions (H6) + DataverseEnvCreationOptions
// (H5) for the shape of the class; parity with DataverseEnvironmentRegistryOptions
// (task 112/122) for the fail-fast Validate()/ValidateOnStart() wiring added
// by task 142 (Wave G-4, H7 credential provisioning + NFR-05).
//
// A44.5 (task 205i, 2026-08-25 — H7/task-142 half of A30's sentinel
// contract): ClientSecret is no longer UNCONDITIONALLY required. The FR-39
// ordered credential chain (Credentials property, mirror of the BFF's
// Graph:Credentials contract per master's DataverseServiceClientImpl
// migration brought in via A35) decides: MI-FIC-first chains accept an EMPTY
// secret slot (empty is the SIGNAL on secret-free envs — auth-v4 §9.1; never
// a sentinel), while the legacy/unconfigured [ClientSecret] chain preserves
// the task-142 boot fail-fast byte-for-byte (prong-3 unmigrated envs, §6.5
// resolution record). Invalid chain configuration ALWAYS fail-fasts.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.Credentials;

namespace Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

/// <summary>
/// Bound options for <see cref="H7DataverseEnvVarValuesHandler"/>'s
/// <see cref="IEnvVarValuesWriter"/> collaborator. Configuration key:
/// <see cref="SectionName"/> (<c>EnvVarValues</c>).
/// </summary>
public sealed class EnvVarValuesOptions
{
    /// <summary>Configuration section name (bound via Program.cs <c>AddOptions&lt;EnvVarValuesOptions&gt;()</c>).</summary>
    public const string SectionName = "EnvVarValues";

    /// <summary>
    /// Maximum wall-clock time for a single Dataverse Web API HTTP call
    /// (definition lookup or value PATCH/POST). Defaults to 30 seconds —
    /// env-var upserts are single-record CRUD operations, not long-running
    /// jobs.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Client secret for the BFF Entra app registration
    /// (<see cref="Models.InterStepState.BffAppRegId"/>). H7 authenticates to
    /// the target Dataverse env via OAuth2 client-credentials (confidential
    /// client) using this secret — the SAME identity + auth pattern H6 uses
    /// for solution import (<c>pac auth create --clientSecret</c>), because
    /// the MI-Dataverse App User (H10) has not yet been created at H7's point
    /// in the DAG (H10 runs AFTER H7 per design.md §4.1 handler catalog row:
    /// "H5 → H6 (solutions) → H7 → H10 (app-user, needs H6 solutions) → H11").
    /// As of task 142 (Wave G-4), <c>modules/controlplane-worker-app-service.bicep</c>
    /// wires this UNCONDITIONALLY as a Key Vault app-setting reference
    /// (<c>EnvVarValues__ClientSecret</c> -&gt; <c>@Microsoft.KeyVault(VaultName=…;SecretName=BFF-API-ClientSecret)</c>)
    /// -- REQUIRED in every deployed environment; <see cref="Validate"/> fails
    /// fast at Worker boot (NFR-05) if unset. Handler ALSO emits
    /// <see cref="EnvVarValuesRejectionCodes.MissingClientSecret"/> at runtime
    /// as defense-in-depth if this seam is ever reached without DI startup
    /// validation.
    /// </summary>
    /// <remarks>
    /// For wave-C4 unit tests, this is set to a non-empty placeholder via
    /// <c>Options.Create(...)</c> so tests exercise the happy path without
    /// depending on a real KV. Parity with H6's <c>SolutionImportOptions.ClientSecret</c>
    /// remark — this options-bound field is NOT persisted to Cosmos.
    /// </remarks>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// FR-39 ordered credential chain for H7's Dataverse auth (A44.5, task
    /// 205i). Bound from <c>EnvVarValues:Credentials</c> — the exact
    /// structural analogue of the BFF's <c>Graph:Credentials</c> section
    /// (<c>EnvVarValues__Credentials__Order__0=ManagedIdentityFederated</c> +
    /// <c>EnvVarValues__Credentials__RequireSecretFreeIdentity=true</c> on
    /// secret-free environments per the §10.2 live contract). Unconfigured =
    /// legacy <c>[ClientSecret]</c> chain — task-142 behavior preserved for
    /// prong-3 unmigrated environments.
    /// </summary>
    public WorkerCredentialSelectionOptions Credentials { get; set; } = new();

    /// <summary>
    /// Startup validation applied by Program.cs's
    /// <c>AddOptions&lt;EnvVarValuesOptions&gt;().ValidateOnStart()</c>
    /// registration (task 142, Wave G-4). Throws
    /// <see cref="InvalidOperationException"/> on invalid values so a
    /// misconfigured Worker fails fast at boot (NFR-05 parity with
    /// <c>DataverseEnvironmentRegistryOptions.Validate</c> /
    /// <c>CustomerRunGuardOptions.Validate</c>). This is a boot-time layer
    /// ON TOP OF the handler's existing runtime MissingClientSecret guard
    /// (H7DataverseEnvVarValuesHandler.cs step (2)) -- both are kept; see
    /// file header for why.
    ///
    /// <para><b>A44.5 relaxation (two distinct code paths, both fail-fast on
    /// invalid chain config):</b> the empty-<see cref="ClientSecret"/> boot
    /// fail-fast applies ONLY when the effective FR-39 chain's primary
    /// credential is <see cref="CredentialKind.ClientSecret"/> (legacy /
    /// prong-3 unmigrated envs — task-142 semantics unchanged). Under an
    /// MI-FIC-first chain an empty secret slot is ACCEPTED: on secret-free
    /// environments empty is the SIGNAL (auth-v4 §9.1) and the Bicep KV-ref
    /// is conditionally omitted. Chain-shape violations (unknown kind,
    /// duplicate, RequireSecretFreeIdentity contradiction) ALWAYS throw via
    /// <see cref="WorkerCredentialSelectionOptions.ResolveEffectiveOrder"/>.</para>
    /// </summary>
    internal void Validate()
    {
        // Fail-fast on invalid provider-chain configuration FIRST (A44.5) —
        // an unparseable/contradictory chain must never reach a handler.
        var secretRequired = Credentials.ClientSecretIsRequiredFirst(SectionName);

        if (secretRequired && string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:ClientSecret' is required when the FR-39 credential chain's " +
                $"primary is {nameof(CredentialKind.ClientSecret)} (the legacy/unconfigured default — prong-3 " +
                "unmigrated environment). Set the shared BFF app-reg " +
                "client secret H7 authenticates to customer Dataverse environments with (same identity " +
                "H6 uses for solution import). In deployed environments this is bound via the Worker App " +
                "Service's EnvVarValues__ClientSecret KV-reference app setting " +
                "(modules/controlplane-worker-app-service.bicep, sourced from the platform Key Vault's " +
                "canonical 'BFF-API-ClientSecret' secret; emitted only when requireSecretFreeIdentity=false). " +
                $"Secret-free environments instead set '{SectionName}:Credentials:Order:0' = " +
                $"{nameof(CredentialKind.ManagedIdentityFederated)} — NEVER a placeholder value in this slot " +
                "(auth-v4 §9.1: a sentinel fails opaquely with AADSTS7000215).");
        }
        if (RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:RequestTimeout' must be between 1 second and 5 minutes (actual: {RequestTimeout}).");
        }
    }
}
