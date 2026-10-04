using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Schema scripts decide solution membership one way (unified-access-control-r2 batch 4 integration): every
/// <c>scripts/Set-*Schema.ps1</c> that puts components into a solution answers "is it in the solution?" through
/// <c>scripts/common/DataverseSolutionMembership.ps1</c>, never through its own <c>solutioncomponents</c> read.
/// </summary>
/// <remarks>
/// <para>Each script used to carry its own copy, and the copies drifted. Most read ONE page of solution components, and
/// most missed that a column, key or relationship of a table added with <c>rootcomponentbehavior = 0</c> has no row of its
/// own: <c>-Verify</c> then reported an included component MISSING, the gate could never pass, and
/// <c>AddSolutionComponent</c> on it was a silent no-op (found re-running tasks 133 and 143's verify, 2026-10-03). A
/// verify that cannot pass gets skipped, which is how a missing component reaches a customer environment.</para>
/// <para><b>MAINTENANCE PROCEDURE</b>: a failure names the script. Dot-source the helper
/// (<c>. (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')</c>), read membership with
/// <c>Get-DvSolutionMembership</c> and decide each component with <c>Test-DvInSolution</c> (pass the owning table's
/// MetadataId for a column, key or relationship). Never satisfy the test by renaming the script out of the
/// <c>Set-*Schema.ps1</c> pattern.</para>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class.</para>
/// </remarks>
public class SchemaScriptSolutionMembershipGuardTests
{
    private const string HelperPath = "scripts/common/DataverseSolutionMembership.ps1";

    [Fact(DisplayName = "Schema scripts: every Set-*Schema.ps1 that adds solution components decides membership through the shared helper")]
    public void EverySchemaScriptUsesTheSharedMembershipHelper()
    {
        var scripts = Directory.GetFiles(Path.Combine(SourceScan.RepoRoot, "scripts"), "Set-*Schema.ps1", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: "scripts/" + Path.GetFileName(path), Content: File.ReadAllText(path)))
            .ToList();

        Assert.NotEmpty(scripts);
        var offenders = Offenders(scripts);
        Assert.True(
            offenders.Count == 0,
            "These schema scripts put components into a solution but do not decide membership through " +
            $"{HelperPath} (see this test's MAINTENANCE PROCEDURE): " + string.Join("; ", offenders));
    }

    [Fact(DisplayName = "Schema scripts: the shared helper pages and honours rootcomponentbehavior 0")]
    public void TheHelperPagesAndHonoursIncludeSubcomponents()
    {
        var helper = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, HelperPath));

        Assert.Contains("@odata.nextLink", helper, StringComparison.Ordinal);
        Assert.Contains("rootcomponentbehavior -eq 0", helper, StringComparison.Ordinal);
        Assert.Contains("function Get-DvSolutionMembership", helper, StringComparison.Ordinal);
        Assert.Contains("function Test-DvInSolution", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-StrictMode", helper.Replace("# No Set-StrictMode", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Schema scripts: negative control — an own solutioncomponents read, or no helper, is flagged")]
    public void NegativeControl_FlagsAScriptWithItsOwnMembershipRead()
    {
        const string ownRead = """
            Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id }
            $inSolution = @((Invoke-DvGet "solutioncomponents?`$select=objectid").value)
            """;
        const string headerOnly = """
            Invoke-DvWrite POST 'EntityDefinitions' $body @{ 'MSCRM.SolutionUniqueName' = $SolutionUniqueName }
            """;
        const string helperButOwnReadToo = """
            . (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
            $how = Test-DvInSolution -Membership $m -ComponentId $c.Id
            Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id }
            $rows = Invoke-DvGet "solutioncomponents?`$select=objectid"
            """;

        var offenders = Offenders(new[] { ("a.ps1", ownRead), ("b.ps1", headerOnly), ("c.ps1", helperButOwnReadToo) });

        Assert.Equal(3, offenders.Count);
    }

    [Fact(DisplayName = "Schema scripts: positive control — a script using the helper, or touching no solution, passes")]
    public void PositiveControl_AcceptsAScriptUsingTheHelper()
    {
        const string usesHelper = """
            . (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
            $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
            $how = Test-DvInSolution -Membership $membership -ComponentId $c.Id -TableMetadataId $c['TableId']
            Invoke-DvWrite POST 'AddSolutionComponent' @{ ComponentId = $c.Id }
            """;
        const string noSolution = "Invoke-DvWrite PATCH 'contacts(1)' @{ firstname = 'x' }";

        Assert.Empty(Offenders(new[] { ("a.ps1", usesHelper), ("b.ps1", noSolution) }));
    }

    private static List<string> Offenders(IEnumerable<(string Path, string Content)> scripts)
    {
        var offenders = new List<string>();
        foreach (var (path, content) in scripts)
        {
            var touchesSolution = content.Contains("AddSolutionComponent", StringComparison.Ordinal)
                || content.Contains("MSCRM.SolutionUniqueName", StringComparison.Ordinal);
            if (!touchesSolution)
                continue;

            var reasons = new List<string>();
            if (!content.Contains("common/DataverseSolutionMembership.ps1", StringComparison.Ordinal))
                reasons.Add("does not dot-source the helper");
            if (!content.Contains("Test-DvInSolution", StringComparison.Ordinal))
                reasons.Add("never calls Test-DvInSolution");
            if (content.Contains("solutioncomponents?", StringComparison.OrdinalIgnoreCase))
                reasons.Add("reads solutioncomponents itself");
            if (reasons.Count > 0)
                offenders.Add($"{path} ({string.Join(", ", reasons)})");
        }

        return offenders;
    }
}
