using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// Lock-step pins for unified-access-control-r2 task 168's regarding-filing lock (decision round 44, integration work with
/// the 168 merge). Each pins two artefacts that must move together and that nothing else ties:
/// <list type="number">
///   <item><c>scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1</c>'s <c>$MinCustomizerVersion</c> equals the
///   <c>SpaarkeGridCustomizer</c> PCF manifest version. With a lower minimum, <c>-Verify</c> would pass against a deployed
///   customizer that leaves the regarding pair and the intermediate lookups editable (round 44: v1.1.0 locked only the
///   roots — ADR-003, fail closed).</item>
///   <item><c>config/regarding-filing-columns.json</c> <c>rootColumns</c> equals
///   <see cref="CoreAncestorResolver.CoreAncestorLookups"/>'s lookup attributes — the JSON's own "howToExtend" says the
///   two change only together; the forms and the grid lock exactly the columns the server stamps.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para><b>MAINTENANCE PROCEDURE.</b> A version bump of SpaarkeGridCustomizer that changes what it locks raises
/// <c>$MinCustomizerVersion</c> in the same change (and a bump that does not still moves both, so the minimum names a
/// version that exists). A change to <c>CoreAncestorLookups</c> changes <c>rootColumns</c> in the same change, then re-runs
/// the two form scripts and redeploys the customizer. Never make a failure here pass by editing only one side.</para>
/// <para>Per <c>tests/CLAUDE.md</c> "Structural fitness functions" this file is MAINTAIN-class.</para>
/// </remarks>
public class RegardingFilingLockStepTests
{
    private const string GridScriptPath = "scripts/Set-SpaarkeGridCustomizerOnChildGrids.ps1";
    private const string ManifestPath = "src/client/pcf/SpaarkeGridCustomizer/ControlManifest.Input.xml";
    private const string FilingColumnsPath = "config/regarding-filing-columns.json";

    [Fact(DisplayName = "Task 168 / round 44: the grid script's $MinCustomizerVersion is the SpaarkeGridCustomizer manifest version")]
    public void GridScriptMinimumIsTheCustomizerManifestVersion()
    {
        var scriptVersion = ReadMinCustomizerVersion(Read(GridScriptPath));
        var manifestVersion = ReadManifestVersion(Read(ManifestPath));

        Assert.True(
            scriptVersion == manifestVersion,
            $"{GridScriptPath} requires SpaarkeGridCustomizer v{scriptVersion}, but {ManifestPath} builds v{manifestVersion}. "
            + "Move them together (see this test's MAINTENANCE PROCEDURE): a lower minimum lets -Verify pass against a "
            + "customizer that does not lock every filing column.");
    }

    [Fact(DisplayName = "Task 168 / round 44: regarding-filing-columns.json rootColumns are CoreAncestorResolver.CoreAncestorLookups")]
    public void FilingColumnRootsAreTheServerCoreAncestorLookups()
    {
        var roots = ReadRootColumns(Read(FilingColumnsPath));
        var server = CoreAncestorResolver.CoreAncestorLookups
            .Select(l => l.LookupAttribute.ToLowerInvariant())
            .ToList();

        Assert.True(
            roots.OrderBy(r => r, StringComparer.Ordinal).SequenceEqual(server.OrderBy(s => s, StringComparer.Ordinal)),
            $"{FilingColumnsPath} rootColumns [{string.Join(", ", roots)}] differ from CoreAncestorResolver.CoreAncestorLookups "
            + $"[{string.Join(", ", server)}]. They change only together (the JSON's howToExtend; this test's MAINTENANCE PROCEDURE).");
        Assert.Equal(roots.Count, roots.Distinct(StringComparer.Ordinal).Count());
    }

    private const string PickerScriptPath = "scripts/Add-RegardingFilingPickerToForms.ps1";

    [Fact(DisplayName = "Task 168 / round 44: the picker derives its required pair from the one list — no column named in the derivation; optional columns are pair columns")]
    public void PickerRequiredPairIsDerivedFromTheOneList()
    {
        var derivation = ReadRequiredPairAssignment(Read(PickerScriptPath));
        Assert.True(
            !Regex.IsMatch(derivation, @"'sprk_[a-z_]+'", RegexOptions.IgnoreCase) && derivation.Contains("OptionalPairTextColumns", StringComparison.Ordinal),
            $"{PickerScriptPath}'s $RequiredPairColumns must be derived from config/regarding-filing-columns.json "
            + "(recordTypeColumn + pairTextColumns minus optionalPairTextColumns), naming no column itself (decision round 44). Found: "
            + derivation);

        using var doc = JsonDocument.Parse(Read(FilingColumnsPath));
        var pair = doc.RootElement.GetProperty("pairTextColumns").EnumerateArray().Select(e => e.GetString()!.ToLowerInvariant()).ToHashSet();
        var optional = doc.RootElement.GetProperty("optionalPairTextColumns").EnumerateArray().Select(e => e.GetString()!.ToLowerInvariant()).ToList();
        var stray = optional.Where(o => !pair.Contains(o)).ToList();
        Assert.True(stray.Count == 0,
            $"{FilingColumnsPath} optionalPairTextColumns names [{string.Join(", ", stray)}], which is not in pairTextColumns.");
    }

    /// <summary>The ONE <c>$RequiredPairColumns = …</c> assignment line of the picker script.</summary>
    internal static string ReadRequiredPairAssignment(string script)
    {
        var matches = Regex.Matches(script, @"^\$RequiredPairColumns\s*=.*$", RegexOptions.Multiline);
        if (matches.Count != 1)
            throw new InvalidOperationException($"expected exactly one $RequiredPairColumns assignment, found {matches.Count}");
        return matches[0].Value;
    }

    // ── Negative controls: the readers fail on what the pins exist to catch ─────────────────────────────────

    [Fact(DisplayName = "Task 168 lock-step negative control: the readers see a lowered minimum, a moved manifest version and a drifted root list")]
    public void NegativeControl_TheReadersSeeEachDrift()
    {
        Assert.Equal(new Version(1, 1, 0), ReadMinCustomizerVersion("$MinCustomizerVersion = [version]'1.1.0'"));
        Assert.Equal(new Version(1, 2, 0), ReadManifestVersion(
            """<?xml version="1.0"?><manifest><control namespace="Spaarke.Controls" constructor="SpaarkeGridCustomizer" version="1.2.0" /></manifest>"""));
        Assert.Throws<InvalidOperationException>(() => ReadMinCustomizerVersion("$MinVersion = '1.1.1'"));
        var hardCoded = ReadRequiredPairAssignment(
            "$RequiredPairColumns = @(@($RecordTypeColumn) + @($PairTextColumns | Where-Object { $_ -cne 'sprk_regardingrecordnumber' }))");
        Assert.Matches(@"'sprk_[a-z_]+'", hardCoded);

        var drifted = ReadRootColumns("""{ "rootColumns": ["sprk_regardingproject", "sprk_regardingmatter", "sprk_regardingworkassignment"] }""");
        var server = CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute.ToLowerInvariant()).OrderBy(s => s, StringComparer.Ordinal);
        Assert.False(drifted.OrderBy(r => r, StringComparer.Ordinal).SequenceEqual(server));
    }

    // ── Readers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(SourceScan.RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>The ONE <c>$MinCustomizerVersion = [version]'x.y.z'</c> assignment in the grid script.</summary>
    internal static Version ReadMinCustomizerVersion(string script)
    {
        var matches = Regex.Matches(script, @"^\s*\$MinCustomizerVersion\s*=\s*\[version\]\s*'(?<v>[0-9]+(\.[0-9]+){1,3})'",
            RegexOptions.Multiline);
        if (matches.Count != 1)
            throw new InvalidOperationException($"expected exactly one $MinCustomizerVersion = [version]'x.y.z' assignment, found {matches.Count}");
        return Version.Parse(matches[0].Groups["v"].Value);
    }

    /// <summary>The <c>version</c> of the manifest's SpaarkeGridCustomizer <c>control</c> element.</summary>
    internal static Version ReadManifestVersion(string manifestXml)
    {
        var control = XDocument.Parse(manifestXml).Descendants("control")
            .Single(c => (string?)c.Attribute("constructor") == "SpaarkeGridCustomizer");
        return Version.Parse((string)control.Attribute("version")!);
    }

    internal static IReadOnlyList<string> ReadRootColumns(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("rootColumns").EnumerateArray()
            .Select(e => e.GetString()!.Trim().ToLowerInvariant())
            .ToList();
    }
}
