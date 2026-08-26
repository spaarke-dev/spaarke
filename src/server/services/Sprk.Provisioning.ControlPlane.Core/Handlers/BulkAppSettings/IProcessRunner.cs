// -----------------------------------------------------------------------------
// IProcessRunner.cs
//
// Task 201 — narrow seam over `System.Diagnostics.Process` so H4b's
// pwsh-invocation is fake-substitutable in unit tests. New to this project
// (grep verified: no pre-existing IProcessRunner in .Core or .Worker as of
// 2026-08-24 recon).
//
// CONTRACT:
//   Execute a single one-shot child process with an argument list + timeout.
//   Captures stdout + stderr in full. Returns exit code + captured streams.
//   Does NOT throw on non-zero exit — that's a domain outcome. Throws ONLY on
//   infrastructure faults (process failed to start, timeout exceeded).
//
// ADR-028 discipline: callers MUST NOT log ProcessResult.Stdout / .Stderr
// verbatim if the child process could have printed secret material. H4b uses
// PwshProcessRunner for the Configure-AppServiceSettings.generated.ps1
// invocation whose args carry per-env cleartext values — H4b redacts the
// stdout/stderr in log lines by only surfacing exit code + a bounded tail
// (see H4bBulkAppSettingsHandler.RedactProcessDiagnostic).
// -----------------------------------------------------------------------------

namespace Sprk.Provisioning.ControlPlane.Handlers.BulkAppSettings;

/// <summary>
/// Runs a one-shot child process with a bounded execution budget. Fake-
/// substitutable so H4b unit tests can control exit code, stdout, and stderr
/// without spawning a real pwsh.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Executes <paramref name="executable"/> with <paramref name="args"/>.
    /// Captures stdout + stderr in full; returns exit code + captured streams
    /// once the process exits OR the <paramref name="timeout"/> elapses.
    /// </summary>
    /// <param name="executable">Executable name or absolute path (e.g. "pwsh", "/usr/bin/pwsh").</param>
    /// <param name="args">Argument list (each element passed as a separate argv entry — no shell interpolation).</param>
    /// <param name="environment">Optional additional environment variables applied to the child process. Null = inherit only.</param>
    /// <param name="timeout">
    /// Hard upper bound for process execution. When exceeded, the process is
    /// killed and the method throws <see cref="TimeoutException"/>. Null =
    /// no timeout (dangerous; caller MUST supply a value in production).
    /// </param>
    /// <param name="cancellationToken">Cancellation token — cancellation kills the child and throws.</param>
    /// <returns>Exit code + captured stdout + captured stderr.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the process cannot be started (executable not found, access denied, etc.).</exception>
    /// <exception cref="TimeoutException">Thrown when <paramref name="timeout"/> elapses before process exit.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled.</exception>
    Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan? timeout,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of a one-shot child process invocation.
/// </summary>
/// <param name="ExitCode">The process's numeric exit code (0 = success by convention).</param>
/// <param name="Stdout">The full stdout capture (may be empty).</param>
/// <param name="Stderr">The full stderr capture (may be empty).</param>
public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
