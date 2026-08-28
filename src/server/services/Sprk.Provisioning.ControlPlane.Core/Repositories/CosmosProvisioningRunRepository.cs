// -----------------------------------------------------------------------------
// CosmosProvisioningRunRepository.cs
//
// Cosmos-backed implementation of <see cref="IProvisioningRunRepository"/>
// (task 037). One instance per HTTP request (Scoped); the shared
// <see cref="CosmosClient"/> singleton is thread-safe.
//
// PARTITION-KEY DISCIPLINE (§4D I3 / FR-30):
//   Every Cosmos SDK call in this file passes an explicit
//   <c>new PartitionKey(customerId)</c>. Grep-friendly: every
//   `_container.` invocation is immediately followed by a `new PartitionKey(`
//   argument. The Wave C6 ArchTest (`ITenantIsolationTests.I3_...`) scans
//   Cosmos SDK call sites for the presence of a `PartitionKey` argument;
//   this file is written to pass by construction.
//
// CONCURRENCY (FR-23 I5):
//   <see cref="ReplaceRunAsync"/> passes <c>IfMatchEtag</c> and translates
//   Cosmos <c>412 PreconditionFailed</c> into a typed
//   <see cref="ReplaceRunResult.Conflict"/> carrying the current stored
//   document (via a follow-up point-read). The endpoint layer (Wave C5)
//   maps this to HTTP 409 with the winning state. No CosmosException leaks
//   past this repository on the concurrency path.
//
// SECRET-STORAGE INVARIANT (§4D — spec.md FR-27 acceptance):
//   Cleartext secrets are structurally impossible: the
//   <see cref="KeyVaultSecretRef"/> POCO has no `Value` string field, so
//   there is no property this repository could serialize into Cosmos that
//   would carry a cleartext value. Task 025's ArchTest verifies the type
//   shape independently.
// -----------------------------------------------------------------------------

using System.Net;
using Microsoft.Azure.Cosmos;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Repositories;

/// <inheritdoc cref="IProvisioningRunRepository"/>
public sealed class CosmosProvisioningRunRepository : IProvisioningRunRepository
{
    private readonly Container _container;
    private readonly ILogger<CosmosProvisioningRunRepository> _logger;

    /// <summary>
    /// Initializes the repository over the given <see cref="CosmosClient"/>
    /// + database/container names (bound by <see cref="Modules.CosmosModule"/>).
    /// </summary>
    /// <param name="cosmosClient">Singleton Cosmos client (owns the connection pool).</param>
    /// <param name="databaseName">Database name (default <c>spaarke-provisioning</c>).</param>
    /// <param name="containerName">Container name (default <c>runs</c>, partition key <c>/customerId</c>).</param>
    /// <param name="logger">Logger for diagnostic messages (identifiers/counts only — never document bodies).</param>
    public CosmosProvisioningRunRepository(
        CosmosClient cosmosClient,
        string databaseName,
        string containerName,
        ILogger<CosmosProvisioningRunRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(logger);

        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ProvisioningRunReadResult?> ReadRunAsync(
        string customerId,
        string runId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        try
        {
            // §4D I3: explicit PartitionKey — never a cross-partition read.
            var response = await _container.ReadItemAsync<ProvisioningRun>(
                id: runId,
                partitionKey: new PartitionKey(customerId),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return new ProvisioningRunReadResult(response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // 404 is expected during initial-provision handshake and reconciler crash-recovery scan.
            _logger.LogDebug(
                "ProvisioningRun not found (customerId={CustomerId}, runId={RunId})",
                customerId, runId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<ProvisioningRunReadResult> CreateRunAsync(
        ProvisioningRun run,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.RunId);

        try
        {
            // §4D I3: explicit PartitionKey — never rely on the SDK to sniff it from the document.
            var response = await _container.CreateItemAsync(
                item: run,
                partitionKey: new PartitionKey(run.CustomerId),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Created ProvisioningRun (customerId={CustomerId}, runId={RunId}, status={Status})",
                run.CustomerId, run.RunId, run.Status);

            return new ProvisioningRunReadResult(response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // Duplicate id inside the customer's partition. Cosmos maps
            // this to 409 Conflict; we translate to InvalidOperationException
            // so callers who intended CREATE-only semantics get a signal
            // that is distinct from the ETag-based Conflict on Replace.
            throw new InvalidOperationException(
                $"A ProvisioningRun with runId '{run.RunId}' already exists in customer '{run.CustomerId}'. " +
                "Use ReplaceRunAsync (with the current ETag) for updates.",
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task<ReplaceRunResult> ReplaceRunAsync(
        ProvisioningRun run,
        string ifMatchEtag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(run.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ifMatchEtag);

        var partitionKey = new PartitionKey(run.CustomerId);
        var requestOptions = new ItemRequestOptions { IfMatchEtag = ifMatchEtag };

        try
        {
            // §4D I3: explicit PartitionKey. FR-23 I5: IfMatchEtag drives the concurrency guard.
            var response = await _container.ReplaceItemAsync(
                item: run,
                id: run.RunId,
                partitionKey: partitionKey,
                requestOptions: requestOptions,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return new ReplaceRunResult.Success(response.Resource, response.ETag);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            // FR-23 I5: ETag mismatch -> re-read the current state so the caller
            // can decide (endpoint layer maps to HTTP 409 with winning runId).
            _logger.LogInformation(
                "ReplaceRun ETag conflict (customerId={CustomerId}, runId={RunId}, ifMatch={IfMatch}); re-reading current state",
                run.CustomerId, run.RunId, ifMatchEtag);

            var current = await ReadRunAsync(run.CustomerId, run.RunId, cancellationToken).ConfigureAwait(false);
            if (current is null)
            {
                // Race: document was deleted after our PreconditionFailed but before
                // the follow-up read. Distinguish from a stale-ETag conflict so
                // callers can decide whether to recreate.
                return new ReplaceRunResult.NotFound();
            }
            return new ReplaceRunResult.Conflict(current);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Document was deleted between the caller's read and this write.
            return new ReplaceRunResult.NotFound();
        }
    }
}
