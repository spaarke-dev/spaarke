using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 095c (notice for uac-r2, notes/095c-from-email-firm-optional.md): <c>sprk_invoicesubmission.sprk_trackingscope</c> is a
/// numbering column, never an access input. External visibility of a submission is decided by its FIRM (<c>sprk_vendororg</c>) and its core
/// record only: <see cref="InvoiceSubmissionService.ListAsync"/> filters the firm in the caller's firms,
/// <c>ExternalInvoiceSubmissionAuthorizationFilter.DecideReadOneAsync</c> and the external replacement gate
/// (<c>ExternalInvoiceSubmissionEndpoints.ReplacementGate</c>) need the view's firm in the caller's firms. A no-firm submission (scope
/// <c>none</c>) therefore stays invisible externally; a gate that read the scope instead could treat the text <c>none</c>, or a firm id
/// written as text, as a firm match.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Over the compiled IL (<see cref="IlCallScan"/>), as <see cref="SubmissionSourceEmailAppOnlyGuardTests"/>: the set of types
/// that load a string naming the column is exactly the reviewed set below (its writer, its phase probe and its app-only write refusal), so a new
/// reader fails here until it is reviewed. The column name is a <c>const</c> (<see cref="TrackingNumberAllocator.ScopeColumn"/>), so every use
/// compiles to an <c>ldstr</c> in the type that uses it. The three external gates' declaring types are asserted absent by name as well.</para>
/// <para><b>Enforced by this test</b> (CLAUDE.md section 16 ladder): no type can say "this column decides no access", so the rule is an
/// ArchTest with must-fire and must-not-fire controls.</para>
/// <para><b>Not covered</b>: a reader that builds the column name at run time; a read of all columns (<c>ColumnSet(true)</c>) that then reads
/// the scope by a computed name.</para>
/// </remarks>
public sealed class SubmissionTrackingScopeNotAccessGuardTests
{
    private const string Column = TrackingNumberAllocator.ScopeColumn;

    /// <summary>The reviewed types that name the column. None decides access.</summary>
    private static readonly Type[] ReviewedTypes =
    [
        typeof(TrackingNumberAllocator), // WRITER: counts and writes the scope at create (app-only, IGenericEntityService)
        typeof(TrackingScopePhaseProbe), // METADATA: reads whether the column exists (entity metadata, no row)
        typeof(InheritedAccessPermission), // REFUSAL: lists it in AccessColumns, so an app-only generic writer cannot set it
        // READER (095d-1b): Set firm reads it app-only only to confirm a legacy repeat holds the stored firm's scope; it decides no access
        // (the route's rights are the caller's Write on the invoice and Read and AppendTo on the firm). The allocator writes it.
        typeof(Sprk.Bff.Api.Services.Finance.InvoiceAssignFirmService),
    ];

    /// <summary>The external submission gates: their types must never name the scope.</summary>
    private static readonly Type[] ExternalGates =
    [
        typeof(InvoiceSubmissionService), // ListAsync (firm In callerFirmIds) and the view's FirmId (from sprk_vendororg)
        typeof(ExternalInvoiceSubmissionAuthorizationFilter), // DecideReadOneAsync
        typeof(ExternalInvoiceSubmissionEndpoints), // ReplacementGate and VisibleSubmissionsAsync
    ];

    private static readonly Assembly[] Scanned = [typeof(InvoiceSubmissionService).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "095c: only the reviewed types name sprk_trackingscope (a new reader must be reviewed: the scope is never an access input)")]
    public void OnlyTheReviewedTypesNameTheColumn()
    {
        var naming = TypesNamingTheColumn(Scanned.SelectMany(a => a.GetTypes()));

        var unreviewed = naming.Except(ReviewedTypes).Select(t => t.FullName).ToList();
        var stale = ReviewedTypes.Except(naming).Select(t => t.FullName).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These types name {Column} but are not in the reviewed list: {string.Join(", ", unreviewed)}. The tracking scope numbers a "
            + "submission; it never decides who sees one (external visibility is the firm, sprk_vendororg, and the core record). Review the "
            + "reader against the 'Notice for uac-r2' in projects/ebilling-r1/notes/095c-from-email-firm-optional.md, then add it here.");
        Assert.True(stale.Count == 0, $"These reviewed types no longer name {Column}; remove them from ReviewedTypes: {string.Join(", ", stale)}");
    }

    [Fact(DisplayName = "095c: ListAsync, DecideReadOneAsync and the replacement gate never read sprk_trackingscope")]
    public void TheExternalGatesNeverNameTheScope()
    {
        var naming = TypesNamingTheColumn(ExternalGates.SelectMany(IlCallScan.WithNested));

        Assert.True(naming.Count == 0,
            $"An external submission gate names {Column}: {string.Join(", ", naming.Select(t => t.FullName))}. A no-firm submission must stay "
            + "invisible externally: gate on the firm (FirmId from sprk_vendororg), never on the scope.");
    }

    // ── Controls ──

    [Fact(DisplayName = "095c controls: the scan fires on a type that names the column, and not on one that names a neighbouring column")]
    public void Controls()
    {
        Assert.Contains(typeof(TrackingScopeControlFixture), TypesNamingTheColumn(IlCallScan.WithNested(typeof(TrackingScopeControlFixture))));
        Assert.Empty(TypesNamingTheColumn(IlCallScan.WithNested(typeof(TrackingScopeUnrelatedControlFixture))));
        Assert.Contains(typeof(TrackingNumberAllocator), TypesNamingTheColumn(IlCallScan.WithNested(typeof(TrackingNumberAllocator))));
    }

    private static HashSet<Type> TypesNamingTheColumn(IEnumerable<Type> types) =>
        IlCallScan.StringLoads(types)
            .Where(s => s.Value.Contains(Column, StringComparison.OrdinalIgnoreCase))
            .Select(s => IlCallScan.Outermost(s.Caller))
            .ToHashSet();
}

/// <summary>Names the column, so the scan sees this type (must-fire). Never run; only its IL is read.</summary>
internal static class TrackingScopeControlFixture
{
    public static string Name() => "sprk_trackingscope";
}

/// <summary>Must-not-fire control: names the tracking number only.</summary>
internal static class TrackingScopeUnrelatedControlFixture
{
    public static string Column() => "sprk_trackingnumber";
}
