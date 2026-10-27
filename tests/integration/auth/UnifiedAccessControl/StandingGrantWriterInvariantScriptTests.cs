using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// C4: the standing-grant writer invariant that <c>scripts/Set-ExternalAccessContactSecuritySchema.ps1 -Verify</c> (step h)
/// reports, exercised by running its functions (<c>scripts/common/StandingGrantWriterInvariant.ps1</c>) in <c>pwsh</c> over
/// rows shaped as the Web API serves them. Offline: no Dataverse. The rule: the BFF application user updates
/// <c>contact.sprk_standinggrant</c> only through its DIRECT Standing Grant Administrators membership, and no other profile it
/// holds (directly or through a team) grants create or update on it.
/// </summary>
/// <remarks>Requires PowerShell 7 (<c>pwsh</c>) on the PATH, as <see cref="GrantProvenanceBackfillScriptTests"/> does.</remarks>
public class StandingGrantWriterInvariantScriptTests
{
    private const string Sga = "5a5a5a5a-0000-4000-8000-00000000c4c4";
    private const string Writers = "77777777-0000-4000-8000-00000000c4c4";
    private const string Other = "88888888-0000-4000-8000-00000000c4c4";
    private const string User = "11111111-0000-4000-8000-00000000c4c4";
    private const string SystemAdministrator = "99999999-0000-4000-8000-00000000c5c5";

    /// <summary>S19's pinned name (<c>SecureRecordSetupProcedure.SystemAdministratorProfileName</c>), as step h passes it.</summary>
    private const string SystemAdministratorName = "System Administrator";

    private const string SystemAdministratorInfo =
        "app user 'BFF' holds System Administrator: field security does not restrict it; the direct-SGA rule constrains every other route.";

    private static object Profile(string id, string name) => new { fieldsecurityprofileid = id, name };

    private static object Permission(string profileId, int create, int update)
        => new { _fieldsecurityprofileid_value = profileId, canread = 4, cancreate = create, canupdate = update };

    private static readonly object SgaWrites = Permission(Sga, 4, 4);

    private static List<(string State, string What)> Findings(object[] direct, object[] team, object[] writers)
    {
        var output = RunPwsh($$"""
            $direct = @('{{Json(direct)}}' | ConvertFrom-Json)
            $team = @('{{Json(team)}}' | ConvertFrom-Json)
            $writers = @('{{Json(writers)}}' | ConvertFrom-Json)
            $f = Get-StandingGrantWriterFinding -UserName "app user 'BFF'" -StandingGrantProfileId '{{Sga}}' `
                -StandingGrantProfileName 'Standing Grant Administrators' -SystemAdministratorProfileName '{{SystemAdministratorName}}' `
                -Column 'contact.sprk_standinggrant' `
                -DirectProfiles $direct -TeamProfiles $team -WriterPermissions $writers
            ConvertTo-Json -InputObject @($f | ForEach-Object { [pscustomobject]@{ State = $_.State; What = $_.What } }) -Depth 4 -Compress
            """);
        using var doc = JsonDocument.Parse(output);
        return doc.RootElement.EnumerateArray()
            .Select(e => (e.GetProperty("State").GetString()!, e.GetProperty("What").GetString()!))
            .ToList();
    }

    // ── Must not fire ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ADirectMember_WithNoOtherWritingProfile_Passes()
    {
        var findings = Findings(
            direct: new[] { Profile(Sga, "Standing Grant Administrators"), Profile(Writers, "Spaarke BFF-Managed Field Writers") },
            team: Array.Empty<object>(),
            writers: new[] { SgaWrites, Permission(Other, 0, 4) });

        findings.Should().OnlyContain(f => f.State == "OK");
        findings.Should().HaveCount(2);
    }

    [Fact]
    public void AnotherWritingProfile_TheBffDoesNotHold_DoesNotFire()
    {
        var findings = Findings(
            direct: new[] { Profile(Sga, "Standing Grant Administrators") },
            team: new[] { Profile(Writers, "Spaarke BFF-Managed Field Writers") },
            writers: new[] { SgaWrites, Permission(Other, 4, 4) });

        findings.Should().OnlyContain(f => f.State == "OK");
    }

    // ── Must fire ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NotADirectMember_Fails()
    {
        var findings = Findings(direct: Array.Empty<object>(), team: Array.Empty<object>(), writers: new[] { SgaWrites });

        findings.Should().ContainSingle(f => f.State == "FAIL" && f.What.Contains("not a DIRECT member", StringComparison.Ordinal));
    }

    [Fact]
    public void HoldingStandingGrantAdministrators_ThroughATeam_Fails()
    {
        var findings = Findings(
            direct: new[] { Profile(Sga, "Standing Grant Administrators") },
            team: new[] { Profile(Sga, "Standing Grant Administrators") },
            writers: new[] { SgaWrites });

        findings.Should().Contain(f => f.State == "FAIL" && f.What.Contains("through a team", StringComparison.Ordinal));
        findings.Should().NotContain(f => f.What.StartsWith("no other profile", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, 0, 4)]
    [InlineData(true, 4, 0)]
    [InlineData(false, 0, 4)]
    [InlineData(false, 4, 0)]
    public void AnotherProfileTheBffHolds_GrantingCreateOrUpdate_Fails(bool direct, int create, int update)
    {
        var other = Profile(Other, "Some Other Profile");
        var findings = Findings(
            direct: direct ? new[] { Profile(Sga, "Standing Grant Administrators"), other } : new[] { Profile(Sga, "Standing Grant Administrators") },
            team: direct ? Array.Empty<object>() : new[] { other },
            writers: new[] { SgaWrites, Permission(Other, create, update) });

        findings.Should().ContainSingle(f => f.State == "FAIL"
                                             && f.What.Contains("Some Other Profile", StringComparison.Ordinal)
                                             && f.What.Contains(direct ? "directly" : "through a team", StringComparison.Ordinal));
    }

    [Fact]
    public void StandingGrantAdministrators_WithoutUpdate_Fails()
    {
        var findings = Findings(
            direct: new[] { Profile(Sga, "Standing Grant Administrators") },
            team: Array.Empty<object>(),
            writers: Array.Empty<object>());

        findings.Should().Contain(f => f.State == "FAIL" && f.What.Contains("grants no update", StringComparison.Ordinal));
    }

    // ── C5: the System Administrator profile ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// MUST NOT FIRE: a BFF that holds S19's pinned System Administrator profile (directly or through a team), which grants
    /// the column, passes (no FAIL, so -Verify exits 0) and is noted as INFO, with the exact text, on every run.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HoldingSystemAdministrator_Passes_AndIsNotedAsInfo(bool direct, bool grantsTheColumn)
    {
        var admin = Profile(SystemAdministrator, SystemAdministratorName);
        var findings = Findings(
            direct: direct ? new[] { Profile(Sga, "Standing Grant Administrators"), admin } : new[] { Profile(Sga, "Standing Grant Administrators") },
            team: direct ? Array.Empty<object>() : new[] { admin },
            writers: grantsTheColumn ? new[] { SgaWrites, Permission(SystemAdministrator, 4, 4) } : new[] { SgaWrites });

        findings.Should().NotContain(f => f.State == "FAIL");
        findings.Should().ContainSingle(f => f.State == "INFO").Which.What.Should().Be(SystemAdministratorInfo);

        // An INFO is not a gap: -Verify counts only MISSING and FAIL, so it exits 0.
        File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Set-ExternalAccessContactSecuritySchema.ps1"))
            .Should().Contain("if ($State -in 'MISSING', 'FAIL') { $gaps.Add($What) }");
    }

    /// <summary>MUST NOT FIRE: a BFF that does not hold System Administrator gets no INFO line.</summary>
    [Fact]
    public void NotHoldingSystemAdministrator_HasNoInfo()
    {
        var findings = Findings(
            direct: new[] { Profile(Sga, "Standing Grant Administrators") },
            team: Array.Empty<object>(),
            writers: new[] { SgaWrites, Permission(SystemAdministrator, 4, 4) });

        findings.Should().NotContain(f => f.State == "INFO");
        findings.Should().OnlyContain(f => f.State == "OK");
    }

    /// <summary>
    /// MUST FIRE: the exemption is System Administrator alone. Another profile the BFF holds that grants the column still
    /// fails beside it, and so does a profile whose name only resembles the pinned one.
    /// </summary>
    [Theory]
    [InlineData("Some Other Profile")]
    [InlineData("system administrator")]
    [InlineData("System Administrators")]
    public void AnotherWritingProfile_BesideSystemAdministrator_StillFails(string otherName)
    {
        var findings = Findings(
            direct: new[] { Profile(Sga, "Standing Grant Administrators"), Profile(SystemAdministrator, SystemAdministratorName), Profile(Other, otherName) },
            team: Array.Empty<object>(),
            writers: new[] { SgaWrites, Permission(SystemAdministrator, 4, 4), Permission(Other, 0, 4) });

        findings.Should().ContainSingle(f => f.State == "FAIL" && f.What.Contains($"'{otherName}'", StringComparison.Ordinal));
        findings.Should().ContainSingle(f => f.State == "INFO");
    }

    /// <summary>MUST FIRE: Standing Grant Administrators held only through a team fails, System Administrator or not.</summary>
    [Fact]
    public void TeamOnlyStandingGrantAdministrators_WithSystemAdministrator_StillFails()
    {
        var findings = Findings(
            direct: new[] { Profile(SystemAdministrator, SystemAdministratorName) },
            team: new[] { Profile(Sga, "Standing Grant Administrators") },
            writers: new[] { SgaWrites, Permission(SystemAdministrator, 4, 4) });

        findings.Should().Contain(f => f.State == "FAIL" && f.What.Contains("not a DIRECT member", StringComparison.Ordinal));
        findings.Should().Contain(f => f.State == "FAIL" && f.What.Contains("through a team", StringComparison.Ordinal));
    }

    // ── The queries ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheQueries_AreReadOnly_AndTheTeamLegIsOneServerSideJoin()
    {
        var output = RunPwsh($"""
            $q = Get-StandingGrantWriterQuery -SystemUserId '{User.ToUpperInvariant()}' -Table 'contact' -Column 'sprk_standinggrant'
            ConvertTo-Json -InputObject ([pscustomobject]$q) -Compress
            """);
        using var doc = JsonDocument.Parse(output);
        var q = doc.RootElement;

        q.GetProperty("DirectProfiles").GetString().Should().Be(
            $"fieldsecurityprofiles?$select=fieldsecurityprofileid,name&$filter=systemuserprofiles_association/any(u:u/systemuserid eq {User})");
        q.GetProperty("TeamProfiles").GetString().Should().Be(
            "fieldsecurityprofiles?$select=fieldsecurityprofileid,name&$filter="
            + $"teamprofiles_association/any(t:t/teammembership_association/any(u:u/systemuserid eq {User}))");
        q.GetProperty("Writers").GetString().Should().Be(
            "fieldpermissions?$select=canread,cancreate,canupdate,_fieldsecurityprofileid_value&$filter=entityname eq 'contact' "
            + "and attributelogicalname eq 'sprk_standinggrant' and (cancreate eq 4 or canupdate eq 4)");
    }

    // ── pwsh ─────────────────────────────────────────────────────────────────────────────────────────────────────

    private static string Json(object value) => JsonSerializer.Serialize(value).Replace("'", "''", StringComparison.Ordinal);

    private static string RunPwsh(string body)
    {
        var helper = Path.Combine(RepoRoot(), "scripts", "common", "StandingGrantWriterInvariant.ps1");
        File.Exists(helper).Should().BeTrue($"{helper} is the invariant the schema script's -Verify dot-sources");

        var script = "$ErrorActionPreference = 'Stop'\n" + $". '{helper.Replace("'", "''", StringComparison.Ordinal)}'\n" + body;
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(script);

        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new InvalidOperationException("PowerShell 7 (pwsh) must be on the PATH to exercise the -Verify invariant.", ex);
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit(120_000).Should().BeTrue("pwsh must finish");
            process.ExitCode.Should().Be(0, $"the invariant script failed: {stderr.Result}");
            return stdout.Result.Trim();
        }
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
