using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 166 fix round (EB-D-200, adversarial review K4): the seven <c>sprk_invoice</c> columns that are field-secured to the BFF
/// application user ("Spaarke BFF-Managed Field Writers", read / create / update; "Spaarke BFF-Managed Field Readers", read): the billed total
/// (<see cref="InvoiceReviewService.InvTotalAmount"/>), the approved total and the four derived totals (<see cref="InvoiceTotalsService"/>), and
/// the firm (<see cref="InvoiceSubmissionPersister.InvoiceVendorOrg"/>). Every BFF WRITE of them must go through the BFF's application identity
/// (<see cref="IGenericEntityService"/>, directly or through <see cref="ITrackingNumberAllocator"/>); a write made as the caller is refused natively
/// once the columns are secured. And no writer may be a generic one: the app identity is a Writers member, so field security does not stop its
/// generic writers, and those are refused by <c>InheritedAccessPermission.AccessColumns</c> (<c>InheritedAccessPermission.RefuseAppOnlyWrite</c>),
/// which the typed writers must not ask.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Over the compiled IL (<see cref="IlCallScan"/>), as <see cref="ResubmissionOfAppOnlyWriteGuardTests"/> and
/// <see cref="SubmissionSourceEmailAppOnlyGuardTests"/>: (C1) the set of types that load a string naming any of the seven columns is exactly the
/// reviewed set below (<see cref="Writers"/>, <see cref="Readers"/> and <see cref="Registrations"/>), each with its identity recorded in
/// <c>projects/ebilling-r1/notes/166-invoice-field-security.md</c> (section 2), so a new writer or reader fails until it is reviewed and listed;
/// (C2) no WRITER (closures and state machines included) calls a caller-identity Dataverse seam (the findings guard's definition,
/// <see cref="FindingsJsonAppOnlyReadGuardTests.CallerIdentityReason"/>); (C3) every writer reaches Dataverse through
/// <see cref="IGenericEntityService"/> or <see cref="ITrackingNumberAllocator"/> (every allocator is pinned app-only once, by
/// <see cref="ResubmissionOfAppOnlyWriteGuardTests"/> C4); (C4) no writer calls <c>InheritedAccessPermission.RefuseAppOnlyWrite</c>, the refusal
/// of the generic writers. Readers are listed so that a new type naming a column is reviewed, but their identity is not constrained: the Readers
/// profile gives every user read access. A type that names the column of another table (a line's or a submission's <c>sprk_vendororg</c>) is
/// listed by what it does with it.</para>
/// <para><b>Enforced by this test</b> (CLAUDE.md section 16 ladder): no type can say "this write is app-only", so the rule is an ArchTest with
/// must-fire and must-not-fire controls (the caller-identity call shapes are the resubmission guard's fixtures).</para>
/// <para><b>Not covered</b> (code-review concerns): C2 is not transitive; a writer that builds a column name at run time; a generic writer that
/// copies caller-supplied column names into a row (the field-mapping framework, the AI record-update tools: refused by <c>AccessColumns</c> and
/// <c>RecordTableRules.MappingProtectedColumns</c>, pinned by <c>AccessTableAppOnlyWriterTests</c> and <c>RecordTableRulesTests</c>).</para>
/// </remarks>
public sealed class InvoiceMoneyColumnsAppOnlyWriteGuardTests
{
    /// <summary>The seven columns (constants, so a rename drifts here).</summary>
    private static readonly string[] Columns =
    [
        InvoiceReviewService.InvTotalAmount, InvoiceTotalsService.InvoiceApprovedTotal, InvoiceTotalsService.InvoiceFeesTotal,
        InvoiceTotalsService.InvoiceExpensesTotal, InvoiceTotalsService.InvoiceLineAdjustmentsTotal,
        InvoiceTotalsService.InvoiceInvoiceAdjustmentsTotal, InvoiceSubmissionPersister.InvoiceVendorOrg,
    ];

    /// <summary>Types that write one of the columns (or the same-named column of another table), each app-only (identity in the 166 notes).</summary>
    private static readonly Type[] Writers =
    [
        typeof(InvoiceTotalsService), // the five derived totals: RecalculateAsync, a conditional update through IRecordService (app-only)
        typeof(InvoiceSubmissionPersister), // the invoice's billed total and firm at create: FillInvoice, in the one ExecuteTransactionAsync (app-only)
        typeof(InvoiceAssignFirmService), // Set firm: the invoice's firm, in the renumber transaction (TrackingNumberAllocator, app-only)
        typeof(InvoiceCorrectionService), // the header correction: TotalDue to the billed total, in the correction's transactions (app-only)
        typeof(TrackingNumberAllocator), // the submission's firm (not the invoice's): BuildRow, BuildRenumberUpdate, through IGenericEntityService
        typeof(InvoiceAdjustmentService), // a line's firm (not the invoice's): BuildRow copies the invoice's firm onto the new line
        typeof(InvoiceLineAddService), // a line's firm (not the invoice's): Add line copies the invoice's firm onto the added line, through IGenericEntityService (app-only, task 164a)
    ];

    /// <summary>Types that read one of the columns (or hold its name in a list); the Readers profile gives every user read access.</summary>
    private static readonly Type[] Readers =
    [
        typeof(Sprk.Bff.Api.Api.Finance.InvoiceSubmissionFromEmailHttp),
        typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalInvoiceVisibility),
        typeof(ApExportService), typeof(FinanceBaseValues), typeof(FinanceSummaryService), typeof(InvoiceApprovalService),
        typeof(InvoiceContextService), typeof(InvoiceCorrectionReevaluation), typeof(InvoiceFindingsDiff), typeof(InvoiceLineDecisionService),
        typeof(InvoiceRejectReturnService), typeof(InvoiceReviewReadService),
        typeof(Sprk.Bff.Api.Services.Finance.Procedures.ProcedureAssignmentService),
        typeof(FromEmailSubmissionService), typeof(InvoiceFirmGate), typeof(InvoiceFirmHold), typeof(InvoiceSubmissionRerun),
        typeof(InvoiceSubmissionService), typeof(StoredSubmissionRead), typeof(SubmissionCommitGate), typeof(SubmissionSnapshot),
        typeof(SubmissionOverrideService),
        typeof(Sprk.Bff.Api.Services.Insights.LiveFacts.InvoiceLiveFactResolver),
        typeof(Sprk.Bff.Api.Services.Jobs.Handlers.InvoiceIndexDocument),
        typeof(Sprk.Bff.Api.Services.Signals.Billing.BillingFactLoader),
        typeof(RuleSetService), // task 083: GetFirmClauseAsync reads the invoice's firm (sprk_vendororg) app-only; writes nothing
    ];

    /// <summary>Types that hold the names in a registration: the allow-list, the generic-writer refusal and the mapping protection.</summary>
    private static readonly Type[] Registrations =
    [
        typeof(Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule), // ReadableColumns of sprk_invoice
        typeof(InheritedAccessPermission), // AccessColumns of sprk_invoice (app-only generic writers refuse the six amounts)
        typeof(Sprk.Bff.Api.Services.RecordCreation.RecordTableRules), // MappingProtectedColumns of sprk_invoice (field-mapping rules skip the six amounts)
    ];

    private static IEnumerable<Type> Reviewed => Writers.Concat(Readers).Concat(Registrations);

    /// <summary>The assemblies whose types may reach Dataverse: the BFF and its data layer.</summary>
    private static readonly Assembly[] Scanned = [typeof(InvoiceTotalsService).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "166 K4 C1: only the reviewed types name the seven field-secured invoice columns (a new writer must be reviewed for its identity)")]
    public void OnlyTheReviewedTypesNameTheColumns()
    {
        var naming = TypesNamingTheColumns(Scanned.SelectMany(a => a.GetTypes()));

        var unreviewed = naming.Except(Reviewed).Select(t => t.FullName).ToList();
        var stale = Reviewed.Except(naming).Select(t => t.FullName).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These types name one of {string.Join(", ", Columns)} but are not in the reviewed lists: {string.Join(", ", unreviewed)}. The columns are "
            + "field-secured so that only the BFF application user writes them, so a write as the caller is refused. Write them through "
            + "IGenericEntityService (app-only) from a typed writer, record the writer and its identity in "
            + "projects/ebilling-r1/notes/166-invoice-field-security.md, then add it to Writers (or to Readers if it only reads).");
        Assert.True(stale.Count == 0, $"These reviewed types no longer name any of the columns; remove them: {string.Join(", ", stale)}");
        Assert.Empty(Writers.Intersect(Readers).Concat(Writers.Intersect(Registrations)).Concat(Readers.Intersect(Registrations)));
    }

    [Fact(DisplayName = "166 K4 C2: no writer of the seven invoice columns calls a caller-identity Dataverse seam")]
    public void TheWritersCallNoCallerIdentitySeam()
    {
        var violations = CallerIdentityCalls(Writers).ToList();

        Assert.True(violations.Count == 0,
            "A writer of a field-secured invoice column calls a caller-identity Dataverse seam. The columns are field-secured so that only the BFF "
            + "application user writes them, so a write as the caller is refused. Use IGenericEntityService: " + string.Join("; ", violations));
    }

    [Fact(DisplayName = "166 K4 C3: every writer reaches Dataverse through the app-only seams")]
    public void TheWritersUseTheAppOnlyClient()
    {
        foreach (var type in Writers)
        {
            Assert.True(UsesAppOnlySeam(type),
                $"{type.FullName} names a field-secured invoice column but references no IGenericEntityService or ITrackingNumberAllocator member: the scan cannot see its identity");
        }
    }

    [Fact(DisplayName = "166 K4 C4: no writer asks the generic writers' refusal (the typed writers are not refused their own columns)")]
    public void TheWritersDoNotAskTheGenericWritersRefusal()
    {
        var asks = AsksTheGenericRefusal(Writers).ToList();

        Assert.True(asks.Count == 0,
            "A typed writer of a field-secured invoice column calls InheritedAccessPermission.RefuseAppOnlyWrite, the refusal of the generic writers "
            + "(AccessColumns names these columns), so it would refuse its own write: " + string.Join("; ", asks));
    }

    // ── Controls ──

    [Fact(DisplayName = "166 K4 controls: C1 fires on a type that names a column and is not reviewed, and not on one that names another column")]
    public void C1_Controls()
    {
        var naming = TypesNamingTheColumns(IlCallScan.WithNested(typeof(InvoiceMoneyControlFixtures)));

        Assert.Contains(typeof(InvoiceMoneyControlFixtures), naming);
        Assert.Empty(TypesNamingTheColumns(IlCallScan.WithNested(typeof(InvoiceMoneyUnrelatedControlFixture))));
        Assert.DoesNotContain(typeof(InvoiceMoneyControlFixtures), Reviewed);
    }

    [Theory(DisplayName = "166 K4 controls: C2 fires on a write made as the caller (OBO client, impersonating overload), and C3 does not see an app-only seam in it")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.CallerClientWriter), "IDataverseUserClient")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.ImpersonatingOverloadWriter), "impersonat")]
    public void C2_FiresOnEachCallerShape(Type planted, string expected)
    {
        var violations = CallerIdentityCalls([planted]).ToList();

        Assert.Contains(violations, v => v.Contains(expected, StringComparison.OrdinalIgnoreCase));
        Assert.False(UsesAppOnlySeam(planted));
    }

    [Theory(DisplayName = "166 K4 controls: C2 does not fire on an app-only write, and C3 sees it")]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.AppOnlyWriter))]
    [InlineData(typeof(ResubmissionOfWriteControlFixtures.AllocatorWriter))]
    public void C2_DoesNotFireOnAnAppOnlyWrite(Type planted)
    {
        Assert.Empty(CallerIdentityCalls([planted]));
        Assert.True(UsesAppOnlySeam(planted));
    }

    [Fact(DisplayName = "166 K4 controls: C4 fires on a writer that asks the generic refusal, and not on an app-only writer")]
    public void C4_Controls()
    {
        Assert.NotEmpty(AsksTheGenericRefusal([typeof(InvoiceMoneyControlFixtures.GenericRefusalAsker)]));
        Assert.Empty(AsksTheGenericRefusal([typeof(ResubmissionOfWriteControlFixtures.AppOnlyWriter)]));
        // The generic writers DO ask it (so the scan can see the call): the guard is not vacuous.
        Assert.NotEmpty(AsksTheGenericRefusal([typeof(DataverseUpdateHandler)]));
    }

    // ── The scan ──

    private static HashSet<Type> TypesNamingTheColumns(IEnumerable<Type> types) =>
        IlCallScan.StringLoads(types)
            .Where(s => Columns.Any(c => s.Value.Contains(c, StringComparison.OrdinalIgnoreCase)))
            .Select(s => IlCallScan.Outermost(s.Caller))
            .ToHashSet();

    private static IEnumerable<string> CallerIdentityCalls(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types.SelectMany(IlCallScan.WithNested))
            .Select(r => (r.Caller, r.CallerMethod, r.Target, Why: FindingsJsonAppOnlyReadGuardTests.CallerIdentityReason(r.Target)))
            .Where(r => r.Why is not null)
            .Select(r => $"{IlCallScan.Outermost(r.Caller).Name}.{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)} -> {IlCallScan.Describe(r.Target)} ({r.Why})")
            .Distinct();

    private static IEnumerable<string> AsksTheGenericRefusal(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types.SelectMany(IlCallScan.WithNested))
            .Where(r => r.Target.DeclaringType == typeof(InheritedAccessPermission) && r.Target.Name == "RefuseAppOnlyWrite")
            .Select(r => $"{IlCallScan.Outermost(r.Caller).Name}.{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)}")
            .Distinct();

    private static bool UsesAppOnlySeam(Type type) =>
        IlCallScan.MethodReferences(IlCallScan.WithNested(type))
            .Any(r => r.Target.DeclaringType is { } d
                && (typeof(IGenericEntityService).IsAssignableFrom(d) || typeof(ITrackingNumberAllocator).IsAssignableFrom(d)));
}

/// <summary>Names a column, so C1 sees this type (must-fire); also a nested writer that asks the generic refusal (C4's must-fire). Never run; only its IL is read.</summary>
internal static class InvoiceMoneyControlFixtures
{
    public static string Name() => "sprk_feestotal";

    internal sealed class GenericRefusalAsker
    {
        public static bool Asks() => InheritedAccessPermission.RefuseAppOnlyWrite("sprk_invoice", ["sprk_feestotal"]) is not null;
    }
}

/// <summary>Must-not-fire control for C1: names another column only.</summary>
internal static class InvoiceMoneyUnrelatedControlFixture
{
    public static string Column() => "sprk_feesubtotal";
}
