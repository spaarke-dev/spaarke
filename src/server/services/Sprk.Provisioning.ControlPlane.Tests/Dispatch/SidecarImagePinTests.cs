// -----------------------------------------------------------------------------
// SidecarImagePinTests.cs
//
// T186 L58: the dev control plane's parameter file pinned the Exchange sidecar to `provisioning-sidecar:latest`.
// build-provisioning-sidecar.yml never pushes `latest` (only date-versioned and commit-SHA tags), so `latest` stayed the
// 2026-10-04 manual first push — older than task 251's POST /ensure-customer-mailbox — and H14m failed with HTTP 404.
// Every control-plane parameter file must name a tag CI publishes.
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Dispatch;

public sealed class SidecarImagePinTests
{
    // The tags build-provisioning-sidecar.yml pushes: YYYY.MM.DD-<run number> and the 40-hex commit SHA.
    private static readonly Regex CiTag = new(@"^(\d{4}\.\d{2}\.\d{2}-\d+|[0-9a-f]{40})$", RegexOptions.CultureInvariant);

    private static readonly Regex AcrImageTagParam = new(
        @"^\s*param\s+acrImageTag\s*=\s*'(?<image>[^']+)'", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    [Fact]
    public void T186_L58_EveryControlPlaneParameterFile_PinsTheSidecarToATagCiPublishes()
    {
        var files = Directory.GetFiles(
            Path.Combine(RepoRoot(), "infrastructure", "bicep", "parameters"), "platform-controlplane-*.bicepparam");
        files.Should().NotBeEmpty();

        var images = files
            .Select(f => (File: Path.GetFileName(f), Match: AcrImageTagParam.Match(File.ReadAllText(f))))
            .Where(x => x.Match.Success)
            .Select(x => (x.File, Image: x.Match.Groups["image"].Value))
            .ToList();
        images.Should().NotBeEmpty("the dev parameter file sets the sidecar image");

        foreach (var (file, image) in images)
        {
            IsCiPublishedTag(image).Should().BeTrue($"{file} sets acrImageTag = '{image}', which is not a tag CI publishes");
        }
    }

    [Theory]
    [InlineData("sprkcontrolplanedevacr.azurecr.io/provisioning-sidecar:2026.10.15-31", true)]
    [InlineData("sprkcontrolplanedevacr.azurecr.io/provisioning-sidecar:ed2b703f3243b91c4f776051aea71974a6e32b3f", true)]
    [InlineData("sprkcontrolplanedevacr.azurecr.io/provisioning-sidecar:latest", false)] // must fire: the L58 value
    [InlineData("sprkcontrolplanedevacr.azurecr.io/provisioning-sidecar", false)] // no tag means latest
    [InlineData("mcr.microsoft.com/appsvc/staticsite:latest", false)] // the module's placeholder default
    public void T186_L58_TagCheck_AcceptsCiTags_AndRefusesLatest(string image, bool expected)
        => IsCiPublishedTag(image).Should().Be(expected);

    private static bool IsCiPublishedTag(string image)
    {
        var lastSegment = image[(image.LastIndexOf('/') + 1)..];
        var colon = lastSegment.IndexOf(':');
        return colon >= 0 && CiTag.IsMatch(lastSegment[(colon + 1)..]);
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
