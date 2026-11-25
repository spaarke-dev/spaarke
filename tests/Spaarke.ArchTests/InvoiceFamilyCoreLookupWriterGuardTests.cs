using System.Reflection;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 DEF-064 (#2643): the BFF writers of an invoice-family row's core-record lookups all go through
/// <see cref="InvoiceFamilyCoreRecordGuard"/>, and a new one cannot be added without the guard. A row of a family table names exactly one
/// core record (matter, project or work assignment); the guard is the one server-side owner of that rule (FR-05). Every write path calls it,
/// each pinned by its own tests, but nothing pinned the COMPLETENESS of that set. Two pins here, read from the compiled IL
/// (<see cref="IlCallScan"/>, detector in <see cref="InvoiceFamilyWriterScan"/>):
/// </summary>
/// <remarks>
/// <para><b>1. The roster (positive pin).</b> Every type that calls a deciding member of the guard
/// (<see cref="InvoiceFamilyWriterScan.Enforcing"/>) is on <see cref="Roster"/> with a one-line "writes X" reason, and must keep calling
/// one: removing the call from a listed type fails the test, as does a listed type that no longer exists, and so does a NEW caller that is
/// not listed. A path that reaches the guard through another type is pinned as a <see cref="Delegates"/> entry (the type must keep
/// referencing the type that decides).</para>
/// <para><b>2. The scan (negative pin).</b> A type is a <b>candidate writer</b> when its IL loads the name of a family table, loads a
/// core-lookup column name AND has a method that calls a Dataverse write without calling a deciding guard member in that same method
/// (a lambda's body, which cannot be tied to its method, is decided when any body of the type calls one). Tables and columns come from
/// <see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/>; names are compared case-insensitively. A table is its name, its entity-set
/// plural, or either one opening a Web API path (<c>sprk_invoices(</c>, <c>sprk_invoices/</c>); a column may carry a trailing
/// <c>@odata.bind</c>. The typed <c>sprk_matter</c> / <c>sprk_project</c> count as a column unless the
/// load is the entity-name argument of <c>new EntityReference(...)</c>. A write is a method named <c>Create*</c>, <c>Update*</c>,
/// <c>Upsert*</c>, <c>Patch*</c>, <c>Post*</c>, <c>BulkUpdate*</c>, <c>Associate*</c> or <c>Execute*</c> declared in
/// <c>Spaarke.Dataverse</c>, <c>Microsoft.Xrm.Sdk</c>, <c>Microsoft.PowerPlatform</c> or <c>Sprk.Bff.Api.Infrastructure.Dataverse</c>. A
/// candidate must be on <see cref="Reviewed"/> (by method), with a reason.</para>
/// <para><b>Not covered:</b> a column or table name that is not a string constant in the writing type; a write through a seam outside the
/// four namespaces; a type that names a family table and a core column and writes a DIFFERENT column of that row (that case is why the
/// reviewed list exists); the entity-name argument heuristic reads three instructions after the string load; an overload of a method shares
/// the guard call of a same-named one.</para>
/// <para><b>Maintaining <see cref="Reviewed"/>.</b> A failure names the type, method, tables, columns and write that matched. First choice:
/// call the guard in that method. Only add a reviewed entry when the method (a) writes none of the family's core lookups, (b) writes them
/// only to restore the row's own previous values, or (c) is a post-decision writer whose every caller decides first (and is on the
/// roster or delegates). The reason says which and what the method writes. A reviewed method that no longer matches fails, so the list only
/// shrinks. Never add an entry without reading the write. A new deciding caller goes on <see cref="Roster"/>.</para>
/// <para><b>Controls</b>: <see cref="Detector_MustFireAndMustNotFire"/> runs the detector over the <c>InvoiceFamilyWriterControl*</c>
/// fixtures below. Enforced by: this guard (ArchTests in CI and the Stop hook).</para>
/// </remarks>
public class InvoiceFamilyCoreLookupWriterGuardTests
{
    /// <summary>Every type that calls a deciding guard member today, with what it writes. The closed set (see the class remarks).</summary>
    private static readonly IReadOnlyDictionary<string, string> Roster = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints"] =
            "writes the field-mapping push's target row (any table, family rows included) as its caller: CheckGenericUpdateAsync before the write",
        ["Sprk.Bff.Api.Api.Finance.InvoiceSubmissionEndpoints"] =
            "writes the invoice submission row naming its core record on create: Check and DeriveResolverFieldsAsync",
        ["Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore"] =
            "writes the playbook update action's target row (any table, family rows included): CheckGenericUpdateAsync",
        ["Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver"] =
            "derives a family row's core record from the stored row (Check) for the ancestor stamp; its own writes are access-permission columns",
        ["Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler"] =
            "writes the output orchestrator's update of any row, family rows included: CheckGenericUpdateAsync",
        ["Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite"] =
            "the create / update / re-file core of every child-record route and chat tool; writes family rows' core lookups and the resolver fields",
        ["Sprk.Bff.Api.Services.Finance.InvoiceAdjustmentService"] =
            "creates an adjustment line (sprk_billingevent) under an invoice: Check on the row it builds",
        ["Sprk.Bff.Api.Services.Finance.InvoiceCorrectionService"] =
            "creates a correction row (sprk_invoicecorrection) carrying the line's core lookups: Check on the row it builds",
        ["Sprk.Bff.Api.Services.Finance.InvoiceLineAddService"] =
            "creates an invoice line (sprk_billingevent) under an invoice: Check on the row it builds",
        ["Sprk.Bff.Api.Services.Finance.InvoiceLineDecisionService"] =
            "creates the correction rows of a line decision carrying the line's core lookups: Check on the row it builds",
        ["Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister"] =
            "creates the invoice, line and submission rows of a submission, naming their core record: Check and DeriveResolverFieldsAsync",
        ["Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionService"] =
            "creates the submission row naming its core record: Check and DeriveResolverFieldsAsync",
        ["Sprk.Bff.Api.Services.Finance.Submission.SubmitInvoiceFromEmailOperation"] =
            "creates the submission of an invoice taken from an email: Check on the row it builds",
        ["Sprk.Bff.Api.Services.RecordCreation.RecordTableRules"] =
            "the create core's invoice-family pre-write step: DeriveGenericResolverFieldsAsync for every family table",
    };

    /// <summary>A type that reaches the guard through another type: it must keep referencing that type's member (<c>*</c> = any member).</summary>
    private static readonly IReadOnlyList<(string Type, string Target, string Member, string Reason)> Delegates =
    [
        ("Sprk.Bff.Api.Services.RecordCreation.RecordCreationService", "Sprk.Bff.Api.Services.RecordCreation.RecordTableRules", "Find",
            "the create core: its per-table pre-write rules (RecordTableRules, which decides every family create) are looked up here"),
        ("Sprk.Bff.Api.Api.ChildRecordEndpoints", "Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite", "*",
            "the child-record routes: every create, update and re-file of a family row is written through OwnedChildWrite"),
    ];

    private sealed record ReviewedType(string Reason, string[] Methods);

    /// <summary>
    /// Candidate writers that were read and write none of the family's core lookups (or only restore the row's own previous values, or
    /// follow a caller's decision). Keyed by type, with the methods reviewed; the closed set the scan must equal.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ReviewedType> Reviewed = new Dictionary<string, ReviewedType>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Services.Access.ExternalTeamShareSynchronizer"] = new(
            "(a) names the family tables to re-own rows; writes only ownerid (a team) with a row-version guard", ["(closures)"]),
        ["Sprk.Bff.Api.Services.Access.SecureRootInheritance"] = new(
            "(a) reads the lineage columns; writes only the access-permission and inheritance-marker columns", ["WriteMarkerAsync", "WritePermissionAsync"]),
        ["Sprk.Bff.Api.Services.Ai.Handlers.DataverseSearchDataHandler"] = new(
            "(a) POSTs a search query to the search service; reads only", ["ExecuteChatAsync"]),
        ["Sprk.Bff.Api.Services.Ai.Handlers.EmailDraftToolHandler"] = new(
            "(a) creates the draft sprk_communication row; the family tables are only regarding record types", ["ExecuteDraftAsync"]),
        ["Sprk.Bff.Api.Services.Ai.Nodes.TaskActionCore"] = new(
            "(a) creates a to-do (sprk_event) with its own regarding pair; the family tables are only regarding record types", ["CreateAsync"]),
        ["Sprk.Bff.Api.Services.Communication.CommunicationEnrichmentService"] = new(
            "(a) writes sprk_communication triage, owner and review-log columns; the family tables are only regarding record types",
            ["AnnotateTriageSummaryAsync", "AssignOwningTeamAsync", "CreateReviewLogAsync", "PersistTriageResultAsync"]),
        ["Sprk.Bff.Api.Services.Communication.CommunicationService"] = new(
            "(a) creates and updates sprk_communication and its attachment rows; the invoice is a regarding record type",
            [
                "ArchiveExistingAttachmentsAsync", "ArchiveOutboundAttachmentsAsync", "ArchiveToSpeAsync", "CreateAttachmentRecordsAsync",
                "CreatePreparedRecordAsync", "PostToRecordThreadAsync", "RetirePostWithoutThreadAsync", "StampInternetMessageIdAsync",
            ]),
        ["Sprk.Bff.Api.Services.Communication.EmailUploadCaptureService"] = new(
            "(a) creates sprk_communication; the invoice is only a regarding record type", ["CaptureWithOutcomeAsync"]),
        ["Sprk.Bff.Api.Services.Dataverse.InvoiceFamilyLockedWrite"] = new(
            "(c) the post-decision writer of the locked lookups, as the application, after the caller's guard decision: its callers are "
            + "ChildRecordEndpoints (delegate: OwnedChildWrite) and FieldMappingEndpoints (roster: CheckGenericUpdateAsync)",
            ["PatchAsCallerAsync", "UpdateExistingSplitAsync"]),
        ["Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver"] = new(
            "(b) the re-file's owner step: writes ownerid and, after a failed re-file, restores the row's own previous values; the re-file itself is "
            + "OwnedChildWrite.RefileAsync, which calls the guard",
            ["PutBackFilingAsync", "ReleaseOwnSecureWriteAsync", "SettleOwnerAsync"]),
        ["Sprk.Bff.Api.Services.Finance.ApExportService"] = new(
            "(a) creates the export batch row and writes the invoice's batch lookup; reads the core columns for the export lines",
            ["CompensateAsync", "CompleteAsync", "CreateBatchAsync", "ReleaseClaimAsync"]),
        ["Sprk.Bff.Api.Services.Finance.EligibleApproverResolver"] = new(
            "(a) associates eligible approvers and stamps the resolved-on column of an approval", ["SyncAsync"]),
        ["Sprk.Bff.Api.Services.Finance.InvoiceCorrectionReevaluation"] = new(
            "(a) writes a submission's findings and status and a line's status only",
            ["DeriveStatusAsync", "PatchFindingsAsync", "ReconcileSignalsAsync", "RecordFollowedThroughAsync", "RederivePendingAsync"]),
        ["Sprk.Bff.Api.Services.Finance.SpendSnapshotService"] = new(
            "(a) upserts its own spend-snapshot rows (its matter / project lookups are the snapshot's, not a family row's)", ["UpsertSnapshotAsync"]),
        ["Sprk.Bff.Api.Services.Finance.Submission.SubmissionCommitGate"] = new(
            "(a) writes a submission's status and failure columns, reopens a superseded invoice's status and rolls back its own rows",
            ["CompensateAsync", "NoteUndoHeldAsync", "ReopenSupersededAsync", "UndoAsync"]),
        ["Sprk.Bff.Api.Services.Finance.SubmissionOverrideService"] = new(
            "(a) writes a submission's status and an invoice's decision note and review detail in one transaction",
            ["DecideLockedAsync", "RepairReleasedColumnsAsync"]),
        ["Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence"] = new(
            "(a) writes sprk_document and its association lookups; the invoice is a document's association target, not a family row",
            ["CreateDocumentWithSpePointersAsync", "GraduateLinkedCopyIfDivergedAsync", "LinkEditableCopyToCanonicalAsync", "RecordNewVersionAsync"]),
        ["Sprk.Bff.Api.Services.Ontology.OntologyAdminAuthoringService"] = new(
            "(a) writes the ontology's own policy and version rows",
            ["PublishVersionAsync", "SavePolicyAsync", "StampAndMoveAsync", "StampInForceToAsync"]),
        ["Sprk.Bff.Api.Services.Workspace.TodoGenerationService"] = new(
            "(a) creates to-do rows with their own regarding pair; the invoice and matter are query targets and regarding record types",
            ["CreateTodoAsync"]),
    };

    /// <summary>
    /// Methods of a type on <see cref="Roster"/> that write other things than the row the type decides. Each decides its own row in another
    /// method of the same type (the roster pin holds that call), so these are reviewed by method like the candidates.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, ReviewedType> ReviewedOnRoster = new Dictionary<string, ReviewedType>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver"] = new(
            "(a) writes the access-permission column only", ["RefreshDocumentAccessPermissionAsync", "RefreshInheritedAccessPermissionAsync"]),
        ["Sprk.Bff.Api.Services.Finance.InvoiceAdjustmentService"] = new(
            "(a) writes a line's active state and the invoice totals; the added line's row is decided by Check where it is built",
            ["ApplyRemoveAsync", "FinishAddAsync", "UndoRemovalAsync"]),
        ["Sprk.Bff.Api.Services.Finance.InvoiceCorrectionService"] = new(
            "(a) submits the correction row decided by Check where it is built, with the line value updates", ["ApplyAsync"]),
        ["Sprk.Bff.Api.Services.Finance.InvoiceLineAddService"] = new(
            "(a) reconciles the signal rows of the added line; the line itself is decided by Check where it is built", ["ReconcileSignalsAsync"]),
        ["Sprk.Bff.Api.Services.Finance.InvoiceLineDecisionService"] = new(
            "(a) writes line decision status and the correction rows decided by Check where they are built", ["ApplyAsync", "WriteUnreviewedAsync"]),
    };

    private static IReadOnlyDictionary<string, InvoiceFamilyWriterScan.TypeFindings> ScanProduct() =>
        InvoiceFamilyWriterScan.Scan(typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "DEF-064 roster: every type that calls a deciding member of InvoiceFamilyCoreRecordGuard is listed, and keeps calling one")]
    public void EveryDecidingCaller_IsOnTheRoster_AndEveryRosterTypeStillCalls()
    {
        var assembly = typeof(Program).Assembly;
        var found = ScanProduct();
        var callers = found.Values.Where(f => f.EnforcingCalls.Count > 0).Select(f => f.Type).ToHashSet(StringComparer.Ordinal);

        var unlisted = callers.Except(Roster.Keys).OrderBy(n => n).ToList();
        Assert.True(
            unlisted.Count == 0,
            "These types call a deciding member of InvoiceFamilyCoreRecordGuard but are not on the roster: " + string.Join(", ", unlisted)
            + ". Add each with a one-line 'writes X' reason.");

        var gone = Roster.Keys.Where(n => assembly.GetType(n) is null).OrderBy(n => n).ToList();
        Assert.True(gone.Count == 0, "Roster types that no longer exist (remove or rename the entry): " + string.Join(", ", gone));

        var dropped = Roster.Keys.Except(callers).OrderBy(n => n).ToList();
        Assert.True(
            dropped.Count == 0,
            "These roster types no longer call a deciding member of InvoiceFamilyCoreRecordGuard (" + string.Join(", ", InvoiceFamilyWriterScan.Enforcing.Order())
            + "): " + string.Join(", ", dropped) + ". The write they own would be unguarded: restore the call.");

        Assert.All(Roster, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key + " needs a reason."));
    }

    [Fact(DisplayName = "DEF-064 delegates: the create core and the child-record routes keep reaching the guard through the type that decides")]
    public void EveryDelegate_StillReferencesItsDecidingType()
    {
        var assembly = typeof(Program).Assembly;
        foreach (var (typeName, target, member, _) in Delegates)
        {
            var type = assembly.GetType(typeName);
            Assert.True(type is not null, typeName + " no longer exists (update Delegates).");
            Assert.True(assembly.GetType(target) is not null, target + " no longer exists (update Delegates).");

            var references = IlCallScan.MethodReferences(IlCallScan.WithNested(type!))
                .Any(r => r.Target.DeclaringType is { } d && IlCallScan.Outermost(d).FullName == target
                          && (member == "*" || r.Target.Name == member));
            Assert.True(references, $"{typeName} no longer references {target}.{member}: its writes of family rows would skip the guard.");
        }
    }

    [Fact(DisplayName = "DEF-064: every method that writes a family row's core lookups calls the guard in that method, or is reviewed")]
    public void EveryFamilyCoreLookupWriter_CallsTheGuard_OrIsReviewed()
    {
        var candidates = ScanProduct().Values.Where(f => f.IsCandidate).ToDictionary(f => f.Type, StringComparer.Ordinal);
        var reviewed = Reviewed.Concat(ReviewedOnRoster).ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

        var unreviewed = new List<string>();
        foreach (var (type, findings) in candidates)
        {
            reviewed.TryGetValue(type, out var entry);
            foreach (var unit in findings.UnguardedWrites.Where(u => entry is null || !entry.Methods.Contains(u.Method)))
            {
                unreviewed.Add($"{type}.{unit.Method} (tables: {string.Join(",", findings.Tables)}; columns: {string.Join(",", findings.Columns)}; "
                               + $"writes: {string.Join(",", unit.Writes)})");
            }
        }

        Assert.True(
            unreviewed.Count == 0,
            "These methods name an invoice-family table and a core-record lookup column AND call a Dataverse write, yet no deciding member of "
            + "InvoiceFamilyCoreRecordGuard is called in them: " + string.Join("; ", unreviewed)
            + ". An invoice-family row names exactly one core record (FR-05): call the guard before the write, or - only when the method "
            + "meets the criteria in the class remarks - add it to Reviewed with a reason.");

        var stale = new List<string>();
        foreach (var (type, entry) in reviewed)
        {
            candidates.TryGetValue(type, out var findings);
            var live = findings?.UnguardedWrites.Select(u => u.Method).ToHashSet(StringComparer.Ordinal) ?? [];
            stale.AddRange(entry.Methods.Where(m => !live.Contains(m)).Select(m => $"{type}.{m}"));
        }

        Assert.True(
            stale.Count == 0,
            "Reviewed methods that no longer match the signal (gone, renamed, or now call the guard): " + string.Join(", ", stale.Order())
            + ". Remove them so the list only shrinks.");

        Assert.All(reviewed, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value.Reason), entry.Key + " needs a reason."));
    }

    [Fact(DisplayName = "DEF-064 (controls): the detector flags an unguarded writer in any shape and passes readers, guarded writers and unrelated writers")]
    public void Detector_MustFireAndMustNotFire()
    {
        var controls = new[]
        {
            typeof(InvoiceFamilyWriterControlUnguardedCreate),
            typeof(InvoiceFamilyWriterControlUnguardedAsyncUpdate),
            typeof(InvoiceFamilyWriterControlUnguardedWebApiPayload),
            typeof(InvoiceFamilyWriterControlMixedMethods),
            typeof(InvoiceFamilyWriterControlGuardedCreate),
            typeof(InvoiceFamilyWriterControlNonDecidingGuardMember),
            typeof(InvoiceFamilyWriterControlReader),
            typeof(InvoiceFamilyWriterControlOtherTableWriter),
            typeof(InvoiceFamilyWriterControlOtherColumnWriter),
            typeof(InvoiceFamilyWriterControlEntityNameOnly),
        }.SelectMany(IlCallScan.WithNested);

        var found = InvoiceFamilyWriterScan.Scan(controls);
        string Name(Type t) => t.FullName!;
        IReadOnlyList<string> Unguarded(Type t) => found[Name(t)].UnguardedWrites.Select(u => u.Method).ToList();

        // Must fire: a create on an Entity, an update inside an async method (a state machine, attributed to its method), a Web API payload
        // with only @odata.bind keys on an entity set, and a second unguarded method in a type whose first method is guarded.
        Assert.Equal(["Write"], Unguarded(typeof(InvoiceFamilyWriterControlUnguardedCreate)));
        Assert.Equal(["Refile"], Unguarded(typeof(InvoiceFamilyWriterControlUnguardedAsyncUpdate)));
        Assert.Equal(["Write"], Unguarded(typeof(InvoiceFamilyWriterControlUnguardedWebApiPayload)));
        Assert.Equal(["UnguardedWrite"], Unguarded(typeof(InvoiceFamilyWriterControlMixedMethods)));
        Assert.Contains("Check", found[Name(typeof(InvoiceFamilyWriterControlMixedMethods))].EnforcingCalls);

        // Must fire: a call of a guard member that decides nothing (a column list) does not satisfy the guard.
        Assert.Equal(["Write"], Unguarded(typeof(InvoiceFamilyWriterControlNonDecidingGuardMember)));
        Assert.Empty(found[Name(typeof(InvoiceFamilyWriterControlNonDecidingGuardMember))].EnforcingCalls);

        // Must be satisfied: the first control's shape plus a deciding call in the same method; it is a roster caller, not a candidate.
        Assert.False(found[Name(typeof(InvoiceFamilyWriterControlGuardedCreate))].IsCandidate);
        Assert.Contains("Check", found[Name(typeof(InvoiceFamilyWriterControlGuardedCreate))].EnforcingCalls);

        // Must not fire: a reader; a writer of a table outside the family; a writer of a family row that names no core lookup; a writer
        // whose only core-table name is the entity-name argument of a reference.
        foreach (var quiet in new[]
        {
            typeof(InvoiceFamilyWriterControlReader),
            typeof(InvoiceFamilyWriterControlOtherTableWriter),
            typeof(InvoiceFamilyWriterControlOtherColumnWriter),
            typeof(InvoiceFamilyWriterControlEntityNameOnly),
        })
        {
            Assert.False(found.TryGetValue(Name(quiet), out var hit) && hit.IsCandidate, quiet.Name + " must not be a candidate.");
        }
    }

    [Fact(DisplayName = "DEF-064: the guard's static members are all classified, and the signal's tables are the guard's own")]
    public void TheGuardsMembersAreClassified_AndTheSignalFollowsItsTableList()
    {
        var members = InvoiceFamilyWriterScan.Guard
            .GetMembers(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(m => m is MethodInfo or PropertyInfo or FieldInfo && !m.Name.StartsWith('<')
                        && m is not FieldInfo { IsLiteral: true } and not FieldInfo { IsPrivate: true } and not MethodInfo { IsPrivate: true })
            .Select(m => m.Name)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        var unclassified = members.Where(n => !InvoiceFamilyWriterScan.Enforcing.Contains(n) && !InvoiceFamilyWriterScan.NotEnforcing.Contains(n)).ToList();
        Assert.True(
            unclassified.Count == 0,
            "New InvoiceFamilyCoreRecordGuard members not classified in InvoiceFamilyWriterScan (Enforcing = decides a write; NotEnforcing = anything "
            + "else): " + string.Join(", ", unclassified));

        var gone = InvoiceFamilyWriterScan.Enforcing.Where(n => !members.Contains(n)).ToList();
        Assert.True(gone.Count == 0, "Enforcing members that no longer exist: " + string.Join(", ", gone));

        var keys = InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scanned = InvoiceFamilyWriterScan.Tables().Where(t => keys.Contains(t)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(keys.SetEquals(scanned), "The scan's tables must be exactly the guard's CoreRecordLookups keys (plus their entity-set plurals).");
    }
}

/// <summary>Control (must fire): builds an invoice row naming a matter under the attribute key and creates it.</summary>
internal static class InvoiceFamilyWriterControlUnguardedCreate
{
    internal static Task<Guid> Write(IGenericEntityService entities, Guid matterId)
    {
        var row = new Entity("sprk_invoice");
        row["sprk_matter"] = new EntityReference("sprk_matter", matterId);
        return entities.CreateAsync(row);
    }
}

/// <summary>Control (must fire): the write is inside an async method, so it is a state machine's body.</summary>
internal static class InvoiceFamilyWriterControlUnguardedAsyncUpdate
{
    internal static async Task Refile(IGenericEntityService entities, Guid lineId, Guid projectId)
    {
        await Task.Yield();
        await entities.UpdateAsync(
            "sprk_billingevent",
            lineId,
            new Dictionary<string, object> { ["sprk_project"] = new EntityReference("sprk_project", projectId) });
    }
}

/// <summary>Control (must fire): a Web API write through the per-user client, an entity set and only <c>@odata.bind</c> keys.</summary>
internal static class InvoiceFamilyWriterControlUnguardedWebApiPayload
{
    private const string EntitySet = "sprk_invoicesubmissions";

    internal static Task Write(IDataverseUserClient client, Guid id, string bind, CancellationToken ct)
    {
        var payload = new Dictionary<string, object> { ["sprk_RegardingMatter@odata.bind"] = bind, ["sprk_RegardingProject@odata.bind"] = bind };
        return client.PatchAsync(EntitySet + "(" + id + ")", System.Text.Json.JsonSerializer.Serialize(payload), ct);
    }
}

/// <summary>Control (must fire on the second method only): one method decides before it writes, the other writes with no decision.</summary>
internal static class InvoiceFamilyWriterControlMixedMethods
{
    internal static Task<Guid> GuardedWrite(IGenericEntityService entities, Guid matterId)
    {
        var row = new Entity("sprk_invoicesubmission");
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
        _ = InvoiceFamilyCoreRecordGuard.Check(row);
        return entities.CreateAsync(row);
    }

    internal static Task<Guid> UnguardedWrite(IGenericEntityService entities, Guid projectId)
    {
        var row = new Entity("sprk_invoiceapproval");
        row["sprk_regardingproject"] = new EntityReference("sprk_project", projectId);
        return entities.CreateAsync(row);
    }
}

/// <summary>Control (must be satisfied): a create with a deciding call of the guard in the same method.</summary>
internal static class InvoiceFamilyWriterControlGuardedCreate
{
    internal static Task<Guid> Write(IGenericEntityService entities, Guid matterId)
    {
        var row = new Entity("sprk_invoicesubmission");
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
        _ = InvoiceFamilyCoreRecordGuard.Check(row);
        return entities.CreateAsync(row);
    }
}

/// <summary>Control (must fire): names the guard's column list but never asks it to decide.</summary>
internal static class InvoiceFamilyWriterControlNonDecidingGuardMember
{
    internal static Task<Guid> Write(IGenericEntityService entities, Guid matterId)
    {
        _ = InvoiceFamilyCoreRecordGuard.ColumnsToRead("sprk_invoicecorrection");
        var row = new Entity("sprk_invoicecorrection");
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
        return entities.CreateAsync(row);
    }
}

/// <summary>Control (must not fire): names the table and the core columns and only reads.</summary>
internal static class InvoiceFamilyWriterControlReader
{
    internal static Task<Entity> Read(IGenericEntityService entities, Guid id) =>
        entities.RetrieveAsync("sprk_invoice", id, ["sprk_matter", "sprk_project", "sprk_regardingworkassignment"]);
}

/// <summary>Control (must not fire): writes a core lookup on a table outside the invoice family.</summary>
internal static class InvoiceFamilyWriterControlOtherTableWriter
{
    internal static Task<Guid> Write(IGenericEntityService entities, Guid matterId)
    {
        var row = new Entity("sprk_event");
        row["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterId);
        return entities.CreateAsync(row);
    }
}

/// <summary>Control (must not fire): writes a family row's status and names no core-record lookup.</summary>
internal static class InvoiceFamilyWriterControlOtherColumnWriter
{
    internal static Task Write(IGenericEntityService entities, Guid id) =>
        entities.UpdateAsync("sprk_invoice", id, new Dictionary<string, object> { ["statuscode"] = 2 });
}

/// <summary>Control (must not fire): a family row whose only mention of a core table is the entity-name argument of a reference.</summary>
internal static class InvoiceFamilyWriterControlEntityNameOnly
{
    internal static Task Write(IGenericEntityService entities, Guid id, Guid matterId) =>
        entities.UpdateAsync(
            "sprk_invoice",
            id,
            new Dictionary<string, object> { ["sprk_vendororg"] = new EntityReference("sprk_matter", matterId) });
}
