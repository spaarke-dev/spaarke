using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Operator scripts keep the ontology writer's business unit out of "every default team" field-security profiles
/// (customer-provisioning-orchestration-r1 task 267, #2618): every top-level <c>scripts/*.ps1</c> that reads ALL business-unit
/// default teams and touches a profile's team membership (<c>teamprofiles_association</c>) splits them through
/// <c>scripts/common/OntologyWriterUnit.ps1</c>, and that helper names the same unit as H7b.
/// </summary>
/// <remarks>
/// <para>The ontology writer's application user sits alone in the business unit "Spaarke Ontology Writer"; that unit's default
/// team must be in NO field-security profile. H7b (SecureRecordSetupProcedure, procedure version 8) skips it at S11/S16 and
/// refuses it at S24 (<c>secure_setup.ontology_writer_holds_other_profile</c>, QuarantineRequired). Nine operator scripts added
/// "every default team" to a reader profile and demanded it under <c>-Verify</c>, so running any of them after H7b put the
/// writer's team back into a reader profile and made the next H7b run quarantine the stamp.</para>
/// <para><b>MAINTENANCE PROCEDURE</b>: a failure names the script. Dot-source the helper
/// (<c>. (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')</c>), select <c>_businessunitid_value</c> on the default-team
/// read and pass it through <c>Split-DvDefaultTeamsByOntologyWriterUnit</c>; add only <c>.ReaderTeams</c> to a profile, and
/// report each of <c>.WriterUnitTeams</c> found on the profile as a FAIL (<c>Get-DvOntologyWriterUnitTeamsOnProfile</c>). If
/// the unit's name changes, change <c>SecureRecordSetupProcedure.OntologyWriterBusinessUnitName</c> and the helper together.
/// A read of ONE unit's default team (filtered on <c>_businessunitid_value</c>) is not an "every default team" read and is not
/// checked. <c>scripts/ebilling/*</c> (a subfolder) is owned by another project and is not scanned.</para>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class.</para>
/// </remarks>
public class OntologyWriterUnitScriptGuardTests
{
    private const string HelperPath = "scripts/common/OntologyWriterUnit.ps1";
    private const string ProcedurePath =
        "src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SecureRecordSetup/SecureRecordSetupProcedure.cs";

    // An unfiltered-by-unit default-team read: the filter STARTS with isdefault and never names a unit.
    private static readonly Regex EveryDefaultTeamRead = new(
        @"teams\?[^'""\r\n]*\$filter=isdefault eq true(?![^'""\r\n]*_businessunitid_value eq)", RegexOptions.Compiled);

    [Fact(DisplayName = "Ontology writer unit: every script that puts all default teams on a profile splits them through the helper")]
    public void EveryDefaultTeamProfileScriptExcludesTheOntologyWriterUnit()
    {
        var scripts = Directory.GetFiles(Path.Combine(SourceScan.RepoRoot, "scripts"), "*.ps1", SearchOption.TopDirectoryOnly)
            .Select(path => (Path: "scripts/" + Path.GetFileName(path), Content: File.ReadAllText(path)))
            .ToList();

        Assert.NotEmpty(scripts);
        var offenders = Offenders(scripts);
        Assert.True(
            offenders.Count == 0,
            "These scripts read every business-unit default team and touch a field-security profile's teams without keeping " +
            $"the ontology writer's unit out through {HelperPath} (see this test's MAINTENANCE PROCEDURE): " + string.Join("; ", offenders));
        // Not vacuous: the nine scripts task 267 changed are in scope.
        Assert.True(scripts.Count(s => IsInScope(s.Content)) >= 9, "the scan found fewer than the nine known default-team scripts");
    }

    [Fact(DisplayName = "Ontology writer unit: the helper names the unit H7b names")]
    public void TheHelperNamesTheUnitH7bNames()
    {
        var helper = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, HelperPath));
        var procedure = File.ReadAllText(Path.Combine(SourceScan.RepoRoot, ProcedurePath));

        var inProcedure = Regex.Match(procedure, @"public const string OntologyWriterBusinessUnitName = ""([^""]+)"";");
        var inHelper = Regex.Match(helper, @"(?m)^\$OntologyWriterBusinessUnitName = '([^']+)'\s*$");
        Assert.True(inProcedure.Success, "SecureRecordSetupProcedure.OntologyWriterBusinessUnitName was not found");
        Assert.True(inHelper.Success, "the helper's $OntologyWriterBusinessUnitName was not found");
        Assert.Equal(inProcedure.Groups[1].Value, inHelper.Groups[1].Value);
        Assert.Contains("function Split-DvDefaultTeamsByOntologyWriterUnit", helper, StringComparison.Ordinal);
        Assert.Contains("function Get-DvOntologyWriterUnitTeamsOnProfile", helper, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Ontology writer unit: negative control — a default-team profile script without the split is flagged")]
    public void NegativeControl_FlagsAScriptThatAddsEveryDefaultTeam()
    {
        const string noHelper = """
            $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=isdefault eq true').value)
            foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid 'x' }
            """;
        const string helperNotCalled = """
            . (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')
            $defaultTeams = @((Invoke-DvGet "teams?`$select=teamid,name&`$filter=isdefault eq true&`$expand=businessunitid(`$select=name)").value)
            $onReader = @($reader.teamprofiles_association | ForEach-Object { $_.teamid })
            """;

        Assert.Equal(2, Offenders(new[] { ("a.ps1", noHelper), ("b.ps1", helperNotCalled) }).Count);
    }

    [Fact(DisplayName = "Ontology writer unit: positive control — a split script, a one-unit read, or no profile passes")]
    public void PositiveControl_AcceptsTheSanctionedShapes()
    {
        const string splits = """
            . (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')
            $split = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers -DefaultTeams @((Invoke-DvGet 'teams?$select=teamid,name,_businessunitid_value&$filter=isdefault eq true').value)
            foreach ($t in $split.ReaderTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid 'x' }
            """;
        const string oneUnit = """
            $defaultTeams = @(Get-All "teams?`$select=teamid,name&`$filter=_businessunitid_value eq $buId and isdefault eq true and teamtype eq 0")
            $x = Invoke-DvGet "fieldsecurityprofiles($p)/teamprofiles_association"
            """;
        const string noProfile = "$teams = Invoke-DvGet 'teams?$select=teamid&$filter=isdefault eq true'";

        Assert.Empty(Offenders(new[] { ("a.ps1", splits), ("b.ps1", oneUnit), ("c.ps1", noProfile) }));
    }

    private static bool IsInScope(string content) =>
        EveryDefaultTeamRead.IsMatch(content) && content.Contains("teamprofiles_association", StringComparison.Ordinal);

    private static List<string> Offenders(IEnumerable<(string Path, string Content)> scripts)
    {
        var offenders = new List<string>();
        foreach (var (path, content) in scripts)
        {
            if (!IsInScope(content))
                continue;

            var reasons = new List<string>();
            if (!content.Contains("common/OntologyWriterUnit.ps1", StringComparison.Ordinal))
                reasons.Add("does not dot-source the helper");
            if (!content.Contains("Split-DvDefaultTeamsByOntologyWriterUnit", StringComparison.Ordinal))
                reasons.Add("never calls Split-DvDefaultTeamsByOntologyWriterUnit");
            if (reasons.Count > 0)
                offenders.Add($"{path} ({string.Join(", ", reasons)})");
        }

        return offenders;
    }
}
