using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// field-mapping-server-write-path-r1 task 016 (UAC-r2 condition 5): Repair mode
/// (<c>RecordCreationService.RepairAsync</c>) has NO production caller yet. It is <c>internal</c>, is reached through no
/// endpoint, and no job calls it; task 052 (the scheduled pass and the admin trigger) is its first caller, and must list
/// itself here when it lands, so that the security conditions on Repair are re-checked against a real caller.
/// </summary>
/// <remarks>
/// Enforced by this source scan: a call on a receiver (<c>x.RepairAsync(</c>) anywhere under <c>src/server</c>. The private
/// <c>RepairAsync</c> methods of other jobs are called bare, without a receiver, and are not matched.
/// </remarks>
public sealed class RecordRepairReachabilityTests
{
    private static readonly Regex ReceiverCall = new(@"\.\s*RepairAsync\s*\(", RegexOptions.Compiled);

    /// <summary>Files allowed to call Repair. Empty until task 052.</summary>
    private static readonly IReadOnlySet<string> AllowedCallers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void RecordRepair_HasNoProductionCaller_UntilTask052ListsOne()
    {
        var callers = SourceScan.ServerSourceFiles()
            .Where(file => ReceiverCall.IsMatch(SourceScan.CodeText(File.ReadAllLines(file))))
            .Select(SourceScan.Relative)
            .Where(file => !AllowedCallers.Contains(Path.GetFileName(file)))
            .ToList();

        Assert.True(callers.Count == 0,
            "Repair mode (RecordCreationService.RepairAsync) gained a production caller: " + string.Join(", ", callers) +
            ". Re-check the UAC-r2 conditions on Repair against it, then list the file in AllowedCallers.");
    }

    [Theory]
    [InlineData("var outcome = await creation.RepairAsync(table, id, ct);", true)]
    [InlineData("await _records . RepairAsync (table, id, ct);", true)]
    [InlineData("await RepairAsync(run, idempotency, row, cancellationToken);", false)]
    [InlineData("private async Task RepairAsync(Run run, CancellationToken ct)", false)]
    public void TheScan_MatchesACallOnAReceiver_Only(string line, bool matches) =>
        Assert.Equal(matches, ReceiverCall.IsMatch(line));
}
