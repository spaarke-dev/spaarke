using System.Reflection;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// UAC task 194b fix round R (B3): <see cref="RecordOwnershipContext.ForParentsInWriterOrder"/> — asking the owner question
/// in the order a writer names its parents, not its table's primary order — is a CLOSED list of two callers:
/// <c>SignalWriter.OwnershipContextOf</c> and <c>DecisionRecordWriter.OwnershipContextOf</c>, whose own order IS their
/// table's rule. Every other writer asks through <see cref="RecordOwnershipContext.ForRow"/>, the order the reconciler asks
/// the written row in; a writer that needs another primary has found a table whose order is wrong, and that is changed in
/// <c>SecureChildLineage</c>, with a reason, never by a new caller here.
/// </summary>
/// <remarks>
/// <para><b>Why IL.</b> <see cref="IlCallScan"/> reads the compiled method references, so a call inside a lambda, a local
/// function or an async state machine counts, and a comment, string or <c>nameof</c> does not. A caller is pinned by its
/// source METHOD (<see cref="IlCallScan.SourceMethod"/>), not just its type: another method of an allowed writer — or a
/// lambda in the allowed method, which is a different body — is a violation too.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over the
/// <c>WriterOrderOptOutControl*</c> fixtures: a direct call and a call inside a lambda are found; a writer that asks through
/// <see cref="RecordOwnershipContext.ForRow"/> and one that only names the opt-out with <c>nameof</c> are not.</para>
/// </remarks>
public class WriterOrderOptOutAllowListTests
{
    /// <summary>The closed list: (type, method) pairs that may call the opt-out, each with its reason.</summary>
    private static readonly IReadOnlyList<(Type Type, string Method, string Reason)> AllowList = new[]
    {
        (typeof(SignalWriter), nameof(SignalWriter.OwnershipContextOf),
            "sprk_signal's rule is the writer's order (core, subject, stamps); ForExistingRowAsync builds the same list."),
        (typeof(DecisionRecordWriter), nameof(DecisionRecordWriter.OwnershipContextOf),
            "sprk_decisionrecord's rule is the writer's order (its Signal's core, subject, derived cores, each Signal's subject)."),
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "194b R (B3): only SignalWriter.OwnershipContextOf and DecisionRecordWriter.OwnershipContextOf ask in the writer's order")]
    public void OnlyTheClosedListCallsTheWriterOrderOptOut()
    {
        var callers = CallersOfTheOptOut(BffTypes.Value);

        var outside = callers
            .Where(c => !AllowList.Any(a => a.Type == c.Type && string.Equals(a.Method, c.Method, StringComparison.Ordinal)))
            .Select(c => $"{c.Type.FullName}.{c.Method}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.True(outside.Length == 0,
            "RecordOwnershipContext.ForParentsInWriterOrder is a CLOSED opt-out (UAC 194b fix round R, B3). Ask through " +
            "RecordOwnershipContext.ForRow (the table's primary order, as the reconciler asks); if the table's order is wrong, " +
            "change it in SecureChildLineage with a reason. Not on the list:\n" + string.Join("\n", outside));

        var stale = AllowList
            .Where(a => !callers.Any(c => c.Type == a.Type && string.Equals(c.Method, a.Method, StringComparison.Ordinal)))
            .Select(a => $"{a.Type.Name}.{a.Method}")
            .ToArray();
        Assert.True(stale.Length == 0,
            "An allow-list entry no longer calls ForParentsInWriterOrder — remove it, so the list stays exactly its callers: " +
            string.Join(", ", stale));
    }

    [Fact(DisplayName = "194b R (B3): control — a direct or lambda call is found; ForRow or a nameof is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(WriterOrderOptOutControlDirectCaller), typeof(WriterOrderOptOutControlLambdaCaller),
            typeof(WriterOrderOptOutControlForRowWriter), typeof(WriterOrderOptOutControlNameOnly),
        };
        var found = CallersOfTheOptOut(fixtures.SelectMany(IlCallScan.WithNested))
            .Select(c => c.Type.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { nameof(WriterOrderOptOutControlDirectCaller), nameof(WriterOrderOptOutControlLambdaCaller) }, found);

        // A lambda's body is not the method it sits in: even inside an allowed method's NAME it would not match the pin.
        var lambda = CallersOfTheOptOut(IlCallScan.WithNested(typeof(WriterOrderOptOutControlLambdaCaller))).Single();
        Assert.NotEqual(nameof(WriterOrderOptOutControlLambdaCaller.OwnershipContextOf), lambda.Method);
    }

    /// <summary>The detector: each (outermost type, source method) whose compiled body references the opt-out.</summary>
    private static IReadOnlyList<(Type Type, string Method)> CallersOfTheOptOut(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(RecordOwnershipContext)
                        && r.Target.Name == nameof(RecordOwnershipContext.ForParentsInWriterOrder))
            .Select(r => (IlCallScan.Outermost(r.Caller), IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)))
            .ToArray();
}

// Compiled control fixtures for WriterOrderOptOutAllowListTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: asks in its own order directly.</summary>
internal static class WriterOrderOptOutControlDirectCaller
{
    internal static RecordOwnershipContext Ask(Guid matterId) =>
        RecordOwnershipContext.ForParentsInWriterOrder(new RecordOwnershipParent?[] { new("sprk_matter", matterId) });
}

/// <summary>MUST FIRE: asks in its own order inside a lambda — in a method named like an allowed one.</summary>
internal static class WriterOrderOptOutControlLambdaCaller
{
    internal static RecordOwnershipContext OwnershipContextOf(Guid matterId)
    {
        Func<RecordOwnershipContext> ask = () =>
            RecordOwnershipContext.ForParentsInWriterOrder(new RecordOwnershipParent?[] { new("sprk_matter", matterId) });
        return ask();
    }
}

/// <summary>MUST NOT FIRE: asks through the one builder.</summary>
internal static class WriterOrderOptOutControlForRowWriter
{
    internal static RecordOwnershipContext Ask(Guid matterId) =>
        RecordOwnershipContext.ForRow(
            "sprk_event",
            new[] { KeyValuePair.Create("sprk_regardingmatter", (RecordOwnershipParent?)new RecordOwnershipParent("sprk_matter", matterId)) });
}

/// <summary>MUST NOT FIRE: only names the opt-out (this comment calls ForParentsInWriterOrder; the code does not).</summary>
internal static class WriterOrderOptOutControlNameOnly
{
    internal static string Name() => nameof(RecordOwnershipContext.ForParentsInWriterOrder);
}
