// -----------------------------------------------------------------------------
// AzCliKvSecretReader.cs
//
// Production IKvSecretReader — shells out to `az keyvault secret show` under
// the operator's `az` auth chain (DefaultAzureCredential inside az CLI via
// `az login`). Parity with AzCliKvSecretsWriter's shell-out posture (H4).
//
// CI COVERAGE:
//   Not exercised by CI unit suite (no live az CLI + no live KV — parity with
//   AzCliKvSecretsWriter's own CI-exclusion note). Handler unit tests
//   substitute a fake IKvSecretReader.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>Shells out to <c>az keyvault secret show --query value -o tsv</c>.</summary>
public sealed class AzCliKvSecretReader : IKvSecretReader
{
    private static readonly TimeSpan KvSecretReadTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<AzCliKvSecretReader> _logger;

    /// <summary>Constructs the reader bound to the az CLI executable (resolved via PATH).</summary>
    public AzCliKvSecretReader(ILogger<AzCliKvSecretReader> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<KvSecretReadResult> ReadSecretAsync(
        string vaultName,
        string subscriptionId,
        string secretName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        try
        {
            var (exitCode, stdout, stderr) = await InvokeAzAsync(
                cancellationToken,
                "keyvault", "secret", "show",
                "--vault-name", vaultName,
                "--name", secretName,
                "--subscription", subscriptionId,
                "--query", "value",
                "-o", "tsv").ConfigureAwait(false);

            if (exitCode == 0)
            {
                var value = stdout.Trim();
                return string.IsNullOrEmpty(value)
                    ? new KvSecretReadResult.Failure(
                        $"az keyvault secret show for '{secretName}' on vault '{vaultName}' exited 0 but returned an empty value.")
                    : new KvSecretReadResult.Success(value);
            }

            if (stderr.Contains("SecretNotFound", StringComparison.OrdinalIgnoreCase))
            {
                return new KvSecretReadResult.NotFound();
            }

            return new KvSecretReadResult.Failure(
                $"az keyvault secret show exit {exitCode} for '{secretName}' on vault '{vaultName}'. " +
                $"Stderr: {Truncate(stderr, 400)}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AzCliKvSecretReader infrastructure fault reading '{SecretName}' on vault '{VaultName}'",
                secretName, vaultName);
            return new KvSecretReadResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAzAsync(
        CancellationToken cancellationToken,
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "az",
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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(KvSecretReadTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("az keyvault secret show timed out.");
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
            // Benign race between HasExited check + Kill.
        }
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max ? s : s[..max] + "...[truncated]";
}
