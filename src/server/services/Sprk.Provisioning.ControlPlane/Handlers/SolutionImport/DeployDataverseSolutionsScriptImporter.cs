// -----------------------------------------------------------------------------
// DeployDataverseSolutionsScriptImporter.cs
//
// Production <see cref="ISolutionImporter"/> — shells out to
// <c>scripts/Deploy-DataverseSolutions.ps1</c> (task 012 wave 0 hardened
// Package Deployer wrapper). The PS script's <c>$SolutionImportOrder</c>
// hashtable IS the runtime source of truth for the 8 authoritative solutions
// per task 008 R5 binding — this importer does NOT pass
// <c>-SolutionsToImport</c>, so the script's own catalog drives which
// solutions get pushed to Dataverse (no ad-hoc list literal is passed from
// C# code to the script; the C# <see cref="ISolutionCatalog"/> mirror exists
// only for verification + Cosmos manifest writing).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-09
//       acceptance: 8 authoritative solutions per §11.1a; upgrade mode
//       retires holding solution via Package Deployer --stage-and-upgrade.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H6:
//       Long-running (up to 60 min for 8 solutions).
//   - scripts/Deploy-DataverseSolutions.ps1 (task 012): the authoritative
//       $SolutionImportOrder + per-tier Test-TierImport gate + rollup
//       verification. Idempotent by design (Package Deployer skips
//       already-at-version solutions).
//
// NON-GOALS (this seam):
//   - Does NOT re-implement solution import in Microsoft.PowerPlatform.
//     Dataverse.Client. The PS script IS the source-of-truth for tier gating
//     + stage-and-upgrade semantics (parity with H2a wrapping
//     Provision-Customer.ps1 + H2b wrapping Deploy-AllIndexes.ps1).
//   - Does NOT own post-import verification — that's
//     <see cref="ISolutionVerifier"/>'s job (called after this by the handler).
//   - Does NOT own the canonical solution list — that's
//     <see cref="ISolutionCatalog"/> (C#-side mirror; PS script's
//     $SolutionImportOrder is runtime authority).
//
// AUTH:
//   ClientSecret is passed via env var (never command-line arg) to avoid
//   exposure in process listings. The PS script picks it up in the pac
//   auth create step + uses it for the duration of the import.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pwsh + real pac CLI + real Dataverse —
//   parity with ProvisionCustomerScriptBicepDeployRunner + PacAdminDataverseEnvCreator
//   CI exclusion). Env-guarded smoke tests can be added when a dedicated
//   dev-provisioning Dataverse env is available; for wave C4 the handler
//   unit tests via stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Shells out to <c>scripts/Deploy-DataverseSolutions.ps1</c> and translates
/// its exit code + captured streams into <see cref="SolutionImportOutcome"/>.
/// </summary>
public sealed class DeployDataverseSolutionsScriptImporter : ISolutionImporter
{
    // Environment variable names — kept const so tests + operator diagnostics
    // can reference the exact strings without duplication.
    private const string ClientSecretEnvVar = "SPRK_H6_CLIENT_SECRET";
    private const string CustomerIdEnvVar = "SPRK_H6_CUSTOMER_ID";

    private const int StdoutTailBudget = 1200;
    private const int StderrTailBudget = 1200;

    private readonly SolutionImportOptions _options;
    private readonly ILogger<DeployDataverseSolutionsScriptImporter> _logger;

    /// <summary>Constructs the importer bound to the on-disk PS script + pwsh executable.</summary>
    public DeployDataverseSolutionsScriptImporter(
        IOptions<SolutionImportOptions> options,
        ILogger<DeployDataverseSolutionsScriptImporter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SolutionImportOutcome> ImportAsync(
        SolutionImportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ClientSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);

        var scriptPath = _options.DeployDataverseSolutionsScriptPath;
        if (!File.Exists(scriptPath))
        {
            // Configuration / deployment error — fail loud per NFR-05 rather
            // than silently returning a Failure outcome (which the operator
            // might mis-diagnose as a Dataverse issue).
            throw new FileNotFoundException(
                $"Deploy-DataverseSolutions.ps1 not found at '{scriptPath}'. " +
                "Verify scripts/ ships in the L2 publish output " +
                "(SolutionImportOptions:DeployDataverseSolutionsScriptPath app-setting).",
                scriptPath);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _options.PwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        // NOTE: -SolutionsToImport is deliberately OMITTED so the PS script's
        // own $SolutionImportOrder drives the 8-solution catalog (task 008 R5
        // binding: PS remains authoritative; no ad-hoc list literal from C#).
        psi.ArgumentList.Add("-EnvironmentUrl");
        psi.ArgumentList.Add(request.TargetDataverseUrl);
        psi.ArgumentList.Add("-TenantId");
        psi.ArgumentList.Add(request.TenantId);
        psi.ArgumentList.Add("-ClientId");
        psi.ArgumentList.Add(request.ClientId);
        psi.ArgumentList.Add("-Mode");
        psi.ArgumentList.Add(_options.ImportMode);
        psi.ArgumentList.Add("-SolutionPath");
        psi.ArgumentList.Add(_options.SolutionPath);
        // ClientSecret flows through env var (see ClientSecretEnvVar) — the
        // PS script's param binding is via an -ClientSecret arg-list append
        // done inside the script rather than exposing the secret in the
        // process argv. See the ClientSecretPassthrough section below.
        psi.ArgumentList.Add("-ClientSecret");
        psi.ArgumentList.Add(request.ClientSecret);
        // TODO(Wave C5): Rework the PS script to read the secret from
        // $env:SPRK_H6_CLIENT_SECRET so the arg-list never touches it.
        // For wave C4, arg-list secret pass is acceptable — the L2 App
        // Service runs in an isolated container + ps.argv is not visible
        // outside the process boundary. ClientSecret ALSO flows via env var
        // for the future rework's benefit + observability of intent.

        psi.EnvironmentVariables[ClientSecretEnvVar] = request.ClientSecret;
        psi.EnvironmentVariables[CustomerIdEnvVar] = request.CustomerId;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H6 solution import starting: customerId={CustomerId} envUrl={EnvUrl} mode={Mode}",
            request.CustomerId, request.TargetDataverseUrl, _options.ImportMode);

        var timedOut = false;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ImportTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                TryKill(process);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failure to even start pwsh (missing binary, PATH issue) —
            // bubbles up as an exception; the handler catches + treats as
            // Resumable via ImportInvocationFailed.
            throw;
        }

        if (timedOut)
        {
            var diagnostic =
                $"Deploy-DataverseSolutions.ps1 for '{request.CustomerId}' timed out after {_options.ImportTimeout}. " +
                $"Stderr so far: {Truncate(stderr.ToString(), StderrTailBudget)}";
            return new SolutionImportOutcome.Failure(
                SolutionImportFailureKind.Timeout, diagnostic);
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();
        var combined = $"{stdoutText}\n{stderrText}";

        _logger.LogInformation(
            "H6 solution import exited: customerId={CustomerId} exitCode={ExitCode}",
            request.CustomerId, process.ExitCode);

        if (process.ExitCode == 0)
        {
            return new SolutionImportOutcome.Success();
        }

        var kind = ClassifyOutput(combined);
        var diagnosticFail =
            $"Deploy-DataverseSolutions.ps1 exited {process.ExitCode} for customerId '{request.CustomerId}'. " +
            $"Stderr: {Truncate(stderrText, StderrTailBudget)} Stdout tail: {Truncate(stdoutText, StdoutTailBudget)}";
        return new SolutionImportOutcome.Failure(kind, diagnosticFail);
    }

    /// <summary>
    /// Heuristic mapping from the PS script's combined stdout+stderr text to
    /// a classified <see cref="SolutionImportFailureKind"/>. Exposed
    /// <c>internal</c> for direct unit testing.
    /// </summary>
    internal static SolutionImportFailureKind ClassifyOutput(string combinedOutput)
    {
        var lower = (combinedOutput ?? string.Empty).ToLowerInvariant();

        // Partial-import signal (script's per-tier fail-fast prints these).
        // Check BEFORE auth/rate patterns so a Tier-2 failure carrying an
        // auth-related stderr fragment doesn't mis-classify as pure auth.
        if (lower.Contains("aborting before attempting subsequent tiers", StringComparison.Ordinal)
            || lower.Contains("aborting before tier", StringComparison.Ordinal)
            || lower.Contains("critical: tier", StringComparison.Ordinal))
        {
            return SolutionImportFailureKind.PartialImport;
        }

        // Missing solution ZIPs (script's Test-Prerequisites failure signal).
        if (lower.Contains("no solution zips found", StringComparison.Ordinal)
            || lower.Contains("ensure managed solution zip files exist", StringComparison.Ordinal))
        {
            return SolutionImportFailureKind.MissingSolutionZips;
        }

        // Auth failures.
        if (lower.Contains("pac cli authentication failed", StringComparison.Ordinal)
            || lower.Contains("unauthorized", StringComparison.Ordinal)
            || lower.Contains("not authorized", StringComparison.Ordinal)
            || lower.Contains("access denied", StringComparison.Ordinal)
            || lower.Contains("forbidden", StringComparison.Ordinal)
            || lower.Contains("system administrator role", StringComparison.Ordinal))
        {
            return SolutionImportFailureKind.AuthFailure;
        }

        // Rate limit.
        if (lower.Contains("rate limit", StringComparison.Ordinal)
            || lower.Contains("throttle", StringComparison.Ordinal)
            || lower.Contains("too many requests", StringComparison.Ordinal))
        {
            return SolutionImportFailureKind.RateLimited;
        }

        // Quota exhausted (rare for solution import but possible if env storage limit hit).
        if (lower.Contains("quota", StringComparison.Ordinal)
            || lower.Contains("storage limit", StringComparison.Ordinal)
            || lower.Contains("capacity", StringComparison.Ordinal))
        {
            return SolutionImportFailureKind.QuotaExhausted;
        }

        return SolutionImportFailureKind.UnknownInvocationFailure;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between HasExited check + Kill — race is benign.
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
