// -----------------------------------------------------------------------------
// SpeContainerActivationGuardTests.cs
//
// #2040: Microsoft Graph creates a SharePoint Embedded container `inactive` and deletes it automatically when it is not
// activated within 24 hours. The create scripts relied on a later write to activate the container implicitly, which no
// live probe confirmed. H8 (GraphContainerProvisioner) calls /activate explicitly; every script that creates a container
// (POST to the containers collection) must too.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class SpeContainerActivationGuardTests
{
    // A POST to the containers COLLECTION creates one: the URI ends at `/containers"` (any Graph version).
    private static readonly Regex CreatesContainer = new(
        @"fileStorage/containers""\s*`?\s*(\r?\n\s*)?-Method\s+'?Post", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex ActivatesContainer = new(
        @"fileStorage/containers/[^""\s]+/activate""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Scripts that create a container but cannot run. Each entry states why.
    private static readonly Dictionary<string, string> Exempt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Create-NewContainerType.ps1"] =
            "deprecated 2026-08-30 (task 213.3): throws before its param block, so it never reaches the container create",
    };

    [Fact]
    public void Issue2040_EveryScriptThatCreatesAContainer_ActivatesIt()
    {
        var scripts = Directory.GetFiles(Path.Combine(RepoRoot(), "scripts"), "*.ps1", SearchOption.AllDirectories)
            .Where(f => CreatesContainer.IsMatch(File.ReadAllText(f)))
            .ToList();
        scripts.Should().NotBeEmpty("New-BusinessUnitContainer.ps1 and Provision-Customer.ps1 create containers");

        var missing = scripts
            .Where(f => !Exempt.ContainsKey(Path.GetFileName(f)) && !Activates(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();
        missing.Should().BeEmpty("a container that is not activated within 24 hours is deleted automatically (#2040)");
    }

    [Fact]
    public void Issue2040_ExemptScripts_StillExist_AndStillCreateAContainer()
    {
        foreach (var name in Exempt.Keys)
        {
            var path = Directory.GetFiles(Path.Combine(RepoRoot(), "scripts"), name, SearchOption.AllDirectories)
                .Should().ContainSingle($"exemption '{name}' names a script that is gone; remove it").Subject;
            CreatesContainer.IsMatch(File.ReadAllText(path)).Should().BeTrue($"'{name}' no longer creates a container; remove its exemption");
        }
    }

    [Theory]
    [InlineData("Invoke-RestMethod -Uri \"https://graph.microsoft.com/v1.0/storage/fileStorage/containers\" `\n    -Method Post -Body $b", true, false)] // must fire
    [InlineData("Invoke-RestMethod -Uri \"https://graph.microsoft.com/v1.0/storage/fileStorage/containers\" -Method Post -Body $b\n" +
                "Invoke-RestMethod -Uri \"https://graph.microsoft.com/v1.0/storage/fileStorage/containers/$id/activate\" -Method Post", true, true)]
    [InlineData("Invoke-RestMethod -Uri \"https://graph.microsoft.com/v1.0/storage/fileStorage/containers?$filter=x\" -Method Get", false, false)] // a list, not a create
    [InlineData("Invoke-RestMethod -Uri \"https://graph.microsoft.com/beta/storage/fileStorage/containers/$($c.id)\" -Method Patch", false, false)] // an update
    public void Issue2040_Detection_Controls(string script, bool creates, bool activates)
    {
        CreatesContainer.IsMatch(script).Should().Be(creates);
        Activates(script).Should().Be(activates);
    }

    private static bool Activates(string text) => ActivatesContainer.IsMatch(text);

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with SidecarImagePinTests).
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
