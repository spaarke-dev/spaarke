// -----------------------------------------------------------------------------
// AzCliSpeContainerIdKvWriter.cs
//
// Production ISpeContainerIdKvWriter — shells out to `az keyvault secret
// show`/`set` under the operator's `az` auth chain (DefaultAzureCredential
// inside az CLI via `az login`). Parity with H4's AzCliKvSecretsWriter
// shell-out posture (see ISpeContainerIdKvWriter.cs for why this is a
// separate, narrower seam rather than a reuse of AzCliKvSecretsWriter).
//
// This write is orthogonal to T6 (Key Vault is not an SPE resource) — parity
// with New-BusinessUnitContainer.ps1's own Dataverse write, which the script
// header explicitly notes "remains delegated by design ... orthogonal to the
// T6 trap." The `az` CLI's own auth chain (delegated, operator-scoped) is the
// correct posture here, same as every other KV write in this codebase.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>Shells out to `az keyvault secret show/set` for the single container-type-id secret.</summary>
public sealed class AzCliSpeContainerIdKvWriter : ISpeContainerIdKvWriter
{
    private readonly SpeContainerTypeOptions _options;
    private readonly ILogger<AzCliSpeContainerIdKvWriter> _logger;

    /// <summary>Constructs the writer bound to the az CLI executable path.</summary>
    public AzCliSpeContainerIdKvWriter(
        IOptions<SpeContainerTypeOptions> options,
        ILogger<AzCliSpeContainerIdKvWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SpeContainerIdKvWriteResult> WriteAsync(
        SpeContainerIdKvWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetKeyVaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerTypeId);

        var secretName = _options.ContainerTypeIdSecretName;

        bool exists;
        string? existingValue;
        try
        {
            (exists, existingValue) = await TryGetExistingAsync(
                request.TargetKeyVaultName, request.SubscriptionId, secretName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SpeContainerIdKvWriteResult.Failure(
                $"az CLI probe failed BEFORE any KV write attempted: {ex.GetType().Name}: {ex.Message}. " +
                "Verify az is on PATH + `az login` succeeded on the L2 App Service host.");
        }

        if (exists && request.UpgradeMode)
        {
            _logger.LogInformation(
                "H8 KV writer: SKIP (never-rotate; container=data) {SecretName} on vault {Vault} " +
                "(upgrade mode + already exists) — customer={CustomerId}",
                secretName, request.TargetKeyVaultName, request.CustomerId);
            return new SpeContainerIdKvWriteResult.SkippedAlreadyPresent(
                PreviewValue(existingValue));
        }

        try
        {
            _logger.LogInformation(
                "H8 KV writer: WRITE {SecretName} on vault {Vault} (customer={CustomerId})",
                secretName, request.TargetKeyVaultName, request.CustomerId);
            await InvokeAzAsync(
                _options.KvOperationTimeout,
                cancellationToken,
                "keyvault", "secret", "set",
                "--vault-name", request.TargetKeyVaultName,
                "--name", secretName,
                "--value", request.ContainerTypeId,
                "--subscription", request.SubscriptionId)
                .ConfigureAwait(false);
            return new SpeContainerIdKvWriteResult.Wrote();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SpeContainerIdKvWriteResult.Failure(
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<(bool Exists, string? Value)> TryGetExistingAsync(
        string vaultName, string subscriptionId, string secretName, CancellationToken cancellationToken)
    {
        try
        {
            var value = await InvokeAzForOutputAsync(
                _options.KvOperationTimeout,
                cancellationToken,
                "keyvault", "secret", "show",
                "--vault-name", vaultName,
                "--name", secretName,
                "--subscription", subscriptionId,
                "--query", "value",
                "-o", "tsv")
                .ConfigureAwait(false);
            return (true, value);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("SecretNotFound", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }
    }

    private static string PreviewValue(string? value)
        => string.IsNullOrEmpty(value) ? "(empty)" : (value.Length <= 8 ? value : value[..8] + "...");

    private async Task InvokeAzAsync(
        TimeSpan timeout, CancellationToken cancellationToken, params string[] args)
        => await InvokeAzForOutputAsync(timeout, cancellationToken, args).ConfigureAwait(false);

    private async Task<string> InvokeAzForOutputAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        params string[] args)
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
            throw new TimeoutException(
                $"az CLI invocation timed out after {timeout}. Args: {SafeArgsForLog(args)}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"az CLI exit {process.ExitCode}. Stderr: {Truncate(stderr.ToString(), 800)}");
        }

        return stdout.ToString().Trim();
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
            // Benign race between HasExited check + Kill.
        }
    }

    /// <summary>Redacts any <c>--value</c> argument to <c>***</c> so cleartext never hits log output.</summary>
    private static string SafeArgsForLog(string[] args)
    {
        var redacted = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            redacted.Add(args[i]);
            if (string.Equals(args[i], "--value", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                redacted.Add("***");
                i++;
            }
        }
        return string.Join(' ', redacted);
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s
            : s[..max] + "...[truncated]";
}
