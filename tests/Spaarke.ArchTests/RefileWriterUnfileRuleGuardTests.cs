using System.Reflection;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3b (owner, 2026-11-11: "every unfile path follows the same rule"). A re-file
/// writer that can unfile a <c>sprk_communication</c> takes the ONE unfile rule: it chooses what an unfile does through
/// <see cref="DirectMessageOwnerStep.UnfiledOwnershipFor"/> and sets that choice as <see cref="RecordReparent.WhenUnfiled"/>,
/// passes the typed regarding lookups its change sets through <see cref="RecordReparent.RegardingLinksSetIn{TValue}"/> and every
/// other column the rule reads that it writes through <see cref="RecordReparent.UnfileRuleColumnsWrittenIn{TValue}"/> (so the
/// rule judges the row as the change leaves it), and runs <see cref="DirectMessageOwnerStep.AfterReparentAsync"/> after the
/// write. <see cref="RecordReparent.WhenUnfiled"/> has a default, so a writer that sets none of these takes the default with no
/// error; this guard is what holds the rule.
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> The compiled method references (<see cref="IlCallScan"/>), attributed to the source METHOD that makes
/// them: a call inside a lambda, a local function or an async state machine counts for the method it is written in, and a
/// comment, a string or a <c>nameof</c> does not. Each method that calls <see cref="IRecordOwnershipResolver.ReparentAsync"/> is
/// a re-file; it must reference every required member itself or through a method of its own type that it calls (a helper, at
/// any depth), and the result of <c>UnfiledOwnershipFor</c> must be the value it sets as <c>WhenUnfiled</c>: the setter is the
/// IL instruction right after the call, and no branch lands on it. Every other <c>WhenUnfiled</c> set in such a method or its
/// helpers (a ternary that can pick another value, a later <c>with { WhenUnfiled = … }</c> overwrite, a constant) is reported.
/// A re-file METHOD that never unfiles a communication is listed in <see cref="NeverUnfiles"/> with its reason; another re-file
/// in the same type is still held to the rule. Which table a writer re-files is a runtime value, so every <c>ReparentAsync</c>
/// caller is held to the rule unless it is listed. Overloads that share a name are one method here.</para>
/// <para><b>Not listed because they are not callers.</b> <c>SecureChildReconciler</c> re-owns rows through its own writer
/// members, and <c>ReconciledEmailFiling</c> re-files through <see cref="IncomingAssociationResolver"/>; neither calls
/// <c>ReparentAsync</c>. The browser re-file routes (<c>ChildRecordEndpoints</c>, the communication filing route) and the chat
/// update tool re-file through <see cref="OwnedChildWrite.RefileAsync"/>, which is held here.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over the
/// <c>UnfileRuleControl*</c> fixtures below.</para>
/// </remarks>
public class RefileWriterUnfileRuleGuardTests
{
    private sealed record NeverUnfilesEntry(Type Type, string Method, string Reason);

    /// <summary>The <c>ReparentAsync</c> callers that never unfile a communication, by source method, each verified at its
    /// call.</summary>
    private static readonly IReadOnlyList<NeverUnfilesEntry> NeverUnfiles = new[]
    {
        new NeverUnfilesEntry(typeof(ThreadResolver), "JoinCoreAsync",
            "A message JOINING a thread: the change sets the thread lookup and clears no regarding lookup (KeepCreator). A Direct " +
            "join is owned by the join path's own step (DirectMessageOwnerStep.GrantThenOwnDirectAsync)."),
        new NeverUnfilesEntry(typeof(IncomingAssociationResolver), "ApplyDecisionAsync",
            "Inbound association: additive filings only, the engine never clears a sibling regarding (KeepCreator)."),
        new NeverUnfilesEntry(typeof(DataverseDocumentsEndpoints), nameof(DataverseDocumentsEndpoints.MapDataverseDocumentsEndpoints),
            "The document re-file route: re-files a sprk_document only (EntityLogicalName is the literal sprk_document), never a " +
            "communication."),
    };

    private const string UnfiledOwnershipForMember = $"{nameof(DirectMessageOwnerStep)}.{nameof(DirectMessageOwnerStep.UnfiledOwnershipFor)}";
    private const string WhenUnfiledSetter = $"{nameof(RecordReparent)}.set_{nameof(RecordReparent.WhenUnfiled)}";

    /// <summary>What a re-file missing the flow of the choice into <c>WhenUnfiled</c> is reported as.</summary>
    private const string FlowMissing = "WhenUnfiled set from UnfiledOwnershipFor";

    /// <summary>What a re-file that also sets <c>WhenUnfiled</c> from anything but the choice is reported as.</summary>
    private const string OtherValueSet = "WhenUnfiled set from nothing but UnfiledOwnershipFor";

    /// <summary>The members a re-file that may unfile a communication must reference.</summary>
    private static readonly string[] Required =
    [
        UnfiledOwnershipForMember,
        $"{nameof(RecordReparent)}.{nameof(RecordReparent.RegardingLinksSetIn)}",
        $"{nameof(RecordReparent)}.{nameof(RecordReparent.UnfileRuleColumnsWrittenIn)}",
        $"{nameof(DirectMessageOwnerStep)}.{nameof(DirectMessageOwnerStep.AfterReparentAsync)}",
    ];

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "fm 054 3b: every method that calls ReparentAsync takes the one unfile rule (UnfiledOwnershipFor into WhenUnfiled, RegardingLinksSetIn, UnfileRuleColumnsWrittenIn, AfterReparentAsync) or its type is listed")]
    public void EveryReparentCallerTakesTheOneUnfileRule()
    {
        var scan = Scan(BffTypes.Value);

        // Non-vacuity: the four unfile writers are seen re-filing AND taking the rule, in the method that re-files.
        foreach (var compliant in new[]
                 {
                     "Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite", "Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler",
                     "Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore",
                     "Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints",
                 })
        {
            var reparents = scan.Where(s => s.Key.Type.FullName == compliant && s.Value.Reparents).ToArray();
            Assert.True(
                reparents.Length > 0 && reparents.All(s => Missing(scan, s.Key).Length == 0),
                $"{compliant} should be seen re-filing AND taking the one unfile rule in that method — the scan lost its signal. " +
                "Seen: " + string.Join("; ", reparents.Select(s => $"{s.Key.Method}: missing {string.Join(",", Missing(scan, s.Key))}")));
        }

        var problems = Problems(scan, NeverUnfiles.Select(e => (e.Type, e.Method)));

        Assert.True(problems.Count == 0,
            "A re-file that can unfile a communication takes the one unfile rule in the method that re-files (or a helper of its " +
            "type it calls): WhenUnfiled set from DirectMessageOwnerStep.UnfiledOwnershipFor and from nothing else, " +
            "RegardingLinksSet from RecordReparent.RegardingLinksSetIn, UnfileRuleColumnsWritten from " +
            "RecordReparent.UnfileRuleColumnsWrittenIn, and DirectMessageOwnerStep.AfterReparentAsync after the write. Take it, or " +
            "list the method with the reason it never unfiles a communication:\n" + string.Join("\n", problems));
    }

    /// <summary>Every re-file method in <paramref name="scan"/> that is not exempt and misses the rule, and every exemption that
    /// names no re-file method.</summary>
    private static List<string> Problems(
        IReadOnlyDictionary<(Type Type, string Method), Use> scan, IEnumerable<(Type Type, string Method)> exempt)
    {
        var listed = exempt.ToHashSet();
        var problems = new List<string>();
        foreach (var (key, use) in scan.OrderBy(s => s.Key.Type.FullName, StringComparer.Ordinal).ThenBy(s => s.Key.Method, StringComparer.Ordinal))
        {
            if (!use.Reparents || listed.Contains(key) || Missing(scan, key) is not { Length: > 0 } missing)
                continue;
            problems.Add($"{key.Type.FullName}.{key.Method} calls ReparentAsync without {string.Join(", ", missing)}");
        }

        foreach (var (type, method) in listed)
        {
            if (!(scan.GetValueOrDefault((type, method)) is { Reparents: true }))
                problems.Add($"STALE entry {type.FullName}.{method}: it no longer calls ReparentAsync — remove it.");
        }

        return problems;
    }

    [Fact(DisplayName = "fm 054 3b: control — a re-file without the unfile rule is found per method; one with it, through a helper, or no re-file, is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var scan = Scan(new[]
            {
                typeof(UnfileRuleControlBypassingWriter), typeof(UnfileRuleControlCompliantWriter), typeof(UnfileRuleControlPlainWriter),
                typeof(UnfileRuleControlSecondBranchWriter), typeof(UnfileRuleControlHelperWriter),
                typeof(UnfileRuleControlDiscardedChoiceWriter),
            }
            .SelectMany(IlCallScan.WithNested));

        var bypassing = (typeof(UnfileRuleControlBypassingWriter), nameof(UnfileRuleControlBypassingWriter.WriteAsync));
        Assert.True(scan.GetValueOrDefault(bypassing) is { Reparents: true }
                    && Missing(scan, bypassing).ToHashSet().SetEquals(Required.Append(FlowMissing)),
            "must fire: re-files, names the members only in a comment and a nameof, and takes none of them");

        var compliant = (typeof(UnfileRuleControlCompliantWriter), nameof(UnfileRuleControlCompliantWriter.WriteAsync));
        Assert.True(scan.GetValueOrDefault(compliant) is { Reparents: true } && Missing(scan, compliant).Length == 0,
            "must not fire: takes all of them, the move inside a lambda. Missing: " + string.Join(",", Missing(scan, compliant)));

        Assert.True(!scan.Any(s => s.Key.Type == typeof(UnfileRuleControlPlainWriter) && s.Value.Reparents),
            "must not fire: re-files nothing");

        var secondBranch = (typeof(UnfileRuleControlSecondBranchWriter), nameof(UnfileRuleControlSecondBranchWriter.WriteOtherAsync));
        Assert.True(scan.GetValueOrDefault(secondBranch) is { Reparents: true }
                    && Missing(scan, secondBranch).ToHashSet().SetEquals(Required.Append(FlowMissing)),
            "must fire: a second re-file in a type whose first re-file takes the rule, that takes none of it itself");
        var firstBranch = (typeof(UnfileRuleControlSecondBranchWriter), nameof(UnfileRuleControlSecondBranchWriter.WriteAsync));
        Assert.True(Missing(scan, firstBranch).Length == 0, "must not fire: the compliant first re-file of the same type");

        var helper = (typeof(UnfileRuleControlHelperWriter), nameof(UnfileRuleControlHelperWriter.WriteAsync));
        Assert.True(scan.GetValueOrDefault(helper) is { Reparents: true } && Missing(scan, helper).Length == 0,
            "must not fire: the re-file takes the rule through helpers of its own type. Missing: " + string.Join(",", Missing(scan, helper)));

        var discarded = (typeof(UnfileRuleControlDiscardedChoiceWriter), nameof(UnfileRuleControlDiscardedChoiceWriter.WriteAsync));
        Assert.True(Missing(scan, discarded).SequenceEqual([FlowMissing]),
            "must fire: references every member but never sets the choice as WhenUnfiled. Missing: " +
            string.Join(",", Missing(scan, discarded)));
    }

    [Fact(DisplayName = "fm 054 3b pass 3: control — WhenUnfiled set from a ternary or overwritten later is found; the choice set alone is not")]
    public void Detector_WhenUnfiledFromAnythingButTheChoice_MustFire()
    {
        var scan = Scan(new[]
            {
                typeof(UnfileRuleControlTernaryChoiceWriter), typeof(UnfileRuleControlOverwrittenChoiceWriter),
                typeof(UnfileRuleControlCompliantWriter),
            }
            .SelectMany(IlCallScan.WithNested));

        var ternary = (typeof(UnfileRuleControlTernaryChoiceWriter), nameof(UnfileRuleControlTernaryChoiceWriter.WriteAsync));
        Assert.True(scan.GetValueOrDefault(ternary) is { Reparents: true } && Missing(scan, ternary).Contains(OtherValueSet),
            "must fire: a ternary can set another value. Missing: " + string.Join(",", Missing(scan, ternary)));

        var overwritten = (typeof(UnfileRuleControlOverwrittenChoiceWriter), nameof(UnfileRuleControlOverwrittenChoiceWriter.WriteAsync));
        Assert.True(scan.GetValueOrDefault(overwritten) is { Reparents: true } && Missing(scan, overwritten).SequenceEqual([OtherValueSet]),
            "must fire: the choice is set, then overwritten with another value. Missing: " + string.Join(",", Missing(scan, overwritten)));

        var compliant = (typeof(UnfileRuleControlCompliantWriter), nameof(UnfileRuleControlCompliantWriter.WriteAsync));
        Assert.True(Missing(scan, compliant).Length == 0, "must not fire: the choice set once, alone");
    }

    [Fact(DisplayName = "fm 054 3b pass 3: control — an exemption covers its own method only; another re-file of the same type is held, and a stale entry is reported")]
    public void Detector_ExemptionsArePerMethod()
    {
        var scan = Scan(IlCallScan.WithNested(typeof(UnfileRuleControlExemptTypeWriter)));
        var type = typeof(UnfileRuleControlExemptTypeWriter);

        var problems = Problems(scan, [(type, nameof(UnfileRuleControlExemptTypeWriter.JoinAsync))]);
        Assert.True(
            problems.Count == 1 && problems[0].Contains($".{nameof(UnfileRuleControlExemptTypeWriter.UnfileAsync)} calls ReparentAsync", StringComparison.Ordinal),
            "must fire on the new re-file of an exempt type, not on the exempt one: " + string.Join(" | ", problems));

        var stale = Problems(scan, [(type, nameof(UnfileRuleControlExemptTypeWriter.JoinAsync)), (type, nameof(UnfileRuleControlExemptTypeWriter.UnfileAsync)), (type, "NoSuchMethod")]);
        Assert.True(stale.Count == 1 && stale[0].StartsWith("STALE entry", StringComparison.Ordinal),
            "must fire: an entry naming no re-file method is stale: " + string.Join(" | ", stale));
    }

    // ── uac-r2 task 197a (#2722): the resolver side of the one unfile rule ─────────────────────────────────────────────

    private const string ResolverNonDirectAnswer = "NonDirectUnfileAnswerAsync";
    private const string ResolverFactsReader = "ReadUnfiledFactsAsync";

    [Fact(DisplayName = "uac-r2 197a: an unfiled non-Direct communication's owner is decided in ONE resolver member, from facts made in ONE member, through the one creator-team path; every re-derivation after the write carries the facts")]
    public void TheNonDirectUnfileOwner_HasOneDecisionPoint_AndTheSettleCarriesItsFacts()
    {
        var bff = BffTypes.Value;
        var facts = typeof(UnfiledCommunicationFacts);

        // The creator is read from the facts by the decision point alone (never a second creator lookup).
        var creatorReaders = SourceMethodsReferencing(bff, m => m.DeclaringType == facts && m.Name == $"get_{nameof(UnfiledCommunicationFacts.CreatorSystemUserId)}", facts);
        Assert.Equal([(typeof(RecordOwnershipResolver), ResolverNonDirectAnswer)], creatorReaders);

        // The facts are made in one member: what the re-file read before its change.
        var makers = SourceMethodsReferencing(bff, m => m.DeclaringType == facts
            && (m.IsConstructor || m.Name is nameof(UnfiledCommunicationFacts.Keep) or nameof(UnfiledCommunicationFacts.ToCreator)), facts);
        Assert.Equal([(typeof(RecordOwnershipResolver), ResolverFactsReader)], makers);

        // The decision point takes the creator's team from the resolver's one creator-team path (191c included).
        var decision = MembersReferencedBy(typeof(RecordOwnershipResolver), ResolverNonDirectAnswer);
        Assert.Contains($"{nameof(RecordOwnershipResolver)}.ResolveFromUserAsync", decision);

        // Every re-derivation after the write (the settle and the check of its unconditional Secure write) passes the facts.
        foreach (var settle in new[] { "SettleOwnerAsync", "ConfirmUnconditionalSecureWriteAsync" })
        {
            var members = MembersReferencedBy(typeof(RecordOwnershipResolver), settle);
            Assert.True(members.Contains($"{nameof(RecordOwnershipResolver)}.ResolveReparentOwnerAsync") && members.Contains("RowBeforeChange.get_Unfiled"),
                $"{settle} re-derives the owner without the unfile facts it was decided from: " + string.Join(", ", members.Order()));
        }
    }

    [Fact(DisplayName = "uac-r2 197a: control — a second reader of the unfile creator is found; one reader alone is not")]
    public void UnfileFactsReaderDetector_MustFireAndMustNotFire()
    {
        var facts = typeof(UnfiledCommunicationFacts);
        bool IsCreator(MethodBase m) => m.DeclaringType == facts && m.Name == $"get_{nameof(UnfiledCommunicationFacts.CreatorSystemUserId)}";

        var two = SourceMethodsReferencing(IlCallScan.WithNested(typeof(UnfileFactsControlTwoReaders)), IsCreator, facts);
        Assert.True(two.Count == 2, "must fire: two members read the creator. Seen: " + string.Join(", ", two));

        var one = SourceMethodsReferencing(IlCallScan.WithNested(typeof(UnfileFactsControlOneReader)), IsCreator, facts);
        Assert.True(one.Count == 1 && one[0].Method == nameof(UnfileFactsControlOneReader.DecideAsync),
            "must not fire: one reader, through a lambda attributed to its method. Seen: " + string.Join(", ", one));
    }

    /// <summary>Every (outermost type, source method) in <paramref name="types"/> outside <paramref name="except"/> that references a
    /// method <paramref name="target"/> matches, distinct and ordered.</summary>
    private static List<(Type Type, string Method)> SourceMethodsReferencing(
        IEnumerable<Type> types, Func<MethodBase, bool> target, Type except) =>
        IlCallScan.MethodReferences(types)
            .Where(r => IlCallScan.Outermost(r.Caller) != except && target(r.Target))
            .Select(r => (IlCallScan.Outermost(r.Caller), SourceMethodOf(r.Caller, r.CallerMethod)))
            .Distinct()
            .OrderBy(r => r.Item1.FullName, StringComparer.Ordinal).ThenBy(r => r.Item2, StringComparer.Ordinal)
            .ToList();

    /// <summary>Every member (<c>Type.Name</c>) the source method <paramref name="method"/> of <paramref name="type"/> references,
    /// its lambdas, local functions and state machines included.</summary>
    private static HashSet<string> MembersReferencedBy(Type type, string method) =>
        IlCallScan.MethodReferences(IlCallScan.WithNested(type))
            .Where(r => SourceMethodOf(r.Caller, r.CallerMethod) == method)
            .Select(r => MemberOf(r.Target))
            .ToHashSet(StringComparer.Ordinal);

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Use
    {
        public bool Reparents { get; set; }
        public bool ChoiceSetAsWhenUnfiled { get; set; }
        public bool OtherValueSetAsWhenUnfiled { get; set; }
        public HashSet<string> Members { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Helpers { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>What the re-file at <paramref name="key"/> is missing, over it and every method of its type it calls.</summary>
    private static string[] Missing(IReadOnlyDictionary<(Type Type, string Method), Use> scan, (Type Type, string Method) key)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        var flows = false;
        var otherValue = false;
        var pending = new Stack<string>([key.Method]);
        while (pending.TryPop(out var method))
        {
            if (!seen.Add(method) || !scan.TryGetValue((key.Type, method), out var use))
                continue;
            members.UnionWith(use.Members);
            flows |= use.ChoiceSetAsWhenUnfiled;
            otherValue |= use.OtherValueSetAsWhenUnfiled;
            foreach (var helper in use.Helpers)
                pending.Push(helper);
        }

        var missing = Required.Where(r => !members.Contains(r)).ToList();
        if (!flows)
            missing.Add(FlowMissing);
        if (otherValue)
            missing.Add(OtherValueSet);
        return missing.ToArray();
    }

    private static Dictionary<(Type Type, string Method), Use> Scan(IEnumerable<Type> types)
    {
        var all = types.ToArray();

        // Only the types that reference ReparentAsync or a required member are read method by method.
        var relevant = IlCallScan.MethodReferences(all)
            .Where(r => IsReparent(r.Target, IlCallScan.Outermost(r.Caller)) || IsRequired(r.Target))
            .Select(r => IlCallScan.Outermost(r.Caller))
            .ToHashSet();

        var uses = new Dictionary<(Type Type, string Method), Use>();
        foreach (var type in all.Where(t => relevant.Contains(IlCallScan.Outermost(t))))
        {
            var owner = IlCallScan.Outermost(type);
            foreach (var body in IlCallScan.DeclaredBodies(type))
            {
                var key = (owner, SourceMethodOf(type, body));
                if (!uses.TryGetValue(key, out var use))
                    uses[key] = use = new Use();

                foreach (var target in IlCallScan.ReferencedMethods(body))
                {
                    use.Reparents |= IsReparent(target, owner);
                    if (IsRequired(target))
                        use.Members.Add(MemberOf(target));

                    if (target.DeclaringType is { } declaring && IlCallScan.Outermost(declaring) == owner)
                    {
                        var helper = SourceMethodOf(declaring, target);
                        if (helper != key.Item2)
                            use.Helpers.Add(helper);
                    }
                }

                // Every WhenUnfiled set, by instruction: from the choice only when the call to UnfiledOwnershipFor is the
                // instruction right before it and no branch lands on the set (a ternary or a conditional overwrite can).
                var instructions = IlCallScan.Instructions(body);
                var branchTargets = BranchTargets(instructions);
                for (var i = 0; i < instructions.Count; i++)
                {
                    if (MethodCalled(body, instructions[i]) is not { } called
                        || called.DeclaringType != typeof(RecordReparent) || MemberOf(called) != WhenUnfiledSetter)
                        continue;

                    var fromChoice = i > 0
                        && !branchTargets.Contains(instructions[i].Offset)
                        && MethodCalled(body, instructions[i - 1]) is { } previous
                        && previous.DeclaringType == typeof(DirectMessageOwnerStep) && MemberOf(previous) == UnfiledOwnershipForMember;
                    if (fromChoice)
                        use.ChoiceSetAsWhenUnfiled = true;
                    else
                        use.OtherValueSetAsWhenUnfiled = true;
                }
            }
        }

        return uses;
    }

    /// <summary>The method a call instruction calls, or <c>null</c> for any other instruction.</summary>
    private static MethodBase? MethodCalled(MethodBase body, IlCallScan.IlInstruction instruction) =>
        instruction.OpCode.OperandType == System.Reflection.Emit.OperandType.InlineMethod
        && (instruction.OpCode == System.Reflection.Emit.OpCodes.Call || instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt)
            ? IlCallScan.Resolve(body, instruction.Operand, (module, token, ta, ma) => module.ResolveMethod(token, ta, ma))
            : null;

    /// <summary>Every offset a branch, a conditional branch or a <c>switch</c> in the body can land on.</summary>
    private static HashSet<int> BranchTargets(IReadOnlyList<IlCallScan.IlInstruction> instructions)
    {
        var targets = new HashSet<int>();
        foreach (var instruction in instructions)
        {
            if (instruction.OpCode.OperandType is System.Reflection.Emit.OperandType.InlineBrTarget
                or System.Reflection.Emit.OperandType.ShortInlineBrTarget)
            {
                targets.Add(instruction.Operand);
            }

            if (instruction.Targets is { } cases)
                targets.UnionWith(cases);
        }

        return targets;
    }

    private static bool IsReparent(MethodBase target, Type owner) =>
        target.Name == nameof(IRecordOwnershipResolver.ReparentAsync)
        && target.DeclaringType is { } declaring && typeof(IRecordOwnershipResolver).IsAssignableFrom(declaring)
        && declaring != owner;

    private static bool IsRequired(MethodBase target) =>
        (target.DeclaringType == typeof(DirectMessageOwnerStep) || target.DeclaringType == typeof(RecordReparent))
        && Required.Contains(MemberOf(target), StringComparer.Ordinal);

    private static string MemberOf(MethodBase target) => $"{target.DeclaringType?.Name}.{target.Name}";

    /// <summary>
    /// The source-level method a compiled body is written in: the method itself; for an async or iterator state machine the
    /// method it was built from (<see cref="IlCallScan.SourceMethod"/>); for a lambda, a local function or a state machine of one,
    /// the method named inside the compiler's angle brackets (<c>&lt;RefileAsync&gt;b__0</c>, <c>&lt;&lt;RefileAsync&gt;b__0&gt;d</c>).
    /// </summary>
    private static string SourceMethodOf(Type declaring, MethodBase body) =>
        SourceMethods.GetOrAdd((declaring, body), key => ComputeSourceMethodOf(key.Declaring, key.Body));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(Type Declaring, MethodBase Body), string> SourceMethods = new();

    private static string ComputeSourceMethodOf(Type declaring, MethodBase body)
    {
        var source = IlCallScan.SourceMethod(declaring, body);
        if (source is not null && !source.Name.StartsWith('<'))
            return source.Name;

        var names = new List<string?> { source?.Name, body.Name };
        for (var type = declaring; type is not null && type.DeclaringType is not null; type = type.DeclaringType)
            names.Add(type.Name);
        foreach (var name in names)
        {
            if (name is not null && name.StartsWith('<'))
            {
                var inner = name.TrimStart('<');
                var end = inner.IndexOf('>');
                if (end > 0)
                    return inner[..end];
            }
        }

        return body.Name;
    }
}

// Compiled control fixtures for RefileWriterUnfileRuleGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: re-files a communication and takes the default unfile choice; the rule's members appear only in this
/// comment (UnfiledOwnershipFor, RegardingLinksSetIn, UnfileRuleColumnsWrittenIn, AfterReparentAsync) and in a nameof.</summary>
internal sealed class UnfileRuleControlBypassingWriter(IRecordOwnershipResolver ownership)
{
    internal async Task<string> WriteAsync(Guid id, Func<CancellationToken, Task> write)
    {
        await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingmatter"] = null },
            },
            write, CancellationToken.None);
        return nameof(DirectMessageOwnerStep.AfterReparentAsync);
    }
}

/// <summary>MUST NOT FIRE: takes all of them; the move runs inside a lambda (attributed to this method).</summary>
internal sealed class UnfileRuleControlCompliantWriter(IRecordOwnershipResolver ownership, DirectMessageOwnerStep step)
{
    internal async Task WriteAsync(Guid id, Dictionary<string, object?> fields, Func<CancellationToken, Task> write)
    {
        var reparent = await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingmatter"] = null },
                WhenUnfiled = DirectMessageOwnerStep.UnfiledOwnershipFor("sprk_communication", actsForAPerson: true, step),
                RegardingLinksSet = RecordReparent.RegardingLinksSetIn("sprk_communication", fields),
                UnfileRuleColumnsWritten = RecordReparent.UnfileRuleColumnsWrittenIn("sprk_communication", fields),
            },
            write, CancellationToken.None);
        Func<Task> after = () => step.AfterReparentAsync(id, reparent, CancellationToken.None);
        await after();
    }
}

/// <summary>MUST NOT FIRE: writes, but re-files nothing.</summary>
internal sealed class UnfileRuleControlPlainWriter(Spaarke.Dataverse.IGenericEntityService entities)
{
    internal Task WriteAsync(Guid id) =>
        entities.UpdateAsync("sprk_communication", id, new Dictionary<string, object> { ["sprk_name"] = "x" }, CancellationToken.None);
}

/// <summary>MUST FIRE on <see cref="WriteOtherAsync"/>: a second re-file in a type whose first re-file takes the rule.</summary>
internal sealed class UnfileRuleControlSecondBranchWriter(IRecordOwnershipResolver ownership, DirectMessageOwnerStep step)
{
    internal async Task WriteAsync(Guid id, Dictionary<string, object?> fields, Func<CancellationToken, Task> write)
    {
        var reparent = await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingmatter"] = null },
                WhenUnfiled = DirectMessageOwnerStep.UnfiledOwnershipFor("sprk_communication", actsForAPerson: true, step),
                RegardingLinksSet = RecordReparent.RegardingLinksSetIn("sprk_communication", fields),
                UnfileRuleColumnsWritten = RecordReparent.UnfileRuleColumnsWrittenIn("sprk_communication", fields),
            },
            write, CancellationToken.None);
        await step.AfterReparentAsync(id, reparent, CancellationToken.None);
    }

    internal async Task WriteOtherAsync(Guid id, Func<CancellationToken, Task> write) =>
        await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingperson"] = null },
            },
            write, CancellationToken.None);
}

/// <summary>MUST NOT FIRE: the re-file takes the rule through two helpers of its own type.</summary>
internal sealed class UnfileRuleControlHelperWriter(IRecordOwnershipResolver ownership, DirectMessageOwnerStep step)
{
    internal async Task WriteAsync(Guid id, Dictionary<string, object?> fields, Func<CancellationToken, Task> write)
    {
        var reparent = await ownership.ReparentAsync(Request(id, fields), write, CancellationToken.None);
        await AfterAsync(id, reparent);
    }

    private RecordReparent Request(Guid id, Dictionary<string, object?> fields) => new()
    {
        EntityLogicalName = "sprk_communication",
        RecordId = id,
        ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingmatter"] = null },
        WhenUnfiled = DirectMessageOwnerStep.UnfiledOwnershipFor("sprk_communication", actsForAPerson: true, step),
        RegardingLinksSet = RecordReparent.RegardingLinksSetIn("sprk_communication", fields),
        UnfileRuleColumnsWritten = RecordReparent.UnfileRuleColumnsWrittenIn("sprk_communication", fields),
    };

    private Task AfterAsync(Guid id, RecordOwnerResolution reparent) => step.AfterReparentAsync(id, reparent, CancellationToken.None);
}

/// <summary>MUST FIRE: references every member, but the unfile choice is discarded, never set as <c>WhenUnfiled</c>.</summary>
internal sealed class UnfileRuleControlDiscardedChoiceWriter(IRecordOwnershipResolver ownership, DirectMessageOwnerStep step)
{
    internal async Task WriteAsync(Guid id, Dictionary<string, object?> fields, Func<CancellationToken, Task> write)
    {
        var choice = DirectMessageOwnerStep.UnfiledOwnershipFor("sprk_communication", actsForAPerson: true, step);
        var reparent = await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingmatter"] = null },
                RegardingLinksSet = RecordReparent.RegardingLinksSetIn("sprk_communication", fields),
                UnfileRuleColumnsWritten = RecordReparent.UnfileRuleColumnsWrittenIn("sprk_communication", fields),
            },
            write, CancellationToken.None);
        GC.KeepAlive(choice);
        await step.AfterReparentAsync(id, reparent, CancellationToken.None);
    }
}

/// <summary>MUST FIRE: every member is taken, but a ternary can set another value as <c>WhenUnfiled</c>.</summary>
internal sealed class UnfileRuleControlTernaryChoiceWriter(IRecordOwnershipResolver ownership, DirectMessageOwnerStep step)
{
    internal async Task WriteAsync(Guid id, bool person, Dictionary<string, object?> fields, Func<CancellationToken, Task> write)
    {
        var reparent = await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingmatter"] = null },
                WhenUnfiled = person
                    ? DirectMessageOwnerStep.UnfiledOwnershipFor("sprk_communication", actsForAPerson: true, step)
                    : UnfiledOwnership.KeepCreator,
                RegardingLinksSet = RecordReparent.RegardingLinksSetIn("sprk_communication", fields),
                UnfileRuleColumnsWritten = RecordReparent.UnfileRuleColumnsWrittenIn("sprk_communication", fields),
            },
            write, CancellationToken.None);
        await step.AfterReparentAsync(id, reparent, CancellationToken.None);
    }
}

/// <summary>MUST FIRE: the choice is set as <c>WhenUnfiled</c>, then overwritten by a later <c>with</c>.</summary>
internal sealed class UnfileRuleControlOverwrittenChoiceWriter(IRecordOwnershipResolver ownership, DirectMessageOwnerStep step)
{
    internal async Task WriteAsync(Guid id, bool keep, Dictionary<string, object?> fields, Func<CancellationToken, Task> write)
    {
        var request = new RecordReparent
        {
            EntityLogicalName = "sprk_communication",
            RecordId = id,
            ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingmatter"] = null },
            WhenUnfiled = DirectMessageOwnerStep.UnfiledOwnershipFor("sprk_communication", actsForAPerson: true, step),
            RegardingLinksSet = RecordReparent.RegardingLinksSetIn("sprk_communication", fields),
            UnfileRuleColumnsWritten = RecordReparent.UnfileRuleColumnsWrittenIn("sprk_communication", fields),
        };
        if (keep)
            request = request with { WhenUnfiled = UnfiledOwnership.KeepCreator };
        var reparent = await ownership.ReparentAsync(request, write, CancellationToken.None);
        await step.AfterReparentAsync(id, reparent, CancellationToken.None);
    }
}

/// <summary>Per-method exemptions: <see cref="JoinAsync"/> is the exempt re-file (it never unfiles); <see cref="UnfileAsync"/>
/// is a new re-file of the same type that unfiles without the rule (MUST FIRE).</summary>
internal sealed class UnfileRuleControlExemptTypeWriter(IRecordOwnershipResolver ownership)
{
    internal Task<RecordOwnerResolution> JoinAsync(Guid id, Guid thread, Func<CancellationToken, Task> write) =>
        ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>
                {
                    ["sprk_communicationthread"] = new Microsoft.Xrm.Sdk.EntityReference("sprk_communicationthread", thread),
                },
                WhenUnfiled = UnfiledOwnership.KeepCreator,
            },
            write, CancellationToken.None);

    internal Task<RecordOwnerResolution> UnfileAsync(Guid id, Func<CancellationToken, Task> write) =>
        ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_communication",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?> { ["sprk_regardingmatter"] = null },
            },
            write, CancellationToken.None);
}

/// <summary>197a control, MUST FIRE: two members read the unfile creator.</summary>
internal sealed class UnfileFactsControlTwoReaders
{
    internal static Guid? DecideAsync(UnfiledCommunicationFacts facts) => facts.CreatorSystemUserId;

    internal static bool SecondLookup(UnfiledCommunicationFacts facts) => facts.CreatorSystemUserId is not null;
}

/// <summary>197a control, MUST NOT FIRE: one reader, inside a lambda of its method.</summary>
internal sealed class UnfileFactsControlOneReader
{
    internal static Guid? DecideAsync(UnfiledCommunicationFacts facts)
    {
        Func<Guid?> read = () => facts.CreatorSystemUserId;
        return read();
    }
}