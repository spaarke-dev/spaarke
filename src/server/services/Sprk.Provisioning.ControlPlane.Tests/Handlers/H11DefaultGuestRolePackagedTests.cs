// -----------------------------------------------------------------------------
// H11DefaultGuestRolePackagedTests.cs
//
// T218e AC3: the role H11 gives every B2B guest by default must be a role the
// package ships. H6 installs the committed SpaarkeMaster source (src/dataverse/
// solutions/SpaarkeMaster, CI-packed by 218d); if the default names a role the
// package lacks, every guest run fails at H11 with userprov-security-role-not-found.
//
// The role list is read from the committed export (Roles/*.xml, <Role name="…">),
// so renaming the role in dev and re-exporting, or changing the default, breaks
// this test instead of a customer run.
// -----------------------------------------------------------------------------

using System.Xml.Linq;
using FluentAssertions;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class H11DefaultGuestRolePackagedTests
{
    [Fact]
    public void DefaultGuestSecurityRoleName_InCommittedPackage_IsAShippedRole()
    {
        var rolesFolder = Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster", "Roles");
        Directory.Exists(rolesFolder).Should().BeTrue("the SpaarkeMaster source export is committed (T218e)");

        var packagedRoles = Directory.EnumerateFiles(rolesFolder, "*.xml")
            .Select(f => (string?)XDocument.Load(f).Root?.Attribute("name"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        packagedRoles.Should().Contain(H11UserProvisioningOptions.DefaultGuestSecurityRoleName,
            "H11 assigns it to every guest when GuestSecurityRoleNames is not configured");
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
