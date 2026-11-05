using System.Reflection;
using System.Reflection.Emit;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ebilling-r1 082c-2 (brief 082c C16; uac-r2 condition 3, 2026-11-04): no <c>ex.Message</c> and no <c>ToString()</c> of a Procedure resolver
/// record reaches a reader surface. <see cref="ProcedureResolutionFaultException"/>'s message names row, Procedure and rule ids for operators,
/// and a resolver record's text is internal basis; a reader text is built from codes, names and <c>ProcedureRulesOnDate.ForReaders()</c> only.
/// </summary>
/// <remarks>
/// <para><b>The signal (IL).</b> In the source methods of <see cref="ReaderSurfaces"/> (lambdas and state machines count for the method they
/// were written in): a reference to <c>get_Message</c> of any <see cref="Exception"/> type; a <c>ToString</c> declared by a type of
/// <see cref="ResolverRecords"/>; or a generic method (string interpolation's <c>AppendFormatted&lt;T&gt;</c>, <c>string.Join&lt;T&gt;</c>) whose
/// type argument is one of them.</para>
/// <para><b>Limits.</b> A record passed as <c>object</c> (<c>string.Concat(object)</c>, <c>string.Format</c>) is not seen in IL without data flow;
/// the records' <c>PrintMembers</c> (082c C17, each with a test that its text holds no id) is the second line for that case. Logs are not
/// surfaces: they carry the type name (as <c>JobHandler.cs:208</c>).</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the rule over <see cref="ProcedureReaderRedactionControl"/>.</para>
/// </remarks>
public class ProcedureReaderRedactionGuardTests
{
    /// <summary>The reader surfaces of brief C16, by the type that builds each (the 202 body and the status view: the submission service and its
    /// contracts; the findings JSON and plan: the persister; Signals: the after-steps, the mapper and the system findings; the pipeline findings:
    /// the processor; the review read model; admin attention; Decision Record facts; the submitter notification; correction and adjustment
    /// responses; the approval refusal).</summary>
    internal static readonly Type[] ReaderSurfaces =
    [
        typeof(InvoiceSubmissionProcessor),
        typeof(InvoiceSubmissionPersister),
        typeof(InvoiceSubmissionAfterSteps),
        typeof(BillingSystemFindings),
        typeof(BillingFindingSignalMapper),
        typeof(InvoiceSubmissionService),
        typeof(SubmissionHoldWarning),
        typeof(InvoiceProcedureHold),
        typeof(AdminAttentionText),
        typeof(SubmissionOutcomeNotifier),
        typeof(BillingDecisionRecorder),
        typeof(InvoiceReviewService),
        typeof(InvoiceCorrectionReevaluation),
        typeof(InvoiceCorrectionService),
        typeof(InvoiceAdjustmentService),
        typeof(InvoiceApprovalService),
    ];

    /// <summary>The resolver records whose text is internal basis (082b / 082c C17).</summary>
    internal static readonly Type[] ResolverRecords =
    [
        typeof(ProcedureGap), typeof(BillingProcedureGap), typeof(ResolvedRule), typeof(ResolvedPolicyVersion), typeof(ExcludedRule),
        typeof(ExcludedProcedure), typeof(AppliedProcedure), typeof(RecordRuleSlot), typeof(ProcedureOverrideRow), typeof(CoreRecordLink),
        typeof(ProcedureRulesOnDate), typeof(ProcedureResolution),
    ];

    [Fact(DisplayName = "082c-2 C16: no ex.Message and no resolver-record ToString() on a reader surface")]
    public void ReaderSurfaces_UseNoExceptionMessageAndNoResolverRecordText()
    {
        var problems = Violations(ReaderSurfaces.SelectMany(IlCallScan.WithNested).Distinct());

        Assert.True(problems.Count == 0,
            "A reader surface carries no ex.Message and no ToString() of a Procedure resolver record (brief 082c C16; build reader text from " +
            "codes, names and ForReaders()):\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "082c-2 C16 control: ex.Message, a record's ToString() and its interpolation fire; a name, a code and the type name do not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var problems = Violations(IlCallScan.WithNested(typeof(ProcedureReaderRedactionControl)));
        var name = typeof(ProcedureReaderRedactionControl).FullName;

        Assert.Contains(problems, p => p.Contains($"{name}::UsesExMessage", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains($"{name}::PrintsAGap", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains($"{name}::InterpolatesARule", StringComparison.Ordinal));
        Assert.DoesNotContain(problems, p => p.Contains("UsesTheName", StringComparison.Ordinal) || p.Contains("UsesTheTypeName", StringComparison.Ordinal));
        Assert.True(problems.Count == 3, string.Join(Environment.NewLine, problems));
    }

    private static IReadOnlyList<string> Violations(IEnumerable<Type> types)
    {
        var records = ResolverRecords.ToHashSet();
        var problems = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            foreach (var body in IlCallScan.DeclaredBodies(type))
            {
                var instructions = IlCallScan.Instructions(body);
                for (var i = 0; i < instructions.Count; i++)
                {
                    if (instructions[i].OpCode.OperandType != OperandType.InlineMethod
                        || IlCallScan.Resolve(body, instructions[i].Operand, (m, t, ta, ma) => m.ResolveMethod(t, ta, ma)) is not { } target)
                    {
                        continue;
                    }

                    var reason = target switch
                    {
                        { Name: "get_Message", DeclaringType: { } t } when typeof(Exception).IsAssignableFrom(t) => "reads an exception's Message",
                        { Name: "ToString", DeclaringType: { } t } when records.Contains(t) => $"prints {t.Name}.ToString()",

                        // A virtual call binds to object.ToString(): the receiver is the value the previous instruction loaded.
                        { Name: "ToString", DeclaringType: { } t } when t == typeof(object) && i > 0
                            && ReceiverType(body, instructions[i - 1]) is { } receiver && records.Contains(receiver)
                            => $"prints {receiver.Name}.ToString()",
                        MethodInfo { IsGenericMethod: true, Name: "AppendFormatted" or "Join" or "Format" or "Concat" } generic
                            when generic.GetGenericArguments().Any(records.Contains)
                            => $"formats a {generic.GetGenericArguments().First(records.Contains).Name} through {generic.Name}",
                        _ => null,
                    };
                    if (reason is not null)
                    {
                        problems.Add($"{ExternalDocumentNarrowingGuardTests.SourceKey(type, body)}: {reason}");
                    }
                }
            }
        }

        return problems.ToList();
    }

    /// <summary>The static type of the value <paramref name="load"/> pushes, for the loads a receiver usually comes from (an argument, a local,
    /// a field, a call's result); null otherwise.</summary>
    private static Type? ReceiverType(MethodBase body, IlCallScan.IlInstruction load)
    {
        var op = load.OpCode;
        var parameters = body.GetParameters();
        var offset = body.IsStatic ? 0 : 1;
        Type? Arg(int index) => index - offset is var p && p >= 0 && p < parameters.Length ? parameters[p].ParameterType
            : index == 0 && !body.IsStatic ? body.DeclaringType : null;
        Type? Local(int index) => body.GetMethodBody()?.LocalVariables is { } locals && index < locals.Count ? locals[index].LocalType : null;

        if (op == OpCodes.Ldarg_0) return Arg(0);
        if (op == OpCodes.Ldarg_1) return Arg(1);
        if (op == OpCodes.Ldarg_2) return Arg(2);
        if (op == OpCodes.Ldarg_3) return Arg(3);
        if (op == OpCodes.Ldarg_S || op == OpCodes.Ldarg) return Arg(load.Operand);
        if (op == OpCodes.Ldloc_0) return Local(0);
        if (op == OpCodes.Ldloc_1) return Local(1);
        if (op == OpCodes.Ldloc_2) return Local(2);
        if (op == OpCodes.Ldloc_3) return Local(3);
        if (op == OpCodes.Ldloc_S || op == OpCodes.Ldloc) return Local(load.Operand);
        if (op == OpCodes.Ldfld || op == OpCodes.Ldsfld)
        {
            return IlCallScan.Resolve(body, load.Operand, (m, t, ta, ma) => m.ResolveField(t, ta, ma))?.FieldType;
        }

        if (op == OpCodes.Call || op == OpCodes.Callvirt)
        {
            return (IlCallScan.Resolve(body, load.Operand, (m, t, ta, ma) => m.ResolveMethod(t, ta, ma)) as MethodInfo)?.ReturnType;
        }

        return null;
    }
}

/// <summary>The control fixture of <see cref="ProcedureReaderRedactionGuardTests"/> (never run; its IL is scanned).</summary>
internal static class ProcedureReaderRedactionControl
{
    // Must fire.
    internal static string UsesExMessage(Exception ex) => "failed: " + ex.Message;

    internal static string PrintsAGap(ProcedureGap gap) => gap.ToString();

    internal static string InterpolatesARule(ResolvedRule rule) => $"rule {rule}";

    // Must not fire.
    internal static string UsesTheName(ProcedureGap gap) => gap.ProcedureName + " " + gap.Kind.ToString();

    internal static string UsesTheTypeName(Exception ex) => ex.GetType().Name;
}
