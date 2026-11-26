using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// What <see cref="DocumentMatterTypeResolver"/> decided for one document.
/// </summary>
/// <param name="MatterTypeId">Record id of the matter type, or <c>null</c> when the source record has no type (or nothing is resolved).</param>
/// <param name="SourceId">Id of the matter or project the type came from. <c>""</c> means "resolved: the document belongs to no matter
/// or project" (including a document, matter or project that no longer exists); <c>null</c> means "not resolved" (a read failed, or the
/// parent could not be decided) and the caller leaves the index fields as they are.</param>
public readonly record struct MatterTypeResolution(string? MatterTypeId, string? SourceId)
{
    /// <summary>Resolved: the document belongs to no matter or project.</summary>
    public static readonly MatterTypeResolution NoSource = new(null, string.Empty);

    /// <summary>Not resolved (a read failed): nothing to stamp, retry later.</summary>
    public static readonly MatterTypeResolution Unresolved = new(null, null);

    /// <summary>True when <see cref="SourceId"/> is set (including the empty "resolved, none" marker).</summary>
    public bool IsResolved => SourceId is not null;
}

/// <summary>
/// Derives the matter type a document is filed under, for the Matter Type search filter: the type (<c>sprk_mattertype</c>) of the
/// matter or project the document belongs to, one hop from the parent <see cref="DocumentIndexParentResolver"/> chose.
/// </summary>
/// <remarks>
/// <para><b>Rule.</b> A <c>matter</c> / <c>project</c> parent is the source. A <c>workassignment</c> parent gives its
/// <c>sprk_regardingmatter</c>, else its <c>sprk_regardingproject</c>; an <c>invoice</c> parent its <c>sprk_matter</c>, else its
/// <c>sprk_project</c>. A document with no parent (or one of those with no matter or project) falls back to its source email
/// (<c>sprk_document.sprk_email</c>): that email's <c>sprk_matter</c>, else <c>sprk_project</c>. The type is then read from the
/// source. A source without a type answers <c>(null, sourceId)</c> so a type assigned later still finds the document's chunks.</para>
/// <para><b>Parentless chunks.</b> When the caller has no parent (a chunk indexed without one, or read back from the index without
/// one) and the document id is a record id, the parent is asked of <see cref="DocumentIndexParentResolver.DecideAsync(string?, CancellationToken)"/>
/// and used ONLY to derive the type (no parent field is written). The empty "resolved, none" marker is answered only when that
/// decision is <c>Decided</c> and the document has no source email; an undecided or failed read is <see cref="MatterTypeResolution.Unresolved"/>
/// so the sync job asks again.</para>
/// <para><b>Deleted rows.</b> A Dataverse not-found (<c>0x80040217</c>) is an answer, not a failure: a document, parent or source that no
/// longer exists contributes no source, so a document whose only link is gone is resolved with the empty marker.</para>
/// <para><b>Never fails indexing.</b> A failed read logs a warning and answers <see cref="MatterTypeResolution.Unresolved"/>; the
/// caller leaves the fields unset and <c>MatterTypeIndexSyncJob</c> resolves the document on a later run. Only cancellation propagates.</para>
/// <para><b>Placement (CLAUDE.md §10/§11).</b> <c>Services/Dataverse</c>, beside <see cref="DocumentIndexParentResolver"/>: a Dataverse
/// read with no AI in it. (1) Existing: <see cref="DocumentIndexParentResolver"/> chooses the parent but reads no matter type, and the
/// index holds no type. (2) Extension: it answers an access decision and stays one; the type is a different question over the same
/// parent, so it is a sibling that takes the parent as input rather than a second code path inside it. (3) Cost of doing nothing: the
/// Matter Type filter matches no document. Scoped, like the indexing services that call it.</para>
/// </remarks>
public sealed class DocumentMatterTypeResolver
{
    private const string MatterTable = "sprk_matter";
    private const string ProjectTable = "sprk_project";
    private const string MatterTypeColumn = "sprk_mattertype";

    private readonly IGenericEntityService _dataverse;
    private readonly DocumentIndexParentResolver _parents;
    private readonly ILogger<DocumentMatterTypeResolver> _logger;

    public DocumentMatterTypeResolver(
        IGenericEntityService dataverse,
        DocumentIndexParentResolver parents,
        ILogger<DocumentMatterTypeResolver> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _parents = parents ?? throw new ArgumentNullException(nameof(parents));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolves the matter type for <paramref name="documentId"/> filed under <paramref name="parent"/> (null when the document has none).
    /// </summary>
    public async Task<MatterTypeResolution> ResolveAsync(string? documentId, ParentEntityContext? parent, CancellationToken ct)
    {
        try
        {
            var hasDocument = Guid.TryParse(documentId, out var docId) && docId != Guid.Empty;

            // No parent from the caller: it may be absent because the document has none, or because the parent could not be decided
            // (the index writer fails closed). Only a decided "none" may be sealed as such.
            if (parent is null && hasDocument)
            {
                var decision = await _parents.DecideAsync(documentId, ct).ConfigureAwait(false);
                if (!decision.Decided)
                {
                    return MatterTypeResolution.Unresolved;
                }

                parent = decision.Parent;
            }

            var source = await SourceFromParentAsync(parent, ct).ConfigureAwait(false);

            if (source is null && hasDocument)
            {
                source = await SourceFromEmailAsync(docId, ct).ConfigureAwait(false);
            }

            if (source is null)
            {
                return MatterTypeResolution.NoSource;
            }

            var (table, id) = source.Value;
            var read = await ReadMatterTypeAsync(table, id, ct).ConfigureAwait(false);
            return read.Exists ? new MatterTypeResolution(read.TypeId, id.ToString("D")) : MatterTypeResolution.NoSource;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Matter type: document {DocumentId} could not be resolved ({ExceptionType}); indexing without a matter type, a later sync run retries.",
                documentId, ex.GetType().Name);
            return MatterTypeResolution.Unresolved;
        }
    }

    private async Task<(string Table, Guid Id)?> SourceFromParentAsync(ParentEntityContext? parent, CancellationToken ct)
    {
        if (parent is null || !Guid.TryParse(parent.EntityId, out var parentId) || parentId == Guid.Empty)
        {
            return null;
        }

        var type = parent.EntityType.Trim().ToLowerInvariant();
        if (type.StartsWith("sprk_", StringComparison.Ordinal))
        {
            type = type["sprk_".Length..];
        }

        switch (type)
        {
            case "matter":
                return (MatterTable, parentId);
            case "project":
                return (ProjectTable, parentId);
            case "workassignment":
                return await FirstLinkAsync("sprk_workassignment", parentId, "sprk_regardingmatter", "sprk_regardingproject", ct).ConfigureAwait(false);
            case "invoice":
                return await FirstLinkAsync("sprk_invoice", parentId, "sprk_matter", "sprk_project", ct).ConfigureAwait(false);
            default:
                return null;
        }
    }

    private async Task<(string Table, Guid Id)?> SourceFromEmailAsync(Guid documentId, CancellationToken ct)
    {
        var document = await RetrieveOrNullAsync("sprk_document", documentId, ["sprk_email"], ct).ConfigureAwait(false);
        var email = document?.GetAttributeValue<EntityReference>("sprk_email");
        if (email is null || email.Id == Guid.Empty)
        {
            return null;
        }

        return await FirstLinkAsync("email", email.Id, "sprk_matter", "sprk_project", ct).ConfigureAwait(false);
    }

    /// <summary>The matter the row names, else the project, as a source; null when it names neither (or the row is gone).</summary>
    private async Task<(string Table, Guid Id)?> FirstLinkAsync(
        string table, Guid id, string matterColumn, string projectColumn, CancellationToken ct)
    {
        var row = await RetrieveOrNullAsync(table, id, [matterColumn, projectColumn], ct).ConfigureAwait(false);
        if (row?.GetAttributeValue<EntityReference>(matterColumn) is { } matter && matter.Id != Guid.Empty)
        {
            return (MatterTable, matter.Id);
        }

        if (row?.GetAttributeValue<EntityReference>(projectColumn) is { } project && project.Id != Guid.Empty)
        {
            return (ProjectTable, project.Id);
        }

        return null;
    }

    /// <summary>The source's type; <c>Exists</c> is false when the source row is gone (not-found, or null from a service double).</summary>
    private async Task<(bool Exists, string? TypeId)> ReadMatterTypeAsync(string table, Guid id, CancellationToken ct)
    {
        var row = await RetrieveOrNullAsync(table, id, [MatterTypeColumn], ct).ConfigureAwait(false);
        if (row is null)
        {
            return (false, null);
        }

        var type = row.GetAttributeValue<EntityReference>(MatterTypeColumn);
        return (true, type is null || type.Id == Guid.Empty ? null : type.Id.ToString("D"));
    }

    /// <summary>A retrieve where a Dataverse not-found (the SDK throws <c>0x80040217</c>) is the answer "no such row"; any other fault propagates.</summary>
    private async Task<Entity?> RetrieveOrNullAsync(string table, Guid id, string[] columns, CancellationToken ct)
    {
        try
        {
            return await _dataverse.RetrieveAsync(table, id, columns, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (RecordContainerResolver.IsRecordNotFoundInChain(ex))
        {
            return null;
        }
    }
}
