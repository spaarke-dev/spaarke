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
/// any depth), and the result of <c>UnfiledOwnershipFor</c> must be the value it sets as <c>WhenUnfiled</c> (the setter is the
/// next method referenced after the call). A type whose every re-file never unfiles a communication is listed in
/// <see cref="NeverUnfiles"/> with its reason. Which table a writer re-files is a runtime value, so every <c>ReparentAsync</c>
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
    private sealed record NeverUnfilesEntry(Type Type, string Reason);

    /// <summary>The <c>ReparentAsync</c> callers that never unfile a communication, each verified at its call.</summary>
    private static readonly IReadOnlyList<NeverUnfilesEntry> NeverUnfiles = new[]
    {
        new NeverUnfilesEntry(typeof(ThreadResolver),
            "A message JOINING a thread: the change sets the thread lookup and clears no regarding lookup (KeepCreator). A Direct " +
            "join is owned by the join path's own step (DirectMessageOwnerStep.GrantThenOwnDirectAsync)."),
        new NeverUnfilesEntry(typeof(IncomingAssociationResolver),
            "Inbound association: additive filings only, the engine never clears a sibling regarding (KeepCreator)."),
        new NeverUnfilesEntry(typeof(DataverseDocumentsEndpoints),
            "Re-files a sprk_document only (EntityLogicalName is the literal sprk_document), never a communication."),
    };

    private const string UnfiledOwnershipForMember = $"{nameof(DirectMessageOwnerStep)}.{nameof(DirectMessageOwnerStep.UnfiledOwnershipFor)}";
    private const string WhenUnfiledSetter = $"{nameof(RecordReparent)}.set_{nameof(RecordReparent.WhenUnfiled)}";

    /// <summary>What a re-file missing the flow of the choice into <c>WhenUnfiled</c> is reported as.</summary>
    private const string FlowMissing = "WhenUnfiled set from UnfiledOwnershipFor";

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

        var listed = NeverUnfiles.ToDictionary(e => e.Type);
        var problems = new List<string>();
        foreach (var (key, use) in scan.OrderBy(s => s.Key.Type.FullName, StringComparer.Ordinal).ThenBy(s => s.Key.Method, StringComparer.Ordinal))
        {
            if (!use.Reparents || listed.ContainsKey(key.Type) || Missing(scan, key) is not { Length: > 0 } missing)
                continue;
            problems.Add($"{key.Type.FullName}.{key.Method} calls ReparentAsync without {string.Join(", ", missing)}");
        }

        foreach (var exception in NeverUnfiles)
        {
            if (!scan.Any(s => s.Key.Type == exception.Type && s.Value.Reparents))
                problems.Add($"STALE entry {exception.Type.FullName}: it no longer calls ReparentAsync — remove it.");
        }

        Assert.True(problems.Count == 0,
            "A re-file that can unfile a communication takes the one unfile rule in the method that re-files (or a helper of its " +
            "type it calls): WhenUnfiled set from DirectMessageOwnerStep.UnfiledOwnershipFor, RegardingLinksSet from " +
            "RecordReparent.RegardingLinksSetIn, UnfileRuleColumnsWritten from RecordReparent.UnfileRuleColumnsWrittenIn, and " +
            "DirectMessageOwnerStep.AfterReparentAsync after the write. Take it, or list the type with the reason it never " +
            "unfiles a communication:\n" + string.Join("\n", problems));
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

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Use
    {
        public bool Reparents { get; set; }
        public bool ChoiceSetAsWhenUnfiled { get; set; }
        public HashSet<string> Members { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Helpers { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>What the re-file at <paramref name="key"/> is missing, over it and every method of its type it calls.</summary>
    private static string[] Missing(IReadOnlyDictionary<(Type Type, string Method), Use> scan, (Type Type, string Method) key)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal);
        var flows = false;
        var pending = new Stack<string>([key.Method]);
        while (pending.TryPop(out var method))
        {
            if (!seen.Add(method) || !scan.TryGetValue((key.Type, method), out var use))
                continue;
            members.UnionWith(use.Members);
            flows |= use.ChoiceSetAsWhenUnfiled;
            foreach (var helper in use.Helpers)
                pending.Push(helper);
        }

        var missing = Required.Where(r => !members.Contains(r)).ToList();
        if (!flows)
            missing.Add(FlowMissing);
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

                MethodBase? previous = null;
                foreach (var target in IlCallScan.ReferencedMethods(body))
                {
                    use.Reparents |= IsReparent(target, owner);
                    if (IsRequired(target))
                        use.Members.Add(MemberOf(target));
                    if (previous is not null && IsRequired(previous) && MemberOf(previous) == UnfiledOwnershipForMember
                        && target.DeclaringType == typeof(RecordReparent) && MemberOf(target) == WhenUnfiledSetter)
                    {
                        use.ChoiceSetAsWhenUnfiled = true;
                    }

                    if (target.DeclaringType is { } declaring && IlCallScan.Outermost(declaring) == owner)
                    {
                        var helper = SourceMethodOf(declaring, target);
                        if (helper != key.Item2)
                            use.Helpers.Add(helper);
                    }

                    previous = target;
                }
            }
        }

        return uses;
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
