// -----------------------------------------------------------------------------
// EnvVarValuesOptions.cs
//
// Bound options for the H7 handler's Dataverse Web API writer collaborator.
// Loaded from the "EnvVarValuesOptions" configuration section by Program.cs.
// Parity with SolutionImportOptions (H6) + DataverseEnvCreationOptions (H5).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;

/// <summary>
/// Bound options for <see cref="H7DataverseEnvVarValuesHandler"/>'s
/// <see cref="IEnvVarValuesWriter"/> collaborator. Configuration key:
/// <c>EnvVarValuesOptions</c>.
/// </summary>
public sealed class EnvVarValuesOptions
{
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
    /// MUST be null / whitespace in checked-in configs; the wave-C5 KV wiring
    /// populates this via a Key Vault app-setting reference
    /// <c>@Microsoft.KeyVault(SecretUri=…)</c>. Handler emits
    /// <see cref="EnvVarValuesRejectionCodes.MissingClientSecret"/> if unset
    /// when H7 dispatches.
    /// </summary>
    /// <remarks>
    /// For wave-C4 unit tests, this is set to a non-empty placeholder via
    /// <c>Options.Create(...)</c> so tests exercise the happy path without
    /// depending on a real KV. Parity with H6's <c>SolutionImportOptions.ClientSecret</c>
    /// remark — this options-bound field is NOT persisted to Cosmos.
    /// </remarks>
    public string? ClientSecret { get; set; }
}
