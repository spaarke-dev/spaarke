using System.Reflection;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// GitHub #2091 — every RE-FILE writer in <c>Sprk.Bff.Api</c> runs <see cref="SecureChildReconciler.AfterRefileAsync"/>, the
/// ONE step after a re-file (the re-filed row's secure-child mirror, the pass over everything filed under it, a
/// communication's thread). A writer that skips it leaves a row moved under a Secure record readable by its business unit
/// until the two-minute net, and releases nothing when it moves out (the defect #2091 fixed in the playbook update node
/// and the output orchestrator's generic update).
/// </summary>
/// <remarks>
/// <para><b>The signal.</b> A re-file goes through a re-file seam: <see cref="IRecordOwnershipResolver.ReparentAsync"/> (the
/// one owner re-derivation for a row whose filing lookups move), <see cref="OwnedChildWrite.RefileAsync"/> (the one re-file
/// core) or <see cref="ThreadResolver.AssignToThreadReconcilingOwnerAsync"/> (a message joining a record thread, S6, without
/// the step — the join that runs it is <c>ThreadResolver.JoinThreadAsync</c>, which a caller uses instead). A type
/// whose compiled code references a seam must also reference <see cref="SecureChildReconciler.AfterRefileAsync"/>, or be
/// listed: <see cref="DelegatingWriters"/> (it hands the re-file back to its callers, and THEIR calls of the named members
/// are held to the same rule) or <see cref="KnownViolations"/> (the ratchet baseline: each one has a reason, a new one
/// fails, and an entry that no longer violates must be removed).</para>
/// <para><b>Why IL, not text.</b> Both writers #2091 fixed already MENTIONED <c>AfterRefileAsync</c> in a comment saying they
/// did not run it; a text scan would have passed them. <see cref="IlCallScan"/> reads the compiled method references, and
/// attributes a lambda, local function or async state machine to its outermost type — so a hook called inside a closure
/// counts, and a comment, string or <c>nameof</c> does not.</para>
/// <para><b>Granularity and limits.</b> Per TYPE, as #2091 states the rule: a type that runs the hook on one path and
/// skips it on another passes (code review). A writer that moves a filing lookup with a plain update and NO seam (no owner
/// re-derivation) is NOT seen, and cannot be from IL: which table and column an update writes are runtime values — a
/// <c>{nav}@odata.bind</c> key built from metadata or playbook configuration, or a payload a caller hands in — so "writes a
/// lineage lookup" is not a property of the compiled code. The heuristic (a generic update call plus the literal
/// <c>@odata.bind</c>) flags every create and every non-filing bind, and misses the pass-through writers: the playbook
/// update node, the output orchestrator's update and the field-mapping push, which #2091 round 3 found writing filing binds
/// with no seam. That class is closed instead by the ONE payload reader every generic writer uses,
/// <see cref="RecordReparent.ParentChangesWithClearsIn{TValue}"/>: it turns a filing bind into a parent change (or refuses
/// it), so those writers re-file through <see cref="IRecordOwnershipResolver.ReparentAsync"/> and are held here. A new
/// generic writer that bypasses the reader is a code-review item. Lineage tables that are NOT reparentable children
/// (<c>sprk_signal</c>, <c>sprk_decisionrecord</c>, <c>sprk_fileversion</c>, <c>sprk_communicationthread</c>, …) still re-file
/// through the generic writers with no seam and no step — their owner is not re-derived there — and are covered only by the
/// two-minute net.</para>
/// <para><b>Controls.</b> <see cref="Detector_MustFireAndMustNotFire"/> runs the same detector over
/// the <c>RefileGuardControl*</c> fixtures below: a reparent with only a comment and a <c>nameof</c> of the hook, and a
/// thread join without it, are found; a reparent whose hook runs inside a lambda, and a writer that re-files nothing, are
/// not.</para>
/// </remarks>
public class RefileWriterAfterRefileGuardTests
{
    /// <summary>A type that re-files and returns the outcome to its callers, who run the step; <paramref name="DelegatedSeams"/>
    /// are its members whose callers are scanned as re-file writers in its place.</summary>
    private sealed record DelegatingWriter(Type Type, string[] DelegatedSeams, string Reason);

    private static readonly IReadOnlyList<DelegatingWriter> DelegatingWriters = new[]
    {
        new DelegatingWriter(typeof(OwnedChildWrite), [nameof(OwnedChildWrite.RefileAsync)],
            "Task 147 r1: the ONE re-file core. It answers RefileOutcome.Written; its callers (the child-records route and the " +
            "chat update tool) run AfterRefileAsync."),
        new DelegatingWriter(typeof(IncomingAssociationResolver),
            [nameof(IncomingAssociationResolver.ApplyToExistingRecordAsync), nameof(IncomingAssociationResolver.ResolveAsync)],
            "Behaviour review F3: ApplyToExistingRecordAsync answers whether it re-filed; its caller ReconciledEmailFiling runs " +
            "AfterRefileAsync. ResolveAsync has no caller today; a new one is held to the rule."),
    };

    /// <summary>The ratchet baseline: re-file writers that do not run the step yet, each with its reason.</summary>
    private sealed record KnownViolation(Type Type, string Reason);

    private static readonly IReadOnlyList<KnownViolation> KnownViolations = new[]
    {
        new KnownViolation(typeof(InvoiceReviewService),
            "Found by this guard (#2091 review): a document filed under an invoice is re-parented (ReparentAsync, inherited " +
            "matter) without AfterRefileAsync. Not in #2091's scope; follow-up #2095."),
    };

    private static readonly Lazy<IReadOnlyList<Type>> BffTypes = new(() => typeof(Program).Assembly.GetTypes());

    [Fact(DisplayName = "#2091: every type that re-files through a re-file seam runs SecureChildReconciler.AfterRefileAsync (or is listed)")]
    public void EveryRefileWriterRunsTheAfterRefileStep()
    {
        var scan = Scan(BffTypes.Value, IsRefileSeam);

        // Non-vacuity: the writers that run the step are seen as re-file writers that call it.
        foreach (var compliant in new[]
                 {
                     "Sprk.Bff.Api.Api.ChildRecordEndpoints", "Sprk.Bff.Api.Api.DataverseDocumentsEndpoints",
                     "Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler", "Sprk.Bff.Api.Services.Office.ReconciledEmailFiling",
                     // #2091: the thread JOIN (ThreadResolver.JoinThreadAsync, which CommunicationService's respond-into-thread
                     // also calls) re-files through ReparentAsync and runs the step with RefileThreadStep.PlacedByWriter.
                     "Sprk.Bff.Api.Services.Communication.ThreadResolver",
                 })
        {
            Assert.True(
                scan.Any(s => s.Key.FullName == compliant && s.Value.Seams.Count > 0 && s.Value.CallsHook),
                $"{compliant} should be seen re-filing through a seam AND calling AfterRefileAsync — the scan lost its signal.");
        }

        var delegating = DelegatingWriters.ToDictionary(d => d.Type);
        var baseline = KnownViolations.ToDictionary(k => k.Type);
        var problems = new List<string>();

        foreach (var (type, use) in scan.OrderBy(s => s.Key.FullName, StringComparer.Ordinal))
        {
            if (use.Seams.Count == 0 || use.CallsHook || delegating.ContainsKey(type))
                continue;
            if (!baseline.ContainsKey(type))
                problems.Add($"NEW re-file writer without AfterRefileAsync: {type.FullName} (re-files through {string.Join(", ", use.Seams.Order())})");
        }

        foreach (var known in KnownViolations)
        {
            var use = scan.GetValueOrDefault(known.Type);
            if (use is null || use.Seams.Count == 0)
                problems.Add($"STALE baseline entry {known.Type.FullName}: it no longer re-files through a seam — remove it.");
            else if (use.CallsHook)
                problems.Add($"STALE baseline entry {known.Type.FullName}: it now runs AfterRefileAsync — remove it (the ratchet).");
        }

        foreach (var writer in DelegatingWriters)
        {
            if (scan.GetValueOrDefault(writer.Type) is not { Seams.Count: > 0 })
                problems.Add($"STALE delegating entry {writer.Type.FullName}: it no longer re-files through a seam — remove it.");
        }

        Assert.True(problems.Count == 0,
            "A re-file writer must run SecureChildReconciler.AfterRefileAsync after a successful write that moved a filing " +
            "lookup, reading whether the row was isolated BEFORE the write (#2091). Call it, or list the type with its reason:\n" +
            string.Join("\n", problems));
    }

    /// <summary>
    /// #2159 slice 2: re-file writers that run the hook WITHOUT the reparent's answer, each with its reason — none of them can
    /// re-file a document out of one secure record into another with F3, so none has a release to record.
    /// </summary>
    private static readonly IReadOnlyList<KnownViolation> WithoutTheResolution = new[]
    {
        new KnownViolation(typeof(ThreadResolver),
            "A message joining a record thread (sprk_communication): never a document, so never a file's F3 release."),
    };

    [Fact(DisplayName = "#2159 slice 2: every re-file writer that re-files through ReparentAsync or the re-file core passes the reparent's answer to AfterRefileAsync")]
    public void EveryRefileWriterPassesTheReparentsAnswer()
    {
        var scan = ScanForTheResolution(BffTypes.Value);

        // Non-vacuity: the document re-file writers are seen passing it.
        foreach (var compliant in new[]
                 {
                     "Sprk.Bff.Api.Api.DataverseDocumentsEndpoints", "Sprk.Bff.Api.Api.ChildRecordEndpoints",
                     "Sprk.Bff.Api.Services.Ai.Handlers.DataverseUpdateRecordHandler",
                 })
        {
            Assert.True(scan.Any(s => s.Key.FullName == compliant && s.Value.Reparents && s.Value.PassesResolution),
                $"{compliant} should be seen re-filing AND passing the reparent's answer to AfterRefileAsync — the scan lost its signal.");
        }

        var allowed = WithoutTheResolution.ToDictionary(k => k.Type);
        var problems = new List<string>();
        foreach (var (type, use) in scan.OrderBy(s => s.Key.FullName, StringComparer.Ordinal))
        {
            if (!use.Reparents || !use.CallsHook || use.PassesResolution || allowed.ContainsKey(type))
                continue;
            problems.Add($"{type.FullName} runs AfterRefileAsync without the reparent's answer (RecordOwnerResolution)");
        }

        foreach (var known in WithoutTheResolution)
        {
            if (scan.GetValueOrDefault(known.Type) is not { Reparents: true, CallsHook: true, PassesResolution: false })
                problems.Add($"STALE entry {known.Type.FullName}: it no longer runs the hook without the answer — remove it.");
        }

        Assert.True(problems.Count == 0,
            "A re-file writer passes the reparent's RecordOwnerResolution to SecureChildReconciler.AfterRefileAsync, so a " +
            "document re-filed out of secure record S1 into S2 with F3 records its file's release (#2159 slice 2):\n" +
            string.Join("\n", problems));
    }

    [Fact(DisplayName = "#2159 slice 2: control — a hook without the reparent's answer is found; one with it is not")]
    public void ResolutionDetector_MustFireAndMustNotFire()
    {
        var scan = ScanForTheResolution(new[] { typeof(RefileGuardControlHookedReparentWriter), typeof(RefileGuardControlHookedWithTheAnswerWriter) }
            .SelectMany(IlCallScan.WithNested));

        Assert.True(scan.GetValueOrDefault(typeof(RefileGuardControlHookedReparentWriter)) is { Reparents: true, CallsHook: true, PassesResolution: false },
            "must fire: re-files and runs the hook without the answer");
        Assert.True(scan.GetValueOrDefault(typeof(RefileGuardControlHookedWithTheAnswerWriter)) is { Reparents: true, PassesResolution: true },
            "must not fire: passes the answer");
    }

    private sealed class ResolutionUse
    {
        public bool Reparents { get; set; }
        public bool CallsHook { get; set; }
        public bool PassesResolution { get; set; }
    }

    private static Dictionary<Type, ResolutionUse> ScanForTheResolution(IEnumerable<Type> types)
    {
        var uses = new Dictionary<Type, ResolutionUse>();
        foreach (var (caller, _, target) in IlCallScan.MethodReferences(types))
        {
            var owner = IlCallScan.Outermost(caller);
            var reparents = target.DeclaringType != owner
                && ((target.Name == nameof(IRecordOwnershipResolver.ReparentAsync) && target.DeclaringType is { } d
                        && typeof(IRecordOwnershipResolver).IsAssignableFrom(d))
                    || (target.DeclaringType == typeof(OwnedChildWrite) && target.Name == nameof(OwnedChildWrite.RefileAsync)));
            var hook = IsTheHook(target);
            if (!reparents && !hook)
                continue;

            if (!uses.TryGetValue(owner, out var use))
                uses[owner] = use = new ResolutionUse();
            use.Reparents |= reparents;
            if (hook)
            {
                use.CallsHook = true;
                use.PassesResolution |= target.GetParameters().Any(p => p.ParameterType == typeof(RecordOwnerResolution));
            }
        }

        return uses;
    }

    [Fact(DisplayName = "#2091: negative control — an unhooked reparent or thread join is found; a hook inside a lambda, or no re-file, is not")]
    public void Detector_MustFireAndMustNotFire()
    {
        var fixtures = new[]
        {
            typeof(RefileGuardControlUnhookedReparentWriter), typeof(RefileGuardControlUnhookedThreadJoinWriter),
            typeof(RefileGuardControlHookedReparentWriter), typeof(RefileGuardControlPlainWriter),
        };
        var scan = Scan(fixtures.SelectMany(IlCallScan.WithNested), IsRefileSeam);

        var flagged = scan.Where(s => s.Value.Seams.Count > 0 && !s.Value.CallsHook).Select(s => s.Key.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(
            new[] { nameof(RefileGuardControlUnhookedReparentWriter), nameof(RefileGuardControlUnhookedThreadJoinWriter) },
            flagged);

        Assert.True(scan.GetValueOrDefault(typeof(RefileGuardControlHookedReparentWriter)) is { CallsHook: true, Seams.Count: > 0 },
            "a hook called inside a lambda is attributed to its outermost type");
        Assert.True(scan.GetValueOrDefault(typeof(RefileGuardControlPlainWriter)) is null or { Seams.Count: 0 },
            "a writer that re-files nothing is not a re-file writer");
    }

    // ── detector ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class Use
    {
        public HashSet<string> Seams { get; } = new(StringComparer.Ordinal);
        public bool CallsHook { get; set; }
    }

    /// <summary>Outermost type → the re-file seams its compiled code references, and whether it references the hook.</summary>
    private static Dictionary<Type, Use> Scan(IEnumerable<Type> types, Func<MethodBase, bool> isSeam)
    {
        var uses = new Dictionary<Type, Use>();
        foreach (var (caller, _, target) in IlCallScan.MethodReferences(types))
        {
            var owner = IlCallScan.Outermost(caller);
            var seam = isSeam(target) && target.DeclaringType != owner;
            var hook = IsTheHook(target);
            if (!seam && !hook)
                continue;

            if (!uses.TryGetValue(owner, out var use))
                uses[owner] = use = new Use();
            if (seam)
                use.Seams.Add($"{target.DeclaringType?.Name}.{target.Name}");
            if (hook)
                use.CallsHook = true;
        }

        return uses;
    }

    private static bool IsRefileSeam(MethodBase method) =>
        method.DeclaringType is { } declaring
        && ((method.Name == nameof(IRecordOwnershipResolver.ReparentAsync) && typeof(IRecordOwnershipResolver).IsAssignableFrom(declaring))
            || (declaring == typeof(ThreadResolver) && method.Name == nameof(ThreadResolver.AssignToThreadReconcilingOwnerAsync))
            || DelegatingWriters.Any(d => d.Type == declaring && d.DelegatedSeams.Contains(method.Name, StringComparer.Ordinal)));

    private static bool IsTheHook(MethodBase method) =>
        method.DeclaringType == typeof(SecureChildReconciler) && method.Name == nameof(SecureChildReconciler.AfterRefileAsync);
}

// Compiled control fixtures for RefileWriterAfterRefileGuardTests.Detector_MustFireAndMustNotFire. Top-level types (the scan
// attributes nested types to their outermost one). Never run; only their IL is read.

/// <summary>MUST FIRE: re-parents, and only MENTIONS the hook (this comment says it runs AfterRefileAsync; it does not).</summary>
internal sealed class RefileGuardControlUnhookedReparentWriter(IRecordOwnershipResolver ownership)
{
    internal async Task<string> WriteAsync(Guid id, Func<CancellationToken, Task> write)
    {
        // SecureChildReconciler.AfterRefileAsync runs after this — in this comment only.
        await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_todo",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(),
            },
            write, CancellationToken.None);
        return nameof(SecureChildReconciler.AfterRefileAsync);
    }
}

/// <summary>MUST FIRE: a message joins a record thread (S6) without the hook.</summary>
internal sealed class RefileGuardControlUnhookedThreadJoinWriter(IGenericEntityService entities, IRecordOwnershipResolver ownership)
{
    internal Task<ThreadAssignment> JoinAsync(Guid message, Guid thread) =>
        ThreadResolver.AssignToThreadReconcilingOwnerAsync(entities, ownership, message, thread, _ => Task.CompletedTask, CancellationToken.None);
}

/// <summary>MUST NOT FIRE: re-parents, then runs the hook inside a lambda (attributed to this type).</summary>
internal sealed class RefileGuardControlHookedReparentWriter(IRecordOwnershipResolver ownership, SecureChildReconciler children)
{
    internal async Task WriteAsync(Guid id, Func<CancellationToken, Task> write)
    {
        await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_todo",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(),
            },
            write, CancellationToken.None);
        Func<Task> after = () => children.AfterRefileAsync("sprk_todo", id, () => Task.FromResult(false), CancellationToken.None);
        await after();
    }
}

/// <summary>#2159 slice 2 MUST NOT FIRE: re-parents, then runs the hook WITH the reparent's answer.</summary>
internal sealed class RefileGuardControlHookedWithTheAnswerWriter(IRecordOwnershipResolver ownership, SecureChildReconciler children)
{
    internal async Task WriteAsync(Guid id, Func<CancellationToken, Task> write)
    {
        var reparent = await ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = "sprk_document",
                RecordId = id,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(),
            },
            write, CancellationToken.None);
        await children.AfterRefileAsync("sprk_document", id, () => Task.FromResult(false), reparent, CancellationToken.None);
    }
}

/// <summary>MUST NOT FIRE: writes, but through no re-file seam.</summary>
internal sealed class RefileGuardControlPlainWriter(IGenericEntityService entities)
{
    internal Task WriteAsync(Guid id) =>
        entities.UpdateAsync("sprk_todo", id, new Dictionary<string, object> { ["sprk_name"] = "x" }, CancellationToken.None);
}
