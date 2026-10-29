using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;
using Edge = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.Edge;
using EdgeKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.EdgeKind;
using FamilyKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.FamilyKind;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 070 (FR-29; design §12.3 A; UAC-r2 C2, C3) — <see cref="RecordFamilyEdges"/>
/// is the ONE list of filing edges, and three guards hold it there:
/// <list type="number">
/// <item><b>Parity (ratchet).</b> Every existing registry is a checked view of it: <c>SecureChildLineage.Children</c>,
/// <c>SecureRootInheritance.TypedFilingColumns</c> / <c>IntermediateFilingColumns</c> / the BFF-managed pair,
/// <c>CoreAncestorResolver</c>'s children (<c>IntermediateRootColumns</c> + the stamped children's stamp columns) and its
/// <c>StampSourceColumns</c>, <c>RecordOwnershipResolver.OwnershipParentEntities</c> and
/// <c>ExternalDataService.ServiceRequestRelations</c>. A disagreement not in <see cref="KnownDisagreements"/> fails; a
/// baseline entry that no longer describes a disagreement fails too, so the baseline is worked down, never left stale.</item>
/// <item><b>Content pin (UAC C3).</b> The six access columns of every row equal <see cref="PinnedAccessRows"/>. A change
/// fails with a message requiring a "UAC-r2 review:" line in the PR. <c>RecordNumberColumn</c> is not pinned.</item>
/// <item><b>Isolation stays Filing-only (UAC C2).</b> A synthetic non-Filing row leaves the SecureChildLineage view and
/// the CoreAncestorResolver children view unchanged; <c>RecordFamilyEdges.DetailTables</c> is display only and never
/// removes a row from them.</item>
/// <item><b>Ownership-parent lineage (UAC-r2 ruling, 2026-10-28).</b> Every SecureChildLineage lookup whose target is an
/// ownership parent (and is not a citation) is a registry row or a baselined disagreement.</item>
/// </list>
/// Each guard ships with must-fire and must-not-fire controls over synthetic data. Source only: static tables, no DI.
/// </summary>
public class RecordFamilyEdgesGuardTests
{
    private const string Registry = "RecordFamilyEdges";

    // ------------------------------------------------------------------------------------------------------------------
    // 1. Parity with a ratchet baseline
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The known disagreements at task 070, each with its one-line reason. UAC-r2 works these down; a line that no longer
    /// describes a disagreement fails (<see cref="ParityProblems"/>).
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> KnownDisagreements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // SecureChildLineage keeps every lookup on purpose (an extra lookup can only narrow a sharee mirror); these are
        // the ones its own remarks say are not filing.
        [Only("SecureChildLineage", "sprk_document.sprk_currentversionid -> sprk_fileversion")] =
            "a document's current version, not its filing (SecureChildLineage remarks)",
        [Only("SecureChildLineage", "sprk_document.sprk_canonicaldocument -> sprk_document")] =
            "a document's canonical copy, not its filing (SecureChildLineage remarks)",
        [Only("SecureChildLineage", "sprk_analysis.sprk_outputfileid -> sprk_document")] =
            "an analysis's output file, not its filing (SecureChildLineage remarks)",

        // ebilling-r1 (PROPOSAL, needs UAC-r2 review): invoice-submission lookups ebilling task 013 added to SecureChildLineage
        // that are not the submission's filing. Its filing root is its one core record (FR-05); see RecordFamilyEdges.
        [Only("SecureChildLineage", "sprk_invoicesubmission.sprk_resubmissionof -> sprk_invoicesubmission")] =
            "the earlier submission this one resubmits (same firm invoice number, D-37): a peer reference, not its filing; "
            + "sprk_invoicesubmission is not an ownership parent",
        // UAC-r2: decide — RecordOwnershipContext.ParentsOf reads sprk_document as an ownership-parent table, so a write
        // that sets this column offers the report as a parent, as it does for the analysis's sprk_outputfileid above.
        [Only("SecureChildLineage", "sprk_invoicesubmission.sprk_reportdocument -> sprk_document")] =
            "the adjustment or rejection report generated for the submission: its output, not its filing (as an "
            + "analysis's sprk_outputfileid)",

        // CoreAncestorResolver's stamp sources are the intermediates a stamp is COPIED from, not every filing lookup.
        [Extra("CoreAncestorResolver.StampSourceColumns", "sprk_analysis.sprk_documentid -> sprk_document")] =
            "the analysis's ownership primary, not a stamp source (CoreAncestorResolver.cs:212-214)",

        // CoreAncestorResolver's children are its stamp / root topology, not every filing lookup to a core.
        [Extra("CoreAncestorResolver children", "sprk_decisionrecord.sprk_matter -> sprk_matter")] =
            "typed decision-context core lookup: a lineage parent, not a stamp or root column (D-34, CoreAncestorResolver remarks)",
        [Extra("CoreAncestorResolver children", "sprk_decisionrecord.sprk_project -> sprk_project")] =
            "typed decision-context core lookup: a lineage parent, not a stamp or root column (D-34, CoreAncestorResolver remarks)",
        [Extra("CoreAncestorResolver children", "sprk_decisionrecord.sprk_workassignment -> sprk_workassignment")] =
            "typed decision-context core lookup: a lineage parent, not a stamp or root column (D-34, CoreAncestorResolver remarks)",
        [Extra("CoreAncestorResolver children", "sprk_signal.sprk_matter -> sprk_matter")] =
            "typed core lookup of a Signal: a lineage parent, not a stamp column (CoreAncestorResolver remarks, task 144)",
        [Extra("CoreAncestorResolver children", "sprk_document.sprk_relatedservicerequest -> sprk_servicerequest")] =
            "a service request's own family (ServiceRequestRelations); the stamp topology excludes it by name (IntermediateRootColumns)",

        // OwnershipParentEntities lists the tables a child can be filed under FOR OWNERSHIP.
        [Only("OwnershipParentEntities", "sprk_kpiassessment")] =
            "an ownership parent no filing lookup names (no SecureChildLineage row has a lookup to it)",
        [Only("OwnershipParentEntities", "sprk_memo")] =
            "an ownership parent (and stamp intermediate) no filing lookup names (no SecureChildLineage row has a lookup to it)",
        [Only("OwnershipParentEntities", "sprk_spendsignal")] =
            "an ownership parent no filing lookup names (no SecureChildLineage row has a lookup to it)",
        [Extra("OwnershipParentEntities", "sprk_decisionrecord")] =
            "a lineage parent of a Signal only (signal.sprk_decisionrecord); ownership does not list it",
        [Extra("OwnershipParentEntities", "sprk_emailartifact")] =
            "a lineage parent of an attachment artifact only; ownership does not list it",
        [Extra("OwnershipParentEntities", "sprk_communicationthread")] =
            "grouping container, never an ownership parent (RecordOwnershipResolver.cs:1150); a message takes its record thread's filing",
    };

    [Fact(DisplayName = "Task 070: every existing registry agrees with RecordFamilyEdges, or the disagreement is in the baseline")]
    public void EveryExistingRegistry_AgreesWithTheRegistry_OrIsBaselined()
    {
        var problems = ParityProblems(ParityViews(RecordFamilyEdges.All), KnownDisagreements);

        Assert.True(
            problems.Count == 0,
            "RecordFamilyEdges and an existing registry disagree (FR-29). A filing edge added to one registry must be added "
            + "to RecordFamilyEdges (with a \"UAC-r2 review:\" line in the PR, which the content pin also requires), or the "
            + "disagreement listed in KnownDisagreements with a one-line reason UAC-r2 accepts. A STALE line has been worked "
            + "down: delete it.\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 070: parity control — an edge added to SecureChildLineage that the registry lacks fails (must fire)")]
    public void ParityProblems_AnEdgeOnlyInASource_Fires()
    {
        var views = new[] { new ParityView("SecureChildLineage", new[] { "a.x -> b", "a.new -> c" }, new[] { "a.x -> b" }) };

        var problems = ParityProblems(views, new Dictionary<string, string>());

        Assert.Equal(new[] { "NEW: " + Only("SecureChildLineage", "a.new -> c") }, problems);
    }

    [Fact(DisplayName = "Task 070: parity control — an edge only in the registry fails (must fire)")]
    public void ParityProblems_AnEdgeOnlyInTheRegistry_Fires()
    {
        var views = new[] { new ParityView("SecureChildLineage", new[] { "a.x -> b" }, new[] { "a.x -> b", "a.extra -> c" }) };

        var problems = ParityProblems(views, new Dictionary<string, string>());

        Assert.Equal(new[] { "NEW: " + Extra("SecureChildLineage", "a.extra -> c") }, problems);
    }

    [Fact(DisplayName = "Task 070: parity control — a baselined disagreement passes (must not fire); a stale baseline line fails")]
    public void ParityProblems_BaselinedPasses_StaleFires()
    {
        var views = new[] { new ParityView("SecureChildLineage", new[] { "a.x -> b", "a.known -> c" }, new[] { "a.x -> b" }) };
        var baseline = new Dictionary<string, string>
        {
            [Only("SecureChildLineage", "a.known -> c")] = "seeded: known",
            [Only("SecureChildLineage", "a.gone -> d")] = "seeded: worked down",
        };

        var problems = ParityProblems(views, baseline);

        Assert.Equal(new[] { "STALE: " + Only("SecureChildLineage", "a.gone -> d") }, problems);
    }

    /// <summary>One existing registry, as edge keys, beside the registry's view of the same shape.</summary>
    internal sealed record ParityView(string Name, IReadOnlyCollection<string> Source, IReadOnlyCollection<string> Registry);

    internal static string Only(string name, string edge) => $"{name}: {edge} — in {name}, not in {Registry}";

    internal static string Extra(string name, string edge) => $"{name}: {edge} — in {Registry}, not in {name}";

    internal static IEnumerable<string> Disagreements(ParityView view)
    {
        var source = view.Source.ToHashSet(StringComparer.Ordinal);
        var registry = view.Registry.ToHashSet(StringComparer.Ordinal);
        return source.Except(registry).Select(e => Only(view.Name, e))
            .Concat(registry.Except(source).Select(e => Extra(view.Name, e)));
    }

    internal static IReadOnlyList<string> ParityProblems(IEnumerable<ParityView> views, IReadOnlyDictionary<string, string> baseline)
    {
        var all = views.SelectMany(Disagreements).ToHashSet(StringComparer.Ordinal);
        return all.Where(d => !baseline.ContainsKey(d)).Select(d => "NEW: " + d)
            .Concat(baseline.Keys.Where(k => !all.Contains(k)).Select(k => "STALE: " + k))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }

    private static string Key(string child, string column, string parent) =>
        $"{child.Trim().ToLowerInvariant()}.{column.Trim().ToLowerInvariant()} -> {parent.Trim().ToLowerInvariant()}";

    private static string Key(Edge e) => Key(e.ChildTable, e.LookupColumn, e.ParentTable);

    /// <summary>Each existing registry beside the registry's view of it, over <paramref name="rows"/>.</summary>
    internal static IReadOnlyList<ParityView> ParityViews(IReadOnlyList<Edge> rows)
    {
        var filing = RecordFamilyEdges.Of(FamilyKind.Filing, rows);

        var lineage = new ParityView(
            "SecureChildLineage",
            SecureChildLineage.Children.Values.SelectMany(t => t.Lookups.Select(l => Key(t.LogicalName, l.Key, l.Value))).ToArray(),
            RecordFamilyEdges.SecureLineageView(rows).SelectMany(t => t.Value.Select(l => Key(t.Key, l.Key, l.Value))).ToArray());

        var typed = new ParityView(
            "SecureRootInheritance.TypedFilingColumns",
            SecureRootInheritance.TypedFilingColumns.SelectMany(t => t.Value.Select(c => Key(t.Key, c.Column, c.ParentTable))).ToArray(),
            filing.Where(e => e.EdgeKind == EdgeKind.FilingCore && SecureRootInheritance.IsParent(e.ParentTable)).Select(Key).ToArray());

        var intermediate = new ParityView(
            "SecureRootInheritance.IntermediateFilingColumns",
            SecureRootInheritance.IntermediateFilingColumns.SelectMany(t => t.Value.Select(c => Key(t.Key, c.Column, c.Intermediate))).ToArray(),
            filing.Where(e => e.EdgeKind == EdgeKind.FilingCore && !SecureRootInheritance.IsParent(e.ParentTable)).Select(Key).ToArray());

        var pair = new ParityView(
            "SecureRootInheritance pair",
            SecureRootInheritance.BffManagedPairTables
                .SelectMany(t => new[] { SecureRootInheritance.Matter, SecureRootInheritance.Project }
                    .Select(p => Key(t, SecureRootInheritance.PairIdColumn, p)))
                .ToArray(),
            filing.Where(e => e.EdgeKind == EdgeKind.Pair).Select(Key).ToArray());

        // CoreAncestorResolver's children: the root columns of every intermediate, and the four stamp columns of every
        // stamped child; compared over the tables it classifies.
        var carTables = CoreAncestorResolver.IntermediateRootColumns.Keys
            .Concat(CoreAncestorResolver.StampSourceColumns.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var coreChildren = new ParityView(
            "CoreAncestorResolver children",
            CoreAncestorResolver.IntermediateRootColumns.SelectMany(t => t.Value.Select(c => Key(t.Key, c.Column, c.RootEntity)))
                .Concat(CoreAncestorResolver.StampSourceColumns.Keys.SelectMany(t =>
                    CoreAncestorResolver.CoreAncestorLookups.Select(l => Key(t, l.LookupAttribute, l.EntityType))))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            RecordFamilyEdges.CoreAncestorChildrenView(rows)
                .Where(t => carTables.Contains(t.Key))
                .SelectMany(t => t.Value.Select(c => Key(t.Key, c.Column, c.RootEntity)))
                .ToArray());

        var stampSources = new ParityView(
            "CoreAncestorResolver.StampSourceColumns",
            CoreAncestorResolver.StampSourceColumns.SelectMany(t => t.Value.Select(c => Key(t.Key, c.Column, c.Intermediate))).ToArray(),
            filing.Where(e => CoreAncestorResolver.StampSourceColumns.ContainsKey(e.ChildTable)
                              && CoreAncestorResolver.IntermediateRootColumns.ContainsKey(e.ParentTable))
                .Select(Key).ToArray());

        var ownershipParents = new ParityView(
            "OwnershipParentEntities",
            RecordOwnershipResolver.OwnershipParentEntities.Select(t => t.Trim().ToLowerInvariant()).ToArray(),
            filing.Select(e => e.ParentTable.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray());

        var serviceRequest = new ParityView(
            "ServiceRequestRelations",
            ExternalDataService.ServiceRequestRelations.Select(r => Key(r.EntityLogicalName, r.LookupAttribute, "sprk_servicerequest")).ToArray(),
            filing.Where(e => string.Equals(e.ParentTable, "sprk_servicerequest", StringComparison.OrdinalIgnoreCase)).Select(Key).ToArray());

        return new[] { lineage, typed, intermediate, pair, coreChildren, stampSources, ownershipParents, serviceRequest };
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 2. Content pin over the six access columns (UAC C3)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The access columns of every row (FamilyKind | child.lookup -> parent | EdgeKind | InSecureFamily), sorted. UAC-r2
    /// owns this content: change it only with a "UAC-r2 review:" line in the PR.
    /// ebilling-r1 PROPOSAL (needs UAC-r2 review): the 22 rows of sprk_externalidentifier, sprk_invoiceapproval,
    /// sprk_invoicecorrection and sprk_invoicesubmission, sprk_billingevent / sprk_invoice .sprk_regardingworkassignment and
    /// sprk_decisionrecord / sprk_signal .sprk_regardinginvoiceline (RecordFamilyEdges, the ebilling-r1 block).
    /// </summary>
    internal static readonly string[] PinnedAccessRows =
    {
        "Filing | sprk_agreement.sprk_regardingdocument -> sprk_document | LineageHop | secure",
        "Filing | sprk_agreement.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_agreement.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_analysis.sprk_documentid -> sprk_document | LineageHop | secure",
        "Filing | sprk_analysis.sprk_regardingbudget -> sprk_budget | LineageHop | secure",
        "Filing | sprk_analysis.sprk_regardingcommunication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_analysis.sprk_regardingdocument -> sprk_document | LineageHop | secure",
        "Filing | sprk_analysis.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_analysis.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_analysis.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_analysis.sprk_regardingservicerequest -> sprk_servicerequest | Stamp | not-secure",
        "Filing | sprk_analysis.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_analysisoutput.sprk_analysisid -> sprk_analysis | LineageHop | secure",
        "Filing | sprk_attachmentartifact.sprk_document -> sprk_document | LineageHop | secure",
        "Filing | sprk_attachmentartifact.sprk_emailartifact -> sprk_emailartifact | LineageHop | secure",
        "Filing | sprk_billingevent.sprk_invoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_billingevent.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_billingevent.sprk_project -> sprk_project | LineageHop | secure",
        "Filing | sprk_billingevent.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_budget.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_budget.sprk_project -> sprk_project | LineageHop | secure",
        "Filing | sprk_budgetbucket.sprk_budget -> sprk_budget | LineageHop | secure",
        "Filing | sprk_budgetrevision.sprk_budget -> sprk_budget | LineageHop | secure",
        "Filing | sprk_budgetrevision.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_communication.sprk_communicationthread -> sprk_communicationthread | LineageHop | secure",
        "Filing | sprk_communication.sprk_regardinganalysis -> sprk_analysis | LineageHop | secure",
        "Filing | sprk_communication.sprk_regardingbudget -> sprk_budget | LineageHop | secure",
        "Filing | sprk_communication.sprk_regardingevent -> sprk_event | LineageHop | secure",
        "Filing | sprk_communication.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_communication.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_communication.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_communication.sprk_regardingreportcard -> sprk_reportcard | LineageHop | secure",
        "Filing | sprk_communication.sprk_regardingservicerequest -> sprk_servicerequest | Stamp | not-secure",
        "Filing | sprk_communication.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_communicationattachment.sprk_communication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_communicationattachment.sprk_document -> sprk_document | LineageHop | secure",
        "Filing | sprk_communicationparticipant.sprk_communication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_communicationthread.sprk_regardinganalysis -> sprk_analysis | LineageHop | secure",
        "Filing | sprk_communicationthread.sprk_regardingbudget -> sprk_budget | LineageHop | secure",
        "Filing | sprk_communicationthread.sprk_regardingevent -> sprk_event | LineageHop | secure",
        "Filing | sprk_communicationthread.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_communicationthread.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_communicationthread.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_communicationthread.sprk_regardingservicerequest -> sprk_servicerequest | Stamp | not-secure",
        "Filing | sprk_communicationthread.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_decisionrecord.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_decisionrecord.sprk_project -> sprk_project | LineageHop | secure",
        "Filing | sprk_decisionrecord.sprk_regardingcommunication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_decisionrecord.sprk_regardingdocument -> sprk_document | LineageHop | secure",
        "Filing | sprk_decisionrecord.sprk_regardingevent -> sprk_event | LineageHop | secure",
        "Filing | sprk_decisionrecord.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_decisionrecord.sprk_regardinginvoiceline -> sprk_billingevent | LineageHop | secure",
        "Filing | sprk_decisionrecord.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_decisionrecord.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_decisionrecord.sprk_regardingservicerequest -> sprk_servicerequest | Stamp | not-secure",
        "Filing | sprk_decisionrecord.sprk_regardingtodo -> sprk_todo | LineageHop | secure",
        "Filing | sprk_decisionrecord.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_decisionrecord.sprk_workassignment -> sprk_workassignment | LineageHop | secure",
        "Filing | sprk_document.sprk_invoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_document.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_document.sprk_parentdocument -> sprk_document | LineageHop | secure",
        "Filing | sprk_document.sprk_project -> sprk_project | LineageHop | secure",
        "Filing | sprk_document.sprk_relatedagreement -> sprk_agreement | LineageHop | secure",
        "Filing | sprk_document.sprk_relatedcommunication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_document.sprk_relatedevent -> sprk_event | LineageHop | secure",
        "Filing | sprk_document.sprk_relatedinvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_document.sprk_relatedmatter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_document.sprk_relatedproject -> sprk_project | LineageHop | secure",
        "Filing | sprk_document.sprk_relatedservicerequest -> sprk_servicerequest | LineageHop | not-secure",
        "Filing | sprk_document.sprk_relatedtodo -> sprk_todo | LineageHop | secure",
        "Filing | sprk_document.sprk_relatedworkassignment -> sprk_workassignment | LineageHop | secure",
        "Filing | sprk_document.sprk_workassignment -> sprk_workassignment | LineageHop | secure",
        "Filing | sprk_emailartifact.sprk_document -> sprk_document | LineageHop | secure",
        "Filing | sprk_emailreviewlog.sprk_communication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_event.sprk_regardingagreement -> sprk_agreement | LineageHop | secure",
        "Filing | sprk_event.sprk_regardinganalysis -> sprk_analysis | LineageHop | secure",
        "Filing | sprk_event.sprk_regardingbudget -> sprk_budget | LineageHop | secure",
        "Filing | sprk_event.sprk_regardingcommunication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_event.sprk_regardingevent -> sprk_event | LineageHop | secure",
        "Filing | sprk_event.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_event.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_event.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_event.sprk_regardingreportcard -> sprk_reportcard | LineageHop | secure",
        "Filing | sprk_event.sprk_regardingservicerequest -> sprk_servicerequest | Stamp | not-secure",
        "Filing | sprk_event.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_eventlog.sprk_event -> sprk_event | LineageHop | secure",
        "Filing | sprk_externalidentifier.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_externalidentifier.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_externalidentifier.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_externalidentifier.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_fileversion.sprk_document -> sprk_document | LineageHop | secure",
        "Filing | sprk_invoice.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_invoice.sprk_project -> sprk_project | LineageHop | secure",
        "Filing | sprk_invoice.sprk_regardingagreement -> sprk_agreement | LineageHop | secure",
        "Filing | sprk_invoice.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_invoiceapproval.sprk_invoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_invoiceapproval.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_invoiceapproval.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_invoiceapproval.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_invoicecorrection.sprk_invoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_invoicecorrection.sprk_invoiceline -> sprk_billingevent | LineageHop | secure",
        "Filing | sprk_invoicecorrection.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_invoicecorrection.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_invoicecorrection.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_invoicesubmission.sprk_document -> sprk_document | LineageHop | secure",
        "Filing | sprk_invoicesubmission.sprk_invoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_invoicesubmission.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_invoicesubmission.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_invoicesubmission.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_kpiassessment.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_kpiassessment.sprk_project -> sprk_project | LineageHop | secure",
        "Filing | sprk_kpiassessment.sprk_reportcard -> sprk_reportcard | LineageHop | secure",
        "Filing | sprk_memo.sprk_regardingagreement -> sprk_agreement | LineageHop | secure",
        "Filing | sprk_memo.sprk_regardinganalysis -> sprk_analysis | LineageHop | secure",
        "Filing | sprk_memo.sprk_regardingbudget -> sprk_budget | LineageHop | secure",
        "Filing | sprk_memo.sprk_regardingcommunication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_memo.sprk_regardingdocument -> sprk_document | LineageHop | secure",
        "Filing | sprk_memo.sprk_regardingevent -> sprk_event | LineageHop | secure",
        "Filing | sprk_memo.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_memo.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_memo.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_memo.sprk_regardingservicerequest -> sprk_servicerequest | Stamp | not-secure",
        "Filing | sprk_memo.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_memo.sprk_reportcard -> sprk_reportcard | LineageHop | secure",
        "Filing | sprk_policy.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_policyversion.sprk_policy -> sprk_policy | LineageHop | secure",
        "Filing | sprk_project.sprk_regardingrecordid -> sprk_matter | Pair | secure",
        "Filing | sprk_project.sprk_regardingrecordid -> sprk_project | Pair | secure",
        "Filing | sprk_reportcard.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_reportcard.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_signal.sprk_decisionrecord -> sprk_decisionrecord | LineageHop | secure",
        "Filing | sprk_signal.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_signal.sprk_regardingcommunication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_signal.sprk_regardingdocument -> sprk_document | LineageHop | secure",
        "Filing | sprk_signal.sprk_regardingevent -> sprk_event | LineageHop | secure",
        "Filing | sprk_signal.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_signal.sprk_regardinginvoiceline -> sprk_billingevent | LineageHop | secure",
        "Filing | sprk_signal.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_signal.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_signal.sprk_regardingservicerequest -> sprk_servicerequest | Stamp | not-secure",
        "Filing | sprk_signal.sprk_regardingtodo -> sprk_todo | LineageHop | secure",
        "Filing | sprk_signal.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_spendsignal.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_spendsignal.sprk_project -> sprk_project | LineageHop | secure",
        "Filing | sprk_spendsignal.sprk_snapshot -> sprk_spendsnapshot | LineageHop | secure",
        "Filing | sprk_spendsnapshot.sprk_matter -> sprk_matter | LineageHop | secure",
        "Filing | sprk_spendsnapshot.sprk_project -> sprk_project | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardingagreement -> sprk_agreement | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardinganalysis -> sprk_analysis | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardingbudget -> sprk_budget | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardingcommunication -> sprk_communication | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardingdocument -> sprk_document | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardingevent -> sprk_event | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardinginvoice -> sprk_invoice | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardingmatter -> sprk_matter | Stamp | secure",
        "Filing | sprk_todo.sprk_regardingproject -> sprk_project | Stamp | secure",
        "Filing | sprk_todo.sprk_regardingreportcard -> sprk_reportcard | LineageHop | secure",
        "Filing | sprk_todo.sprk_regardingservicerequest -> sprk_servicerequest | Stamp | not-secure",
        "Filing | sprk_todo.sprk_regardingworkassignment -> sprk_workassignment | Stamp | secure",
        "Filing | sprk_workassignment.sprk_regardingevent -> sprk_event | FilingCore | secure",
        "Filing | sprk_workassignment.sprk_regardinginvoice -> sprk_invoice | FilingCore | secure",
        "Filing | sprk_workassignment.sprk_regardingmatter -> sprk_matter | FilingCore | secure",
        "Filing | sprk_workassignment.sprk_regardingproject -> sprk_project | FilingCore | secure",
    };

    [Fact(DisplayName = "Task 070: the access columns of RecordFamilyEdges match the UAC-r2 pin")]
    public void AccessColumns_MatchThePin()
    {
        var failure = PinFailure(PinnedAccessRows, AccessRows(RecordFamilyEdges.All));

        Assert.True(failure is null, failure);
    }

    [Fact(DisplayName = "Task 070: pin control — changing any of the six access columns fails and names the UAC-r2 review line (must fire)")]
    public void PinFailure_AnyAccessColumnChange_FiresWithTheReviewLine()
    {
        var row = new Edge(FamilyKind.Filing, "sprk_child", "sprk_lookup", "sprk_parent", EdgeKind.LineageHop, true, "sprk_number");
        var pinned = AccessRows(new[] { row });
        var changed = new[]
        {
            row with { FamilyKind = (FamilyKind)2 },
            row with { ChildTable = "sprk_other" },
            row with { LookupColumn = "sprk_otherlookup" },
            row with { ParentTable = "sprk_otherparent" },
            row with { EdgeKind = EdgeKind.Stamp },
            row with { InSecureFamily = false },
        };

        Assert.All(changed, c =>
        {
            var failure = PinFailure(pinned, AccessRows(new[] { c }));
            Assert.NotNull(failure);
            Assert.Contains("UAC-r2 review:", failure, StringComparison.Ordinal);
        });
    }

    [Fact(DisplayName = "Task 070: pin control — changing RecordNumberColumn does not fail the pin (must not fire)")]
    public void PinFailure_RecordNumberColumnChange_DoesNotFire()
    {
        var row = new Edge(FamilyKind.Filing, "sprk_child", "sprk_lookup", "sprk_parent", EdgeKind.LineageHop, true, "sprk_number");

        var failure = PinFailure(AccessRows(new[] { row }), AccessRows(new[] { row with { RecordNumberColumn = null } }));

        Assert.Null(failure);
    }

    internal static IReadOnlyList<string> AccessRows(IEnumerable<Edge> rows) =>
        rows.Select(e => $"{e.FamilyKind} | {Key(e)} | {e.EdgeKind} | {(e.InSecureFamily ? "secure" : "not-secure")}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

    internal static string? PinFailure(IReadOnlyCollection<string> pinned, IReadOnlyCollection<string> actual)
    {
        var added = actual.Except(pinned, StringComparer.Ordinal).ToArray();
        var removed = pinned.Except(actual, StringComparer.Ordinal).ToArray();
        if (added.Length == 0 && removed.Length == 0 && pinned.Count == actual.Count)
        {
            return null;
        }

        return "The access columns of RecordFamilyEdges changed (FamilyKind, ChildTable, LookupColumn, ParentTable, EdgeKind, "
               + "InSecureFamily — UAC-r2 C3). UAC-r2 owns this content: update PinnedAccessRows AND add a line "
               + "\"UAC-r2 review: <reviewer, date, what was reviewed>\" to the PR description before merge.\n"
               + string.Join("\n", added.Select(a => "  + " + a).Concat(removed.Select(r => "  - " + r)));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 3. Isolation views stay Filing-only (UAC C2)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>A row of a kind that is not Filing (a later peer kind such as a priority edge), on tables both views read.</summary>
    private static readonly Edge NonFilingRow =
        new((FamilyKind)2, "sprk_document", "sprk_prioritymatter", "sprk_matter", EdgeKind.LineageHop, true, null);

    [Fact(DisplayName = "Task 070: C2 — a synthetic non-Filing row leaves the SecureChildLineage and CoreAncestorResolver children views unchanged")]
    public void IsolationViews_ANonFilingRow_LeavesThemUnchanged()
    {
        var withRow = RecordFamilyEdges.All.Append(NonFilingRow).ToArray();

        Assert.Equal(Flatten(RecordFamilyEdges.SecureLineageView()), Flatten(RecordFamilyEdges.SecureLineageView(withRow)));
        Assert.Equal(Flatten(RecordFamilyEdges.CoreAncestorChildrenView()), Flatten(RecordFamilyEdges.CoreAncestorChildrenView(withRow)));
        Assert.Equal(
            RecordFamilyEdges.Of(FamilyKind.Filing).Select(Key),
            RecordFamilyEdges.Of(FamilyKind.Filing, withRow).Select(Key));
    }

    [Fact(DisplayName = "Task 070: C2 control — the same row as Filing changes both views (the C2 test is not vacuous)")]
    public void IsolationViews_TheSameRowAsFiling_ChangesThem()
    {
        var withRow = RecordFamilyEdges.All.Append(NonFilingRow with { FamilyKind = FamilyKind.Filing }).ToArray();

        Assert.NotEqual(Flatten(RecordFamilyEdges.SecureLineageView()), Flatten(RecordFamilyEdges.SecureLineageView(withRow)));
        Assert.NotEqual(Flatten(RecordFamilyEdges.CoreAncestorChildrenView()), Flatten(RecordFamilyEdges.CoreAncestorChildrenView(withRow)));
    }

    [Fact(DisplayName = "Task 070: DetailTables is display only — the isolation views still carry every detail table's rows")]
    public void DetailTables_LeaveTheIsolationViewsUnchanged()
    {
        var filing = RecordFamilyEdges.Of(FamilyKind.Filing);
        var lineage = Flatten(RecordFamilyEdges.SecureLineageView()).ToHashSet(StringComparer.Ordinal);
        var coreChildren = Flatten(RecordFamilyEdges.CoreAncestorChildrenView()).ToHashSet(StringComparer.Ordinal);
        var detailRows = filing.Where(e => RecordFamilyEdges.DetailTables.Contains(e.ChildTable)).ToArray();

        Assert.All(RecordFamilyEdges.DetailTables, t => Assert.Contains(filing, e => e.ChildTable == t));
        Assert.DoesNotContain("sprk_kpiassessment", RecordFamilyEdges.DetailTables);
        Assert.All(detailRows.Where(e => e.InSecureFamily), e => Assert.Contains(Key(e), lineage));
        Assert.All(detailRows.Where(e => RecordFamilyEdges.CoreTables.Contains(e.ParentTable)), e => Assert.Contains(Key(e), coreChildren));
    }

    // ------------------------------------------------------------------------------------------------------------------
    // 4. Every SecureChildLineage lookup to an ownership parent is a filing row (UAC-r2 ruling, 2026-10-28)
    // ------------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "Task 070: every SecureChildLineage lookup to an ownership parent (not a citation) is a registry row or baselined")]
    public void EveryOwnershipParentLineageLookup_IsARow_OrBaselined()
    {
        var gaps = OwnershipLineageGaps(LineageLookups(), RecordFamilyEdges.All, KnownDisagreements);

        Assert.True(
            gaps.Count == 0,
            "A SecureChildLineage lookup names an ownership parent (RecordOwnershipResolver.IsOwnershipParent) and is not a "
            + "citation, so it files the row — it must be a RecordFamilyEdges row (UAC-r2 review: line in the PR) or a "
            + "baselined SecureChildLineage disagreement with UAC-r2's reason:\n" + string.Join("\n", gaps));
    }

    [Fact(DisplayName = "Task 070: ownership-lineage control — the analysis's sprk_documentid row removed fires (must fire)")]
    public void OwnershipLineageGaps_TheAnalysisPrimaryRemoved_Fires()
    {
        var rows = RecordFamilyEdges.All
            .Where(e => !(e.ChildTable == "sprk_analysis" && e.LookupColumn == "sprk_documentid"))
            .ToArray();

        var gaps = OwnershipLineageGaps(LineageLookups(), rows, KnownDisagreements);

        Assert.Equal(new[] { "sprk_analysis.sprk_documentid -> sprk_document" }, gaps);
    }

    [Fact(DisplayName = "Task 070: ownership-lineage control — a citation, a non-parent target and a baselined lookup pass (must not fire)")]
    public void OwnershipLineageGaps_CitationNonParentOrBaselined_DoesNotFire()
    {
        var lineage = new[]
        {
            ("sprk_signal", "sprk_policy", "sprk_policy"), // a citation (RecordOwnershipResolver.IsCitation)
            ("sprk_child", "sprk_version", "sprk_fileversion"), // not an ownership parent
            ("sprk_child", "sprk_known", "sprk_document"), // baselined
        };
        var baseline = new Dictionary<string, string>
        {
            [Only("SecureChildLineage", "sprk_child.sprk_known -> sprk_document")] = "seeded: known",
        };

        Assert.Empty(OwnershipLineageGaps(lineage, Array.Empty<Edge>(), baseline));
    }

    private static IEnumerable<(string Child, string Column, string Target)> LineageLookups() =>
        SecureChildLineage.Children.Values.SelectMany(t => t.Lookups.Select(l => (t.LogicalName, l.Key, l.Value)));

    internal static IReadOnlyList<string> OwnershipLineageGaps(
        IEnumerable<(string Child, string Column, string Target)> lineage,
        IEnumerable<Edge> rows,
        IReadOnlyDictionary<string, string> baseline)
    {
        var registry = RecordFamilyEdges.Of(FamilyKind.Filing, rows).Select(Key).ToHashSet(StringComparer.Ordinal);
        return lineage
            .Where(l => RecordOwnershipResolver.IsOwnershipParent(l.Target) && !RecordOwnershipResolver.IsCitation(l.Child, l.Column))
            .Select(l => Key(l.Child, l.Column, l.Target))
            .Where(k => !registry.Contains(k) && !baseline.ContainsKey(Only("SecureChildLineage", k)))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> Flatten(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> view) =>
        view.SelectMany(t => t.Value.Select(l => Key(t.Key, l.Key, l.Value))).OrderBy(s => s, StringComparer.Ordinal).ToArray();

    private static IReadOnlyList<string> Flatten(IReadOnlyDictionary<string, IReadOnlyList<(string Column, string RootEntity)>> view) =>
        view.SelectMany(t => t.Value.Select(c => Key(t.Key, c.Column, c.RootEntity))).OrderBy(s => s, StringComparer.Ordinal).ToArray();
}
