// -----------------------------------------------------------------------------
// SecretClientKvSharedSecretAccessor.cs
//
// Task 200 — production <see cref="ISharedKvSecretAccessor"/>. Wraps
// Azure.Security.KeyVault.Secrets.SecretClient (per-vault-per-call, parity
// with SecretClientKvWriter + SecretClientKvReader constructor posture).
//
// SDK SHAPES (ground-truthed via reflection against the installed 4.11.0
// package — same posture SecretClientKvWriter's file header documents):
//   SetSecretAsync(name, value, ct) — upsert (SDK docs: "Sets a secret in a
//     specified key vault. If the named secret already exists, Azure Key
//     Vault creates a new version of that secret.").
//   GetSecretAsync(name, version=null, ct) — throws RequestFailedException
//     with Status==404 when absent.
//
// CLEARTEXT NO-LOG (ADR-028): ZERO Log* calls that include the value; only
// diagnostics referencing the secret NAME, vault NAME, and HTTP status.
// -----------------------------------------------------------------------------

using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Production <see cref="ISharedKvSecretAccessor"/>. Constructs a
/// <see cref="SecretClient"/> per call — the vault name is a per-invocation
/// input, matching <see cref="SecretClientKvWriter"/>'s posture.
/// </summary>
public sealed class SecretClientKvSharedSecretAccessor : ISharedKvSecretAccessor
{
    private readonly TokenCredential _credential;
    private readonly SecretClientOptions? _clientOptions;
    private readonly ILogger<SecretClientKvSharedSecretAccessor> _logger;

    /// <summary>Constructs the production accessor bound to the shared UAMI-pinned credential.</summary>
    public SecretClientKvSharedSecretAccessor(
        TokenCredential credential,
        ILogger<SecretClientKvSharedSecretAccessor> logger)
        : this(credential, clientOptions: null, logger)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/>.</summary>
    internal SecretClientKvSharedSecretAccessor(
        TokenCredential credential,
        SecretClientOptions? clientOptions,
        ILogger<SecretClientKvSharedSecretAccessor> logger)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(logger);
        _credential = credential;
        _clientOptions = clientOptions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SharedKvSecretReadResult> ReadAsync(
        string vaultName,
        string secretName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);

        try
        {
            var client = BuildClient(vaultName);
            var response = await client.GetSecretAsync(secretName, version: null, cancellationToken)
                .ConfigureAwait(false);
            var value = response.Value.Value;
            return string.IsNullOrEmpty(value)
                ? new SharedKvSecretReadResult.Failure(
                    $"GetSecretAsync for '{secretName}' on vault '{vaultName}' succeeded but returned an empty value.")
                : new SharedKvSecretReadResult.Success(value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return new SharedKvSecretReadResult.NotFound();
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogError(ex,
                "H4-shared KV read access denied (403) for '{SecretName}' on '{VaultName}' — " +
                "verify the L2 UAMI holds Key Vault Secrets Officer on this vault.",
                secretName, vaultName);
            return new SharedKvSecretReadResult.Failure(
                $"Access denied (403) reading '{secretName}' on vault '{vaultName}'. Verify the L2 UAMI has " +
                $"'Key Vault Secrets Officer' RBAC on this vault. {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H4-shared KV read fault for '{SecretName}' on '{VaultName}'", secretName, vaultName);
            return new SharedKvSecretReadResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<SharedKvSecretWriteResult> WriteAsync(
        string vaultName,
        string secretName,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretName);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        try
        {
            var client = BuildClient(vaultName);
            await client.SetSecretAsync(secretName, value, cancellationToken).ConfigureAwait(false);
            return new SharedKvSecretWriteResult.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 403)
        {
            _logger.LogError(ex,
                "H4-shared KV write access denied (403) for '{SecretName}' on '{VaultName}' — " +
                "verify the L2 UAMI holds Key Vault Secrets Officer on this vault.",
                secretName, vaultName);
            return new SharedKvSecretWriteResult.Failure(
                $"Access denied (403) writing '{secretName}' on vault '{vaultName}'. Verify the L2 UAMI has " +
                $"'Key Vault Secrets Officer' RBAC on this vault. {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H4-shared KV write fault for '{SecretName}' on '{VaultName}'", secretName, vaultName);
            return new SharedKvSecretWriteResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private SecretClient BuildClient(string vaultName)
    {
        var vaultUri = new Uri($"https://{vaultName}.vault.azure.net/");
        return _clientOptions is null
            ? new SecretClient(vaultUri, _credential)
            : new SecretClient(vaultUri, _credential, _clientOptions);
    }
}
