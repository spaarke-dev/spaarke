using Sprk.Bff.Api.Services.Finance.Procedures;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 084e fix round 2 (ontology-r1, 2026-11-12): <see cref="PolicyVersionValidator.TryPrepareBillingRuleForRead"/> is the billing gate's
/// display-only entry (no Error log, no <c>billing.rule.invalid</c> meter). If an evaluation path called it, a refused rule would silently lose its
/// FR-24 log and meter. So it may be called only from the record read models under <c>Sprk.Bff.Api.Services.Finance.Procedures</c>; every
/// evaluation calls <see cref="PolicyVersionValidator.TryPrepareBillingRule"/>. Enforced by: this guard (an IL scan of the BFF assembly, in CI and the
/// Stop hook), with a must-fire control (a fixture caller in another namespace) and a must-not-fire control (the read service's own call is seen and
/// allowed). Not covered: a call through reflection or a delegate built at run time.
/// </summary>
public sealed class BillingGateForReadCallerGuardTests
{
    private const string AllowedNamespace = "Sprk.Bff.Api.Services.Finance.Procedures";

    private static IReadOnlyList<string> ForReadCallersOutsideTheReadModels(IEnumerable<Type> types) =>
        ForReadCallers(types)
            .Where(c => !InAllowedNamespace(c.Caller))
            .Select(c => $"{c.Caller.FullName}.{c.Method}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<(Type Caller, string Method)> ForReadCallers(IEnumerable<Type> types) =>
        IlCallScan.MethodReferences(types)
            .Where(r => r.Target.DeclaringType == typeof(PolicyVersionValidator)
                        && r.Target.Name == nameof(PolicyVersionValidator.TryPrepareBillingRuleForRead))
            .Select(r => (IlCallScan.Outermost(r.Caller), IlCallScan.SourceMethodName(r.Caller, r.CallerMethod)));

    private static bool InAllowedNamespace(Type type) =>
        type.Namespace is { } ns && (ns == AllowedNamespace || ns.StartsWith(AllowedNamespace + ".", StringComparison.Ordinal));

    [Fact(DisplayName = "084e: the billing gate's read entry (no log, no meter) is called only from the record read models, never from an evaluation path")]
    public void TryPrepareBillingRuleForRead_IsCalledOnlyFromTheRecordReadModels()
    {
        var offenders = ForReadCallersOutsideTheReadModels(typeof(Program).Assembly.GetTypes());

        Assert.True(offenders.Count == 0,
            "PolicyVersionValidator.TryPrepareBillingRuleForRead is for read-side display only; an evaluation must call TryPrepareBillingRule so a "
            + "refused rule is logged and metered (FR-24). Callers outside " + AllowedNamespace + ":\n" + string.Join("\n", offenders));
    }

    [Fact(DisplayName = "084e guard (must-not-fire): the rules-in-force read service's own call is seen and allowed")]
    public void TheReadServiceCall_IsSeen_AndAllowed()
    {
        var callers = ForReadCallers(typeof(Program).Assembly.GetTypes()).ToList();

        Assert.Contains(callers, c => c.Caller == typeof(RecordProcedureReadService));
        Assert.All(callers, c => Assert.True(InAllowedNamespace(c.Caller), $"{c.Caller.FullName}.{c.Method}"));
    }

    [Fact(DisplayName = "084e guard (must-fire): a caller outside the read models, such as an evaluation under Services/Signals, fails the guard")]
    public void ACallerOutsideTheReadModels_Fails()
    {
        var offenders = ForReadCallersOutsideTheReadModels([typeof(EvaluationThatCallsTheReadGate)]);

        Assert.Equal([$"{typeof(EvaluationThatCallsTheReadGate).FullName}.{nameof(EvaluationThatCallsTheReadGate.Judge)}"], offenders);
    }
}

/// <summary>
/// Must-fire fixture for <see cref="BillingGateForReadCallerGuardTests"/>: an "evaluation" outside the read models (here the ArchTests namespace,
/// standing in for Services/Signals) that wrongly calls the read entry.
/// </summary>
internal static class EvaluationThatCallsTheReadGate
{
    internal static bool Judge(PolicyVersionValidator gate, PolicyVersionSnapshot pv) => gate.TryPrepareBillingRuleForRead(pv, out _);
}
