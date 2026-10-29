// -----------------------------------------------------------------------------
// CustomerOwnerRoleSetTests.cs
//
// #2429 (H7c) — L2's reader of the two declared customer owner role files: the embedded resources are the repository's
// config files (the csproj links the ONE file, never a copy), they are exactly the files scripts/Set-CustomerOwnerTeams.ps1
// reads ($DeclaredRoleFiles, in its order), and every document the script's Read-Config refuses is refused here too.
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.CustomerOwnerTeams;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.CustomerOwnerTeams;

public sealed class CustomerOwnerRoleSetTests
{
    [Fact]
    public void EmbeddedFiles_AreExactlyTheFilesTheScriptReads_InItsOrder()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "Set-CustomerOwnerTeams.ps1"));

        ScriptDeclaredRoleFiles(script).Should().Equal(
            CustomerOwnerRoleSet.Embedded.Definitions.Select(d => d.File),
            "H7c and the script must set up the same teams — a file added to one and not the other is drift");
        CustomerOwnerRoleSet.Embedded.Definitions.Select(d => d.File).Should().Equal(
            CustomerOwnerRoleSet.RestrictedContentFile, CustomerOwnerRoleSet.CustomerConfigurationFile);
    }

    [Theory]
    [InlineData(CustomerOwnerRoleSet.RestrictedContentFile)]
    [InlineData(CustomerOwnerRoleSet.CustomerConfigurationFile)]
    public void EachEmbeddedDefinition_IsTheRepositoryFile(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "config", file)));
        var root = doc.RootElement;
        var embedded = CustomerOwnerRoleSet.Embedded.Definitions.Single(d => d.File == file);

        embedded.TeamName.Should().Be(root.GetProperty("teamName").GetString());
        embedded.RoleName.Should().Be(root.GetProperty("roleName").GetString());
        embedded.Tables.Should().Equal(
            root.GetProperty("tables").EnumerateArray().Select(t => new CustomerOwnerRoleTable(
                t.GetProperty("logicalName").GetString()!, t.GetProperty("privilegeName").GetString()!)),
            "the csproj links the ONE file — never a copy");
    }

    [Fact]
    public void EmbeddedSet_NamesTheTwoTeamsTheBffResolves()
    {
        var set = CustomerOwnerRoleSet.Embedded;

        set.RestrictedContent.TeamName.Should().Be("Spaarke Restricted Content Owners");
        set.RestrictedContent.RoleName.Should().Be("Restricted Content Owner");
        set.CustomerConfiguration.TeamName.Should().Be("Spaarke Customer Configuration Owners");
        set.CustomerConfiguration.RoleName.Should().Be("Customer Configuration Owner");
        set.SetHash().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    public static TheoryData<string, string> BadDocuments() => new()
    {
        { "schemaVersion 2", Doc(schemaVersion: "2") },
        { "access Write", Doc(access: "Write") },
        { "depth Global", Doc(depth: "Global") },
        { "businessUnit root", Doc(businessUnit: "root") },
        { "no teamName", Doc(teamName: "") },
        { "no roleName", Doc(roleName: "") },
        { "no tables", Doc(tables: "") },
        { "duplicate table", Doc(tables: Entry("sprk_a", "prvReadsprk_A") + "," + Entry("SPRK_A", "prvReadsprk_A")) },
        { "entry without evidence", Doc(tables: "{\"logicalName\":\"sprk_a\",\"privilegeName\":\"p\",\"reason\":\"r\"}") },
        { "entry without privilegeName", Doc(tables: "{\"logicalName\":\"sprk_a\",\"reason\":\"r\",\"evidence\":\"e\"}") },
        { "not JSON", "{ nope" },
    };

    [Theory]
    [MemberData(nameof(BadDocuments))]
    public void BadDocument_IsRefused(string because, string json)
    {
        var act = () => CustomerOwnerRoleSet.Parse(json, Doc(teamName: "T2", roleName: "R2", tables: Entry("sprk_b", "prvReadsprk_B")));

        act.Should().Throw<InvalidOperationException>(because);
    }

    [Theory]
    [InlineData("team", "T1", "R2", "sprk_b")]
    [InlineData("role", "T2", "R1", "sprk_b")]
    [InlineData("table", "T2", "R2", "sprk_a")]
    public void TheTwoFiles_MayNotShareATeamARoleOrATable(string because, string team, string role, string table)
    {
        var act = () => CustomerOwnerRoleSet.Parse(Doc(), Doc(teamName: team, roleName: role, tables: Entry(table, "prvRead" + table)));

        act.Should().Throw<InvalidOperationException>(because);
    }

    [Fact]
    public void GoodDocuments_AreAccepted_PositiveControl()
        => CustomerOwnerRoleSet.Parse(Doc(), Doc(teamName: "T2", roleName: "R2", tables: Entry("sprk_b", "prvReadsprk_B")))
            .Definitions.Select(d => d.TeamName).Should().Equal("T1", "T2");

    /// <summary>The single-quoted file names of the script's <c>$DeclaredRoleFiles = @( … )</c> assignment, in order.</summary>
    private static IReadOnlyList<string> ScriptDeclaredRoleFiles(string script)
    {
        var match = Regex.Match(script, @"(?m)^\$DeclaredRoleFiles\s*=\s*@\((?<body>[^)]*)\)");
        match.Success.Should().BeTrue("the script declares its role files in one $DeclaredRoleFiles = @( … ) assignment");
        return Regex.Matches(match.Groups["body"].Value, "'([^']*)'").Select(m => m.Groups[1].Value).ToArray();
    }

    private static string Entry(string table, string privilege)
        => $"{{\"logicalName\":\"{table}\",\"privilegeName\":\"{privilege}\",\"reason\":\"r\",\"evidence\":\"e\"}}";

    private static string Doc(
        string schemaVersion = "1", string access = "Read", string depth = "Basic", string businessUnit = "customer",
        string teamName = "T1", string roleName = "R1", string? tables = null)
        => $"{{\"schemaVersion\":{schemaVersion},\"teamName\":\"{teamName}\",\"roleName\":\"{roleName}\"," +
           $"\"businessUnit\":\"{businessUnit}\",\"access\":\"{access}\",\"depth\":\"{depth}\"," +
           $"\"tables\":[{tables ?? Entry("sprk_a", "prvReadsprk_A")}]}}";

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("Repository root (Spaarke.sln) not found.");
    }
}
