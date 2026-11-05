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
/// <see cref="ResolverRecords"/> or by an <see cref="Exception"/> type (or <c>object.ToString</c> on a receiver of either); or a generic method
/// (string interpolation's <c>AppendFormatted&lt;T&gt;</c>, with or without an alignment or a format specifier, and <c>string.Join&lt;T&gt;</c>)
/// whose type argument is a resolver record or an <see cref="Exception"/>. 082c-2 fix round 1 (F3): an exception's full text names its inner
/// exceptions, its stack and the ids in <see cref="ProcedureResolutionFaultException"/>'s message, so <c>ToString()</c> and interpolation of
/// an exception are the same leak as <c>Message</c>.</para>
/// <para><b>The string operator <c>+</c></b> lowers <c>"x" + ex</c> to <c>ex?.ToString()</c> (a null-guarded <c>object.ToString</c>), which is seen
/// as a <c>ToString</c> on the receiver. <b>Non-generic <c>Concat</c> / <c>Format</c> / <c>Join</c>.</b> A resolver record or an exception passed as
/// <c>object</c> (<c>string.Format("{0}", ex)</c>) is seen when its load is one of the arguments of the call: a load of an argument, local,
/// field or call result whose static type is one of those, in the statement that makes the call (the scan stops at a store, a branch or another
/// statement boundary), and that is not itself the receiver of a further call or field read (<c>ex.GetType().Name</c> is not a hit).</para>
/// <para><b>Limits.</b> A value whose static type is already <c>object</c> (assigned to an <c>object</c> local or parameter first) is not seen in
/// IL without data flow; the records' <c>PrintMembers</c> (082c C17, each with a test that its text holds no id) is the second line for a
/// record, and a reader surface that catches an exception keeps the typed exception in hand. Logs are not surfaces: they carry the type name
/// (as <c>JobHandler.cs:208</c>).</para>
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

    [Fact(DisplayName = "082c-2 C16 control: ex.Message, an exception's or a record's ToString(), interpolation, Concat and Format fire; a name, a code and the type name do not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var problems = Violations(IlCallScan.WithNested(typeof(ProcedureReaderRedactionControl)));
        var name = typeof(ProcedureReaderRedactionControl).FullName;

        string[] mustFire =
        [
            nameof(ProcedureReaderRedactionControl.UsesExMessage), nameof(ProcedureReaderRedactionControl.PrintsAGap),
            nameof(ProcedureReaderRedactionControl.InterpolatesARule),
            // 082c-2 fix round 1 (F3): the exception's full text.
            nameof(ProcedureReaderRedactionControl.InterpolatesAnException), nameof(ProcedureReaderRedactionControl.InterpolatesAnExceptionWithAFormat),
            nameof(ProcedureReaderRedactionControl.InterpolatesAnExceptionWithAnAlignment), nameof(ProcedureReaderRedactionControl.PrintsAnException),
            nameof(ProcedureReaderRedactionControl.PrintsADerivedException), nameof(ProcedureReaderRedactionControl.ConcatsAnException),
            nameof(ProcedureReaderRedactionControl.ConcatsAnExceptionBetweenStrings), nameof(ProcedureReaderRedactionControl.FormatsAnException),
            nameof(ProcedureReaderRedactionControl.JoinsExceptions), nameof(ProcedureReaderRedactionControl.ConcatsARule),
            nameof(ProcedureReaderRedactionControl.FormatsARule),
        ];
        var missed = mustFire.Where(method => !problems.Any(p => p.Contains($"{name}::{method}", StringComparison.Ordinal))).ToList();
        Assert.True(missed.Count == 0, $"The detector missed: {string.Join(", ", missed)}. It found:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");

        // Must not fire: a name, a code, the type name, and a call on the exception that yields a code or name.
        string[] mustNotFire =
        [
            nameof(ProcedureReaderRedactionControl.UsesTheName), nameof(ProcedureReaderRedactionControl.UsesTheTypeName),
            nameof(ProcedureReaderRedactionControl.ConcatsTheTypeName), nameof(ProcedureReaderRedactionControl.InterpolatesTheTypeName),
            nameof(ProcedureReaderRedactionControl.FormatsTheTypeName), nameof(ProcedureReaderRedactionControl.ConcatsAnIntAndAString),
        ];
        var falseHits = mustNotFire.Where(method => problems.Any(p => p.Contains($"{name}::{method}", StringComparison.Ordinal))).ToList();
        Assert.True(falseHits.Count == 0, $"The detector fired on: {string.Join(", ", falseHits)}. It found:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");

        Assert.True(problems.Count == mustFire.Length, string.Join(Environment.NewLine, problems));
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

                        // 082c-2 fix round 1 (F3): an exception's full text (inner exceptions, stack, the ids of a fault's message).
                        { Name: "ToString", DeclaringType: { } t } when typeof(Exception).IsAssignableFrom(t) => $"prints {t.Name}.ToString()",

                        // A virtual call binds to object.ToString(): the receiver is the value the previous instruction loaded; the string
                        // operator + lowers `"x" + value` to `value?.ToString()` (load, dup, brtrue, pop, ldnull, br, then the call).
                        { Name: "ToString", DeclaringType: { } t } when t == typeof(object) && ToStringReceiver(body, instructions, i) is { } receiver
                            && IsLeaky(receiver, records)
                            => $"prints {receiver.Name}.ToString()",
                        MethodInfo { IsGenericMethod: true, Name: "AppendFormatted" or "Join" or "Format" or "Concat" } generic
                            when generic.GetGenericArguments().Any(a => IsLeaky(a, records))
                            => $"formats a {generic.GetGenericArguments().First(a => IsLeaky(a, records)).Name} through {generic.Name}",

                        // Not generic: a record or an exception passed as object to Concat / Format / Join (the string operator +, string.Format).
                        { Name: "Concat" or "Format" or "Join", DeclaringType: { } t } when t == typeof(string)
                            && BareObjectArgument(body, instructions, i, records) is { } passed
                            => $"formats a {passed.Name} through string.{target.Name}",
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

    /// <summary>The static type of the receiver of the <c>object.ToString</c> call at <paramref name="callIndex"/>: that of the previous load, or of
    /// the load before the null-guard (<c>dup; brtrue; pop; ldnull; br</c>) the compiler emits for <c>"x" + value</c> and <c>value?.ToString()</c>.</summary>
    private static Type? ToStringReceiver(MethodBase body, IReadOnlyList<IlCallScan.IlInstruction> instructions, int callIndex)
    {
        if (callIndex <= 0)
        {
            return null;
        }

        if (ReceiverType(body, instructions[callIndex - 1]) is { } direct)
        {
            return direct;
        }

        return callIndex >= 6
            && instructions[callIndex - 1].OpCode.FlowControl == FlowControl.Branch
            && instructions[callIndex - 2].OpCode == OpCodes.Ldnull
            && instructions[callIndex - 3].OpCode == OpCodes.Pop
            && instructions[callIndex - 4].OpCode.FlowControl == FlowControl.Cond_Branch
            && instructions[callIndex - 5].OpCode == OpCodes.Dup
                ? ReceiverType(body, instructions[callIndex - 6])
                : null;
    }

    /// <summary>Whether text of a value of <paramref name="type"/> must not reach a reader: a resolver record or an exception.</summary>
    private static bool IsLeaky(Type type, HashSet<Type> records) => records.Contains(type) || typeof(Exception).IsAssignableFrom(type);

    /// <summary>
    /// For a non-generic <c>string.Concat</c> / <c>Format</c> / <c>Join</c> call at <paramref name="callIndex"/>: the static type of an argument that
    /// is a resolver record or an exception handed over as it is, or null. Walks back through the call's statement (at most 16 instructions, to a
    /// store, a branch or a return), reading each plain load; a load that the next instruction consumes as a receiver (<c>ex.GetType()</c>,
    /// <c>ex.Code</c>) is not an argument.
    /// </summary>
    private static Type? BareObjectArgument(MethodBase body, IReadOnlyList<IlCallScan.IlInstruction> instructions, int callIndex, HashSet<Type> records)
    {
        if (IlCallScan.Resolve(body, instructions[callIndex].Operand, (m, t, ta, ma) => m.ResolveMethod(t, ta, ma)) is not MethodInfo concat
            || !concat.GetParameters().Any(p => p.ParameterType == typeof(object) || p.ParameterType == typeof(object[])))
        {
            return null;
        }

        for (var j = callIndex - 1; j >= 0 && j >= callIndex - 16; j--)
        {
            var op = instructions[j].OpCode;
            if (op.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw
                || op == OpCodes.Stloc || op == OpCodes.Stloc_S || op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1 || op == OpCodes.Stloc_2 || op == OpCodes.Stloc_3
                || op == OpCodes.Stfld || op == OpCodes.Stsfld || op == OpCodes.Starg || op == OpCodes.Starg_S || op == OpCodes.Pop)
            {
                break;
            }

            // Only a plain load can be a bare argument (a call result was produced by the call that consumed its own receiver).
            if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj
                || ReceiverType(body, instructions[j]) is not { } type || !IsLeaky(type, records))
            {
                continue;
            }

            var next = j + 1 < callIndex ? instructions[j + 1].OpCode : default;
            var consumedAsReceiver = j + 1 < callIndex
                && (next.OperandType == OperandType.InlineMethod || next == OpCodes.Ldfld || next == OpCodes.Ldflda || next == OpCodes.Castclass || next == OpCodes.Isinst);
            if (!consumedAsReceiver)
            {
                return type;
            }
        }

        return null;
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

    // 082c-2 fix round 1 (F3): an exception's text is ex.Message plus its inner exceptions, stack and any ids in a fault's message.
    internal static string InterpolatesAnException(Exception ex) => $"failed: {ex}";

    internal static string InterpolatesAnExceptionWithAFormat(Exception ex) => $"failed: {ex:G}";

    internal static string InterpolatesAnExceptionWithAnAlignment(Exception ex) => $"failed: {ex,20}";

    internal static string PrintsAnException(Exception ex) => ex.ToString();

    internal static string PrintsADerivedException(ProcedureResolutionFaultException ex) => ex.ToString();

    internal static string ConcatsAnException(Exception ex) => "failed: " + ex;

    internal static string ConcatsAnExceptionBetweenStrings(string head, Exception ex, string tail) => head + ex + tail;

    internal static string FormatsAnException(Exception ex) => string.Format("failed: {0}", ex);

    internal static string JoinsExceptions(IEnumerable<Exception> faults) => string.Join("; ", faults);

    internal static string ConcatsARule(ResolvedRule rule) => "rule: " + rule;

    internal static string FormatsARule(ResolvedRule rule) => string.Format("rule: {0}", rule);

    // Must not fire.
    internal static string UsesTheName(ProcedureGap gap) => gap.ProcedureName + " " + gap.Kind.ToString();

    internal static string UsesTheTypeName(Exception ex) => ex.GetType().Name;

    internal static string ConcatsTheTypeName(Exception ex) => "failed: " + ex.GetType().Name;

    internal static string InterpolatesTheTypeName(Exception ex) => $"failed: {ex.GetType().Name}";

    internal static string FormatsTheTypeName(Exception ex) => string.Format("failed: {0}", ex.GetType().Name);

    internal static string ConcatsAnIntAndAString(string head, int count, Exception ex) => head + count + ex.GetType().Name;
}
