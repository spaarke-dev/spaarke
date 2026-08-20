// -----------------------------------------------------------------------------
// IR3GateVerifier.cs
//
// RETIRED (task 132, Wave G-3, 2026-08-19): superseded by
// <see cref="IArtifactManifestVerifier"/> (DS-4 §5 re-scope — see
// DotnetR3GateVerifier.cs's retirement banner). No production or test code
// implements this interface going forward; retained on disk unregistered per
// this project's retirement convention.
// -----------------------------------------------------------------------------

// L2 abstraction over the r3-era pre-swap gate verification for H9. Production
// impl (<see cref="DotnetR3GateVerifier"/>) shells out to <c>dotnet build</c> +
// <c>dotnet test</c> + <c>naming-conformance-check.ps1</c>. Unit tests inject
// stubs that construct canned <see cref="R3GateVerificationResult"/> instances.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="DotnetR3GateVerifier"/> — invokes dotnet CLI +
//       pwsh with a per-gate exit-code interpretation, gracefully treating
//       gates whose artifacts have not yet landed (r1 tasks 064 / 067
//       pending) as <see cref="R3GateStatus.Skipped"/>.
//     - Test: stubs injected per unit test that construct
//       <see cref="R3GateVerificationResult"/> directly (see
//       <c>H9BffDeployHandlerTests</c>).
//   Interface earns its keep — no NIH.
//
// GATE-NOT-YET-AVAILABLE POSTURE (documented per POML mandatory pre-work #7):
//   Not all r3-era gate artifacts have landed in Wave 4 as this handler is
//   authored:
//     - GodClassGuardTests: LANDED (r3 task 040 — tests/Spaarke.ArchTests/GodClassGuardTests.cs).
//     - Analyzers-as-errors: LANDED (r3 task 041 — Directory.Build.props
//       inherits TreatWarningsAsErrors=true across the SUT + tests projects).
//     - 4 new ArchTests I1–I5: PENDING (r1 task 064 — not yet in the tree at
//       H9 authoring time).
//     - Naming-conformance script: PENDING (r3 task 063 — script may not be
//       present at all customer publish locations).
//     - Graph app-role parity ArchTest: PENDING (r3 task 062 / r1 task 067 —
//       nightly test queued behind CI-wiring per task 088).
//   The production verifier treats missing gates as <see cref="R3GateStatus.Skipped"/>
//   (logs prominently but does not fail the deploy). Once the pending gate
//   artifacts land, the verifier auto-picks them up without a code change —
//   the discovery step is filesystem-based, not compile-time-bound.
//
// DESIGN CHOICE (shell-out vs library):
//   Same trade-off as H2a's IBicepDeployRunner + H4's IKvSecretsWriter (see
//   their file headers): the pwsh / dotnet-cli-driven scripts + test projects
//   ARE the source-of-truth. Re-implementing in a native library duplicates
//   surface area. The verifier WRAPS the existing artifacts + adds the
//   distinct-code-per-failing-gate discipline the H9 handler needs.
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Verifies the five r3-era pre-swap gates for <see cref="H9BffDeployHandler"/>:
/// analyzers-as-errors + god-class ratchet + 4 new ArchTests + naming-
/// conformance + Graph app-role parity. Production impl shells out to
/// <c>dotnet</c> + <c>pwsh</c>; test impls return canned results.
/// </summary>
public interface IR3GateVerifier
{
    /// <summary>
    /// Runs all five r3-era gates in a deterministic order and returns the
    /// per-gate outcome. Domain failures (a gate is Failed) do NOT throw —
    /// the handler decides how to react. Infrastructure faults (dotnet not on
    /// PATH, script not found, timeout) MAY throw.
    /// </summary>
    /// <param name="request">Verifier inputs (customerId, repository root, cancellation).</param>
    /// <param name="cancellationToken">Cancellation token — long-running builds MUST honor it.</param>
    Task<R3GateVerificationResult> VerifyAsync(R3GateVerificationRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single r3-gate verification pass. Immutable record; the caller
/// (<see cref="H9BffDeployHandler"/>) constructs one per run.
/// </summary>
/// <param name="CustomerId">Customer id — flows into log lines and per-run drift-report file names.</param>
/// <param name="RunId">RunId — flows into log lines for cross-reference with Cosmos state.</param>
public sealed record R3GateVerificationRequest(string CustomerId, string RunId);

/// <summary>
/// Per-gate status. <see cref="Passed"/> and <see cref="Skipped"/> both allow
/// the deploy to proceed; only <see cref="Failed"/> blocks the slot-swap.
/// </summary>
public enum R3GateStatus
{
    /// <summary>Gate ran and reported no violation.</summary>
    Passed = 1,

    /// <summary>Gate artifact (test class, script) not yet present at this publish location — reported prominently but does NOT block the deploy (interim posture — see IR3GateVerifier file header).</summary>
    Skipped = 2,

    /// <summary>Gate ran and reported at least one violation — slot-swap BLOCKED.</summary>
    Failed = 3,
}

/// <summary>
/// The five r3-era gates enumerated (matches spec.md §5.5 verbatim, in the
/// order the verifier runs them).
/// </summary>
public enum R3GateKind
{
    /// <summary>r3 task 041 — analyzers-as-errors baseline (dotnet build with zero warnings).</summary>
    Analyzers = 1,

    /// <summary>r3 task 040 — god-class ratchet (tests/Spaarke.ArchTests/GodClassGuardTests.cs).</summary>
    GodClassRatchet = 2,

    /// <summary>r1 task 064 — 4 new tenant-isolation ArchTests (I1–I5).</summary>
    ArchTests = 3,

    /// <summary>r3 task 063 — canonical naming-conformance (scripts/naming-conformance-check.ps1).</summary>
    NamingConformance = 4,

    /// <summary>r3 task 062 / r1 task 067 — Graph app-role parity ArchTest (nightly, queued behind CI-wiring per task 088).</summary>
    GraphAppRoleParity = 5,
}

/// <summary>
/// One per-gate outcome. <see cref="Diagnostic"/> is populated for
/// <see cref="R3GateStatus.Failed"/> (operator-facing message) and
/// <see cref="R3GateStatus.Skipped"/> (why the gate could not run at this
/// publish location); empty for <see cref="R3GateStatus.Passed"/>.
/// </summary>
public sealed record R3GateOutcome(R3GateKind Kind, R3GateStatus Status, string Diagnostic);

/// <summary>
/// Aggregate verification result. The list contains exactly one entry per
/// <see cref="R3GateKind"/>, in the enum-declaration order. Convenience
/// helpers surface pass/fail state without pattern-matching every entry.
/// </summary>
/// <param name="Outcomes">One per-gate outcome, in enum-declaration order.</param>
public sealed record R3GateVerificationResult(IReadOnlyList<R3GateOutcome> Outcomes)
{
    /// <summary>True iff no gate returned <see cref="R3GateStatus.Failed"/> (skipped gates do NOT block).</summary>
    public bool AllGatesGreen => Outcomes.All(o => o.Status != R3GateStatus.Failed);

    /// <summary>
    /// The first failed gate (if any) — the handler uses this to decide the
    /// distinct rejection code + diagnostic per POML acceptance criterion 2.
    /// </summary>
    public R3GateOutcome? FirstFailure => Outcomes.FirstOrDefault(o => o.Status == R3GateStatus.Failed);

    /// <summary>Convenience helper for logs: comma-separated <c>{Kind}={Status}</c> per gate.</summary>
    public string ToLogSummary()
        => string.Join(", ", Outcomes.Select(o => $"{o.Kind}={o.Status}"));
}
