// -----------------------------------------------------------------------------
// SecretClientSpeContainerIdKvWriter.cs
//
// Task 131 (Wave G-3) — production ISpeContainerIdKvWriter. Ports the retired
// AzCliSpeContainerIdKvWriter.cs (`az keyvault secret show/set`) to
// Azure.Security.KeyVault.Secrets.SecretClient — reuse of task 125's
// SecretClientKvWriter.cs idiom (SAME SDK class + SAME per-vault-per-call
// construction + SAME fake-transport test seam), per this task's POML step 3
// ("Reuse task 125's SecretClient pattern for the canonical SPE-
// ContainerTypeId write") and CLAUDE.md §11 (extend the proven pattern rather
// than inventing a new one).
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11 — carried forward from
// ISpeContainerIdKvWriter.cs's own header, still valid for this SDK swap):
// this remains a DELIBERATELY NARROW seam (ONE named secret, ONE explicit
// value) — it does NOT adopt task 125's SecretClientKvWriter.cs wholesale
// (that class's manifest/resolver/never-delete-guard/ArmClient-preflight-probe
// machinery exists for H4's ~26-entry canonical manifest, a materially larger
// problem H8 does not have). This file reuses ONLY the SecretClient
// construction + existence-check + fake-transport-test idiom, not the whole
// class. Cross-cutting-reuse would mean threading H8's single write through
// H4's manifest abstraction — a larger, riskier change to H4's already-
// shipped, tested production code, out of this task's scope (parity with
// ISpeContainerIdKvWriter.cs's original "Phase H (task 084) is explicitly
// slated to replace [H4's manifest] wholesale — extending it now duplicates
// work Phase H will redo" reasoning).
//
// NO ArmClient PREFLIGHT PROBE (deliberate simplification vs SecretClientKvWriter):
// H4's writer performs a whole-invocation `ArmClient.GetDefaultSubscriptionAsync`
// probe before any KV work, because a single H4 invocation writes ~26 secrets
// and a failed credential-chain probe upfront avoids partial multi-secret
// writes. H8 writes exactly ONE secret — GetSecretAsync's own
// RequestFailedException (auth failure surfaces here identically to a
// dedicated probe) already provides the same "fail before any write" honesty
// for a single-entry writer, without adding an unused ArmClient dependency to
// this narrower seam.
// -----------------------------------------------------------------------------

using Azure;
using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <summary>
/// Calls Azure.Security.KeyVault.Secrets.SecretClient for H8's single
/// SPE-ContainerTypeId secret write. Constructed with a
/// <see cref="TokenCredential"/> (SecretClient built per-vault-per-call — the
/// vault name is a per-run parameter, same posture as
/// <see cref="KvSecretsPopulation.SecretClientKvWriter"/>). Tests inject a
/// <see cref="SecretClientOptions"/> built against a fake
/// <see cref="Azure.Core.Pipeline.HttpClientTransport"/> — never a
/// Mock&lt;HttpMessageHandler&gt; (ADR-038).
/// </summary>
public sealed class SecretClientSpeContainerIdKvWriter : ISpeContainerIdKvWriter
{
    private readonly TokenCredential _credential;
    private readonly SecretClientOptions? _clientOptions;
    private readonly SpeContainerTypeOptions _options;
    private readonly ILogger<SecretClientSpeContainerIdKvWriter> _logger;

    /// <summary>Constructs the production writer bound to the shared UAMI-pinned credential.</summary>
    public SecretClientSpeContainerIdKvWriter(
        TokenCredential credential,
        IOptions<SpeContainerTypeOptions> options,
        ILogger<SecretClientSpeContainerIdKvWriter> logger)
        : this(credential, clientOptions: null, options, logger)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/>.</summary>
    internal SecretClientSpeContainerIdKvWriter(
        TokenCredential credential,
        SecretClientOptions? clientOptions,
        IOptions<SpeContainerTypeOptions> options,
        ILogger<SecretClientSpeContainerIdKvWriter> logger)
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
    public async Task<SpeContainerIdKvWriteResult> WriteAsync(
        SpeContainerIdKvWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetKeyVaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerTypeId);

        var secretName = _options.ContainerTypeIdSecretName;
        var vaultUri = new Uri($"https://{request.TargetKeyVaultName}.vault.azure.net/");
        var client = _clientOptions is null
            ? new SecretClient(vaultUri, _credential)
            : new SecretClient(vaultUri, _credential, _clientOptions);

        bool exists;
        string? existingPreview;
        try
        {
            (exists, existingPreview) = await TryGetExistingAsync(client, secretName, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SpeContainerIdKvWriteResult.Failure(
                $"KV probe failed BEFORE any write attempted: {ex.GetType().Name}: {ex.Message}. " +
                "Verify the L2 UAMI credential chain (DefaultAzureCredential) is reachable and has 'Get secret' " +
                $"on vault '{request.TargetKeyVaultName}'.");
        }

        if (exists && request.UpgradeMode)
        {
            _logger.LogInformation(
                "H8 KV writer: SKIP (never-rotate; container=data) {SecretName} on vault {Vault} " +
                "(upgrade mode + already exists) — customer={CustomerId}",
                secretName, request.TargetKeyVaultName, request.CustomerId);
            return new SpeContainerIdKvWriteResult.SkippedAlreadyPresent(existingPreview ?? "(empty)");
        }

        try
        {
            _logger.LogInformation(
                "H8 KV writer: WRITE {SecretName} on vault {Vault} (customer={CustomerId})",
                secretName, request.TargetKeyVaultName, request.CustomerId);
            await WithTimeoutAsync(
                ct => client.SetSecretAsync(secretName, request.ContainerTypeId, ct),
                _options.KvOperationTimeout,
                cancellationToken).ConfigureAwait(false);
            return new SpeContainerIdKvWriteResult.Wrote();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new SpeContainerIdKvWriteResult.Failure($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<(bool Exists, string? Preview)> TryGetExistingAsync(
        SecretClient client, string secretName, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.KvOperationTimeout);
        try
        {
            var response = await client.GetSecretAsync(secretName, version: null, timeoutCts.Token)
                .ConfigureAwait(false);
            return (true, PreviewValue(response.Value.Value));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return (false, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"SecretClient.GetSecretAsync invocation timed out after {_options.KvOperationTimeout}.");
        }
    }

    private static string PreviewValue(string? value)
        => string.IsNullOrEmpty(value) ? "(empty)" : (value.Length <= 8 ? value : value[..8] + "...");

    private static async Task WithTimeoutAsync(
        Func<CancellationToken, Task> action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await action(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"SecretClient invocation timed out after {timeout}.");
        }
    }
}
