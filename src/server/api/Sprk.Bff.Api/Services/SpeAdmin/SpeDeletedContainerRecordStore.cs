using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// Who owned a container when the BFF soft-deleted it: <c>containerId</c>, this stamp's customer id (the task-227d
/// ownership marker), the task-165 business-unit stamp, the config it was deleted through, its container type and when.
/// The ONLY source the SPE admin recycle bin trusts for a deleted container (#1652).
/// </summary>
public sealed record SpeDeletedContainerRecord(
    string ContainerId,
    string CustomerId,
    Guid BusinessUnitId,
    Guid ConfigId,
    string ContainerTypeId,
    DateTimeOffset DeletedAt);

/// <summary>
/// The store of <see cref="SpeDeletedContainerRecord"/>s: one document per deleted container in the stamp's EXISTING
/// per-customer Cosmos DB <c>memory</c> container (#1652, owner decision "protected B" 2026-10-12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a record at all.</b> Graph discards a container's <c>customProperties</c> on soft delete (measured on dev
/// 2026-10-11/12, #1652): a deleted container carries neither the ownership marker nor the business-unit stamp, so
/// nothing ON the container can say whose it is. The BFF writes this record immediately before it soft-deletes; a
/// container with no record is shown to and acted on by no one (fail closed) — that includes every container deleted
/// outside the BFF or before this shipped.
/// </para>
/// <para>
/// <b>Why this store (CLAUDE.md §11, reuse first).</b> The customer must not be able to write it: a forged record could
/// restore or purge another customer's container, so a plain Dataverse row is out (customers write Dataverse —
/// <c>SpeContainerOwnershipGuard</c> remarks). The stamp's Cosmos DB account is per customer
/// (<c>infrastructure/bicep/customer.bicep</c>), has local (key) auth disabled, and grants data-plane access only to the
/// BFF's identities (<c>modules/cosmos-db.bicep</c>, Built-in Data Contributor) — no customer principal can write it.
/// The <c>memory</c> container is already shared by document type (workspace tabs, pinned context; every reader filters
/// on <c>documentType</c> within a partition), so this adds a document type, not a resource: no Bicep, no new
/// container, no Dataverse column, no secret. Redis was rejected (not durable).
/// </para>
/// <para>
/// <b>Isolation inside the container.</b> Records live in their own partition (<see cref="PartitionValue"/>, never a
/// tenant id — other documents are partitioned by the caller's <c>tid</c>, a GUID) under an id derived from the
/// container id (<see cref="DocumentId"/>). A read accepts a document only when its <c>documentType</c>, its own
/// <c>containerId</c> and every field are present and well-formed; anything else is "no record".
/// </para>
/// <para>
/// <b>Lifetime.</b> Each document carries a per-item <c>ttl</c> of <see cref="RecordTtl"/> — longer than SharePoint
/// Embedded's 93-day recycle-bin retention, overriding the container's 90-day default — and is removed after a restore
/// or purge through the BFF. A record that outlives its container is inert: every use also requires Graph to list the id
/// as deleted for the config's container type.
/// </para>
/// <para>
/// <b>Faults.</b> A read or write that gets no answer throws <see cref="SpeDeletedContainerRecordUnavailableException"/>:
/// a read fault refuses (503), a write fault stops the soft delete. Only a 404 is "no record".
/// </para>
/// <para>Methods are virtual only so tests can supply an in-memory subclass.</para>
/// </remarks>
public class SpeDeletedContainerRecordStore
{
    /// <summary>The existing Cosmos container the records live in (reused — see the remarks).</summary>
    internal const string CosmosContainerName = "memory";

    /// <summary>The <c>documentType</c> discriminator of a record.</summary>
    internal const string DocumentTypeValue = "spe-deleted-container-ownership";

    /// <summary>The partition every record lives in (the container's partition key path is <c>/tenantId</c>).</summary>
    internal const string PartitionValue = "sprk:spe-deleted-container-ownership";

    /// <summary>Per-item time to live: past SharePoint Embedded's 93-day recycle-bin retention.</summary>
    internal static readonly TimeSpan RecordTtl = TimeSpan.FromDays(100);

    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly ILogger<SpeDeletedContainerRecordStore> _logger;

    public SpeDeletedContainerRecordStore(
        CosmosClient cosmosClient,
        IConfiguration configuration,
        ILogger<SpeDeletedContainerRecordStore> logger)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentNullException.ThrowIfNull(configuration);
        _cosmosClient = cosmosClient;
        // The same database AiPersistenceModule resolves for the rest of the stamp's Cosmos state.
        _databaseName = configuration["CosmosPersistence:DatabaseName"] ?? "spaarke-ai";
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Writes (upserts) the record of a container about to be soft-deleted. Throws
    /// <see cref="SpeDeletedContainerRecordUnavailableException"/> when it cannot — the caller must not delete.
    /// </summary>
    public virtual async Task WriteAsync(SpeDeletedContainerRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(record.ContainerId);

        var document = RecordDocument.From(record);
        try
        {
            await GetContainer()
                .UpsertItemAsync(document, new PartitionKey(PartitionValue), cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new SpeDeletedContainerRecordUnavailableException(
                $"The ownership record of container '{record.ContainerId}' could not be written.", ex);
        }

        _logger.LogInformation(
            "Recorded the ownership of container {ContainerId} before its soft delete (customer {CustomerId}, business unit {BusinessUnitId}, config {ConfigId}).",
            record.ContainerId, record.CustomerId, record.BusinessUnitId, record.ConfigId);
    }

    /// <summary>
    /// The record of <paramref name="containerId"/>, or null when there is none or the document is not a well-formed
    /// record of exactly that container. Throws <see cref="SpeDeletedContainerRecordUnavailableException"/> when the
    /// store gives no answer — the caller refuses, never reads that as "no record".
    /// </summary>
    public virtual async Task<SpeDeletedContainerRecord?> ReadAsync(string containerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        RecordDocument? document;
        try
        {
            var response = await GetContainer()
                .ReadItemAsync<RecordDocument>(DocumentId(containerId), new PartitionKey(PartitionValue), cancellationToken: ct)
                .ConfigureAwait(false);
            document = response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new SpeDeletedContainerRecordUnavailableException(
                $"The ownership record of container '{containerId}' could not be read.", ex);
        }

        var record = document?.ToRecord(containerId);
        if (record is null)
        {
            _logger.LogWarning(
                "The ownership record document of container {ContainerId} is not a well-formed record of that container — ignored (no record).",
                containerId);
        }

        return record;
    }

    /// <summary>
    /// Removes the record of <paramref name="containerId"/> (after a restore or purge). Best effort: a record left
    /// behind is inert (Graph no longer lists the id as deleted) and expires with its <c>ttl</c>; a failure is logged.
    /// </summary>
    public virtual async Task RemoveAsync(string containerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        try
        {
            await GetContainer()
                .DeleteItemAsync<RecordDocument>(DocumentId(containerId), new PartitionKey(PartitionValue), cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone.
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "The ownership record of container {ContainerId} could not be removed; it is inert and expires with its ttl.",
                containerId);
        }
    }

    /// <summary>
    /// The document id of a container's record: a fixed prefix and the SHA-256 of the container id, so no container id
    /// character can collide with Cosmos id rules or with another document type's ids.
    /// </summary>
    internal static string DocumentId(string containerId) =>
        "spedc-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(containerId))).ToLowerInvariant();

    private Container GetContainer() => _cosmosClient.GetContainer(_databaseName, CosmosContainerName);

    /// <summary>The Cosmos document (System.Text.Json — the stamp's CosmosClient honours these names).</summary>
    internal sealed class RecordDocument
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("tenantId")] public string? PartitionKey { get; set; }
        [JsonPropertyName("documentType")] public string? DocumentType { get; set; }
        [JsonPropertyName("containerId")] public string? ContainerId { get; set; }
        [JsonPropertyName("customerId")] public string? CustomerId { get; set; }
        [JsonPropertyName("businessUnitId")] public Guid? BusinessUnitId { get; set; }
        [JsonPropertyName("configId")] public Guid? ConfigId { get; set; }
        [JsonPropertyName("containerTypeId")] public string? ContainerTypeId { get; set; }
        [JsonPropertyName("deletedAt")] public DateTimeOffset? DeletedAt { get; set; }
        [JsonPropertyName("ttl")] public int? Ttl { get; set; }

        internal static RecordDocument From(SpeDeletedContainerRecord record) => new()
        {
            Id = DocumentId(record.ContainerId),
            PartitionKey = PartitionValue,
            DocumentType = DocumentTypeValue,
            ContainerId = record.ContainerId,
            CustomerId = record.CustomerId,
            BusinessUnitId = record.BusinessUnitId,
            ConfigId = record.ConfigId,
            ContainerTypeId = record.ContainerTypeId,
            DeletedAt = record.DeletedAt,
            Ttl = (int)RecordTtl.TotalSeconds,
        };

        /// <summary>The record, only when this is a complete record of exactly <paramref name="containerId"/>.</summary>
        internal SpeDeletedContainerRecord? ToRecord(string containerId)
        {
            if (!string.Equals(DocumentType, DocumentTypeValue, StringComparison.Ordinal)
                || !string.Equals(Id, DocumentId(containerId), StringComparison.Ordinal)
                || !string.Equals(ContainerId, containerId, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(CustomerId)
                || BusinessUnitId is not { } unit || unit == Guid.Empty
                || ConfigId is not { } config || config == Guid.Empty
                || string.IsNullOrWhiteSpace(ContainerTypeId)
                || DeletedAt is not { } deletedAt)
            {
                return null;
            }

            return new SpeDeletedContainerRecord(containerId, CustomerId, unit, config, ContainerTypeId, deletedAt);
        }
    }
}

/// <summary>The deleted-container record store gave no answer: refuse (read) or do not delete (write).</summary>
public sealed class SpeDeletedContainerRecordUnavailableException(string message, Exception inner)
    : Exception(message, inner);
