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

    /// <summary>
    /// The invoice family's core-record lookups on <c>sprk_invoice</c> and <c>sprk_billingevent</c> — ebilling-r1's
    /// <c>InvoiceFamilyCoreRecordGuard.CoreRecordLookups</c> (<c>TypedMatterProjectPlusRegardingWorkAssignment</c>, on
    /// <c>work/ebilling-r1</c>): a row of either names exactly ONE core record, so a field-mapping rule may not fill one
    /// (ebilling-r1's review of task 013, 2026-10-21, item 3). Declared before <see cref="All"/>: static initialisers run
    /// in textual order.
    /// </summary>
    private static readonly IReadOnlySet<string> InvoiceFamilyCoreLookups =
        Columns("sprk_matter", "sprk_project", CoreAncestorResolver.StampColumnFor("sprk_workassignment"));

    /// <summary>
    /// The secure flag and the access columns of a secure root (matter, project): the BFF's own, written only through the
    /// secure endpoints and the Secure Record setup, never by a field-mapping rule (#2220). Compile-time constants only, so
    /// declaring them here cannot create a static-initialisation cycle. Declared before <see cref="All"/>.
    /// <c>internal</c> (#2251): the update side reads the same set (<c>InheritedAccessPermission.SecureRootColumns</c>,
    /// refused to the app-only generic writers on every root), so create and update cannot drift.
    /// </summary>
    internal static readonly IReadOnlyList<string> SecureRootAccessColumns =
    [
        SecurableEntityRegistry.SecureFlagAttribute,
        InheritedAccessPermission.Column,
        Sprk.Bff.Api.Services.Access.AccessInheritance.Column,
        SecurableEntityRegistry.SecurityBusinessUnitAttribute,
    ];

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
            //
            // #2220: and the secure flag and access columns (SecureRootAccessColumns): the BFF's own on a matter, as on a
            // project and a work assignment.
            MappingProtectedColumns = Columns(
                ["sprk_matternumber", "ownerid", "sprk_containerid", RecordCreatorPerson.Column, .. SecureRootAccessColumns]),
            // scripts/Set-RecordNumberingSchema.ps1: MAT-{SEQNUM:6}, alternate key sprk_MatterNumber.
            AutonumberColumn = "sprk_matternumber",
            ForPerson = new ForPersonLookup("sprk_assignedtointernal", "contact"),
            ServerDefaults =
            [
                new ServerDefault("sprk_assignedtointernal", "Office quick-create",
                    "The contact the request names, else a mapped value, else the maker's linked contact (Office " +
                    "RecordCreationService, tasks 100 and 152)."),
                ChatCallerIsTheForPerson("sprk_assignedtointernal"),
            ],
            // Office RecordCreationService.ApplyBusinessUnitDefaultsAsync fills sprk_searchindexname / sprk_ai_search_index.
            SearchIndexed = true,
            // Task 018a (F4-1): Office RecordCreationService.ApplyReferenceAsync — the matter type and practice area are
            // dropped with a warning when they cannot be used, never refused.
            OptionalReferenceLookups = Columns("sprk_mattertype", "sprk_practicearea"),
        },
        new RecordTableRule
        {
            Table = "sprk_project",
            Kind = RecordTableKind.Root,
            // Was Office RecordCreationService.ProjectProtectedAttributes: the same protections as a matter with the
            // project number, plus the secure flag (task 158 r1: the BFF's own). Per table on purpose: a merged set would
            // newly skip a rule targeting sprk_projectnumber on a MATTER create (task 031).
            //
            // #2220: and the access columns (SecureRootAccessColumns), so the Office path and the unified create protect the
            // same four as a matter and a work assignment.
            MappingProtectedColumns = Columns(
                ["sprk_projectnumber", "ownerid", "sprk_containerid", RecordCreatorPerson.Column, .. SecureRootAccessColumns]),
            // scripts/Set-RecordNumberingSchema.ps1: PRJ-{SEQNUM:6}, alternate key sprk_ProjectNumber.
            AutonumberColumn = "sprk_projectnumber",
            ForPerson = new ForPersonLookup("sprk_assignedtointernal", "contact"),
            ServerDefaults =
            [
                new ServerDefault("sprk_assignedtointernal", "Office quick-create",
                    "The contact the request names, else a mapped value, else the maker's linked contact (Office " +
                    "RecordCreationService, tasks 100 and 152)."),
                ChatCallerIsTheForPerson("sprk_assignedtointernal"),
            ],
            SearchIndexed = true,
            // Task 018a (F4-1): Office RecordCreationService.ApplyReferenceAsync — the project type (live column name
            // sprk_projecttype_ref, the lookup carries the table's name).
            OptionalReferenceLookups = Columns("sprk_projecttype_ref"),
        },
        new RecordTableRule
        {
            Table = "sprk_workassignment",
            Kind = RecordTableKind.Root,
            // Was ChildRecordEndpoints.RootServerOwnedColumns (ontology task 046, D-113 / task 175): its own SPE
            // container, the secure business unit it is isolated in, and its Access Permission. None is field-secured.
            // Enforced today ONLY by POST /api/v1/child-records/{table} for a root; the chat create tool does not refuse
            // them (#1608; task 012 notes, "Pre-existing path difference"). Which paths enforce them is task 013's decision.
            TableServerOwnedColumns = Columns(
                "sprk_containerid", SecurableEntityRegistry.SecurityBusinessUnitAttribute, InheritedAccessPermission.Column),
            ForPerson = new ForPersonLookup("sprk_assignedtointernal", "contact"),
            ServerDefaults =
            [
                ChatCallerIsTheForPerson("sprk_assignedtointernal"),
                // Task 037 (spec FR-36, owner 2026-10-27, #2354; UAC-r2 conditions 1-4): Assigned To takes the new row's own
                // Assigned To Internal. Both are CONTACT lookups (live metadata, spaarkedev1, 2026-10-27), so the reference
                // is copied as-is: no contact/user conversion, never a name or email match. The ONE declaration: the
                // owned create (OwnedChildWrite.CreateAsync) and RecordCreationService read it through CopiedDefaultsFor.
                new ServerDefault(AssignedToDefaults.AssignedToAttribute,
                    "every user-initiated server create of a work assignment (the child-records route, the chat create, " +
                    "the record-creation service with a calling user)",
                    "The new row's own sprk_assignedtointernal (after mapping), copied as-is, when the request leaves " +
                    "sprk_assignedto unset (absent, no bind); a supplied value or an explicit null wins. Create only.")
                {
                    CopiedFrom = "sprk_assignedtointernal",
                },
            ],
        },

        // ── Children on the browser create route (ChildRecordEndpoints.CreateTables) ──────────────────────────────────

        new RecordTableRule
        {
            Table = "sprk_todo",
            Kind = RecordTableKind.Child,
            ForPerson = new ForPersonLookup(AssignedToDefaults.AssignedToAttribute, "contact"),
            ServerDefaults =
            [
                new ServerDefault(AssignedToDefaults.AssignedToAttribute, "server writers",
                    "The triggering person's contact, else the regarding parent's responsible internal contact " +
                    "(AssignedToDefaults; TodoGenerationService and TaskActionCore)."),
                ChatCallerIsTheForPerson(AssignedToDefaults.AssignedToAttribute),
            ],
        },
        new RecordTableRule
        {
            Table = "sprk_event",
            Kind = RecordTableKind.Child,
            ForPerson = new ForPersonLookup(AssignedToDefaults.AssignedToAttribute, "contact"),
            ServerDefaults =
            [
                new ServerDefault(AssignedToDefaults.AssignedToAttribute, "server writers",
                    "The triggering person's contact, else the regarding parent's responsible internal contact " +
                    "(AssignedToDefaults)."),
                ChatCallerIsTheForPerson(AssignedToDefaults.AssignedToAttribute),
            ],
        },
        new RecordTableRule { Table = "sprk_memo", Kind = RecordTableKind.Child },
        // ebilling-r1 #1911 (not on master): the invoice-family pre-write rule registers on this table, sprk_billingevent
        // and the other four invoice-family tables (task 012 notes, "Invoice-family reconciliation recipe").
        new RecordTableRule
        {
            Table = "sprk_invoice",
            Kind = RecordTableKind.Child,
            // Task 014 (ebilling-r1's review of 013, item 3): a fill-only mapping that filled a second core lookup would fail
            // a valid create with multiple_core_records. The core record is the caller's to name.
            MappingProtectedColumns = InvoiceFamilyCoreLookups,
        },
        new RecordTableRule { Table = "sprk_reportcard", Kind = RecordTableKind.Child },
        new RecordTableRule { Table = "sprk_analysis", Kind = RecordTableKind.Child },
        new RecordTableRule
        {
            Table = "sprk_document",
            Kind = RecordTableKind.Child,
            // OWNER REVIEW: in no old list. Spec FR-10's acceptance names Document with Matter and Project; no create
            // path fills a document's search index today (grep sprk_searchindexname: only the Office Matter/Project create).
            SearchIndexed = true,
            // Task 014 (unified-access-control-r2 D7 / #2169, 2026-10-21): the document's access record — the inherited
            // floor and the document's own narrowing — is written by the BFF only, never by a field-mapping rule (the push
            // skips it too, task 040). sprk_accesspermission is NOT here: it stays fill-only at create.
            MappingProtectedColumns = Columns(Sprk.Bff.Api.Services.Access.AccessInheritance.Column),
        },
        new RecordTableRule
        {
            Table = "sprk_budget",
            Kind = RecordTableKind.Child,
            // Task 167 (D-193): BUD-{SEQNUM:6}, set on the attribute in SpaarkeMaster Entities/sprk_Budget/Entity.xml. The platform fills
            // it; no code sets it, and a field-mapping rule may not either.
            MappingProtectedColumns = Columns("sprk_budgetnumber"),
            AutonumberColumn = "sprk_budgetnumber",
        },
        new RecordTableRule { Table = "sprk_kpiassessment", Kind = RecordTableKind.Child },
        new RecordTableRule
        {
            Table = "sprk_billingevent",
            Kind = RecordTableKind.Child,
            // Task 014 (ebilling-r1's review of 013, item 3): as sprk_invoice.
            MappingProtectedColumns = InvoiceFamilyCoreLookups,
        },

        // ── Other server create tables named by the old lists ────────────────────────────────────────────────────────

        new RecordTableRule
        {
            Table = "sprk_communication",
            Kind = RecordTableKind.Child,
            ForPerson = new ForPersonLookup("sprk_sentby", "systemuser"),
            ServerDefaults = [ChatCallerIsTheForPerson("sprk_sentby")],
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

    /// <summary>
    /// Task 037 (FR-36): what <paramref name="rule"/>'s copied defaults (<see cref="ServerDefault.CopiedFrom"/>) come to on
    /// the create row <paramref name="item"/> — one entry per copied default whose column the row leaves UNSET (absent: no
    /// value, no bind and no clear; an explicit null is a value the caller chose and wins), with the lookup the row binds
    /// in its source column, or <c>null</c> when the source is unset or cleared too (the column then stays blank). Pure:
    /// the create paths apply the answer, each through its own checks, before the create and so before the "Assigned *"
    /// materialisation that reads the created row. Never read by an update, the repair pass or a push (create only).
    /// </summary>
    internal static IReadOnlyList<CopiedDefaultFill> CopiedDefaultsFor(RecordTableRule? rule, DataverseWriteItemMapper.MappedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (rule is null)
            return [];

        var fills = new List<CopiedDefaultFill>();
        foreach (var serverDefault in rule.ServerDefaults)
        {
            if (serverDefault.CopiedFrom is not { } source
                || item.Columns.Contains(serverDefault.Column, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var bound = item.Lookups.FirstOrDefault(l => string.Equals(l.Column, source, StringComparison.OrdinalIgnoreCase));
            fills.Add(new CopiedDefaultFill(serverDefault, bound));
        }

        return fills;
    }

    /// <summary>The chat create's "for" person: the caller, when the item leaves the column unset (owner round 7 item 3).</summary>
    private static ServerDefault ChatCallerIsTheForPerson(string column) => new(column, "chat create (dataverse.create_record)",
        "The caller, when the item leaves it unset (DataverseCreateRecordHandler.WithForPersonAsync, owner round 7 item 3).");

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

/// <summary>
/// A value the server fills on create when the request leaves the column empty (fill-only), the create path that fills
/// it today, and its rule. OWNER REVIEW: the paths differ today (the chat create names the caller, the Office create and
/// the server writers apply their own precedence); task 013 decides the one default the unified service applies.
/// </summary>
internal sealed record ServerDefault(string Column, string Path, string Rule)
{
    /// <summary>
    /// Task 037 (FR-36): the column of the SAME new row whose lookup this default copies, as-is, when the create leaves
    /// <see cref="Column"/> unset; <c>null</c> for a default that only describes what a path fills (every other entry).
    /// Applied by the create paths through <see cref="RecordTableRules.CopiedDefaultsFor"/>; create only.
    /// </summary>
    public string? CopiedFrom { get; init; }
}

/// <summary>
/// Task 037: one copied default a create row leaves unset (<see cref="RecordTableRules.CopiedDefaultsFor"/>), and the lookup
/// the row binds in its source column — <c>null</c> when the source is unset or cleared too.
/// </summary>
internal sealed record CopiedDefaultFill(ServerDefault Default, DataverseWriteItemMapper.MappedLookup? Source);

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
/// A pre-write rule's refusal. Nothing is created. Exactly one of: a stable refusal <see cref="Code"/> with its
/// <see cref="Detail"/> (<see cref="Refused"/>); the caller's own Dataverse failure (<see cref="Failed"/>); or a record
/// the row names that the caller cannot see (<see cref="NotFound"/>, answered as the uniform not-found). The factories
/// are the only way to make one, so a refusal with no reason cannot exist.
/// </summary>
internal sealed record PreWriteRefusal
{
    private PreWriteRefusal(string? code, string? detail, DataverseUserResponse? clientFailure, bool parentUnavailable)
    {
        Code = code;
        Detail = detail;
        ClientFailure = clientFailure;
        ParentUnavailable = parentUnavailable;
    }

    /// <summary>The rule's stable refusal code; <c>null</c> for a client failure or a not-found.</summary>
    public string? Code { get; }

    /// <summary>Why, in words the caller can act on; set with <see cref="Code"/>.</summary>
    public string? Detail { get; }

    /// <summary>The caller's own Dataverse failure on a read the rule needed.</summary>
    public DataverseUserResponse? ClientFailure { get; }

    /// <summary>A record the row names that the caller cannot see (the uniform not-found).</summary>
    public bool ParentUnavailable { get; }

    /// <summary>Refused by the rule with a stable code.</summary>
    public static PreWriteRefusal Refused(string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new(code, detail, null, false);
    }

    /// <summary>A read the rule needed failed under the caller's token.</summary>
    public static PreWriteRefusal Failed(DataverseUserResponse clientFailure)
    {
        ArgumentNullException.ThrowIfNull(clientFailure);
        return new(null, null, clientFailure, false);
    }

    /// <summary>A record the row names cannot be seen by the caller (or does not exist).</summary>
    public static PreWriteRefusal NotFound() => new(null, null, null, true);
}

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

    /// <summary>
    /// Task 018a (F4-1): the lookups a create request may mark as optional REFERENCES
    /// (<see cref="RecordCreationRequest.ReferenceLookupColumns"/>) — dropped with a warning when the caller may not use
    /// them, never refused. Exactly what the Office quick-create drops today (matter type, practice area, project type).
    /// Never the "for" person, a column field mapping may not write, the regarding pair, a filing lookup, or an
    /// access-conferring or Assigned* column (<see cref="RecordCreationService.IsNeverAReference"/>; asserted by the
    /// registry tests). Empty when the table has none.
    /// </summary>
    public IReadOnlySet<string> OptionalReferenceLookups { get; init; } = NoColumns;

    /// <summary>Why field mapping is NOT applied to this table; <c>null</c> when it is.</summary>
    public NamedRule? MappingExclusion { get; init; }

    /// <summary>Whether field mapping applies on create (FR-02). False only through a named <see cref="MappingExclusion"/>.</summary>
    public bool MappingApplies => MappingExclusion is null;

    /// <summary>The table's exception to the record-first team owner (FR-09); <c>null</c> when there is none.</summary>
    public NamedRule? OwnershipException { get; init; }

    /// <summary>Table-keyed rules on the row as it will be written (see <see cref="PreWriteRule"/>). Empty when none.</summary>
    public IReadOnlyList<PreWriteRule> PreWriteRules { get; init; } = [];
}
