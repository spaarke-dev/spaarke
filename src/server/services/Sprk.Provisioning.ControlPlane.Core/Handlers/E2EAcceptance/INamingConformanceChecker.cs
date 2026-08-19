// -----------------------------------------------------------------------------
// INamingConformanceChecker.cs
//
// L2 abstraction over the INDEPENDENT invocation of scripts/naming-conformance-
// check.ps1 (r3 task 063). This is a SEPARATE invocation from the wrapped call
// inside Validate-DeployedEnvironment.ps1 — H13 owns SC #17 ("exit 0 on
// r1-owned surfaces post-provisioning") as its own explicit pass/fail boundary
// so that failure in either surface fails H13 with a distinct rejection code.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 impls (production script runner + test fakes per unit test). The
//   naming-conformance script is the r3-owned canonical enforcement + this
//   handler MUST NOT modify it (per POML mandatory pre-work #9 + spec §7.9
//   naming remediation is r1-owned).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Invokes <c>scripts/naming-conformance-check.ps1</c> independently of the
/// wrapped invocation inside Validate-DeployedEnvironment.ps1. Production impl
/// shells out to pwsh; test impls return canned results.
/// </summary>
public interface INamingConformanceChecker
{
    /// <summary>
    /// Runs the naming-conformance check + returns a typed outcome. Non-zero
    /// exit is a domain failure (returned in <see cref="NamingConformanceOutcome.Failure"/>);
    /// pwsh-launch-level exceptions are infra faults (thrown so the handler
    /// classifies Resumable).
    /// </summary>
    Task<NamingConformanceOutcome> CheckAsync(NamingConformanceRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single naming-conformance check. Immutable record.
/// </summary>
/// <param name="CustomerId">Customer id — flows into log lines.</param>
/// <param name="RunId">RunId — for log correlation.</param>
public sealed record NamingConformanceRequest(string CustomerId, string RunId);

/// <summary>
/// Typed outcome of a naming-conformance check. Discriminated union:
/// <see cref="Success"/> when the script exited 0 (no violations); <see cref="Failure"/>
/// when it exited non-zero.
/// </summary>
public abstract record NamingConformanceOutcome
{
    private NamingConformanceOutcome() { }

    /// <summary>Script exited 0 — no naming violations found on r1-owned surfaces.</summary>
    public sealed record Success() : NamingConformanceOutcome;

    /// <summary>Script exited non-zero — at least one violation.</summary>
    /// <param name="ExitCode">The script's non-zero exit code.</param>
    /// <param name="Diagnostic">Operator-facing diagnostic aggregating the violations.</param>
    public sealed record Failure(int ExitCode, string Diagnostic) : NamingConformanceOutcome;
}
