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
/// </remarks>
internal static class SecureChildLineage
{
    /// <summary>One table: its entity set (for POA writes) and its lookups to parents (column → target table).</summary>
    internal sealed record Table(string LogicalName, string EntitySet, IReadOnlyDictionary<string, string> Lookups)
    {
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
        new(logicalName, entitySet, lookups.ToDictionary(l => l.Column, l => l.Target, StringComparer.OrdinalIgnoreCase));

    /// <summary>Document link targets that are not a filing parent (identities, the email activity, a service request).</summary>
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
        T("sprk_billingevent", "sprk_billingevents",
            ("sprk_invoice", "sprk_invoice"), ("sprk_matter", Matter), ("sprk_project", Project)),
        T("sprk_budget", "sprk_budgets",
            ("sprk_matter", Matter), ("sprk_project", Project)),
        // Ontology platform R1 task 044 (D-33/D-38): the revision a decision records is owned by the secure team under a Secure
        // matter, so it is a child of its budget and its matter (live: sprk_budgetrevision.sprk_budget, .sprk_matter).
        T("sprk_budgetrevision", "sprk_budgetrevisions",
            ("sprk_budget", "sprk_budget"), ("sprk_matter", Matter)),
        T("sprk_communication", "sprk_communications",
            new[] { ("sprk_communicationthread", "sprk_communicationthread") }
                .Concat(Regarding("sprk_analysis", "sprk_budget", "sprk_event", "sprk_invoice", Matter, Project, "sprk_reportcard", WorkAssignment))
                .ToArray()),
        T("sprk_communicationattachment", "sprk_communicationattachments",
            ("sprk_communication", "sprk_communication"), ("sprk_document", "sprk_document")),
        T("sprk_communicationparticipant", "sprk_communicationparticipants",
            ("sprk_communication", "sprk_communication")),
        T("sprk_communicationthread", "sprk_communicationthreads",
            Regarding("sprk_analysis", "sprk_budget", "sprk_event", "sprk_invoice", Matter, Project, WorkAssignment)),
        // spaarke-ontology-platform-r1 task 039 (owner D-33/D-36; reviewed by unified-access-control-r2 on #1355). Lookups
        // read live, spaarkedev1 2026-10-07. Typed core lookups only: sprk_corerecordtype (a catalog row) and
        // sprk_corerecordid (a string) are not lookups to a parent; sprk_action and sprk_policyversion are not filing.
        T("sprk_decisionrecord", "sprk_decisionrecords",
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_workassignment", WorkAssignment)),
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
        T("sprk_invoice", "sprk_invoices",
            new[] { ("sprk_matter", Matter), ("sprk_project", Project) }
                .Concat(Regarding("sprk_agreement")).ToArray()),
        T("sprk_kpiassessment", "sprk_kpiassessments",
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_reportcard", "sprk_reportcard")),
        T("sprk_memo", "sprk_memos",
            Regarding("sprk_agreement", "sprk_analysis", "sprk_budget", "sprk_communication", "sprk_document", "sprk_event",
                    "sprk_invoice", Matter, Project, WorkAssignment)
                .Concat(new[] { ("sprk_reportcard", "sprk_reportcard") }).ToArray()),
        T("sprk_reportcard", "sprk_reportcards",
            Regarding(Matter, Project)),
        // spaarke-ontology-platform-r1 task 039 (owner D-33/D-36; reviewed by unified-access-control-r2 on #1355). Lookups
        // read live, spaarkedev1 2026-10-07: the typed core lookup sprk_matter, the subject's regarding lookups and the
        // Decision Record. sprk_regardingservicerequest is left out (see the remarks); sprk_policy, sprk_policyversion and
        // sprk_corerecordtype (a catalog row) are not filing parents.
        T("sprk_signal", "sprk_signals",
            new[] { ("sprk_matter", Matter), ("sprk_decisionrecord", "sprk_decisionrecord") }
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

    /// <summary>True for a table whose rows the synchronizer mirrors (a child, never a root).</summary>
    internal static bool IsChild(string? logicalName) =>
        logicalName is not null && Children.ContainsKey(logicalName);

    /// <summary>True for one of the three secure-root tables.</summary>
    internal static bool IsRoot(string? logicalName) => logicalName is not null && Roots.Contains(logicalName);
}
