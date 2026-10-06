namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013) through which document CRUD code keeps the RAG index true to a document whose file was MOVED
/// to another SharePoint Embedded item — unified-access-control-r2 task 166 f1-v1, owner round 37 item 1.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> <c>DocumentContainerRelocator</c> (the legacy migration and every Make Secure move) copies a
/// document's file into its derived container and re-points the row at the COPY, a new item id. The file pipeline keys
/// chunks <c>{speFileId}_{index}</c> and stores the item id on each chunk, so without this the document's chunks keep
/// naming the old item: search returns that stale <c>speFileId</c> next to the row's new drive, a per-result action asks
/// for an item that is not in that drive, and a plain re-index would only ADD the new item's chunks beside the old ones.</para>
/// <para><b>ADR-013.</b> The relocator is CRUD code (<c>Services/Documents</c>); it injects only this interface — never
/// <c>IRagService</c>, <c>IPostUploadIndexingEnqueuer</c> or another AI-internal type. No existing PublicContracts facade
/// indexes or un-indexes a file (owner round 37: extend one if it fits — none does), so this is one interface with ONE
/// method.</para>
/// </remarks>
public interface IRelocatedFileIndexing
{
    /// <summary>
    /// Indexes the document's NEW item (an app-only RAG job — the copy is BFF-written, the precondition of that path) and
    /// removes the OLD item's chunks: all of them when the old item is gone, otherwise only those attributed to this
    /// document (the old item is then another record's file and its chunks stay that record's).
    /// </summary>
    /// <returns><see cref="RelocatedFileIndexOutcome.Settled"/> when both are done (or there is nothing to index); otherwise
    /// the reason, which the relocator reports as <c>index-pending</c> and a repeat call retries. Never throws for an
    /// indexing failure.</returns>
    Task<RelocatedFileIndexOutcome> ReindexRelocatedFileAsync(
        RelocatedFileIndexRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Input of <see cref="IRelocatedFileIndexing.ReindexRelocatedFileAsync"/>.</summary>
/// <param name="DocumentId">The <c>sprk_document</c> whose file moved.</param>
/// <param name="DriveId">The drive the document's file is in NOW.</param>
/// <param name="ItemId">The document's item NOW (the copy).</param>
/// <param name="FileName">The item's name (indexability by extension).</param>
/// <param name="FileSizeBytes">The item's size when known.</param>
/// <param name="OldItemId">The item the document named before the move.</param>
/// <param name="OldItemRemoved">The old item no longer exists or no row references it any more: remove ALL its chunks.</param>
/// <param name="PreviousSearchIndexName">The index the document was last stamped into (<c>sprk_searchindexname</c>),
/// where the old chunks are; the tenant default is cleaned as well.</param>
public sealed record RelocatedFileIndexRequest(
    Guid DocumentId,
    string DriveId,
    string ItemId,
    string? FileName,
    long? FileSizeBytes,
    string OldItemId,
    bool OldItemRemoved,
    string? PreviousSearchIndexName);

/// <summary>The result of <see cref="IRelocatedFileIndexing.ReindexRelocatedFileAsync"/>.</summary>
/// <param name="Settled">The index is true to the document's new item.</param>
/// <param name="Detail">What was done, or why it is still pending.</param>
public sealed record RelocatedFileIndexOutcome(bool Settled, string Detail)
{
    /// <summary>Done (or nothing to do).</summary>
    public static RelocatedFileIndexOutcome Done(string detail) => new(true, detail);

    /// <summary>Not done; the repeat call retries.</summary>
    public static RelocatedFileIndexOutcome Pending(string detail) => new(false, detail);
}
