using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 082c-2 (ontology D-205; uac-r2 2026-11-04, binding): <c>sprk_invoice.sprk_reviewdetail</c> is DERIVED
/// (<see cref="InvoiceReviewDetail.ReviewDetailOf"/>) and written only by the named writers, each recomputing it in the same write as the change it
/// follows. A new method that names the column fails here until it is reviewed and listed.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Over the compiled IL (<see cref="IlCallScan.StringLoads"/>): the column name is a <c>const</c>
/// (<see cref="InvoiceReviewDetail.Column"/>), so every use compiles to an <c>ldstr</c> in the method that uses it (lambdas and state machines
/// count for the method they were written in). The set of (type, source method) pairs that load it is exactly <see cref="Writers"/> plus
/// <see cref="NonWriters"/>.</para>
/// <para><b>Writers</b> (ontology D-205 numbering): (1) the submission persister's held create; (3) the 095d-1 Set firm apply; (6) every exit from
/// In Review and (7) the correction's Superseded close, both in <see cref="InvoiceLifecycleService"/>, the one home of the invoice status.
/// The supersede undo (fix round 1, F2) is the other half of (7): <c>SubmissionCommitGate.ReopenSupersededAsync</c> derives the detail from the
/// reopened invoice's remaining holds with <see cref="InvoiceReviewDetail.ReviewDetailOf"/> and hands it to
/// <see cref="IInvoiceLifecycleService.BuildSupersedeUndoRequest"/>, which writes it in <c>BuildConditionalTransition</c> (listed below). The gate
/// names no column literal, so <see cref="TheCommitGateUndoDerivesTheDetail"/> pins that it derives one.</para>
/// <para><b>Pending writers</b> (each must recompute from the invoice's REMAINING reasons in its own transaction, and be added here): (2) the
/// re-check that clears a gap or unresolved (no 082c-2 path clears one: 082d / 101's correction re-evaluation and the ebilling-r2 explicit
/// re-check); (4) the 095d-2 re-run; (5) the 114 Override release. The 089e chain close is NOT a pending writer (8): C19 found it writes
/// <c>sprk_signal</c> only and never an invoice status, so it never needs the detail.</para>
/// <para><b>Enforced by this test</b> (CLAUDE.md section 16 ladder): a type cannot say "only these methods write this column", so the rule is an
/// ArchTest with must-fire and must-not-fire controls. <b>Not covered:</b> a writer that builds the column name at run time; the generic paths
/// (app-only generic writes, field mappings and the Update Record node), which refuse the column through <c>ParentLineage</c>' <c>AccessColumns</c>
/// (app-only generic writes) AND <c>RecordTableRules</c>' <c>MappingProtectedColumns</c> (field mappings, the Update Record node).</para>
/// </remarks>
public sealed class InvoiceReviewDetailWritersGuardTests
{
    private const string Column = InvoiceReviewDetail.Column;

    /// <summary>The reviewed writers: (outermost type, source method).</summary>
    internal static readonly (Type Type, string Method)[] Writers =
    [
        // (1) the held create: ReviewDetailOf(HoldReasonsOf(outcome)), In Review only, in the create of the one persist transaction.
        (typeof(InvoiceSubmissionPersister), "PersistCoreAsync"),
        // (3) Set firm (095d-1): firm_missing lifts; recomputed from the Procedure reasons the findings still name, in the renumber transaction.
        (typeof(InvoiceAssignFirmService), "ApplyPlan.Build"),
        // (6) every TransitionAsync move (out of In Review, or onwards from Approved): blank, in the status write.
        (typeof(InvoiceLifecycleService), "TransitionAsync"),
        // (7) the correction's Superseded close: blank, in the status request the correction's transaction carries (never on the undo).
        (typeof(InvoiceLifecycleService), "BuildConditionalTransition"),
    ];

    /// <summary>Types and methods that name the column without writing a row value: the definition, the probe and the two registrations.</summary>
    internal static readonly (Type Type, string Method)[] NonWriters =
    [
        (typeof(InvoiceReviewDetail), "NamesTheColumn"), // the column-missing fault match
        (typeof(ReviewDetailColumnProbe), "IsPresentAsync"), // the one-row read that probes the column
        (typeof(ReviewDetailColumnProbe), "MarkMissing"), // the log-once warning
        (typeof(Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule), "AddExternalAccess"), // ReadableColumns of sprk_invoice (uac-r2)
        (typeof(Sprk.Bff.Api.Services.RecordCreation.RecordTableRules), ".cctor"), // MappingProtectedColumns of sprk_invoice (uac-r2)
        (typeof(Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission), ".cctor"), // AccessColumns of sprk_invoice: refused to app-only generic writes (uac-r2 F4)
        (typeof(Sprk.Bff.Api.Services.Dataverse.InheritedAccessPermission), "RefuseAppOnlyWrite"), // the refusal's reason text for the column
        // 082c-2 fix round 3 (uac-r2 U1): the external invoice reads null the column outside In Review (a projection of a READ response, never a row write).
        (typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalInvoiceReviewDetail), "IsDetail"), // the column-name match Project uses to null the value and label in the response copy
        (typeof(Sprk.Bff.Api.Infrastructure.ExternalAccess.ExternalInvoiceReviewDetail), "NeedsStatusRead"), // whether a $select names the column (to add the status)
    ];

    private static readonly Assembly[] Scanned = [typeof(InvoiceSubmissionPersister).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "082c-2: only the reviewed writers (and the definition and registrations) name sprk_invoice.sprk_reviewdetail")]
    public void OnlyTheReviewedMethodsNameTheColumn()
    {
        var naming = MethodsNamingTheColumn(Scanned.SelectMany(a => a.GetTypes()));
        var reviewed = Writers.Concat(NonWriters).Select(Key).ToHashSet(StringComparer.Ordinal);

        var unreviewed = naming.Except(reviewed).ToList();
        var stale = reviewed.Except(naming).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These methods name {Column} but are not reviewed: {string.Join(", ", unreviewed)}. The review detail is derived (ontology D-205): a writer "
            + "recomputes it with InvoiceReviewDetail.ReviewDetailOf from the invoice's remaining hold reasons in its own write, and blanks it on every "
            + "exit from In Review (uac-r2 (b)). Review it, then add it to Writers.");
        Assert.True(stale.Count == 0, $"These reviewed methods no longer name {Column}; remove them: {string.Join(", ", stale)}");
    }

    [Fact(DisplayName = "082c-2 fix round 1 (F2): the supersede undo derives the review detail (ReviewDetailOf) in SubmissionCommitGate.ReopenSupersededAsync")]
    public void TheCommitGateUndoDerivesTheDetail()
    {
        var derivers = MethodsCallingReviewDetailOf(IlCallScan.WithNested(typeof(SubmissionCommitGate)));

        Assert.True(derivers.Contains($"{typeof(SubmissionCommitGate).FullName}::ReopenSupersededAsync"),
            "The supersede undo reopens the old invoice In Review, so it must carry the detail the invoice's remaining holds derive (D-205 writer 7). "
            + $"SubmissionCommitGate no longer calls InvoiceReviewDetail.ReviewDetailOf in ReopenSupersededAsync; it calls it in: {string.Join(", ", derivers)}");

        // Controls: a method that calls ReviewDetailOf is seen; one that does not, is not.
        Assert.Contains(Key((typeof(ReviewDetailDeriverControlFixture), nameof(ReviewDetailDeriverControlFixture.Derive))),
            MethodsCallingReviewDetailOf(IlCallScan.WithNested(typeof(ReviewDetailDeriverControlFixture))));
        Assert.Empty(MethodsCallingReviewDetailOf(IlCallScan.WithNested(typeof(ReviewDetailUnrelatedControlFixture))));
    }

    [Fact(DisplayName = "082c-2 controls: the scan fires on a method that names the column, and not on one that names another column")]
    public void Controls()
    {
        Assert.Contains(Key((typeof(ReviewDetailWriterControlFixture), nameof(ReviewDetailWriterControlFixture.Write))),
            MethodsNamingTheColumn(IlCallScan.WithNested(typeof(ReviewDetailWriterControlFixture))));
        Assert.Empty(MethodsNamingTheColumn(IlCallScan.WithNested(typeof(ReviewDetailUnrelatedControlFixture))));
    }

    private static string Key((Type Type, string Method) entry) => $"{entry.Type.FullName}::{entry.Method}";

    private static HashSet<string> MethodsCallingReviewDetailOf(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.Name == nameof(InvoiceReviewDetail.ReviewDetailOf) && r.Target.DeclaringType == typeof(InvoiceReviewDetail))
            .Select(r => Key((IlCallScan.Outermost(r.Caller), IlCallScan.SourceMethodName(r.Caller, r.CallerMethod))))
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> MethodsNamingTheColumn(IEnumerable<Type> types) =>
        IlCallScan.StringLoads(types)
            .Where(s => s.Value.Contains(Column, StringComparison.OrdinalIgnoreCase))
            .Select(s => Key((IlCallScan.Outermost(s.Caller), IlCallScan.SourceMethodName(s.Caller, s.CallerMethod))))
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>Names the column (must-fire). Never run; only its IL is read.</summary>
internal static class ReviewDetailWriterControlFixture
{
    public static string Write() => "sprk_reviewdetail";
}

/// <summary>Derives the detail from reasons (must-fire for the derivation scan). Never run; only its IL is read.</summary>
internal static class ReviewDetailDeriverControlFixture
{
    public static int? Derive() => InvoiceReviewDetail.ReviewDetailOf(null);
}

/// <summary>Names another column only (must-not-fire).</summary>
internal static class ReviewDetailUnrelatedControlFixture
{
    public static string Write() => "sprk_firmnoticetext";
}
