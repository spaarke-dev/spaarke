// -----------------------------------------------------------------------------
// CosmosActiveRunScanner.cs
//
// L2 CONTROL-PLANE Cosmos implementation of the state-reconciler's
// cross-partition scan (task 058, Wave C5).
//
// See <see cref="IActiveRunScanner"/> for the rationale on why this is the
// only cross-partition read in the L2 project + why it lives on a dedicated
// interface rather than on <see cref="Repositories.IProvisioningRunRepository"/>.
//
// CROSS-PARTITION WAIVER (§4D I3 / FR-30 — Wave C6 ArchTest task 064):
//   The <see cref="QueryActiveRunsAsync"/> method is annotated with
//   <see cref="AllowCrossPartitionScanAttribute"/>. The task 064 ArchTest
//   pattern-matches the attribute BY NAME (regex over source syntax) rather
//   than resolving the attribute type, so the annotation is what the
//   fitness function actually reads. Every waiver carries a non-empty reason
//   citing the design section that justifies it — this one cites design.md
//   §4.2 (reconciler poll) + spec.md FR-22.
//
// LOCAL ATTRIBUTE DECLARATION (see bottom of file):
//   The canonical <c>Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute</c>
//   (introduced by task 064) is intentionally NOT referenced here — pulling
//   in Spaarke.Core transitively adds Spaarke.Dataverse → the full Dataverse
//   SDK (Microsoft.PowerPlatform.Dataverse.Client 1.2.26) which would
//   materially inflate the L2 publish size for a single attribute. The
//   attribute's own docstring explicitly permits a same-named local
//   declaration (the ArchTest matches by attribute NAME, not by type
//   resolution). The local declaration mirrors the canonical type's
//   constructor + Reason semantics so the reviewer contract still holds.
// -----------------------------------------------------------------------------

using Microsoft.Azure.Cosmos;
using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Reconciler;

/// <inheritdoc cref="IActiveRunScanner"/>
public sealed class CosmosActiveRunScanner : IActiveRunScanner
{
    // SQL projection: `status` is written by ProvisioningRun as its serialised
    // enum name ("Running" / "WaitingOnGate" / etc.), courtesy of the
    // JsonStringEnumConverter attribute on RunStatus. This filter is index-
    // friendly (composite range index on `status` per cosmos-provisioning.bicep
    // §6.2 index policy).
    private const string ActiveRunsQuery =
        "SELECT * FROM c WHERE c.status IN ('Running', 'WaitingOnGate')";

    // Page size for the cross-partition scan. 100 balances round-trip count
    // (fewer larger pages) against per-request RU (very large pages cost
    // proportionally more). Reconciler cadence (5s) makes latency non-critical.
    private const int PageMaxItemCount = 100;

    private readonly Container _container;
    private readonly ILogger<CosmosActiveRunScanner> _logger;

    /// <summary>
    /// Constructs the scanner over the given Cosmos <see cref="CosmosClient"/>
    /// + database/container names (default binding by
    /// <see cref="ReconcilerModule"/> reuses <see cref="Modules.CosmosModule.DefaultDatabaseName"/>
    /// + <see cref="Modules.CosmosModule.DefaultContainerName"/> so both the
    /// repository and this scanner target the same physical container).
    /// </summary>
    public CosmosActiveRunScanner(
        CosmosClient cosmosClient,
        string databaseName,
        string containerName,
        ILogger<CosmosActiveRunScanner> logger)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentNullException.ThrowIfNull(logger);

        _container = cosmosClient.GetContainer(databaseName, containerName);
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Cross-partition scan is intentional here per design.md §4.2 (the
    /// reconciler is the ONE place in L2 that reads across all customer
    /// partitions). Attribute is required by the Wave-C6 tenant-isolation
    /// ArchTest (task 064 / I3) — it identifies this call site as a
    /// reviewed exception rather than a bug.
    /// </remarks>
    [AllowCrossPartitionScan("design.md §4.2 handler-execution model — state-reconciler polls Cosmos every 5s across all customerId partitions to advance the DAG; spec.md FR-22; task 058.")]
    public async Task<IReadOnlyList<ProvisioningRun>> QueryActiveRunsAsync(CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(ActiveRunsQuery);
        var requestOptions = new QueryRequestOptions
        {
            MaxItemCount = PageMaxItemCount,
        };

        var results = new List<ProvisioningRun>();
        using var iterator = _container.GetItemQueryIterator<ProvisioningRun>(query, requestOptions: requestOptions);

        while (iterator.HasMoreResults)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await iterator.ReadNextAsync(cancellationToken).ConfigureAwait(false);
            results.AddRange(page.Resource);
        }

        _logger.LogDebug(
            "Reconciler active-run scan returned {ActiveRunCount} runs (status in Running/WaitingOnGate).",
            results.Count);

        return results;
    }
}

/// <summary>
/// L2-local mirror of <c>Spaarke.Core.Attributes.AllowCrossPartitionScanAttribute</c>
/// (task 064). Same NAME so the I3 ArchTest matches by regex; not the same
/// TYPE so we avoid pulling in Spaarke.Core → Spaarke.Dataverse → the
/// Dataverse SDK for a single attribute. The reviewer contract is identical:
/// every usage MUST cite a design section, spec FR, task ID, or ADR that
/// justifies the cross-partition waiver.
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
internal sealed class AllowCrossPartitionScanAttribute : Attribute
{
    /// <summary>Concise waiver justification — required.</summary>
    public string Reason { get; }

    /// <summary>Constructs the waiver; <paramref name="reason"/> must be non-empty.</summary>
    public AllowCrossPartitionScanAttribute(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "AllowCrossPartitionScan requires a non-empty reason citing the design section, " +
                "spec FR, task ID, or ADR that justifies the waiver.",
                nameof(reason));
        }
        Reason = reason;
    }
}
