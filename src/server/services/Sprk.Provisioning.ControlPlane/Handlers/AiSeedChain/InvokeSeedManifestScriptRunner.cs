// -----------------------------------------------------------------------------
// InvokeSeedManifestScriptRunner.cs
//
// Production <see cref="ISeedManifestRunner"/> implementation — shells out to
// task 069's scripts/seed-data/Invoke-SeedManifest.ps1 with -Live +
// -EnvironmentUrl and captures the exit code + stdout/stderr for the handler
// to record into Cosmos runNotes.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-15
//       (H12a acceptance — all seed rows present, no duplicates, playbook
//       consumers resolve to shipped playbooks).
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       non-zero exit maps to QuarantineRequired (partial seed = broken FK).
//   - ADR-039 amendment 2026-07-05: retired-artifact enforcement is a HARD
//       gate in the orchestrator; a non-zero exit here from that gate means
//       the manifest was hand-edited to include a retired artifact — the
//       handler's own defense-in-depth check should have caught it first,
//       but this is the last-ditch backstop.
//
// NON-GOALS (this task):
//   - Does NOT re-implement the per-artifact deployer fan-out in C#. The PS
//     orchestrator IS the source-of-truth (parity with
//     <see cref="BicepInfraDeploy.ProvisionCustomerScriptBicepDeployRunner"/>).
//   - Does NOT own retired-artifact enforcement — that's task 069's PS
//     script + <see cref="FileSeedManifestReader"/>'s defense-in-depth scan.
//   - Does NOT own idempotency — the handler computes
//     <c>h12a-{customerId}-{manifestHash}</c> and checks CompletedPhases BEFORE
//     invoking this runner.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pwsh + real Dataverse — parity with
//   <see cref="BicepInfraDeploy.ProvisionCustomerScriptBicepDeployRunner"/>).
//   Env-guarded smoke tests can be added alongside CosmosSmokeTests when a
//   dedicated dev-provisioning Dataverse env is available; for this task the
//   handler unit tests via stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <summary>
/// Shells out to <c>scripts/seed-data/Invoke-SeedManifest.ps1</c> and translates
/// its exit code + captured streams into <see cref="SeedManifestInvocationOutcome"/>.
/// </summary>
public sealed class InvokeSeedManifestScriptRunner : ISeedManifestRunner
{
    private const int StdoutTailBudget = 800;
    private const int StderrTailBudget = 800;
    private const string TenantIdEnvVar = "SPRK_H12A_TENANT_ID";
    private const string CustomerIdEnvVar = "SPRK_H12A_CUSTOMER_ID";

    private readonly AiSeedChainOptions _options;
    private readonly ILogger<InvokeSeedManifestScriptRunner> _logger;

    /// <summary>Constructs the runner bound to the on-disk PS script + pwsh executable.</summary>
    public InvokeSeedManifestScriptRunner(
        IOptions<AiSeedChainOptions> options,
        ILogger<InvokeSeedManifestScriptRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SeedManifestInvocationOutcome> InvokeAsync(
        SeedManifestInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scriptPath = _options.InvokeSeedManifestScriptPath;
        if (!File.Exists(scriptPath))
        {
            // Configuration / deployment error — fail loud per NFR-05 rather
            // than silently returning a Failure outcome (which the operator
            // might mis-diagnose as a Dataverse issue).
            throw new FileNotFoundException(
                $"Invoke-SeedManifest.ps1 not found at '{scriptPath}'. " +
                "Verify scripts/seed-data/ ships in the L2 publish output (see spec.md NFR-05 fail-fast).",
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
        psi.ArgumentList.Add("-Live");
        psi.ArgumentList.Add("-EnvironmentUrl");
        psi.ArgumentList.Add(request.TargetDataverseUrl);
        psi.ArgumentList.Add("-Manifest");
        psi.ArgumentList.Add(_options.ManifestPath);

        // TenantId flows via env var so per-artifact seeder scripts that
        // require the auth scope can pick it up without needing us to
        // introspect their varying parameter surfaces. CustomerId flows the
        // same way for downstream log-correlation. Parity with
        // ProvisionCustomerScriptBicepDeployRunner.
        psi.EnvironmentVariables[TenantIdEnvVar] = request.TenantId;
        psi.EnvironmentVariables[CustomerIdEnvVar] = request.CustomerId;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H12a AI seed chain starting: customerId={CustomerId} tenantId={TenantId} dataverseUrl={DataverseUrl}",
            request.CustomerId, request.TenantId, request.TargetDataverseUrl);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.SeedTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"H12a Invoke-SeedManifest.ps1 for '{request.CustomerId}' timed out after {_options.SeedTimeout}.");
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "H12a AI seed chain exited: customerId={CustomerId} exitCode={ExitCode}",
            request.CustomerId, process.ExitCode);

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"Invoke-SeedManifest.ps1 exited {process.ExitCode} for customerId '{request.CustomerId}'. " +
                $"Stderr: {Truncate(stderrText, StderrTailBudget)} Stdout tail: {Truncate(stdoutText, StdoutTailBudget)}";
            return new SeedManifestInvocationOutcome.Failure(diagnostic);
        }

        // Success: return the stdout tail for observability. The handler
        // records it into Cosmos runNotes so operators can inspect the
        // per-artifact PENDING/PLACEHOLDER markers task 069's summary emits.
        var summary = Truncate(stdoutText, StdoutTailBudget);
        return new SeedManifestInvocationOutcome.Success(summary);
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
