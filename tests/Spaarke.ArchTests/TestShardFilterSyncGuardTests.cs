using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// The nightly test shards run the same namespace filters as the PR run (quality-alert #2934 / #2067).
/// </summary>
/// <remarks>
/// <para><c>ci-tier2-advisory.yml</c> (<c>full-unit-tests-shard</c>) and <c>nightly-health.yml</c> (<c>flake-hunt-run</c>,
/// <c>coverage-shard</c>) each split Sprk.Bff.Api.Tests into namespace shards, and each file says "KEEP IN SYNC". On
/// 2026-11-11 the PR run split <c>api-ai-finance-external</c> out of <c>api-seam</c>; the nightly matrices were not
/// updated, so the nightly api-seam leg kept growing until it hit its caps every night from 2026-11-19 (both quality
/// alerts). This test makes the two files agree: the same shard names with the same filters, and the flake-hunt
/// analysis expecting as many shards as the matrix runs.</para>
/// <para><b>MAINTENANCE PROCEDURE</b>: a failure names the shard that differs. Rebalance in all three matrices in one
/// PR (and <c>EXPECTED_SHARDS</c> in the flake-hunt analysis). <c>other-projects</c> is coverage-only (the other test
/// projects of Spaarke.sln) and is not compared.</para>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class.</para>
/// </remarks>
public class TestShardFilterSyncGuardTests
{
    private const string PrWorkflow = ".github/workflows/ci-tier2-advisory.yml";
    private const string NightlyWorkflow = ".github/workflows/nightly-health.yml";

    /// <summary>Shards that exist only in the nightly coverage matrix, by design.</summary>
    private static readonly IReadOnlySet<string> CoverageOnlyShards = new HashSet<string>(StringComparer.Ordinal) { "other-projects" };

    [Fact(DisplayName = "CI shards: the nightly flake-hunt and coverage matrices use the PR run's shard filters")]
    public void NightlyShardFilters_MatchThePrRun()
    {
        var pr = ShardFilters(Read(PrWorkflow), "full-unit-tests-shard");
        var nightly = Read(NightlyWorkflow);

        var problems = Differences(pr, ShardFilters(nightly, "flake-hunt-run"), "flake-hunt-run")
            .Concat(Differences(pr, ShardFilters(nightly, "coverage-shard"), "coverage-shard"))
            .ToList();

        Assert.True(problems.Count == 0,
            $"{NightlyWorkflow} no longer runs the shards of {PrWorkflow} (KEEP IN SYNC): " + string.Join("; ", problems));
    }

    [Fact(DisplayName = "CI shards: the flake-hunt analysis expects as many shards as flake-hunt-run runs")]
    public void FlakeHuntExpectedShards_MatchesTheMatrix()
    {
        var nightly = Read(NightlyWorkflow);
        var match = Regex.Match(nightly, @"EXPECTED_SHARDS\s*=\s*(\d+)");
        Assert.True(match.Success, $"{NightlyWorkflow} no longer declares EXPECTED_SHARDS; update this test.");

        Assert.Equal(ShardFilters(nightly, "flake-hunt-run").Count, int.Parse(match.Groups[1].Value));
    }

    [Fact(DisplayName = "CI shards: negative control — a nightly matrix missing a shard or with another filter is flagged")]
    public void ShardSync_NegativeControl_FlagsAMissingShardAndAChangedFilter()
    {
        var pr = ShardFilters(Job("full-unit-tests-shard", ("a", "F~A."), ("b", "F~B."), ("rest", "F!~A.&F!~B.")), "full-unit-tests-shard");
        var nightly = ShardFilters(Job("flake-hunt-run", ("a", "F~A."), ("rest", "F!~A.")), "flake-hunt-run");

        var problems = Differences(pr, nightly, "flake-hunt-run");

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("'b' is missing", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("'rest' has another filter", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "CI shards: positive control — identical matrices, plus a coverage-only shard, pass")]
    public void ShardSync_PositiveControl_IdenticalMatricesPass()
    {
        var pr = ShardFilters(Job("full-unit-tests-shard", ("a", "F~A."), ("rest", "F!~A.")), "full-unit-tests-shard");
        var coverage = ShardFilters(Job("coverage-shard", ("a", "F~A."), ("rest", "F!~A."), ("other-projects", "")), "coverage-shard");

        Assert.Equal(2, pr.Count);
        Assert.Empty(Differences(pr, coverage, "coverage-shard"));
    }

    private static string Read(string path) => File.ReadAllText(Path.Combine(SourceScan.RepoRoot, path)).Replace("\r\n", "\n");

    /// <summary>The <c>- shard: x</c> / <c>filter: '…'</c> pairs of one top-level job (a 2-space-indented key under <c>jobs:</c>).</summary>
    internal static Dictionary<string, string> ShardFilters(string workflow, string job)
    {
        var start = Regex.Match(workflow, $@"^  {Regex.Escape(job)}:\s*$", RegexOptions.Multiline);
        Assert.True(start.Success, $"Job '{job}' not found; update this test.");
        var next = Regex.Match(workflow[(start.Index + start.Length)..], @"^  [A-Za-z0-9_-]+:\s*$", RegexOptions.Multiline);
        var block = next.Success
            ? workflow.Substring(start.Index, start.Length + next.Index)
            : workflow[start.Index..];

        var pairs = Regex.Matches(block, @"-\s*shard:\s*([A-Za-z0-9_-]+)\s*\n\s*filter:\s*'([^']*)'")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
        Assert.True(pairs.Count > 0, $"Job '{job}' lists no shard filters; update this test.");
        return pairs;
    }

    private static List<string> Differences(IReadOnlyDictionary<string, string> pr, IReadOnlyDictionary<string, string> nightly, string job)
    {
        var problems = new List<string>();
        foreach (var (shard, filter) in pr)
        {
            if (!nightly.TryGetValue(shard, out var other))
                problems.Add($"{job}: shard '{shard}' is missing");
            else if (other != filter)
                problems.Add($"{job}: shard '{shard}' has another filter");
        }
        problems.AddRange(nightly.Keys
            .Where(shard => !pr.ContainsKey(shard) && !CoverageOnlyShards.Contains(shard))
            .Select(shard => $"{job}: shard '{shard}' is not in the PR run"));
        return problems;
    }

    private static string Job(string name, params (string Shard, string Filter)[] shards) =>
        $"jobs:\n  {name}:\n    strategy:\n      matrix:\n        include:\n" +
        string.Concat(shards.Select(s => $"          - shard: {s.Shard}\n            filter: '{s.Filter}'\n")) +
        "  next-job:\n    runs-on: ubuntu-latest\n";
}
