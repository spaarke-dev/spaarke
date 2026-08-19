// -----------------------------------------------------------------------------
// AzCliArmKeyVaultRefProbe.cs
//
// RETIRED (task 123, Wave G-2, 2026-08-19): superseded by
// <see cref="ArmKeyVaultRefProbe"/> (Azure.ResourceManager.AppService SDK
// port). No longer registered in Worker/Program.cs's IArmKeyVaultRefProbe DI
// slot (that registration — reused by H4/task 125 too — now points at the
// new class). Retained on disk (NOT deleted); see
// ProvisionCustomerScriptBicepDeployRunner.cs's retirement banner for the
// full rationale.
// -----------------------------------------------------------------------------

// Production <see cref="IArmKeyVaultRefProbe"/> — reads
// <c>keyVaultReferenceIdentity</c> on the customer's BFF App Service prod +
// staging slots via `az webapp show` / `az webapp deployment slot show`.
//
// Uses the operator's `az` auth chain (DefaultAzureCredential inside az CLI
// via `az login`), matching <see cref="Preflight.PowerShellPreflightProbe"/>'s
// posture. Not exercised by CI unit suite — the H2a handler tests use stubs.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;

/// <summary>Shells out to `az webapp show` + `az webapp deployment slot show`.</summary>
public sealed class AzCliArmKeyVaultRefProbe : IArmKeyVaultRefProbe
{
    private readonly BicepInfraDeployOptions _options;
    private readonly ILogger<AzCliArmKeyVaultRefProbe> _logger;

    /// <summary>Constructs the probe bound to the az CLI executable path.</summary>
    public AzCliArmKeyVaultRefProbe(
        IOptions<BicepInfraDeployOptions> options,
        ILogger<AzCliArmKeyVaultRefProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ArmKeyVaultRefProbeResult> VerifyKeyVaultReferenceIdentityAsync(
        ArmKeyVaultRefProbeInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var prodIdentity = await ReadKeyVaultRefIdentityAsync(
            input.SubscriptionId, input.ResourceGroupName, input.AppServiceName,
            slotName: null, cancellationToken).ConfigureAwait(false);

        var stagingIdentity = await ReadKeyVaultRefIdentityAsync(
            input.SubscriptionId, input.ResourceGroupName, input.AppServiceName,
            slotName: input.StagingSlotName, cancellationToken).ConfigureAwait(false);

        var expected = input.ExpectedUserAssignedIdentityResourceId;
        var prodMatch = string.Equals(prodIdentity, expected, StringComparison.OrdinalIgnoreCase);
        var stagingMatch = string.Equals(stagingIdentity, expected, StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation(
            "T1 probe: appService={AppService} prodMatch={ProdMatch} stagingMatch={StagingMatch}",
            input.AppServiceName, prodMatch, stagingMatch);

        return prodMatch && stagingMatch
            ? new ArmKeyVaultRefProbeResult.Match()
            : new ArmKeyVaultRefProbeResult.Mismatch(prodIdentity, stagingIdentity);
    }

    private async Task<string?> ReadKeyVaultRefIdentityAsync(
        string subscriptionId,
        string resourceGroupName,
        string appServiceName,
        string? slotName,
        CancellationToken cancellationToken)
    {
        // az webapp [deployment slot] show --query "keyVaultReferenceIdentity" -o tsv
        var psi = new ProcessStartInfo
        {
            FileName = _options.AzCliExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("webapp");
        if (slotName is not null)
        {
            psi.ArgumentList.Add("deployment");
            psi.ArgumentList.Add("slot");
            psi.ArgumentList.Add("show");
            psi.ArgumentList.Add("--slot");
            psi.ArgumentList.Add(slotName);
        }
        else
        {
            psi.ArgumentList.Add("show");
        }
        psi.ArgumentList.Add("--subscription");
        psi.ArgumentList.Add(subscriptionId);
        psi.ArgumentList.Add("--resource-group");
        psi.ArgumentList.Add(resourceGroupName);
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add(appServiceName);
        psi.ArgumentList.Add("--query");
        psi.ArgumentList.Add("keyVaultReferenceIdentity");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("tsv");

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

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.ArmProbeTimeout);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"az webapp show for '{appServiceName}' (slot={slotName ?? "prod"}) timed out after {_options.ArmProbeTimeout}.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"az webapp show exited {process.ExitCode} for appService='{appServiceName}' slot='{slotName ?? "prod"}'. " +
                $"Stderr: {stderr}");
        }

        var value = stdout.ToString().Trim();
        // `az` prints "None" (or blank) when the property is null.
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return value;
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
}
