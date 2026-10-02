// -----------------------------------------------------------------------------
// SecretClientKvWriter.cs
//
// Production <see cref="IKvSecretsWriter"/> — task 125 (Wave G-2, Option D
// hybrid) SDK port of <see cref="AzCliKvSecretsWriter"/> (shelled out to
// `az keyvault secret set/show/delete`; RETIRED, kept on disk unregistered —
// see that file's retirement banner). Uses
// Azure.Security.KeyVault.Secrets.SecretClient
// (SetSecretAsync/GetSecretAsync/StartDeleteSecretAsync) under
// DefaultAzureCredential pinned to the L2 UAMI — parity with
// <see cref="Preflight.KeyVaultCertBootstrapProbe"/> (task 120), which
// verified the installed 4.11.0 package's SecretClient shape via reflection
// before writing that file; this port reuses the SAME ground-truthed shapes
// (SetSecretAsync(name,value,ct), GetSecretAsync(name,version,ct) throwing
// RequestFailedException with Status==404 for a missing secret,
// StartDeleteSecretAsync(name,ct) returning a DeleteSecretOperation whose
// initial response already reflects the soft-delete — no WaitForCompletionAsync
// needed for H4's purposes, parity with the retired CLI's synchronous
// `az keyvault secret delete` semantics).
//
// "account show" REPLACEMENT (per task 125 POML <prompt>): the retired
// writer's whole-invocation prerequisite probe (`az account show`) verified
// the auth chain was live BEFORE any per-entry KV work started. The SDK
// equivalent is <see cref="ArmClient.GetDefaultSubscriptionAsync"/> — ground-
// truthed via reflection against the installed Azure.ResourceManager 1.14.0
// package to confirm it performs a genuine ARM round-trip (this project's
// ArmClient instances are constructed WITHOUT an explicit defaultSubscriptionId
// — see Worker/Program.cs's factory-lambda registration comment for
// ArmKeyVaultRefProbe/ArmDeploymentRunner — so GetDefaultSubscriptionAsync
// must discover the subscription via a live `GET /subscriptions` call, the
// closest 1:1 SDK equivalent to `az account show`'s "is my credential chain
// working" check). A RequestFailedException here fails BEFORE any KV write —
// same Resumable-no-partial-state guarantee as the retired probe.
//
// VALUE RESOLUTION (task 126, Wave G-2 Batch G-2C — H4 real-values
// correctness gate): value resolution is now DELEGATED to
// <see cref="IKvSecretValueResolver"/> (KvSecretValueResolver.cs) — the
// deterministic non-functional placeholder string this file previously
// always returned (see KvSecretValueResolver.cs's file header for the exact
// retired format) is gone. This writer still owns ALL
// control-flow decisions (existence checks, rotation-safety, the FIC
// omit-list, and the FromBicepOutput skip-if-already-provisioned guard);
// the resolver ONLY answers "what value should I write" or "I cannot,
// here's why" for a single entry. No entry in this writer special-cases
// `BFF-API-ClientSecret` by name — per spec.md FR-39's pluggability
// contract, every manifest entry (including that one) flows through the
// SAME generic Upsert/Delete/rotation-safe path; the FIC-omit mechanism is
// driven entirely by <see cref="KvSecretWriteRequest.OmitCanonicalNames"/>
// (a run-parameter-supplied SET, i.e. DATA), never a hardcoded name check.
// -----------------------------------------------------------------------------

using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;

/// <summary>
/// Calls Azure.Security.KeyVault.Secrets.SecretClient for each manifest entry.
/// Constructed with a <see cref="TokenCredential"/> (SecretClient is built
/// per-vault-per-call, matching <see cref="Preflight.KeyVaultCertBootstrapProbe"/>'s
/// posture — the vault name is a per-run parameter) plus an <see cref="ArmClient"/>
/// for the whole-writer prerequisite probe. Tests inject a
/// <see cref="SecretClientOptions"/> built against a fake
/// <see cref="Azure.Core.Pipeline.HttpClientTransport"/> so the SDK's own
/// request/response marshaling runs unmodified — never a
/// Mock&lt;HttpMessageHandler&gt;, never a wrapper mock of SecretClient itself.
/// </summary>
public sealed class SecretClientKvWriter : IKvSecretsWriter
{
    private static readonly TimeSpan PreflightProbeTimeout = TimeSpan.FromSeconds(15);

    private readonly TokenCredential _credential;
    private readonly ArmClient _armClient;
    private readonly SecretClientOptions? _clientOptions;
    private readonly IKvSecretValueResolver _resolver;
    private readonly KvSecretsPopulationOptions _options;
    private readonly ILogger<SecretClientKvWriter> _logger;

    /// <summary>Constructs the production writer bound to the shared UAMI-pinned credential.</summary>
    public SecretClientKvWriter(
        TokenCredential credential,
        ArmClient armClient,
        IKvSecretValueResolver resolver,
        IOptions<KvSecretsPopulationOptions> options,
        ILogger<SecretClientKvWriter> logger)
        : this(credential, armClient, clientOptions: null, resolver, options, logger)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/>.</summary>
    internal SecretClientKvWriter(
        TokenCredential credential,
        ArmClient armClient,
        SecretClientOptions? clientOptions,
        IKvSecretValueResolver resolver,
        IOptions<KvSecretsPopulationOptions> options,
        ILogger<SecretClientKvWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _credential = credential;
        _armClient = armClient;
        _clientOptions = clientOptions;
        _resolver = resolver;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<KvSecretsWriteOutcome> WriteAsync(
        KvSecretWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetKeyVaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SubscriptionId);

        // Whole-writer prerequisite probe — verify the ARM credential chain is
        // live BEFORE attempting any per-entry KV work. An unreachable
        // credential chain is a Failure (Resumable) not a per-entry partial-fail.
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(PreflightProbeTimeout);
            await _armClient.GetDefaultSubscriptionAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new KvSecretsWriteOutcome.Failure(
                $"ARM default-subscription probe timed out after {PreflightProbeTimeout} BEFORE any KV write attempted. " +
                "Verify the L2 UAMI credential chain (DefaultAzureCredential) is reachable.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new KvSecretsWriteOutcome.Failure(
                $"ARM default-subscription probe failed BEFORE any KV write attempted: {ex.GetType().Name}: {ex.Message}. " +
                "Verify the L2 UAMI has been granted a role on at least one subscription (DefaultAzureCredential chain).");
        }

        var vaultUri = new Uri($"https://{request.TargetKeyVaultName}.vault.azure.net/");
        var client = _clientOptions is null
            ? new SecretClient(vaultUri, _credential)
            : new SecretClient(vaultUri, _credential, _clientOptions);

        var results = new List<KvSecretWriteResult>(capacity: request.Entries.Count);

        foreach (var entry in request.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ExecuteEntryAsync(client, entry, request, cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        return new KvSecretsWriteOutcome.Success(results);
    }

    private async Task<KvSecretWriteResult> ExecuteEntryAsync(
        SecretClient client,
        KvSecretEntry entry,
        KvSecretWriteRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (entry.Operation == KvSecretOperation.Delete)
            {
                // BINDING belt-and-braces guard (code-review W1, 2026-08-17,
                // carried verbatim from the retired AzCliKvSecretsWriter). The
                // HANDLER's pre-check is the primary defense — if it fires the
                // writer is never invoked. Two independent gates raise the bar
                // (parity with ADR-039's dispatcher-vs-orchestrator double-gate).
                if (H4KvSecretsPopulationHandler.BindingNeverDeleteSecrets.Contains(entry.CanonicalName))
                {
                    return new KvSecretWriteResult(
                        entry.CanonicalName, KvSecretWriteAction.Failed,
                        $"BINDING never-delete guard: writer refuses Delete op for '{entry.CanonicalName}' — " +
                        "spec.md MUST rule + r3 handoff. Fleet-wide OBO + shared-lib Dataverse depend on this secret.");
                }

                _logger.LogInformation(
                    "H4 KV writer: DELETE {SecretName} on vault {Vault} (customer={CustomerId})",
                    entry.CanonicalName, request.TargetKeyVaultName, request.CustomerId);
                await WithTimeoutAsync(
                    ct => client.StartDeleteSecretAsync(entry.CanonicalName, ct),
                    _options.KvOperationTimeout,
                    cancellationToken).ConfigureAwait(false);
                return new KvSecretWriteResult(entry.CanonicalName, KvSecretWriteAction.Deleted, null);
            }

            // Upsert branch — rotation-safe by default.

            // FIC-omit seam (task 126, spec.md FR-39). Manifest/run-parameter
            // driven — NEVER a hardcoded canonical-name check. Fires BEFORE
            // any KV call at all (no existence probe, no resolver call).
            if (request.OmitCanonicalNames.Contains(entry.CanonicalName))
            {
                _logger.LogInformation(
                    "H4 KV writer: OMIT {SecretName} on vault {Vault} " +
                    "(auth-v4 FIC-covered per run parameter; customer={CustomerId})",
                    entry.CanonicalName, request.TargetKeyVaultName, request.CustomerId);
                return new KvSecretWriteResult(entry.CanonicalName, KvSecretWriteAction.Omitted, null);
            }

            var exists = await SecretExistsAsync(client, entry.CanonicalName, _options.KvOperationTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (exists && request.UpgradeMode && !request.RotateExisting)
            {
                _logger.LogInformation(
                    "H4 KV writer: SKIP (rotation-safe) {SecretName} on vault {Vault} " +
                    "(upgrade mode + rotate=false + already exists) — customer={CustomerId}",
                    entry.CanonicalName, request.TargetKeyVaultName, request.CustomerId);
                return new KvSecretWriteResult(
                    entry.CanonicalName, KvSecretWriteAction.SkippedRotationSafe, null);
            }

            // FromBicepOutput entries are expected to already be provisioned
            // by the customer.bicep ARM deployment (H2a) — see
            // KvSecretValueResolver.cs's file-header mapping note. If the
            // secret already exists here (Bicep already wrote it, or a prior
            // run did), do NOT overwrite it with a resolver guess — skip,
            // even outside upgrade mode (a FRESH run's H2a step runs BEFORE
            // H4 in the same DAG, so "exists" here means "Bicep already
            // wrote it this run").
            if (entry.ValueSource == KvSecretValueSource.FromBicepOutput && exists)
            {
                _logger.LogInformation(
                    "H4 KV writer: SKIP (already provisioned by ARM deployment) {SecretName} on vault {Vault} " +
                    "(customer={CustomerId})",
                    entry.CanonicalName, request.TargetKeyVaultName, request.CustomerId);
                return new KvSecretWriteResult(
                    entry.CanonicalName, KvSecretWriteAction.SkippedRotationSafe, null);
            }

            var resolution = await _resolver.ResolveAsync(entry, request, cancellationToken).ConfigureAwait(false);
            if (resolution is KvSecretValueResolution.Failed resolutionFailure)
            {
                return new KvSecretWriteResult(
                    entry.CanonicalName, KvSecretWriteAction.Failed, resolutionFailure.Diagnostic);
            }
            var value = ((KvSecretValueResolution.Resolved)resolution).Value;

            // The cleartext value ONLY passes through SetSecretAsync's request
            // body here. It is not logged, not returned in the result record,
            // and not persisted to Cosmos anywhere.
            _logger.LogInformation(
                "H4 KV writer: WRITE {SecretName} on vault {Vault} (customer={CustomerId} rotate={Rotate})",
                entry.CanonicalName, request.TargetKeyVaultName, request.CustomerId, request.RotateExisting);
            await WithTimeoutAsync(
                ct => client.SetSecretAsync(entry.CanonicalName, value, ct),
                _options.KvOperationTimeout,
                cancellationToken).ConfigureAwait(false);
            return new KvSecretWriteResult(entry.CanonicalName, KvSecretWriteAction.Wrote, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Per-entry failure — return a Failed result WITHOUT the value.
            // ErrorMessage carries the SDK exception signal only.
            return new KvSecretWriteResult(entry.CanonicalName, KvSecretWriteAction.Failed,
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<bool> SecretExistsAsync(
        SecretClient client, string secretName, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await client.GetSecretAsync(secretName, version: null, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"SecretClient.GetSecretAsync invocation timed out after {timeout}.");
        }
    }

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
