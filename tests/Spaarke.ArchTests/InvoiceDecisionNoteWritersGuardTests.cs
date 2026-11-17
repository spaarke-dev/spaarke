using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 095f-2a (EB-D-236, EB-D-240; design 1.3 and N4 as settled by section 15): who may name the two invoice note columns.
/// <c>sprk_invoice.sprk_decisionnote</c> (<see cref="InvoiceCorrectionNote.DecisionColumn"/>) is the INTERNAL note of the latest invoice-level
/// decision, written only in the same conditional write as the decision's own invoice change. <c>sprk_invoice.sprk_correctionnote</c>
/// (<see cref="InvoiceCorrectionNote.Column"/>) is the FIRM's "why corrected" text, written only by the submission persister; no decision path
/// ever writes it. A new method that names either column fails here until it is reviewed and listed.
/// </summary>
/// <remarks>
/// <para><b>How.</b> Over the compiled IL (<see cref="IlCallScan.StringLoads"/>), as <see cref="InvoiceReviewDetailWritersGuardTests"/>: both column
/// names are <c>const</c>s, so every use compiles to an <c>ldstr</c> in the method that uses it (lambdas and state machines count for the method they
/// were written in). The set of (type, source method) pairs that load a column is checked against its reviewed set.</para>
/// <para><b>The decision note's writers</b> (design 1.3 and section 15): the lifecycle transition (<see cref="DecisionNoteWriters"/>, built by
/// 095f-2a: reject, return and approve reach the column through it); the override release transaction (095f-2c) and the totals path of an
/// adjustment (095f-2b) are allowed but not built yet (<see cref="PendingDecisionNoteWriters"/>). The task that builds one moves it from the pending
/// list to <see cref="DecisionNoteWriters"/> in the same change; the stale check applies only to the built list, so the pending entries do not
/// fail before their task lands. Anything else that names the column fails, a firm-facing reader included (the external planes are also guarded by
/// <c>InvoiceNoteColumnsGuardTests.ExternalPlane_NeverNamesTheInternalDecisionNote</c>).</para>
/// <para><b>The correction note's one writer</b> (EB-D-240: "<c>sprk_correctionnote</c> keeps its single writer"): the persister's create
/// (<see cref="CorrectionNoteWriters"/>). The definition and its check are not loads of the name.</para>
/// <para><b>Maintenance.</b> When this fails, read the message: an unreviewed method either must not name the column (use the decision path's
/// lifecycle overload, or keep the firm's note where the persister writes it), or it is a reviewed new writer and is added to the matching list
/// with its task and decision. <b>Enforced by this test</b> (CLAUDE.md section 16 ladder): a type cannot say "only these methods write this
/// column", so the rule is an ArchTest with must-fire and must-not-fire controls (<see cref="DecisionNoteControls"/>,
/// <see cref="CorrectionNoteControls"/>). <b>Not covered:</b> a writer that builds the column name at run time, and the generic write paths, which
/// are governed by their own allow-lists.</para>
/// </remarks>
public sealed class InvoiceDecisionNoteWritersGuardTests
{
    private const string DecisionColumn = InvoiceCorrectionNote.DecisionColumn;
    private const string CorrectionColumn = InvoiceCorrectionNote.Column;

    /// <summary>The built writers of <c>sprk_decisionnote</c>: (outermost type, source method). Each must still name it (stale check).</summary>
    internal static readonly (Type Type, string Method)[] DecisionNoteWriters =
    [
        // 095f-2a (design 2 Change 1): the note in the same fields dictionary as the status, in the one conditional update. Reject and return
        // pass the note or the reason; approve passes the note only.
        (typeof(InvoiceLifecycleService), "TransitionAsync"),
    ];

    /// <summary>Allowed writers of <c>sprk_decisionnote</c> whose task has not landed yet (design 1.3, section 15). May or may not name it.</summary>
    internal static readonly (Type Type, string Method)[] PendingDecisionNoteWriters =
    [
        // 095f-2c (design 4): the override release adds the note to the invoice update of its one transaction.
        (typeof(SubmissionOverrideService), "BuildReleaseTransaction"),
        // 095f-2b (design 3 "Change, either option"): the adjustment's note in the totals write of the invoice.
        (typeof(InvoiceTotalsService), "RecalculateAsync"),
    ];

    /// <summary>The one writer of <c>sprk_correctionnote</c> (EB-D-225, EB-D-240): the persister's create of a Correction's invoice.</summary>
    internal static readonly (Type Type, string Method)[] CorrectionNoteWriters =
    [
        (typeof(InvoiceSubmissionPersister), "FillInvoice"),
    ];

    private static readonly Assembly[] Scanned = [typeof(InvoiceSubmissionPersister).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "095f-2a EB-D-240: only the lifecycle transition (and the pending override release and totals path) names sprk_invoice.sprk_decisionnote")]
    public void OnlyTheReviewedWritersNameTheDecisionNote()
    {
        var naming = MethodsNaming(Scanned.SelectMany(a => a.GetTypes()), DecisionColumn);

        var unreviewed = Unreviewed(naming, DecisionNoteWriters.Concat(PendingDecisionNoteWriters));
        var stale = DecisionNoteWriters.Select(Key).Except(naming).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These methods name {DecisionColumn} but are not reviewed writers: {string.Join(", ", unreviewed)}. The decision note is internal "
            + "(EB-D-240) and is written only in the same conditional write as the decision's own invoice change: decision paths go through "
            + "IInvoiceLifecycleService.TransitionAsync(invoiceId, target, decisionNote, ct). No firm-facing reader may name it. A reviewed new writer "
            + "is added to DecisionNoteWriters with its task.");
        Assert.True(stale.Count == 0, $"These reviewed writers no longer name {DecisionColumn}; remove them: {string.Join(", ", stale)}");
    }

    [Fact(DisplayName = "095f-2a EB-D-240: sprk_invoice.sprk_correctionnote keeps its single writer, the submission persister; no decision path names it")]
    public void TheCorrectionNoteKeepsItsOneWriter()
    {
        var naming = MethodsNaming(Scanned.SelectMany(a => a.GetTypes()), CorrectionColumn);

        var unreviewed = Unreviewed(naming, CorrectionNoteWriters);
        var stale = CorrectionNoteWriters.Select(Key).Except(naming).ToList();
        Assert.True(unreviewed.Count == 0,
            $"These methods name {CorrectionColumn}, which is the FIRM's own correction note written only by the submission persister (EB-D-240): "
            + $"{string.Join(", ", unreviewed)}. A decision's note goes to {DecisionColumn} through the lifecycle overload instead.");
        Assert.True(stale.Count == 0, $"The persister no longer names {CorrectionColumn} where this guard expects it; update CorrectionNoteWriters: {string.Join(", ", stale)}");
    }

    [Fact(DisplayName = "095f-2a controls: a planted extra writer of sprk_decisionnote is reported (must fire); the allowed set and other columns are not (must not fire)")]
    public void DecisionNoteControls()
    {
        var reviewed = DecisionNoteWriters.Concat(PendingDecisionNoteWriters).ToArray();

        // Must fire: a method outside the allowed set that names the column.
        var planted = MethodsNaming(IlCallScan.WithNested(typeof(DecisionNotePlantedWriterControlFixture)), DecisionColumn);
        Assert.Equal([Key((typeof(DecisionNotePlantedWriterControlFixture), nameof(DecisionNotePlantedWriterControlFixture.Write)))], Unreviewed(planted, reviewed));

        // Must not fire: the real lifecycle (the built writer), and a method that names only the firm's correction note or another column.
        var lifecycle = MethodsNaming(IlCallScan.WithNested(typeof(InvoiceLifecycleService)), DecisionColumn);
        Assert.Contains(Key(DecisionNoteWriters[0]), lifecycle);
        Assert.Empty(Unreviewed(lifecycle, reviewed));
        Assert.Empty(MethodsNaming(IlCallScan.WithNested(typeof(CorrectionNotePlantedDecisionPathControlFixture)), DecisionColumn));
        Assert.Empty(MethodsNaming(IlCallScan.WithNested(typeof(NoteColumnsUnrelatedControlFixture)), DecisionColumn));
    }

    [Fact(DisplayName = "095f-2a controls: a planted decision-path writer of sprk_correctionnote is reported (must fire); the persister and the decision note are not (must not fire)")]
    public void CorrectionNoteControls()
    {
        // Must fire: a decision-path method that writes the firm's correction note.
        var planted = MethodsNaming(IlCallScan.WithNested(typeof(CorrectionNotePlantedDecisionPathControlFixture)), CorrectionColumn);
        Assert.Equal(
            [Key((typeof(CorrectionNotePlantedDecisionPathControlFixture), nameof(CorrectionNotePlantedDecisionPathControlFixture.Decide)))],
            Unreviewed(planted, CorrectionNoteWriters));

        // Must not fire: the real persister (the one writer), a method that names only the decision note, and an unrelated column.
        var persister = MethodsNaming(IlCallScan.WithNested(typeof(InvoiceSubmissionPersister)), CorrectionColumn);
        Assert.Contains(Key(CorrectionNoteWriters[0]), persister);
        Assert.Empty(Unreviewed(persister, CorrectionNoteWriters));
        Assert.Empty(MethodsNaming(IlCallScan.WithNested(typeof(DecisionNotePlantedWriterControlFixture)), CorrectionColumn));
        Assert.Empty(MethodsNaming(IlCallScan.WithNested(typeof(NoteColumnsUnrelatedControlFixture)), CorrectionColumn));
    }

    private static string Key((Type Type, string Method) entry) => $"{entry.Type.FullName}::{entry.Method}";

    /// <summary>The naming methods outside <paramref name="reviewed"/> (pure), ordered.</summary>
    private static List<string> Unreviewed(IEnumerable<string> naming, IEnumerable<(Type Type, string Method)> reviewed)
    {
        var allowed = reviewed.Select(Key).ToHashSet(StringComparer.Ordinal);
        return naming.Where(n => !allowed.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static HashSet<string> MethodsNaming(IEnumerable<Type> types, string column) =>
        IlCallScan.StringLoads(types)
            .Where(s => s.Value.Contains(column, StringComparison.OrdinalIgnoreCase))
            .Select(s => Key((IlCallScan.Outermost(s.Caller), IlCallScan.SourceMethodName(s.Caller, s.CallerMethod))))
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>Names the decision note outside the allowed writers (must-fire). Never run; only its IL is read.</summary>
internal static class DecisionNotePlantedWriterControlFixture
{
    public static Dictionary<string, object?> Write(string note) => new() { [InvoiceCorrectionNote.DecisionColumn] = note };
}

/// <summary>A decision path that writes the firm's correction note (must-fire for the correction-note guard). Never run; only its IL is read.</summary>
internal static class CorrectionNotePlantedDecisionPathControlFixture
{
    public static Dictionary<string, object?> Decide(string note) => new() { ["statuscode"] = 2, [InvoiceCorrectionNote.Column] = note };
}

/// <summary>Names neither note column (must-not-fire).</summary>
internal static class NoteColumnsUnrelatedControlFixture
{
    public static string Write() => "sprk_reviewdetail";
}
