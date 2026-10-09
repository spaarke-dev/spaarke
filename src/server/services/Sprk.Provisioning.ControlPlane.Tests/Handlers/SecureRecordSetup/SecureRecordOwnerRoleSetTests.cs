// -----------------------------------------------------------------------------
// SecureRecordOwnerRoleSetTests.cs
//
// T256 (H7b) — L2's reader of config/secure-record-owner-role.json: the embedded live file parses, and every document
// the BFF's reader and scripts/Set-SecureRecordOwnerRolePrivileges.ps1 refuse is refused here too. (The cross-reader
// parity — both readers over the same documents — is Spaarke.ArchTests SecureRecordOwnerRoleSetParityTests.)
// -----------------------------------------------------------------------------

using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.SecureRecordSetup;

public sealed class SecureRecordOwnerRoleSetTests
{
    [Fact]
    public void EmbeddedLiveFile_Parses_AndNamesTheContainedRoleAndUnit()
    {
        var set = SecureRecordOwnerRoleSet.Embedded;

        set.RoleName.Should().Be("Secure Record Owner");
        set.BusinessUnitName.Should().Be("Secure Record");
        set.Tables.Should().NotBeEmpty();
        set.Tables.Should().Contain(t => t.LogicalName == "sprk_document" && t.PrivilegeName == "prvReadsprk_Document");
        set.SetHash().Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void EmbeddedFile_IsByteForByteTheRepositoryFile()
    {
        var repoFile = Path.Combine(RepoRoot(), "config", "secure-record-owner-role.json");
        var fromDisk = SecureRecordOwnerRoleSet.Parse(File.ReadAllText(repoFile));

        fromDisk.Tables.Should().Equal(SecureRecordOwnerRoleSet.Embedded.Tables, "the csproj links the ONE file — never a copy");
    }

    public static TheoryData<string, string> BadDocuments() => new()
    {
        { "schemaVersion 2", Doc(schemaVersion: "2") },
        { "access Write", Doc(access: "Write") },
        { "depth Global", Doc(depth: "Global") },
        { "no roleName", Doc(roleName: "") },
        { "no tables", Doc(tables: "") },
        { "duplicate table", Doc(tables: Entry("sprk_a", "prvReadsprk_A") + "," + Entry("SPRK_A", "prvReadsprk_A")) },
        { "entry without evidence", Doc(tables: "{\"logicalName\":\"sprk_a\",\"privilegeName\":\"p\",\"reason\":\"r\"}") },
        { "entry without reason", Doc(tables: "{\"logicalName\":\"sprk_a\",\"privilegeName\":\"p\",\"evidence\":\"e\"}") },
        { "entry without privilegeName", Doc(tables: "{\"logicalName\":\"sprk_a\",\"reason\":\"r\",\"evidence\":\"e\"}") },
        { "not JSON", "{ nope" },
    };

    [Theory]
    [MemberData(nameof(BadDocuments))]
    public void BadDocument_IsRefused(string because, string json)
    {
        var act = () => SecureRecordOwnerRoleSet.Parse(json);

        act.Should().Throw<InvalidOperationException>(because).WithMessage("*not a valid codified set*");
    }

    [Fact]
    public void GoodDocument_IsAccepted_PositiveControl()
        => SecureRecordOwnerRoleSet.Parse(Doc()).Tables.Should().ContainSingle().Which.PrivilegeName.Should().Be("prvReadsprk_A");

    internal static string Entry(string table, string privilege)
        => $"{{\"logicalName\":\"{table}\",\"privilegeName\":\"{privilege}\",\"reason\":\"r\",\"evidence\":\"e\"}}";

    internal static string Doc(
        string schemaVersion = "1", string access = "Read", string depth = "Basic", string roleName = "Secure Record Owner",
        string? tables = null)
        => $"{{\"schemaVersion\":{schemaVersion},\"roleName\":\"{roleName}\",\"businessUnitName\":\"Secure Record\"," +
           $"\"access\":\"{access}\",\"depth\":\"{depth}\",\"tables\":[{tables ?? Entry("sprk_a", "prvReadsprk_A")}]}}";

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("Repository root (Spaarke.sln) not found above the test output.");
    }
}
