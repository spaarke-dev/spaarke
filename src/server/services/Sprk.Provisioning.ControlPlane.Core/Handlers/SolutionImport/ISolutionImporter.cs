// -----------------------------------------------------------------------------
// ISolutionImporter.cs
//
// L2 abstraction over the actual solution-import invocation. The production
// implementation (<see cref="DeployDataverseSolutionsScriptImporter"/>) shells
// out to <c>scripts/Deploy-DataverseSolutions.ps1</c> — the task 012 (wave 0)
// hardened Package Deployer wrapper whose $SolutionImportOrder IS the R5
// binding solution-list authority (task 008 audit + POML constraint 3). Unit
// tests inject stubs to avoid pwsh + real Dataverse round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="DeployDataverseSolutionsScriptImporter"/> —
//       shells out to Deploy-DataverseSolutions.ps1 with -ClientId +
//       -ClientSecret + -EnvironmentUrl + -TenantId + -Mode.
//     - Test: stubs injected per unit test that construct
//       <see cref="SolutionImportOutcome"/> directly.
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE (shell-out vs SDK vs re-implement):
//   Same trade-off as H2a (shells out to Provision-Customer.ps1), H2b (shells
//   out to Deploy-AllIndexes.ps1), and H12a (shells out to Invoke-SeedManifest.ps1):
//   the wave-0 PS script IS the source-of-truth for the dependency-tier
//   ordering + Package Deployer stage-and-upgrade semantics + per-tier
//   Test-TierImport gate. Re-implementing in C# via Microsoft.PowerPlatform.
//   Dataverse.Client's Package Deployer surface would duplicate ~500 lines
//   of tested PS logic. H6 therefore WRAPS the script (adding Cosmos state
//   management + idempotency + post-import verification via a separate seam)
//   rather than replacing it.
//
// NON-GOALS (this seam):
//   - Does NOT own post-import verification — that's <see cref="ISolutionVerifier"/>'s
//     job (called after this).
//   - Does NOT own the canonical solution list — that's <see cref="ISolutionCatalog"/>
//     (which mirrors the PS script's $SolutionImportOrder; the PS remains
//     the runtime authority since the importer does not pass -SolutionsToImport).
//   - Does NOT own auth-secret resolution beyond receiving the resolved
//     values as method args — Wave C5 wires Key Vault; wave C4 reads from
//     bound options.
//
// LONG-RUNNING SEMANTICS (design.md § 4.2 fire-and-forget):
//   The PS script blocks synchronously until Dataverse acknowledges each
//   tier + all rollup verification passes. 8 solutions × up to 5 min per
//   large solution = up to 40 min per import. The handler's CancellationToken
//   threads through the shell-out timeout so the outer polling / timeout
//   window is authoritative. The pwsh process MUST honor the token
//   (process.Kill after the token trips).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Executes the per-customer solution import for handler H6. Production impl
/// shells out to <c>scripts/Deploy-DataverseSolutions.ps1</c>; test impls
/// return canned <see cref="SolutionImportOutcome"/>s.
/// </summary>
public interface ISolutionImporter
{
    /// <summary>
    /// Invokes the Deploy-DataverseSolutions.ps1 orchestrator against the
    /// customer's Dataverse env. Returns a typed outcome — Success on
    /// exit 0 (all tiers imported + PS-side per-tier verification passed);
    /// Failure on any non-zero exit with a classified
    /// <see cref="SolutionImportFailureKind"/>. Domain failures do NOT throw;
    /// infrastructure faults (script missing, pwsh missing) MAY throw.
    /// </summary>
    /// <param name="request">Import inputs (customerId, tenantId, clientId, clientSecret, target env URL).</param>
    /// <param name="cancellationToken">Cancellation token — the long-running (up to 60 min) import MUST honor it (process.Kill on trip).</param>
    Task<SolutionImportOutcome> ImportAsync(
        SolutionImportRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single Deploy-DataverseSolutions.ps1 invocation. Immutable
/// record; the caller (<see cref="H6SolutionImportHandler"/>) constructs one
/// per run.
/// </summary>
/// <param name="CustomerId">Customer partition key (3-10 lowercase alphanumeric).</param>
/// <param name="TenantId">Entra tenant id (§4D I1 — MUST be explicit, never default).</param>
/// <param name="ClientId">BFF Entra app registration id (H3 output — populated by upstream H3 into InterStepState.BffAppRegId).</param>
/// <param name="ClientSecret">Resolved client secret. NEVER logged; passed via env var to the pwsh child process.</param>
/// <param name="TargetDataverseUrl">Target customer Dataverse env URL (H5 output — populated by upstream H5 into InterStepState.DataverseEnvUrl).</param>
public sealed record SolutionImportRequest(
    string CustomerId,
    string TenantId,
    string ClientId,
    string ClientSecret,
    string TargetDataverseUrl);

/// <summary>
/// Discriminated result of <see cref="ISolutionImporter.ImportAsync"/>. Success
/// on exit 0 (all 8 solutions imported + per-tier verified); Failure carries a
/// classified <see cref="SolutionImportFailureKind"/> the handler maps to a
/// §4C class + <see cref="SolutionImportRejectionCodes"/> value.
/// </summary>
public abstract record SolutionImportOutcome
{
    private SolutionImportOutcome() { }

    /// <summary>PS script exited 0 — all 8 solutions imported + per-tier verification passed.</summary>
    public sealed record Success() : SolutionImportOutcome;

    /// <summary>
    /// PS script exited non-zero. <paramref name="FailureKind"/> tells the
    /// handler how to classify the failure per §4C rollback;
    /// <paramref name="Diagnostic"/> is the operator-facing message
    /// (stderr + stdout tail).
    /// </summary>
    public sealed record Failure(
        SolutionImportFailureKind FailureKind,
        string Diagnostic) : SolutionImportOutcome;
}

/// <summary>
/// Classified failure kinds surfaced by <see cref="ISolutionImporter"/>. The
/// handler maps each to a <see cref="SolutionImportRejectionCodes"/> value +
/// §4C <see cref="Handlers.FailureClass"/> classification.
/// </summary>
public enum SolutionImportFailureKind
{
    /// <summary>
    /// PS script reported PAC CLI authentication / authorization failure
    /// (missing System Administrator role, expired secret, tenant mismatch).
    /// Maps to <see cref="SolutionImportRejectionCodes.PacAuthFailure"/>
    /// (Resumable).
    /// </summary>
    AuthFailure = 1,

    /// <summary>
    /// PS script reported rate-limit / throttle response from Dataverse.
    /// Maps to <see cref="SolutionImportRejectionCodes.RateLimited"/>
    /// (Resumable).
    /// </summary>
    RateLimited = 2,

    /// <summary>
    /// PS script reported quota exhaustion (env storage limit hit, etc.).
    /// Maps to <see cref="SolutionImportRejectionCodes.QuotaExhausted"/>
    /// (Resumable).
    /// </summary>
    QuotaExhausted = 3,

    /// <summary>
    /// PS script reported solution ZIPs missing on disk at the configured
    /// SolutionPath. Maps to
    /// <see cref="SolutionImportRejectionCodes.MissingSolutionZips"/>
    /// (Resumable — operator rebuilds solution pack + resumes).
    /// </summary>
    MissingSolutionZips = 4,

    /// <summary>
    /// PS script exited non-zero AFTER at least one tier's solutions
    /// imported successfully. Detected via stderr patterns matching
    /// "Tier N had import failure" or "aborted BEFORE attempting subsequent
    /// tiers". Package Deployer stage-and-upgrade may leave a holding
    /// solution behind → QuarantineRequired (operator manually inspects +
    /// retires orphaned holding solutions).
    /// </summary>
    PartialImport = 5,

    /// <summary>
    /// PS script exceeded the configured
    /// <see cref="SolutionImportOptions.ImportTimeout"/> window. Maps to
    /// <see cref="SolutionImportRejectionCodes.ImportTimeout"/> (Resumable —
    /// operator inspects `pac solution list` and resumes idempotently).
    /// </summary>
    Timeout = 6,

    /// <summary>
    /// PS script exited non-zero without a classified subtype AND the
    /// verifier confirms no solutions imported (no partial state). Maps to
    /// <see cref="SolutionImportRejectionCodes.ImportInvocationFailed"/>
    /// (Resumable — no side effect, operator inspects stderr + resumes).
    /// </summary>
    UnknownInvocationFailure = 7,
}
