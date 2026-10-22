using System.Reflection;
using Sprk.Bff.Api.Services.RecordCreation;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 016 (UAC-r2 condition 5): Repair mode
/// (<c>RecordCreationService.RepairAsync</c>) has NO production caller yet. It is <c>internal</c>, is reached through no
/// endpoint, and no job calls it; task 052 (the scheduled pass and the admin trigger) is its first caller, and must list
/// itself here when it lands, so that the security conditions on Repair are re-checked against a real caller.
/// </summary>
/// <remarks>
/// Enforced by an IL scan (UAC-r2 condition (b) F3-1; the text regex it replaces missed a receiverless wrapper and a method
/// group): every reference to <c>RecordCreationService.RepairAsync</c> in the BFF's compiled code — a call, a method group
/// (<c>ldftn</c>) or a token — is attributed to its declared member (<c>Type::Member</c>; a lambda, local function or async
/// state machine counts for the member it was written in) and must be listed. ANY reference outside the list fails, so a
/// wrapper is caught where it references Repair, and a caller of the wrapper needs no transitive walk.
/// </remarks>
public sealed class RecordRepairReachabilityTests
{
    /// <summary>
    /// Members allowed to reference Repair, each with its reason. Empty until task 052 (which is hard-blocked on task 033).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedReferences =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void RecordRepair_HasNoProductionCaller_UntilTask052ListsOne()
    {
        var references = RepairReferences(typeof(Program).Assembly.GetTypes());

        var unlisted = references.Where(r => !AllowedReferences.ContainsKey(r)).ToList();
        Assert.True(unlisted.Count == 0,
            "Repair mode (RecordCreationService.RepairAsync) gained a production reference: " + string.Join(", ", unlisted) +
            ". Re-check the UAC-r2 conditions on Repair against it, then list the member in AllowedReferences.");

        var stale = AllowedReferences.Keys.Where(k => !references.Contains(k)).ToList();
        Assert.True(stale.Count == 0, "Remove these AllowedReferences entries (they no longer reference Repair): " + string.Join(", ", stale));
    }

    [Fact]
    public void TheScan_FindsAWrapperAMethodGroupAndALambda_NotAnotherRepairAsync()
    {
        var fixtures = new[]
        {
            typeof(RepairReachabilityControlWrapper), typeof(RepairReachabilityControlCallsTheWrapper),
            typeof(RepairReachabilityControlMethodGroup), typeof(RepairReachabilityControlLambda),
            typeof(RepairReachabilityControlAnotherRepair),
        }.SelectMany(IlCallScan.WithNested);

        Assert.Equal(
            new[]
            {
                $"{typeof(RepairReachabilityControlLambda).FullName}::Run",
                $"{typeof(RepairReachabilityControlMethodGroup).FullName}::Take",
                $"{typeof(RepairReachabilityControlWrapper).FullName}::RepairOnce",
            },
            RepairReferences(fixtures));
    }

    /// <summary>Every member (<c>Type::Member</c>) whose compiled code references <c>RecordCreationService.RepairAsync</c>.</summary>
    internal static SortedSet<string> RepairReferences(IEnumerable<Type> types) =>
        new(IlCallScan.MethodReferences(types)
                .Where(r => IsRepair(r.Target))
                .Select(r => ExternalDocumentNarrowingGuardTests.SourceKey(r.Caller, r.CallerMethod)),
            StringComparer.Ordinal);

    private static bool IsRepair(MethodBase method) =>
        method.DeclaringType == typeof(RecordCreationService) && method.Name == nameof(RecordCreationService.RepairAsync);
}

// Compiled control fixtures for RecordRepairReachabilityTests (never run; only their IL is read).

/// <summary>MUST FIRE: a wrapper that calls Repair.</summary>
internal sealed class RepairReachabilityControlWrapper(RecordCreationService creation)
{
    internal Task<RecordRepairOutcome> RepairOnce(string table, Guid id) => creation.RepairAsync(table, id, CancellationToken.None);
}

/// <summary>Calls the wrapper only: the wrapper is what is flagged (any reference outside the list fails).</summary>
internal sealed class RepairReachabilityControlCallsTheWrapper(RepairReachabilityControlWrapper wrapper)
{
    internal Task<RecordRepairOutcome> Run(Guid id) => wrapper.RepairOnce("sprk_event", id);
}

/// <summary>MUST FIRE: Repair taken as a method group.</summary>
internal sealed class RepairReachabilityControlMethodGroup(RecordCreationService creation)
{
    internal Func<string, Guid, CancellationToken, Task<RecordRepairOutcome>> Take() => creation.RepairAsync;
}

/// <summary>MUST FIRE: Repair called inside a lambda (counted for the member it is written in).</summary>
internal sealed class RepairReachabilityControlLambda(RecordCreationService creation)
{
    internal async Task Run(Guid id)
    {
        Func<Task<RecordRepairOutcome>> repair = () => creation.RepairAsync("sprk_event", id, CancellationToken.None);
        await repair();
    }
}

/// <summary>MUST NOT FIRE: a different method that happens to be named RepairAsync.</summary>
internal sealed class RepairReachabilityControlAnotherRepair
{
    internal Task RepairAsync(string table, Guid id, CancellationToken ct) => Task.CompletedTask;

    internal Task Run(Guid id) => RepairAsync("sprk_event", id, CancellationToken.None);
}
