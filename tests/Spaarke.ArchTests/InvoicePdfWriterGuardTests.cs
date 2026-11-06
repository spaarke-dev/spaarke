using System.Reflection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Files;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 094 (uac-r2 request, 2026-10-28): a Stored File with purpose <c>InvoicePdf</c> is created ONLY in the commit that
/// creates its invoice. Task 098's external invoice predicate reads "an InvoicePdf with no source" as the upload's own PDF, so a route
/// that wrote one onto an existing invoice (task 123's supporting-file upload included, which writes <c>InvoiceSupportingFile</c> only)
/// would make an internal invoice whose source is unknowable read as an upload. This turns "no route writes one onto an existing
/// invoice" into a build check.
/// </summary>
/// <remarks>
/// <para><b>The signal (IL).</b> A Stored File WRITER is a source method (a lambda or async state machine counts for the method it was
/// written in) that loads the table name <c>sprk_storedfile</c> (the <see cref="StoredFileSchema.Entity"/> constant is inlined) AND
/// references a write primitive: the <see cref="Entity"/> constructor, a <see cref="CreateRequest"/>, <see cref="UpdateRequest"/> or
/// <see cref="UpsertRequest"/>, or a create, update or upsert of <see cref="IGenericEntityService"/>.</para>
/// <para><b>The rules.</b> (1) Every writer is in <see cref="ApprovedWriters"/>: the ONE row constructor
/// (<see cref="StoredFileService.BuildRow"/>, which sets purpose and parent) and two status-only updates (so no update writes a
/// purpose or re-points a parent: nothing is attached onto an existing invoice). (2) <see cref="StoredFileService.BuildRow"/> is
/// called only by <see cref="ApprovedRowCallers"/>: the persister's commit, whose transaction creates the invoice with the row, and
/// the generic create, which refuses an InvoicePdf before any read or write (must-fire:
/// <c>StoredFileServiceTests.InvoicePdf_OutsideTheCommit_IsRefused_NothingWritten</c>).</para>
/// <para><b>Limits.</b> A table name built at run time, a raw Web API write and a write through reflection are not seen. The
/// persister's commit placing the row in the invoice's transaction is proved by its unit tests (InvoiceSubmissionPersisterTests),
/// not here.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same rules over <see cref="InvoicePdfGuardControl"/>:
/// an InvoicePdf row built outside the commit, a <c>BuildRow</c> call from another member and an update that attaches a row onto an
/// invoice fire; a read and a write of another table do not.</para>
/// </remarks>
public class InvoicePdfWriterGuardTests
{
    private const string Service = "Sprk.Bff.Api.Services.Finance.Files.StoredFileService";

    /// <summary>The members that may write a Stored File row, each with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> ApprovedWriters = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [$"{Service}::BuildRow"] =
            "The ONE Stored File row constructor (a create: purpose, parent, source, SHA, owner, Not yet servable). Its callers are pinned.",
        [$"{Service}::FlipToServableAsync"] = "Status only (Servable), conditioned on the row version: never a purpose or a parent.",
        [$"{Service}::WithdrawForUndoAsync"] = "Status only (Removed; the submission undo), conditioned on the row version.",
        ["Sprk.Bff.Api.Services.Finance.Submission.SubmissionCommitGate::CompensateAsync"] =
            "Seen for its DeleteRequests of the invoice's Stored File rows (the undo) next to its update of the SUBMISSION row; it " +
            "never creates or updates a Stored File.",
    };

    /// <summary>The members that may call <see cref="StoredFileService.BuildRow"/>, each with the reason.</summary>
    private static readonly IReadOnlyDictionary<string, string> ApprovedRowCallers = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Sprk.Bff.Api.Services.Finance.Submission.InvoiceSubmissionPersister::PersistCoreAsync"] =
            "The commit (model 9.1 step 4): the invoice PDF's row (an upload's, a source copy's, a from-email part's) joins the ONE " +
            "transaction that creates its invoice.",
        [$"{Service}::StageCreateFlipAsync"] =
            "The generic create (UploadForCallerAsync, CreateForAppAsync): refuses StoredFilePurpose.InvoicePdf before any read or write.",
    };

    [Fact(DisplayName = "Task 094: an InvoicePdf Stored File is created only in the commit that creates its invoice; no other member writes a Stored File row's purpose or parent")]
    public void AnInvoicePdfIsCreatedOnlyWithItsInvoice()
    {
        var types = typeof(Program).Assembly.GetTypes();
        var writers = Writers(types);
        var callers = RowCallers(types);

        // Non-vacuity: every approved member is seen.
        foreach (var writer in ApprovedWriters.Keys)
            Assert.True(writers.Contains(writer), $"{writer} should be seen writing sprk_storedfile: the scan lost its signal.");
        foreach (var caller in ApprovedRowCallers.Keys)
            Assert.True(callers.Contains(caller), $"{caller} should be seen calling StoredFileService.BuildRow: the scan lost its signal.");

        var problems = Judge(writers, callers, ApprovedWriters, ApprovedRowCallers);
        Assert.True(problems.Count == 0,
            "A Stored File with purpose InvoicePdf is created only in the commit that creates its invoice (task 094; task 098 reads a " +
            "sourceless InvoicePdf as the upload's own PDF):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "Task 094 InvoicePdf guard control: a row built outside the commit, a BuildRow call from another member and an update attaching a row onto an invoice fire; a read and another table's write do not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var control = new[] { typeof(InvoicePdfGuardControl) };
        var writers = Writers(control);
        var callers = RowCallers(control);
        var name = typeof(InvoicePdfGuardControl).FullName;

        Assert.Equal(
            new[] { $"{name}::AttachesOntoAnInvoiceAsync", $"{name}::CreatesAnInvoicePdfOutsideTheCommit" },
            writers.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { $"{name}::CallsBuildRow" }, callers.ToArray());

        var problems = Judge(writers, callers, ApprovedWriters, ApprovedRowCallers);
        Assert.Contains(problems, p => p.StartsWith("NEW Stored File writer", StringComparison.Ordinal) && p.Contains("CreatesAnInvoicePdfOutsideTheCommit"));
        Assert.Contains(problems, p => p.StartsWith("NEW Stored File writer", StringComparison.Ordinal) && p.Contains("AttachesOntoAnInvoiceAsync"));
        Assert.Contains(problems, p => p.StartsWith("NEW BuildRow caller", StringComparison.Ordinal) && p.Contains("CallsBuildRow"));
        Assert.Equal(3, problems.Count);
        Assert.DoesNotContain(problems, p => p.Contains("ReadsStoredFiles") || p.Contains("CreatesAnInvoice:"));

        // Must not fire when the same members are listed.
        var listed = Judge(writers, callers,
            writers.ToDictionary(w => w, _ => "listed"), callers.ToDictionary(c => c, _ => "listed"));
        Assert.Empty(listed);
    }

    /// <summary>Every source method that loads <c>sprk_storedfile</c> and references a write primitive.</summary>
    private static IReadOnlySet<string> Writers(IEnumerable<Type> types)
    {
        var all = types.SelectMany(IlCallScan.WithNested).Distinct().ToList();
        var loads = IlCallScan.StringLoads(all)
            .Where(s => string.Equals(s.Value, StoredFileSchema.Entity, StringComparison.Ordinal))
            .Select(s => ExternalDocumentNarrowingGuardTests.SourceKey(s.Caller, s.CallerMethod))
            .ToHashSet(StringComparer.Ordinal);
        return IlCallScan.MethodReferences(all)
            .Where(r => IsWritePrimitive(r.Target))
            .Select(r => ExternalDocumentNarrowingGuardTests.SourceKey(r.Caller, r.CallerMethod))
            .Where(loads.Contains)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Every source method that references <see cref="StoredFileService.BuildRow"/> (outside its own type's declaration).</summary>
    private static IReadOnlySet<string> RowCallers(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types.SelectMany(IlCallScan.WithNested).Distinct())
            .Where(r => r.Target.DeclaringType == typeof(StoredFileService) && r.Target.Name == nameof(StoredFileService.BuildRow))
            .Select(r => ExternalDocumentNarrowingGuardTests.SourceKey(r.Caller, r.CallerMethod))
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsWritePrimitive(MethodBase target) => target switch
    {
        ConstructorInfo ctor => ctor.DeclaringType == typeof(Entity) || ctor.DeclaringType == typeof(CreateRequest)
            || ctor.DeclaringType == typeof(UpdateRequest) || ctor.DeclaringType == typeof(UpsertRequest),
        _ => target.DeclaringType is { } declaring && typeof(IGenericEntityService).IsAssignableFrom(declaring)
            && (target.Name.StartsWith("Create", StringComparison.Ordinal) || target.Name.StartsWith("Update", StringComparison.Ordinal)
                || target.Name.StartsWith("Upsert", StringComparison.Ordinal)),
    };

    private static List<string> Judge(
        IReadOnlySet<string> writers, IReadOnlySet<string> callers,
        IReadOnlyDictionary<string, string> approvedWriters, IReadOnlyDictionary<string, string> approvedCallers) =>
        writers.Where(w => !approvedWriters.ContainsKey(w)).Order(StringComparer.Ordinal)
            .Select(w => $"NEW Stored File writer {w}: only BuildRow creates a row (an InvoicePdf only in the invoice's commit); an update writes status only")
            .Concat(callers.Where(c => !approvedCallers.ContainsKey(c)).Order(StringComparer.Ordinal)
                .Select(c => $"NEW BuildRow caller {c}: a Stored File row is created only in the persister's commit or the generic create that refuses InvoicePdf"))
            .ToList();
}

/// <summary>The control fixture of <see cref="InvoicePdfWriterGuardTests"/> (never run; its IL is scanned).</summary>
internal static class InvoicePdfGuardControl
{
    // Must fire: an InvoicePdf row built outside the commit.
    internal static CreateRequest CreatesAnInvoicePdfOutsideTheCommit(Guid invoiceId) => new()
    {
        Target = new Entity("sprk_storedfile", Guid.NewGuid())
        {
            ["sprk_purpose"] = new OptionSetValue((int)StoredFilePurpose.InvoicePdf),
            ["sprk_invoice"] = new EntityReference("sprk_invoice", invoiceId),
        },
    };

    // Must fire: the row constructor called from a member that is not the commit.
    internal static CreateRequest CallsBuildRow(StoredFileService service, StoredFileRowSpec spec, StagedBlob blob, RecordOwnerResolution owner) =>
        service.BuildRow(spec, blob, owner, null);

    // Must fire: an update that attaches an existing row onto an invoice.
    internal static Task AttachesOntoAnInvoiceAsync(IGenericEntityService entities, Guid fileId, Guid invoiceId) =>
        entities.UpdateAsync("sprk_storedfile", fileId,
            new Dictionary<string, object> { ["sprk_invoice"] = new EntityReference("sprk_invoice", invoiceId) }, CancellationToken.None);

    // Must not fire: a read.
    internal static QueryExpression ReadsStoredFiles() => new("sprk_storedfile") { ColumnSet = new ColumnSet("sprk_purpose") };

    // Must not fire: a write of another table.
    internal static Entity CreatesAnInvoice() => new("sprk_invoice", Guid.NewGuid());
}
