namespace Sprk.Bff.Api.Services.RecordMatching;

/// <summary>
/// Service for syncing Dataverse records to Azure AI Search index.
/// Supports bulk initial load and incremental updates.
/// </summary>
public interface IDataverseIndexSyncService
{
    /// <summary>
    /// Perform a full sync of all supported Dataverse record types to the search index.
    /// This clears existing documents and re-indexes everything.
    /// </summary>
    /// <param name="recordTypes">Optional list of record types to sync. If null, syncs all supported types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sync result with statistics.</returns>
    Task<IndexSyncResult> BulkSyncAsync(IEnumerable<string>? recordTypes = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Perform an incremental sync of records modified since the last sync.
    /// </summary>
    /// <param name="since">Only sync records modified after this timestamp.</param>
    /// <param name="recordTypes">Optional list of record types to sync. If null, syncs all supported types.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sync result with statistics.</returns>
    Task<IndexSyncResult> IncrementalSyncAsync(DateTimeOffset since, IEnumerable<string>? recordTypes = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync a single Dataverse record to the search index.
    /// Used for real-time updates when a record changes.
    /// </summary>
    /// <param name="entityName">Dataverse entity logical name (e.g., "sprk_matter").</param>
    /// <param name="recordId">Dataverse record GUID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SyncRecordAsync(string entityName, Guid recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a record from the search index.
    /// Used when a Dataverse record is deleted.
    /// </summary>
    /// <param name="entityName">Dataverse entity logical name.</param>
    /// <param name="recordId">Dataverse record GUID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveRecordAsync(string entityName, Guid recordId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the current sync status and statistics.
    /// </summary>
    Task<IndexSyncStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of an index sync operation.
/// </summary>
public class IndexSyncResult
{
    public bool Success { get; set; }
    public int RecordsProcessed { get; set; }
    public int RecordsIndexed { get; set; }
    public int RecordsFailed { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, int> RecordsByType { get; set; } = new();
}

/// <summary>
/// Current status of the search index.
/// </summary>
public class IndexSyncStatus
{
    public string IndexName { get; set; } = string.Empty;
    public long DocumentCount { get; set; }
    public DateTimeOffset? LastSyncTime { get; set; }
    public Dictionary<string, int> DocumentsByType { get; set; } = new();
    public bool IsHealthy { get; set; }
}
