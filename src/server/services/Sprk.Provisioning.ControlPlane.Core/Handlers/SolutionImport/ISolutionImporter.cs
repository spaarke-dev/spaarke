// -----------------------------------------------------------------------------
// ISolutionImporter.cs
//
// L2 abstraction over the import of the Spaarke Dataverse package into a
// customer environment (handler H6). Production implementation:
// <see cref="DataverseWebApiSolutionImporter"/> (Dataverse Web API
// ImportSolution / StageAndUpgrade + importjobs polling). Unit tests inject
// stubs.
//
// T218b (ADR-027 §3-§4, amended 2026-10-07): the package is ONE solution,
// <see cref="SpaarkePackage.SolutionUniqueName"/>, managed by default and
// unmanaged only on explicit instruction. The importer reads the installed
// solution FIRST and refuses — nothing imported — a managed↔unmanaged switch
// and a downgrade.
//
// NON-GOALS (this seam):
//   - Does NOT own post-import verification — that's <see cref="ISolutionVerifier"/>'s
//     job (called after this).
//   - Does NOT resolve credentials beyond receiving the values as method args.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Imports the Spaarke package into a customer's Dataverse environment for handler H6.
/// </summary>
public interface ISolutionImporter
{
    /// <summary>
    /// Imports (or upgrades to) the published SpaarkeMaster package of the requested type. Returns a typed outcome —
    /// <see cref="SolutionImportOutcome.Success"/> when imported or already at the package version;
    /// <see cref="SolutionImportOutcome.Failure"/> with a classified <see cref="SolutionImportFailureKind"/> otherwise.
    /// Domain failures do NOT throw; infrastructure faults MAY throw.
    /// </summary>
    /// <param name="request">Import inputs (customerId, tenantId, clientId, clientSecret, target env URL, package type).</param>
    /// <param name="cancellationToken">Cancellation token — the long-running (up to 60 min) import MUST honor it.</param>
    Task<SolutionImportOutcome> ImportAsync(
        SolutionImportRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to one package import. Immutable record; the caller (<see cref="H6SolutionImportHandler"/>) constructs one
/// per run.
/// </summary>
/// <param name="CustomerId">Customer partition key (customerId standard: 3-8 lowercase letters/digits, starts with a letter).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — MUST be explicit, never default).</param>
/// <param name="ClientId">BFF Entra app registration id (H3 output — InterStepState.BffAppRegId).</param>
/// <param name="ClientSecret">
/// Resolved client secret, NEVER logged. A44.5 (task 205i): MAY be <c>null</c>/empty on secret-free environments —
/// <see cref="DataverseWebApiSolutionImporter"/> then resolves its credential from the FR-39 ordered chain
/// (<see cref="Credentials.WorkerDataverseCredentialFactory"/>, MI-FIC first). Empty is the SIGNAL (auth-v4 §9.1);
/// never pass a sentinel value.
/// </param>
/// <param name="TargetDataverseUrl">Target customer Dataverse env URL (H5 output — InterStepState.DataverseEnvUrl).</param>
/// <param name="Managed">T218b — <c>true</c> imports the managed package (the default); <c>false</c> the unmanaged one.</param>
public sealed record SolutionImportRequest(
    string CustomerId,
    string TenantId,
    string ClientId,
    string? ClientSecret,
    string TargetDataverseUrl,
    bool Managed);

/// <summary>
/// Discriminated result of <see cref="ISolutionImporter.ImportAsync"/>. Failure carries a classified
/// <see cref="SolutionImportFailureKind"/> the handler maps to a §4C class + <see cref="SolutionImportRejectionCodes"/> value.
/// </summary>
public abstract record SolutionImportOutcome
{
    private SolutionImportOutcome() { }

    /// <summary>The package is installed at the published version (imported now, or already there).</summary>
    public sealed record Success() : SolutionImportOutcome;

    /// <summary>
    /// The import did not complete. <paramref name="FailureKind"/> tells the handler how to classify the failure per
    /// §4C rollback; <paramref name="Diagnostic"/> is the operator-facing message.
    /// </summary>
    public sealed record Failure(
        SolutionImportFailureKind FailureKind,
        string Diagnostic) : SolutionImportOutcome;
}

/// <summary>
/// Classified failure kinds surfaced by <see cref="ISolutionImporter"/>. The handler maps each to a
/// <see cref="SolutionImportRejectionCodes"/> value + §4C <see cref="Handlers.FailureClass"/> classification.
/// </summary>
public enum SolutionImportFailureKind
{
    /// <summary>Token acquisition failed, or Dataverse refused the identity (401/403, missing privilege). Resumable.</summary>
    AuthFailure = 1,

    /// <summary>Dataverse throttled the call (429). Resumable.</summary>
    RateLimited = 2,

    /// <summary>Environment capacity / storage quota reached. Resumable.</summary>
    QuotaExhausted = 3,

    /// <summary>
    /// The package artifact is unusable: manifest missing or unparseable, no SpaarkeMaster entry, no blob for the
    /// requested type, blob missing, or version undeterminable. Resumable — publish the package and resume.
    /// </summary>
    MissingSolutionZips = 4,

    /// <summary>
    /// A <c>StageAndUpgrade</c> failed after it started — a holding solution may be left behind. QuarantineRequired.
    /// </summary>
    PartialImport = 5,

    /// <summary>The import job did not finish within <see cref="SolutionImportOptions.ImportTimeout"/>. Resumable.</summary>
    Timeout = 6,

    /// <summary>Unclassified failure with no upgrade in progress (no partial state). Resumable.</summary>
    UnknownInvocationFailure = 7,

    /// <summary>T218b — the environment holds SpaarkeMaster of the other type. Nothing imported. Resumable.</summary>
    PackageTypeMismatch = 8,

    /// <summary>T218b — the environment holds a higher SpaarkeMaster version than the package. Nothing imported. Resumable.</summary>
    DowngradeRefused = 9,
}
