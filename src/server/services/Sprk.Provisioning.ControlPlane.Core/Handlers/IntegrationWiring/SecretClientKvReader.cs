// -----------------------------------------------------------------------------
// SecretClientKvReader.cs
//
// Production <see cref="IKvSecretReader"/> — task 160 (Wave G-6, Option D
// hybrid) SDK port of <see cref="AzCliKvSecretReader"/> (shelled out to
// `az keyvault secret show`; RETIRED, kept on disk unregistered — see that
// file's retirement banner). Uses Azure.Security.KeyVault.Secrets.SecretClient
// under DefaultAzureCredential pinned to the L2 UAMI — the read-side
// counterpart to task 125's <see cref="KvSecretsPopulation.SecretClientKvWriter"/>,
// reusing the SAME ground-truthed shapes that writer's file header documents
// (GetSecretAsync(name, version, ct) throwing RequestFailedException with
// Status==404 for a missing secret; KeyVaultSecret.Value is the cleartext
// string) plus task 120's KeyVaultCertBootstrapProbe precedent for
// constructing a per-vault-per-call SecretClient from a shared TokenCredential.
//
// ACCESS-DENIED vs NOT-FOUND vs GENERIC (task 143/144 verification-focused
// discipline — silent-fail-adjacent traps matter here): a 404
// RequestFailedException maps to the domain NotFound outcome (H14b/H14c
// treat this as "H4 has not yet provisioned this secret" — a distinct,
// Resumable-classified condition per those sub-handlers' own switch
// statements). A 403 RequestFailedException is classified as its OWN
// Failure branch with an explicit "verify the L2 UAMI RBAC grant" diagnostic
// — collapsing 403 into the generic catch-all would produce a diagnostic
// that reads like a transient fault when it is actually a permanent
// misconfiguration (the exact class of trap task 143/144 caught elsewhere
// in this project). Every other RequestFailedException status, and any
// non-Azure exception (network, DNS, timeout), falls through to the generic
// Failure branch — parity with AzCliKvSecretReader's own generic-exception
// catch shape (this reader's return CONTRACT is deliberately unchanged from
// the retired reader's: Success(value) | NotFound() | Failure(diagnostic) —
// see SecretClientKvReaderTests.cs's parity-shape tests).
//
// CI COVERAGE:
//   Fake-transport unit tests only (Azure.Core.Pipeline.HttpClientTransport
//   against a hand-rolled HttpMessageHandler — never Mock<HttpMessageHandler>,
//   ADR-038). No live az CLI, no live KV — same CI-exclusion posture the
//   retired reader documented for its own shell-out path.
// -----------------------------------------------------------------------------

using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <summary>
/// Calls Azure.Security.KeyVault.Secrets.SecretClient.GetSecretAsync for a
/// single secret. Constructed with a <see cref="TokenCredential"/>
/// (SecretClient is built per-vault-per-call — the vault name is a per-call
/// parameter, matching <see cref="KvSecretsPopulation.SecretClientKvWriter"/>'s
/// posture). Tests inject a <see cref="SecretClientOptions"/> built against a
/// fake <see cref="Azure.Core.Pipeline.HttpClientTransport"/> so the SDK's
/// own request/response marshaling runs unmodified — never a
/// Mock&lt;HttpMessageHandler&gt;, never a wrapper mock of SecretClient itself.
/// </summary>
public sealed class SecretClientKvReader : IKvSecretReader
{
    private readonly TokenCredential _credential;
    private readonly SecretClientOptions? _clientOptions;
    private readonly IntegrationWiringOptions _options;
    private readonly ILogger<SecretClientKvReader> _logger;

    /// <summary>Constructs the production reader bound to the shared UAMI-pinned credential.</summary>
    public SecretClientKvReader(
        TokenCredential credential,
        IOptions<IntegrationWiringOptions> options,
        ILogger<SecretClientKvReader> logger)
        : this(credential, clientOptions: null, options, logger)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/>.</summary>
    internal SecretClientKvReader(
        TokenCredential credential,
        SecretClientOptions? clientOptions,
        IOptions<IntegrationWiringOptions> options,
        ILogger<SecretClientKvReader> logger)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _credential = credential;
        _clientOptions = clientOptions;
        _options = options.Value;
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
            var vaultUri = new Uri($"https://{vaultName}.vault.azure.net/");
            var client = _clientOptions is null
                ? new SecretClient(vaultUri, _credential)
                : new SecretClient(vaultUri, _credential, _clientOptions);

            KeyVaultSecret secret;
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(_options.KvReadTimeout);
                try
                {
                    var response = await client.GetSecretAsync(secretName, version: null, timeoutCts.Token)
                        .ConfigureAwait(false);
                    secret = response.Value;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"SecretClient.GetSecretAsync timed out after {_options.KvReadTimeout} reading '{secretName}' on vault '{vaultName}'.");
                }
            }

            var value = secret.Value?.Trim();
            return string.IsNullOrEmpty(value)
                ? new KvSecretReadResult.Failure(
                    $"SecretClient.GetSecretAsync for '{secretName}' on vault '{vaultName}' succeeded but returned an empty value.")
                : new KvSecretReadResult.Success(value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new KvSecretReadResult.NotFound();
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogError(ex,
                "SecretClientKvReader access denied (403) reading '{SecretName}' on vault '{VaultName}' — " +
                "verify the L2 UAMI has been granted the 'Key Vault Secrets User' RBAC role on this vault.",
                secretName, vaultName);
            return new KvSecretReadResult.Failure(
                $"Access denied (403) reading '{secretName}' on vault '{vaultName}'. Verify the L2 UAMI holds " +
                $"the 'Key Vault Secrets User' RBAC role on this vault. {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SecretClientKvReader infrastructure fault reading '{SecretName}' on vault '{VaultName}'",
                secretName, vaultName);
            return new KvSecretReadResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
