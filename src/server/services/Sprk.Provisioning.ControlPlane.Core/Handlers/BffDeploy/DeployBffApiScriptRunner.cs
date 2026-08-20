// -----------------------------------------------------------------------------
// DeployBffApiScriptRunner.cs
//
// RETIRED (task 132, Wave G-3, 2026-08-19): superseded by the
// resolve-manifest/download-blob/Kudu-zip-deploy triad
// (<see cref="ArtifactManifestVerifier"/> + <see cref="BlobArtifactDownloader"/> +
// <see cref="KuduZipDeployer"/>, DS-4 §5 re-scope). This collaborator ran
// `dotnet publish` AT PROVISION TIME (line ~221 below) — "the heaviest
// environment dependency of all" per DS-4's handler audit — requiring a full
// repo checkout + dotnet SDK inside the L2 runtime, which Option D's zero-
// shell design forbids. No longer registered in Worker/Program.cs's
// IBffDeployRunner DI slot. Retained on disk (NOT deleted); see
// AzCliKvSecretsWriter.cs's retirement banner for the full rationale of this
// project's on-disk-retention convention.
// -----------------------------------------------------------------------------

// Production <see cref="IBffDeployRunner"/> implementation. Shells out to
// scripts/Deploy-BffApi.ps1 to build + zip + deploy to the staging slot +
// verify staging /health (steps 1–3 of the script's 7-step flow). The
// handler's IAppServiceSlotSwapper + IHealthProbe handle steps 4–6 in-
// process for tight Cosmos state coupling — see IBffDeployRunner file header
// "SCOPE DIVISION" for full rationale.
//
// SPEC / DESIGN references:
//   - spec.md FR-12 (H9 acceptance).
//   - design.md §4.1 H9 row + Gap 2 (Phase 4 hardening).
//   - .claude/skills/bff-deploy/SKILL.md (reference model for slot-swap).
//   - scripts/Deploy-BffApi.ps1 (canonical impl; task 013 hardened Phase 4
//     of the parent Deploy-Release.ps1 orchestrator to be customerId-driven).
//
// PATH-A INVOCATION POSTURE (documented per CLAUDE.md §6.5):
//   The wave-shipped Deploy-BffApi.ps1 lacks a -SkipSwap switch. The runner
//   invokes the script WITHOUT -UseSlotDeploy so the script performs a
//   direct deploy path — but targets the STAGING App Service (i.e., the
//   caller passes the staging slot's FQDN via -AppServiceName rather than
//   the production App Service). This preserves the script's build + zip +
//   deploy + verify-staging-health scope (avoiding the script's own
//   steps 4–6) while giving the handler ownership of the swap + prod
//   smoke test + rollback. In a future iteration the script can grow a
//   -SkipSwap switch + the runner can flip to the more conventional
//   -UseSlotDeploy + -SkipSwap invocation — the seam remains stable.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pwsh + real Azure — parity with
//   ProvisionCustomerScriptBicepDeployRunner).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Shells out to <c>scripts/Deploy-BffApi.ps1</c> for the build + zip +
/// staging-slot deploy + staging-slot health check (H9's steps 1–3).
/// </summary>
public sealed class DeployBffApiScriptRunner : IBffDeployRunner
{
    private readonly BffDeployOptions _options;
    private readonly ILogger<DeployBffApiScriptRunner> _logger;

    public DeployBffApiScriptRunner(
        IOptions<BffDeployOptions> options,
        ILogger<DeployBffApiScriptRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<BffDeployRunnerOutcome> DeployAsync(
        BffDeployRunnerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var scriptPath = _options.DeployBffApiScriptPath;
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                $"Deploy-BffApi.ps1 not found at '{scriptPath}'. " +
                "Verify scripts/ ships in the L2 publish output.",
                scriptPath);
        }

        var stopwatch = Stopwatch.StartNew();

        // Path A invocation (see file header): deploy DIRECTLY to the
        // staging App Service (`{AppServiceName}-{SlotName}`) so the script
        // stays in its steps-1-3 scope. The handler's IAppServiceSlotSwapper
        // + IHealthProbe own steps 4-6.
        var stagingAppServiceName = $"{request.AppServiceName}-{request.StagingSlotName}";

        var psi = new ProcessStartInfo
        {
            FileName = _options.PwshExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _options.RepositoryRoot,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(scriptPath);
        psi.ArgumentList.Add("-Environment");
        psi.ArgumentList.Add(request.EnvironmentName);
        psi.ArgumentList.Add("-ResourceGroupName");
        psi.ArgumentList.Add(request.ResourceGroupName);
        psi.ArgumentList.Add("-AppServiceName");
        psi.ArgumentList.Add(stagingAppServiceName);
        if (request.SkipBuild)
        {
            psi.ArgumentList.Add("-SkipBuild");
        }
        // BuildId travels as an env var so the script can emit it in logs
        // without a script-signature change (parity with H2a's env-var carry).
        psi.EnvironmentVariables["SPRK_H9_BUILD_ID"] = request.BuildId;
        psi.EnvironmentVariables["SPRK_H9_CUSTOMER_ID"] = request.CustomerId;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H9 BFF deploy starting: customerId={CustomerId} appService={AppService} buildId={BuildId}",
            request.CustomerId, stagingAppServiceName, request.BuildId);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.DeployTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"H9 BFF deploy for '{request.CustomerId}' timed out after {_options.DeployTimeout}.");
        }

        stopwatch.Stop();

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "H9 BFF deploy exited: customerId={CustomerId} exitCode={ExitCode} durationMs={DurationMs}",
            request.CustomerId, process.ExitCode, stopwatch.ElapsedMilliseconds);

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"Deploy-BffApi.ps1 exit {process.ExitCode} for customerId '{request.CustomerId}'. " +
                $"Stderr: {Truncate(stderrText, 600)} Stdout tail: {Truncate(stdoutText, 400)}";
            return new BffDeployRunnerOutcome.Failure(diagnostic);
        }

        // Compute URLs deterministically from the request (Deploy-BffApi.ps1
        // exposes them via console output but the format is stable and easy
        // to derive without stdout parsing).
        var stagingUrl = $"https://{stagingAppServiceName}.azurewebsites.net";
        var productionUrl = $"https://{request.AppServiceName}.azurewebsites.net";

        return new BffDeployRunnerOutcome.Success(new BffDeployRunnerOutputs
        {
            PublishZipPath = _options.BffPublishZipPath,
            StagingSlotUrl = stagingUrl,
            ProductionSlotUrl = productionUrl,
            Duration = stopwatch.Elapsed,
        });
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
            // Race between HasExited + Kill — benign.
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
