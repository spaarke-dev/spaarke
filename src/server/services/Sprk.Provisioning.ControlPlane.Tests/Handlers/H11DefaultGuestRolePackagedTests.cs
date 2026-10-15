// -----------------------------------------------------------------------------
// H11DefaultGuestRolePackagedTests.cs
//
// Structural fitness function (T218e AC3): the role H11 gives every B2B guest by
// default must be a role the package ships. H6 installs the committed SpaarkeMaster
// source (src/dataverse/solutions/SpaarkeMaster, CI-packed by 218d); if the default
// names a role the package lacks, every guest run fails at H11 with
// userprov-security-role-not-found.
//
// The role list is read from the committed export (Roles/*.xml, <Role name="…">).
// Controls (tests/CLAUDE.md): the negative control proves the scan does not find a
// role that is absent; the positive control proves it finds one that is present.
//
// MAINTENANCE: if this fails, either the role was renamed or dropped in dev and the
// package re-exported (restore it, or change H11UserProvisioningOptions.
// DefaultGuestSecurityRoleName together with the guide), or the export layout changed
// (fix PackagedRoleNames and keep both controls passing).
// -----------------------------------------------------------------------------

using System.Xml.Linq;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H11DefaultGuestRolePackagedTests
{
    [Fact]
    public void DefaultGuestRoleIsShippedInTheCommittedPackage()
    {
        var rolesFolder = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");
        Directory.Exists(rolesFolder).Should().BeTrue("the SpaarkeMaster source export is committed (T218e)");

        PackagedRoleNames(rolesFolder).Should().Contain(H11UserProvisioningOptions.DefaultGuestSecurityRoleName,
            "H11 assigns it to every guest when GuestSecurityRoleNames is not configured");
    }

    [Fact]
    public void RoleScanFindsARolePresentInTheFolder_PositiveControl()
    {
        var folder = SeededRolesFolder("Spaarke Basic User", "Spaarke Office Add In User");
        PackagedRoleNames(folder).Should().Contain("Spaarke Basic User");
    }

    [Fact]
    public void RoleScanDoesNotFindARoleAbsentFromTheFolder_NegativeControl()
    {
        var folder = SeededRolesFolder("Spaarke Office Add In User");
        PackagedRoleNames(folder).Should().NotContain("Spaarke Basic User");
    }

    private static IReadOnlyList<string> PackagedRoleNames(string rolesFolder) =>
        Directory.EnumerateFiles(rolesFolder, "*.xml")
            .Select(f => (string?)XDocument.Load(f).Root?.Attribute("name"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToList();

    private static string SeededRolesFolder(params string[] roleNames)
    {
        // Same shape as the pac unpack: one file per role, <Role id="{…}" name="…">.
        var folder = Path.Combine(Path.GetTempPath(), $"h11-roles-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        foreach (var name in roleNames)
        {
            new XDocument(new XElement("Role",
                    new XAttribute("id", $"{{{Guid.NewGuid()}}}"),
                    new XAttribute("name", name),
                    new XElement("IsCustomizable", 1)))
                .Save(Path.Combine(folder, $"{name}.xml"));
        }
        return folder;
    }

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with ArmTemplateInspectorTests).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var gitMarker = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(gitMarker) || File.Exists(gitMarker)) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate the repo root walking up from '{AppContext.BaseDirectory}'.");
    }
}
