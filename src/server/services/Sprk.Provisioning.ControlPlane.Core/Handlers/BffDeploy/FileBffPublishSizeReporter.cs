// -----------------------------------------------------------------------------
// FileBffPublishSizeReporter.cs
//
// Production <see cref="IBffPublishSizeReporter"/> implementation. Reads the
// compressed BFF publish zip's file size, computes signed delta vs baseline,
// and returns a report with per-threshold flags the handler consumes to
// decide whether to reject the deploy per NFR-01.
//
// NFR-01 RATIONALE:
//   spec.md NFR-01 + CLAUDE.md §10 bullet 4: every BFF-touching task MUST
//   report absolute + delta compressed publish size. Baseline 44.96 MB
//   (2026-08-13); ≥+5 MB single-task delta → explicit justification; ≥60 MB
//   HARD STOP. This reporter emits the metrics; the handler enforces the
//   thresholds.
//
// CI COVERAGE:
//   Filesystem-only; can be tested in-process via a temp-file fixture.
//   Handler unit tests via IBffPublishSizeReporter stubs are the primary
//   coverage; this impl is trivial enough to skip a dedicated unit-test
//   file.
// -----------------------------------------------------------------------------

using System.Globalization;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Reads a publish zip's file size, computes NFR-01 delta + threshold flags.
/// </summary>
public sealed class FileBffPublishSizeReporter : IBffPublishSizeReporter
{
    private readonly ILogger<FileBffPublishSizeReporter> _logger;

    public FileBffPublishSizeReporter(ILogger<FileBffPublishSizeReporter> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<PublishSizeReport> MeasureAsync(
        PublishSizeReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PublishZipPath);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(request.PublishZipPath))
        {
            throw new FileNotFoundException(
                $"BFF publish zip not found at '{request.PublishZipPath}' — Deploy-BffApi.ps1 did not " +
                "produce the compressed artifact this reporter measures. Verify the deploy runner's " +
                "output layout matches BffDeployOptions.BffPublishZipPath.",
                request.PublishZipPath);
        }

        var absoluteBytes = new FileInfo(request.PublishZipPath).Length;
        var deltaBytes = absoluteBytes - request.BaselineBytes;
        var exceedsDelta = deltaBytes > request.DeltaCeilingBytes;
        var exceedsAbsolute = absoluteBytes > request.AbsoluteCeilingBytes;

        var summary = FormatSummary(request.CustomerId, absoluteBytes, deltaBytes, request.BaselineBytes, exceedsDelta, exceedsAbsolute);

        _logger.LogInformation(
            "H9 publish-size report: customerId={CustomerId} absoluteBytes={Absolute} deltaBytes={Delta} " +
            "exceedsDelta={ExceedsDelta} exceedsAbsolute={ExceedsAbsolute}",
            request.CustomerId, absoluteBytes, deltaBytes, exceedsDelta, exceedsAbsolute);

        return Task.FromResult(new PublishSizeReport(
            absoluteBytes,
            deltaBytes,
            exceedsDelta,
            exceedsAbsolute,
            summary));
    }

    private static string FormatSummary(
        string customerId,
        long absoluteBytes,
        long deltaBytes,
        long baselineBytes,
        bool exceedsDelta,
        bool exceedsAbsolute)
    {
        var absoluteMb = absoluteBytes / (double)(1024 * 1024);
        var deltaMb = deltaBytes / (double)(1024 * 1024);
        var baselineMb = baselineBytes / (double)(1024 * 1024);
        var sign = deltaBytes >= 0 ? "+" : "";
        var flags = new List<string>();
        if (exceedsAbsolute) flags.Add("ABSOLUTE-CEILING-EXCEEDED");
        if (exceedsDelta) flags.Add("DELTA-THRESHOLD-EXCEEDED");
        var flagText = flags.Count == 0 ? "OK" : string.Join(" ", flags);

        return string.Format(
            CultureInfo.InvariantCulture,
            "H9 NFR-01 publish size ({0}): {1:F2} MB (delta {2}{3:F2} MB vs baseline {4:F2} MB) — {5}",
            customerId, absoluteMb, sign, deltaMb, baselineMb, flagText);
    }
}
