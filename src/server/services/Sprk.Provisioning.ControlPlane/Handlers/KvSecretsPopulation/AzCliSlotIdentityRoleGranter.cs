// -----------------------------------------------------------------------------
// AzCliSlotIdentityRoleGranter.cs
//
// Production <see cref="ISlotIdentityRoleGranter"/> — reads both slots' System-
// Assigned MI principals via `az webapp identity show` / `az webapp deployment
// slot show` and grants the KV Secrets User role via `az role assignment create`.
//
// EXPECTED STEADY-STATE OUTCOME (post-Phase-C):
//   Both `az webapp identity show` calls return an object without a `principalId`
//   (or with `null` — the App Service uses UAMI-only per uami.bicep task 028 +
//   app-service.bicep task 029). The granter returns
//   <see cref="SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity"/> which
//   the handler interprets as SUCCESS-equivalent.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>Shells out to `az webapp identity show` + `az role assignment create`.</summary>
public sealed class AzCliSlotIdentityRoleGranter : ISlotIdentityRoleGranter
{
    private readonly KvSecretsPopulationOptions _options;
    private readonly ILogger<AzCliSlotIdentityRoleGranter> _logger;

    /// <summary>Constructs the granter bound to the az CLI executable path.</summary>
    public AzCliSlotIdentityRoleGranter(
        IOptions<KvSecretsPopulationOptions> options,
        ILogger<AzCliSlotIdentityRoleGranter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SlotIdentityRoleGrantResult> GrantAsync(
        SlotIdentityRoleGrantInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.VaultResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.RoleDefinitionId);

        string? prodPrincipal;
        string? stagingPrincipal;
        try
        {
            prodPrincipal = await ReadSlotSystemAssignedPrincipalIdAsync(
                input.SubscriptionId, input.ResourceGroupName, input.AppServiceName,
                slotName: null, cancellationToken).ConfigureAwait(false);
            stagingPrincipal = await ReadSlotSystemAssignedPrincipalIdAsync(
                input.SubscriptionId, input.ResourceGroupName, input.AppServiceName,
                slotName: input.StagingSlotName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SlotIdentityRoleGrantResult.Failure(
                $"T5 slot-identity read failed for App Service '{input.AppServiceName}': " +
                $"{ex.GetType().Name}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(prodPrincipal) && string.IsNullOrWhiteSpace(stagingPrincipal))
        {
            _logger.LogInformation(
                "H4 T5 interim: no System-Assigned MI on either slot (post-Phase-C UAMI steady state) — " +
                "T5 structurally impossible; treating as SUCCESS. appService={AppService}",
                input.AppServiceName);
            return new SlotIdentityRoleGrantResult.NoSlotSystemAssignedIdentity(
                $"No System-Assigned MI on prod or staging slot of '{input.AppServiceName}' — post-Phase-C UAMI steady state.");
        }

        var failures = new List<string>(capacity: 2);
        if (!string.IsNullOrWhiteSpace(prodPrincipal))
        {
            var err = await TryGrantAsync(input, prodPrincipal!, "prod", cancellationToken).ConfigureAwait(false);
            if (err is not null) failures.Add($"prod slot ({prodPrincipal}): {err}");
        }
        if (!string.IsNullOrWhiteSpace(stagingPrincipal))
        {
            var err = await TryGrantAsync(input, stagingPrincipal!, input.StagingSlotName, cancellationToken).ConfigureAwait(false);
            if (err is not null) failures.Add($"staging slot ({stagingPrincipal}): {err}");
        }

        if (failures.Count > 0)
        {
            return new SlotIdentityRoleGrantResult.Failure(
                $"T5 KV Secrets User grant failed for App Service '{input.AppServiceName}': " +
                string.Join(" | ", failures));
        }

        return new SlotIdentityRoleGrantResult.Granted(prodPrincipal ?? "", stagingPrincipal ?? "");
    }

    private async Task<string?> ReadSlotSystemAssignedPrincipalIdAsync(
        string subscriptionId, string resourceGroupName, string appServiceName,
        string? slotName, CancellationToken cancellationToken)
    {
        var args = new List<string> { "webapp" };
        if (slotName is not null)
        {
            args.Add("deployment");
            args.Add("slot");
            args.Add("show");
            args.Add("--slot");
            args.Add(slotName);
        }
        else
        {
            args.Add("show");
        }
        args.Add("--subscription");
        args.Add(subscriptionId);
        args.Add("--resource-group");
        args.Add(resourceGroupName);
        args.Add("--name");
        args.Add(appServiceName);
        args.Add("--query");
        args.Add("identity.principalId");
        args.Add("-o");
        args.Add("tsv");

        var (exit, stdout, stderr) = await InvokeAzCapturingAsync(
            _options.T5RoleGrantTimeout, cancellationToken, args.ToArray()).ConfigureAwait(false);
        if (exit != 0)
        {
            throw new InvalidOperationException(
                $"az webapp {(slotName is not null ? "deployment slot " : "")}show for '{appServiceName}' " +
                $"(slot={slotName ?? "prod"}) exit {exit}. Stderr: {Truncate(stderr, 500)}");
        }
        var value = stdout.Trim();
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return value;
    }

    private async Task<string?> TryGrantAsync(
        SlotIdentityRoleGrantInput input, string principalId, string slotLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "H4 T5 interim: granting KV Secrets User on {KvResourceId} to slot={Slot} principalId={PrincipalId}",
                input.VaultResourceId, slotLabel, principalId);
            var (exit, _, stderr) = await InvokeAzCapturingAsync(
                _options.T5RoleGrantTimeout, cancellationToken,
                "role", "assignment", "create",
                "--subscription", input.SubscriptionId,
                "--assignee-object-id", principalId,
                "--assignee-principal-type", "ServicePrincipal",
                "--role", input.RoleDefinitionId,
                "--scope", input.VaultResourceId).ConfigureAwait(false);

            if (exit != 0)
            {
                // az returns non-zero when the role assignment already exists;
                // treat idempotent duplicate as success.
                if (stderr.Contains("RoleAssignmentExists", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "H4 T5 idempotent — role assignment already exists for {Slot} principalId={PrincipalId}",
                        slotLabel, principalId);
                    return null;
                }
                return $"az role assignment create exit {exit}: {Truncate(stderr, 400)}";
            }
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task<(int exit, string stdout, string stderr)> InvokeAzCapturingAsync(
        TimeSpan timeout, CancellationToken cancellationToken, params string[] args)
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
            timeoutCts.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"az CLI invocation timed out after {timeout}.");
        }

        return (process.ExitCode, stdout.ToString(), stderr.ToString());
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
