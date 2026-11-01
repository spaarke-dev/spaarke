using System.Reflection;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 (task 097 second review F2, owner decision 2026-10-30): <c>sprk_invoicesubmission.sprk_resubmissionof</c>, the earlier
/// submission a Correction replaces, is field-secured so that only the server sets it: the BFF application user ("Spaarke BFF-Managed
/// Field Writers", read / create / update) writes it, and every other user only reads it ("Spaarke BFF-Managed Field Readers", read).
/// Every BFF write of the column must therefore go through the BFF's application identity (<see cref="IGenericEntityService"/>, directly or
/// through <see cref="ITrackingNumberAllocator"/>, which creates the submission row through it). A write made as the caller would be
/// refused natively once the column is secured, and the Correction intake would fail for that caller.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Over the compiled IL (<see cref="IlCallScan"/>), as <see cref="FindingsJsonAppOnlyReadGuardTests"/>: (C1) the set of
/// types that load a string naming the column is exactly the reviewed set below, each with its identity recorded in
/// <c>projects/ebilling-r1/notes/097d-supersede-signals.md</c> ("Field security for sprk_resubmissionof"), so a new writer or reader fails
/// until it is reviewed and listed; (C2) no reviewed type (closures and state machines included) calls a caller-identity Dataverse seam
/// (the same definition as the findings guard: <see cref="FindingsJsonAppOnlyReadGuardTests.CallerIdentityReason"/>); (C3) every reviewed
/// type that reaches Dataverse does so through <see cref="IGenericEntityService"/> or <see cref="ITrackingNumberAllocator"/>, and the
/// data-only type reaches no Dataverse client at all; (C4) every implementation of <see cref="ITrackingNumberAllocator"/> (the seam the two
/// writers hand the row to, so the writers themselves never name a client) reaches Dataverse through <see cref="IGenericEntityService"/>
/// and calls no caller-identity seam. The column name is a <c>const</c> (<see cref="InvoiceSubmissionService.ResubmissionOfColumn"/>), so
/// every use compiles to an <c>ldstr</c> in the type that uses it, interpolated Web API paths and <c>@odata.bind</c> keys included.</para>
/// <para><b>Enforced by this test</b> (CLAUDE.md §16 ladder): no type can say "this write is app-only", and the findings guard reads one
/// other column, so the rule is an ArchTest with must-fire and must-not-fire controls.</para>
/// <para><b>Not covered</b> (code-review concerns, and the live script's (p4) for the maker-configuration half): C2 is not transitive; a
/// writer that builds the column name at run time; a generic writer that copies caller-supplied column names into a row (the field-mapping
/// framework, the AI record-update tools: the script's (p4) refuses a mapping row that targets the column, and a caller-side write of any
/// kind is refused by the platform once the column is secured); a caller-identity seam added later under a name the C2 rules do not match.</para>
/// </remarks>
public sealed class ResubmissionOfAppOnlyWriteGuardTests
{
    private const string Column = InvoiceSubmissionService.ResubmissionOfColumn;

    /// <summary>The reviewed types that name the column, each app-only (identity in the 097d notes).</summary>
    private static readonly Type[] ReviewedTypes =
    [
        typeof(InvoiceSubmissionService), // WRITER (upload and from-document intake): sets it on the create via ITrackingNumberAllocator; reads it for a retry's match
        typeof(SubmitInvoiceFromEmailOperation), // WRITER (from-email intake, 097b): sets it on the create via ITrackingNumberAllocator
        typeof(SubmissionSnapshot), // reader: the column list of the submission read for the evaluation (IGenericEntityService)
        typeof(SubmissionCommitGate), // reader: the commit's re-read of the Correction target (IGenericEntityService)
        typeof(InvoiceSubmissionAfterSteps), // reader: the Signals close's re-read of the Correction target (IGenericEntityService)
        typeof(Sprk.Bff.Api.Api.Finance.InvoiceSubmissionFromEmailHttp), // reader (097e prefill): finds the replacing submission by the column (IGenericEntityService)
        typeof(SecureChildLineage), // data only: the lineage table lists the lookup; it reaches no Dataverse client
    ];

    /// <summary>The reviewed types that hold a table and no client: they must reach no Dataverse client at all.</summary>
    private static readonly Type[] DataOnlyTypes = [typeof(SecureChildLineage)];

    /// <summary>The assemblies whose types may reach Dataverse: the BFF and its data layer.</summary>
    private static readonly Assembly[] Scanned = [typeof(InvoiceSubmissionService).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "097 F2 C1: only the reviewed types name sprk_resubmissionof (a new writer must be reviewed for its identity)")]
    public void OnlyTheReviewedTypesNameTheColumn()
    {
        var naming = TypesNamingTheColumn(Scanned.SelectMany(a => a.GetTypes()));

        var unreviewed = naming.Except(ReviewedTypes).Select(t => t.FullName).ToList();
        var stale = ReviewedTypes.Except(naming).Select(t => t.FullName).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These types name {Column} but are not in the reviewed list: {string.Join(", ", unreviewed)}. The column is field-secured so that "
            + "only the BFF application user writes it, so a write as the caller is refused. Write it through IGenericEntityService (app-only), "
            + "record the writer and its identity in projects/ebilling-r1/notes/097d-supersede-signals.md, then add it to ReviewedTypes.");
        Assert.True(stale.Count == 0, $"These reviewed types no longer name {Column}; remove them from ReviewedTypes: {string.Join(", ", stale)}");
    }

    [Fact(DisplayName = "097 F2 C2: no reviewed type that names sprk_resubmissionof calls a caller-identity Dataverse seam")]
    public void TheReviewedTypesCallNoCallerIdentitySeam()
    {
        var violations = CallerIdentityCalls(ReviewedTypes).ToList();

        Assert.True(violations.Count == 0,
            $"A type that names {Column} calls a caller-identity Dataverse seam. The column is field-secured so that only the BFF application "
            + "user writes it, so a write as the caller is refused (and a caller read goes through the reader profile, not this seam). "
            + "Use IGenericEntityService: " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "097 F2 C3: every reviewed type reaches Dataverse through the app-only seams, and the data-only type through none")]
    public void TheReviewedTypesUseTheAppOnlyClient()
    {
        foreach (var type in ReviewedTypes.Except(DataOnlyTypes))
        {
            Assert.True(UsesAppOnlySeam(type),
                $"{type.FullName} names {Column} but references no IGenericEntityService or ITrackingNumberAllocator member: the scan cannot see its identity");
        }

        foreach (var type in DataOnlyTypes)
        {
            Assert.False(UsesAnyDataverseClient(type), $"{type.FullName} is listed as data only but references a Dataverse client; review it as a reader or writer");
        }
    }

    [Fact(DisplayName = "097 F2 C4: every ITrackingNumberAllocator (the create the intake hands the row to) is app-only")]
    public void EveryAllocatorIsAppOnly()
    {
        var allocators = AllocatorImplementations(Scanned.SelectMany(a => a.GetTypes())).ToList();

        Assert.Contains(typeof(TrackingNumberAllocator), allocators);
        foreach (var allocator in allocators)
        {
            Assert.True(AllocatorViolations(allocator).Count == 0,
                $"{allocator.FullName} implements ITrackingNumberAllocator, which creates the submission row carrying {Column}, but it "
                + "does not reach Dataverse only through IGenericEntityService: " + string.Join("; ", AllocatorViolations(allocator)));
        }
    }

    // ── Controls ──

    [Fact(DisplayName = "097 F2 controls: C1 fires on a type that names the column and is not reviewed, and not on one that does not")]
    public void C1_Controls()
    {
        var naming = TypesNamingTheColumn(IlCallScan.WithNested(typeof(ResubmissionOfWriteControlFixtures)));

        Assert.Contains(typeof(ResubmissionOfWriteControlFixtures), naming);
        Assert.Empty(TypesNamingTheColumn(IlCallScan.WithNested(typeof(ResubmissionOfUnrelatedControlFixture))));
    }

    [Theory(DisplayName = "097 F2 controls: C2 fires on a write made as the caller (OBO client, impersonating overload)")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.CallerClientWriter), "IDataverseUserClient")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.ImpersonatingOverloadWriter), "impersonat")]
    public void C2_FiresOnEachCallerShape(Type planted, string expected)
    {
        var violations = CallerIdentityCalls([planted]).ToList();

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.False(UsesAppOnlySeam(planted));
    }

    [Theory(DisplayName = "097 F2 controls: C2 does not fire on an app-only write, and C3 sees it")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.AppOnlyWriter))]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.AllocatorWriter))]
    public void C2_DoesNotFireOnAnAppOnlyWrite(Type planted)
    {
        Assert.Empty(CallerIdentityCalls([planted]));
        Assert.True(UsesAppOnlySeam(planted));
    }

    [Fact(DisplayName = "097 F2 controls: C3 flags a data-only type that reaches a client, and not one that reaches none")]
    public void C3_DataOnlyControls()
    {
        Assert.True(UsesAnyDataverseClient(typeof(ResubmissionOfWriteControlFixtures.AppOnlyWriter)));
        Assert.False(UsesAnyDataverseClient(typeof(ResubmissionOfWriteControlFixtures.DataOnly)));
    }

    [Fact(DisplayName = "097 F2 controls: C4 fires on an allocator that creates the row as the caller, and not on the real one")]
    public void C4_Controls()
    {
        var planted = typeof(ResubmissionOfWriteControlFixtures.CallerBackedAllocator);

        Assert.Contains(planted, AllocatorImplementations([planted]));
        Assert.NotEmpty(AllocatorViolations(planted));
        Assert.Empty(AllocatorViolations(typeof(TrackingNumberAllocator)));
        Assert.Empty(AllocatorImplementations([typeof(ResubmissionOfWriteControlFixtures.AppOnlyWriter)]));
    }

    // ── The scan ──

    private static HashSet<Type> TypesNamingTheColumn(IEnumerable<Type> types) =>
        IlCallScan.StringLoads(types)
            .Where(s => s.Value.Contains(Column, StringComparison.OrdinalIgnoreCase))
            .Select(s => IlCallScan.Outermost(s.Caller))
            .ToHashSet();

    /// <summary>
    /// Methods whose <c>callerSystemUserId</c> parameter the findings guard's rule reads as an impersonating (MSCRMCallerID) overload but is
    /// a VALUE: the from-email intake's checks (<c>InvoiceSubmissionService.DecideForFromEmailAsync</c>) stamp the caller's systemuser as the
    /// new row's creator person (OP-5) and read app-only through <see cref="IGenericEntityService"/>; nothing there is sent as the caller.
    /// Exact methods only: the method body belongs to a reviewed type, so an impersonating call added inside it is still seen by C2.
    /// </summary>
    private static readonly (Type Declaring, string Method)[] CallerIdValueCarriers =
        [(typeof(InvoiceSubmissionService), nameof(InvoiceSubmissionService.DecideForFromEmailAsync))];

    private static IEnumerable<string> CallerIdentityCalls(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types.SelectMany(IlCallScan.WithNested))
            .Where(r => !CallerIdValueCarriers.Any(c => r.Target.DeclaringType == c.Declaring && r.Target.Name == c.Method))
            .Select(r => (r.Caller, r.CallerMethod, r.Target, Why: FindingsJsonAppOnlyReadGuardTests.CallerIdentityReason(r.Target)))
            .Where(r => r.Why is not null)
            .Select(r => $"{IlCallScan.Outermost(r.Caller).Name}.{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)} -> {IlCallScan.Describe(r.Target)} ({r.Why})")
            .Distinct();

    private static bool UsesAppOnlySeam(Type type) =>
        IlCallScan.MethodReferences(IlCallScan.WithNested(type))
            .Any(r => r.Target.DeclaringType is { } d
                && (typeof(IGenericEntityService).IsAssignableFrom(d) || typeof(ITrackingNumberAllocator).IsAssignableFrom(d)));

    private static bool UsesAnyDataverseClient(Type type) =>
        IlCallScan.MethodReferences(IlCallScan.WithNested(type))
            .Any(r => r.Target.DeclaringType is { } d
                && (typeof(IGenericEntityService).IsAssignableFrom(d) || typeof(ITrackingNumberAllocator).IsAssignableFrom(d)
                    || typeof(IDataverseUserClient).IsAssignableFrom(d)));

    private static IEnumerable<Type> AllocatorImplementations(IEnumerable<Type> types) =>
        types.Where(t => t.IsClass && !t.IsAbstract && typeof(ITrackingNumberAllocator).IsAssignableFrom(t));

    /// <summary>An allocator must create through <see cref="IGenericEntityService"/> and call no caller-identity seam.</summary>
    private static List<string> AllocatorViolations(Type allocator)
    {
        var violations = CallerIdentityCalls([allocator]).ToList();
        if (!IlCallScan.MethodReferences(IlCallScan.WithNested(allocator))
                .Any(r => r.Target.DeclaringType is { } d && typeof(IGenericEntityService).IsAssignableFrom(d)))
        {
            violations.Add($"{allocator.Name} references no IGenericEntityService member");
        }

        return violations;
    }
}

/// <summary>Must-fire and must-not-fire controls for <see cref="ResubmissionOfAppOnlyWriteGuardTests"/>. Never run; only their IL is read.</summary>
internal static class ResubmissionOfWriteControlFixtures
{
    internal sealed class CallerClientWriter(IDataverseUserClient user)
    {
        public Task<DataverseUserResponse> WriteAsync(Guid id, Guid earlier, CancellationToken ct) =>
            user.PatchAsync($"sprk_invoicesubmissions({id:D})", $"{{\"sprk_resubmissionof@odata.bind\":\"/sprk_invoicesubmissions({earlier:D})\"}}", ct);
    }

    internal sealed class ImpersonatingOverloadWriter(IFieldMappingDataverseService fields)
    {
        public Task WriteAsync(Guid id, Guid earlier, Guid caller) =>
            fields.UpdateRecordFieldsAsync("sprk_invoicesubmission", id,
                new Dictionary<string, object?> { ["sprk_resubmissionof"] = new EntityReference("sprk_invoicesubmission", earlier) }, impersonateSystemUserId: caller);
    }

    internal sealed class AppOnlyWriter(IGenericEntityService entities)
    {
        public Task<Guid> CreateAsync(Guid earlier, CancellationToken ct)
        {
            var row = new Entity("sprk_invoicesubmission");
            row["sprk_resubmissionof"] = new EntityReference("sprk_invoicesubmission", earlier);
            return entities.CreateAsync(row, ct);
        }
    }

    internal sealed class AllocatorWriter(ITrackingNumberAllocator allocator)
    {
        public Task<TrackingNumberResult> CreateAsync(Guid id, Guid firm, Guid earlier, CancellationToken ct) =>
            allocator.CreateSubmissionAsync(id, firm, "INV-1",
                new Dictionary<string, object> { ["sprk_resubmissionof"] = new EntityReference("sprk_invoicesubmission", earlier) }, ct);
    }

    /// <summary>An allocator that creates the row as the caller: the Correction target it carries would be refused.</summary>
    internal sealed class CallerBackedAllocator(IDataverseUserClient user) : ITrackingNumberAllocator
    {
        public async Task<TrackingNumberResult> CreateSubmissionAsync(
            Guid submissionId, Guid firmId, string firmInvoiceNumber, IReadOnlyDictionary<string, object>? fields = null, CancellationToken ct = default)
        {
            await user.PostAsync("sprk_invoicesubmissions", "{}", ct);
            return TrackingNumberResult.Refused("control");
        }
    }

    /// <summary>A table and no client: the shape of <c>SecureChildLineage</c>. Also names the column (C1's must-fire).</summary>
    internal sealed class DataOnly
    {
        public static string[] Lookups() => ["sprk_resubmissionof"];
    }

    /// <summary>Names the column, so C1 sees this type.</summary>
    public static string Name() => "sprk_resubmissionof";
}

/// <summary>Must-not-fire control for C1: names another column only.</summary>
internal static class ResubmissionOfUnrelatedControlFixture
{
    public static string Column() => "sprk_supersedesinvoice";
}
