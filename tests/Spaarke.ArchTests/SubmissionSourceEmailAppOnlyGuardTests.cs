using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 (task 095a review F2, uac-r2 binding): <c>sprk_invoicesubmission.sprk_sourceemaildocument</c>, the link from a from-email submission
/// to its source <c>.eml</c>, is field-secured to the BFF application user ("Spaarke BFF-Managed Field Writers", read / create / update; no reader
/// profile), so a read or write made as the CALLER returns null or is refused natively. Every BFF reader and writer of the column must therefore go
/// through the BFF's application identity (<see cref="IGenericEntityService"/>, directly or through <see cref="ITrackingNumberAllocator"/>, which
/// creates the submission row through it), or the by-email route would silently find no attempt (a 404 "not submitted" when the email was submitted)
/// and the from-email intake could not create its row.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Over the compiled IL (<see cref="IlCallScan"/>), as <see cref="ResubmissionOfAppOnlyWriteGuardTests"/>: (C1) the set of types
/// that load a string naming the column is exactly the reviewed set below, each with its identity recorded in
/// <c>projects/ebilling-r1/notes/095a-submission-by-email-read.md</c> (section 9, "Field security (F2)"), so a new reader or writer fails until it is
/// reviewed and listed; (C2) no reviewed type (closures and state machines included) calls a caller-identity Dataverse seam (the findings guard's
/// definition, <see cref="FindingsJsonAppOnlyReadGuardTests.CallerIdentityReason"/>); (C3) every reviewed type reaches Dataverse through
/// <see cref="IGenericEntityService"/> or <see cref="ITrackingNumberAllocator"/>. The allocator seam itself (every <see cref="ITrackingNumberAllocator"/>
/// creates the row through <see cref="IGenericEntityService"/>) is pinned once for both columns by
/// <see cref="ResubmissionOfAppOnlyWriteGuardTests"/> (C4). The column name is a <c>const</c>
/// (<see cref="InvoiceSubmissionService.SourceEmailDocumentColumn"/>), so every use compiles to an <c>ldstr</c> in the type that uses it.</para>
/// <para><b>Enforced by this test</b> (CLAUDE.md section 16 ladder): no type can say "this read is app-only", and the two sibling guards read other
/// columns, so the rule is an ArchTest with must-fire and must-not-fire controls (the controls' call shapes are the sibling guard's fixtures).</para>
/// <para><b>Not covered</b> (code-review concerns): C2 is not transitive; a reader that builds the column name at run time; a read of all columns
/// as the caller (the column would read null, which no code here depends on); a caller-identity seam added later under a name the C2 rules do not match.</para>
/// </remarks>
public sealed class SubmissionSourceEmailAppOnlyGuardTests
{
    private const string Column = InvoiceSubmissionService.SourceEmailDocumentColumn;

    /// <summary>The reviewed types that name the column, each app-only (identity in the 095a notes).</summary>
    private static readonly Type[] ReviewedTypes =
    [
        typeof(SubmitInvoiceFromEmailOperation), // WRITER (from-email intake): sets it on the create via ITrackingNumberAllocator (app-only)
        typeof(InvoiceSubmissionEndpoints), // READER (GET /invoice-submissions?internetMessageId=): the attempt query by the column, IGenericEntityService (app-only); the caller's own Read is asked per record afterwards
        typeof(RecordOwnershipResolver), // registration: the column is a citation (CitationLookups); it reads rows only through IGenericEntityService (app-only)
    ];

    /// <summary>The assemblies whose types may reach Dataverse: the BFF and its data layer.</summary>
    private static readonly Assembly[] Scanned = [typeof(InvoiceSubmissionService).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "095a F2 C1: only the reviewed types name sprk_sourceemaildocument (a new reader or writer must be reviewed for its identity)")]
    public void OnlyTheReviewedTypesNameTheColumn()
    {
        var naming = TypesNamingTheColumn(Scanned.SelectMany(a => a.GetTypes()));

        var unreviewed = naming.Except(ReviewedTypes).Select(t => t.FullName).ToList();
        var stale = ReviewedTypes.Except(naming).Select(t => t.FullName).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These types name {Column} but are not in the reviewed list: {string.Join(", ", unreviewed)}. The column is field-secured to the BFF "
            + "application user, so a read or write as the caller returns null or is refused. Use IGenericEntityService (app-only), record the "
            + "reader or writer and its identity in projects/ebilling-r1/notes/095a-submission-by-email-read.md, then add it to ReviewedTypes.");
        Assert.True(stale.Count == 0, $"These reviewed types no longer name {Column}; remove them from ReviewedTypes: {string.Join(", ", stale)}");
    }

    [Fact(DisplayName = "095a F2 C2: no reviewed type that names sprk_sourceemaildocument calls a caller-identity Dataverse seam")]
    public void TheReviewedTypesCallNoCallerIdentitySeam()
    {
        var violations = CallerIdentityCalls(ReviewedTypes).ToList();

        Assert.True(violations.Count == 0,
            $"A type that names {Column} calls a caller-identity Dataverse seam. The column is field-secured to the BFF application user, so a "
            + "read as the caller returns null and the by-email route would find no attempt. Use IGenericEntityService: " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "095a F2 C3: every reviewed type reaches Dataverse through the app-only seams")]
    public void TheReviewedTypesUseTheAppOnlyClient()
    {
        foreach (var type in ReviewedTypes)
        {
            Assert.True(UsesAppOnlySeam(type),
                $"{type.FullName} names {Column} but references no IGenericEntityService or ITrackingNumberAllocator member: the scan cannot see its identity");
        }
    }

    // ── Controls (the call shapes are the sibling guard's fixtures: they name sprk_resubmissionof, which these rules do not depend on) ──

    [Fact(DisplayName = "095a F2 controls: C1 fires on a type that names the column and is not reviewed, and not on one that names another column")]
    public void C1_Controls()
    {
        var naming = TypesNamingTheColumn(IlCallScan.WithNested(typeof(SourceEmailControlFixtures)));

        Assert.Contains(typeof(SourceEmailControlFixtures), naming);
        Assert.Empty(TypesNamingTheColumn(IlCallScan.WithNested(typeof(SourceEmailUnrelatedControlFixture))));
    }

    [Theory(DisplayName = "095a F2 controls: C2 fires on a read or write made as the caller (OBO client, impersonating overload), and C3 does not see an app-only seam in it")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.CallerClientWriter), "IDataverseUserClient")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.ImpersonatingOverloadWriter), "impersonat")]
    public void C2_FiresOnEachCallerShape(Type planted, string expected)
    {
        var violations = CallerIdentityCalls([planted]).ToList();

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.False(UsesAppOnlySeam(planted));
    }

    [Theory(DisplayName = "095a F2 controls: C2 does not fire on an app-only read or write, and C3 sees it")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.AppOnlyWriter))]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.AllocatorWriter))]
    public void C2_DoesNotFireOnAnAppOnlyAccess(Type planted)
    {
        Assert.Empty(CallerIdentityCalls([planted]));
        Assert.True(UsesAppOnlySeam(planted));
    }

    // ── The scan ──

    private static HashSet<Type> TypesNamingTheColumn(IEnumerable<Type> types) =>
        IlCallScan.StringLoads(types)
            .Where(s => s.Value.Contains(Column, StringComparison.OrdinalIgnoreCase))
            .Select(s => IlCallScan.Outermost(s.Caller))
            .ToHashSet();

    /// <summary>
    /// <c>InvoiceSubmissionService.DecideForFromEmailAsync</c> has a <c>callerSystemUserId</c> parameter the findings guard's rule reads as an
    /// impersonating (MSCRMCallerID) overload but is a VALUE (it stamps the caller as the new row's creator person and reads app-only); the same
    /// exemption, for the same method, as <see cref="ResubmissionOfAppOnlyWriteGuardTests"/>. The method body belongs to a type that is not
    /// reviewed here, so an impersonating call inside it is not this guard's concern.
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
}

/// <summary>Names the column, so C1 sees this type (must-fire). Never run; only its IL is read.</summary>
internal static class SourceEmailControlFixtures
{
    public static string Name() => "sprk_sourceemaildocument";
}

/// <summary>Must-not-fire control for C1: names another column only.</summary>
internal static class SourceEmailUnrelatedControlFixture
{
    public static string Column() => "sprk_sourceemail";
}
