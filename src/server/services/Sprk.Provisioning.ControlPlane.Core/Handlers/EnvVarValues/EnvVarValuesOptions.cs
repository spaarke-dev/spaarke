// -----------------------------------------------------------------------------
// EnvVarValuesOptions.cs
//
// Bound options for the H7 handler's Dataverse Web API writer collaborator.
// Loaded from the "EnvVarValues" configuration section (SectionName below) by
// Program.cs. Parity with SolutionImportOptions (H6) + DataverseEnvCreationOptions
// (H5) for the shape of the class; parity with DataverseEnvironmentRegistryOptions
// (task 112/122) for the fail-fast Validate()/ValidateOnStart() wiring added
// by task 142 (Wave G-4, H7 credential provisioning + NFR-05).
// -----------------------------------------------------------------------------

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
    /// </summary>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:ClientSecret' is required. Set the shared BFF app-reg " +
                "client secret H7 authenticates to customer Dataverse environments with (same identity " +
                "H6 uses for solution import). In deployed environments this is bound via the Worker App " +
                "Service's EnvVarValues__ClientSecret KV-reference app setting " +
                "(modules/controlplane-worker-app-service.bicep, sourced from the platform Key Vault's " +
                "canonical 'BFF-API-ClientSecret' secret).");
        }
        if (RequestTimeout < TimeSpan.FromSeconds(1) || RequestTimeout > TimeSpan.FromMinutes(5))
        {
            throw new InvalidOperationException(
                $"Configuration '{SectionName}:RequestTimeout' must be between 1 second and 5 minutes (actual: {RequestTimeout}).");
        }
    }
}
