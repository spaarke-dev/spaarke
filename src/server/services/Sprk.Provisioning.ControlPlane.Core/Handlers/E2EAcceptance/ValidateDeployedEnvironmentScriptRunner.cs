// -----------------------------------------------------------------------------
// ValidateDeployedEnvironmentScriptRunner.cs
//
// RETIRED (task 181, Phase C'' Wave G-7 Batch G-7B, 2026-08-20): superseded by
// <see cref="E2EValidationRunner"/> -- a pure-C# port of scripts/Validate-
// DeployedEnvironment.ps1's actually-shipped check surface (BFF /healthz,
// /ping, CORS preflight) with ZERO ProcessStartInfo / pwsh dependency. Per
// DS-4 section 6 this retires the last H13 shell-out so H13 executes cleanly
// under Option D's zero-shell L2 Worker host without a pwsh prerequisite in
// the App Service publish layout. The .ps1 script itself remains UNCHANGED on
// disk as the operator-invokable off-cluster diagnostic tool (the pure-C#
// port is the L2 runtime path; the .ps1 remains the operator/CI path).
//
// Retained on disk (NOT deleted); mirrors NamingConformanceScriptRunner.cs's
// retirement banner. No longer registered in E2EAcceptanceModule.cs's
// IE2EValidationRunner DI slot (see the task-181 DI-swap comment there).
//
// SILENT-FAIL AUDIT NOTE (why the port did NOT try to be 1:1 with the .ps1's
// Dataverse-env-var + dev-leakage checks): the .ps1 authenticates against
// Dataverse via the operator's ambient `pac auth create` session; L2 has no
// equivalent Dataverse identity on the H13 envelope (H10 provisions the
// BFF's MI Dataverse App User, not L2's). Rather than plumb a second
// Dataverse identity through H13 for a partial-port, the C# runner surfaces
// those checks in the ChecksSkipped list -- an operator grepping the H13
// outcome sees "dataverse-env-vars-present" in Skipped rather than mistaking
// silence for verification. Phase F rerun (task 186) closes the extended set.
//
// (ORIGINAL SUMMARY -- for historical context)
// Production <see cref="IE2EValidationRunner"/> impl -- shells out to pwsh +
// scripts/Validate-DeployedEnvironment.ps1 (Phase B extended). Parses the
// script's stdout for check-by-check outcomes (the script's summary section
// emits `PASS: <check>` / `FAIL: <check>` / `SKIP: <check>` lines the wrapper
// scans).
//
// SHELL-OUT PATTERN PARITY:
//   Mirrored Handlers/IntegrationWiring/ExchangePolicyScriptApplier.cs and
//   Handlers/BffDeploy/DeployBffApiScriptRunner.cs (Process.Start + async
//   stdout/stderr capture + timeout enforcement + non-zero-exit inspection).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="IE2EValidationRunner"/>
public sealed class ValidateDeployedEnvironmentScriptRunner : IE2EValidationRunner
{
    private static readonly Regex PassLineRegex = new(@"^PASS:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex FailLineRegex = new(@"^FAIL:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex SkipLineRegex = new(@"^SKIP:\s*(.+)$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<ValidateDeployedEnvironmentScriptRunner> _logger;

    public ValidateDeployedEnvironmentScriptRunner(
        IOptions<H13AcceptanceOptions> options,
        ILogger<ValidateDeployedEnvironmentScriptRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<E2EValidationOutcome> RunAsync(
        E2EValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!File.Exists(_options.ValidateDeployedEnvironmentScriptPath))
        {
            throw new FileNotFoundException(
                $"Validate-DeployedEnvironment.ps1 not found at '{_options.ValidateDeployedEnvironmentScriptPath}'. " +
                "Set E2EAcceptance:ValidateDeployedEnvironmentScriptPath to the path relative to the App Service publish root.",
                _options.ValidateDeployedEnvironmentScriptPath);
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
        psi.ArgumentList.Add(_options.ValidateDeployedEnvironmentScriptPath);
        psi.ArgumentList.Add("-DataverseUrl");
        psi.ArgumentList.Add(request.DataverseUrl);
        if (!string.IsNullOrWhiteSpace(request.BffApiUrl))
        {
            psi.ArgumentList.Add("-BffApiUrl");
            psi.ArgumentList.Add(request.BffApiUrl);
        }

        _logger.LogInformation(
            "H13 E2E validation invoking: script={Script} customerId={CustomerId} runId={RunId} slot={Slot}",
            _options.ValidateDeployedEnvironmentScriptPath, request.CustomerId, request.RunId, request.TargetSlotName);

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
        timeoutCts.CancelAfter(_options.ValidateScriptTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new TimeoutException(
                $"Validate-DeployedEnvironment.ps1 exceeded timeout {_options.ValidateScriptTimeout} " +
                $"(customerId={request.CustomerId} runId={request.RunId}).");
        }

        var combined = stdout.ToString();
        var passed = PassLineRegex.Matches(combined).Select(m => m.Groups[1].Value.Trim()).ToList();
        var failed = FailLineRegex.Matches(combined).Select(m => m.Groups[1].Value.Trim()).ToList();
        var skipped = SkipLineRegex.Matches(combined).Select(m => m.Groups[1].Value.Trim()).ToList();

        if (process.ExitCode != 0 || failed.Count > 0)
        {
            var diag = new StringBuilder();
            diag.Append("Validate-DeployedEnvironment.ps1 exited ").Append(process.ExitCode);
            if (failed.Count > 0)
            {
                diag.Append(" with ").Append(failed.Count).Append(" failing check(s): ");
                diag.AppendJoin(", ", failed);
            }
            if (stderr.Length > 0)
            {
                diag.Append(" | stderr: ").Append(stderr.ToString().Trim());
            }
            return new E2EValidationOutcome.Failure(failed, diag.ToString());
        }

        return new E2EValidationOutcome.Success(passed, skipped);
    }
}
