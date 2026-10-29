using System.Reflection;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 122 (#2235, ADR-028): <c>sprk_invoicesubmission.sprk_findingsjson</c> is field-secured to the BFF application user
/// ("Spaarke BFF-Managed Field Writers", read / create / update; no reader profile), so a read made as the CALLER returns null for every
/// user outside that profile. Every BFF reader of the column must therefore read it app-only (<see cref="IGenericEntityService"/>, the
/// BFF's application identity), or /review, /blocked, Override, Return and the submission job would silently see no findings.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Over the compiled IL (<see cref="IlCallScan"/>): (C1) the set of types that load a string naming the column is
/// exactly the reviewed set below, each with its identity recorded in <c>projects/ebilling-r1/notes/122-findings-json-field-secure.md</c>,
/// so a new reader fails until it is reviewed and listed; (C2) no reviewed type (closures and state machines included) calls a
/// caller-identity Dataverse seam; (C3) every reviewed type reaches Dataverse through <see cref="IGenericEntityService"/>. The column
/// name is a <c>const</c> (<see cref="InvoiceSubmissionPersister"/>), so every use compiles to an <c>ldstr</c> in the type that uses it,
/// interpolated Web API paths included.</para>
/// <para><b>Caller-identity seams (C2)</b>: any member of a type that implements <see cref="IDataverseUserClient"/> (the BFF's OBO
/// Dataverse client); any member of a type whose name contains <c>Impersonated</c>; any method whose name contains <c>AsCaller</c>; any
/// task-returning method with a parameter whose name contains <c>impersonat</c> or <c>callerSystemUserId</c> (the <c>MSCRMCallerID</c>
/// overloads; a value builder that only carries a caller id, such as <c>RecordOwnershipContext.ForParents</c>, reads nothing).
/// <c>CallerRecordAccessProbe</c>'s rights and privilege checks are not row reads and are not listed.</para>
/// <para><b>Enforced by this test</b> (CLAUDE.md §16 ladder): no type can say "this read is app-only", and there is no existing scan of
/// column reads, so the rule is an ArchTest with must-fire and must-not-fire controls.</para>
/// <para><b>Not covered</b> (code-review concerns): C2 is not transitive, so a reviewed type that hands the submission read to a helper in
/// another type which reads it as the caller is not seen (that helper names the column only if it loads the string itself, which C1
/// catches); a column array exposed by a reviewed type (<c>InvoiceSubmissionAfterSteps.SubmissionColumns</c>) and read by another type
/// through a field load; a caller-identity seam added later under a name the C2 rules do not match; and a read of all columns.</para>
/// </remarks>
public sealed class FindingsJsonAppOnlyReadGuardTests
{
    private const string Column = InvoiceSubmissionPersister.SubmissionFindingsJson;

    /// <summary>The reviewed readers and writers of the column, each app-only (identity in the task 122 notes).</summary>
    private static readonly Type[] ReviewedTypes =
    [
        typeof(InvoiceSubmissionPersister),
        typeof(InvoiceSubmissionAfterSteps),
        typeof(AdminAttentionText),
        typeof(SubmissionOverrideService),
        typeof(InvoiceReviewReadService),
        typeof(SubmissionCommitGate), // task 094: reads it app-only (IGenericEntityService) to decide whether a commit is left, and clears it in the undo
        typeof(InvoiceCorrectionService), // task 101 B1: reads it app-only (IGenericEntityService) for the header provenance (EB-D-170) of a header correction; never writes it
    ];

    /// <summary>The assemblies whose types may read Dataverse: the BFF and its data layer.</summary>
    private static readonly Assembly[] Scanned = [typeof(InvoiceSubmissionPersister).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "122 (#2235) C1: only the reviewed types name sprk_findingsjson (a new reader must be reviewed for its identity)")]
    public void OnlyTheReviewedTypesNameTheColumn()
    {
        var naming = TypesNamingTheColumn(Scanned.SelectMany(a => a.GetTypes()));

        var unreviewed = naming.Except(ReviewedTypes).Select(t => t.FullName).ToList();
        var stale = ReviewedTypes.Except(naming).Select(t => t.FullName).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These types name {Column} but are not in the reviewed list: {string.Join(", ", unreviewed)}. The column is field-secured to the "
            + "BFF application user, so a read as the caller returns null. Read it through IGenericEntityService (app-only), record the "
            + "reader and its identity in projects/ebilling-r1/notes/122-findings-json-field-secure.md, then add it to ReviewedTypes.");
        Assert.True(stale.Count == 0, $"These reviewed types no longer name {Column}; remove them from ReviewedTypes: {string.Join(", ", stale)}");
    }

    [Fact(DisplayName = "122 (#2235) C2: no reviewed reader of sprk_findingsjson calls a caller-identity Dataverse seam")]
    public void TheReviewedReadersCallNoCallerIdentitySeam()
    {
        var violations = CallerIdentityCalls(ReviewedTypes).ToList();

        Assert.True(violations.Count == 0,
            $"A type that reads {Column} calls a caller-identity Dataverse seam. The column is field-secured to the BFF application user, so "
            + "a caller read returns null and the findings silently disappear. Read the submission through IGenericEntityService: "
            + string.Join("; ", violations));
    }

    [Fact(DisplayName = "122 (#2235) C3: every reviewed reader of sprk_findingsjson reaches Dataverse through the app-only IGenericEntityService")]
    public void TheReviewedReadersUseTheAppOnlyClient()
    {
        foreach (var type in ReviewedTypes)
        {
            Assert.True(UsesAppOnlyClient(type), $"{type.FullName} names {Column} but references no IGenericEntityService member: the scan cannot see its read");
        }
    }

    // ── Controls ──

    [Fact(DisplayName = "122 (#2235) controls: C1 fires on a type that names the column and is not reviewed, and not on one that does not")]
    public void C1_Controls()
    {
        var naming = TypesNamingTheColumn(IlCallScan.WithNested(typeof(FindingsJsonReadControlFixtures)));

        Assert.Contains(typeof(FindingsJsonReadControlFixtures), naming);
        Assert.Empty(TypesNamingTheColumn(IlCallScan.WithNested(typeof(FindingsJsonUnrelatedControlFixture))));
    }

    [Theory(DisplayName = "122 (#2235) controls: C2 fires on each caller-identity read shape")]
    [InlineData(typeof(FindingsJsonReadControlFixtures.CallerClientReader), "IDataverseUserClient")]
    [InlineData(typeof(FindingsJsonReadControlFixtures.ImpersonatedQueryReader), "Impersonated")]
    [InlineData(typeof(FindingsJsonReadControlFixtures.AsCallerReader), "AsCaller")]
    [InlineData(typeof(FindingsJsonReadControlFixtures.ImpersonatingOverloadWriter), "impersonat")]
    public void C2_FiresOnEachCallerShape(Type planted, string expected)
    {
        var violations = CallerIdentityCalls([planted]).ToList();

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "122 (#2235) controls: C2 does not fire on an app-only read, and C3 sees it")]
    public void C2_DoesNotFireOnAnAppOnlyRead()
    {
        var planted = typeof(FindingsJsonReadControlFixtures.AppOnlyReader);

        Assert.Empty(CallerIdentityCalls([planted]));
        Assert.True(UsesAppOnlyClient(planted));
        Assert.False(UsesAppOnlyClient(typeof(FindingsJsonReadControlFixtures.CallerClientReader)));
    }

    // ── The scan ──

    private static HashSet<Type> TypesNamingTheColumn(IEnumerable<Type> types) =>
        IlCallScan.StringLoads(types)
            .Where(s => s.Value.Contains(Column, StringComparison.OrdinalIgnoreCase))
            .Select(s => IlCallScan.Outermost(s.Caller))
            .ToHashSet();

    private static IEnumerable<string> CallerIdentityCalls(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types.SelectMany(IlCallScan.WithNested))
            .Select(r => (r.Caller, r.CallerMethod, r.Target, Why: CallerIdentityReason(r.Target)))
            .Where(r => r.Why is not null)
            .Select(r => $"{IlCallScan.Outermost(r.Caller).Name}.{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)} -> {IlCallScan.Describe(r.Target)} ({r.Why})")
            .Distinct();

    private static string? CallerIdentityReason(MethodBase target)
    {
        var declaring = target.DeclaringType;
        if (declaring is not null && typeof(IDataverseUserClient).IsAssignableFrom(declaring))
        {
            return "a member of an IDataverseUserClient (OBO)";
        }

        if (declaring is not null && declaring.Name.Contains("Impersonated", StringComparison.Ordinal))
        {
            return "a member of an Impersonated seam";
        }

        if (target.Name.Contains("AsCaller", StringComparison.Ordinal))
        {
            return "an AsCaller method";
        }

        // Only an I/O call (it returns a task) can send MSCRMCallerID; a value builder that carries a caller id (RecordOwnershipContext.ForParents) reads nothing.
        return IsAsync(target) && target.GetParameters().Any(p => p.Name is { } n
                && (n.Contains("impersonat", StringComparison.OrdinalIgnoreCase) || n.Contains("callerSystemUserId", StringComparison.OrdinalIgnoreCase)))
            ? "an impersonating (MSCRMCallerID) overload"
            : null;
    }

    private static bool IsAsync(MethodBase method) =>
        method is MethodInfo { ReturnType: var r }
        && (r == typeof(Task) || r == typeof(ValueTask)
            || (r.IsGenericType && (r.GetGenericTypeDefinition() == typeof(Task<>) || r.GetGenericTypeDefinition() == typeof(ValueTask<>))));

    private static bool UsesAppOnlyClient(Type type) =>
        IlCallScan.MethodReferences(IlCallScan.WithNested(type))
            .Any(r => r.Target.DeclaringType is { } d && typeof(IGenericEntityService).IsAssignableFrom(d));
}

/// <summary>Must-fire and must-not-fire controls for <see cref="FindingsJsonAppOnlyReadGuardTests"/>. Never run; only their IL is read.</summary>
internal static class FindingsJsonReadControlFixtures
{
    internal sealed class CallerClientReader(IDataverseUserClient user)
    {
        public Task<DataverseUserResponse> ReadAsync(Guid id, CancellationToken ct) =>
            user.GetAsync($"sprk_invoicesubmissions({id:D})?$select=sprk_findingsjson", ct);
    }

    internal sealed class ImpersonatedQueryReader(Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery query)
    {
        public Task<IReadOnlyList<Dictionary<string, System.Text.Json.JsonElement>>> ReadAsync(Guid caller, CancellationToken ct) =>
            query.QueryAsync("sprk_invoicesubmissions", "$select=sprk_findingsjson", caller, ct);
    }

    internal sealed class AsCallerReader(IEventDataverseService events)
    {
        public Task<(EventEntity[] Items, int TotalCount)> ReadAsync(Guid caller, CancellationToken ct) =>
            events.QueryEventsAsCallerAsync(caller, ct: ct);
    }

    internal sealed class ImpersonatingOverloadWriter(IFieldMappingDataverseService fields)
    {
        public Task WriteAsync(Guid id, Guid caller) =>
            fields.UpdateRecordFieldsAsync("sprk_invoicesubmission", id, new Dictionary<string, object?> { ["sprk_findingsjson"] = "{}" }, impersonateSystemUserId: caller);
    }

    internal sealed class AppOnlyReader(IGenericEntityService entities)
    {
        public Task<Entity> ReadAsync(Guid id, CancellationToken ct) =>
            entities.RetrieveAsync("sprk_invoicesubmission", id, ["sprk_findingsjson"], ct);
    }
}

/// <summary>Must-not-fire control for C1: names another column only.</summary>
internal static class FindingsJsonUnrelatedControlFixture
{
    public static string Column() => "sprk_firmnoticetext";
}
