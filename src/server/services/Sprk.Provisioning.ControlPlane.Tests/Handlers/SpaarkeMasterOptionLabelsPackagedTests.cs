// -----------------------------------------------------------------------------
// SpaarkeMasterOptionLabelsPackagedTests.cs
//
// T186 L39 — live, H6's managed import of SpaarkeMaster 1.2.3.0 into a fresh stamp failed: "OptionSets import:
// FAILURE: Option Label ... description is null or empty ... language: 1033". Set-ContactIdentityBindingSchema.ps1 and
// Set-AssignedAccessLedgerSchema.ps1 had created 22 options with EMPTY labels in spaarkedev1 ($Options[$value] on an
// [ordered] dictionary with int keys indexes by position and returned $null), and the export carried them into the
// package. An unmanaged import over an existing option tolerates that; a fresh managed install does not.
//
// This structural test reads the committed package source and fails on any option (global choice or a column's local
// choice) without an English label, so an export that carries one fails CI before it can be published.
//
// Controls (tests/CLAUDE.md): the scan is run against seeded XML with and without an empty label.
// -----------------------------------------------------------------------------

using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class SpaarkeMasterOptionLabelsPackagedTests
{
    private const string English = "1033";

    [Fact]
    public void EveryPackagedOption_HasAnEnglishLabel()
    {
        var package = PackagePath();
        var files = Directory.EnumerateFiles(Path.Combine(package, "OptionSets"), "*.xml")
            .Concat(Directory.EnumerateFiles(Path.Combine(package, "Entities"), "Entity.xml", SearchOption.AllDirectories))
            .ToList();
        files.Should().NotBeEmpty("the scan must read the package source, not an empty folder");

        var unlabelled = files
            .SelectMany(f => UnlabelledOptions(XDocument.Load(f)).Select(o => $"{Path.GetRelativePath(package, f)}: {o}"))
            .ToList();

        unlabelled.Should().BeEmpty(
            "a managed import into a new environment refuses an option without a label (T186 L39). Label the option in " +
            "spaarkedev1, bump the version and re-export (docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md)");
    }

    [Fact]
    public void Scan_FindsAnEmptyOrMissingLabel_AndPassesALabelledOne()
    {
        var seeded = XDocument.Parse("""
            <Entity><attributes><attribute><optionset Name="sprk_x">
              <options>
                <option value="100000000"><labels><label description="Granted" languagecode="1033" /></labels></option>
                <option value="100000001"><labels><label description="" languagecode="1033" /></labels></option>
                <option value="100000002"><labels><label description="Accordé" languagecode="1036" /></labels></option>
                <option value="100000003"><labels><label description="   " languagecode="1033" /></labels></option>
              </options>
            </optionset></attribute></attributes></Entity>
            """);

        UnlabelledOptions(seeded).Should().Equal("sprk_x 100000001", "sprk_x 100000002", "sprk_x 100000003");
        UnlabelledOptions(XDocument.Parse("""
            <optionset Name="sprk_y"><options>
              <option value="1"><labels><label description="External" languagecode="1033" /></labels></option>
            </options></optionset>
            """)).Should().BeEmpty();
    }

    /// <summary>"{choice} {value}" for each option whose English label is missing or blank.</summary>
    private static IReadOnlyList<string> UnlabelledOptions(XDocument document)
        => document.Descendants("option")
            .Where(o => !o.Elements("labels").Elements("label").Any(l =>
                (string?)l.Attribute("languagecode") == English && !string.IsNullOrWhiteSpace((string?)l.Attribute("description"))))
            .Select(o => $"{(string?)o.Ancestors("optionset").FirstOrDefault()?.Attribute("Name") ?? "?"} {(string?)o.Attribute("value")}")
            .ToList();

    private static string PackagePath()
        => Path.Combine(RepoRoot(), "src", "dataverse", "solutions", "SpaarkeMaster");

    private static string RepoRoot()
    {
        // A worktree's .git is a FILE; a regular checkout's is a directory (parity with ContactIdentityBindingSchemaPackagedTests).
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
