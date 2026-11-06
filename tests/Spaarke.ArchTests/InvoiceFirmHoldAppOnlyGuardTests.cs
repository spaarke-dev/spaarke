using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 095c-5 (uac-r2 2026-11-04, binding): <c>sprk_invoice.sprk_firmhold</c>, the invoice's own "firm needed" hold, is field-secured
/// ("Spaarke BFF-Managed Field Writers" read / create / update; "Spaarke BFF-Managed Field Readers" read). Field security does not fail a read by a
/// user outside the Readers profile: it returns null. A read on the caller's token would therefore see "no hold" and let an approval or an export
/// through, failing closed the wrong way. Every BFF reader and writer of the column must reach Dataverse through the application identity
/// (<see cref="IGenericEntityService"/>), as <c>sprk_findingsjson</c> is read (<see cref="FindingsJsonAppOnlyReadGuardTests"/>).
/// </summary>
/// <remarks>
/// <para><b>How.</b> Over the compiled IL (<see cref="IlCallScan"/>), as <see cref="FindingsJsonAppOnlyReadGuardTests"/>: (C1) the set of types that load
/// a string naming the column is exactly the reviewed set below, each with its identity recorded in
/// <c>projects/ebilling-r1/notes/095c-from-email-firm-optional.md</c> (section 15), so a new reader or writer fails until it is reviewed and listed;
/// (C2) no reviewed type (closures and state machines included) calls a caller-identity Dataverse seam (the findings guard's definition,
/// <see cref="FindingsJsonAppOnlyReadGuardTests.CallerIdentityReason"/>); (C3) every reviewed reader or writer reaches Dataverse through
/// <see cref="IGenericEntityService"/>. The refusal list (<see cref="InheritedAccessPermission"/>, a registration that reads nothing) is held to C1
/// and C2 only. The column name is a <c>const</c> (<see cref="InvoiceFirmHold.InvoiceHoldColumn"/>), so every use compiles to an <c>ldstr</c> in the
/// type that uses it.</para>
/// <para><b>Enforced by this test</b> (CLAUDE.md section 16 ladder): no type can say "this read is app-only", and the sibling guards scan other
/// columns, so the rule is an ArchTest with must-fire and must-not-fire controls (the caller-shape controls are the findings guard's fixtures).</para>
/// <para><b>Not covered</b> (code-review concerns): C2 is not transitive; a reader that builds the column name at run time; a read of all columns as
/// the caller (the hold would read null, which no code here depends on: <see cref="InvoiceFirmHold"/> re-reads it app-only and never trusts the
/// invoice row a caller hands in); a caller-identity seam added later under a name the C2 rules do not match.</para>
/// </remarks>
public sealed class InvoiceFirmHoldAppOnlyGuardTests
{
    private const string Column = InvoiceFirmHold.InvoiceHoldColumn;

    /// <summary>The reviewed readers and writers of the column, each app-only (identity in the 095c notes, section 15).</summary>
    private static readonly Type[] ReviewedTypes =
    [
        typeof(InvoiceFirmHold), // READER: HeldInvoicesAsync queries sprk_invoice for the column through the IGenericEntityService its callers pass (app-only)
        typeof(InvoiceOpenHolds), // READER (082c-2 fix round 3): ReasonsAsync reads the invoice's own hold through the IGenericEntityService its callers pass (app-only); never writes it
        typeof(InvoiceSubmissionPersister), // WRITER: sets it in the invoice create of the held outcome, inside the app-only ExecuteTransactionAsync
        // WRITER (095d-1b, EB-D-185): WRITES the rerun-pending marker over firm_missing in the invoice update of Set firm's one renumber
        // transaction, sent by the allocator's app-only ExecuteTransactionAsync; it never clears the column. It reads the hold only through
        // InvoiceFirmHold.IsHeldAsync over its own IGenericEntityService (app-only).
        typeof(Sprk.Bff.Api.Services.Finance.InvoiceAssignFirmService),
        // READER and WRITER (095d-2a-2): the re-run reads the invoice's hold through its own IGenericEntityService (app-only) and clears the
        // rerun-pending marker in its one ExecuteTransactionAsync (app-only, row-version conditioned), the only clearer of that marker.
        typeof(InvoiceSubmissionRerun),
        // READER (095d-2b-1 fix round, uac-r2 2026-11-06 C1/C2): the external invoice predicate withholds an invoice and its submissions while
        // the hold is set. ReadFirmHeldInvoicesAsync reads the column through the IGenericEntityService ExternalDataService passes (its
        // app-only filing reader; none -> the read throws and everything asked is hidden). It never writes the column, and the decision never
        // reads it from a caller-token row.
        typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalInvoiceVisibility),
    ];

    /// <summary>Types that name the column without reading or writing a row: held to C1 and C2, not C3.</summary>
    private static readonly Type[] RegistrationOnlyTypes =
    [
        typeof(InheritedAccessPermission), // AccessColumns: the column is refused to the app-only generic writers
    ];

    /// <summary>The assemblies whose types may reach Dataverse: the BFF and its data layer.</summary>
    private static readonly Assembly[] Scanned = [typeof(InvoiceSubmissionPersister).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "095c-5 C1: only the reviewed types name sprk_invoice.sprk_firmhold (a new reader or writer must be reviewed for its identity)")]
    public void OnlyTheReviewedTypesNameTheColumn()
    {
        var naming = TypesNamingTheColumn(Scanned.SelectMany(a => a.GetTypes()));
        var reviewed = ReviewedTypes.Concat(RegistrationOnlyTypes).ToList();

        var unreviewed = naming.Except(reviewed).Select(t => t.FullName).ToList();
        var stale = reviewed.Except(naming).Select(t => t.FullName).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These types name {Column} but are not in the reviewed list: {string.Join(", ", unreviewed)}. The column is field-secured, so a read as "
            + "the caller returns null for a user outside the Readers profile and reads as 'no hold'. Read it through IGenericEntityService (app-only), "
            + "record the reader and its identity in projects/ebilling-r1/notes/095c-from-email-firm-optional.md (section 15), then add it to ReviewedTypes.");
        Assert.True(stale.Count == 0, $"These reviewed types no longer name {Column}; remove them from the list: {string.Join(", ", stale)}");
    }

    [Fact(DisplayName = "095c-5 C2: no type that names sprk_invoice.sprk_firmhold calls a caller-identity Dataverse seam")]
    public void TheReviewedTypesCallNoCallerIdentitySeam()
    {
        var violations = CallerIdentityCalls(ReviewedTypes.Concat(RegistrationOnlyTypes)).ToList();

        Assert.True(violations.Count == 0,
            $"A type that names {Column} calls a caller-identity Dataverse seam. A caller read of the field-secured hold returns null (no hold) and "
            + "the approval or export would go through. Use IGenericEntityService: " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "095c-5 C3: every reviewed reader or writer of sprk_invoice.sprk_firmhold reaches Dataverse through the app-only IGenericEntityService")]
    public void TheReviewedTypesUseTheAppOnlyClient()
    {
        foreach (var type in ReviewedTypes)
        {
            Assert.True(UsesAppOnlyClient(type), $"{type.FullName} names {Column} but references no IGenericEntityService member: the scan cannot see its identity");
        }
    }

    // ── Controls (the caller-shape fixtures are the findings guard's: they name sprk_findingsjson, which these rules do not depend on) ──

    [Fact(DisplayName = "095c-5 controls: C1 fires on a type that names the column and is not reviewed, and not on one that names another column")]
    public void C1_Controls()
    {
        Assert.Contains(typeof(FirmHoldControlFixture), TypesNamingTheColumn(IlCallScan.WithNested(typeof(FirmHoldControlFixture))));
        Assert.Empty(TypesNamingTheColumn(IlCallScan.WithNested(typeof(FirmHoldUnrelatedControlFixture))));
    }

    [Theory(DisplayName = "095c-5 controls: C2 fires on each caller-identity read shape, and C3 does not see an app-only seam in it")]
    [InlineData(typeof(FindingsJsonReadControlFixtures.CallerClientReader), "IDataverseUserClient")]
    [InlineData(typeof(FindingsJsonReadControlFixtures.ImpersonatedQueryReader), "Impersonated")]
    [InlineData(typeof(FindingsJsonReadControlFixtures.AsCallerReader), "AsCaller")]
    [InlineData(typeof(FindingsJsonReadControlFixtures.ImpersonatingOverloadWriter), "impersonat")]
    public void C2_FiresOnEachCallerShape(Type planted, string expected)
    {
        Assert.Contains(CallerIdentityCalls([planted]), v => v.Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.False(UsesAppOnlyClient(planted));
    }

    [Fact(DisplayName = "095c-5 controls: C2 does not fire on an app-only read, and C3 sees it")]
    public void C2_DoesNotFireOnAnAppOnlyRead()
    {
        var planted = typeof(FindingsJsonReadControlFixtures.AppOnlyReader);

        Assert.Empty(CallerIdentityCalls([planted]));
        Assert.True(UsesAppOnlyClient(planted));
    }

    // ── The scan ──

    private static HashSet<Type> TypesNamingTheColumn(IEnumerable<Type> types) =>
        IlCallScan.StringLoads(types)
            .Where(s => s.Value.Contains(Column, StringComparison.OrdinalIgnoreCase))
            .Select(s => IlCallScan.Outermost(s.Caller))
            .ToHashSet();

    private static IEnumerable<string> CallerIdentityCalls(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types.SelectMany(IlCallScan.WithNested))
            .Select(r => (r.Caller, r.CallerMethod, r.Target, Why: FindingsJsonAppOnlyReadGuardTests.CallerIdentityReason(r.Target)))
            .Where(r => r.Why is not null)
            .Select(r => $"{IlCallScan.Outermost(r.Caller).Name}.{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)} -> {IlCallScan.Describe(r.Target)} ({r.Why})")
            .Distinct();

    private static bool UsesAppOnlyClient(Type type) =>
        IlCallScan.MethodReferences(IlCallScan.WithNested(type))
            .Any(r => r.Target.DeclaringType is { } d && typeof(IGenericEntityService).IsAssignableFrom(d));
}

/// <summary>Names the column, so C1 sees this type (must-fire). Never run; only its IL is read.</summary>
internal static class FirmHoldControlFixture
{
    public static string Name() => "sprk_firmhold";
}

/// <summary>Must-not-fire control for C1: names another column only.</summary>
internal static class FirmHoldUnrelatedControlFixture
{
    public static string Column() => "sprk_firmnoticetext";
}
