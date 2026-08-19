// -----------------------------------------------------------------------------
// IDataverseEnvCreator.cs
//
// L2 abstraction over the actual Dataverse env-creation invocation. The
// production implementation (<see cref="PacAdminDataverseEnvCreator"/>) shells
// out to <c>pac admin create-environment</c> (interim per design.md § 4.1 H5
// row + M-10 deferral of TF Power Platform provider adoption). Unit tests
// inject stubs to avoid pac CLI + real Dataverse round-trips.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="PacAdminDataverseEnvCreator"/> — shells out to
//       `pac admin create-environment` with parsed environment-url output.
//     - Test: stubs injected per unit test that construct
//       <see cref="DataverseEnvCreationOutcome"/> directly (see
//       <c>H5DataverseEnvCreationHandlerTests</c>).
//   Interface earns its keep — no NIH.
//
// DESIGN CHOICE (shell-out vs SDK vs TF):
//   Same trade-off as H2a (shells out to Provision-Customer.ps1) and H0
//   preflight probes (shell out to scripts/preflight/*.ps1): the pac CLI IS
//   the source-of-truth surface for Dataverse env creation until the TF
//   Power Platform provider migration lands (design.md § 4.1 M-10 deferral).
//   Re-implementing in C# via Dataverse admin SDK is a large surface area
//   that duplicates what the CLI already tested + supported. H5 therefore
//   WRAPS the CLI (adding a post-condition Web-API health check the CLI does
//   not perform, per POML acceptance criterion #3) rather than replacing it.
//
// LONG-RUNNING SEMANTICS (design.md § 4.2 fire-and-forget):
//   `pac admin create-environment` is a SYNCHRONOUS blocking call from the
//   caller's perspective — the CLI itself waits until Dataverse acknowledges
//   env-creation completion (typically 15–30 min per design.md § 4.2). The
//   handler's CancellationToken threads through the shell-out timeout so
//   the outer polling / timeout window is authoritative. The `pac` process
//   MUST honor the token (SIGTERM / process.Kill after the token trips).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <summary>
/// Executes the per-customer Dataverse environment creation for handler H5.
/// Production impl shells out to <c>pac admin create-environment</c>; test
/// impls return canned <see cref="DataverseEnvCreationOutcome"/>s.
/// </summary>
public interface IDataverseEnvCreator
{
    /// <summary>
    /// Creates a Dataverse environment for the customer. Returns a typed
    /// outcome — success carries the environment URL that downstream
    /// handlers (H6 solution import, H7 env-var values, H10 app user)
    /// consume via <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.DataverseEnvUrl"/>;
    /// failure carries a classified diagnostic. Domain failures do NOT
    /// throw — infrastructure faults MAY throw (parity with
    /// <see cref="IPreflightQuotaProbe"/>).
    /// </summary>
    /// <param name="request">Env-creation inputs (customerId, tenantId, region, tier, optional displayName).</param>
    /// <param name="cancellationToken">Cancellation token — the long-running (15–30 min) pac invocation MUST honor it (process.Kill on trip).</param>
    Task<DataverseEnvCreationOutcome> CreateEnvironmentAsync(
        DataverseEnvCreationRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single Dataverse env-creation invocation. Immutable record;
/// the caller (<see cref="H5DataverseEnvCreationHandler"/>) constructs one
/// per run from
/// <see cref="Sprk.Provisioning.ControlPlane.Models.ProvisioningRun.Parameters"/>.
/// </summary>
/// <param name="CustomerId">Customer partition key (3-10 lowercase alphanumeric).</param>
/// <param name="TenantId">
/// Entra tenant id (§4D I1 — MUST be explicit, never default). Flows through
/// to <c>pac admin create-environment -TenantId</c> so the pac CLI targets
/// the correct tenant.
/// </param>
/// <param name="Region">
/// Dataverse region code (e.g. <c>unitedstates</c>, <c>europe</c>) — required
/// by <c>pac admin create-environment --region</c>. No default; operator MUST
/// supply.
/// </param>
/// <param name="Tier">
/// Dataverse env type (<c>Sandbox</c> | <c>Production</c> | <c>Trial</c>). Note
/// per design.md D14 rationale (R15): SPs cannot create <c>Developer</c>-type
/// envs — the caller MUST reject <c>Developer</c> before dispatching.
/// </param>
/// <param name="DisplayName">
/// Optional friendly name shown in the Power Platform Admin Center. Defaults
/// to <c>{CustomerId} (Spaarke)</c> when null/empty.
/// </param>
public sealed record DataverseEnvCreationRequest(
    string CustomerId,
    string TenantId,
    string Region,
    string Tier,
    string? DisplayName);

/// <summary>
/// Discriminated result of <see cref="IDataverseEnvCreator.CreateEnvironmentAsync"/>.
/// Success carries the environment URL; Failure carries a classified diagnostic
/// (later mapped to a <see cref="DataverseEnvCreationRejectionCodes"/> value by
/// the handler based on <see cref="Failure.FailureKind"/>).
/// </summary>
public abstract record DataverseEnvCreationOutcome
{
    private DataverseEnvCreationOutcome() { }

    /// <summary>Env creation succeeded — <paramref name="EnvironmentUrl"/> is the customer's Dataverse Web API base URL.</summary>
    /// <param name="EnvironmentUrl">Full URL (e.g. <c>https://spaarke-acme.crm.dynamics.com/</c>) — the value H5 writes to <see cref="Sprk.Provisioning.ControlPlane.Models.InterStepState.DataverseEnvUrl"/>.</param>
    public sealed record Success(string EnvironmentUrl) : DataverseEnvCreationOutcome;

    /// <summary>
    /// Env creation failed. <paramref name="FailureKind"/> tells the handler
    /// how to classify the failure per §4C rollback (Resumable vs
    /// QuarantineRequired); <paramref name="Diagnostic"/> is the operator-
    /// facing message.
    /// </summary>
    /// <param name="FailureKind">Classified failure mode (see <see cref="DataverseEnvCreationFailureKind"/>).</param>
    /// <param name="Diagnostic">Operator-facing message (e.g. "pac exit 3: rate limited — try again in 1 hour").</param>
    public sealed record Failure(
        DataverseEnvCreationFailureKind FailureKind,
        string Diagnostic) : DataverseEnvCreationOutcome;
}

/// <summary>
/// Classified failure kinds surfaced by <see cref="IDataverseEnvCreator"/>.
/// The handler maps each to a <see cref="DataverseEnvCreationRejectionCodes"/>
/// value + §4C <see cref="FailureClass"/> classification.
/// </summary>
public enum DataverseEnvCreationFailureKind
{
    /// <summary>
    /// pac CLI reported an authentication / authorization failure. Handler
    /// maps to <see cref="DataverseEnvCreationRejectionCodes.PacAuthFailure"/>
    /// (§4C Resumable — operator refreshes creds).
    /// </summary>
    AuthFailure = 1,

    /// <summary>
    /// Tenant hit Dataverse env-creation rate limit. Handler maps to
    /// <see cref="DataverseEnvCreationRejectionCodes.RateLimited"/>
    /// (§4C Resumable — operator waits for rate window).
    /// </summary>
    RateLimited = 2,

    /// <summary>
    /// Tenant / capacity quota exhausted. Handler maps to
    /// <see cref="DataverseEnvCreationRejectionCodes.QuotaExhausted"/>
    /// (§4C Resumable — operator escalates for quota bump).
    /// </summary>
    QuotaExhausted = 3,

    /// <summary>
    /// pac CLI reported partial provisioning (env row exists but is
    /// inconsistent). Handler maps to
    /// <see cref="DataverseEnvCreationRejectionCodes.PartialProvisioning"/>
    /// (§4C QuarantineRequired — orphaned resource may exist).
    /// </summary>
    PartialProvisioning = 4,

    /// <summary>
    /// Env creation did not complete within the configured window. Handler
    /// maps to <see cref="DataverseEnvCreationRejectionCodes.EnvCreationTimeout"/>
    /// (§4C Resumable — operator inspects PPAC + resumes). Distinct from
    /// generic invocation failure so operator dashboards can branch.
    /// </summary>
    Timeout = 5,

    /// <summary>
    /// pac CLI exited non-zero without a classified subtype. Handler maps to
    /// <see cref="DataverseEnvCreationRejectionCodes.PacInvocationFailed"/>
    /// (§4C Resumable — operator inspects stderr).
    /// </summary>
    UnknownInvocationFailure = 6,
}
