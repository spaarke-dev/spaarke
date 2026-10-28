using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 070 (FR-29; design §12.3 A; D-191) — the ONE list of the edges that join a
/// record to its family: each row is a typed lookup (or the regarding pair) that files a CHILD table's row under a PARENT
/// table's row. Static data; edge TYPES live here, edge INSTANCES live in Dataverse. No family table exists (D-191).
/// </summary>
/// <remarks>
/// <para><b>Content owner: unified-access-control-r2.</b> UAC's isolation walks are checked against these rows, so the
/// access columns (<see cref="Edge.FamilyKind"/>, <see cref="Edge.ChildTable"/>, <see cref="Edge.LookupColumn"/>,
/// <see cref="Edge.ParentTable"/>, <see cref="Edge.EdgeKind"/>, <see cref="Edge.InSecureFamily"/>) are pinned by
/// <c>RecordFamilyEdgesGuardTests</c>: changing one fails the build until the pin is updated, and the PR carries a
/// "UAC-r2 review:" line. <see cref="Edge.RecordNumberColumn"/> is display data and is not pinned (UAC C3).</para>
/// <para><b>Seeded, not written from scratch</b>, from the registries at master <c>de38296499</c> (UAC task 194):
/// <c>SecureChildLineage.Children</c> (every child row, <see cref="EdgeKind.Stamp"/> / <see cref="EdgeKind.LineageHop"/>);
/// <c>SecureRootInheritance.TypedFilingColumns</c> and <c>IntermediateFilingColumns</c> (<see cref="EdgeKind.FilingCore"/>);
/// <c>SecureRootInheritance.BffManagedPairTables</c> × its parents (<see cref="EdgeKind.Pair"/>: the Matter → Project edge,
/// owner decision (a), #2352); and <c>ExternalDataService.ServiceRequestRelations</c> (a service request's OWN family,
/// UAC-r2 round 2; outside the Secure family, so <see cref="Edge.InSecureFamily"/> is false). The existing registries are
/// untouched and each is a CHECKED VIEW of this one: <c>RecordFamilyEdgesGuardTests</c> compares them with a ratchet
/// baseline, so a new disagreement fails the build and the known ones are listed with their reason. Migrating UAC's walks
/// onto this list is UAC's call.</para>
/// <para><b>Left out, each with its reason.</b> Links an existing registry itself declares are not filing:
/// <c>sprk_document.sprk_currentversionid</c>, <c>.sprk_canonicaldocument</c> and <c>sprk_analysis.sprk_outputfileid</c>
/// (SecureChildLineage keeps them only because an extra lookup can only narrow a mirror);
/// <c>sprk_analysis.sprk_documentid</c> (the document it analyses, its input — CoreAncestorResolver.StampSourceColumns);
/// <c>sprk_communication.sprk_communicationthread</c> (a grouping container; a message is filed under its regarding —
/// RecordOwnershipResolver.OwnershipParentEntities). Never family (UAC-r2 canonical definition, design §12.2): N:N
/// associations (incl. <c>sprk_Project_Matter_nn</c>), citations (<c>sprk_signal.sprk_policy</c>), people and organization
/// lookups, grant rows, <c>sprk_procedureassignment</c> (owner, 2026-10-27), <c>sprk_policy.sprk_currentversion</c> (a
/// loop), and <c>sprk_invoicelineitem</c> until ebilling deploys it (#2348).</para>
/// <para><b>One kind (D-191).</b> Every row is <see cref="FamilyKind.Filing"/>: a tree with one root, the core ancestor.
/// A later peer kind (for example a patent family's priority edges) would add rows of another kind; every view an access
/// walk could use filters <see cref="FamilyKind.Filing"/> (UAC C2), so such a row can never enter an isolation walk or I-6.
/// <see cref="FamilyKind"/> has no zero member, so an uninitialised kind is never Filing.</para>
/// <para><b>Placement and component justification (CLAUDE.md §10, §11).</b> In <c>Services/Dataverse</c> beside
/// <see cref="CoreAncestorResolver"/>; static data, no DI registration, no package. Existing: seven registries in five
/// files (listed above, plus <see cref="CoreAncestorResolver"/>'s stamp topology and
/// <see cref="RecordOwnershipResolver.OwnershipParentEntities"/>). Extension: no — each serves one walk with its own shape;
/// this is their checked union, and the parity test leaves them untouched. Cost of doing nothing: the family reader (task
/// 071) and push (task 040) would each declare an eighth copy of the hierarchy, and drift such as the missing
/// <c>sprk_budgetbucket</c> row UAC task 194 found goes undetected.</para>
/// </remarks>
internal static class RecordFamilyEdges
{
    /// <summary>The kind of family an edge belongs to (D-191). Only <see cref="Filing"/> exists in W7; no zero member.</summary>
    internal enum FamilyKind
    {
        /// <summary>A tree joined by filing edges, rooted at the core ancestor.</summary>
        Filing = 1,
    }

    /// <summary>What kind of filing link an edge is.</summary>
    internal enum EdgeKind
    {
        /// <summary>A typed filing lookup on a CORE table: a work assignment under a matter, project, invoice or event.</summary>
        FilingCore = 1,

        /// <summary>The ADR-024 regarding pair (<see cref="PairIdColumn"/> + <see cref="PairTypeColumn"/>) on a table whose
        /// pair is BFF-managed — a project under a matter or project (#2352).</summary>
        Pair = 2,

        /// <summary>One of the four <c>sprk_regarding{core}</c> columns (<see cref="StampColumns"/>) on a child table.</summary>
        Stamp = 3,

        /// <summary>Any other child-table lookup: to another child, or a typed lookup to a core.</summary>
        LineageHop = 4,
    }

    /// <summary>
    /// One edge: a row of <paramref name="ChildTable"/> is filed under the <paramref name="ParentTable"/> row its
    /// <paramref name="LookupColumn"/> names. For <see cref="EdgeKind.Pair"/> the column is <see cref="PairIdColumn"/> and
    /// the parent is the type <see cref="PairTypeColumn"/> names.
    /// </summary>
    /// <param name="FamilyKind">The family kind (pinned).</param>
    /// <param name="ChildTable">The child table's logical name (pinned).</param>
    /// <param name="LookupColumn">The lookup, or the pair id column (pinned).</param>
    /// <param name="ParentTable">The parent table's logical name (pinned).</param>
    /// <param name="EdgeKind">The kind of link (pinned).</param>
    /// <param name="InSecureFamily">Whether the edge is part of a Secure root's family (pinned): false only for an edge
    /// to a service request, which is a root of its own outside the Secure family.</param>
    /// <param name="RecordNumberColumn">The child table's record number column, or null when it has none (display only,
    /// not pinned).</param>
    internal sealed record Edge(
        FamilyKind FamilyKind,
        string ChildTable,
        string LookupColumn,
        string ParentTable,
        EdgeKind EdgeKind,
        bool InSecureFamily,
        string? RecordNumberColumn);

    private const string Matter = "sprk_matter";
    private const string Project = "sprk_project";
    private const string WorkAssignment = "sprk_workassignment";
    private const string ServiceRequest = "sprk_servicerequest";

    private const string Agreement = "sprk_agreement";
    private const string Analysis = "sprk_analysis";
    private const string Budget = "sprk_budget";
    private const string Communication = "sprk_communication";
    private const string Document = "sprk_document";
    private const string Event = "sprk_event";
    private const string Invoice = "sprk_invoice";
    private const string ReportCard = "sprk_reportcard";
    private const string ToDo = "sprk_todo";

    /// <summary>The ADR-024 regarding pair's record id column (a string).</summary>
    internal const string PairIdColumn = "sprk_regardingrecordid";

    /// <summary>The ADR-024 regarding pair's type column (a lookup to <c>sprk_recordtype_ref</c>).</summary>
    internal const string PairTypeColumn = "sprk_regardingrecordtype";

    /// <summary>The four core tables (a family's possible roots).</summary>
    internal static readonly IReadOnlySet<string> CoreTables =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Project, Matter, WorkAssignment, ServiceRequest };

    /// <summary>The four <c>sprk_regarding{core}</c> stamp columns and the core each names.</summary>
    internal static readonly IReadOnlyDictionary<string, string> StampColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_regardingproject"] = Project,
            ["sprk_regardingmatter"] = Matter,
            ["sprk_regardingworkassignment"] = WorkAssignment,
            ["sprk_regardingservicerequest"] = ServiceRequest,
        };

    /// <summary>
    /// Each table's record number column (solution source <c>Entities/*/Entity.xml</c>). A table not listed has none
    /// (version numbers are ordinals, not record numbers). Display data only.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> RecordNumberColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Matter] = "sprk_matternumber",
            [Project] = "sprk_projectnumber",
            [WorkAssignment] = "sprk_workassignmentnumber",
            [ServiceRequest] = "sprk_servicerequestnumber",
            [Analysis] = "sprk_analysis_number",
            [Budget] = "sprk_budgetnumber",
            ["sprk_decisionrecord"] = "sprk_decisionnumber",
            [Document] = "sprk_document_number",
            [Event] = "sprk_eventnumber",
            [Invoice] = "sprk_invoicenumber",
            [ReportCard] = "sprk_reportcardnumber",
            ["sprk_signal"] = "sprk_signalnumber",
        };

    /// <summary>The record number column of <paramref name="table"/>, or null when it has none.</summary>
    internal static string? RecordNumberColumnOf(string table) =>
        RecordNumberColumns.TryGetValue(table, out var column) ? column : null;

    /// <summary>Document link targets that are relationships, not filing parents (identities and the email activity).</summary>
    /// <remarks>Declared before <see cref="All"/>: static fields initialise in textual order, and <see cref="Build"/> reads it.</remarks>
    private static readonly HashSet<string> DocumentLinkTargetsThatAreNotFiling = new(StringComparer.OrdinalIgnoreCase)
    {
        "contact", "sprk_organization", "email",
    };

    /// <summary>Every edge, all kinds. Readers take a kind's view (<see cref="Of"/>), never this list directly.</summary>
    internal static readonly IReadOnlyList<Edge> All = Build();

    /// <summary>The edges of one <paramref name="kind"/> (of <paramref name="rows"/>, default <see cref="All"/>).</summary>
    internal static IReadOnlyList<Edge> Of(FamilyKind kind, IEnumerable<Edge>? rows = null) =>
        (rows ?? All).Where(e => e.FamilyKind == kind).ToArray();

    /// <summary>
    /// The <c>SecureChildLineage.Children</c> view: for each CHILD table in the Secure family, its lookups (column →
    /// parent table). Filing edges only (UAC C2).
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> SecureLineageView(IEnumerable<Edge>? rows = null) =>
        Of(FamilyKind.Filing, rows)
            .Where(e => e.InSecureFamily && e.EdgeKind is EdgeKind.Stamp or EdgeKind.LineageHop)
            .GroupBy(e => e.ChildTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, string>)g.ToDictionary(e => e.LookupColumn, e => e.ParentTable, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The <see cref="CoreAncestorResolver"/> children view: for each CHILD table, the columns that file it directly under
    /// a core (column, core table) — the shape of <see cref="CoreAncestorResolver.IntermediateRootColumns"/>. Filing edges
    /// only (UAC C2).
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<(string Column, string RootEntity)>> CoreAncestorChildrenView(IEnumerable<Edge>? rows = null) =>
        Of(FamilyKind.Filing, rows)
            .Where(e => !CoreTables.Contains(e.ChildTable) && CoreTables.Contains(e.ParentTable))
            .GroupBy(e => e.ChildTable, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<(string Column, string RootEntity)>)g.Select(e => (e.LookupColumn, e.ParentTable)).ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<Edge> Build()
    {
        var rows = new List<Edge>();

        // --- core → core: SecureRootInheritance (task 158, #1630, #1616) ---
        // TypedFilingColumns: a work assignment's typed regarding lookups.
        rows.AddRange(Secure(WorkAssignment, ("sprk_regardingmatter", Matter), ("sprk_regardingproject", Project)));
        // IntermediateFilingColumns: a work assignment under an invoice or event (owner Option A, 2026-10-11).
        rows.AddRange(Secure(WorkAssignment, ("sprk_regardinginvoice", Invoice), ("sprk_regardingevent", Event)));
        // BffManagedPairTables × its parents (a matter or project): the project's pair, its only filing. The Matter →
        // Project edge is this pair (owner decision (a), #2352); never the sprk_Project_Matter_nn association.
        rows.AddRange(Secure(Project, (PairIdColumn, Matter), (PairIdColumn, Project)));

        // --- core → child and child → grandchild: SecureChildLineage at de38296499 ---
        rows.AddRange(Secure(Agreement,
            ("sprk_regardingdocument", Document), ("sprk_regardingmatter", Matter), ("sprk_regardingproject", Project)));
        // Left out: sprk_documentid (the document analysed — its input, not its filing) and sprk_outputfileid (its output).
        rows.AddRange(Secure(Analysis,
            ("sprk_regardingbudget", Budget), ("sprk_regardingcommunication", Communication),
            ("sprk_regardingdocument", Document), ("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", Matter),
            ("sprk_regardingproject", Project), ("sprk_regardingworkassignment", WorkAssignment)));
        rows.AddRange(Secure("sprk_analysisoutput", ("sprk_analysisid", Analysis)));
        rows.AddRange(Secure("sprk_attachmentartifact",
            ("sprk_document", Document), ("sprk_emailartifact", "sprk_emailartifact")));
        rows.AddRange(Secure("sprk_billingevent",
            ("sprk_invoice", Invoice), ("sprk_matter", Matter), ("sprk_project", Project)));
        rows.AddRange(Secure(Budget, ("sprk_matter", Matter), ("sprk_project", Project)));
        rows.AddRange(Secure("sprk_budgetbucket", ("sprk_budget", Budget)));
        rows.AddRange(Secure("sprk_budgetrevision", ("sprk_budget", Budget), ("sprk_matter", Matter)));
        // Left out: sprk_communicationthread (a grouping container; the message is filed under its regarding).
        rows.AddRange(Secure(Communication,
            ("sprk_regardinganalysis", Analysis), ("sprk_regardingbudget", Budget), ("sprk_regardingevent", Event),
            ("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", Matter), ("sprk_regardingproject", Project),
            ("sprk_regardingreportcard", ReportCard), ("sprk_regardingworkassignment", WorkAssignment)));
        rows.AddRange(Secure("sprk_communicationattachment",
            ("sprk_communication", Communication), ("sprk_document", Document)));
        rows.AddRange(Secure("sprk_communicationparticipant", ("sprk_communication", Communication)));
        rows.AddRange(Secure("sprk_communicationthread",
            ("sprk_regardinganalysis", Analysis), ("sprk_regardingbudget", Budget), ("sprk_regardingevent", Event),
            ("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", Matter), ("sprk_regardingproject", Project),
            ("sprk_regardingworkassignment", WorkAssignment)));
        // Ontology: the typed core lookups (the decision context) and the subject's regarding lookups.
        rows.AddRange(Secure("sprk_decisionrecord",
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_workassignment", WorkAssignment),
            ("sprk_regardingcommunication", Communication), ("sprk_regardingdocument", Document),
            ("sprk_regardingevent", Event), ("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", Matter),
            ("sprk_regardingproject", Project), ("sprk_regardingtodo", ToDo),
            ("sprk_regardingworkassignment", WorkAssignment)));
        // A document's record links come from the ONE declaration of that vocabulary (DocumentLinkFields — a second copy
        // drifts silently; DocumentLinkVocabularyGuardTests), minus the links to an identity or the email activity. Its
        // link to a service request is that request's own family (ServiceRequestRelations), outside the Secure family.
        // The content pin still pins every row this produces. Plus the parent document; left out: sprk_canonicaldocument
        // (its canonical copy) and sprk_currentversionid (its version).
        rows.AddRange(DocumentLinkFields.All
            .Where(f => !DocumentLinkTargetsThatAreNotFiling.Contains(f.TargetEntityLogicalName))
            .Select(f => Row(Document, f.LogicalName, f.TargetEntityLogicalName,
                inSecureFamily: !string.Equals(f.TargetEntityLogicalName, ServiceRequest, StringComparison.OrdinalIgnoreCase))));
        rows.AddRange(Secure(Document, ("sprk_parentdocument", Document)));
        rows.AddRange(Secure("sprk_emailartifact", ("sprk_document", Document)));
        rows.AddRange(Secure("sprk_emailreviewlog", ("sprk_communication", Communication)));
        rows.AddRange(Secure(Event,
            ("sprk_regardingagreement", Agreement), ("sprk_regardinganalysis", Analysis), ("sprk_regardingbudget", Budget),
            ("sprk_regardingcommunication", Communication), ("sprk_regardingevent", Event),
            ("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", Matter), ("sprk_regardingproject", Project),
            ("sprk_regardingreportcard", ReportCard), ("sprk_regardingworkassignment", WorkAssignment)));
        rows.AddRange(Secure("sprk_eventlog", ("sprk_event", Event)));
        rows.AddRange(Secure("sprk_fileversion", ("sprk_document", Document)));
        rows.AddRange(Secure(Invoice,
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_regardingagreement", Agreement)));
        rows.AddRange(Secure("sprk_kpiassessment",
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_reportcard", ReportCard)));
        rows.AddRange(Secure("sprk_memo",
            ("sprk_regardingagreement", Agreement), ("sprk_regardinganalysis", Analysis), ("sprk_regardingbudget", Budget),
            ("sprk_regardingcommunication", Communication), ("sprk_regardingdocument", Document),
            ("sprk_regardingevent", Event), ("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", Matter),
            ("sprk_regardingproject", Project), ("sprk_regardingworkassignment", WorkAssignment),
            ("sprk_reportcard", ReportCard)));
        // #2084: a rule is a member only when its sprk_matter is set; its versions are the rule's.
        rows.AddRange(Secure("sprk_policy", ("sprk_matter", Matter)));
        rows.AddRange(Secure("sprk_policyversion", ("sprk_policy", "sprk_policy")));
        rows.AddRange(Secure(ReportCard, ("sprk_regardingmatter", Matter), ("sprk_regardingproject", Project)));
        // Ontology: the typed matter, the Decision Record and the subject's regarding lookups; never the cited rule.
        rows.AddRange(Secure("sprk_signal",
            ("sprk_matter", Matter), ("sprk_decisionrecord", "sprk_decisionrecord"),
            ("sprk_regardingcommunication", Communication), ("sprk_regardingdocument", Document),
            ("sprk_regardingevent", Event), ("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", Matter),
            ("sprk_regardingproject", Project), ("sprk_regardingtodo", ToDo),
            ("sprk_regardingworkassignment", WorkAssignment)));
        rows.AddRange(Secure("sprk_spendsignal",
            ("sprk_matter", Matter), ("sprk_project", Project), ("sprk_snapshot", "sprk_spendsnapshot")));
        rows.AddRange(Secure("sprk_spendsnapshot", ("sprk_matter", Matter), ("sprk_project", Project)));
        rows.AddRange(Secure(ToDo,
            ("sprk_regardingagreement", Agreement), ("sprk_regardinganalysis", Analysis), ("sprk_regardingbudget", Budget),
            ("sprk_regardingcommunication", Communication), ("sprk_regardingdocument", Document),
            ("sprk_regardingevent", Event), ("sprk_regardinginvoice", Invoice), ("sprk_regardingmatter", Matter),
            ("sprk_regardingproject", Project), ("sprk_regardingreportcard", ReportCard),
            ("sprk_regardingworkassignment", WorkAssignment)));

        // --- a service request's own family: ExternalDataService.ServiceRequestRelations at de38296499 (the document's
        // link is above, from DocumentLinkFields) ---
        rows.AddRange(new[]
        {
            (Analysis, "sprk_regardingservicerequest"),
            (Communication, "sprk_regardingservicerequest"),
            ("sprk_communicationthread", "sprk_regardingservicerequest"),
            ("sprk_decisionrecord", "sprk_regardingservicerequest"),
            (Event, "sprk_regardingservicerequest"),
            ("sprk_memo", "sprk_regardingservicerequest"),
            ("sprk_signal", "sprk_regardingservicerequest"),
            (ToDo, "sprk_regardingservicerequest"),
        }.Select(r => Row(r.Item1, r.Item2, ServiceRequest, inSecureFamily: false)));

        return rows;
    }

    private static IEnumerable<Edge> Secure(string child, params (string Column, string Parent)[] lookups) =>
        lookups.Select(l => Row(child, l.Column, l.Parent, inSecureFamily: true));

    private static Edge Row(string child, string column, string parent, bool inSecureFamily) =>
        new(FamilyKind.Filing, child, column, parent, KindOf(child, column), inSecureFamily, RecordNumberColumnOf(child));

    private static EdgeKind KindOf(string child, string column) =>
        CoreTables.Contains(child)
            ? string.Equals(column, PairIdColumn, StringComparison.OrdinalIgnoreCase) ? EdgeKind.Pair : EdgeKind.FilingCore
            : StampColumns.ContainsKey(column) ? EdgeKind.Stamp : EdgeKind.LineageHop;
}
