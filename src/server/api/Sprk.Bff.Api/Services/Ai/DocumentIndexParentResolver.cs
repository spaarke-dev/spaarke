using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Derives the parent record a document's index chunks are filed under (<c>parentEntityType</c> /
/// <c>parentEntityId</c>) from the document ROW. The ONE derivation (#1510, unified-access-control-r2 task 177): Send-to-Index
/// uses it, and so does every indexing path that receives no parent but knows the document.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A producer that indexes a file without a parent — the relocation after a secure transition
/// (<c>RelocatedFileIndexing</c>), an Office new-item save, a Compose create-on-save, the post-analysis re-index — wrote
/// chunks with no parent fields. <c>scope=entity</c> and <c>scope=all</c> search authorize and filter by those fields, so
/// the document dropped out of record search and parent-scoped RAG although Dataverse still listed it under its record.</para>
/// <para><b>Order.</b> (1) The row's matter, project, invoice — exactly Send-to-Index's previous inline derivation, in its
/// order. (2) The row's work assignment. (3) The event the document is related to, named by the EVENT's core ancestor
/// (matter, project, work assignment — <see cref="CoreAncestorResolver"/>, one hop): search authorizes a parent through
/// <c>SemanticSearchAuthorizationFilter.AuthorizableEntitySets</c>, which has no event, so a chunk filed under the event
/// itself could never be found; the event inherits its access from that core record, which is why naming it is
/// access-consistent. Anything else: no parent. The types are named the way <c>scope=entity</c> names them
/// (unprefixed: <c>matter</c>, <c>project</c>, <c>invoice</c>, <c>workassignment</c>).</para>
/// <para><b>Never fails the caller and never invents a parent.</b> An unreadable row, an unreadable event or an event
/// with no core ancestor is logged and answers "no parent" — the file is still indexed, as before this class existed.
/// Only cancellation propagates.</para>
/// <para><b>Placement (CLAUDE.md §10/§11).</b> Beside the indexing services it serves. Not a method on
/// <see cref="CoreAncestorResolver"/>: that resolver derives ACCESS stamps (core types only, no names, fail-closed for a
/// write); a search parent also names an invoice and must never fail an index. It reuses that resolver for the event
/// hop instead of reading the event's root columns again.</para>
/// </remarks>
public sealed class DocumentIndexParentResolver
{
    private const string DocumentEntity = "sprk_document";
    private const string MatterColumn = "sprk_matter";
    private const string ProjectColumn = "sprk_project";
    private const string InvoiceColumn = "sprk_invoice";
    private const string WorkAssignmentColumn = "sprk_workassignment";
    private const string RelatedEventColumn = "sprk_relatedevent";

    /// <summary>The columns the derivation reads when it has only the document id (one read).</summary>
    internal static readonly string[] RowColumns =
        [MatterColumn, ProjectColumn, InvoiceColumn, WorkAssignmentColumn, RelatedEventColumn];

    /// <summary>The columns <see cref="DocumentEntity"/> does not carry, read only when its own links name no parent.</summary>
    private static readonly string[] BeyondEntityColumns = [WorkAssignmentColumn, RelatedEventColumn];

    /// <summary>An event's core ancestors, in the order a parent is named from them.</summary>
    private static readonly (string RootEntity, string SearchType, string UnknownName)[] EventAncestorOrder =
    [
        ("sprk_matter", "matter", "Unknown Matter"),
        ("sprk_project", "project", "Unknown Project"),
        ("sprk_workassignment", "workassignment", "Unknown Work Assignment"),
    ];

    private readonly IGenericEntityService _dataverse;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly ILogger<DocumentIndexParentResolver> _logger;

    public DocumentIndexParentResolver(
        IGenericEntityService dataverse,
        CoreAncestorResolver coreAncestors,
        ILogger<DocumentIndexParentResolver> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Step (1) alone, from an already-read <see cref="Spaarke.Dataverse.DocumentEntity"/>: its matter, else project, else
    /// invoice. No I/O.
    /// </summary>
    public static ParentEntityContext? FromDocumentLinks(Spaarke.Dataverse.DocumentEntity document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!string.IsNullOrEmpty(document.MatterId))
        {
            return new ParentEntityContext("matter", document.MatterId, document.MatterName ?? "Unknown Matter");
        }

        if (!string.IsNullOrEmpty(document.ProjectId))
        {
            return new ParentEntityContext("project", document.ProjectId, document.ProjectName ?? "Unknown Project");
        }

        if (!string.IsNullOrEmpty(document.InvoiceId))
        {
            return new ParentEntityContext("invoice", document.InvoiceId, document.InvoiceName ?? "Unknown Invoice");
        }

        return null;
    }

    /// <summary>
    /// The parent for a document already read through <see cref="IDocumentDataverseService.GetDocumentAsync"/>: its
    /// matter / project / invoice when it has one (no further read), otherwise one read of the links that type does not
    /// carry (work assignment, related event).
    /// </summary>
    public async Task<ParentEntityContext?> ResolveAsync(Spaarke.Dataverse.DocumentEntity document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (FromDocumentLinks(document) is { } parent)
        {
            return parent;
        }

        if (!Guid.TryParse(document.Id, out var documentId) || documentId == Guid.Empty)
        {
            return null;
        }

        var row = await ReadRowAsync(documentId, BeyondEntityColumns, ct).ConfigureAwait(false);
        return row is null ? null : await FromBeyondEntityLinksAsync(documentId, row, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The parent for a document known only by id (an index request that carried none): one read of
    /// <see cref="RowColumns"/>, then the order in the remarks. Null when the id is not a record id, the row cannot be
    /// read, or nothing on it names a parent.
    /// </summary>
    public async Task<ParentEntityContext?> ResolveAsync(string? documentId, CancellationToken ct)
    {
        if (!Guid.TryParse(documentId, out var id) || id == Guid.Empty)
        {
            return null;
        }

        var row = await ReadRowAsync(id, RowColumns, ct).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        return Named(row, MatterColumn, "matter", "Unknown Matter")
            ?? Named(row, ProjectColumn, "project", "Unknown Project")
            ?? Named(row, InvoiceColumn, "invoice", "Unknown Invoice")
            ?? await FromBeyondEntityLinksAsync(id, row, ct).ConfigureAwait(false);
    }

    private async Task<ParentEntityContext?> FromBeyondEntityLinksAsync(Guid documentId, Entity row, CancellationToken ct)
    {
        if (Named(row, WorkAssignmentColumn, "workassignment", "Unknown Work Assignment") is { } workAssignment)
        {
            return workAssignment;
        }

        var relatedEvent = row.GetAttributeValue<EntityReference>(RelatedEventColumn);
        if (relatedEvent is null || relatedEvent.Id == Guid.Empty)
        {
            return null;
        }

        var ancestors = await _coreAncestors.ResolveStampsAsync("sprk_event", relatedEvent.Id, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!ancestors.Succeeded)
        {
            _logger.LogWarning(
                "Index parent: document {DocumentId}'s event {EventId} has no readable core ancestor ({Error}); indexing without a parent.",
                documentId, relatedEvent.Id, ancestors.Error);
            return null;
        }

        foreach (var (rootEntity, searchType, unknownName) in EventAncestorOrder)
        {
            var stamp = ancestors.Stamps.FirstOrDefault(s => string.Equals(s.EntityType, rootEntity, StringComparison.OrdinalIgnoreCase));
            if (stamp is not null)
            {
                return new ParentEntityContext(searchType, stamp.RecordId.ToString(), unknownName);
            }
        }

        // No ancestor, or only one search cannot name (a service request).
        _logger.LogInformation(
            "Index parent: document {DocumentId}'s event {EventId} is under no matter, project or work assignment; indexing without a parent.",
            documentId, relatedEvent.Id);
        return null;
    }

    private async Task<Entity?> ReadRowAsync(Guid documentId, string[] columns, CancellationToken ct)
    {
        try
        {
            var row = await _dataverse.RetrieveAsync(DocumentEntity, documentId, columns, ct).ConfigureAwait(false);
            if (row is null)
            {
                _logger.LogWarning("Index parent: document {DocumentId} was not found; indexing without a parent.", documentId);
            }

            return row;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Index parent: document {DocumentId}'s row could not be read; indexing without a parent.", documentId);
            return null;
        }
    }

    private static ParentEntityContext? Named(Entity row, string column, string searchType, string unknownName)
    {
        var reference = row.GetAttributeValue<EntityReference>(column);
        return reference is null || reference.Id == Guid.Empty
            ? null
            : new ParentEntityContext(searchType, reference.Id.ToString(), string.IsNullOrWhiteSpace(reference.Name) ? unknownName : reference.Name);
    }
}
