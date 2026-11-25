// -----------------------------------------------------------------------------
// DecisionRecordReaderRolePinTests.cs
//
// ebilling-r1 DEF-059, S29. H7b's constants for "Spaarke Decision Record Reader" (name, the one privilege, its depth, the
// release that ships it) are tied here to the files that define the role: SpaarkeMaster's role XML and the latest
// role-privilege-expectations JSON. A role that gains a privilege, or a release that renames it, fails here instead of
// failing on a stamp.
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.SecureRecordSetup;

public sealed class DecisionRecordReaderRolePinTests
{
    private const string RoleName = SecureRecordSetupProcedure.DecisionRecordReaderRoleName;
    private const string PrivilegeName = SecureRecordSetupProcedure.DecisionRecordReaderPrivilege;
    private const string PrivilegeDepth = SecureRecordSetupProcedure.DecisionRecordReaderPrivilegeDepth;

    [Fact]
    public void ThePin_MatchesTheRoleFileAndTheLatestRoleExpectations()
    {
        var role = XDocument.Load(Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Roles", $"{RoleName}.xml"));
        var declared = role.Root!.Element("RolePrivileges")!.Elements("RolePrivilege")
            .Select(p => (Name: (string)p.Attribute("name")!, Level: (string)p.Attribute("level")!)).ToList();
        declared.Should().Equal([(PrivilegeName, PrivilegeDepth)], "the role file declares exactly the one privilege S29 pins");
        ((string)role.Root.Attribute("name")!).Should().Be(RoleName);

        using var expectations = LatestExpectations();
        var entry = expectations.RootElement.GetProperty("roles").GetProperty(RoleName);
        entry.GetProperty("present").EnumerateObject().Select(p => (p.Name, p.Value.GetString()!)).Should()
            .Equal([(PrivilegeName, PrivilegeDepth)], "the post-import expectation is the same one privilege at the same depth");
        entry.GetProperty("absent").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void ThePin_NamesAReleaseThatIsNotNewerThanTheLatestExpectations()
    {
        // The remedy S29 prints names the release that first ships the role; the expectations of that release must exist.
        File.Exists(Path.Combine(RepoRoot(), "scripts", "provisioning", "role-privilege-expectations",
                $"spaarkemaster-{SecureRecordSetupProcedure.DecisionRecordReaderRoleMinimumRelease}.json"))
            .Should().BeTrue("the release the remedy names is one whose role expectations are recorded");
    }

    [Fact]
    public void ThePinControl_ADifferentDeclaredPrivilege_WouldFailTheComparison()
    {
        // Must-fire control: the comparison used above rejects a role that declares anything else.
        var declared = new[] { ("prvReadsprk_DecisionRecord", "Global"), ("prvWritesprk_DecisionRecord", "Global") };

        declared.Should().NotEqual([(PrivilegeName, PrivilegeDepth)]);
    }

    private static JsonDocument LatestExpectations()
    {
        var dir = Path.Combine(RepoRoot(), "scripts", "provisioning", "role-privilege-expectations");
        var latest = Directory.GetFiles(dir, "spaarkemaster-*.json")
            .OrderBy(f => Version.Parse(Path.GetFileNameWithoutExtension(f)["spaarkemaster-".Length..]))
            .Last();
        return JsonDocument.Parse(File.ReadAllText(latest));
    }

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException($"No repository root above {AppContext.BaseDirectory}.");
    }
}
