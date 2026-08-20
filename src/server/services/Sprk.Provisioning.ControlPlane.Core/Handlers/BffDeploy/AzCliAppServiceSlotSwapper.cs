// -----------------------------------------------------------------------------
// AzCliAppServiceSlotSwapper.cs
//
// RETIRED (task 132, Wave G-3, 2026-08-19): superseded by
// <see cref="ArmSlotSwapper"/> (Azure.ResourceManager.AppService
// WebSiteSlotResource.SwapSlotAsync — a proper awaited LRO, DS-4 §5
// re-scope). IAppServiceSlotSwapper ITSELF is UNCHANGED (still the active
// interface — H9BffDeployHandler's swap + rollback-re-swap call sites are
// not modified by this retirement); only THIS implementation is no longer
// registered in Worker/Program.cs's IAppServiceSlotSwapper DI slot. Retained
// on disk (NOT deleted); see AzCliKvSecretsWriter.cs's retirement banner for
// the full rationale of this project's on-disk-retention convention.
// -----------------------------------------------------------------------------

// Production <see cref="IAppServiceSlotSwapper"/> implementation. Shells out
// to `az webapp deployment slot swap` — the operator auth chain (az login)
// provides the ARM token, honoring ADR-028's UAMI-outbound MUST rule at the
// operator boundary.
//
// SPEC / DESIGN references:
//   - spec.md FR-12 (H9 slot-swap semantics).
//   - .claude/skills/bff-deploy/SKILL.md (reference model — same az CLI
//     invocation shape as Deploy-BffApi.ps1's step 5 + step 7 rollback).
//   - ADR-028: operator az CLI auth chain is acceptable for the slot-swap
//     ARM op — no account-key path.
//
// CI COVERAGE:
//   Not exercised by CI unit suite (az CLI + real Azure). Handler unit tests
//   via IAppServiceSlotSwapper stubs are the coverage.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;

/// <summary>
/// Invokes <c>az webapp deployment slot swap</c> for H9's swap + rollback branches.
/// </summary>
public sealed class AzCliAppServiceSlotSwapper : IAppServiceSlotSwapper
{
    private readonly BffDeployOptions _options;
    private readonly ILogger<AzCliAppServiceSlotSwapper> _logger;

    public AzCliAppServiceSlotSwapper(
        IOptions<BffDeployOptions> options,
        ILogger<AzCliAppServiceSlotSwapper> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SlotSwapResult> SwapAsync(
        SlotSwapRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();

        var psi = new ProcessStartInfo
        {
            FileName = _options.AzCliExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("webapp");
        psi.ArgumentList.Add("deployment");
        psi.ArgumentList.Add("slot");
        psi.ArgumentList.Add("swap");
        psi.ArgumentList.Add("--subscription");
        psi.ArgumentList.Add(request.SubscriptionId);
        psi.ArgumentList.Add("--resource-group");
        psi.ArgumentList.Add(request.ResourceGroupName);
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add(request.AppServiceName);
        psi.ArgumentList.Add("--slot");
        psi.ArgumentList.Add(request.SourceSlotName);
        psi.ArgumentList.Add("--target-slot");
        psi.ArgumentList.Add(request.TargetSlotName);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H9 slot swap starting: appService={AppService} {Source}=>{Target}",
            request.AppServiceName, request.SourceSlotName, request.TargetSlotName);

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.SlotSwapTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"H9 slot swap ({request.SourceSlotName}=>{request.TargetSlotName}) timed out after {_options.SlotSwapTimeout}.");
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "H9 slot swap exited: appService={AppService} exitCode={ExitCode} durationMs={DurationMs}",
            request.AppServiceName, process.ExitCode, stopwatch.ElapsedMilliseconds);

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"az webapp deployment slot swap exit {process.ExitCode} " +
                $"({request.SourceSlotName}=>{request.TargetSlotName}). " +
                $"Stderr: {Truncate(stderr.ToString(), 600)} Stdout tail: {Truncate(stdout.ToString(), 400)}";
            return new SlotSwapResult.Failure(diagnostic);
        }

        return new SlotSwapResult.Success(stopwatch.Elapsed);
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
