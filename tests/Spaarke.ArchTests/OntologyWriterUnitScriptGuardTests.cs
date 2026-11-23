using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Operator scripts keep the ontology writer's business unit out of "every default team" field-security profiles
/// (customer-provisioning-orchestration-r1 task 267, #2618): every <c>scripts/**/*.ps1</c> that reads ALL business-unit
/// default teams and touches a profile's team membership (<c>teamprofiles_association</c>) splits them through
/// <c>scripts/common/OntologyWriterUnit.ps1</c>, and that helper names the same unit as H7b.
/// </summary>
/// <remarks>
/// <para>The ontology writer's application user sits alone in the business unit "Spaarke Ontology Writer"; that unit's default
/// team must be in NO field-security profile. H7b (SecureRecordSetupProcedure, procedure version 8) skips it at S11/S16 and
/// refuses it at S24 (<c>secure_setup.ontology_writer_holds_other_profile</c>, QuarantineRequired). Nine operator scripts added
/// "every default team" to a reader profile and demanded it under <c>-Verify</c>, so running any of them after H7b put the
/// writer's team back into a reader profile and made the next H7b run quarantine the stamp.</para>
/// <para><b>Detection.</b> Comments (<c>#</c> line comments, <c>&lt;# #&gt;</c> blocks) are stripped first, so a mention in a
/// comment neither puts a script in scope nor satisfies the rule. A script is in scope when a <c>teams?</c> query's
/// <c>$filter</c> contains <c>isdefault eq true</c> as ANY clause (first or later) without a <c>_businessunitid_value eq</c>
/// clause (a read of ONE unit's default team is not an "every default team" read), and the script mentions
/// <c>teamprofiles_association</c>. An in-scope script must dot-source the helper and CALL
/// <c>Split-DvDefaultTeamsByOntologyWriterUnit -…</c>.</para>
/// <para><b>MAINTENANCE PROCEDURE</b>: a failure names the script. Dot-source the helper
/// (<c>. (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')</c>; <c>'../common/…'</c> from a subfolder), select
/// <c>_businessunitid_value</c> on the default-team read and pass it through <c>Split-DvDefaultTeamsByOntologyWriterUnit</c>;
/// add only <c>.ReaderTeams</c> to a profile, and report each of <c>.WriterUnitTeams</c> found on the profile as a FAIL
/// (<c>Get-DvOntologyWriterUnitTeamsOnProfile</c>). If the unit's name changes, change
/// <c>SecureRecordSetupProcedure.OntologyWriterBusinessUnitName</c> and the helper together.</para>
/// <para><b>RATCHET BASELINE</b> (<see cref="KnownOffenders"/>): scripts that trip the guard today but are owned by another
/// project. The rule is one-way. A NEW offender not in the list fails. A listed script that NO LONGER trips the guard also
/// fails (stale entry): delete its line from <see cref="KnownOffenders"/> in the same change that fixed the script, so the
/// list only shrinks. Never add an entry to silence a script you own; fix the script. An entry needs a written reason naming
/// the owner and the date the fix was agreed.</para>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class.</para>
/// </remarks>
public class OntologyWriterUnitScriptGuardTests
{
    private const string HelperPath = "scripts/common/OntologyWriterUnit.ps1";
    private const string ProcedurePath =
        "src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/SecureRecordSetup/SecureRecordSetupProcedure.cs";

    /// <summary>
    /// Ratchet baseline: script path (repo-relative, forward slashes) → why it is allowed to trip the guard for now.
    /// Remove an entry as soon as the script is fixed (the stale-entry check fails until you do).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownOffenders = new Dictionary<string, string>
    {
    };

    // One quoted-or-bare Web API query starting at "teams?" (up to the closing quote or end of line).
    private static readonly Regex TeamsQuery = new(@"(?<![\w])teams\?[^'""\r\n]*", RegexOptions.Compiled);

    // isdefault eq true as any clause of the $filter ($filter=... up to the next &).
    private static readonly Regex DefaultFilterClause = new(@"\$filter=[^&]*\bisdefault eq true\b", RegexOptions.Compiled);

    private static readonly Regex BlockComment = new(@"<#.*?#>", RegexOptions.Compiled | RegexOptions.Singleline);

    // A # that starts a line or follows whitespace begins a comment to end of line.
    private static readonly Regex LineComment = new(@"(?m)(?<=^|\s)#.*$", RegexOptions.Compiled);

    private static readonly Regex DotSource = new(@"common/OntologyWriterUnit\.ps1", RegexOptions.Compiled);

    // A call (a parameter follows), not a bare mention inside a string.
    private static readonly Regex SplitCall = new(@"(?<![\w'""-])Split-DvDefaultTeamsByOntologyWriterUnit\s+-\w", RegexOptions.Compiled);

    [Fact(DisplayName = "Ontology writer unit: every script that puts all default teams on a profile splits them through the helper (ratchet)")]
    public void EveryDefaultTeamProfileScriptExcludesTheOntologyWriterUnit()
    {
        var root = SourceScan.RepoRoot;
        var scripts = Directory.GetFiles(Path.Combine(root, "scripts"), "*.ps1", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(root, path).Replace('\\', '/'), Content: File.ReadAllText(path)))
            .Where(s => !s.Path.Contains("/node_modules/", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(scripts);
        var (newOffenders, staleEntries) = Evaluate(scripts, KnownOffenders.Keys.ToHashSet(StringComparer.Ordinal));

        Assert.True(
            newOffenders.Count == 0,
            "These scripts read every business-unit default team and touch a field-security profile's teams without keeping " +
            $"the ontology writer's unit out through {HelperPath} (see this test's MAINTENANCE PROCEDURE): " + string.Join("; ", newOffenders));
        Assert.True(
            staleEntries.Count == 0,
            "These KnownOffenders baseline entries no longer trip the guard (fixed, moved or deleted): delete them from " +
            "KnownOffenders so the ratchet shrinks: " + string.Join("; ", staleEntries));
        // Not vacuous: the nine scripts task 267 changed are in scope and pass.
        Assert.True(
            scripts.Count(s => IsInScope(s.Content) && !KnownOffenders.ContainsKey(s.Path)) >= 9,
            "the scan found fewer than the nine known default-team scripts");
    }

    [Fact(DisplayName = "Ontology writer unit: every baseline entry has a written reason")]
    public void EveryBaselineEntryHasAReason()
    {
        Assert.All(KnownOffenders, kv =>
        {
            Assert.StartsWith("scripts/", kv.Key, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{kv.Key} has no reason");
        });
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

    [Fact(DisplayName = "Ontology writer unit: negative control — isdefault as a LATER $filter clause is still an every-default-team read")]
    public void NegativeControl_FlagsIsDefaultAsALaterFilterClause()
    {
        const string laterClause = """
            $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid,name&$filter=teamtype eq 0 and isdefault eq true').value)
            foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid 'x' }
            """;
        const string laterClauseInterpolated = """
            $defaultTeams = @((Invoke-DvGet "teams?`$select=teamid&`$filter=statecode eq 0 and isdefault eq true and teamtype eq 0").value)
            $x = $reader.teamprofiles_association
            """;

        var offenders = Offenders(new[] { ("a.ps1", laterClause), ("b.ps1", laterClauseInterpolated) });
        Assert.Equal(2, offenders.Count);
        Assert.All(offenders, o => Assert.Contains("does not dot-source the helper", o, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Ontology writer unit: negative control — a helper mentioned only in comments is not a call")]
    public void NegativeControl_FlagsAHelperMentionedOnlyInAComment()
    {
        const string lineComments = """
            # . (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')
            $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid&$filter=isdefault eq true').value)  # TODO Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api
            foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid 'x' }
            """;
        const string blockComment = """
            <#
              . (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')
              $split = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers -DefaultTeams $teams
            #>
            $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid&$filter=isdefault eq true').value)
            foreach ($t in $defaultTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid 'x' }
            """;
        const string mentionedInAString = """
            . (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')
            Write-Host 'use Split-DvDefaultTeamsByOntologyWriterUnit'
            $defaultTeams = @((Invoke-DvGet 'teams?$select=teamid&$filter=isdefault eq true').value)
            $x = $reader.teamprofiles_association
            """;

        Assert.Equal(3, Offenders(new[] { ("a.ps1", lineComments), ("b.ps1", blockComment), ("c.ps1", mentionedInAString) }).Count);
    }

    [Fact(DisplayName = "Ontology writer unit: positive control — a split script, a one-unit read, no profile, or a commented-out read passes")]
    public void PositiveControl_AcceptsTheSanctionedShapes()
    {
        const string splits = """
            . (Join-Path $PSScriptRoot 'common/OntologyWriterUnit.ps1')
            $split = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers -DefaultTeams @((Invoke-DvGet 'teams?$select=teamid,name,_businessunitid_value&$filter=isdefault eq true').value)
            foreach ($t in $split.ReaderTeams) { Ensure-Member $readerId $ReaderProfileName 'teamprofiles_association' 'teams' $t.teamid 'x' }
            """;
        const string splitsFromSubfolder = """
            . (Join-Path $PSScriptRoot '../common/OntologyWriterUnit.ps1')
            $split = Split-DvDefaultTeamsByOntologyWriterUnit -Api $Api -Headers $headers -DefaultTeams @((Invoke-DvGet 'teams?$filter=teamtype eq 0 and isdefault eq true').value)
            $x = $reader.teamprofiles_association
            """;
        const string oneUnit = """
            $defaultTeams = @(Get-All "teams?`$select=teamid,name&`$filter=_businessunitid_value eq $buId and isdefault eq true and teamtype eq 0")
            $x = Invoke-DvGet "fieldsecurityprofiles($p)/teamprofiles_association"
            """;
        const string oneUnitFirst = """
            $defaultTeams = @(Get-All "teams?`$filter=isdefault eq true and _businessunitid_value eq $buId")
            $x = Invoke-DvGet "fieldsecurityprofiles($p)/teamprofiles_association"
            """;
        const string noProfile = "$teams = Invoke-DvGet 'teams?$select=teamid&$filter=isdefault eq true'";
        const string commentedRead = """
            # $teams = Invoke-DvGet 'teams?$select=teamid&$filter=isdefault eq true'
            <# teams?$filter=isdefault eq true #>
            $x = $reader.teamprofiles_association
            """;

        Assert.Empty(Offenders(new[]
        {
            ("a.ps1", splits), ("b.ps1", splitsFromSubfolder), ("c.ps1", oneUnit),
            ("d.ps1", oneUnitFirst), ("e.ps1", noProfile), ("f.ps1", commentedRead),
        }));
    }

    [Fact(DisplayName = "Ontology writer unit: ratchet controls — a new offender fails, a stale baseline entry fails, a baselined offender passes")]
    public void RatchetControls()
    {
        const string offender = """
            $d = @((Invoke-DvGet 'teams?$filter=isdefault eq true').value)
            $x = $reader.teamprofiles_association
            """;
        const string clean = "$x = 1";
        var baseline = new HashSet<string>(StringComparer.Ordinal) { "scripts/x/Known.ps1", "scripts/x/Fixed.ps1" };

        var (newOffenders, stale) = Evaluate(
            new[] { ("scripts/x/Known.ps1", offender), ("scripts/x/Fixed.ps1", clean), ("scripts/x/New.ps1", offender) },
            baseline);

        Assert.Single(newOffenders);
        Assert.Contains("scripts/x/New.ps1", newOffenders[0], StringComparison.Ordinal);
        Assert.Equal(new[] { "scripts/x/Fixed.ps1" }, stale);

        // A baselined entry for a script that no longer exists is stale too.
        var (_, missing) = Evaluate(new[] { ("scripts/x/Known.ps1", offender) },
            new HashSet<string>(StringComparer.Ordinal) { "scripts/x/Known.ps1", "scripts/x/Gone.ps1" });
        Assert.Equal(new[] { "scripts/x/Gone.ps1" }, missing);
    }

    private static string StripComments(string content) =>
        LineComment.Replace(BlockComment.Replace(content, string.Empty), string.Empty);

    private static bool IsInScope(string content)
    {
        var code = StripComments(content);
        return code.Contains("teamprofiles_association", StringComparison.Ordinal)
            && TeamsQuery.Matches(code).Any(m =>
                DefaultFilterClause.IsMatch(m.Value) && !m.Value.Contains("_businessunitid_value eq", StringComparison.Ordinal));
    }

    private static List<string> Offenders(IEnumerable<(string Path, string Content)> scripts)
    {
        var offenders = new List<string>();
        foreach (var (path, content) in scripts)
        {
            if (!IsInScope(content))
                continue;

            var code = StripComments(content);
            var reasons = new List<string>();
            if (!DotSource.IsMatch(code))
                reasons.Add("does not dot-source the helper");
            if (!SplitCall.IsMatch(code))
                reasons.Add("never calls Split-DvDefaultTeamsByOntologyWriterUnit");
            if (reasons.Count > 0)
                offenders.Add($"{path} ({string.Join(", ", reasons)})");
        }

        return offenders;
    }

    /// <summary>Offenders not in the baseline, and baseline entries that are not (or no longer) offenders.</summary>
    private static (List<string> NewOffenders, List<string> StaleEntries) Evaluate(
        IEnumerable<(string Path, string Content)> scripts, ISet<string> baseline)
    {
        var all = Offenders(scripts);
        // Offenders() entries are "<path> (<reasons>)"; the path is everything before the first " (".
        var offenderPaths = all.ToDictionary(o => o[..o.IndexOf(" (", StringComparison.Ordinal)], o => o, StringComparer.Ordinal);

        var newOffenders = offenderPaths.Where(kv => !baseline.Contains(kv.Key)).Select(kv => kv.Value).ToList();
        var stale = baseline.Where(p => !offenderPaths.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        return (newOffenders, stale);
    }
}
