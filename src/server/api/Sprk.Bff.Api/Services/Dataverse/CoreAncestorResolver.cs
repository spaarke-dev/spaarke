using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Models;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Derives the ultimate CORE-record ancestor of a regarding target so server-created child records carry
/// the same FR-26 stamp the client write path produces. The C# mirror of
/// <c>PolymorphicResolverService.deriveCoreAncestorStamps</c> in <c>@spaarke/ui-components</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The evaluator's child-inheritance term is a set-membership test —
/// <c>child.sprk_regarding{core} ∈ {accessible core ids}</c> — and it can only read a lookup the child ROW
/// already carries; the <c>ScopeDimension</c> shape is a synchronous
/// <c>Func&lt;CallerPrincipal, IReadOnlySet&lt;Guid&gt;&gt;</c> with no Dataverse round-trip by design. So a
/// <c>todo → communication → matter</c> chain is inexpressible unless the ultimate core ancestor is
/// denormalized onto the child at write time. That denormalized stamp is what keeps every chain ONE hop,
/// which is why ADR-034's 1-hop cap holds here unamended.
/// </para>
/// <para>
/// <b>Client parity is the point.</b> FR-26 acceptance covers ALL chains, not just PCF-authored ones. If a
/// server writer skips the stamp, FR-27 inheritance silently under-grants for exactly those records — a
/// contact with Project access does not see server-filed emails on the project — and the one-time backfill
/// (task 053) would permanently miss them going forward. The taxonomy literals below are pinned by a test on
/// BOTH sides so the two implementations cannot drift.
/// </para>
/// <para>
/// <b>Two rules that are easy to get backwards.</b>
/// (1) Matter does NOT inherit from Project — both are CORE, and selecting a core target stamps only that
/// target. Inverting it hands every Project holder every Matter beneath it.
/// (2) Derivation is exactly one hop: it reads the target's own root columns and stops. Those columns are
/// either the target's own direct links or themselves FR-26 stamps written when the target was saved, which is
/// why one read suffices.
/// </para>
/// <para>
/// <b>Keeping the copy fresh (task 156, owner round 4 item 5 option b).</b> Because the stamp is a copy, it goes
/// stale when the intermediate it was copied from is re-filed. <see cref="CoreAncestorRestamper"/> re-stamps the
/// children in the same operation on every BFF re-file path, <c>CoreAncestorStampReconciliationJob</c> repairs
/// copies left stale by writes outside the BFF within one 5-minute cycle, and <c>RecordContainerResolver</c>
/// compares a child's copy with its intermediate's LIVE root before trusting it (refusing
/// <c>container_ancestor_stale</c> on a mismatch). All three classify a row with <see cref="ClassifyStampSource"/>
/// over <see cref="StampSourceColumns"/> and <see cref="IntermediateRootColumns"/>.
/// </para>
/// <para>
/// <b>Placement (CLAUDE.md §10).</b> Lives in <c>Services/Dataverse/</c> rather than
/// <c>Services/Communication/</c> because it must serve every child-entity writer — todo, event, analysis,
/// document, invoice — not just the communication pipeline. Folding it into <c>ThreadResolver</c> would
/// couple those writers to the communication module. It adds no package, no endpoint, and no new interface
/// (ADR-010): reads go through the already-registered <see cref="IGenericEntityService"/>, and column
/// presence through a delegate seam rather than a fresh abstraction.
/// </para>
/// <para>
/// See <c>projects/unified-access-control-r2/notes/phase3-derivation-rules.md</c> for the rules table and
/// <c>notes/phase3-server-writers.md</c> for the writer inventory.
/// </para>
/// </remarks>
public sealed class CoreAncestorResolver
{
    /// <summary>
    /// CORE record entities — direct grants required; these never inherit.
    /// <b>Pinned literally by test, and MUST equal the TypeScript <c>CORE_RECORD_ENTITIES</c>.</b>
    /// Changing this set changes who can see what.
    /// </summary>
    public static readonly IReadOnlyList<string> CoreRecordEntities =
    [
        "sprk_project",
        "sprk_matter",
        "sprk_workassignment",
        "sprk_servicerequest",
    ];

    /// <summary>
    /// CHILD record entities — inherit their core ancestor's rights in one hop, via the stamp this resolver
    /// derives. <b>Pinned literally by test; MUST equal the TypeScript <c>CHILD_RECORD_ENTITIES</c>.</b>
    /// </summary>
    /// <remarks>
    /// Entities in NEITHER set (<c>sprk_budget</c>, <c>sprk_organization</c>, <c>contact</c>,
    /// <c>account</c>, <c>sprk_reportcard</c>) are intentionally unclassified for FR-26 — they confer access
    /// through other evaluator terms, never through core-ancestor inheritance. That is a distinct,
    /// non-error state; see <see cref="CoreAncestorStatus.Unclassified"/>.
    /// <para><c>sprk_memo</c> was added by unified-access-control-r2 task 147 (owner round 2 item 6, C10 part 2). A memo
    /// carries all four <c>sprk_regarding{core}</c> lookups (live spaarkedev1, read-only, 2026-10-04). Since task 156,
    /// derivation keys on <see cref="IntermediateRootColumns"/>, not on this set, so the memo is ALSO an intermediate there
    /// (task 147 r1), a stamped child in <see cref="StampSourceColumns"/>, and has storage-resolver links
    /// (<c>RecordContainerResolver.ChildAncestorLinks</c>); only then does a record filed under a memo derive the memo's
    /// own core ancestor. The TypeScript <c>CHILD_RECORD_ENTITIES</c> changed in the same change, and
    /// <c>Taxonomy_MatchesTheTypeScriptSide</c> pins the two; <c>EveryChildTaxonomyEntity_IsAnIntermediate</c> pins that
    /// no CHILD type reads as Unclassified here while the TypeScript side derives it.</para>
    /// </remarks>
    public static readonly IReadOnlyList<string> ChildRecordEntities =
    [
        "sprk_invoice",
        "sprk_communication",
        "sprk_document",
        "sprk_event",
        "sprk_todo",
        "sprk_analysis",
        "sprk_memo",
    ];

    /// <summary>
    /// The lookup column that carries each CORE entity's stamp on a child row. These four are the ONLY
    /// access-conferring ancestor lookups — any other <c>sprk_regarding*</c> column is a relationship, not an
    /// access edge.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not every child entity carries all four: <c>sprk_invoice</c> carries none of them (it links through
    /// typed <c>sprk_matter</c> / <c>sprk_project</c> lookups), while <c>sprk_todo</c> and <c>sprk_event</c>
    /// carry all four (live spaarkedev1, read-only, 2026-10-01 — an earlier note here
    /// that <c>sprk_todo</c> lacks <c>sprk_regardingservicerequest</c> was stale). Presence is therefore always
    /// resolved against live metadata, never assumed — reading a non-existent column would fault and turn a
    /// schema gap into a blocked write.
    /// </remarks>
    public static readonly IReadOnlyList<(string EntityType, string LookupAttribute)> CoreAncestorLookups =
    [
        ("sprk_project", "sprk_regardingproject"),
        ("sprk_matter", "sprk_regardingmatter"),
        ("sprk_workassignment", "sprk_regardingworkassignment"),
        ("sprk_servicerequest", "sprk_regardingservicerequest"),
    ];

    private static readonly HashSet<string> CoreSet = new(CoreRecordEntities, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ChildSet = new(ChildRecordEntities, StringComparer.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------
    // Task 156 (owner round 4 item 5, option b): the topology the stamp is copied along.
    //
    // A child's stamp is a COPY of the root of the record it is filed under (its "intermediate"). For the copy to
    // stay equal to the root, three things must agree on one table: what the root of each intermediate IS (its root
    // columns), which child tables carry a copy and from which columns (the stamp sources), and which of those
    // columns is THE source on a given row (ClassifyStampSource). The cascade (CoreAncestorRestamper), the
    // reconciliation job and RecordContainerResolver's live comparison all read these three — so they cannot
    // disagree about what "stale" means.
    // -------------------------------------------------------------------------

    /// <summary>
    /// The columns ON each intermediate that name its root (project / matter / work assignment / service request), by
    /// root entity. From the task 155 f3 live sweep plus the task 156 sweep of the five types f3 classified but did not
    /// describe (spaarkedev1, read-only, 2026-10-02). <b>Pinned by test; the TypeScript
    /// <c>INTERMEDIATE_ROOT_COLUMNS</c> MUST equal it row for row (task 169).</b>
    /// </summary>
    /// <remarks>
    /// <para><b>Why not just the four <c>sprk_regarding{core}</c> columns.</b> Until task 156 this resolver derived a
    /// child target's root ONLY from those four. Four of the eight intermediates do not carry them:
    /// <c>sprk_invoice</c> and <c>sprk_budget</c> name their root through typed <c>sprk_matter</c> /
    /// <c>sprk_project</c>, and <c>sprk_document</c> through typed <c>sprk_matter</c> / <c>sprk_project</c> /
    /// <c>sprk_workassignment</c> (and their <c>sprk_related*</c> twins — 0 live rows set those, 2026-10-02). So a to-do
    /// under an invoice or a document carried NO stamp, and agreement / budget / report card were not derived at all
    /// (Unclassified). Owner round 4 item 5 puts all of them in scope: a child's stamp must equal the root of
    /// whatever it is filed under. Agreement and report card DO carry <c>sprk_regardingmatter</c> /
    /// <c>sprk_regardingproject</c>.</para>
    /// <para><b>The access taxonomy is untouched.</b> <see cref="ChildRecordEntities"/> and
    /// <see cref="CoreRecordEntities"/> (pinned to the TypeScript side) do not change: a core record still never
    /// inherits, and nothing here makes an intermediate itself inherit. What changes is only what a child filed
    /// under one of these types is stamped WITH.</para>
    /// <para><b>The client mirror derives the same table (task 169).</b> <c>PolymorphicResolverService.ts</c> carries a
    /// literal <c>INTERMEDIATE_ROOT_COLUMNS</c> that <c>deriveCoreAncestorStamps</c> reads with the same ambiguity
    /// rule, so a child picked through the RegardingResolver PCF under any of these types is stamped at save time.
    /// This table stays the source of truth (ADR-002 WP-2: the server owns the invariant, the client previews it);
    /// <c>CoreAncestorResolverTests.IntermediateRootColumns_MatchTheTypeScriptSide</c> and
    /// <c>CoreAncestorResolverTests.CoreAncestorLookups_MatchTheTypeScriptSide</c> fail the build if the TypeScript
    /// table or its stamp columns drift from this one. Client create paths that do not call the derivation at all
    /// remain the reconciliation job's to stamp (WP-5).</para>
    /// <para>Two columns of one root type on one row (a document's <c>sprk_matter</c> and <c>sprk_relatedmatter</c>)
    /// that name DIFFERENT records are a derivation ERROR — the root is not known, so nothing is stamped.</para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Column, string RootEntity)>> IntermediateRootColumns =
        new Dictionary<string, IReadOnlyList<(string Column, string RootEntity)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_communication"] = StandardRootColumns(),
            ["sprk_event"] = StandardRootColumns(),
            ["sprk_todo"] = StandardRootColumns(),
            ["sprk_analysis"] = StandardRootColumns(),
            // Task 147 r1: the memo joined the CHILD taxonomy (owner round 2 item 6), so it is an intermediate too — a
            // record filed under a memo is stamped with the memo's root. It carries all four sprk_regarding{core}
            // columns (live spaarkedev1, read-only, 2026-10-04), the same four the TypeScript derivation reads for a
            // CHILD target, so C# and TypeScript agree for a memo target. Without this entry the C# side read a memo
            // target as Unclassified while the TypeScript side derived it (verifier item 1).
            ["sprk_memo"] = StandardRootColumns(),
            ["sprk_invoice"] = [("sprk_project", "sprk_project"), ("sprk_matter", "sprk_matter")],
            // The canonical document link vocabulary's (Spaarke.Dataverse.DocumentLinkFields — the one declaration)
            // links to a project / matter / work assignment: the typed column and its related twin. EXCLUDED, by name:
            // the related service request (a service request cannot carry sprk_issecure, and the storage resolver
            // holds it) and every link to a non-root.
            ["sprk_document"] = DocumentLinkFields.All
                .Where(f => f.TargetEntityLogicalName is "sprk_matter" or "sprk_project" or "sprk_workassignment")
                .Select(f => (f.LogicalName, f.TargetEntityLogicalName))
                .ToArray(),
            ["sprk_agreement"] = [("sprk_regardingmatter", "sprk_matter"), ("sprk_regardingproject", "sprk_project")],
            ["sprk_reportcard"] = [("sprk_regardingmatter", "sprk_matter"), ("sprk_regardingproject", "sprk_project")],
            ["sprk_budget"] = [("sprk_matter", "sprk_matter"), ("sprk_project", "sprk_project")],
        };

    private static IReadOnlyList<(string Column, string RootEntity)> StandardRootColumns() =>
        CoreAncestorLookups.Select(l => (l.LookupAttribute, l.EntityType)).ToArray();

    /// <summary>
    /// The child tables that CARRY a stamp, and the columns on each that name the intermediate the stamp is copied
    /// from. From the task 155 f3 / f4 live sweeps (to-do, event, communication) and the task 156 sweep of
    /// <c>sprk_analysis</c> (2026-10-02).
    /// </summary>
    /// <remarks>
    /// Only these four tables carry the <c>sprk_regarding{core}</c> stamp columns. A document, an invoice, a work
    /// assignment, a project and a contact carry none, so they are never re-stamped (a work assignment's
    /// <c>sprk_regardingmatter</c> is its own, user-chosen filing — task 155 interpretation iii). A service request is
    /// CORE: a to-do's <c>sprk_regardingservicerequest</c> is a root column, not a source. <c>sprk_analysis</c>'s
    /// NOT NULL <c>sprk_documentid</c> is the document it ANALYSES (its input), not what it is filed under, so it is not
    /// a source either; the storage resolver reads it as a carrier (<c>RecordContainerResolver</c>).
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Column, string Intermediate)>> StampSourceColumns =
        new Dictionary<string, IReadOnlyList<(string Column, string Intermediate)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_todo"] =
            [
                ("sprk_regardinganalysis", "sprk_analysis"), ("sprk_regardingcommunication", "sprk_communication"),
                ("sprk_regardingdocument", "sprk_document"), ("sprk_regardingevent", "sprk_event"),
                ("sprk_regardinginvoice", "sprk_invoice"), ("sprk_regardingagreement", "sprk_agreement"),
                ("sprk_regardingbudget", "sprk_budget"), ("sprk_regardingreportcard", "sprk_reportcard"),
            ],
            ["sprk_event"] =
            [
                ("sprk_regardinganalysis", "sprk_analysis"), ("sprk_regardingcommunication", "sprk_communication"),
                ("sprk_regardingevent", "sprk_event"), ("sprk_regardinginvoice", "sprk_invoice"),
                ("sprk_regardingagreement", "sprk_agreement"), ("sprk_regardingbudget", "sprk_budget"),
                ("sprk_regardingreportcard", "sprk_reportcard"),
            ],
            ["sprk_communication"] =
            [
                ("sprk_regardinginvoice", "sprk_invoice"), ("sprk_regardingevent", "sprk_event"),
                ("sprk_regardinganalysis", "sprk_analysis"), ("sprk_regardingbudget", "sprk_budget"),
                ("sprk_regardingreportcard", "sprk_reportcard"),
            ],
            ["sprk_analysis"] =
            [
                ("sprk_regardingbudget", "sprk_budget"), ("sprk_regardingcommunication", "sprk_communication"),
                ("sprk_regardingdocument", "sprk_document"), ("sprk_regardinginvoice", "sprk_invoice"),
            ],
            // Task 147 r1 (live sweep of sprk_memo, read-only, 2026-10-04): a memo carries the four stamp columns and
            // these eight lookups to intermediates. Its report-card lookup is named sprk_reportcard, not
            // sprk_regardingreportcard. The BFF's memo create stamps a memo filed under one of these (WP-1), so the
            // copy must be kept fresh by the restamper and the stamp job like every other stamped child.
            ["sprk_memo"] =
            [
                ("sprk_regardinganalysis", "sprk_analysis"), ("sprk_regardingcommunication", "sprk_communication"),
                ("sprk_regardingdocument", "sprk_document"), ("sprk_regardingevent", "sprk_event"),
                ("sprk_regardinginvoice", "sprk_invoice"), ("sprk_regardingagreement", "sprk_agreement"),
                ("sprk_regardingbudget", "sprk_budget"), ("sprk_reportcard", "sprk_reportcard"),
            ],
        };

    /// <summary>
    /// The typed PARTY regarding lookups of each table that carries the polymorphic pair (task 155 f5, from the f3 / f4
    /// live sweeps). The regarding builders write a party's typed lookup and the pair id together, so a pair id equal
    /// to one of these names a person or organization — not ownership, and not a stamp source. One table for the
    /// storage resolver's pair rule 3 (<c>RecordContainerResolver.ChildAncestorLinks</c>) and for
    /// <see cref="ClassifyStampSource"/>. <c>sprk_analysis</c> has none (live 2026-10-02).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Column, string Target)>> PartyRegardingColumns =
        new Dictionary<string, IReadOnlyList<(string Column, string Target)>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_todo"] =
            [
                ("sprk_regardingcontact", "contact"), ("sprk_regardingorganization", "sprk_organization"),
            ],
            ["sprk_event"] =
            [
                ("sprk_regardingcontact", "contact"), ("sprk_regardingorganization", "sprk_organization"),
                ("sprk_regardingaccount", "account"),
            ],
            ["sprk_communication"] =
            [
                ("sprk_regardingperson", "contact"), ("sprk_regardingorganization", "sprk_organization"),
                ("sprk_regardingaccount", "account"),
            ],
            // Task 147 r1 (live 2026-10-04). A timekeeper is a person (a biller on an invoice line): a party, as a
            // contact is, never an owner of content.
            ["sprk_memo"] =
            [
                ("sprk_regardingcontact", "contact"), ("sprk_regardingorganization", "sprk_organization"),
                ("sprk_regardingtimekeeper", "sprk_timekeeper"),
            ],
        };

    /// <summary>The party regarding column names of <paramref name="entity"/> (empty when it has none).</summary>
    public static IReadOnlyList<string> PartyRegardingColumnNames(string entity) =>
        PartyRegardingColumns.TryGetValue(entity, out var parties) ? parties.Select(p => p.Column).ToArray() : [];

    /// <summary>The polymorphic regarding pair's record id column (a STRING — ADR-024 resolver field).</summary>
    public const string RegardingRecordIdColumn = "sprk_regardingrecordid";

    /// <summary>The polymorphic regarding pair's type column (Lookup → <c>sprk_recordtype_ref</c>).</summary>
    public const string RegardingRecordTypeColumn = "sprk_regardingrecordtype";

    /// <summary>True when <paramref name="entityLogicalName"/> carries a stamp (it is one of <see cref="StampSourceColumns"/>).</summary>
    public static bool IsStampedChildEntity(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && StampSourceColumns.ContainsKey(entityLogicalName);

    /// <summary>True when a child filed under <paramref name="entityLogicalName"/> is stamped with ITS root.</summary>
    public static bool IsStampSourceEntity(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && IntermediateRootColumns.ContainsKey(entityLogicalName);

    /// <summary>The root types a child can copy from <paramref name="intermediate"/> (its root columns' entities).</summary>
    public static IReadOnlySet<string> CarriableRootTypes(string intermediate) =>
        IntermediateRootColumns.TryGetValue(intermediate, out var columns)
            ? new HashSet<string>(columns.Select(c => c.RootEntity), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The stamp column on a child for a root entity (<c>sprk_matter</c> → <c>sprk_regardingmatter</c>).</summary>
    public static string StampColumnFor(string rootEntity) =>
        CoreAncestorLookups.First(l => string.Equals(l.EntityType, rootEntity, StringComparison.OrdinalIgnoreCase))
            .LookupAttribute;

    /// <summary>
    /// Which record on a child row its stamp is copied from — the ONE rule the cascade, the reconciliation job and the
    /// storage resolver all apply (task 156).
    /// </summary>
    /// <remarks>
    /// <para>The row's own regarding pair (<c>sprk_regardingrecordid</c>) says what the user filed it under, and the
    /// regarding builders write it with the typed lookup:</para>
    /// <list type="number">
    /// <item>No typed source column set → <see cref="StampSourceKind.NotFiledUnderAnIntermediate"/>: any root column
    /// on the row is its own, direct link.</item>
    /// <item>The pair names a ROOT column set on the row → <see cref="StampSourceKind.DirectRootLink"/>: the user chose
    /// that root (the Office "carrier" to-do: a record regarding plus the document / email it was created from). Every
    /// source column set is a CARRIER, and no root column is a copy.</item>
    /// <item>The pair names a source column set on the row → <see cref="StampSourceKind.Source"/>: that record. The
    /// other source columns set are carriers.</item>
    /// <item>The pair names something else on the row is not carrying (another record entirely) →
    /// <see cref="StampSourceKind.InconsistentPair"/>: the row disagrees with itself; nothing is decided from it.</item>
    /// <item>No pair (or a pair naming the row's own typed party, which the pair rule treats as no pair): exactly one
    /// source column set → <see cref="StampSourceKind.Source"/> (the writers that stamp without a pair —
    /// <c>TaskActionCore</c>, an Ambiguous inbound association — set exactly one regarding); more than one →
    /// <see cref="StampSourceKind.AmbiguousSource"/>.</item>
    /// </list>
    /// </remarks>
    /// <param name="childEntity">A table in <see cref="StampSourceColumns"/>.</param>
    /// <param name="row">The child row, carrying at least its source columns, its stamp columns and its pair id.</param>
    /// <param name="partyRegardingColumns">The row's typed party regarding lookups (an id equal to one of them is "no pair").</param>
    public static StampSourceDecision ClassifyStampSource(
        string childEntity, Entity row, IEnumerable<string>? partyRegardingColumns = null)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!StampSourceColumns.TryGetValue(childEntity, out var sourceColumns))
        {
            return StampSourceDecision.None;
        }

        var setSources = sourceColumns
            .Select(s => (s.Column, s.Intermediate, Id: row.GetAttributeValue<EntityReference>(s.Column)?.Id ?? Guid.Empty))
            .Where(s => s.Id != Guid.Empty)
            .Select(s => new StampSourceLink(s.Column, s.Intermediate, s.Id))
            .ToList();

        if (setSources.Count == 0)
        {
            return StampSourceDecision.None;
        }

        var setRootIds = CoreAncestorLookups
            .Select(l => row.GetAttributeValue<EntityReference>(l.LookupAttribute)?.Id ?? Guid.Empty)
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        var rawPair = row.GetAttributeValue<string>(RegardingRecordIdColumn);
        Guid? pairId = null;
        if (!string.IsNullOrWhiteSpace(rawPair))
        {
            if (!Guid.TryParse(rawPair.Trim(), out var parsed) || parsed == Guid.Empty)
            {
                return new StampSourceDecision(StampSourceKind.InconsistentPair, null, setSources);
            }

            var namesParty = (partyRegardingColumns ?? [])
                .Any(c => row.GetAttributeValue<EntityReference>(c) is { } party && party.Id == parsed);
            if (!namesParty)
            {
                pairId = parsed;
            }
        }

        if (pairId is { } named)
        {
            if (setRootIds.Contains(named))
            {
                return new StampSourceDecision(StampSourceKind.DirectRootLink, null, setSources);
            }

            var source = setSources.FirstOrDefault(s => s.Id == named);
            return source is not null
                ? new StampSourceDecision(StampSourceKind.Source, source, setSources.Where(s => s != source).ToList())
                : new StampSourceDecision(StampSourceKind.InconsistentPair, null, setSources);
        }

        return setSources.Count == 1
            ? new StampSourceDecision(StampSourceKind.Source, setSources[0], [])
            : new StampSourceDecision(StampSourceKind.AmbiguousSource, null, setSources);
    }

    /// <summary>True when the entity is a CORE record (direct grants required).</summary>
    public static bool IsCoreRecordEntity(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && CoreSet.Contains(entityLogicalName);

    /// <summary>True when the entity is a CHILD record (inherits via its core ancestor).</summary>
    public static bool IsChildRecordEntity(string entityLogicalName) =>
        !string.IsNullOrWhiteSpace(entityLogicalName) && ChildSet.Contains(entityLogicalName);

    /// <summary>
    /// Reports which of an entity's columns exist. The test seam for column presence — a delegate rather than
    /// a new interface (ADR-010). Production supplies <see cref="FromMetadata"/>, which is backed by the
    /// already-registered, 6h-cached <see cref="MetadataService"/>.
    /// </summary>
    public delegate Task<IReadOnlySet<string>> EntityColumnProbe(string entityLogicalName, CancellationToken ct);

    private readonly IGenericEntityService _entityService;
    private readonly EntityColumnProbe _columnProbe;
    private readonly ILogger<CoreAncestorResolver> _logger;

    public CoreAncestorResolver(
        IGenericEntityService entityService,
        EntityColumnProbe columnProbe,
        ILogger<CoreAncestorResolver> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _columnProbe = columnProbe ?? throw new ArgumentNullException(nameof(columnProbe));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The columns that exist on <paramref name="entityLogicalName"/>, from the same (6h-cached) probe derivation uses.
    /// For <see cref="CoreAncestorRestamper"/>, which must never select or write a column the table lacks. A failure
    /// PROPAGATES (fail closed — an unknown column set is not an empty one).
    /// </summary>
    internal Task<IReadOnlySet<string>> ProbeColumnsAsync(string entityLogicalName, CancellationToken ct)
        => _columnProbe(entityLogicalName, ct);

    /// <summary>
    /// Build an <see cref="EntityColumnProbe"/> backed by <see cref="MetadataService"/>. A metadata failure
    /// surfaces as an exception so the caller fails closed rather than reading an empty column set and
    /// concluding "no ancestor".
    /// </summary>
    public static EntityColumnProbe FromMetadata(MetadataService metadataService)
    {
        ArgumentNullException.ThrowIfNull(metadataService);
        return async (entityLogicalName, ct) =>
        {
            EntityMetadataDto meta = await metadataService.GetMetadataAsync(entityLogicalName, ct).ConfigureAwait(false);
            return new HashSet<string>(
                meta.Attributes.Select(a => a.LogicalName),
                StringComparer.OrdinalIgnoreCase);
        };
    }

    /// <summary>
    /// Resolve the CORE-record ancestor stamp(s) for a regarding target.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CORE target → the target itself, with no read at all. INTERMEDIATE target (<see cref="IntermediateRootColumns"/>:
    /// the child taxonomy plus agreement, budget and report card, task 156) → ONE read of the target's own root
    /// columns, then stop (ADR-034: no recursion, no grandparent walk). Anything else →
    /// <see cref="CoreAncestorStatus.Unclassified"/>.
    /// </para>
    /// <para>
    /// <b>Fail closed (NFR-01).</b> A read or metadata failure returns <see cref="CoreAncestorStatus.Error"/>;
    /// callers MUST fail the operation (or queue a retry per their existing error contract) rather than create
    /// a child that silently carries no inherited access. This method does not throw — the status is the
    /// contract, so a caller cannot accidentally swallow the failure in a broad catch.
    /// </para>
    /// <para>
    /// <see cref="CoreAncestorStatus.NoAncestor"/> and <see cref="CoreAncestorStatus.Error"/> are kept
    /// DISTINCT on purpose: "this record inherits nothing" (a legitimate orphan) and "we could not find out"
    /// must never share a branch.
    /// </para>
    /// </remarks>
    public async Task<CoreAncestorResult> ResolveStampsAsync(
        string targetEntityLogicalName,
        Guid targetRecordId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetEntityLogicalName))
        {
            return CoreAncestorResult.Failed("Target entity logical name is required.");
        }

        if (targetRecordId == Guid.Empty)
        {
            // Guid.Empty would silently match nothing and read as "no ancestor". Refuse it explicitly —
            // the same fail-closed-by-construction posture as DataverseImpersonation.
            return CoreAncestorResult.Failed(
                $"Target record id for '{targetEntityLogicalName}' is Guid.Empty; refusing to derive an ancestor.");
        }

        // --- CORE target: the target IS the ancestor. Its own parent associations are NOT ancestors —
        //     a Matter associated to a Project does not inherit from it (design.md §4.3). No read.
        if (IsCoreRecordEntity(targetEntityLogicalName))
        {
            var lookup = CoreAncestorLookups
                .First(c => string.Equals(c.EntityType, targetEntityLogicalName, StringComparison.OrdinalIgnoreCase));
            return new CoreAncestorResult(
                CoreAncestorStatus.CoreTarget,
                [new CoreAncestorStamp(lookup.EntityType, lookup.LookupAttribute, targetRecordId)],
                null);
        }

        // --- Neither core nor an intermediate with known root columns: no ancestor concept applies. Not an error.
        //     (Task 156: the intermediates are every type in IntermediateRootColumns — the child taxonomy plus
        //     agreement, budget and report card — not only ChildRecordEntities.)
        if (!IntermediateRootColumns.TryGetValue(targetEntityLogicalName, out var rootColumns))
        {
            return new CoreAncestorResult(CoreAncestorStatus.Unclassified, [], null);
        }

        // --- INTERMEDIATE target: read ITS root columns. This is the single hop.
        IReadOnlySet<string> columns;
        try
        {
            columns = await _columnProbe(targetEntityLogicalName, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // An empty column set is indistinguishable from "metadata unavailable", and guessing the
            // optimistic branch would write an unstamped child. Fail closed.
            _logger.LogError(ex,
                "Core-ancestor column probe failed for child target {Entity}; failing closed per NFR-01.",
                targetEntityLogicalName);
            return CoreAncestorResult.Failed(
                $"Could not read metadata for child target '{targetEntityLogicalName}': {ex.Message}");
        }

        var applicable = rootColumns
            .Where(c => columns.Contains(c.Column))
            .ToList();

        if (applicable.Count == 0)
        {
            // Carries none of its root columns in this org — its chain is already broken upstream.
            _logger.LogWarning(
                "Child target {Entity} has none of its root columns ({Lookups}); no stamp can be derived.",
                targetEntityLogicalName,
                string.Join(", ", rootColumns.Select(c => c.Column)));
            return new CoreAncestorResult(CoreAncestorStatus.NoAncestor, [], null);
        }

        Entity row;
        try
        {
            row = await _entityService
                .RetrieveAsync(targetEntityLogicalName, targetRecordId, applicable.Select(c => c.Column).ToArray(), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Core-ancestor read failed for {Entity}({Id}); failing closed per NFR-01.",
                targetEntityLogicalName, targetRecordId);
            return CoreAncestorResult.Failed(
                $"Failed to read core-ancestor lookups from {targetEntityLogicalName}({targetRecordId}): {ex.Message}");
        }

        if (row is null)
        {
            return CoreAncestorResult.Failed(
                $"Core-ancestor read returned no row for {targetEntityLogicalName}({targetRecordId}).");
        }

        var stamps = new List<CoreAncestorStamp>();
        foreach (var (column, rootEntity) in applicable)
        {
            var reference = row.GetAttributeValue<EntityReference>(column);
            if (reference is null || reference.Id == Guid.Empty)
            {
                continue;
            }

            var stampColumn = StampColumnFor(rootEntity);
            if (stamps.FirstOrDefault(s => string.Equals(s.EntityType, rootEntity, StringComparison.OrdinalIgnoreCase))
                is { } already)
            {
                if (already.RecordId != reference.Id)
                {
                    // Two columns of one root type naming DIFFERENT records (a document's sprk_matter and
                    // sprk_relatedmatter): the root is not known, and either choice is a guess. Fail closed.
                    _logger.LogError(
                        "{Entity}({Id}) names two different {Root} records ({First}, {Second}); refusing to derive a stamp.",
                        targetEntityLogicalName, targetRecordId, rootEntity, already.RecordId, reference.Id);
                    return CoreAncestorResult.Failed(
                        $"{targetEntityLogicalName}({targetRecordId}) names two different {rootEntity} records, so its "
                        + "root cannot be determined.");
                }

                continue;
            }

            stamps.Add(new CoreAncestorStamp(rootEntity, stampColumn, reference.Id));
        }

        if (stamps.Count == 0)
        {
            _logger.LogWarning(
                "Child target {Entity}({Id}) carries no core-ancestor stamp; a record written against it will inherit no access.",
                targetEntityLogicalName, targetRecordId);
            return new CoreAncestorResult(CoreAncestorStatus.NoAncestor, [], null);
        }

        return new CoreAncestorResult(CoreAncestorStatus.Derived, stamps, null);
    }

    /// <summary>
    /// Apply derived ancestor stamps to a child entity being written, skipping any the host cannot store.
    /// </summary>
    /// <remarks>
    /// A derived ancestor the host has no column for (a child entity lacking the matching
    /// <c>sprk_regarding{core}</c> column — e.g. <c>sprk_invoice</c>, which carries none of them; the earlier
    /// example, <c>sprk_todo</c> lacking <c>sprk_regardingservicerequest</c>, was stale as of live metadata on
    /// 2026-10-01) is a genuine hole in child inheritance. It is
    /// RETURNED as <c>unstampable</c> and logged, never silently dropped: it is a schema finding for the owner,
    /// not a runtime condition to paper over.
    /// </remarks>
    /// <param name="child">The child entity being built (mutated in place).</param>
    /// <param name="result">Output of <see cref="ResolveStampsAsync"/>.</param>
    /// <param name="hostColumns">Columns that exist on the host entity (from the same probe).</param>
    /// <param name="skipEntityType">The directly-bound target, whose lookup the caller already wrote.</param>
    /// <returns>Lookup attributes that could not be stamped because the host lacks the column.</returns>
    public IReadOnlyList<string> ApplyStamps(
        Entity child,
        CoreAncestorResult result,
        IReadOnlySet<string> hostColumns,
        string? skipEntityType = null)
    {
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(hostColumns);

        var unstampable = new List<string>();
        foreach (var stamp in result.Stamps)
        {
            if (skipEntityType is not null &&
                string.Equals(stamp.EntityType, skipEntityType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!hostColumns.Contains(stamp.LookupAttribute))
            {
                unstampable.Add(stamp.LookupAttribute);
                _logger.LogWarning(
                    "Derived core ancestor {Entity}({Id}) cannot be stamped on {Host}: no '{Lookup}' column. " +
                    "This record will NOT inherit that ancestor's access (FR-26 gap).",
                    stamp.EntityType, stamp.RecordId, child.LogicalName, stamp.LookupAttribute);
                continue;
            }

            child[stamp.LookupAttribute] = new EntityReference(stamp.EntityType, stamp.RecordId);
        }

        return unstampable;
    }

    /// <summary>
    /// Apply derived ancestor stamps to a field dictionary being written, skipping any the host cannot store.
    /// The <c>Dictionary&lt;string, object&gt;</c> shape used by
    /// <see cref="IGenericEntityService.UpdateAsync(string, Guid, Dictionary{string, object}, CancellationToken)"/>.
    /// </summary>
    /// <returns>Lookup attributes that could not be stamped because the host lacks the column.</returns>
    public IReadOnlyList<string> ApplyStamps(
        IDictionary<string, object> fields,
        string hostEntityLogicalName,
        CoreAncestorResult result,
        IReadOnlySet<string> hostColumns,
        string? skipEntityType = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(hostColumns);

        var unstampable = new List<string>();
        foreach (var stamp in result.Stamps)
        {
            if (skipEntityType is not null &&
                string.Equals(stamp.EntityType, skipEntityType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!hostColumns.Contains(stamp.LookupAttribute))
            {
                unstampable.Add(stamp.LookupAttribute);
                _logger.LogWarning(
                    "Derived core ancestor {Entity}({Id}) cannot be stamped on {Host}: no '{Lookup}' column. " +
                    "This record will NOT inherit that ancestor's access (FR-26 gap).",
                    stamp.EntityType, stamp.RecordId, hostEntityLogicalName, stamp.LookupAttribute);
                continue;
            }

            fields[stamp.LookupAttribute] = new EntityReference(stamp.EntityType, stamp.RecordId);
        }

        return unstampable;
    }

    // -------------------------------------------------------------------------
    // The single call a converged writer makes.
    //
    // Derivation + host-column probe + application are ONE operation because every writer that split
    // them would have to re-implement the same three failure branches, and one of them forgetting the
    // Succeeded check is a silent under-grant. The outcome record is the contract: a writer that
    // ignores it does not compile away the check, it fails review visibly.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Derive the target's core-record ancestors and stamp them onto the child <see cref="Entity"/> being
    /// written. The host entity is taken from <see cref="Entity.LogicalName"/>.
    /// </summary>
    /// <remarks>
    /// Call this AFTER the writer has bound its own typed regarding lookup: the directly-bound target is
    /// skipped (it is already written), and applying stamps last means an earlier pre-clear cannot null
    /// them — the same ordering the client's <c>buildRegardingSelectionPayload</c> enforces.
    /// <para>
    /// <b>Fail closed (NFR-01).</b> Inspect <see cref="CoreAncestorStampOutcome.Succeeded"/> and abort the
    /// write per the writer's own error contract when it is false. Never create the child anyway.
    /// </para>
    /// </remarks>
    public async Task<CoreAncestorStampOutcome> StampAsync(
        Entity child,
        string targetEntityLogicalName,
        Guid targetRecordId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(child);

        var (result, hostColumns, failure) =
            await DeriveAndProbeAsync(child.LogicalName, targetEntityLogicalName, targetRecordId, ct)
                .ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        var unstampable = ApplyStamps(child, result!, hostColumns!, skipEntityType: targetEntityLogicalName);

        // Task 173 (owner round 81): the inherited Access Permission rides the same payload, from every filing parent the
        // payload now names (the target, its stamps, and any carrier the writer set) — never Standard by default.
        var inherited = await InheritedAccessPermissionAsync(
            child.LogicalName,
            ParentLineage.ParentsIn(child.LogicalName, c => child.GetAttributeValue<EntityReference>(c)),
            targetEntityLogicalName, targetRecordId, ct, self: child.Id).ConfigureAwait(false);
        if (inherited is { } level)
        {
            child[InheritedAccessPermission.Column] = new OptionSetValue(level);
        }

        return new CoreAncestorStampOutcome(result!.Status, result.Stamps, unstampable, null)
        {
            InheritedAccessPermission = inherited,
        };
    }

    /// <summary>
    /// Dictionary-shaped sibling of <see cref="StampAsync(Entity, string, Guid, CancellationToken)"/> for
    /// writers that build an update payload rather than an <see cref="Entity"/>.
    /// </summary>
    public async Task<CoreAncestorStampOutcome> StampAsync(
        IDictionary<string, object> fields,
        string hostEntityLogicalName,
        string targetEntityLogicalName,
        Guid targetRecordId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var (result, hostColumns, failure) =
            await DeriveAndProbeAsync(hostEntityLogicalName, targetEntityLogicalName, targetRecordId, ct)
                .ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        var unstampable = ApplyStamps(
            fields, hostEntityLogicalName, result!, hostColumns!, skipEntityType: targetEntityLogicalName);

        // Task 173 (owner round 81): see the Entity overload.
        var inherited = await InheritedAccessPermissionAsync(
            hostEntityLogicalName,
            ParentLineage.ParentsIn(hostEntityLogicalName, c => fields.TryGetValue(c, out var v) ? v as EntityReference : null),
            targetEntityLogicalName, targetRecordId, ct).ConfigureAwait(false);
        if (inherited is { } level)
        {
            fields[InheritedAccessPermission.Column] = new OptionSetValue(level);
        }

        return new CoreAncestorStampOutcome(result!.Status, result.Stamps, unstampable, null)
        {
            InheritedAccessPermission = inherited,
        };
    }

    /// <summary>
    /// Derive the ancestors WITHOUT applying them — for writers whose payload is not an
    /// <see cref="Entity"/> or a dictionary (e.g. a hand-written JSON body). The caller emits each
    /// returned stamp in its own payload shape and must honour the same fail-closed contract.
    /// </summary>
    /// <param name="hostEntityLogicalName">Host entity, used only to report unstampable ancestors.</param>
    public async Task<CoreAncestorStampOutcome> DeriveForHostAsync(
        string hostEntityLogicalName,
        string targetEntityLogicalName,
        Guid targetRecordId,
        CancellationToken ct = default)
    {
        var (result, hostColumns, failure) =
            await DeriveAndProbeAsync(hostEntityLogicalName, targetEntityLogicalName, targetRecordId, ct)
                .ConfigureAwait(false);
        if (failure is not null)
        {
            return failure;
        }

        var stampable = new List<CoreAncestorStamp>();
        var unstampable = new List<string>();
        foreach (var stamp in result!.Stamps)
        {
            if (string.Equals(stamp.EntityType, targetEntityLogicalName, StringComparison.OrdinalIgnoreCase))
            {
                continue; // already bound by the caller
            }

            if (hostColumns!.Contains(stamp.LookupAttribute))
            {
                stampable.Add(stamp);
            }
            else
            {
                unstampable.Add(stamp.LookupAttribute);
                _logger.LogWarning(
                    "Derived core ancestor {Entity}({Id}) cannot be stamped on {Host}: no '{Lookup}' column. " +
                    "This record will NOT inherit that ancestor's access (FR-26 gap).",
                    stamp.EntityType, stamp.RecordId, hostEntityLogicalName, stamp.LookupAttribute);
            }
        }

        // Task 173 (owner round 81): the caller emits it in its own payload shape, as it emits the stamps.
        var inherited = await InheritedAccessPermissionAsync(
            hostEntityLogicalName,
            stampable.Select(s => (s.EntityType, s.RecordId)).ToList(),
            targetEntityLogicalName, targetRecordId, ct).ConfigureAwait(false);

        return new CoreAncestorStampOutcome(result.Status, stampable, unstampable, null)
        {
            InheritedAccessPermission = inherited,
        };
    }

    /// <summary>
    /// Task 173 (owner round 81): the Access Permission a row of <paramref name="hostEntityLogicalName"/> filed under
    /// <paramref name="parents"/> takes from them (<see cref="InheritedAccessPermission"/>), or <see langword="null"/> when
    /// it takes none — the host is not one of the four tables or lacks the column, the row names no parent (its own value
    /// stands), or the parents could not be read (logged as a warning; nothing is written, and the reconcile job corrects
    /// it within its cycle). Never throws (cancellation aside): the value is display-only, so it never fails a write.
    /// </summary>
    /// <remarks>
    /// For a writer that UPDATES an existing row with a field dictionary it did not hand to
    /// <see cref="StampAsync(IDictionary{string, object}, string, string, Guid, CancellationToken)"/> (the inbound association
    /// engine, which adds regarding lookups and never clears one). The row's parents are what it holds AFTER the update: each
    /// filing lookup from <paramref name="fields"/> when the update writes it, otherwise as stored on
    /// <paramref name="existingRowId"/> — so an added parent never hides one the row already has.
    /// </remarks>
    public async Task<int?> ResolveInheritedAccessPermissionAsync(
        string hostEntityLogicalName, IReadOnlyDictionary<string, object> fields, Guid existingRowId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (!InheritedAccessPermission.AppliesTo(hostEntityLogicalName))
            return null;

        Entity? stored = null;
        if (existingRowId != Guid.Empty)
        {
            try
            {
                // No read when the value could not be written anyway (the environment lacks the column).
                if (!(await _columnProbe(hostEntityLogicalName, ct).ConfigureAwait(false)).Contains(InheritedAccessPermission.Column))
                    return null;

                var walk = new ParentLineageWalk(_entityService, _columnProbe, []);
                var columns = await walk.FilingLookupsOfAsync(hostEntityLogicalName, ct).ConfigureAwait(false);
                stored = columns.Count == 0
                    ? new Entity(hostEntityLogicalName, existingRowId)
                    : await _entityService.RetrieveAsync(hostEntityLogicalName, existingRowId, columns.ToArray(), ct)
                        .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "[INHERITED-ACCESS-PERMISSION] {Host} {Id}'s stored filing could not be read; its inherited Access " +
                    "Permission is not written here, and the secure-child reconciliation job corrects it within its cycle.",
                    hostEntityLogicalName, existingRowId);
                return null;
            }
        }

        var parents = ParentLineage.ParentsIn(hostEntityLogicalName, column =>
            fields.TryGetValue(column, out var value) ? value as EntityReference : stored?.GetAttributeValue<EntityReference>(column));
        return await InheritedAccessPermissionAsync(hostEntityLogicalName, parents, null, Guid.Empty, ct, self: existingRowId)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Task 173 (owner round 81): after a BFF write that created a row or changed what it is filed under, through a writer
    /// whose payload did not pass through <c>StampAsync</c> (the client's generic create and re-file,
    /// <c>/api/v1/child-records/{table}</c>), set the row's <c>sprk_accesspermission</c> to the value its parents give it,
    /// when it differs. Reads the row as stored, so every filing parent counts. A parentless row is never written (its own
    /// value stands). Never throws (cancellation aside): a failure is logged and the reconcile job corrects it within its
    /// cycle.
    /// </summary>
    /// <returns>The value the row now holds from its parents, or <see langword="null"/> when it takes none or it could not
    /// be decided.</returns>
    public async Task<int?> RefreshInheritedAccessPermissionAsync(
        string entityLogicalName, Guid recordId, CancellationToken ct = default)
    {
        var table = entityLogicalName?.Trim().ToLowerInvariant();
        if (!InheritedAccessPermission.AppliesTo(table) || recordId == Guid.Empty)
            return null;

        try
        {
            var walk = new ParentLineageWalk(_entityService, _columnProbe, [InheritedAccessPermission.Column]);
            var columns = await walk.ColumnsForAsync(table!, ct).ConfigureAwait(false);
            if (!columns.Contains(InheritedAccessPermission.Column))
                return null; // the environment lacks the column (the schema script adds it)

            var row = await _entityService.RetrieveAsync(table!, recordId, columns.ToArray(), ct).ConfigureAwait(false);
            var parents = await walk.ParentsOfAsync(row, ct).ConfigureAwait(false);
            if (parents.Count == 0)
                return null;

            var answer = await InheritedAccessPermission.ResolveAsync(walk, parents, ct, (table!, recordId)).ConfigureAwait(false);
            if (answer.Status != ParentTopsStatus.Found || answer.Value is not { } level)
            {
                _logger.LogWarning(
                    "[INHERITED-ACCESS-PERMISSION] {Table} {Id}: its inherited Access Permission cannot be decided ({Reason}); " +
                    "left as it is, and the secure-child reconciliation job looks at it again.", table, recordId, answer.Reason);
                return null;
            }

            if (row.GetAttributeValue<OptionSetValue>(InheritedAccessPermission.Column)?.Value != level)
            {
                await _entityService.UpdateAsync(table!, recordId,
                    new Dictionary<string, object> { [InheritedAccessPermission.Column] = new OptionSetValue(level) }, ct)
                    .ConfigureAwait(false);
            }

            return level;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[INHERITED-ACCESS-PERMISSION] {Table} {Id}: its inherited Access Permission could not be set; the secure-child " +
                "reconciliation job corrects it within its cycle.", table, recordId);
            return null;
        }
    }

    private async Task<int?> InheritedAccessPermissionAsync(
        string hostEntityLogicalName,
        IReadOnlyList<(string Table, Guid Id)> payloadParents,
        string? targetEntityLogicalName,
        Guid targetRecordId,
        CancellationToken ct,
        Guid self = default)
    {
        if (!InheritedAccessPermission.AppliesTo(hostEntityLogicalName))
            return null;

        // The writer's target is a parent when the host can be filed under its table (a contact, an organization or a
        // service request is not: the lineage says so).
        var parents = payloadParents.ToList();
        if (targetEntityLogicalName is not null && targetRecordId != Guid.Empty
            && ParentLineage.CanBeFiledUnder(hostEntityLogicalName, targetEntityLogicalName))
        {
            parents.Add((targetEntityLogicalName, targetRecordId));
        }

        if (parents.Count == 0)
            return null; // parentless: the row's own value is the user's (round 81)

        try
        {
            var hostColumns = await _columnProbe(hostEntityLogicalName, ct).ConfigureAwait(false);
            if (!hostColumns.Contains(InheritedAccessPermission.Column))
            {
                // Writing a column the environment lacks would fail the create; the schema script adds it.
                _logger.LogWarning(
                    "{Host} has no {Column} column in this environment; its inherited Access Permission is not written.",
                    hostEntityLogicalName, InheritedAccessPermission.Column);
                return null;
            }

            var walk = new ParentLineageWalk(_entityService, _columnProbe, [InheritedAccessPermission.Column]);
            var answer = await InheritedAccessPermission.ResolveAsync(
                walk, parents, ct, self == Guid.Empty ? null : (hostEntityLogicalName, self)).ConfigureAwait(false);
            if (answer.Status == ParentTopsStatus.Found)
                return answer.Value;

            if (answer.Status == ParentTopsStatus.Undetermined)
            {
                _logger.LogWarning(
                    "[INHERITED-ACCESS-PERMISSION] A {Host} filed under {Parents} is written without its inherited Access " +
                    "Permission: {Reason}. The secure-child reconciliation job corrects it within its cycle.",
                    hostEntityLogicalName, string.Join(", ", parents.Select(p => $"{p.Table} {p.Id:D}")), answer.Reason);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[INHERITED-ACCESS-PERMISSION] A {Host}'s inherited Access Permission could not be resolved; it is written " +
                "without it, and the secure-child reconciliation job corrects it within its cycle.", hostEntityLogicalName);
            return null;
        }
    }

    private async Task<(CoreAncestorResult? Result, IReadOnlySet<string>? HostColumns, CoreAncestorStampOutcome? Failure)>
        DeriveAndProbeAsync(
            string hostEntityLogicalName,
            string targetEntityLogicalName,
            Guid targetRecordId,
            CancellationToken ct)
    {
        var result = await ResolveStampsAsync(targetEntityLogicalName, targetRecordId, ct).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return (null, null, CoreAncestorStampOutcome.Failed(result.Error ?? "Core-ancestor derivation failed."));
        }

        if (result.Stamps.Count == 0)
        {
            // Nothing to apply — skip the host probe entirely rather than pay a metadata call to
            // discover columns we will not write.
            return (result, EmptyColumns, null);
        }

        IReadOnlySet<string> hostColumns;
        try
        {
            hostColumns = await _columnProbe(hostEntityLogicalName, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // An unreadable host column set would make EVERY ancestor look unstampable — silently
            // downgrading a derivation success into a total inheritance loss. Fail closed instead.
            _logger.LogError(ex,
                "Core-ancestor host column probe failed for {Host}; failing closed per NFR-01.",
                hostEntityLogicalName);
            return (null, null, CoreAncestorStampOutcome.Failed(
                $"Could not read metadata for host '{hostEntityLogicalName}': {ex.Message}"));
        }

        return (result, hostColumns, null);
    }

    private static readonly IReadOnlySet<string> EmptyColumns =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Outcome of a core-ancestor derivation. A closed set of DISTINCT states — collapsing any two of them
/// produces either blocked valid writes or silently unstamped children.
/// </summary>
public enum CoreAncestorStatus
{
    /// <summary>Target is itself CORE. The stamp is the target; its own parents are NOT ancestors.</summary>
    CoreTarget,

    /// <summary>Target is CHILD and carried at least one core-ancestor stamp.</summary>
    Derived,

    /// <summary>Target is CHILD and every core-ancestor lookup is null. Legitimate; confers nothing.</summary>
    NoAncestor,

    /// <summary>
    /// Target is neither CORE nor an intermediate with known root columns (organization, contact, account). Since task
    /// 156 budget, agreement and report card are DERIVED from their own root columns.
    /// </summary>
    Unclassified,

    /// <summary>Derivation failed. The caller MUST NOT create the child unstamped (NFR-01).</summary>
    Error,
}

/// <summary>One resolved core-ancestor stamp to write onto the child being saved.</summary>
public sealed record CoreAncestorStamp(string EntityType, string LookupAttribute, Guid RecordId);

/// <summary>Which record on a child row its stamp is copied from (<see cref="CoreAncestorResolver.ClassifyStampSource"/>).</summary>
public enum StampSourceKind
{
    /// <summary>The row is not filed under any intermediate: its root columns are its own.</summary>
    NotFiledUnderAnIntermediate,

    /// <summary>The pair names a root set on the row — the user chose it; every intermediate set is a carrier.</summary>
    DirectRootLink,

    /// <summary>The stamp is copied from <see cref="StampSourceDecision.Source"/>.</summary>
    Source,

    /// <summary>More than one intermediate is set and nothing on the row says which one it is filed under.</summary>
    AmbiguousSource,

    /// <summary>The pair names a record the row does not carry (or is not a GUID): the row disagrees with itself.</summary>
    InconsistentPair,
}

/// <summary>One source column set on a child row: the column, the intermediate type it names, and that record's id.</summary>
public sealed record StampSourceLink(string Column, string Intermediate, Guid Id);

/// <summary>The outcome of <see cref="CoreAncestorResolver.ClassifyStampSource"/>.</summary>
/// <param name="Kind">The classification.</param>
/// <param name="Source">The intermediate the stamp is copied from — set only for <see cref="StampSourceKind.Source"/>.</param>
/// <param name="Carriers">Every other source column set on the row (for a direct link: all of them).</param>
public sealed record StampSourceDecision(StampSourceKind Kind, StampSourceLink? Source, IReadOnlyList<StampSourceLink> Carriers)
{
    /// <summary>The row is filed under no intermediate.</summary>
    public static readonly StampSourceDecision None = new(StampSourceKind.NotFiledUnderAnIntermediate, null, []);
}

/// <summary>Result of <see cref="CoreAncestorResolver.ResolveStampsAsync"/>.</summary>
public sealed record CoreAncestorResult(
    CoreAncestorStatus Status,
    IReadOnlyList<CoreAncestorStamp> Stamps,
    string? Error)
{
    /// <summary>True when the caller may proceed with the write.</summary>
    public bool Succeeded => Status != CoreAncestorStatus.Error;

    internal static CoreAncestorResult Failed(string error) =>
        new(CoreAncestorStatus.Error, [], error);
}

/// <summary>
/// Outcome of a converged writer's single stamping call — derivation, host-column probe, and application
/// collapsed into one result the writer must inspect before it writes.
/// </summary>
/// <param name="Status">Derivation status; <see cref="CoreAncestorStatus.Error"/> means DO NOT WRITE.</param>
/// <param name="Stamps">Ancestors that were applied (or, from <c>DeriveForHostAsync</c>, are the caller's to apply).</param>
/// <param name="Unstampable">
/// Ancestors derived but NOT written because the host entity has no column for them. A real hole in child
/// inheritance (F-050-2), surfaced rather than swallowed — never a reason to abort the write.
/// </param>
/// <param name="Error">Populated only when <see cref="Status"/> is <see cref="CoreAncestorStatus.Error"/>.</param>
public sealed record CoreAncestorStampOutcome(
    CoreAncestorStatus Status,
    IReadOnlyList<CoreAncestorStamp> Stamps,
    IReadOnlyList<string> Unstampable,
    string? Error)
{
    /// <summary>True when the caller may proceed with the write.</summary>
    public bool Succeeded => Status != CoreAncestorStatus.Error;

    /// <summary>
    /// Task 173 (owner round 81): the <c>sprk_accesspermission</c> the host row takes from the records it is filed under —
    /// already applied by <c>StampAsync</c>; a <c>DeriveForHostAsync</c> caller emits it in its own payload. <see langword="null"/>
    /// when the row takes none (not a To Do / Event / Communication / Document, parentless, or undetermined): write
    /// nothing then.
    /// </summary>
    public int? InheritedAccessPermission { get; init; }

    internal static CoreAncestorStampOutcome Failed(string error) =>
        new(CoreAncestorStatus.Error, [], [], error);
}
