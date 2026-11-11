using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Services.Access;

/// <summary>
/// unified-access-control-r2 task 149 (C10 part 2, sharees) — which tables can hold a CHILD of a secure record, and which
/// of their lookups file a row under what. The secure-child share synchronizer walks these lookups upward from each
/// Secure-team-owned row to the secure roots it descends from.
/// </summary>
/// <remarks>
/// <para><b>Where the map comes from.</b> Live metadata, read-only, spaarkedev1 2026-10-02:
/// <c>EntityDefinitions(&lt;table&gt;)/ManyToOneRelationships</c> for every table of the codified Secure Record Owner role set
/// (<c>config/secure-record-owner-role.json</c>, kind <c>child</c>), keeping each lookup whose target is a root or another
/// table of that set. Entity-set names from <c>EntityDefinitions(&lt;table&gt;)?$select=EntitySetName</c> (note
/// <c>sprk_analysises</c>). Every relationship read had Share, Unshare, Reparent and Assign = <c>NoCascade</c> — the platform
/// shares nothing to children, which is why this map exists (notes/task-149-secure-child-sharee-access.md §2).</para>
/// <para><b>Every lookup is kept, on purpose.</b> A row's secure roots are the roots reachable through ANY of its lookups,
/// and a child's mirror is the INTERSECTION of their share sets (escalation trigger 2, fail closed; owner decision, round
/// 11 item 4, 2026-10-03). An extra lookup can
/// only add a root, which can only narrow the intersection; a dropped lookup could leave out the root with the fewest
/// sharees and widen it. So links that are not "filing" in the business sense (a document's current version, its
/// canonical copy, an analysis's output file) are kept too — they can never widen what a child receives.</para>
/// <para><b>Deliberately absent.</b> <c>sprk_servicerequest</c> lookups: a service request is a root of its own that the
/// ownership resolver does not look through (<c>RecordOwnershipResolver.IsReparentableChild</c>), so a row filed only under
/// a service request is never Secure-team-owned. Lookups to identity tables (contact, account, systemuser) are
/// relationships, not parents.</para>
/// <para>Pinned by <c>SecureChildLineageTests</c>: every codified child table has an entry, and every lookup targets a root
/// or a codified child.</para>
/// <para><b>The order of a table's lookups is meaningful (UAC task 194b).</b> It is the order in which an EXISTING row's parents
/// are named to the ownership resolver (<see cref="Table.LookupOrder"/>, read by
/// <c>RecordOwnershipContext.OrderedParentsOf</c>), and the first one the row holds is its PRIMARY parent: the one whose
/// business unit decides an ordinary row's owner. It matches the row's writer: a document's parent document, then its typed core
/// lookups, then its <c>sprk_related*</c> links (<c>DocumentAssociationMap</c>, which the document creates file through); an analysis's document
/// (<c>AnalysisEndpoints</c>, <c>AppOnlyAnalysisService</c>); a Decision Record's typed core. A Signal and a Decision Record
/// name their core record and subject first (<c>RecordOwnershipContext.ForExistingRowAsync</c>). Secure-if-any reads every
/// parent, so the order never decides whether a row is secure.</para>
/// <para><b>The core record pair is a parent too (Signals and Decision Records).</b> A row of
/// <c>RecordOwnershipContext.CorePairTables</c> names its core record in a pair, not a lookup: <c>sprk_corerecordtype</c> (a
/// <c>sprk_recordtype_ref</c> catalog row) + <c>sprk_corerecordid</c> (a string). A Signal has no typed project or work-assignment
/// column, so a Signal whose core is a project or work assignment, and whose subject is another record, reached its core only
/// through the subject's stamp, and re-filing the subject (D-123) dropped the core from its roots. So every walk reads the pair
/// beside the lookups (<see cref="Table.LineageColumns"/>) and adds the record it names as a parent
/// (<see cref="CorePairParent"/>): a root when the catalog names a matter, project or work assignment; nothing for a service
/// request (D-36: a root the resolver does not look through, as the remarks above); undetermined for a malformed pair
/// (<see cref="MalformedCorePair"/>) and for a type the catalog maps to no core table, its table name blank or not one of the
/// closed set of core tables (<see cref="UnknownCoreRecordType"/>): <c>ForExistingRowAsync</c>'s words and rule, so the walk
/// holds such a row as the ownership question does, and the data error is seen (a warning per type per run). A catalog read
/// that faults, a missing catalog row included, is a read fault like any other parent read: it propagates, and the walk's
/// caller retries. The pair is the same rule on both
/// tables; a Decision Record whose typed core lookup names the same record adds no second root. Adding a root can only narrow
/// the intersection, so the pair never widens what a child receives. The pair is kept OUT of <see cref="Table.Lookups"/>:
/// readers of the map as an edge registry (ownership order, filing edges, the form library) see only lookups.</para>
/// </remarks>
internal static class SecureChildLineage
{
    /// <summary>One table: its entity set (for POA writes), its lookups to parents (column → target table) and its external
    /// marker (unified-access-control-r2 task 191b: what an external caller's visibility asks of the row itself; required, so
    /// a table cannot be added without that decision).</summary>
    internal sealed record Table(
        string LogicalName, string EntitySet, IReadOnlyDictionary<string, string> Lookups, ExternalMarker External)
    {
        /// <summary>Task 191b: the table's own external marker (<see cref="ExternalMarker"/>). Never null.</summary>
        public ExternalMarker External { get; init; } = External ?? throw new ArgumentNullException(nameof(External));

        /// <summary>UAC task 194b: <see cref="Lookups"/>' columns in declaration order, the ownership primary order (see the
        /// class remarks).</summary>
        public IReadOnlyList<string> LookupOrder { get; init; } = Lookups.Keys.ToArray();

        /// <summary>The table's primary id column (every Spaarke table: logical name + <c>id</c>, verified live).</summary>
        public string IdColumn => LogicalName + "id";

        /// <summary>Whether the table's rows name a core record in a pair (<see cref="SecureChildLineage.HoldsCorePair"/>).</summary>
        public bool HoldsCorePair => SecureChildLineage.HoldsCorePair(LogicalName);

        /// <summary>
        /// Every column a lineage walk reads to find a row's parents: <see cref="Lookups"/>, then the core record pair on a
        /// table that holds one (see the class remarks). A row read without them would look as if it named no core record.
        /// </summary>
        public IReadOnlyList<string> LineageColumns =>
            HoldsCorePair ? Lookups.Keys.Concat(CorePairColumns).ToArray() : Lookups.Keys.ToArray();
    }

    /// <summary>The core record pair's two columns, in the order <see cref="Table.LineageColumns"/> appends them.</summary>
    internal static readonly IReadOnlyList<string> CorePairColumns =
    [
        Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.CoreRecordTypeColumn,
        Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.CoreRecordIdColumn,
    ];

    /// <summary>The record-type catalog a pair's type names (read through <c>CoreAncestorResolver.CoreTableOfRecordTypeAsync</c>).</summary>
    internal const string RecordTypeCatalogTable = "sprk_recordtype_ref";

    /// <summary>Why a row whose core record pair is malformed has undetermined roots (<c>ForExistingRowAsync</c>'s words).</summary>
    internal const string MalformedCorePair = "its core record pair is malformed";

    /// <summary>Why a row whose core record pair's type maps to no core table (a blank or non-core <c>sprk_recordlogicalname</c>)
    /// has undetermined roots (<c>ForExistingRowAsync</c>'s words).</summary>
    internal const string UnknownCoreRecordType = "its core record type is not a core record table in the record-type catalog";

    /// <summary>
    /// The text forms of a core id the downward walk matches <c>sprk_corerecordid</c> against: every form <c>Guid.TryParse</c>
    /// (<c>RecordOwnershipContext.CorePairOf</c>) accepts that a writer could plausibly store — <c>D</c> (its writers' form),
    /// <c>B</c> (<c>{...}</c>), <c>P</c> (<c>(...)</c>) and <c>N</c> (32 hex digits). The hex-structure <c>X</c> form is left out:
    /// no writer stores it.
    /// </summary>
    internal static readonly IReadOnlyList<string> StoredCoreIdFormats = ["D", "B", "P", "N"];

    /// <summary>True for a table whose rows name a core record in a pair: <c>RecordOwnershipContext.CorePairTables</c>.</summary>
    internal static bool HoldsCorePair(string? logicalName) =>
        logicalName is not null && Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.CorePairTables.Contains(logicalName);

    /// <summary>
    /// The parent a whole core record pair adds to a row's lineage once its type has been read through the catalog
    /// (<c>CoreAncestorResolver.CoreTableOfRecordTypeAsync</c>): the core record when it is one of the three roots; <c>null</c>
    /// for a service request (D-36). A type the catalog maps to no core table (<c>null</c> here) is the caller's to hold as
    /// undetermined (<see cref="UnknownCoreRecordType"/>) before it asks; a malformed pair never reaches here either
    /// (<see cref="MalformedCorePair"/>). Pure.
    /// </summary>
    internal static (string Table, Guid Id)? CorePairParent(string? coreTable, Guid coreId) =>
        coreTable is not null && IsRoot(coreTable) ? (coreTable.ToLowerInvariant(), coreId) : null;

    /// <summary>
    /// The conditions a DOWNWARD walk adds to a query of <paramref name="table"/> to find its rows whose core record pair names
    /// one of <paramref name="frontier"/>'s roots: (<c>sprk_corerecordid</c>, the id in each of
    /// <see cref="StoredCoreIdFormats"/>, lowercase; Dataverse compares text case-insensitively). Empty for a table with no pair
    /// or a frontier with no root. The type is not matched here: each row found is decided by its own upward walk (or ownership
    /// question), which reads the type. <paramref name="alsoServiceRequests"/>: the ORDINARY-parent-move pass only (F4-b), whose
    /// root can be a service request and whose ownership question takes a service-request core as the row's primary parent;
    /// the secure walks never pass it (D-36: a service request is no secure root).
    /// </summary>
    /// <remarks>Known limits. A stored id with leading or trailing whitespace is not matched (the text compare is exact; the
    /// upward walk and the ownership question still read it, as <c>CorePairOf</c> trims). <c>sprk_corerecordid</c> is an
    /// unindexed text column, so the <c>In</c> condition scans the table: acceptable at today's volumes, to revisit for
    /// production volume.</remarks>
    internal static IEnumerable<(string Column, object Value)> CorePairMatches(
        string table, IEnumerable<(string Table, Guid Id)> frontier, bool alsoServiceRequests = false) =>
        HoldsCorePair(table)
            ? frontier
                .Where(r => IsRoot(r.Table)
                    || (alsoServiceRequests && string.Equals(r.Table, ServiceRequest, StringComparison.OrdinalIgnoreCase)))
                .SelectMany(r => StoredCoreIdFormats.Select(format =>
                    (Sprk.Bff.Api.Services.Dataverse.RecordOwnershipContext.CoreRecordIdColumn,
                        (object)r.Id.ToString(format, System.Globalization.CultureInfo.InvariantCulture).ToLowerInvariant())))
            : [];

    private const string ServiceRequest = "sprk_servicerequest";

    private const string Project = "sprk_project";
    private const string Matter = "sprk_matter";
    private const string WorkAssignment = "sprk_workassignment";

    /// <summary>The three secure roots, from the ONE root table (never re-listed).</summary>
    internal static readonly IReadOnlySet<string> Roots = new HashSet<string>(
        Enum.GetValues<ExternalGrantRootType>().Select(ExternalGrantRoot.LogicalNameFor), StringComparer.OrdinalIgnoreCase);

    private static Table T(
        string logicalName, string entitySet, ExternalMarker external, params (string Column, string Target)[] lookups) =>
        new(logicalName, entitySet, lookups.ToDictionary(l => l.Column, l => l.Target, StringComparer.OrdinalIgnoreCase), external)
        {
            LookupOrder = lookups.Select(l => l.Column).ToArray(),
        };

    /// <summary>UAC task 194b: a document's primary order — its parent document, then its typed core lookups, then the rest in
    /// declaration order (the <c>sprk_related*</c> links), as a document's parent link and <c>DocumentAssociationMap</c> name them.</summary>
    private static int DocumentPrimaryRank(string column) => column switch
    {
        "sprk_parentdocument" => 0,
        Matter or Project or WorkAssignment => 1,
        _ => 2,
    };

    /// <summary>Document link targets this SECURE lineage map leaves out: identities and the email activity (relationships, not
    /// parents), and a service request — which IS an ordinary ownership and filing parent (<c>RecordOwnershipResolver.FilingParentOf</c>;
    /// the ordinary pass walks it), but a root of its own that the Secure lineage does not list (the class remarks).</summary>
    private static readonly HashSet<string> DocumentLinksThatAreNotFiling = new(StringComparer.OrdinalIgnoreCase)
    {
        "contact", "sprk_organization", "email", "sprk_servicerequest",
    };

    // The "regarding" lookups most activity-like children share.
    private static (string, string)[] Regarding(params string[] targets) =>
        targets.Select(t => ($"sprk_regarding{t["sprk_".Length..]}", t)).ToArray();

    /// <summary>Every child table, keyed by logical name.</summary>
    internal static readonly IReadOnlyDictionary<string, Table> Children = new[]
    {
        T("sprk_agreement", "sprk_agreements", ExternalMarker.None,
            Regarding("sprk_document", Matter, Project)),
        T("sprk_analysis", "sprk_analysises", ExternalMarker.DocumentText,
            new[] { ("sprk_documentid", "sprk_document"), ("sprk_outputfileid", "sprk_document") }
                .Concat(Regarding("sprk_budget", "sprk_communication", "sprk_document", "sprk_invoice", Matter, Project, WorkAssignment))
                .ToArray()),
        T("sprk_analysisoutput", "sprk_analysisoutputs", ExternalMarker.DocumentText,
            ("sprk_analysisid", "sprk_analysis")),
        T("sprk_attachmentartifact", "sprk_attachmentartifacts", ExternalMarker.Never,
            ("sprk_document", "sprk_document"), ("sprk_emailartifact", "sprk_emailartifact")),
        // ebilling-r1 task 013 (FR-05): + sprk_regardingworkassignment (ebilling-r1 task 010, live spaarkedev1 2026-10-15).
        T("sprk_billingevent", "sprk_billingevents", ExternalMarker.None,
            ("sprk_invoice", "sprk_invoice"), ("sprk_matter", Matter), ("sprk_project", Project),
            ("sprk_regardingworkassignment", WorkAssignment)),
        T("sprk_budget", "sprk_budgets", ExternalMarker.None,
            ("sprk_matter", Matter), ("sprk_project", Project)),
        // Ontology platform R1 task 044 (D-33/D-38): the revision a decision records is owned by the secure team under a Secure
        // matter, so it is a child of its budget and its matter (live: sprk_budgetrevision.sprk_budget, .sprk_matter).
        T("sprk_budgetrevision", "sprk_budgetrevisions", ExternalMarker.None,
            ("sprk_budget", "sprk_budget"), ("sprk_matter", Matter)),
        // unified-access-control-r2 task 194 F1-b: a budget's buckets are its children (solution XML,
        // sprk_BudgetBucket/Entity.xml: lookup sprk_budget; entity set sprk_budgetbuckets). Without this row a Secure
        // budget was isolated and its buckets were not. No core stamp (CoreAncestorResolver): a bucket files nothing.
        T("sprk_budgetbucket", "sprk_budgetbuckets", ExternalMarker.None,
            ("sprk_budget", "sprk_budget")),
        T("sprk_communication", "sprk_communications", ExternalMarker.Predicate(ExternalOwnRule.Communication065),
            new[] { ("sprk_communicationthread", "sprk_communicationthread") }
                .Concat(Regarding("sprk_analysis", "sprk_budget", "sprk_event", "sprk_invoice", Matter, Project, "sprk_reportcard", WorkAssignment))
                .ToArray()) with
        {
            // UAC task 194b R4: the thread's filing ranks AFTER the message's own lookups — the parents a record thread is
            // filed under are asked under the column that files the message there (RecordThreadFiling.FilingLookupsOf), after
            // what the message names itself (the reconciler's question, the re-file's and Repair's). The thread lookup itself
            // targets no ownership parent, so its rank decides nothing else.
            LookupOrder = Regarding("sprk_analysis", "sprk_budget", "sprk_event", "sprk_invoice", Matter, Project, "sprk_reportcard", WorkAssignment)
                .Select(l => l.Item1)
                .Append("sprk_communicationthread")
                .ToArray(),
        },
        T("sprk_communicationattachment", "sprk_communicationattachments", ExternalMarker.None,
            ("sprk_communication", "sprk_communication"), ("sprk_document", "sprk_document")),
        T("sprk_communicationparticipant", "sprk_communicationparticipants", ExternalMarker.None,
            ("sprk_communication", "sprk_communication")),
        T("sprk_communicationthread", "sprk_communicationthreads", ExternalMarker.Predicate(ExternalOwnRule.ThreadOpen),
            Regarding("sprk_analysis", "sprk_budget", "sprk_event", "sprk_invoice", Matter, Project, WorkAssignment)),
        // spaarke-ontology-platform-r1 task 039 (owner D-33/D-36; reviewed by unified-access-control-r2 on #1355). Typed core
        // lookups read live, spaarkedev1 2026-10-07: sprk_corerecordtype (a catalog row) and sprk_corerecordid (a string) are
        // not lookups to a parent, so they are not entries here: the walks read that pair beside these lookups
        // (Table.LineageColumns, CorePairParent; see the remarks). sprk_action and sprk_policyversion are not filing.
        // unified-access-control-r2 task 182 (child-record contract C4): the subject's regarding lookups, the same eight as
        // the Signal row, live on spaarkedev1 2026-10-12 (ontology task 140, #1763). sprk_regardingservicerequest is left
        // out, as on the Signal row (see the remarks). The typed core lookups STAY: they are the decision context (D-34, the
        // core the decision was made on) and a lineage parent, not a stamp awaiting retirement. Ontology task 144 K1 and 145
        // keep them; the stamp follows the subject, the typed core does not, so stamp == core is no reason to retire one.
        // ebilling-r1 task 026 (FR-15, D-94): + the invoice-line subject, sprk_regardinginvoiceline -> sprk_billingevent (live
        // spaarkedev1 2026-10-16). An explicit tuple: Regarding("sprk_billingevent") would build sprk_regardingbillingevent.
        T("sprk_decisionrecord", "sprk_decisionrecords", ExternalMarker.None,
            new[]
                {
                    ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_workassignment", WorkAssignment),
                    ("sprk_regardinginvoiceline", "sprk_billingevent"),
                }
                .Concat(Regarding("sprk_communication", "sprk_document", "sprk_event", "sprk_invoice", Matter, Project,
                    "sprk_todo", WorkAssignment))
                .ToArray()),
        // A document's record links come from the ONE declaration of that vocabulary (DocumentLinkFields — a second copy
        // drifts silently; DocumentLinkVocabularyGuardTests), minus the links that are relationships rather than filing
        // (contact, organization, vendor organization, email) and the service request (see the remarks); plus the three
        // document→document / →version lookups that are not record links.
        T("sprk_document", "sprk_documents", ExternalMarker.Predicate(ExternalOwnRule.DocumentNarrowing),
            DocumentLinkFields.All
                .Where(f => !DocumentLinksThatAreNotFiling.Contains(f.TargetEntityLogicalName))
                .Select(f => (f.LogicalName, f.TargetEntityLogicalName))
                .Concat(new[]
                {
                    ("sprk_canonicaldocument", "sprk_document"), ("sprk_currentversionid", "sprk_fileversion"),
                    ("sprk_parentdocument", "sprk_document"),
                })
                .OrderBy(l => DocumentPrimaryRank(l.Item1)) // 194b: stable, so each group keeps its declaration order
                .ToArray()),
        T("sprk_emailartifact", "sprk_emailartifacts", ExternalMarker.Never,
            ("sprk_document", "sprk_document")),
        T("sprk_emailreviewlog", "sprk_emailreviewlogs", ExternalMarker.Never,
            ("sprk_communication", "sprk_communication")),
        T("sprk_event", "sprk_events", ExternalMarker.None,
            Regarding("sprk_agreement", "sprk_analysis", "sprk_budget", "sprk_communication", "sprk_event", "sprk_invoice",
                Matter, Project, "sprk_reportcard", WorkAssignment)),
        T("sprk_eventlog", "sprk_eventlogs", ExternalMarker.None,
            ("sprk_event", "sprk_event")),
        T("sprk_fileversion", "sprk_fileversions", ExternalMarker.None,
            ("sprk_document", "sprk_document")),
        // ebilling-r1 task 013 (FR-05): + sprk_regardingworkassignment (task 012, live spaarkedev1 2026-10-15). Two other task
        // 012 lookups are left out pending unified-access-control-r2: sprk_supersedesinvoice (the invoice a resubmission
        // replaces — not a filing; as a lineage parent it would also join ParentLineage and the external invoices module's
        // live-parent rule) and sprk_billingexport (an export batch is not a codified table). Task 013 notes, section 10.
        T("sprk_invoice", "sprk_invoices", ExternalMarker.Predicate(ExternalOwnRule.InvoiceSource),
            new[] { ("sprk_matter", Matter), ("sprk_project", Project) }
                .Concat(Regarding("sprk_agreement", WorkAssignment)).ToArray()),
        // ebilling-r1 task 013 (FR-05, D-82): the invoice family's new tables, lookups read live, spaarkedev1 2026-10-15
        // (EntityDefinitions(<table>)/ManyToOneRelationships). Each names its core record through the CoreAncestorLookups
        // trio; the vendor organization, the submitter / decider contact and the issuing organization are identities, not
        // parents.
        // A submission's core record comes first, then its submitted PDF, so a row read with these columns names its core
        // record as its ownership primary; the invoice and the report it produced, and the submission it resubmits, follow
        // (UAC-r2 ruling on RecordFamilyEdges, 2026-10-28: outputs and a peer, not its filing).
        T("sprk_invoicesubmission", "sprk_invoicesubmissions", ExternalMarker.None,
            Regarding(Matter, Project, WorkAssignment).Concat(new[]
            {
                ("sprk_document", "sprk_document"), ("sprk_invoice", "sprk_invoice"), ("sprk_reportdocument", "sprk_document"),
                ("sprk_resubmissionof", "sprk_invoicesubmission"),
            }).ToArray()),
        // Its sprk_decisionrecord lookup is left out: a Decision Record is stamped outside the access taxonomy and nothing is
        // filed under one but a Signal (CoreAncestorResolver.StampedOutsideAccessTaxonomy, uac-r2's condition 3).
        T("sprk_invoiceapproval", "sprk_invoiceapprovals", ExternalMarker.None,
            new[] { ("sprk_invoice", "sprk_invoice") }
                .Concat(Regarding(Matter, Project, WorkAssignment)).ToArray()),
        T("sprk_externalidentifier", "sprk_externalidentifiers", ExternalMarker.None,
            Regarding("sprk_invoice", Matter, Project, WorkAssignment)),
        T("sprk_invoicecorrection", "sprk_invoicecorrections", ExternalMarker.None,
            new[] { ("sprk_invoice", "sprk_invoice"), ("sprk_invoiceline", "sprk_billingevent") }
                .Concat(Regarding(Matter, Project, WorkAssignment)).ToArray()),
        T("sprk_kpiassessment", "sprk_kpiassessments", ExternalMarker.None,
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_reportcard", "sprk_reportcard")),
        T("sprk_memo", "sprk_memos", ExternalMarker.None,
            Regarding("sprk_agreement", "sprk_analysis", "sprk_budget", "sprk_communication", "sprk_document", "sprk_event",
                    "sprk_invoice", Matter, Project, WorkAssignment)
                .Concat(new[] { ("sprk_reportcard", "sprk_reportcard") }).ToArray()),
        // unified-access-control-r2 #2084 (owner round 102 item 4, agreed with spaarke-ontology-platform-r1): a rule scoped to a
        // matter is that matter's child, and its versions are the rule's (solution XML: sprk_policy_matter, sprk_policyversion_policy).
        // A rule with no matter has no lookup value, so it is never reached. sprk_policy.sprk_currentversion is left out: it names
        // the rule's own version, whose only parent is this rule, so it can add no root (only a loop).
        // ebilling-r1 task 089a (procedure model 5.2, agreed with unified-access-control-r2; ontology F3-e): a rule of a
        // record-scoped rule set is that set's child (live spaarkedev1 2026-11-03: sprk_policy.sprk_ruleset). A Procedure's or
        // a legacy set has no record lookup, so it is never reached and its rules stay outside the family.
        T("sprk_policy", "sprk_policies", ExternalMarker.None,
            ("sprk_matter", Matter), ("sprk_ruleset", "sprk_ruleset")),
        T("sprk_policyversion", "sprk_policyversions", ExternalMarker.None,
            ("sprk_policy", "sprk_policy")),
        // ebilling-r1 task 089a (procedure model 5.1, 5.2): a Procedure assignment or override is a secure child of the one
        // record it names (live spaarkedev1 2026-11-03: sprk_matter, sprk_project, sprk_workassignment, one set per row). Its
        // other lookups cite and file nothing (RecordOwnershipResolver.CitationLookups): the Procedure, the pinned Procedure
        // Version and the excluded rule.
        T("sprk_procedureassignment", "sprk_procedureassignments", ExternalMarker.None,
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_workassignment", WorkAssignment)),
        // ebilling-r1 task 089a (procedure model 5.2): a record-scoped rule set (sprk_scope Record or Firm record) is a secure
        // child of its record; one record lookup per core type (live spaarkedev1 2026-11-03), RemoveLink. A Procedure's or a
        // legacy set has none of them set, so it is never reached. sprk_procedure, sprk_firm and sprk_sourcedocument are not
        // filing (a Procedure set carries the Procedure; a legacy uploaded set carries its source document, a CitationLookups entry).
        T("sprk_ruleset", "sprk_rulesets", ExternalMarker.None,
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_workassignment", WorkAssignment)),
        T("sprk_reportcard", "sprk_reportcards", ExternalMarker.None,
            Regarding(Matter, Project)),
        // spaarke-ontology-platform-r1 task 039 (owner D-33/D-36; reviewed by unified-access-control-r2 on #1355). Lookups
        // read live, spaarkedev1 2026-10-07: the typed core lookup sprk_matter, the subject's regarding lookups and the
        // Decision Record. sprk_regardingservicerequest is left out (see the remarks); sprk_policy, sprk_policyversion and
        // sprk_corerecordtype (a catalog row) are not lookup entries. The core record pair (sprk_corerecordtype +
        // sprk_corerecordid) IS a parent, read beside these lookups: a Signal has no typed project or work-assignment column, so
        // without it a project or work-assignment core was reached only through the subject's stamp (see the remarks). #2084:
        // the rule and its version are family tables now, but a
        // Signal CITES the rule that raised it and is not filed under it (RecordOwnershipResolver.CitationLookups).
        // ebilling-r1 task 026 (FR-15, D-94): + sprk_regardinginvoiceline -> sprk_billingevent, as on the Decision Record row.
        T("sprk_signal", "sprk_signals", ExternalMarker.None,
            new[]
                {
                    ("sprk_matter", Matter), ("sprk_decisionrecord", "sprk_decisionrecord"),
                    ("sprk_regardinginvoiceline", "sprk_billingevent"),
                }
                .Concat(Regarding("sprk_communication", "sprk_document", "sprk_event", "sprk_invoice", Matter, Project,
                    "sprk_todo", WorkAssignment))
                .ToArray()),
        T("sprk_spendsignal", "sprk_spendsignals", ExternalMarker.None,
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_snapshot", "sprk_spendsnapshot")),
        T("sprk_spendsnapshot", "sprk_spendsnapshots", ExternalMarker.None,
            ("sprk_matter", Matter), ("sprk_project", Project)),
        // ebilling-r1 task 086 (procedure model 5.2, EB-D-148; lookups and entity set read live, spaarkedev1 2026-10-27): an INVOICE Stored
        // File is its invoice's child and a leaf (no table names a Stored File as a parent; it is not an ownership parent). ONE lineage
        // column, sprk_invoice, declared first (uac-r2 194b: declaration order is the order the primary parent is chosen in). Left out on
        // purpose: sprk_procedureversion and sprk_billingexport (a Procedure-source or export file is not a secure child, model 5.2 / F4-c,
        // so a row with no sprk_invoice is never reached), and sprk_sourcedocument / sprk_sourcecommunication (no root's walk reaches a
        // Stored File through its source; they stay in the row's own question as secure-if-any parents after the invoice, uac-r2 194b re-check K).
        T("sprk_storedfile", "sprk_storedfiles", ExternalMarker.None,
            ("sprk_invoice", "sprk_invoice")),
        T("sprk_todo", "sprk_todos", ExternalMarker.None,
            Regarding("sprk_agreement", "sprk_analysis", "sprk_budget", "sprk_communication", "sprk_document", "sprk_event",
                "sprk_invoice", Matter, Project, "sprk_reportcard", WorkAssignment)),
    }.ToDictionary(t => t.LogicalName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// UAC task 194b R4: the primary order of a ROOT table whose rows are filed under a parent (a root is not a
    /// <see cref="Children"/> entry, so it has no <see cref="Table.LookupOrder"/> there). A project's primary is the record its
    /// ADR-024 pair names (<c>sprk_regardingrecordid</c>, <c>SecureRootInheritance.PairNamesAParentOnItsOwn</c>; the
    /// field-mapping 070 registry's project→pair edge), asked under that column. A work assignment's primary is its matter,
    /// then its project, invoice, event: its filing order (<c>SecureRootInheritance.TypedLookupsOf</c>, owner decision
    /// 2026-10-29). Without an entry the order fell to column name, which put the event first.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RootLookupOrder =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Project] = ["sprk_regardingrecordid"],
            [WorkAssignment] = SecureRootInheritance.TypedLookupsOf(WorkAssignment).Select(t => t.Column).ToArray(),
        };

    /// <summary>True for a table whose rows the synchronizer mirrors (a child, never a root).</summary>
    internal static bool IsChild(string? logicalName) =>
        logicalName is not null && Children.ContainsKey(logicalName);

    /// <summary>True for one of the three secure-root tables.</summary>
    internal static bool IsRoot(string? logicalName) => logicalName is not null && Roots.Contains(logicalName);

    // ── unified-access-control-r2 task 191b: the external markers (#2179 design v2 §3.3) ─────────────────────────────

    /// <summary>
    /// Task 191b: tables OUTSIDE the lineage (they name no parent a walk can follow, so they are not <see cref="Children"/>)
    /// that still declare an external marker, so the registry stays the ONE map of what an external caller may never read.
    /// <c>sprk_affinity</c> holds sender addresses, subject keywords and participant sets learned from filed email
    /// (<c>AffinityStore</c>): communication content with no record to inherit from.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, ExternalMarker> OutsideLineage =
        new Dictionary<string, ExternalMarker>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_affinity"] = ExternalMarker.Never,
        };

    /// <summary>Task 191b: the marker a table declares (<see cref="Children"/>, then <see cref="OutsideLineage"/>);
    /// <c>null</c> for a root and for a table the registry does not know.</summary>
    internal static ExternalMarker? ExternalMarkerOf(string? logicalName) =>
        logicalName is null ? null
        : Children.TryGetValue(logicalName, out var table) ? table.External
        : OutsideLineage.TryGetValue(logicalName, out var marker) ? marker
        : null;

    /// <summary>
    /// Task 191b: every table whose marker is <see cref="ExternalMarkerKind.Never"/>: the communication-content tables an
    /// external caller never reads, whatever they are filed under. Derived from the markers, never listed again
    /// (<c>ExternalCallerCommunicationGuard.RefusedContentTables</c> is this set).
    /// </summary>
    internal static readonly IReadOnlySet<string> NeverExternalTables = new HashSet<string>(
        Children.Values.Where(t => t.External.Kind == ExternalMarkerKind.Never).Select(t => t.LogicalName)
            .Concat(OutsideLineage.Where(kv => kv.Value.Kind == ExternalMarkerKind.Never).Select(kv => kv.Key)),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Task 191b: the tables whose rows carry text derived from a document (<see cref="ExternalMarker.CarriesDocumentText"/>),
    /// keyed by logical name. Derived from the markers (<c>ExternalCallerCommunicationGuard</c>'s document-derived tables are
    /// built from it).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, Table> DocumentTextTables =
        Children.Values.Where(t => t.External.CarriesDocumentText)
            .ToDictionary(t => t.LogicalName, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Task 191b: the three kinds of external marker a registry table declares (#2179 design v2 §3.3).</summary>
internal enum ExternalMarkerKind
{
    /// <summary>No rule of its own: only what the row inherits (its roots' Restricted, and the taint of its lineage parents).</summary>
    None = 0,

    /// <summary>An own-row rule (<see cref="ExternalOwnRule"/>), on top of what the row inherits.</summary>
    Predicate = 1,

    /// <summary>Never externally visible, whatever it is filed under.</summary>
    Never = 2,
}

/// <summary>Task 191b: the own-row rule of a <see cref="ExternalMarkerKind.Predicate"/> marker. Each names an EXISTING rule;
/// the predicate (<c>ExternalVisibility</c>) calls it and never re-implements it.</summary>
internal enum ExternalOwnRule
{
    /// <summary>No own rule (a <see cref="ExternalMarkerKind.None"/> or <see cref="ExternalMarkerKind.Never"/> marker).</summary>
    None = 0,

    /// <summary>The task 065 rule: flag explicitly false, a thread set, that thread explicitly Open now
    /// (<c>CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync</c>).</summary>
    Communication065,

    /// <summary>The thread reads explicitly Open now (<c>CommunicationOpenThreadGate.ReadOpenThreadIdsAsync</c>).</summary>
    ThreadOpen,

    /// <summary>Task 188's document narrowing (<c>ExternalDocumentNarrowing</c>, through the app-only seam), and a legacy
    /// Procedure Library document (its drive is the Procedure Library container) is never visible.</summary>
    DocumentNarrowing,

    /// <summary>ebilling (a′): the invoice predicate (<c>ExternalInvoiceVisibility.DecideAsync</c>).</summary>
    InvoiceSource,
}

/// <summary>
/// Task 191b: what an external caller's visibility asks of a registry table's OWN row (#2179 design v2 §3.3). Required on
/// every <see cref="SecureChildLineage.Table"/>, so a new table cannot be registered without that decision.
/// </summary>
/// <remarks>Instances are the four statics and <see cref="Predicate"/>; nothing else can make one.</remarks>
internal sealed record ExternalMarker
{
    private ExternalMarker(ExternalMarkerKind kind, ExternalOwnRule rule, bool carriesDocumentText)
    {
        Kind = kind;
        Rule = rule;
        CarriesDocumentText = carriesDocumentText;
    }

    /// <summary>The marker's kind.</summary>
    public ExternalMarkerKind Kind { get; }

    /// <summary>The own-row rule (only on a <see cref="ExternalMarkerKind.Predicate"/> marker).</summary>
    public ExternalOwnRule Rule { get; }

    /// <summary>
    /// A <see cref="ExternalMarkerKind.None"/> row that ALSO carries text derived from its documents (an analysis, an
    /// analysis output): the assistant's tools hide it by its documents (task 188 slice C). The predicate needs nothing extra
    /// for it: its document lookups are lineage edges, so the taint closure already hides it with them.
    /// </summary>
    public bool CarriesDocumentText { get; }

    /// <summary>Only inherited taint applies.</summary>
    public static ExternalMarker None { get; } = new(ExternalMarkerKind.None, ExternalOwnRule.None, carriesDocumentText: false);

    /// <summary>Only inherited taint applies, and the row carries a document's text (<see cref="CarriesDocumentText"/>).</summary>
    public static ExternalMarker DocumentText { get; } = new(ExternalMarkerKind.None, ExternalOwnRule.None, carriesDocumentText: true);

    /// <summary>Never externally visible.</summary>
    public static ExternalMarker Never { get; } = new(ExternalMarkerKind.Never, ExternalOwnRule.None, carriesDocumentText: false);

    /// <summary>An own-row rule on top of inherited taint.</summary>
    public static ExternalMarker Predicate(ExternalOwnRule rule) => rule == ExternalOwnRule.None
        ? throw new ArgumentOutOfRangeException(nameof(rule), rule, "A predicate marker names its own-row rule.")
        : new(ExternalMarkerKind.Predicate, rule, carriesDocumentText: false);
}
