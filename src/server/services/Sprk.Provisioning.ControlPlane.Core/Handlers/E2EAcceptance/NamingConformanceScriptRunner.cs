// -----------------------------------------------------------------------------
// NamingConformanceScriptRunner.cs
//
// Production <see cref="INamingConformanceChecker"/> impl — shells out to
// pwsh + scripts/naming-conformance-check.ps1 (r3 task 063). The script's
// exit code IS the outcome: 0 = conformant, non-zero = at least one violation.
// The wrapper captures stdout/stderr to build the diagnostic message.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="INamingConformanceChecker"/>
public sealed class NamingConformanceScriptRunner : INamingConformanceChecker
{
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<NamingConformanceScriptRunner> _logger;

    public NamingConformanceScriptRunner(
        IOptions<H13AcceptanceOptions> options,
        ILogger<NamingConformanceScriptRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<NamingConformanceOutcome> CheckAsync(
        NamingConformanceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(_options.NamingConformanceScriptPath))
        {
            throw new FileNotFoundException(
                $"naming-conformance-check.ps1 not found at '{_options.NamingConformanceScriptPath}'. " +
                "Set E2EAcceptance:NamingConformanceScriptPath to the path relative to the App Service publish root.",
                _options.NamingConformanceScriptPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _options.PwshExecutable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(_options.NamingConformanceScriptPath);

        _logger.LogInformation(
            "H13 naming-conformance invoking: script={Script} customerId={CustomerId} runId={RunId}",
            _options.NamingConformanceScriptPath, request.CustomerId, request.RunId);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start pwsh executable '{_options.PwshExecutable}'.");
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.NamingConformanceTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException(
                $"naming-conformance-check.ps1 exceeded timeout {_options.NamingConformanceTimeout}.");
        }

        if (process.ExitCode == 0)
        {
            return new NamingConformanceOutcome.Success();
        }

        var diag = new StringBuilder();
        diag.Append("naming-conformance-check.ps1 exited ").Append(process.ExitCode).Append(" with violations. ");
        var trimmedStdout = stdout.ToString().Trim();
        if (trimmedStdout.Length > 0)
        {
            diag.Append("stdout: ").Append(trimmedStdout);
        }
        if (stderr.Length > 0)
        {
            diag.Append(" | stderr: ").Append(stderr.ToString().Trim());
        }
        return new NamingConformanceOutcome.Failure(process.ExitCode, diag.ToString());
    }
}
