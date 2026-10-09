using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Decides the parent record a document's index chunks are filed under (<c>parentEntityType</c> / <c>parentEntityId</c>):
/// the record whose access GOVERNS the document. The ONE derivation (#1510, unified-access-control-r2 task 177): Send-to-Index,
/// <c>/index-file</c>, the app-only index job, the OBO enqueuer and the communication grounding
/// (<c>RegardingParentEntityMapper</c>) all use it.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> A producer that indexes a file without a parent — the relocation after a secure transition
/// (<c>RelocatedFileIndexing</c>), an Office new-item save, a Compose create-on-save, the post-analysis re-index — wrote
/// chunks with no parent fields. <c>scope=entity</c> and <c>scope=all</c> search authorize and filter by those fields, so
/// the document dropped out of record search and parent-scoped RAG although Dataverse still listed it under its record. And
/// because search authorizes a row by its parent, the parent IS an access decision: it must be the record that governs the
/// document, the same one <c>RecordContainerResolver</c> stores it under.</para>
/// <para><b>Candidates.</b> The records the document names, most specific first: its work assignment, project, matter,
/// invoice — each through its typed lookup and its <c>sprk_related*</c> twin (a document filed only through a twin is still
/// filed there; "a related matter is still a matter"). Only when it names none of them: the core stamps of the event and of
/// the communication it is related to (work assignment, project, matter — read through <see cref="CoreAncestorResolver"/>, one
/// hop each); search cannot authorize an <c>event</c> or <c>communication</c> parent, and each inherits its access from those
/// records. A communication's own candidates are its <c>sprk_regarding{core}</c> lookups.</para>
/// <para><b>The rule</b> (main-session decision 2026-10-09, task 177 verifier round; matches the storage decision):
/// (1) if exactly ONE candidate is secure (EFFECTIVE: its own flag or a secure record it is filed under, task 174 / round
/// 87), it is the parent — or, when several secure candidates are one secure family, the most specific of them;
/// (2) if none is secure, the most specific candidate;
/// (3) two DIFFERENT secure roots, or a secure state that cannot be read: NO parent (fail closed — the document is not
/// findable in record search rather than findable under a record more people can read).
/// One candidate is its own answer and costs no flag read. Two or more cost one own-flag read
/// (<see cref="ExternalParticipationService.GetRootRecordFlagsAsync"/>) and the ONE filing walk
/// (<see cref="EffectiveRootFlags.ReadAncestryAsync"/>) per table, folded with <c>EffectiveRootFlags.Fold</c>
/// — the same composition as <see cref="ExternalParticipationService.FoldEffectiveAsync"/>, kept here because the family test
/// needs the walk's secure parents, which the folded flags drop.</para>
/// <para>Names are what <c>scope=entity</c> uses (unprefixed: <c>matter</c>, <c>project</c>, <c>invoice</c>,
/// <c>workassignment</c>).</para>
/// <para><b>Never fails the caller.</b> An unreadable row, event or secure state answers "no parent" and is logged; the file
/// is still indexed. Only cancellation propagates.</para>
/// <para><b>Placement (CLAUDE.md §10/§11).</b> In <c>Services/Dataverse</c>, beside <see cref="CoreAncestorResolver"/>: it is a
/// Dataverse access decision with no AI in it, and the communication pipeline (CRUD code) consumes it, which must not take a
/// dependency on <c>Services/Ai</c> (ADR-013 / bff-extensions.md A.4). It reuses
/// <see cref="CoreAncestorResolver"/> for the event hop and the task-174 effective-flag read for the rule; it adds no read
/// of its own beyond the document row. Scoped, because the flag reader is a typed HttpClient.</para>
/// </remarks>
public sealed class DocumentIndexParentResolver
{
    private const string DocumentEntity = "sprk_document";
    private const string MatterColumn = "sprk_matter";
    private const string ProjectColumn = "sprk_project";
    private const string InvoiceColumn = "sprk_invoice";
    private const string WorkAssignmentColumn = "sprk_workassignment";

    /// <summary>The parent types a document's DIRECT links can name, most specific first, with their search name and the
    /// name used when the row gives none.</summary>
    private static readonly (string Table, string SearchType, string UnknownName)[] ParentTypes =
    [
        ("sprk_workassignment", "workassignment", "Unknown Work Assignment"),
        ("sprk_project", "project", "Unknown Project"),
        ("sprk_matter", "matter", "Unknown Matter"),
        ("sprk_invoice", "invoice", "Unknown Invoice"),
    ];

    /// <summary>The intermediates whose core stamps name the parent when the document names no record directly.</summary>
    private static readonly (string Table, string NamedType, string UnknownName)[] IntermediateTypes =
    [
        ("sprk_event", "event", "Unknown Event"),
        ("sprk_communication", "communication", "Unknown Communication"),
    ];

    /// <summary>
    /// A named projection of the ONE document link vocabulary (<see cref="DocumentLinkFields.All"/>): every link to a
    /// <see cref="ParentTypes"/> table, most specific type first, the typed lookup before its <c>sprk_related*</c> twin (both
    /// target the same table — "a related matter is still a matter"). EXCLUDED, by name: the agreement, to-do and service
    /// request links (not records search authorizes a parent by, and not roots), the PARTY links (contact, organization,
    /// vendor organization — referenced, never an owner of content) and the source email (an activity).
    /// </summary>
    private static readonly (string Column, string Table, string SearchType, string UnknownName)[] DirectLinks =
        DocumentLinkFields.All
            .Select(f => (Field: f, Rank: Array.FindIndex(ParentTypes, t => t.Table == f.TargetEntityLogicalName)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Field.LogicalName.StartsWith("sprk_related", StringComparison.Ordinal) ? 1 : 0)
            .Select(x => (x.Field.LogicalName, ParentTypes[x.Rank].Table, ParentTypes[x.Rank].SearchType, ParentTypes[x.Rank].UnknownName))
            .ToArray();

    /// <summary>The projection's links to an <see cref="IntermediateTypes"/> table (the related event and communication).</summary>
    private static readonly (string Column, string Table, string NamedType, string UnknownName)[] IntermediateLinks =
        DocumentLinkFields.All
            .Select(f => (Field: f, Rank: Array.FindIndex(IntermediateTypes, t => t.Table == f.TargetEntityLogicalName)))
            .Where(x => x.Rank >= 0)
            .Select(x => (x.Field.LogicalName, IntermediateTypes[x.Rank].Table, IntermediateTypes[x.Rank].NamedType, IntermediateTypes[x.Rank].UnknownName))
            .ToArray();

    /// <summary>The columns the derivation reads when it has only the document id (one read).</summary>
    internal static readonly string[] RowColumns =
        [.. DirectLinks.Select(l => l.Column), .. IntermediateLinks.Select(l => l.Column)];

    /// <summary>The links <see cref="Spaarke.Dataverse.DocumentEntity"/> does not carry (it has only the typed matter /
    /// project / invoice): read for every Send-to-Index / index-file document.</summary>
    private static readonly string[] BeyondEntityColumns =
        [.. RowColumns.Where(c => c is not (MatterColumn or ProjectColumn or InvoiceColumn))];

    /// <summary>Core tables, most specific first, with their search name and the name used when the row gives none.</summary>
    private static readonly (string Table, string SearchType, string UnknownName)[] CoreOrder =
    [
        ("sprk_workassignment", "workassignment", "Unknown Work Assignment"),
        ("sprk_project", "project", "Unknown Project"),
        ("sprk_matter", "matter", "Unknown Matter"),
    ];

    private readonly IGenericEntityService _dataverse;
    private readonly CoreAncestorResolver _coreAncestors;
    private readonly ExternalParticipationService _flags;
    private readonly ILogger<DocumentIndexParentResolver> _logger;

    public DocumentIndexParentResolver(
        IGenericEntityService dataverse,
        CoreAncestorResolver coreAncestors,
        ExternalParticipationService flags,
        ILogger<DocumentIndexParentResolver> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _coreAncestors = coreAncestors ?? throw new ArgumentNullException(nameof(coreAncestors));
        _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The parent for a document already read through <see cref="IDocumentDataverseService.GetDocumentAsync"/>
    /// (Send-to-Index): <see cref="DecideAsync(Spaarke.Dataverse.DocumentEntity, CancellationToken)"/>'s parent.
    /// </summary>
    public async Task<ParentEntityContext?> ResolveAsync(Spaarke.Dataverse.DocumentEntity document, CancellationToken ct)
        => (await DecideAsync(document, ct).ConfigureAwait(false)).Parent;

    /// <summary>
    /// The decision for a document already read through <see cref="IDocumentDataverseService.GetDocumentAsync"/>: one read
    /// of the links that type does not carry (work assignment, related event), then the rule. <see cref="IndexParentDecision.Named"/>
    /// lists the records the row names (for <c>/index-file</c>'s body check). A failed read is undecided, never the entity's
    /// links alone: a secure work assignment the read would have found could govern the document.
    /// </summary>
    public async Task<IndexParentDecision> DecideAsync(Spaarke.Dataverse.DocumentEntity document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!Guid.TryParse(document.Id, out var documentId) || documentId == Guid.Empty)
        {
            return IndexParentDecision.Undecided;
        }

        var row = await ReadRowAsync(documentId, BeyondEntityColumns, ct).ConfigureAwait(false);
        if (row is null)
        {
            return IndexParentDecision.Undecided;
        }

        // The typed matter / project / invoice come from the entity (with its names), the rest from the row just read.
        var candidates = new List<IndexParentCandidate>(8);
        foreach (var (column, table, searchType, unknownName) in DirectLinks)
        {
            switch (column)
            {
                case ProjectColumn:
                    AddCandidate(candidates, table, searchType, document.ProjectId, document.ProjectName ?? unknownName);
                    break;
                case MatterColumn:
                    AddCandidate(candidates, table, searchType, document.MatterId, document.MatterName ?? unknownName);
                    break;
                case InvoiceColumn:
                    AddCandidate(candidates, table, searchType, document.InvoiceId, document.InvoiceName ?? unknownName);
                    break;
                default:
                    AddCandidate(candidates, row, column, table, searchType, unknownName);
                    break;
            }
        }

        return await DecideForRowAsync(documentId, row, candidates, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The parent for a document known only by id (an index request that carried none): one read of
    /// <see cref="RowColumns"/>, then the rule. Null when the id is not a record id, the row cannot be read, nothing on it
    /// names a parent, or the rule fails closed.
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

        var candidates = new List<IndexParentCandidate>(8);
        foreach (var (column, table, searchType, unknownName) in DirectLinks)
        {
            AddCandidate(candidates, row, column, table, searchType, unknownName);
        }

        return (await DecideForRowAsync(id, row, candidates, ct).ConfigureAwait(false)).Parent;
    }

    /// <summary>
    /// The core candidates a <c>sprk_regarding{core}</c>-carrying row names (a communication), most specific first, named
    /// with <paramref name="unknownName"/> when the lookup carries none.
    /// </summary>
    internal static IReadOnlyList<IndexParentCandidate> CoreRegardingsOf(Entity row, Func<string, string> unknownName)
    {
        ArgumentNullException.ThrowIfNull(row);
        var candidates = new List<IndexParentCandidate>(3);
        foreach (var (table, searchType, _) in CoreOrder)
        {
            var column = CoreAncestorResolver.StampColumnFor(table);
            AddCandidate(candidates, row, column, table, searchType, unknownName(searchType));
        }

        return candidates;
    }

    /// <summary>
    /// The rule over candidates ordered most specific first (see the remarks). One candidate is its own answer, with no read.
    /// </summary>
    internal async Task<ParentEntityContext?> ChooseGoverningAsync(IReadOnlyList<IndexParentCandidate> candidates, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return null;
        }

        if (candidates.Count == 1)
        {
            return candidates[0].Parent;
        }

        var state = new Dictionary<IndexParentCandidate, (bool Secure, HashSet<Guid> Roots)>();
        try
        {
            foreach (var group in candidates.GroupBy(c => c.Table, StringComparer.OrdinalIgnoreCase))
            {
                var ids = group.Select(c => c.Id).Distinct().ToList();
                var own = await _flags.GetRootRecordFlagsAsync(group.Key, ids, ct).ConfigureAwait(false);
                if (own.Count == 0)
                {
                    // Not a flag-bearing table (an invoice): it is never a secure root.
                    foreach (var candidate in group)
                    {
                        state[candidate] = (false, new HashSet<Guid>());
                    }

                    continue;
                }

                var ancestry = await EffectiveRootFlags.ReadAncestryAsync(_dataverse, _logger, group.Key, ids, ct).ConfigureAwait(false);
                foreach (var candidate in group)
                {
                    var answer = ancestry?.GetValueOrDefault(candidate.Id);
                    if (!own.TryGetValue(candidate.Id, out var flags) || flags.IsUnreadable
                        || (ancestry is not null && (answer is null || !answer.IsKnown)))
                    {
                        _logger.LogWarning(
                            "Index parent: the secure state of {Table} {Id} could not be read; indexing without a parent (fail closed).",
                            candidate.Table, candidate.Id);
                        return null;
                    }

                    var effective = EffectiveRootFlags.Fold(flags, answer);
                    var roots = new HashSet<Guid>(answer?.SecureParents.Select(p => p.Id) ?? Enumerable.Empty<Guid>());
                    if (flags.IsSecure)
                    {
                        roots.Add(candidate.Id);
                    }

                    state[candidate] = (effective.IsSecure, roots);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Index parent: the secure state of {Count} candidate records could not be read; indexing without a parent (fail closed).",
                candidates.Count);
            return null;
        }

        var secure = candidates.Where(c => state[c].Secure).ToList();
        if (secure.Count == 0)
        {
            return candidates[0].Parent;
        }

        for (var i = 0; i < secure.Count; i++)
        {
            for (var j = i + 1; j < secure.Count; j++)
            {
                if (!state[secure[i]].Roots.Overlaps(state[secure[j]].Roots))
                {
                    _logger.LogWarning(
                        "Index parent: {FirstTable} {FirstId} and {SecondTable} {SecondId} are under different secure roots; indexing without a parent (fail closed).",
                        secure[i].Table, secure[i].Id, secure[j].Table, secure[j].Id);
                    return null;
                }
            }
        }

        return secure[0].Parent;
    }

    private async Task<IndexParentDecision> DecideForRowAsync(
        Guid documentId, Entity row, IReadOnlyList<IndexParentCandidate> candidates, CancellationToken ct)
    {
        if (candidates.Count > 0)
        {
            var parent = await ChooseGoverningAsync(candidates, ct).ConfigureAwait(false);
            return new IndexParentDecision(parent, candidates.Select(c => c.Parent).ToList(), Decided: parent is not null);
        }

        // No record named directly: the core stamps of the intermediates the document is related to (event, communication),
        // one hop each, decided by the same rule.
        var named = new List<ParentEntityContext>(IntermediateLinks.Length);
        var stamps = new List<IndexParentCandidate>(3);
        foreach (var (column, table, namedType, unknownName) in IntermediateLinks)
        {
            var link = row.GetAttributeValue<EntityReference>(column);
            if (link is null || link.Id == Guid.Empty)
            {
                continue;
            }

            named.Add(new ParentEntityContext(namedType, link.Id.ToString(), link.Name ?? unknownName));
            var ancestors = await _coreAncestors.ResolveStampsAsync(table, link.Id, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (!ancestors.Succeeded)
            {
                _logger.LogWarning(
                    "Index parent: document {DocumentId}'s {Table} {Id} has no readable core ancestor ({Error}); indexing without a parent.",
                    documentId, table, link.Id, ancestors.Error);
                return new IndexParentDecision(null, named, Decided: false);
            }

            foreach (var (coreTable, searchType, coreUnknownName) in CoreOrder)
            {
                foreach (var stamp in ancestors.Stamps.Where(s => string.Equals(s.EntityType, coreTable, StringComparison.OrdinalIgnoreCase)))
                {
                    AddCandidate(stamps, coreTable, searchType, stamp.RecordId.ToString(), coreUnknownName);
                }
            }
        }

        if (named.Count == 0)
        {
            return IndexParentDecision.NothingNamed;
        }

        if (stamps.Count == 0)
        {
            // No ancestor, or only one search cannot name (a service request).
            _logger.LogInformation(
                "Index parent: document {DocumentId}'s related records are under no matter, project or work assignment; indexing without a parent.",
                documentId);
            return new IndexParentDecision(null, named, Decided: true);
        }

        // Most specific first across both intermediates.
        var ordered = stamps
            .OrderBy(c => Array.FindIndex(CoreOrder, o => string.Equals(o.Table, c.Table, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var governing = await ChooseGoverningAsync(ordered, ct).ConfigureAwait(false);
        return new IndexParentDecision(governing, named, Decided: governing is not null);
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

    private static void AddCandidate(
        List<IndexParentCandidate> candidates, Entity row, string column, string table, string searchType, string unknownName)
    {
        var reference = row.GetAttributeValue<EntityReference>(column);
        if (reference is not null && reference.Id != Guid.Empty && !Names(candidates, table, reference.Id))
        {
            var name = string.IsNullOrWhiteSpace(reference.Name) ? unknownName : reference.Name;
            candidates.Add(new IndexParentCandidate(table, reference.Id, new ParentEntityContext(searchType, reference.Id.ToString(), name)));
        }
    }

    private static void AddCandidate(List<IndexParentCandidate> candidates, string table, string searchType, string? id, string name)
    {
        if (Guid.TryParse(id, out var recordId) && recordId != Guid.Empty && !Names(candidates, table, recordId))
        {
            candidates.Add(new IndexParentCandidate(table, recordId, new ParentEntityContext(searchType, id!, name)));
        }
    }

    /// <summary>The same record through a typed lookup and its twin (or two intermediates) is one candidate.</summary>
    private static bool Names(List<IndexParentCandidate> candidates, string table, Guid id) =>
        candidates.Any(c => c.Id == id && string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One record a document (or communication) names, and the parent it would be indexed under.</summary>
internal sealed record IndexParentCandidate(string Table, Guid Id, ParentEntityContext Parent);

/// <summary>
/// What <see cref="DocumentIndexParentResolver.DecideAsync(Spaarke.Dataverse.DocumentEntity, CancellationToken)"/> decided.
/// </summary>
/// <param name="Parent">The governing record, or <c>null</c>.</param>
/// <param name="Named">The records the row names directly (a related event as <c>event</c>) — empty when it names none.</param>
/// <param name="Decided"><c>false</c> when the answer is <c>null</c> because something could not be read or the rule
/// failed closed — a caller must then NOT substitute a parent of its own.</param>
public sealed record IndexParentDecision(ParentEntityContext? Parent, IReadOnlyList<ParentEntityContext> Named, bool Decided)
{
    /// <summary>The row names no record: no parent, decided.</summary>
    public static readonly IndexParentDecision NothingNamed = new(null, Array.Empty<ParentEntityContext>(), Decided: true);

    /// <summary>The row could not be read: no parent, and no substitute.</summary>
    public static readonly IndexParentDecision Undecided = new(null, Array.Empty<ParentEntityContext>(), Decided: false);
}
