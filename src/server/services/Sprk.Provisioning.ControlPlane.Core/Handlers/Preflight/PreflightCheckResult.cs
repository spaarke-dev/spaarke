// -----------------------------------------------------------------------------
// PreflightCheckResult.cs
//
// Return contract for a single <see cref="IPreflightQuotaProbe"/> invocation.
// MIRRORS the four-field PSCustomObject returned by every script under
// scripts/preflight/*.ps1 (task 016) — see scripts/preflight/README.md §
// Shared Return Contract:
//
//     Result     'Pass' | 'Fail'
//     CheckName  matches script basename (also in <see cref="PreflightCheckNames"/>)
//     Headroom   hashtable of { observed, required, region, ... }
//     Diagnostic 'Human-readable message' — on Fail MUST cite BOTH observed
//                headroom AND requested capacity + region.
//
// Keeping the .NET DTO shape parallel to the PS return means a probe that
// shells out to `pwsh -File ...` can deserialize the PS output into this
// record without a translation layer.
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <summary>
/// Result of one <see cref="IPreflightQuotaProbe"/> invocation. Mirrors the
/// PSCustomObject shape emitted by scripts/preflight/*.ps1 verbatim so a
/// PowerShell-shell-out probe implementation can deserialize the script's
/// output without translation.
/// </summary>
/// <param name="CheckName">
/// Stable check identifier — matches <see cref="PreflightCheckNames"/>
/// constants + the underlying PS script basename.
/// </param>
/// <param name="Passed">
/// <c>true</c> when the check succeeded (Result = "Pass" in the PS contract);
/// <c>false</c> on any failure.
/// </param>
/// <param name="Headroom">
/// Observed / required / region + per-check-specific fields. Kept as a
/// <see cref="JsonElement"/> so the probe can capture arbitrary
/// per-check-specific properties (per-model breakdown, per-SKU-family
/// breakdown, cert age hours, etc.) without a new POCO per check.
/// </param>
/// <param name="Diagnostic">
/// Human-readable diagnostic. On failure MUST cite BOTH observed and
/// requested capacity + region so the operator can file a quota-bump
/// request or resolve the missing precondition without further probing.
/// </param>
public sealed record PreflightCheckResult(
    string CheckName,
    bool Passed,
    JsonElement Headroom,
    string Diagnostic);
