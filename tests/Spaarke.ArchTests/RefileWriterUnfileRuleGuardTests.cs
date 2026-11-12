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
/// <see cref="DirectMessageOwnerStep.UnfiledOwnershipFor"/>, passes the typed regarding lookups its change sets through
/// <see cref="RecordReparent.RegardingLinksSetIn{TValue}"/> (so the rule judges the row as the change leaves it), and runs
/// <see cref="DirectMessageOwnerStep.AfterReparentAsync"/> after the write. <see cref="RecordReparent.WhenUnfiled"/> has a
/// default, so a writer that sets none of these takes the default with no error; this guard is what holds the rule.
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> The compiled method references (<see cref="IlCallScan"/>, attributed to the outermost type, so a call
/// inside a lambda, local function or async state machine counts and a comment, string or <c>nameof</c> does not). A type that
/// calls <see cref="IRecordOwnershipResolver.ReparentAsync"/> is a re-file writer; it must reference all three members above, or
/// be listed in <see cref="NeverUnfiles"/> with its reason. Which table a writer re-files is a runtime value, so every
/// <c>ReparentAsync</c> caller is held to the rule unless it is listed.</para>
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

    /// <summary>The members a writer that may unfile a communication must reference.</summary>
    private static readonly string[] Required =
    [
        $"{nameof(DirectMessageOwnerStep)}.{nameof(DirectMessageOwnerStep.UnfiledOwnershipFor)}",
        $"{nameof(RecordReparent)}.{nameof(RecordReparent.RegardingLinksSetIn)}",
        $"{nameof(DirectMessageOwnerStep)}.{nameof(DirectMessageOwnerStep.AfterReparentAsync)}",
    ];

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "fm 054 3b: every ReparentAsync caller takes the one unfile rule (UnfiledOwnershipFor, RegardingLinksSetIn, AfterReparentAsync) or is listed")]
    public void EveryReparentCallerTakesTheOneUnfileRule()
    {
        var scan = Scan(BffTypes.Value);

        // Non-vacuity: the four unfile writers are seen re-filing AND taking the rule.
        foreach (var compliant in new[]
                 {
                     "Sprk.Bff.Api.Services.Dataverse.OwnedChildWrite", "Sprk.Bff.Api.Services.Dataverse.DataverseUpdateHandler",
                     "Sprk.Bff.Api.Services.Ai.Nodes.UpdateRecordActionCore",
                     "Sprk.Bff.Api.Api.FieldMappings.FieldMappingEndpoints",
                 })
        {
            Assert.True(
                scan.Any(s => s.Key.FullName == compliant && s.Value.Reparents && Missing(s.Value).Length == 0),
                $"{compliant} should be seen re-filing AND taking the one unfile rule — the scan lost its signal. Seen: " +
                string.Join("; ", scan.Where(s => s.Key.FullName == compliant).Select(s => $"reparents={s.Value.Reparents} members={string.Join(",", s.Value.Members)}")));
        }

        var listed = NeverUnfiles.ToDictionary(e => e.Type);
        var problems = new List<string>();
        foreach (var (type, use) in scan.OrderBy(s => s.Key.FullName, StringComparer.Ordinal))
        {
            if (!use.Reparents || listed.ContainsKey(type) || Missing(use) is not { Length: > 0 } missing)
                continue;
            problems.Add($"{type.FullName} calls ReparentAsync without {string.Join(", ", missing)}");
        }

        foreach (var exception in NeverUnfiles)
        {
            if (scan.GetValueOrDefault(exception.Type) is not { Reparents: true })
                problems.Add($"STALE entry {exception.Type.FullName}: it no longer calls ReparentAsync — remove it.");
        }

        Assert.True(problems.Count == 0,
            "A re-file writer that can unfile a communication takes the one unfile rule: WhenUnfiled from " +
            "DirectMessageOwnerStep.UnfiledOwnershipFor, RegardingLinksSet from RecordReparent.RegardingLinksSetIn, and " +
            "DirectMessageOwnerStep.AfterReparentAsync after the write. Take it, or list the type with the reason it never " +
            "unfiles a communication:\n" + string.Join("\n", problems));
    }

    [Fact(DisplayName = "fm 054 3b: control — a ReparentAsync caller without the unfile rule is found; one with it, or no re-file, is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var scan = Scan(new[] { typeof(UnfileRuleControlBypassingWriter), typeof(UnfileRuleControlCompliantWriter), typeof(UnfileRuleControlPlainWriter) }
            .SelectMany(IlCallScan.WithNested));

        var bypassing = scan.GetValueOrDefault(typeof(UnfileRuleControlBypassingWriter));
        Assert.True(bypassing is { Reparents: true } && Missing(bypassing).Length == Required.Length,
            "must fire: re-files, names the members only in a comment and a nameof, and takes none of them");
        Assert.True(scan.GetValueOrDefault(typeof(UnfileRuleControlCompliantWriter)) is { Reparents: true } compliant
                    && Missing(compliant).Length == 0,
            "must not fire: takes all three, the move inside a lambda");
        Assert.True(scan.GetValueOrDefault(typeof(UnfileRuleControlPlainWriter)) is null or { Reparents: false },
            "must not fire: re-files nothing");
    }

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Use
    {
        public bool Reparents { get; set; }
        public HashSet<string> Members { get; } = new(StringComparer.Ordinal);
    }

    private static string[] Missing(Use use) => Required.Where(r => !use.Members.Contains(r)).ToArray();

    private static Dictionary<Type, Use> Scan(IEnumerable<Type> types)
    {
        var uses = new Dictionary<Type, Use>();
        foreach (var (caller, _, target) in IlCallScan.MethodReferences(types))
        {
            var owner = IlCallScan.Outermost(caller);
            var reparents = target.Name == nameof(IRecordOwnershipResolver.ReparentAsync)
                            && target.DeclaringType is { } declaring && typeof(IRecordOwnershipResolver).IsAssignableFrom(declaring)
                            && declaring != owner;
            var member = $"{target.DeclaringType?.Name}.{target.Name}";
            var required = Required.Contains(member, StringComparer.Ordinal)
                           && (target.DeclaringType == typeof(DirectMessageOwnerStep) || target.DeclaringType == typeof(RecordReparent));
            if (!reparents && !required)
                continue;

            if (!uses.TryGetValue(owner, out var use))
                uses[owner] = use = new Use();
            use.Reparents |= reparents;
            if (required)
                use.Members.Add(member);
        }

        return uses;
    }
}

// Compiled control fixtures for RefileWriterUnfileRuleGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: re-files a communication and takes the default unfile choice; the rule's members appear only in this
/// comment (UnfiledOwnershipFor, RegardingLinksSetIn, AfterReparentAsync) and in a nameof.</summary>
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

/// <summary>MUST NOT FIRE: takes all three; the move runs inside a lambda (attributed to this type).</summary>
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
