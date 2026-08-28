// -----------------------------------------------------------------------------
// IBffPublishSizeReporter.cs
//
// L2 abstraction over the BFF publish-artifact-size measurement + NFR-01
// delta reporting. The production impl (<see cref="FileBffPublishSizeReporter"/>)
// reads the compressed publish zip's file size and compares to the configured
// baseline. Unit tests inject stubs to avoid filesystem access.
//
// SEAM JUSTIFICATION (ADR-010):
//   ≥2 implementations exist from day 1:
//     - Production: <see cref="FileBffPublishSizeReporter"/> reads the zip
//       file size + writes a per-run report to run notes.
//     - Test: stubs injected per unit test that construct
//       <see cref="PublishSizeReport"/> directly (see H9BffDeployHandlerTests).
//   Interface earns its keep — no NIH.
//
// NFR-01 RATIONALE:
//   spec.md NFR-01 + CLAUDE.md §10 bullet 4 (BFF Hygiene): every BFF-touching
//   task MUST report absolute + delta compressed publish size. Baseline
//   44.96 MB (2026-08-13); ≥+5 MB single-task delta → explicit justification;
//   ≥55 MB cumulative → architecture review; ≥60 MB → HARD STOP. The reporter
//   RETURNS the metric + a threshold-tripped flag; the H9 handler decides
//   whether to REJECT the deploy (per POML acceptance criterion 4).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Measures the compressed BFF publish artifact size and produces the NFR-01
/// delta report. Production impl reads the zip file; test impls return canned
/// results.
/// </summary>
public interface IBffPublishSizeReporter
{
    /// <summary>
    /// Reads the compressed publish artifact size + compares to the baseline.
    /// Returns a report with absolute size + delta + threshold flags. Does
    /// NOT itself decide whether to reject the deploy — that's the handler's
    /// call. Infrastructure faults (zip missing, IO error) MAY throw.
    /// </summary>
    Task<PublishSizeReport> MeasureAsync(PublishSizeReportRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Inputs to a single publish-size measurement.
/// </summary>
/// <param name="CustomerId">Customer id — flows into log lines + per-run report file names.</param>
/// <param name="PublishZipPath">Absolute path to the compressed publish zip Deploy-BffApi.ps1 produced.</param>
/// <param name="BaselineBytes">Baseline compressed publish size in bytes for delta computation.</param>
/// <param name="DeltaCeilingBytes">Delta ceiling in bytes above which the report flags an escalation-required condition.</param>
/// <param name="AbsoluteCeilingBytes">Absolute ceiling in bytes above which the report flags a HARD-STOP condition.</param>
public sealed record PublishSizeReportRequest(
    string CustomerId,
    string PublishZipPath,
    long BaselineBytes,
    long DeltaCeilingBytes,
    long AbsoluteCeilingBytes);

/// <summary>
/// NFR-01 publish-size report. Handler uses this to decide whether to REJECT
/// the deploy (per POML acceptance criterion 4).
/// </summary>
/// <param name="AbsoluteBytes">Measured compressed publish size in bytes.</param>
/// <param name="DeltaBytes">Signed delta vs baseline in bytes (positive = grew, negative = shrank).</param>
/// <param name="ExceedsDeltaThreshold">True when the delta exceeds the configured ceiling (H9 REJECTS).</param>
/// <param name="ExceedsAbsoluteCeiling">True when the absolute size exceeds the configured ceiling (H9 REJECTS — HARD STOP per NFR-01).</param>
/// <param name="Summary">One-line operator-facing summary suitable for PR descriptions + log lines.</param>
public sealed record PublishSizeReport(
    long AbsoluteBytes,
    long DeltaBytes,
    bool ExceedsDeltaThreshold,
    bool ExceedsAbsoluteCeiling,
    string Summary);
