using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.RecordCreation;

/// <summary>
/// The per-table create rules (field-mapping-server-write-path-r1 task 012, spec FR-11): ONE declarative registry that
/// holds, for every table a server create path writes, whether it is a root or a child, the columns the server owns or
/// a mapping rule may not write, its autonumber column, its "for"-person column, its server defaults, whether it is
/// search-indexed (FR-10), whether field mapping applies (FR-02), any ownership exception, and any pre-write rule.
/// </summary>
/// <remarks>
/// <para><b>Declares, never enforces (ADR-002).</b> The registry is data plus lookups. The create paths read it; the
/// unified <c>RecordCreationService</c> (task 013) becomes the one place that enforces it. Until then the five lists it
/// replaces are VIEWS over it, with the same member names and types, so every consumer compiles and behaves unchanged:
/// <see cref="OwnedChildWrite.ServerOwnedColumns"/> and <see cref="OwnedChildWrite.ForPersonColumns"/>,
/// <c>ChildRecordEndpoints.RootServerOwnedColumns</c>, and the Office <c>RecordCreationService</c>'s
/// <c>MatterProtectedAttributes</c> / <c>ProjectProtectedAttributes</c>. Tasks 013, 014 and 018 switch the consumers to
/// the registry and delete the views.</para>
/// <para><b>Per-table differences are kept, not merged.</b> Each value is what the old list held for that table, scoped
/// as the old list was scoped. "Protected columns" (FR-11) are two different policies today and stay two properties:
/// <see cref="RecordTableRule.TableServerOwnedColumns"/> (a CALLER's payload naming one is refused) and
/// <see cref="RecordTableRule.MappingProtectedColumns"/> (a field-mapping RULE targeting one is skipped with a warning).
/// A value that is not in any old list carries its evidence in a comment and an <c>OWNER REVIEW</c> marker
/// (task 012 notes, "Values for owner review").</para>
/// <para><b>OWNER REVIEW (defaults).</b> Field mapping applies unless a table names a <see cref="RecordTableRule.MappingExclusion"/>:
/// a table with no mapping profile maps nothing (the Office create's "missing profile is a silent no-op"), so the default
/// widens nothing. Spec FR-16 names the wizard tables; <c>sprk_memo</c>, <c>sprk_budget</c>, <c>sprk_kpiassessment</c>,
/// <c>sprk_billingevent</c> and <c>sprk_communication</c> are not among them and take the default. The
/// <see cref="RecordTableRule.ServerDefaults"/> are read from today's server writers (cited per entry), not from an old
/// list; tasks 013 and 015 confirm them when they consume them.</para>
/// <para><b>ADR-010.</b> A static class: no DI registration, no interface.</para>
/// <para><b>Component justification (CLAUDE.md §11).</b> Existing — five lists in three files
/// (<c>MatterProtectedAttributes</c>, <c>ProjectProtectedAttributes</c>, <c>ServerOwnedColumns</c>,
/// <c>RootServerOwnedColumns</c>, <c>ForPersonColumns</c>); no registry. Extension — the lists live in three cores that
/// will not survive W2 (the Office service is deleted in 018, <c>OwnedChildWrite</c>'s create half moves in 013), so the
/// single source goes in the unified service's folder and the lists become views over it. Cost of doing nothing — the
/// protected and server-owned columns keep drifting between create paths (they already differ per core), and task 013
/// has no single place to read per-table policy, search-index and mapping applicability.</para>
/// </remarks>
internal static class RecordTableRules
{
    /// <summary>
    /// Columns the server owns on EVERY app-only create, whatever the table: a caller's payload that sets one is refused,
    /// never honoured. (Was <c>OwnedChildWrite.ServerOwnedColumns</c>, which is now a view over this set.) The creator
    /// person (<see cref="RecordCreatorPerson.Column"/>) is refused alongside these by
    /// <see cref="RecordCreatorPerson.NamesColumn"/>, on the tables that carry it.
    /// </summary>
    internal static readonly IReadOnlySet<string> CommonServerOwnedColumns = Columns(
        "ownerid", "owninguser", "owningteam", "owningbusinessunit",
        "createdby", "createdon", "createdonbehalfby",
        "modifiedby", "modifiedon", "modifiedonbehalfby",
        "overriddencreatedon", "importsequencenumber");

    /// <summary>Every registered table, in declaration order.</summary>
    internal static IReadOnlyList<RecordTableRule> All { get; } =
    [
        // ── Roots (the three secure roots: RecordCreatorPerson.StampedTables, SecureRootInheritance) ──────────────────

        new RecordTableRule
        {
            Table = "sprk_matter",
            Kind = RecordTableKind.Root,
            // Was Office RecordCreationService.MatterProtectedAttributes: the number (platform autonumber, task 076),
            // the load-bearing owner, the storage container (unified-access-control-r2 task 076 W1) and the creator
            // person (task 133).
            MappingProtectedColumns = Columns("sprk_matternumber", "ownerid", "sprk_containerid", RecordCreatorPerson.Column),
            // scripts/Set-RecordNumberingSchema.ps1: MAT-{SEQNUM:6}, alternate key sprk_MatterNumber.
            AutonumberColumn = "sprk_matternumber",
            ForPerson = new ForPersonLookup("sprk_assignedtointernal", "contact"),
            ServerDefaults =
            [
                new ServerDefault("sprk_assignedtointernal",
                    "The contact the request names, else a mapped value, else the maker's linked contact (Office " +
                    "RecordCreationService, tasks 100 and 152)."),
            ],
            // Office RecordCreationService.ApplyBusinessUnitDefaultsAsync fills sprk_searchindexname / sprk_ai_search_index.
            SearchIndexed = true,
        },
        new RecordTableRule
        {
            Table = "sprk_project",
            Kind = RecordTableKind.Root,
            // Was Office RecordCreationService.ProjectProtectedAttributes: the same protections as a matter with the
            // project number, plus the secure flag (task 158 r1: the BFF's own). Per table on purpose: a merged set would
            // newly skip a rule targeting sprk_projectnumber on a MATTER create (task 031).
            MappingProtectedColumns = Columns(
                "sprk_projectnumber", "ownerid", "sprk_containerid", RecordCreatorPerson.Column, "sprk_issecure"),
            // scripts/Set-RecordNumberingSchema.ps1: PRJ-{SEQNUM:6}, alternate key sprk_ProjectNumber.
            AutonumberColumn = "sprk_projectnumber",
            ForPerson = new ForPersonLookup("sprk_assignedtointernal", "contact"),
            ServerDefaults =
            [
                new ServerDefault("sprk_assignedtointernal",
                    "The contact the request names, else a mapped value, else the maker's linked contact (Office " +
                    "RecordCreationService, tasks 100 and 152)."),
            ],
            SearchIndexed = true,
        },
        new RecordTableRule
        {
            Table = "sprk_workassignment",
            Kind = RecordTableKind.Root,
            // Was ChildRecordEndpoints.RootServerOwnedColumns (ontology task 046, D-113 / task 175): its own SPE
            // container, the secure business unit it is isolated in, and its Access Permission. None is field-secured.
            // Enforced today ONLY by POST /api/v1/child-records/{table} for a root; the chat create tool does not refuse
            // them (#1608; task 012 notes, "Pre-existing path difference"). Which paths enforce them is task 013's decision.
            TableServerOwnedColumns = Columns("sprk_containerid", "sprk_securitybu", "sprk_accesspermission"),
            ForPerson = new ForPersonLookup("sprk_assignedtointernal", "contact"),
        },

        // ── Children on the browser create route (ChildRecordEndpoints.CreateTables) ──────────────────────────────────

        new RecordTableRule
        {
            Table = "sprk_todo",
            Kind = RecordTableKind.Child,
            ForPerson = new ForPersonLookup(AssignedToDefaults.AssignedToAttribute, "contact"),
            ServerDefaults =
            [
                new ServerDefault(AssignedToDefaults.AssignedToAttribute,
                    "The triggering person's contact, else the regarding parent's responsible internal contact " +
                    "(AssignedToDefaults; the server writers TodoGenerationService and TaskActionCore)."),
            ],
        },
        new RecordTableRule
        {
            Table = "sprk_event",
            Kind = RecordTableKind.Child,
            ForPerson = new ForPersonLookup(AssignedToDefaults.AssignedToAttribute, "contact"),
            ServerDefaults =
            [
                new ServerDefault(AssignedToDefaults.AssignedToAttribute,
                    "The triggering person's contact, else the regarding parent's responsible internal contact " +
                    "(AssignedToDefaults)."),
            ],
        },
        new RecordTableRule { Table = "sprk_memo", Kind = RecordTableKind.Child },
        // ebilling-r1 #1911 (not on master): the invoice-family pre-write rule registers on this table and on
        // sprk_billingevent (task 012 notes, "Invoice-family reconciliation recipe").
        new RecordTableRule { Table = "sprk_invoice", Kind = RecordTableKind.Child },
        new RecordTableRule { Table = "sprk_reportcard", Kind = RecordTableKind.Child },
        new RecordTableRule { Table = "sprk_analysis", Kind = RecordTableKind.Child },
        new RecordTableRule
        {
            Table = "sprk_document",
            Kind = RecordTableKind.Child,
            // OWNER REVIEW: in no old list. Spec FR-10's acceptance names Document with Matter and Project; no create
            // path fills a document's search index today (grep sprk_searchindexname: only the Office Matter/Project create).
            SearchIndexed = true,
        },
        new RecordTableRule { Table = "sprk_budget", Kind = RecordTableKind.Child },
        new RecordTableRule { Table = "sprk_kpiassessment", Kind = RecordTableKind.Child },
        new RecordTableRule { Table = "sprk_billingevent", Kind = RecordTableKind.Child },

        // ── Other server create tables named by the old lists ────────────────────────────────────────────────────────

        new RecordTableRule
        {
            Table = "sprk_communication",
            Kind = RecordTableKind.Child,
            ForPerson = new ForPersonLookup("sprk_sentby", "systemuser"),
            OwnershipException = new NamedRule(
                "UnfiledCommunicationKeepsCreator",
                "Escalation E1: a communication filed under nothing keeps its creator (UnfiledOwnership.KeepCreator; " +
                "OwnedChildWrite.PathFor / KeepsItsCreatorWhenUnfiled). The per-user master thread and Direct-thread " +
                "privacy rest on it; a team owner would show one person's mail to their whole business unit."),
        },

        // ── Ontology writers' tables: never mapped (spaarke-ontology-platform-r1 §0.3) ──────────────────────────────

        new RecordTableRule
        {
            Table = "sprk_signal",
            Kind = RecordTableKind.Child,
            MappingExclusion = OntologyContentIsNotMapped,
        },
        new RecordTableRule
        {
            Table = "sprk_decisionrecord",
            Kind = RecordTableKind.Child,
            MappingExclusion = OntologyContentIsNotMapped,
        },
        new RecordTableRule
        {
            Table = "sprk_budgetrevision",
            Kind = RecordTableKind.Child,
            MappingExclusion = OntologyContentIsNotMapped,
        },
    ];

    private static readonly IReadOnlyDictionary<string, RecordTableRule> ByTable =
        All.ToDictionary(r => r.Table, StringComparer.OrdinalIgnoreCase);

    /// <summary>The rule of <paramref name="table"/>, or <c>null</c> when the registry has no entry for it.</summary>
    internal static RecordTableRule? Find(string? table) =>
        table is not null && ByTable.TryGetValue(table.Trim(), out var rule) ? rule : null;

    /// <summary>The rule of <paramref name="table"/>; throws when the registry has no entry (a view over a known table).</summary>
    internal static RecordTableRule Get(string table) =>
        Find(table) ?? throw new KeyNotFoundException($"The record table rules registry has no entry for '{table}'.");

    private static NamedRule OntologyContentIsNotMapped => new(
        "OntologyContentIsNotMapped",
        "spaarke-ontology-platform-r1 §0.3 binds a Signal's sprk_sentence and fact snapshot and a Decision Record's " +
        "content: no field mapping is applied to these rows (ontology-r1 answer, 2026-10-19; w2-coordination.md).");

    private static IReadOnlySet<string> Columns(params string[] columns) =>
        new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Whether a table's rows are roots (filed under nothing they inherit security from) or children.</summary>
internal enum RecordTableKind
{
    /// <summary>A root: matter, project, work assignment.</summary>
    Root,

    /// <summary>A child or content row filed under a root (or under nothing).</summary>
    Child,
}

/// <summary>The column a table names the person a record is FOR in, and the table that column looks up.</summary>
internal sealed record ForPersonLookup(string Column, string RelatedTable);

/// <summary>A value the server fills on create when the request leaves the column empty (fill-only), and its rule.</summary>
internal sealed record ServerDefault(string Column, string Rule);

/// <summary>A named exception to the default policy, with the reason it exists. The rationale is never empty.</summary>
internal sealed record NamedRule
{
    public NamedRule(string name, string rationale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(rationale);
        Name = name;
        Rationale = rationale;
    }

    public string Name { get; }

    public string Rationale { get; }
}

/// <summary>
/// The row a pre-write rule decides on: the table and the payload AS IT WILL BE WRITTEN (caller values merged with mapped
/// values), with the caller's own Dataverse client for any read the rule needs as the caller (ADR-028).
/// </summary>
internal sealed record PreWriteRow(IDataverseUserClient Caller, string Table, DataverseWriteItemMapper.MappedItem Item);

/// <summary>
/// A pre-write rule's refusal. Nothing is created. Exactly one of: a stable refusal <paramref name="Code"/> with its
/// <paramref name="Detail"/>; the caller's own Dataverse failure (<paramref name="ClientFailure"/>); or a record the row
/// names that the caller cannot see (<paramref name="ParentUnavailable"/>, answered as the uniform not-found).
/// </summary>
internal sealed record PreWriteRefusal(
    string? Code,
    string? Detail,
    DataverseUserResponse? ClientFailure = null,
    bool ParentUnavailable = false);

/// <summary>A pre-write check: <c>null</c> when the row may be written.</summary>
internal delegate Task<PreWriteRefusal?> PreWriteCheck(PreWriteRow row, CancellationToken ct);

/// <summary>
/// A table-keyed rule that runs on the row as it will be written, AFTER the caller checks (Create / Append privilege and
/// AppendTo on every record the row binds — FR-06 step 5) and BEFORE the secure-root plan, the owner and the create
/// (steps 6 to 11). It refuses with a stable code; the create path maps the code to its own contract. The
/// <see cref="NamedRule.Rationale"/> is never empty.
/// </summary>
internal sealed record PreWriteRule(NamedRule Rule, PreWriteCheck Check);

/// <summary>One table's create rules (see <see cref="RecordTableRules"/>).</summary>
internal sealed record RecordTableRule
{
    private static readonly IReadOnlySet<string> NoColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The table's logical name.</summary>
    public required string Table { get; init; }

    /// <summary>Root or child.</summary>
    public required RecordTableKind Kind { get; init; }

    /// <summary>
    /// Protected columns, caller side: columns of THIS table, beyond <see cref="RecordTableRules.CommonServerOwnedColumns"/>,
    /// that a caller's payload may not set. Empty when the table has none.
    /// </summary>
    public IReadOnlySet<string> TableServerOwnedColumns { get; init; } = NoColumns;

    /// <summary>
    /// Protected columns, mapping side: columns a field-mapping rule may not write on this table (skipped with a warning).
    /// Empty when no column is protected from mapping.
    /// </summary>
    public IReadOnlySet<string> MappingProtectedColumns { get; init; } = NoColumns;

    /// <summary>The column the platform autonumber fills (the create leaves it empty and retries on its alternate key).</summary>
    public string? AutonumberColumn { get; init; }

    /// <summary>The column naming the person the record is FOR; <c>null</c> when the table has none.</summary>
    public ForPersonLookup? ForPerson { get; init; }

    /// <summary>Values the server fills on create when the request leaves them empty (fill-only).</summary>
    public IReadOnlyList<ServerDefault> ServerDefaults { get; init; } = [];

    /// <summary>Whether the owning business unit's search-index default is filled on create (FR-10, I-5).</summary>
    public bool SearchIndexed { get; init; }

    /// <summary>Why field mapping is NOT applied to this table; <c>null</c> when it is.</summary>
    public NamedRule? MappingExclusion { get; init; }

    /// <summary>Whether field mapping applies on create (FR-02). False only through a named <see cref="MappingExclusion"/>.</summary>
    public bool MappingApplies => MappingExclusion is null;

    /// <summary>The table's exception to the record-first team owner (FR-09); <c>null</c> when there is none.</summary>
    public NamedRule? OwnershipException { get; init; }

    /// <summary>Table-keyed rules on the row as it will be written (see <see cref="PreWriteRule"/>). Empty when none.</summary>
    public IReadOnlyList<PreWriteRule> PreWriteRules { get; init; } = [];
}
