// -----------------------------------------------------------------------------
// PwshProcessRunner.cs
//
// Task 201 — production impl of IProcessRunner using System.Diagnostics.Process
// with async stdio capture + timeout. Purpose-narrow (single H4b consumer today);
// name reflects the ONLY caller's shape but the class itself does not hard-code
// pwsh — the executable is passed by the caller.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <inheritdoc cref="IProcessRunner"/>
public sealed class PwshProcessRunner : IProcessRunner
{
    private readonly ILogger<PwshProcessRunner> _logger;

    /// <summary>Constructs the process runner.</summary>
    public PwshProcessRunner(ILogger<PwshProcessRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(args);

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a ?? string.Empty);
        }
        if (environment is not null)
        {
            foreach (var kv in environment)
            {
                psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutLock = new object();
        var stderrLock = new object();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stdoutLock) { stdoutBuilder.AppendLine(e.Data); }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (stderrLock) { stderrBuilder.AppendLine(e.Data); }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"PwshProcessRunner: Process.Start returned false for executable '{executable}'.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"PwshProcessRunner: failed to start '{executable}': {ex.GetType().Name}: {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = timeout.HasValue
            ? new CancellationTokenSource(timeout.Value)
            : new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            // Ensure stream drain — WaitForExitAsync returns as soon as the OS
            // sees the process exit; there can be a small delay before the
            // event-based stdout/stderr callbacks flush.
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            throw new TimeoutException(
                $"PwshProcessRunner: '{executable}' did not exit within {timeout?.TotalSeconds ?? -1} seconds.");
        }

        string stdout, stderr;
        lock (stdoutLock) { stdout = stdoutBuilder.ToString(); }
        lock (stderrLock) { stderr = stderrBuilder.ToString(); }

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }

    private void TryKill(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PwshProcessRunner: failed to kill runaway process.");
        }
    }
}
