using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using SearchIndexColumns = Sprk.Bff.Api.Services.Ai.SearchIndexNameResolver;

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
    /// The invoice family's core-record lookups on <paramref name="table"/>: a family row names exactly ONE core record, so
    /// a field-mapping rule may not fill one (ebilling-r1's review of task 013, 2026-10-21, item 3).
    /// </summary>
    /// <remarks>
    /// Task 017 (coordinator item 8): derived from the guard itself (<see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/>),
    /// per table, on all six family tables — so a mapping that fills a SECOND core lookup is skipped with a warning, never
    /// the create refused as <c>invoice_family_multiple_core_records</c>, and the two lists cannot drift. Was a hand-written
    /// (matter, project, regarding work assignment) set on sprk_invoice and sprk_billingevent only.
    /// </remarks>
    private static IReadOnlySet<string> InvoiceFamilyCoreLookupsOf(string table) =>
        Columns(InvoiceFamilyCoreRecordGuard.CoreRecordLookups[table].Select(l => l.Column).ToArray());

    /// <summary>
    /// Task 017 (#1608, owner decision 2b, 2026-10-22): the columns a caller's payload may never name on a secure ROOT — its
    /// own SPE container, the secure business unit it is isolated in, and its Access Permission. Was the child-records
    /// route's own list for a work assignment (ontology task 046, D-113 / task 175); now the ONE set, refused at step 2 of
    /// every create path, on a matter, a project and a work assignment alike.
    /// </summary>
    private static readonly IReadOnlySet<string> RootServerOwnedColumns = Columns(
        "sprk_containerid", SecurableEntityRegistry.SecurityBusinessUnitAttribute, InheritedAccessPermission.Column);

    /// <summary>
    /// ebilling-r1 FR-05 (ISS-010, #1973; #1911, merged): the invoice-family core-record rule — an invoice-family row names
    /// exactly one core record, and an invoice supersedes (a line belongs to) only an invoice of that record. Registered on
    /// every <see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/> table (task 017; task 012 notes,
    /// "Invoice-family reconciliation recipe"). It asks the SAME check the deleted owned create asked
    /// and the re-file still asks (<see cref="OwnedChildWrite.CheckInvoiceFamilyAsync"/>, create shape: no stored row), as the caller
    /// (<see cref="PreWriteRow.Caller"/>), on the row as it will be written; the guard owns the rule, this only maps its answer
    /// (<see cref="InvoiceFamilyPreWrite"/>). Declared before <see cref="All"/>: static initialisers run in textual order.
    /// </summary>
    internal static readonly PreWriteRule InvoiceFamilyRule = new(
        new NamedRule("InvoiceFamilyCoreRecord",
            "ebilling-r1 FR-05 (ISS-010, #1973): an invoice-family row names exactly one core record, and an invoice " +
            "supersedes only an invoice of that record; asked as the caller after AppendTo on every record the row binds."),
        async (row, ct) => InvoiceFamilyPreWrite(
            await OwnedChildWrite.CheckInvoiceFamilyAsync(row.Caller, row.Table, row.Item, stored: null, ct).ConfigureAwait(false)));

    /// <summary>
    /// The invoice-family check's answer as a pre-write refusal, in the child-record route's contract
    /// (<c>ChildRecordEndpoints.InvoiceFamilyRefused</c>): a record the row names that the caller cannot see, or the guard's
    /// <see cref="InvoiceFamilyCoreRecordGuard.RecordNotFoundCode"/>, is the uniform not-found (<see cref="PreWriteRefusal.NotFound"/>,
    /// the ONLY way to it); a read the guard needed that failed or a table it does not know
    /// (<see cref="InvoiceFamilyCoreRecordGuard.ReadFailedCode"/>, <see cref="InvoiceFamilyCoreRecordGuard.UnknownEntityCode"/>)
    /// is <see cref="PreWriteRefusal.Undecided"/> (500, fail closed); the caller's own Dataverse failure is carried; every
    /// other code is what the row names wrong (400), with the guard's own code and reason. A step that refuses nothing it can
    /// name is undecided, never "proceed". <c>null</c> only when the guard admits the row. Pure.
    /// </summary>
    internal static PreWriteRefusal? InvoiceFamilyPreWrite(OwnedChildWrite.InvoiceFamilyStep? step) => step switch
    {
        null => null,
        { ParentUnavailable: true } => PreWriteRefusal.NotFound(),
        { ClientFailure: { } failure } => PreWriteRefusal.Failed(failure),
        { Refusal.RefusalCode: InvoiceFamilyCoreRecordGuard.RecordNotFoundCode } => PreWriteRefusal.NotFound(),
        { Refusal: { RefusalCode: InvoiceFamilyCoreRecordGuard.ReadFailedCode or InvoiceFamilyCoreRecordGuard.UnknownEntityCode } refused } =>
            PreWriteRefusal.Undecided(refused.RefusalCode!, ReasonOf(refused)),
        { Refusal: { RefusalCode: { } code } refused } => PreWriteRefusal.Refused(code, ReasonOf(refused)),
        _ => PreWriteRefusal.Undecided(InvoiceFamilyCoreRecordGuard.ReadFailedCode,
            "The invoice-family check gave no answer; nothing was written."),
    };

    /// <summary>The guard's reason, or its code-only message when it gave none (<see cref="PreWriteRefusal"/> needs one).</summary>
    private static string ReasonOf(InvoiceFamilyCoreRecord refused) =>
        string.IsNullOrWhiteSpace(refused.Reason) ? InvoiceFamilyCoreRecordGuard.CodeOnlyRefusalMessage(refused.RefusalCode!) : refused.Reason;

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
            // Step 8 of the unified create (RecordCreationService.SearchIndexDefaultAsync) writes sprk_searchindexname /
            // sprk_ai_search_index from the owning business unit, and nothing else does: SearchIndexed makes both columns the
            // server's (FR-38, task 039). The Office core's ApplyBusinessUnitDefaultsAsync that used to fill them is deleted (018c).
            // Task 017 (#1608, decision 2b): refused in a caller's payload on every path, as on a work assignment.
            TableServerOwnedColumns = RootServerOwnedColumns,
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
            // Task 017 (#1608, decision 2b): refused in a caller's payload on every path, as on a work assignment.
            TableServerOwnedColumns = RootServerOwnedColumns,
            SearchIndexed = true,
            // Task 018a (F4-1): Office RecordCreationService.ApplyReferenceAsync — the project type (live column name
            // sprk_projecttype_ref, the lookup carries the table's name).
            OptionalReferenceLookups = Columns("sprk_projecttype_ref"),
        },
        new RecordTableRule
        {
            Table = "sprk_workassignment",
            Kind = RecordTableKind.Root,
            // FR-38 (owner, 2026-10-31, before task 039): server-owned search index. #2399 was a Work Assignment create.
            SearchIndexed = true,
            // Was ChildRecordEndpoints.RootServerOwnedColumns (ontology task 046, D-113 / task 175): its own SPE
            // container, the secure business unit it is isolated in, and its Access Permission. None is field-secured.
            // Task 017 (#1608, decision 2b): refused at step 2 of RecordCreationService on every path, the same set as a
            // matter and a project (RootServerOwnedColumns).
            TableServerOwnedColumns = RootServerOwnedColumns,
            ForPerson = new ForPersonLookup("sprk_assignedtointernal", "contact"),
            ServerDefaults =
            [
                ChatCallerIsTheForPerson("sprk_assignedtointernal"),
                // Task 037 (spec FR-36, owner 2026-10-27, #2354; UAC-r2 conditions 1-4): Assigned To takes the new row's own
                // Assigned To Internal. Both are CONTACT lookups (live metadata, spaarkedev1, 2026-10-27), so the reference
                // is copied as-is: no contact/user conversion, never a name or email match. The ONE declaration: the
                // RecordCreationService reads it through CopiedDefaultsFor (the owned create that also read it was deleted in 017).
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
            // FR-38 (owner, 2026-10-31, before task 039): server-owned search index.
            SearchIndexed = true,
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
        // ebilling-r1 #1911 (merged): the invoice-family pre-write rule (InvoiceFamilyRule) is registered on this table,
        // sprk_billingevent and the other four invoice-family tables (task 017; task 012 notes, "Invoice-family
        // reconciliation recipe").
        new RecordTableRule
        {
            Table = "sprk_invoice",
            Kind = RecordTableKind.Child,
            // FR-38 (owner, 2026-10-31, before task 039): server-owned search index (ebilling-r1 reviews this flag).
            SearchIndexed = true,
            // Task 014 (ebilling-r1's review of 013, item 3): a fill-only mapping that filled a second core lookup would fail
            // a valid create with multiple_core_records. The core record is the caller's to name.
            MappingProtectedColumns = InvoiceFamilyCoreLookupsOf("sprk_invoice"),
            PreWriteRules = [InvoiceFamilyRule],
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
            // Task 017 (owner decision 2b): and refused in a caller's payload — the access record is never a caller's to
            // name, for any principal (a system principal is refused it at step 2 too, with every other column an app-only
            // write is refused: InheritedAccessPermission.RefuseAppOnlyWrite). sprk_accesspermission stays the caller's.
            TableServerOwnedColumns = Columns(Sprk.Bff.Api.Services.Access.AccessInheritance.Column),
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
            MappingProtectedColumns = InvoiceFamilyCoreLookupsOf("sprk_billingevent"),
            PreWriteRules = [InvoiceFamilyRule],
        },

        // ── The rest of the invoice family (task 017): InvoiceFamilyCoreRecordGuard.CoreRecordLookups keys ──────────
        // Registered so the invoice-family rule guards every family table a server create path can write (the chat create
        // accepts any table but sprk_document; task 012 notes, recipe step 2). Their core lookups (the regarding trio) are
        // mapping-protected, as on an invoice (coordinator item 8).

        new RecordTableRule
        {
            Table = "sprk_invoicesubmission",
            Kind = RecordTableKind.Child,
            MappingProtectedColumns = InvoiceFamilyCoreLookupsOf("sprk_invoicesubmission"),
            PreWriteRules = [InvoiceFamilyRule],
        },
        new RecordTableRule
        {
            Table = "sprk_invoiceapproval",
            Kind = RecordTableKind.Child,
            MappingProtectedColumns = InvoiceFamilyCoreLookupsOf("sprk_invoiceapproval"),
            PreWriteRules = [InvoiceFamilyRule],
        },
        new RecordTableRule
        {
            Table = "sprk_externalidentifier",
            Kind = RecordTableKind.Child,
            MappingProtectedColumns = InvoiceFamilyCoreLookupsOf("sprk_externalidentifier"),
            PreWriteRules = [InvoiceFamilyRule],
        },
        new RecordTableRule
        {
            Table = "sprk_invoicecorrection",
            Kind = RecordTableKind.Child,
            MappingProtectedColumns = InvoiceFamilyCoreLookupsOf("sprk_invoicecorrection"),
            PreWriteRules = [InvoiceFamilyRule],
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

        // Task 017 (item 9): the budget inquiry's service request (BudgetInquiryExecutor, spec FR-36 of the ontology's task
        // 070), filed under sprk_regardingmatter with the typed ADR-024 pair and the outside firm (sprk_regardingorganization).
        // It declares nothing beyond the defaults, which is what the owned create (OwnedChildWrite.CreateAsync, deleted in 017)
        // applied to it: the common server-owned columns and the creator person refused in the caller's row, the resolver's owner
        // (its regarding matter's business-unit team), no create under a secure owner (it is no reparentable child), and the
        // regarding matter it carries as its own I-1 stamp. Registered so the service accepts it when 017c re-points the
        // inquiry (an unregistered table is record_creation.table_unsupported).
        new RecordTableRule { Table = "sprk_servicerequest", Kind = RecordTableKind.Child },

        // ── The chat create's offered platform tables (task 017, owner decision 2026-10-30, option b) ───────────────────
        // dataverse.create_record offers a task, a person (contact) and a company (account), and creates each today through
        // the owned create (OwnedChildWrite.PathFor → Owned: each is user-owned, none is per-user or a communication), so
        // the service must accept them once the chat moves onto it. Each declares only what that create applies today:
        // a CHILD (no root behaviour; RecordCreatorPerson stamps roots only); the common server-owned columns refused in
        // the caller's row and nothing table-specific; no "for" person and no server default; not search-indexed; no
        // ownership exception, so the owner is the resolver's team — a task's regarding (regardingobjectid, a lookup the
        // resolver's one builder reads like any other) decides it, an unfiled row gets the caller's business-unit team;
        // and refused under a secure owner (none is a reparentable child; the service's step 7 has the same rule). The
        // chat create applies no field mapping, so mapping is excluded by a named rule rather than left to apply.

        new RecordTableRule { Table = "task", Kind = RecordTableKind.Child, MappingExclusion = ChatPlatformTableIsNotMapped },
        new RecordTableRule { Table = "contact", Kind = RecordTableKind.Child, MappingExclusion = ChatPlatformTableIsNotMapped },
        new RecordTableRule { Table = "account", Kind = RecordTableKind.Child, MappingExclusion = ChatPlatformTableIsNotMapped },

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

    private static NamedRule ChatPlatformTableIsNotMapped => new(
        "ChatPlatformTableIsNotMapped",
        "Task 017 (owner decision 2026-10-30, option b): the chat create of a task, contact or account is registered to keep " +
        "the behaviour it had, and the owned create it had (OwnedChildWrite.CreateAsync, deleted in 017) applied no field mapping to it. Mapping these " +
        "platform tables would be new behaviour and needs its own decision.");

    // ── FR-38: the search index is server-owned (task 039) ──────────────────────────────────────────────────────────
    // ONE column check and ONE removal, shared by the unified create (RecordCreationService) and every BFF update path, so
    // they cannot drift. A drop, never a refusal; step 8 of the create is the only writer of either column.

    /// <summary>Where a dropped search-index value came from: the caller's payload (or a server writer's item).</summary>
    internal const string SearchIndexOriginCaller = "caller";

    /// <summary>Where a dropped search-index value came from: a field-mapping rule (step 3, or a push).</summary>
    internal const string SearchIndexOriginMapping = "mapping";

    /// <summary>Where a dropped search-index value came from: an adapter's post-mapping default (step 3½).</summary>
    internal const string SearchIndexOriginDefault = "default";

    private const string BindSuffix = "@odata.bind";

    /// <summary>
    /// FR-38: whether <paramref name="table"/> has a SERVER-OWNED search index, i.e. its registry entry is
    /// <see cref="RecordTableRule.SearchIndexed"/>. The one question every create and update path asks before it drops.
    /// </summary>
    internal static bool OwnsSearchIndex(string? table) => Find(table)?.SearchIndexed == true;

    /// <summary>
    /// FR-38: the index column a payload key names, or <c>null</c> for any other key. Matches the lookup
    /// (<c>sprk_ai_search_index</c>, its navigation property <c>sprk_AI_Search_Index</c> and either one's
    /// <c>@odata.bind</c>, in any case) and the legacy text column (<c>sprk_searchindexname</c>). The canonical logical
    /// name is returned.
    /// </summary>
    internal static string? SearchIndexColumnOf(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        var name = key.Trim();
        if (name.EndsWith(BindSuffix, StringComparison.OrdinalIgnoreCase))
            name = name[..^BindSuffix.Length].TrimEnd();

        if (string.Equals(name, SearchIndexColumns.LookupColumn, StringComparison.OrdinalIgnoreCase))
            return SearchIndexColumns.LookupColumn;
        return string.Equals(name, SearchIndexColumns.LegacyTextColumn, StringComparison.OrdinalIgnoreCase)
            ? SearchIndexColumns.LegacyTextColumn
            : null;
    }

    /// <summary>
    /// FR-38: removes the server-owned search-index columns from a Web API body or a chat tool's <c>item</c> (a JSON object
    /// keyed by column or <c>{nav}@odata.bind</c>), whatever the key's case, and records what was dropped. Returns
    /// <paramref name="body"/> itself when it names neither. Never a refusal: a malformed value is dropped like any other.
    /// </summary>
    internal static JsonElement WithoutSearchIndex(JsonElement body, string origin, ICollection<DroppedSearchIndexValue> dropped)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.EnumerateObject().Any(p => SearchIndexColumnOf(p.Name) is not null))
            return body;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in body.EnumerateObject())
            {
                if (SearchIndexColumnOf(property.Name) is { } column)
                    RecordDropped(dropped, column, origin, property.Value);
                else
                    property.WriteTo(writer);
            }

            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// FR-38: the same removal on a mapped item (<see cref="DataverseWriteItemMapper.MappedItem"/>): its body keys, its
    /// columns, its lookups and its clears. Returns <paramref name="item"/> itself when it names neither column.
    /// </summary>
    internal static DataverseWriteItemMapper.MappedItem WithoutSearchIndex(
        DataverseWriteItemMapper.MappedItem item, string origin, ICollection<DroppedSearchIndexValue> dropped)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var body = JsonDocument.Parse(item.JsonBody);
        var bodyNamesIt = body.RootElement.ValueKind == JsonValueKind.Object
                          && body.RootElement.EnumerateObject().Any(p => SearchIndexColumnOf(p.Name) is not null);
        if (!bodyNamesIt
            && !item.Columns.Any(c => SearchIndexColumnOf(c) is not null)
            && !item.Lookups.Any(l => SearchIndexColumnOf(l.Column) is not null)
            && !item.ClearedColumns.Any(c => SearchIndexColumnOf(c) is not null))
        {
            return item;
        }

        // The body first (it holds the value as sent); a lookup it does not carry in a readable form is read from the item's lookups.
        var json = bodyNamesIt ? WithoutSearchIndex(body.RootElement, origin, dropped).GetRawText() : item.JsonBody;
        foreach (var lookup in item.Lookups.Where(l => SearchIndexColumnOf(l.Column) is not null))
            RecordDropped(dropped, SearchIndexColumns.LookupColumn, origin, lookup.RecordId);

        foreach (var column in item.Columns.Concat(item.ClearedColumns).Select(SearchIndexColumnOf).OfType<string>())
            RecordDropped(dropped, column, origin, null);

        return item with
        {
            JsonBody = json,
            Columns = item.Columns.Where(c => SearchIndexColumnOf(c) is null).ToList(),
            Lookups = item.Lookups.Where(l => SearchIndexColumnOf(l.Column) is null).ToList(),
            ClearedColumns = item.ClearedColumns.Where(c => SearchIndexColumnOf(c) is null).ToList(),
        };
    }

    /// <summary>
    /// FR-38: the same removal, in place, on an update payload that is a dictionary of column (or <c>{nav}@odata.bind</c>) to
    /// value (a playbook output mapping, a confirmed proposal, a field-mapping push). Returns how many keys were removed.
    /// </summary>
    internal static int RemoveSearchIndex(IDictionary<string, object?> fields, string origin, ICollection<DroppedSearchIndexValue> dropped)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var keys = fields.Keys.Where(k => SearchIndexColumnOf(k) is not null).ToArray();
        foreach (var key in keys)
        {
            RecordDropped(dropped, SearchIndexColumnOf(key)!, origin, fields[key]);
            fields.Remove(key);
        }

        return keys.Length;
    }

    /// <summary>One dropped value: the column, where it came from, and the index it named (the id for the lookup, the name for the text column).</summary>
    internal sealed record DroppedSearchIndexValue(string Column, string Origin, Guid? IndexId, string? IndexName);

    private static void RecordDropped(ICollection<DroppedSearchIndexValue> dropped, string column, string origin, object? value)
    {
        var isLookup = string.Equals(column, SearchIndexColumns.LookupColumn, StringComparison.Ordinal);
        var id = isLookup ? IndexIdOf(value) : null;
        var name = isLookup ? null : IndexNameOf(value);

        var existing = dropped.FirstOrDefault(d => d.Column == column && d.Origin == origin);
        if (existing is null)
        {
            dropped.Add(new DroppedSearchIndexValue(column, origin, id, name));
        }
        else if ((id is not null && existing.IndexId is null) || (name is not null && existing.IndexName is null))
        {
            // The same column named twice (two spellings): keep whichever carried a value.
            dropped.Remove(existing);
            dropped.Add(existing with { IndexId = existing.IndexId ?? id, IndexName = existing.IndexName ?? name });
        }
    }

    /// <summary>The catalog row a dropped lookup value named: a bind string, a lookup object, an <see cref="EntityReference"/> or a Guid.</summary>
    private static Guid? IndexIdOf(object? value)
    {
        static Guid? FromText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.Trim();
            var open = trimmed.LastIndexOf('(');
            var close = trimmed.LastIndexOf(')');
            var candidate = open >= 0 && close > open ? trimmed[(open + 1)..close] : trimmed;
            return Guid.TryParse(candidate, out var id) && id != Guid.Empty ? id : null;
        }

        return value switch
        {
            Guid guid => guid == Guid.Empty ? null : guid,
            EntityReference reference => reference.Id == Guid.Empty ? null : reference.Id,
            string text => FromText(text),
            JsonElement { ValueKind: JsonValueKind.String } json => FromText(json.GetString()),
            JsonElement { ValueKind: JsonValueKind.Object } json
                when json.TryGetProperty("recordId", out var recordId) && recordId.ValueKind == JsonValueKind.String =>
                FromText(recordId.GetString()),
            _ => null,
        };
    }

    /// <summary>The index name a dropped text value named (trimmed; none when blank or not text).</summary>
    private static string? IndexNameOf(object? value)
    {
        var text = value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static readonly Regex IndexNameShape = new(
        @"\A[a-z0-9](?:[a-z0-9-]{0,126}[a-z0-9])?\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Review R1 (UAC-r2 condition 4): the ONE way an index name reaches a log. A name is logged only when it has the shape of
    /// an Azure AI Search index name (<c>\A[a-z0-9](?:[a-z0-9-]{0,126}[a-z0-9])?\z</c>; known limit: lowercase-hyphen free text has this shape); anything else is a client's free text
    /// (an email, a sentence) and is logged as <c>&lt;not an index name&gt; (length N)</c>. <c>null</c> stays none.
    /// </summary>
    internal static string? LoggableIndexName(string? name)
    {
        if (name is null)
            return null;

        try
        {
            return IndexNameShape.IsMatch(name) ? name : $"<not an index name> (length {name.Length})";
        }
        catch (RegexMatchTimeoutException)
        {
            return $"<not an index name> (length {name.Length})";
        }
    }

    /// <summary>
    /// FR-38 (UAC-r2 condition 4): logs <c>client_search_index_dropped</c>, ids only, for each dropped value that differs from
    /// what was applied. <paramref name="applied"/> is what step 8 applied to a new record (the id for the lookup, the name
    /// for the text column), or <c>null</c> on an update, where the value is "unchanged" and so never equal. Equal means:
    /// both none, the same id, or the same name compared trimmed and ordinal-ignore-case. The text column's entry also carries
    /// the dropped and applied index NAME (catalog configuration, not personal data); nothing else textual is logged.
    /// Warning level, as 018's default_value_dropped is a warning.
    /// </summary>
    internal static void LogSearchIndexDropped(
        ILogger logger, string table, Guid? recordId, IEnumerable<DroppedSearchIndexValue> dropped,
        (Guid? IndexId, string? IndexName)? applied, string? correlationId = null)
    {
        foreach (var drop in dropped)
        {
            var isLookup = string.Equals(drop.Column, SearchIndexColumns.LookupColumn, StringComparison.Ordinal);
            if (applied is { } now
                && (isLookup
                    ? drop.IndexId == now.IndexId
                    : string.Equals(drop.IndexName ?? string.Empty, now.IndexName?.Trim() ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            const string unchanged = "unchanged";
            if (isLookup)
            {
                logger.LogWarning(
                    "[RECORD-WRITE] client_search_index_dropped: table={Table} record={RecordId} column={Column} origin={Origin} " +
                    "droppedIndexId={DroppedIndexId} appliedIndexId={AppliedIndexId} correlationId={CorrelationId}",
                    table, recordId, drop.Column, drop.Origin, drop.IndexId, applied is { } a ? a.IndexId : unchanged, correlationId);
            }
            else
            {
                logger.LogWarning(
                    "[RECORD-WRITE] client_search_index_dropped: table={Table} record={RecordId} column={Column} origin={Origin} " +
                    "droppedIndexName={DroppedIndexName} appliedIndexName={AppliedIndexName} correlationId={CorrelationId}",
                    table, recordId, drop.Column, drop.Origin, LoggableIndexName(drop.IndexName),
                    applied is { } b ? LoggableIndexName(b.IndexName) : unchanged, correlationId);
            }
        }
    }

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
/// <see cref="Detail"/> — what the row names wrong (<see cref="Refused"/>), or a question the rule could not decide
/// (<see cref="Undecided"/>, task 017); the caller's own Dataverse failure (<see cref="Failed"/>); or a record the row names
/// that the caller cannot see (<see cref="NotFound"/>, answered as the uniform not-found — the only refusal that is). The
/// factories are the only way to make one, so a refusal with no reason cannot exist.
/// </summary>
internal sealed record PreWriteRefusal
{
    private PreWriteRefusal(
        string? code, string? detail, DataverseUserResponse? clientFailure, bool parentUnavailable, bool isUndecided = false)
    {
        Code = code;
        Detail = detail;
        ClientFailure = clientFailure;
        ParentUnavailable = parentUnavailable;
        IsUndecided = isUndecided;
    }

    /// <summary>The rule's stable refusal code (<see cref="Refused"/>, <see cref="Undecided"/>); <c>null</c> for a client failure or a not-found.</summary>
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

    /// <summary>
    /// Task 017 (ebilling-r1 F2): the rule could not decide whether the row may be written (a read it needed failed, a table it
    /// does not know), with a stable code; fail closed. Answered as "not saved" (500), never as the uniform not-found.
    /// </summary>
    public bool IsUndecided { get; }

    /// <summary>A record the row names cannot be seen by the caller (or does not exist).</summary>
    public static PreWriteRefusal NotFound() => new(null, null, null, true);

    /// <summary>The rule could not decide, with a stable code (see <see cref="IsUndecided"/>).</summary>
    public static PreWriteRefusal Undecided(string code, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        return new(code, detail, null, false, isUndecided: true);
    }
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

    /// <summary>
    /// Whether the table's search index is SERVER-OWNED (FR-38, amending FR-10, I-5): both columns
    /// (<c>sprk_ai_search_index</c> and the legacy text <c>sprk_searchindexname</c>) are written by step 8 alone, from the
    /// record's final owning business unit, and a client value is dropped on every BFF write path
    /// (<see cref="RecordTableRules.SearchIndexColumnOf"/>, <see cref="RecordTableRules.OwnsSearchIndex"/>).
    /// </summary>
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
