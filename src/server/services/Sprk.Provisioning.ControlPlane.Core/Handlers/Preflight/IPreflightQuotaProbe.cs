// -----------------------------------------------------------------------------
// IPreflightQuotaProbe.cs
//
// Abstraction over one of the four H0 preflight quota checks. One probe
// implementation per underlying data source; H0PreflightHandler injects an
// IEnumerable<IPreflightQuotaProbe> and orchestrates them in parallel.
//
// DESIGN CHOICE (SDK/REST, not shell-out — task 120, Wave G-2):
//   The four concrete production implementations —
//   <see cref="ArmCognitiveServicesTpmProbe"/>,
//   <see cref="BapRestEnvironmentRateProbe"/>,
//   <see cref="ArmComputeVCpuProbe"/>, and
//   <see cref="KeyVaultCertBootstrapProbe"/> — are pure .NET SDK/REST calls
//   under <c>DefaultAzureCredential</c> pinned to the L2 UAMI (Option D
//   hybrid per DS-1b §1 H0 row). They REPLACE the original shell-out
//   implementation (<c>PowerShellPreflightProbe</c>, which invoked
//   scripts/preflight/*.ps1 — retired by task 120; the L2 App Service has no
//   pwsh runtime under Option D's zero-shell main site per design.md §4.2a).
//   Each ported probe's threshold-comparison logic is ported verbatim from
//   its source script's own comparison block (see each probe's file header).
//
//   Unit tests do NOT exercise the SDK/REST call itself — each probe wraps
//   its Azure SDK/REST call behind a thin internal module-boundary reader
//   seam (e.g. <c>ICognitiveServicesUsageReader</c>) so unit tests inject
//   canned data directly, mirroring each source PS script's own test-mode
//   escape hatch (ADR-038 path #1: pure C# unit test — no external calls).
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1: the four production SDK/REST probes
//   AND the test-only stub probe defined per-test (see H0PreflightHandlerTests.cs's
//   FakeProbe). That satisfies the "genuine seam" bar in ADR-010 for keeping
//   <see cref="IPreflightQuotaProbe"/> as an interface rather than a concrete
//   class.
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
