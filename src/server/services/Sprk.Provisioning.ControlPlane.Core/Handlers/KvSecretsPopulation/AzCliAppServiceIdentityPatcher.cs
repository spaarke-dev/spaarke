// -----------------------------------------------------------------------------
// AzCliAppServiceIdentityPatcher.cs
//
// RETIRED (task 125, Wave G-2, 2026-08-19): superseded by
// <see cref="ArmAppServiceIdentityPatcher"/> (Azure.ResourceManager.AppService
// SDK port). No longer registered in Worker/Program.cs's
// IAppServiceIdentityPatcher DI slot. Retained on disk (NOT deleted); see
// ProvisionCustomerScriptBicepDeployRunner.cs's retirement banner for the
// full rationale.
// -----------------------------------------------------------------------------

// Production <see cref="IAppServiceIdentityPatcher"/> — shells out to
// `az webapp update --set keyVaultReferenceIdentity=<UAMI-RID>` and
// `az webapp deployment slot update --slot <staging> --set
//  keyVaultReferenceIdentity=<UAMI-RID>` under the operator's `az` auth chain.
//
// Parity with H2a's AzCliArmKeyVaultRefProbe posture (read only in H2a's case;
// write in H4's case).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>Shells out to `az webapp update` + `az webapp deployment slot update`.</summary>
public sealed class AzCliAppServiceIdentityPatcher : IAppServiceIdentityPatcher
{
    private readonly KvSecretsPopulationOptions _options;
    private readonly ILogger<AzCliAppServiceIdentityPatcher> _logger;

    /// <summary>Constructs the patcher bound to the az CLI executable path.</summary>
    public AzCliAppServiceIdentityPatcher(
        IOptions<KvSecretsPopulationOptions> options,
        ILogger<AzCliAppServiceIdentityPatcher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AppServiceIdentityPatchResult> PatchKeyVaultReferenceIdentityAsync(
        AppServiceIdentityPatchInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ResourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.AppServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.StagingSlotName);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.UserAssignedIdentityResourceId);

        var failures = new List<string>(capacity: 2);

        // (1) Production slot.
        try
        {
            _logger.LogInformation(
                "H4 T1 PATCH prod slot: appService={AppService} rg={ResourceGroup} uami={UamiRid}",
                input.AppServiceName, input.ResourceGroupName, input.UserAssignedIdentityResourceId);
            await InvokeAzAsync(
                cancellationToken,
                "webapp", "update",
                "--subscription", input.SubscriptionId,
                "--resource-group", input.ResourceGroupName,
                "--name", input.AppServiceName,
                "--set", $"keyVaultReferenceIdentity={input.UserAssignedIdentityResourceId}")
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"prod slot: {ex.GetType().Name}: {ex.Message}");
        }

        // (2) Staging slot.
        try
        {
            _logger.LogInformation(
                "H4 T1 PATCH staging slot: appService={AppService} slot={Slot} rg={ResourceGroup} uami={UamiRid}",
                input.AppServiceName, input.StagingSlotName, input.ResourceGroupName, input.UserAssignedIdentityResourceId);
            await InvokeAzAsync(
                cancellationToken,
                "webapp", "deployment", "slot", "update",
                "--subscription", input.SubscriptionId,
                "--resource-group", input.ResourceGroupName,
                "--name", input.AppServiceName,
                "--slot", input.StagingSlotName,
                "--set", $"keyVaultReferenceIdentity={input.UserAssignedIdentityResourceId}")
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"staging slot '{input.StagingSlotName}': {ex.GetType().Name}: {ex.Message}");
        }

        if (failures.Count > 0)
        {
            return new AppServiceIdentityPatchResult.Failure(
                $"T1 PATCH failed for App Service '{input.AppServiceName}': " +
                string.Join(" | ", failures));
        }

        return new AppServiceIdentityPatchResult.Success();
    }

    private async Task InvokeAzAsync(CancellationToken cancellationToken, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _options.AzCliExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.T1PatchTimeout);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"az webapp update timed out after {_options.T1PatchTimeout}.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"az webapp update exit {process.ExitCode}. Stderr: {Truncate(stderr.ToString(), 800)}");
        }
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
            // Benign race.
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
