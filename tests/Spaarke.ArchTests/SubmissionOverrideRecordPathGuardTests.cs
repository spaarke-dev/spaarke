using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 task 114 (model 15 answer 3; D-100, D-101): the Override of a held submission, a Procedure-gap hold included, records its decision
/// through <c>BillingDecisionRecorder</c> and so <c>DecisionRecordWriter.WriteBillingDecisionAsync</c>, and no other Decision Record entry point.
/// <see cref="SubmissionOverrideService"/> must reference none of the writer's other create entry points: a new one would need ontology-r1's
/// owner review and a place in <c>DecisionRecordContextImmutabilityTests</c>' entry-point list (which also pins that the writer gains no public
/// member: <c>EveryPublicWriteResultMemberIsAListedEntryPoint</c>).
/// </summary>
/// <remarks>
/// <b>Enforced by this test</b> (CLAUDE.md section 16): over the compiled IL (<see cref="IlCallScan.MethodReferences"/>), so a call through a lambda,
/// a state machine or a method group is seen, whatever the receiver expression. <b>Not covered:</b> a method chosen at run time (reflection);
/// the scan's own limits are in <see cref="IlCallScan"/>.
/// </remarks>
public sealed class SubmissionOverrideRecordPathGuardTests
{
    /// <summary>The writer's create entry points the Override must not call itself. (WriteBillingDecisionAsync is reached through the recorder.)</summary>
    private static readonly string[] ForbiddenEntryPoints =
        ["WriteAsync", "WriteGateDecisionAsync", "WriteConditionClearedAsync", "WriteBillingDecisionAsync"];

    [Fact(DisplayName = "114: the Override service calls no Decision Record writer entry point itself (it records through BillingDecisionRecorder)")]
    public void TheOverrideServiceCallsNoWriterEntryPoint()
    {
        var calls = WriterEntryPointCalls(IlCallScan.WithNested(typeof(SubmissionOverrideService)));

        Assert.True(calls.Count == 0,
            "SubmissionOverrideService must record its decision through BillingDecisionRecorder (-> DecisionRecordWriter.WriteBillingDecisionAsync) "
            + $"and call no writer entry point itself; it calls: {string.Join(", ", calls)}.");
    }

    [Fact(DisplayName = "114 controls: the scan fires on a call to a writer entry point, and not on a method that calls none")]
    public void Controls()
    {
        Assert.Contains(
            $"{typeof(WriterEntryPointControlFixture).FullName}::{nameof(WriterEntryPointControlFixture.Clear)} -> WriteConditionClearedAsync",
            WriterEntryPointCalls(IlCallScan.WithNested(typeof(WriterEntryPointControlFixture))));
        Assert.Empty(WriterEntryPointCalls(IlCallScan.WithNested(typeof(WriterEntryPointUnrelatedControlFixture))));
    }

    private static List<string> WriterEntryPointCalls(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(DecisionRecordWriter) && ForbiddenEntryPoints.Contains(r.Target.Name, StringComparer.Ordinal))
            .Select(r => $"{IlCallScan.Outermost(r.Caller).FullName}::{IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)} -> {r.Target.Name}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}

/// <summary>Calls a writer entry point (must-fire). Never run; only its IL is read.</summary>
internal static class WriterEntryPointControlFixture
{
    public static Task<DecisionRecordWriteResult> Clear(DecisionRecordWriter writer) => writer.WriteConditionClearedAsync(null!, default);
}

/// <summary>Calls no writer entry point (must-not-fire).</summary>
internal static class WriterEntryPointUnrelatedControlFixture
{
    public static string Name() => "override-submission";
}
