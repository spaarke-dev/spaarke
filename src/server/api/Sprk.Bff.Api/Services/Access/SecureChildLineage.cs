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
/// lookups, then its <c>sprk_related*</c> links (<c>EmailAttachmentProcessor</c>); an analysis's document
/// (<c>AnalysisEndpoints</c>, <c>AppOnlyAnalysisService</c>); a Decision Record's typed core. A Signal and a Decision Record
/// name their core record and subject first (<c>RecordOwnershipContext.ForExistingRowAsync</c>). Secure-if-any reads every
/// parent, so the order never decides whether a row is secure.</para>
/// </remarks>
internal static class SecureChildLineage
{
    /// <summary>One table: its entity set (for POA writes) and its lookups to parents (column → target table).</summary>
    internal sealed record Table(string LogicalName, string EntitySet, IReadOnlyDictionary<string, string> Lookups)
    {
        /// <summary>UAC task 194b: <see cref="Lookups"/>' columns in declaration order, the ownership primary order (see the
        /// class remarks).</summary>
        public IReadOnlyList<string> LookupOrder { get; init; } = Lookups.Keys.ToArray();

        /// <summary>The table's primary id column (every Spaarke table: logical name + <c>id</c>, verified live).</summary>
        public string IdColumn => LogicalName + "id";
    }

    private const string Project = "sprk_project";
    private const string Matter = "sprk_matter";
    private const string WorkAssignment = "sprk_workassignment";

    /// <summary>The three secure roots, from the ONE root table (never re-listed).</summary>
    internal static readonly IReadOnlySet<string> Roots = new HashSet<string>(
        Enum.GetValues<ExternalGrantRootType>().Select(ExternalGrantRoot.LogicalNameFor), StringComparer.OrdinalIgnoreCase);

    private static Table T(string logicalName, string entitySet, params (string Column, string Target)[] lookups) =>
        new(logicalName, entitySet, lookups.ToDictionary(l => l.Column, l => l.Target, StringComparer.OrdinalIgnoreCase))
        {
            LookupOrder = lookups.Select(l => l.Column).ToArray(),
        };

    /// <summary>UAC task 194b: a document's primary order — its parent document, then its typed core lookups, then the rest in
    /// declaration order (the <c>sprk_related*</c> links), as <c>EmailAttachmentProcessor</c> names them.</summary>
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
        T("sprk_agreement", "sprk_agreements",
            Regarding("sprk_document", Matter, Project)),
        T("sprk_analysis", "sprk_analysises",
            new[] { ("sprk_documentid", "sprk_document"), ("sprk_outputfileid", "sprk_document") }
                .Concat(Regarding("sprk_budget", "sprk_communication", "sprk_document", "sprk_invoice", Matter, Project, WorkAssignment))
                .ToArray()),
        T("sprk_analysisoutput", "sprk_analysisoutputs",
            ("sprk_analysisid", "sprk_analysis")),
        T("sprk_attachmentartifact", "sprk_attachmentartifacts",
            ("sprk_document", "sprk_document"), ("sprk_emailartifact", "sprk_emailartifact")),
        // ebilling-r1 task 013 (FR-05): + sprk_regardingworkassignment (ebilling-r1 task 010, live spaarkedev1 2026-10-15).
        T("sprk_billingevent", "sprk_billingevents",
            ("sprk_invoice", "sprk_invoice"), ("sprk_matter", Matter), ("sprk_project", Project),
            ("sprk_regardingworkassignment", WorkAssignment)),
        T("sprk_budget", "sprk_budgets",
            ("sprk_matter", Matter), ("sprk_project", Project)),
        // Ontology platform R1 task 044 (D-33/D-38): the revision a decision records is owned by the secure team under a Secure
        // matter, so it is a child of its budget and its matter (live: sprk_budgetrevision.sprk_budget, .sprk_matter).
        T("sprk_budgetrevision", "sprk_budgetrevisions",
            ("sprk_budget", "sprk_budget"), ("sprk_matter", Matter)),
        // unified-access-control-r2 task 194 F1-b: a budget's buckets are its children (solution XML,
        // sprk_BudgetBucket/Entity.xml: lookup sprk_budget; entity set sprk_budgetbuckets). Without this row a Secure
        // budget was isolated and its buckets were not. No core stamp (CoreAncestorResolver): a bucket files nothing.
        T("sprk_budgetbucket", "sprk_budgetbuckets",
            ("sprk_budget", "sprk_budget")),
        T("sprk_communication", "sprk_communications",
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
        T("sprk_communicationattachment", "sprk_communicationattachments",
            ("sprk_communication", "sprk_communication"), ("sprk_document", "sprk_document")),
        T("sprk_communicationparticipant", "sprk_communicationparticipants",
            ("sprk_communication", "sprk_communication")),
        T("sprk_communicationthread", "sprk_communicationthreads",
            Regarding("sprk_analysis", "sprk_budget", "sprk_event", "sprk_invoice", Matter, Project, WorkAssignment)),
        // spaarke-ontology-platform-r1 task 039 (owner D-33/D-36; reviewed by unified-access-control-r2 on #1355). Typed core
        // lookups read live, spaarkedev1 2026-10-07: sprk_corerecordtype (a catalog row) and sprk_corerecordid (a string) are
        // not lookups to a parent; sprk_action and sprk_policyversion are not filing.
        // unified-access-control-r2 task 182 (child-record contract C4): the subject's regarding lookups, the same eight as
        // the Signal row, live on spaarkedev1 2026-10-12 (ontology task 140, #1763). sprk_regardingservicerequest is left
        // out, as on the Signal row (see the remarks). The typed core lookups STAY: they are the decision context (D-34, the
        // core the decision was made on) and a lineage parent, not a stamp awaiting retirement. Ontology task 144 K1 and 145
        // keep them; the stamp follows the subject, the typed core does not, so stamp == core is no reason to retire one.
        // ebilling-r1 task 026 (FR-15, D-94): + the invoice-line subject, sprk_regardinginvoiceline -> sprk_billingevent (live
        // spaarkedev1 2026-10-16). An explicit tuple: Regarding("sprk_billingevent") would build sprk_regardingbillingevent.
        T("sprk_decisionrecord", "sprk_decisionrecords",
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
        T("sprk_document", "sprk_documents",
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
        T("sprk_emailartifact", "sprk_emailartifacts",
            ("sprk_document", "sprk_document")),
        T("sprk_emailreviewlog", "sprk_emailreviewlogs",
            ("sprk_communication", "sprk_communication")),
        T("sprk_event", "sprk_events",
            Regarding("sprk_agreement", "sprk_analysis", "sprk_budget", "sprk_communication", "sprk_event", "sprk_invoice",
                Matter, Project, "sprk_reportcard", WorkAssignment)),
        T("sprk_eventlog", "sprk_eventlogs",
            ("sprk_event", "sprk_event")),
        T("sprk_fileversion", "sprk_fileversions",
            ("sprk_document", "sprk_document")),
        // ebilling-r1 task 013 (FR-05): + sprk_regardingworkassignment (task 012, live spaarkedev1 2026-10-15). Two other task
        // 012 lookups are left out pending unified-access-control-r2: sprk_supersedesinvoice (the invoice a resubmission
        // replaces — not a filing; as a lineage parent it would also join ParentLineage and the external invoices module's
        // live-parent rule) and sprk_billingexport (an export batch is not a codified table). Task 013 notes, section 10.
        T("sprk_invoice", "sprk_invoices",
            new[] { ("sprk_matter", Matter), ("sprk_project", Project) }
                .Concat(Regarding("sprk_agreement", WorkAssignment)).ToArray()),
        // ebilling-r1 task 013 (FR-05, D-82): the invoice family's new tables, lookups read live, spaarkedev1 2026-10-15
        // (EntityDefinitions(<table>)/ManyToOneRelationships). Each names its core record through the CoreAncestorLookups
        // trio; the vendor organization, the submitter / decider contact and the issuing organization are identities, not
        // parents.
        // A submission's core record comes first, then its submitted PDF, so a row read with these columns names its core
        // record as its ownership primary; the invoice and the report it produced, and the submission it resubmits, follow
        // (UAC-r2 ruling on RecordFamilyEdges, 2026-10-28: outputs and a peer, not its filing).
        T("sprk_invoicesubmission", "sprk_invoicesubmissions",
            Regarding(Matter, Project, WorkAssignment).Concat(new[]
            {
                ("sprk_document", "sprk_document"), ("sprk_invoice", "sprk_invoice"), ("sprk_reportdocument", "sprk_document"),
                ("sprk_resubmissionof", "sprk_invoicesubmission"),
            }).ToArray()),
        // Its sprk_decisionrecord lookup is left out: a Decision Record is stamped outside the access taxonomy and nothing is
        // filed under one but a Signal (CoreAncestorResolver.StampedOutsideAccessTaxonomy, uac-r2's condition 3).
        T("sprk_invoiceapproval", "sprk_invoiceapprovals",
            new[] { ("sprk_invoice", "sprk_invoice") }
                .Concat(Regarding(Matter, Project, WorkAssignment)).ToArray()),
        T("sprk_externalidentifier", "sprk_externalidentifiers",
            Regarding("sprk_invoice", Matter, Project, WorkAssignment)),
        T("sprk_invoicecorrection", "sprk_invoicecorrections",
            new[] { ("sprk_invoice", "sprk_invoice"), ("sprk_invoiceline", "sprk_billingevent") }
                .Concat(Regarding(Matter, Project, WorkAssignment)).ToArray()),
        T("sprk_kpiassessment", "sprk_kpiassessments",
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_reportcard", "sprk_reportcard")),
        T("sprk_memo", "sprk_memos",
            Regarding("sprk_agreement", "sprk_analysis", "sprk_budget", "sprk_communication", "sprk_document", "sprk_event",
                    "sprk_invoice", Matter, Project, WorkAssignment)
                .Concat(new[] { ("sprk_reportcard", "sprk_reportcard") }).ToArray()),
        // unified-access-control-r2 #2084 (owner round 102 item 4, agreed with spaarke-ontology-platform-r1): a rule scoped to a
        // matter is that matter's child, and its versions are the rule's (solution XML: sprk_policy_matter, sprk_policyversion_policy).
        // A rule with no matter has no lookup value, so it is never reached. sprk_policy.sprk_currentversion is left out: it names
        // the rule's own version, whose only parent is this rule, so it can add no root (only a loop).
        T("sprk_policy", "sprk_policies",
            ("sprk_matter", Matter)),
        T("sprk_policyversion", "sprk_policyversions",
            ("sprk_policy", "sprk_policy")),
        T("sprk_reportcard", "sprk_reportcards",
            Regarding(Matter, Project)),
        // spaarke-ontology-platform-r1 task 039 (owner D-33/D-36; reviewed by unified-access-control-r2 on #1355). Lookups
        // read live, spaarkedev1 2026-10-07: the typed core lookup sprk_matter, the subject's regarding lookups and the
        // Decision Record. sprk_regardingservicerequest is left out (see the remarks); sprk_policy, sprk_policyversion and
        // sprk_corerecordtype (a catalog row) are not filing parents. #2084: the rule and its version are family tables now, but a
        // Signal CITES the rule that raised it and is not filed under it (RecordOwnershipResolver.CitationLookups).
        // ebilling-r1 task 026 (FR-15, D-94): + sprk_regardinginvoiceline -> sprk_billingevent, as on the Decision Record row.
        T("sprk_signal", "sprk_signals",
            new[]
                {
                    ("sprk_matter", Matter), ("sprk_decisionrecord", "sprk_decisionrecord"),
                    ("sprk_regardinginvoiceline", "sprk_billingevent"),
                }
                .Concat(Regarding("sprk_communication", "sprk_document", "sprk_event", "sprk_invoice", Matter, Project,
                    "sprk_todo", WorkAssignment))
                .ToArray()),
        T("sprk_spendsignal", "sprk_spendsignals",
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_snapshot", "sprk_spendsnapshot")),
        T("sprk_spendsnapshot", "sprk_spendsnapshots",
            ("sprk_matter", Matter), ("sprk_project", Project)),
        T("sprk_todo", "sprk_todos",
            Regarding("sprk_agreement", "sprk_analysis", "sprk_budget", "sprk_communication", "sprk_document", "sprk_event",
                "sprk_invoice", Matter, Project, "sprk_reportcard", WorkAssignment)),
    }.ToDictionary(t => t.LogicalName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// UAC task 194b R4: the primary order of a ROOT table whose rows are filed under a parent (a root is not a
    /// <see cref="Children"/> entry, so it has no <see cref="Table.LookupOrder"/> there). A project's primary is the record its
    /// ADR-024 pair names (<c>sprk_regardingrecordid</c>, <c>SecureRootInheritance.PairNamesAParentOnItsOwn</c>; the
    /// field-mapping 070 registry's project→pair edge), asked under that column.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RootLookupOrder =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Project] = ["sprk_regardingrecordid"],
        };

    /// <summary>True for a table whose rows the synchronizer mirrors (a child, never a root).</summary>
    internal static bool IsChild(string? logicalName) =>
        logicalName is not null && Children.ContainsKey(logicalName);

    /// <summary>True for one of the three secure-root tables.</summary>
    internal static bool IsRoot(string? logicalName) => logicalName is not null && Roots.Contains(logicalName);
}
