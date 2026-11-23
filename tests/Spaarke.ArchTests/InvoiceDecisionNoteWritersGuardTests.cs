using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;
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
/// were written in). Review F2: a name passed as a custom attribute argument (a DTO's <c>[JsonPropertyName("sprk_decisionnote")]</c>, on a type, a
/// member, a parameter or a return value; constructor and named arguments, arrays flattened) is metadata with no <c>ldstr</c>, so the attributes of
/// every scanned type and its declared members are read as well and a hit is attributed to its declaring type and member. The set of
/// (type, source method or member) pairs that name a column is checked against its reviewed set.</para>
/// <para><b>The decision note's writers</b> (design 1.3 and section 15): the lifecycle transition (<see cref="DecisionNoteWriters"/>, built by
/// 095f-2a: reject, return, approve and, since 095f-2c, the blocked return reach the column through it) and the override release transaction (built
/// by 095f-2c) and the totals path of an adjustment (built by 095f-2b). <see cref="PendingDecisionNoteWriters"/> stages a reviewed writer whose task
/// has not landed (empty since 095f-2b); the task that builds one moves it to <see cref="DecisionNoteWriters"/> in the same change; the stale check
/// applies only to the built list, so the pending entries do not fail before their task lands. Anything else that names the column fails, a firm-facing reader included (the external planes are also guarded by
/// <c>InvoiceNoteColumnsGuardTests.ExternalPlane_NeverNamesTheInternalDecisionNote</c>).</para>
/// <para><b>The correction note's one writer</b> (EB-D-240: "<c>sprk_correctionnote</c> keeps its single writer"): the persister's create
/// (<see cref="CorrectionNoteWriters"/>). The definition and its check are not loads of the name.</para>
/// <para><b>Maintenance.</b> When this fails, read the message: an unreviewed method either must not name the column (use the decision path's
/// lifecycle overload, or keep the firm's note where the persister writes it), or it is a reviewed new writer and is added to the matching list
/// with its task and decision. <b>Enforced by this test</b> (CLAUDE.md section 16 ladder): a type cannot say "only these methods write this
/// column", so the rule is an ArchTest with must-fire and must-not-fire controls (<see cref="DecisionNoteControls"/>,
/// <see cref="CorrectionNoteControls"/>, <see cref="AttributeControls"/>).</para>
/// <para><b>Not covered:</b>
/// a writer that builds the column name at run time (concatenation, formatting, a name read from configuration or data);
/// the generic write paths, which are governed by their own allow-lists;
/// assembly- and module-level attributes, a const field's value (the definition, not a use), a default parameter value, embedded resources and
/// UTF-8 or array constant data (<c>"…"u8</c>);
/// assemblies other than the BFF and <c>Spaarke.Dataverse</c> (<c>Spaarke.Core</c> is not scanned);
/// whether the pending writers write the column correctly once built (they are allowed, not verified, until their task moves them to the built list);
/// overloads: the allow-lists are keyed by method NAME, so an overload sharing an allowed method's name is allowed too.</para>
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
        // 095f-2c (design 4): the override release adds the note (or the reason) to the invoice update of its one transaction. A blocked return
        // reaches the column through the lifecycle's TransitionAsync above, not here.
        (typeof(SubmissionOverrideService), "BuildReleaseTransaction"),
        // 095f-2b (design 3 "Change, either option"; EB-D-241): an adjustment's note (or an add's reason) in the totals' own conditional update
        // of the invoice (the RecalculateAsync overload that takes the note). Moved from the pending list by 095f-2b.
        (typeof(InvoiceTotalsService), "RecalculateAsync"),
    ];

    /// <summary>Allowed writers of <c>sprk_decisionnote</c> whose task has not landed yet (design 1.3, section 15). May or may not name it. Empty
    /// since 095f-2b built the last one; kept so a future reviewed writer can be staged the same way.</summary>
    internal static readonly (Type Type, string Method)[] PendingDecisionNoteWriters = [];

    /// <summary>The one writer of <c>sprk_correctionnote</c> (EB-D-225, EB-D-240): the persister's create of a Correction's invoice.</summary>
    internal static readonly (Type Type, string Method)[] CorrectionNoteWriters =
    [
        (typeof(InvoiceSubmissionPersister), "FillInvoice"),
    ];

    private static readonly Assembly[] Scanned = [typeof(InvoiceSubmissionPersister).Assembly, typeof(IGenericEntityService).Assembly];

    [Fact(DisplayName = "095f-2a EB-D-240: only the lifecycle transition, the override release and the totals path name sprk_invoice.sprk_decisionnote")]
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

    [Fact(DisplayName = "095f-2a review F2 controls: either column named only in a custom attribute argument (type, DTO property, parameter) is reported (must fire); the allowed sets and an unrelated attribute are not (must not fire)")]
    public void AttributeControls()
    {
        static string[] Expected(Type fixture) =>
            [Key((fixture, nameof(DecisionNoteAttributeControlFixture.Bind))), Key((fixture, nameof(DecisionNoteAttributeControlFixture.Note))), Key((fixture, TypeLevel))];

        // Must fire: no IL loads either name in these fixtures; only their attributes do.
        Assert.Equal(
            Expected(typeof(DecisionNoteAttributeControlFixture)).Order(StringComparer.Ordinal),
            Unreviewed(MethodsNaming(IlCallScan.WithNested(typeof(DecisionNoteAttributeControlFixture)), DecisionColumn),
                DecisionNoteWriters.Concat(PendingDecisionNoteWriters)));
        Assert.Equal(
            Expected(typeof(CorrectionNoteAttributeControlFixture)).Order(StringComparer.Ordinal),
            Unreviewed(MethodsNaming(IlCallScan.WithNested(typeof(CorrectionNoteAttributeControlFixture)), CorrectionColumn), CorrectionNoteWriters));

        // Must not fire: the allowed writers' own types (attributes included) report only their reviewed entries; each fixture names only its
        // own column; an attribute naming another column is not a hit.
        Assert.Empty(Unreviewed(MethodsNaming(IlCallScan.WithNested(typeof(InvoiceLifecycleService)), DecisionColumn), DecisionNoteWriters));
        Assert.Empty(Unreviewed(MethodsNaming(IlCallScan.WithNested(typeof(InvoiceSubmissionPersister)), CorrectionColumn), CorrectionNoteWriters));
        Assert.Empty(MethodsNaming(IlCallScan.WithNested(typeof(DecisionNoteAttributeControlFixture)), CorrectionColumn));
        Assert.Empty(MethodsNaming(IlCallScan.WithNested(typeof(CorrectionNoteAttributeControlFixture)), DecisionColumn));
        Assert.Empty(MethodsNaming(IlCallScan.WithNested(typeof(NoteColumnsUnrelatedControlFixture)), DecisionColumn));
        Assert.Empty(MethodsNaming(IlCallScan.WithNested(typeof(NoteColumnsUnrelatedControlFixture)), CorrectionColumn));
    }

    private static string Key((Type Type, string Method) entry) => $"{entry.Type.FullName}::{entry.Method}";

    /// <summary>The naming methods outside <paramref name="reviewed"/> (pure), ordered.</summary>
    private static List<string> Unreviewed(IEnumerable<string> naming, IEnumerable<(Type Type, string Method)> reviewed)
    {
        var allowed = reviewed.Select(Key).ToHashSet(StringComparer.Ordinal);
        return naming.Where(n => !allowed.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>What a type-level attribute hit is attributed to: the type itself, never one of its method names.</summary>
    internal const string TypeLevel = "(type)";

    private const BindingFlags DeclaredMembers =
        BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    /// <summary>The methods whose IL loads <paramref name="column"/>, plus the types and members whose custom attributes name it.</summary>
    private static HashSet<string> MethodsNaming(IEnumerable<Type> types, string column)
    {
        var list = types.ToList();
        var naming = IlCallScan.StringLoads(list)
            .Where(s => s.Value.Contains(column, StringComparison.OrdinalIgnoreCase))
            .Select(s => Key((IlCallScan.Outermost(s.Caller), IlCallScan.SourceMethodName(s.Caller, s.CallerMethod))))
            .ToHashSet(StringComparer.Ordinal);
        naming.UnionWith(AttributesNaming(list, column));
        return naming;
    }

    /// <summary>
    /// 095f-2a review F2: the types, members and parameters whose custom attribute arguments (constructor and named, arrays flattened) name
    /// <paramref name="column"/>, e.g. <c>[JsonPropertyName("sprk_decisionnote")]</c> on a DTO property: metadata, not an <c>ldstr</c>, so the IL scan
    /// cannot see it. A hit is keyed as (outermost type, member): a member of the outermost type by its name (a parameter or return value by its
    /// method's name), the type itself by <see cref="TypeLevel"/>, and anything on a nested type as <c>"{nested type}.{member}"</c>, so it never
    /// satisfies an allowed method name by accident.
    /// </summary>
    private static IEnumerable<string> AttributesNaming(IEnumerable<Type> types, string column)
    {
        static IEnumerable<object?> Flatten(CustomAttributeTypedArgument argument) =>
            argument.Value is IReadOnlyCollection<CustomAttributeTypedArgument> items ? items.SelectMany(Flatten) : new[] { argument.Value };

        bool Names(IEnumerable<CustomAttributeData> attributes) =>
            attributes.Any(a => a.ConstructorArguments.Concat(a.NamedArguments.Select(n => n.TypedValue)).SelectMany(Flatten)
                .Any(v => v is string text && text.Contains(column, StringComparison.OrdinalIgnoreCase)));

        foreach (var type in types)
        {
            var outermost = IlCallScan.Outermost(type);
            string Where(string member) => type == outermost ? member : $"{type.Name}.{member}";

            if (Names(type.CustomAttributes))
            {
                yield return Key((outermost, Where(TypeLevel)));
            }

            foreach (var member in type.GetMembers(DeclaredMembers).Where(m => m.MemberType != MemberTypes.NestedType))
            {
                IEnumerable<ParameterInfo> parameters = member switch
                {
                    MethodInfo info => info.GetParameters().Append(info.ReturnParameter),
                    MethodBase method => method.GetParameters(),
                    PropertyInfo property => property.GetIndexParameters(),
                    _ => [],
                };
                if (Names(member.CustomAttributes) || parameters.Any(p => Names(p.CustomAttributes)))
                {
                    yield return Key((outermost, Where(member.Name)));
                }
            }
        }
    }
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

/// <summary>Names neither note column, in its IL or its attributes (must-not-fire).</summary>
[Description("sprk_reviewdetail")]
internal sealed class NoteColumnsUnrelatedControlFixture
{
    [JsonPropertyName("sprk_reviewdetail")]
    public string? Detail { get; init; }

    public static string Write([NoteColumnControl(Names = ["sprk_reason"])] string value) => "sprk_reviewdetail" + value;
}

/// <summary>
/// Review F2 (must-fire): the decision note named only in custom attribute arguments, on the type (<see cref="DescriptionAttribute"/>, a
/// constructor argument), a DTO property (<see cref="JsonPropertyNameAttribute"/>) and a method parameter (a named array argument). No IL loads it.
/// Never run; only its metadata is read.
/// </summary>
[Description(InvoiceCorrectionNote.DecisionColumn)]
internal sealed class DecisionNoteAttributeControlFixture
{
    [JsonPropertyName(InvoiceCorrectionNote.DecisionColumn)]
    public string? Note { get; init; }

    public static string Bind([NoteColumnControl(Names = ["sprk_reason", InvoiceCorrectionNote.DecisionColumn])] string value) => value;
}

/// <summary>Review F2 (must-fire): the firm's correction note named only in custom attribute arguments, in the same three shapes.</summary>
[Description(InvoiceCorrectionNote.Column)]
internal sealed class CorrectionNoteAttributeControlFixture
{
    [JsonPropertyName(InvoiceCorrectionNote.Column)]
    public string? Note { get; init; }

    public static string Bind([NoteColumnControl(Names = ["sprk_reason", InvoiceCorrectionNote.Column])] string value) => value;
}

/// <summary>A stand-in binding attribute with a named array argument, for the attribute controls.</summary>
[AttributeUsage(AttributeTargets.All)]
internal sealed class NoteColumnControlAttribute : Attribute
{
    public string[] Names { get; set; } = [];
}
