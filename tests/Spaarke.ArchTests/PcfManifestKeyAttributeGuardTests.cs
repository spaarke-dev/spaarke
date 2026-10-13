using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// A PCF manifest whose <c>*-key</c> attribute (<c>description-key</c>, <c>display-name-key</c>) contains an apostrophe
/// fails Dataverse's XSD (<c>noAposStringType</c>), and the whole solution import fails with it. <c>pac solution import</c>
/// still exits 0, so a deploy script that trusts the exit code reports success while the old control stays live
/// (word-add-in task 127, 2026-10-10: SemanticSearchControl 1.1.85 and VisualHost 1.4.40 both failed this way).
/// This guard moves the rule from the pcf-deploy skill's failure table to the build.
/// </summary>
/// <remarks>
/// Scans every <c>ControlManifest.Input.xml</c> and every committed <c>ControlManifest.xml</c> (the copy the pack scripts
/// zip) under <c>src/client/pcf</c>, skipping <c>node_modules</c>, <c>out</c>, <c>bin</c> and <c>obj</c>. Apostrophes in XML
/// comments and in other attributes are not checked: the XSD rejects them only in the <c>*-key</c> values. No baseline:
/// there were no violations once the two task-127 descriptions were fixed.
/// </remarks>
public class PcfManifestKeyAttributeGuardTests
{
    private static readonly string[] SkippedFolders = ["node_modules", "out", "bin", "obj"];

    [Fact(DisplayName = "PCF manifests: no *-key attribute value contains an apostrophe (Dataverse XSD noAposStringType)")]
    public void NoManifestKeyAttributeContainsAnApostrophe()
    {
        var manifests = ManifestFiles().ToList();
        Assert.True(manifests.Count >= 30,
            $"Scanner found only {manifests.Count} PCF manifests under src/client/pcf (expected >= 30; 39 on 2026-10-10). Repo root resolved to '{SourceScan.RepoRoot}'.");

        var violations = manifests
            .SelectMany(path => ApostropheKeyAttributes(File.ReadAllText(path))
                .Select(v => $"{Path.GetRelativePath(SourceScan.RepoRoot, path)}: {v}"))
            .ToList();

        Assert.True(violations.Count == 0,
            "Dataverse rejects an apostrophe in a PCF manifest *-key attribute (XSD noAposStringType), and the solution import "
            + "fails while pac still exits 0. Reword without the apostrophe (\"the X of this environment\", not \"this environment's X\"):\n  "
            + string.Join("\n  ", violations));
    }

    [Fact(DisplayName = "PCF manifest guard control: an apostrophe in description-key or display-name-key is reported, literal or as &apos;")]
    public void GuardFiresOnApostropheInKeyAttributes()
    {
        const string manifest = """
            <manifest>
              <control namespace="N" constructor="C" version="1.0.0" display-name-key="Owner's view" description-key="Plain">
                <property name="p" display-name-key="P" description-key="Uses this environment&apos;s key" of-type="SingleLine.Text" usage="input" />
              </control>
            </manifest>
            """;

        var found = ApostropheKeyAttributes(manifest);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, f => f.Contains("display-name-key", StringComparison.Ordinal) && f.Contains("Owner's view", StringComparison.Ordinal));
        Assert.Contains(found, f => f.Contains("description-key", StringComparison.Ordinal) && f.Contains("environment's key", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "PCF manifest guard control: apostrophes in comments and in non-key attributes are not reported")]
    public void GuardIgnoresCommentsAndOtherAttributes()
    {
        const string manifest = """
            <manifest>
              <!-- the control's comment may say anything -->
              <control namespace="N" constructor="C" version="1.0.0" display-name-key="Clean name" description-key="Clean description">
                <property name="p" display-name-key="P" description-key="D" default-value="it's fine here" of-type="SingleLine.Text" usage="input" />
              </control>
            </manifest>
            """;

        Assert.Empty(ApostropheKeyAttributes(manifest));
    }

    /// <summary>Every <c>*-key</c> attribute whose value (entities decoded) contains an apostrophe, as "element@attr = value".</summary>
    internal static List<string> ApostropheKeyAttributes(string manifestXml) =>
        XDocument.Parse(manifestXml)
            .Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName.EndsWith("-key", StringComparison.Ordinal) && a.Value.Contains('\''))
            .Select(a => $"<{a.Parent!.Name.LocalName}> {a.Name.LocalName} = \"{a.Value}\"")
            .ToList();

    private static IEnumerable<string> ManifestFiles()
    {
        var pcfRoot = Path.Combine(SourceScan.RepoRoot, "src", "client", "pcf");
        return Directory
            .EnumerateFiles(pcfRoot, "ControlManifest*.xml", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f) is "ControlManifest.Input.xml" or "ControlManifest.xml")
            .Where(f => !SkippedFolders.Any(s => f.Contains($"{Path.DirectorySeparatorChar}{s}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)));
    }
}
