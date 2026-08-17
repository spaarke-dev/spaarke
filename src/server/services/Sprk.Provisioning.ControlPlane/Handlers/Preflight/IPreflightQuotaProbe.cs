// -----------------------------------------------------------------------------
// IPreflightQuotaProbe.cs
//
// Abstraction over one of the four H0 preflight quota checks. One probe
// implementation per underlying data source; H0PreflightHandler injects an
// IEnumerable<IPreflightQuotaProbe> and orchestrates them in parallel.
//
// DESIGN CHOICE (shell-out vs C# reimpl):
//   The concrete production implementation (<see cref="PowerShellPreflightProbe"/>)
//   shells out to scripts/preflight/*.ps1 (task 016). This reuses the tested
//   scripts + Az CLI's built-in auth chain rather than re-implementing four
//   Azure REST paths in C#. Trade-off accepted:
//
//     Pros — single source of truth (spec.md FR-01 + Task 016 own the
//            quota-query logic; H0 handler owns orchestration only), scripts
//            already documented + testable in isolation.
//     Cons — pwsh must exist on the L2 App Service host (linux-x64 publish)
//            + process-spawn overhead per invocation. Both are acceptable
//            for a check that runs once per provisioning start.
//
//   Unit tests do NOT exercise <see cref="PowerShellPreflightProbe"/> —
//   they inject their own <see cref="IPreflightQuotaProbe"/> stubs to
//   avoid a pwsh runtime dependency in the CI unit suite (ADR-038 path #1:
//   pure C# unit test — no external processes).
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1: the production shell-out probe
//   AND the test-only stub probe defined per-test. That satisfies the
//   "genuine seam" bar in ADR-010 for keeping <see cref="IPreflightQuotaProbe"/>
//   as an interface rather than a concrete class. Also unlocks the
//   future path where operators can opt into a pure-C# probe implementation
//   without a runtime pwsh dependency (Q-F candidate follow-up).
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// Executes ONE of the four H0 preflight quota / readiness checks against
/// the underlying Azure / PAC / KV data source. Returns a typed
/// <see cref="PreflightCheckResult"/>; never throws on the domain-check
/// path — a failed check IS a Pass=false result, not an exception.
/// </summary>
public interface IPreflightQuotaProbe
{
    /// <summary>
    /// Stable check identifier — matches <see cref="PreflightCheckNames"/>
    /// constants + the underlying PS script basename. Used by
    /// <c>H0PreflightHandler</c> to build the machine-stable rejection code
    /// on failure and to log per-check outcomes.
    /// </summary>
    string CheckName { get; }

    /// <summary>
    /// Executes the check with the given <paramref name="input"/> and
    /// returns the result. Domain failures (insufficient headroom, missing
    /// cert, etc.) return <c>Passed=false</c> WITH a diagnostic; only
    /// unexpected infrastructure errors (e.g. pwsh not on PATH, script
    /// parse error, network fault) should throw.
    /// </summary>
    /// <param name="input">Customer + tenant + non-secret parameter snapshot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PreflightCheckResult> CheckAsync(
        PreflightProbeInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// Read-only per-check input snapshot. Assembled by
/// <c>H0PreflightHandler</c> from the ProvisioningRun's parameters +
/// customer identity — probes MUST NOT read from Cosmos or other stores
/// themselves (single source of truth: the handler-supplied input).
/// </summary>
/// <param name="CustomerId">Customer partition-key value.</param>
/// <param name="TenantId">
/// Entra tenant id — resolved from <c>ProvisioningRun.Parameters.NonSecret["tenantId"]</c>
/// by the handler. The handler enforces the §4D I1 no-hardcoded-tenant
/// rule (missing → HandlerResult.Failure BEFORE probes fire); probes may
/// assume <see cref="TenantId"/> is non-empty.
/// </param>
/// <param name="NonSecretParameters">
/// The run's non-secret parameter map (region, subscriptionId, KV name,
/// per-model TPM, per-family vCPU, etc.). Probes read only the keys they
/// need + fail with a specific diagnostic on any missing parameter they
/// require.
/// </param>
public sealed record PreflightProbeInput(
    string CustomerId,
    string TenantId,
    IReadOnlyDictionary<string, string> NonSecretParameters);
