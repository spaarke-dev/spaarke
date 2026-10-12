using System.Collections.Concurrent;
using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Services.SpeAdmin;
using RecordDocument = Sprk.Bff.Api.Services.SpeAdmin.SpeDeletedContainerRecordStore.RecordDocument;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// The REAL <see cref="SpeDeletedContainerRecordStore"/> (#1652) over an in-memory Cosmos <c>memory</c> container,
/// substituted at the SDK's <see cref="Container"/> boundary (the <c>PinnedContextRepositoryTests</c> pattern): the
/// production write → read → validate → remove path runs unchanged; only the network is replaced. A document is kept
/// under (partition, id), so a read in another partition finds nothing, as in Cosmos.
/// </summary>
public sealed class FakeSpeDeletedContainerRecords
{
    private readonly ConcurrentDictionary<(string Partition, string Id), RecordDocument> _documents = new();

    public FakeSpeDeletedContainerRecords(ILogger<SpeDeletedContainerRecordStore>? logger = null)
    {
        var container = new Mock<Container>();

        container
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<RecordDocument>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((RecordDocument document, PartitionKey? partition, ItemRequestOptions? _, CancellationToken _) =>
            {
                Calls.Enqueue(new Call("Upsert", document.ContainerId ?? "", partition?.ToString() ?? ""));
                if (FaultWrites)
                {
                    throw new CosmosException("Simulated Cosmos outage.", HttpStatusCode.ServiceUnavailable, 0, string.Empty, 0);
                }

                _documents[(partition?.ToString() ?? "", document.Id ?? "")] = document;
                return Task.FromResult(Response(document));
            });

        container
            .Setup(c => c.ReadItemAsync<RecordDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((string id, PartitionKey partition, ItemRequestOptions? _, CancellationToken _) =>
            {
                Calls.Enqueue(new Call("Read", id, partition.ToString()));
                if (FaultReads)
                {
                    throw new CosmosException("Simulated Cosmos outage.", HttpStatusCode.ServiceUnavailable, 0, string.Empty, 0);
                }

                return _documents.TryGetValue((partition.ToString(), id), out var document)
                    ? Task.FromResult(Response(document))
                    : throw new CosmosException("Not Found", HttpStatusCode.NotFound, 0, string.Empty, 0);
            });

        container
            .Setup(c => c.DeleteItemAsync<RecordDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((string id, PartitionKey partition, ItemRequestOptions? _, CancellationToken _) =>
            {
                Calls.Enqueue(new Call("Delete", id, partition.ToString()));
                return _documents.TryRemove((partition.ToString(), id), out var document)
                    ? Task.FromResult(Response(document))
                    : throw new CosmosException("Not Found", HttpStatusCode.NotFound, 0, string.Empty, 0);
            });

        var client = new Mock<CosmosClient>();
        client.Setup(c => c.GetContainer(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string database, string name) =>
            {
                ContainersAsked.Enqueue((database, name));
                return container.Object;
            });

        Store = new SpeDeletedContainerRecordStore(
            client.Object,
            new ConfigurationBuilder().Build(),
            logger ?? NullLogger<SpeDeletedContainerRecordStore>.Instance);
    }

    /// <summary>The real store, over this fake.</summary>
    public SpeDeletedContainerRecordStore Store { get; }

    /// <summary>Every Cosmos item call, in order.</summary>
    public ConcurrentQueue<Call> Calls { get; } = new();

    /// <summary>Every (database, container) the store asked for.</summary>
    public ConcurrentQueue<(string Database, string Container)> ContainersAsked { get; } = new();

    /// <summary>Reads answer 503 (Cosmos unreachable).</summary>
    public bool FaultReads { get; set; }

    /// <summary>Writes answer 503 (Cosmos unreachable).</summary>
    public bool FaultWrites { get; set; }

    /// <summary>The partition every genuine record lives in, as the SDK prints it.</summary>
    public static string RecordPartition => new PartitionKey(SpeDeletedContainerRecordStore.PartitionValue).ToString();

    /// <summary>Seeds a genuine record (as the BFF would have written it before the soft delete).</summary>
    public void Seed(string containerId, string customerId, Guid businessUnitId, string containerTypeId, Guid? configId = null) =>
        _documents[(RecordPartition, SpeDeletedContainerRecordStore.DocumentId(containerId))] = RecordDocument.From(
            new SpeDeletedContainerRecord(
                containerId, customerId, businessUnitId, configId ?? Guid.Parse("00000000-0000-0000-0000-00000000c0f1"),
                containerTypeId, DateTimeOffset.UtcNow.AddDays(-1)));

    /// <summary>Puts an arbitrary document at <paramref name="containerId"/>'s record id (a forged or tampered record).</summary>
    internal void Put(string containerId, RecordDocument document, string? partition = null) =>
        _documents[(partition ?? RecordPartition, SpeDeletedContainerRecordStore.DocumentId(containerId))] = document;

    /// <summary>The stored document of <paramref name="containerId"/>'s record, or null.</summary>
    internal RecordDocument? DocumentOf(string containerId) =>
        _documents.TryGetValue((RecordPartition, SpeDeletedContainerRecordStore.DocumentId(containerId)), out var document)
            ? document
            : null;

    /// <summary>The valid record of <paramref name="containerId"/>, or null.</summary>
    public SpeDeletedContainerRecord? RecordOf(string containerId) => DocumentOf(containerId)?.ToRecord(containerId);

    /// <summary>The calls of one operation (Upsert / Read / Delete).</summary>
    public IReadOnlyList<Call> CallsOf(string operation) => Calls.Where(c => c.Operation == operation).ToList();

    public void Reset()
    {
        _documents.Clear();
        Calls.Clear();
        ContainersAsked.Clear();
        FaultReads = false;
        FaultWrites = false;
    }

    private static ItemResponse<RecordDocument> Response(RecordDocument document)
    {
        var response = new Mock<ItemResponse<RecordDocument>>();
        response.Setup(r => r.Resource).Returns(document);
        return response.Object;
    }

    /// <summary>One Cosmos item call: the operation, the document id (or container id for an upsert) and the partition.</summary>
    public sealed record Call(string Operation, string Key, string Partition);
}
