using System.Reflection;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 DEF-064 (#2643): every BFF type that WRITES an invoice-family row's core-record lookups calls
/// <see cref="InvoiceFamilyCoreRecordGuard"/>. A row of an invoice-family table names exactly one core record (matter, project or
/// work assignment); the guard is the one server-side owner of that rule (FR-05). Every known write path calls it, each pinned by its
/// own tests, but nothing pinned the COMPLETENESS of that set: a new path that writes those lookups on a family table without the guard
/// would pass review silently. This test is that pin.
/// </summary>
/// <remarks>
/// <para><b>The detection signal</b> (the compiled assembly, via <see cref="IlCallScan"/>, not the text): an outermost type is a
/// <b>candidate writer</b> when its IL does ALL of (1) loads the name of an invoice-family table, (2) loads the name of a core-record
/// lookup column of the family, and (3) calls a Dataverse WRITE method. The tables and columns are read from
/// <see cref="InvoiceFamilyCoreRecordGuard.CoreRecordLookups"/> itself, so the signal follows the guard's own table list; names are
/// compared case-insensitively (a Web API payload spells them <c>sprk_RegardingMatter</c>). A candidate must either call a member of the
/// guard or be on <see cref="Reviewed"/>.</para>
/// <para><b>Why that signal.</b> The compiler folds a <c>const</c>, a <c>nameof</c> and a constant concatenation into one <c>ldstr</c>, so
/// a payload built from named constants is seen. A pure reader (a query, a select list, a filter, a mapper) never reaches (3), so it does
/// not appear. The write methods are those of the Dataverse seams the BFF writes through: any method named <c>Create*</c>, <c>Update*</c>,
/// <c>Upsert*</c>, <c>Patch*</c>, <c>Post*</c>, <c>BulkUpdate*</c>, <c>Associate*</c> or <c>ExecuteTransaction*</c> declared in
/// <c>Spaarke.Dataverse</c>, <c>Microsoft.Xrm.Sdk</c> or <c>Microsoft.PowerPlatform</c>. A path that writes through
/// <c>OwnedChildWrite</c>, <c>RecordCreationService</c> or one of the generic update cores never calls those seams itself, so it is
/// outside the scan on purpose: it gets the guard from the core it calls, and those cores are themselves scanned.</para>
/// <para><b>What the signal cannot see</b> (stated, not hidden): a column name that is not a string constant in the writing type (read
/// at run time from <c>CoreRecordLookups</c>, <c>CoreAncestorResolver.CoreAncestorLookups</c> or a configuration value, or held in a
/// <c>static readonly</c> field of another type); a write through a seam outside the three namespaces above (a hand-rolled
/// <c>HttpClient</c> call); and a type that names a family table and a core column and writes a DIFFERENT column of that row. The last
/// one is the reason the list below exists: it is a reviewed list, not a proof. The list, the guard's own tests and the
/// reviewers of a new write path are the other layers.</para>
/// <para><b>Maintaining <see cref="Reviewed"/>.</b> A failure names the type and the tables, columns and write methods that matched.
/// First choice: call the guard (<see cref="InvoiceFamilyCoreRecordGuard.CheckWriteAsync"/>, <see cref="InvoiceFamilyCoreRecordGuard.Check(Entity)"/>
/// or the generic-update entry points) before the write, as <c>OwnedChildWrite</c> does. Only when the type provably writes none of the
/// family's core lookups (it names them to read, filter or map, and writes other columns or other tables), add it to
/// <see cref="Reviewed"/> with a one-line reason that says what it writes instead. A type on the list that no longer matches the
/// signal, or that now calls the guard, fails too, so the list only shrinks. Never add a type to silence the test without reading the
/// writes. <b>Controls</b>: <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over the <c>InvoiceFamilyWriterControl*</c>
/// fixtures below. Enforced by: this guard (ArchTests in CI and the Stop hook).</para>
/// </remarks>
public class InvoiceFamilyCoreLookupWriterGuardTests
{
    private static readonly Type Guard = typeof(InvoiceFamilyCoreRecordGuard);

    /// <summary>
    /// The reviewed list: types that name a family table and a core-record column and call a Dataverse write, and were read to confirm they
    /// write NONE of the family's core lookups (one line each). The closed set: it must equal the scan's unguarded candidates.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Reviewed = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Services.Access.ExternalTeamShareSynchronizer"] =
            "names the family tables to re-own rows; writes only ownerid (a team) with a row-version guard",
        ["Sprk.Bff.Api.Services.Access.SecureRootInheritance"] =
            "reads the lineage columns; writes only the access-permission / inheritance marker columns, and deletes a half-created row",
        ["Sprk.Bff.Api.Services.Ai.Nodes.TaskActionCore"] =
            "creates a to-do (sprk_event) with its own sprk_regarding{core} pair; the family tables appear only as regarding record types",
        ["Sprk.Bff.Api.Services.Communication.CommunicationEnrichmentService"] =
            "writes sprk_communication regarding fields; the family tables appear only as regarding record types",
        ["Sprk.Bff.Api.Services.Communication.EmailUploadCaptureService"] =
            "creates sprk_communication; the invoice appears only as a regarding record type",
        ["Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver"] =
            "the re-file owner step: writes ownerid and puts back the row's own previous values after a failed re-file; the re-file itself is OwnedChildWrite.RefileAsync, which calls the guard",
        ["Sprk.Bff.Api.Services.Finance.ApExportService"] =
            "creates the export batch row and writes sprk_invoice's batch lookup; reads the core columns for the export lines",
        ["Sprk.Bff.Api.Services.Finance.EligibleApproverResolver"] =
            "associates eligible approvers and stamps the resolved-on column of an approval; reads matter / project to pick the approver roles",
        ["Sprk.Bff.Api.Services.Finance.InvoiceCorrectionReevaluation"] =
            "writes a submission's findings and a line's status columns only; reads the core columns of the invoice",
        ["Sprk.Bff.Api.Services.Finance.Submission.SubmissionCommitGate"] =
            "writes a submission's status and failure columns, reopens a superseded invoice's status, and rolls back its own rows; reads the core columns of the submission",
        ["Sprk.Bff.Api.Services.Finance.SubmissionOverrideService"] =
            "writes a submission's status and an invoice's decision note and review detail in one transaction; reads the core columns of the submission",
        ["Sprk.Bff.Api.Services.Office.OfficeDocumentPersistence"] =
            "writes sprk_document and its association lookups; the invoice is a document's association target, not a family row",
        ["Sprk.Bff.Api.Services.Workspace.TodoGenerationService"] =
            "creates sprk_event / to-do rows with their own regarding pair; the invoice and matter are query targets and regarding record types",
    };

    [Fact(DisplayName = "DEF-064: every type that writes an invoice-family row's core-record lookups calls InvoiceFamilyCoreRecordGuard, or is on the reviewed list")]
    public void EveryFamilyCoreLookupWriter_CallsTheGuard_OrIsReviewed()
    {
        var unguarded = Candidates(typeof(Program).Assembly.GetTypes())
            .Where(c => !c.CallsGuard)
            .ToDictionary(c => c.Type, StringComparer.Ordinal);

        var missing = unguarded.Keys.Except(Reviewed.Keys, StringComparer.Ordinal).OrderBy(n => n).ToList();
        Assert.True(
            missing.Count == 0,
            "These types name an invoice-family table and a core-record lookup column AND call a Dataverse write, yet never call "
            + "InvoiceFamilyCoreRecordGuard: " + string.Join("; ", missing.Select(n => unguarded[n].Describe()))
            + ". An invoice-family row names exactly one core record (FR-05): call the guard before the write, or - only when the type "
            + "provably writes none of those lookups - add it to Reviewed with a one-line reason (see the class remarks).");

        var stale = Reviewed.Keys.Except(unguarded.Keys, StringComparer.Ordinal).OrderBy(n => n).ToList();
        Assert.True(
            stale.Count == 0,
            "These types are on the reviewed list but no longer match the signal (or now call the guard): " + string.Join(", ", stale)
            + ". Remove them so the list only shrinks.");

        Assert.All(Reviewed, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Value), entry.Key + " needs a reason."));
    }

    [Fact(DisplayName = "DEF-064 (controls): the detector flags an unguarded writer in any shape and passes readers, guarded writers and unrelated writers")]
    public void Detector_MustFireAndMustNotFire()
    {
        var controls = new[]
        {
            typeof(InvoiceFamilyWriterControlUnguardedCreate),
            typeof(InvoiceFamilyWriterControlUnguardedAsyncUpdate),
            typeof(InvoiceFamilyWriterControlUnguardedWebApiPayload),
            typeof(InvoiceFamilyWriterControlGuardedCreate),
            typeof(InvoiceFamilyWriterControlReader),
            typeof(InvoiceFamilyWriterControlOtherTableWriter),
            typeof(InvoiceFamilyWriterControlOtherColumnWriter),
        }.SelectMany(IlCallScan.WithNested);

        var found = Candidates(controls).ToDictionary(c => c.Type, StringComparer.Ordinal);

        // Must fire, unguarded: a create on an Entity (a plain method), an update from inside an async method (a state machine, attributed
        // to its outermost type), and a Web API payload spelled in mixed case through the Web API client.
        foreach (var fires in new[]
        {
            typeof(InvoiceFamilyWriterControlUnguardedCreate),
            typeof(InvoiceFamilyWriterControlUnguardedAsyncUpdate),
            typeof(InvoiceFamilyWriterControlUnguardedWebApiPayload),
        })
        {
            Assert.True(found.TryGetValue(fires.FullName!, out var hit), fires.Name + " should be a candidate.");
            Assert.False(hit!.CallsGuard, fires.Name + " never calls the guard.");
        }

        // Must fire but be satisfied: the same shape as the first control, plus a call of the guard.
        Assert.True(found.TryGetValue(typeof(InvoiceFamilyWriterControlGuardedCreate).FullName!, out var guarded));
        Assert.True(guarded!.CallsGuard);

        // Must not fire: a reader (names the table and the columns, calls no write); a writer of a table outside the family; a writer of
        // a family table that names no core lookup.
        Assert.DoesNotContain(typeof(InvoiceFamilyWriterControlReader).FullName!, found.Keys);
        Assert.DoesNotContain(typeof(InvoiceFamilyWriterControlOtherTableWriter).FullName!, found.Keys);
        Assert.DoesNotContain(typeof(InvoiceFamilyWriterControlOtherColumnWriter).FullName!, found.Keys);
        Assert.Equal(4, found.Count);
    }

    [Fact(DisplayName = "DEF-064: the signal's tables and columns come from the guard's own list")]
    public void TheSignalFollowsTheGuardsOwnTableAndColumnList()
    {
        Assert.Equal(6, Tables().Count);
        foreach (var column in new[] { "sprk_matter", "sprk_project", "sprk_regardingmatter", "sprk_regardingproject", "sprk_regardingworkassignment" })
        {
            Assert.Contains(column, Columns());
        }
    }

    private static HashSet<string> Tables() =>
        new(InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Keys, StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> Columns() =>
        new(InvoiceFamilyCoreRecordGuard.CoreRecordLookups.Values.SelectMany(l => l).Select(l => l.Column), StringComparer.OrdinalIgnoreCase);

    private static readonly string[] WritePrefixes =
        ["Create", "Update", "Upsert", "Patch", "Post", "BulkUpdate", "Associate", "ExecuteTransaction"];

    private static readonly string[] WriteNamespaces = ["Spaarke.Dataverse", "Microsoft.Xrm.Sdk", "Microsoft.PowerPlatform"];

    private static bool IsDataverseWrite(MethodBase target) =>
        target.DeclaringType is { } declaring
        && WriteNamespaces.Any(ns => (declaring.Namespace ?? string.Empty).StartsWith(ns, StringComparison.Ordinal))
        && WritePrefixes.Any(p => target.Name.StartsWith(p, StringComparison.Ordinal));

    private sealed record Candidate(
        string Type, bool CallsGuard, IReadOnlyCollection<string> Tables, IReadOnlyCollection<string> Columns, IReadOnlyCollection<string> Writes)
    {
        public string Describe() =>
            $"{Type} (tables: {string.Join(",", Tables)}; columns: {string.Join(",", Columns)}; writes: {string.Join(",", Writes)})";
    }

    /// <summary>
    /// The outermost types in <paramref name="types"/> that load a family table name, load a core-lookup column name and call a Dataverse
    /// write, each with whether it also calls a member of the guard. The guard itself is the owner and is never a candidate.
    /// </summary>
    private static IReadOnlyList<Candidate> Candidates(IEnumerable<Type> types)
    {
        var list = types.Where(t => IlCallScan.Outermost(t) != Guard).ToList();
        var tables = Tables();
        var columns = Columns();

        var tableHits = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var columnHits = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (caller, _, value) in IlCallScan.StringLoads(list))
        {
            var owner = IlCallScan.Outermost(caller).FullName!;
            if (tables.Contains(value))
                Add(tableHits, owner, value.ToLowerInvariant());
            if (columns.Contains(value))
                Add(columnHits, owner, value.ToLowerInvariant());
        }

        var writeHits = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var guardCallers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (caller, _, target) in IlCallScan.MethodReferences(list))
        {
            var owner = IlCallScan.Outermost(caller).FullName!;
            if (target.DeclaringType is { } declaring && IlCallScan.Outermost(declaring) == Guard)
                guardCallers.Add(owner);
            if (IsDataverseWrite(target))
                Add(writeHits, owner, target.DeclaringType!.Name + "." + target.Name);
        }

        return tableHits.Keys
            .Where(t => columnHits.ContainsKey(t) && writeHits.ContainsKey(t))
            .Select(t => new Candidate(
                t, guardCallers.Contains(t), tableHits[t].Order().ToList(), columnHits[t].Order().ToList(), writeHits[t].Order().ToList()))
            .OrderBy(c => c.Type, StringComparer.Ordinal)
            .ToList();

        static void Add(Dictionary<string, HashSet<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out var set))
                map[key] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(value);
        }
    }
}

/// <summary>Control (must fire, unguarded): builds an invoice row naming a matter and creates it.</summary>
internal static class InvoiceFamilyWriterControlUnguardedCreate
{
    internal static Task<Guid> Write(IGenericEntityService entities, Guid matterId)
    {
        var row = new Entity("sprk_invoice");
        row["sprk_matter"] = new EntityReference("sprk_matter", matterId);
        return entities.CreateAsync(row);
    }
}

/// <summary>Control (must fire, unguarded): the write is inside an async method, so it is a state machine's body.</summary>
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

/// <summary>Control (must fire, unguarded): a Web API payload through the Web API client, the column spelled in mixed case.</summary>
internal static class InvoiceFamilyWriterControlUnguardedWebApiPayload
{
    internal static Task Write(DataverseWebApiClient client, Guid id, string bind) =>
        client.UpdateAsync(
            "sprk_invoiceapprovals",
            id,
            new Dictionary<string, object> { ["sprk_invoiceapproval"] = "x", ["sprk_RegardingMatter@odata.bind"] = bind, ["sprk_RegardingProject"] = bind });
}

/// <summary>Control (must fire, satisfied): the first control's shape plus a call of the guard.</summary>
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
