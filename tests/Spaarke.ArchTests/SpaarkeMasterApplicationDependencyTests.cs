using System.Xml.Linq;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// SPAARKEMASTER DEPENDS ON NO APPLICATION A FRESH ENVIRONMENT LACKS (customer-provisioning-orchestration-r1 task 253,
/// gap G38 / finding F13).
///
/// <para>H6 imports SpaarkeMaster into a brand-new customer environment and installs nothing first. On 2026-08-22 a
/// hand-made SpaarkeMaster carried a spurious dependency on Power BI Extensions (<c>powerbimashupparameter</c> in
/// <c>msft_PowerBI_Entities</c>, application <c>msft_PowerBI_Anchor</c>) — an AppSource application a fresh Production
/// environment does not have — and the import failed with MissingDependency (F13). H6 then grew a
/// <c>pac application install</c> step, which cannot run on the L2 Worker host (no pac). The CI-built package
/// (<c>src/dataverse/solutions/SpaarkeMaster</c>, T218) no longer carries that dependency, so task 253 deleted the
/// install step instead of porting it. This test keeps it that way.</para>
///
/// <para>It reads the package's <c>Solution.xml</c> <c>MissingDependencies</c> — every component SpaarkeMaster needs from
/// outside itself — and checks each <c>Required/package/@appName</c> (the application that supplies it) against
/// <see cref="PlatformApplications"/>, the applications the 1.2.0.0 package (T218f, the first CI publish) needs.</para>
///
/// <para><b>When it fails</b> (a re-export added an application): either remove the dependency at source (the usual
/// answer — F13's was spurious, picked up from the authoring environment), or, if a fresh Production environment is
/// known to carry that application, add it to <see cref="PlatformApplications"/> with that evidence in the commit. Never
/// add an AppSource application such as Power BI Extensions: H6 has no way to install it.</para>
/// </summary>
public class SpaarkeMasterApplicationDependencyTests
{
    private static readonly string SolutionXmlPath = Path.Combine(
        SourceScan.RepoRoot, "src", "dataverse", "solutions", "SpaarkeMaster", "Other", "Solution.xml");

    /// <summary>
    /// The applications SpaarkeMaster 1.2.0.0 depends on (platform-provided; T186, the first E2E run, is their live proof
    /// on a fresh environment).
    /// </summary>
    private static readonly IReadOnlySet<string> PlatformApplications = new HashSet<string>(StringComparer.Ordinal)
    {
        "AppModule",
        "AppModule Web Resources Package",
        "Base Custom Controls",
        "DataValidationApp Package",
        "EnvironmentVariables",
        "Microsoft S2SCDSCoreApplication Package",
        "Power Pages Core",
        "Power Pages Core Package",
        "PowerAI",
        "PowerAppsAppCopilotApp Package",
        "PowerAppsAppFramework Package",
        "PowerAppsChecker",
        "PowerAppsCommandingApp Package",
        "PowerAppsDataVizApp Package",
        "PowerAppsFormsApp Package",
        "PowerAppsUnifiedClientExtensionApp Package",
        "msdyn_CustomControlsExtended",
        "msdyn_FlowApprovals",
        "msdynce_Activities",
        "msdynce_AppCommon",
    };

    /// <summary>"application (component ← solution)" for each required component whose supplying application is not a
    /// known platform application, or that is supplied by no application at all.</summary>
    internal static List<string> UnknownApplicationDependencies(XDocument solutionXml)
    {
        var violations = new List<string>();
        foreach (var required in solutionXml.Descendants("MissingDependency").Elements("Required"))
        {
            var component = $"{required.Attribute("schemaName")?.Value} ← {required.Attribute("solution")?.Value}";
            var packages = required.Elements("package").ToList();
            if (packages.Count == 0)
            {
                violations.Add($"(no supplying application) ({component})");
                continue;
            }
            violations.AddRange(packages
                .Select(p => p.Attribute("appName")?.Value ?? "(unnamed)")
                .Where(app => !PlatformApplications.Contains(app))
                .Select(app => $"{app} ({component})"));
        }
        return violations.Distinct(StringComparer.Ordinal).ToList();
    }

    [Fact(DisplayName = "Task 253: SpaarkeMaster depends on no application outside the platform set (F13 — H6 installs none)")]
    public void SpaarkeMaster_DependsOnNoApplicationAFreshEnvironmentLacks()
    {
        var solutionXml = XDocument.Load(SolutionXmlPath);

        var violations = UnknownApplicationDependencies(solutionXml);

        Assert.True(
            violations.Count == 0,
            "SpaarkeMaster's Solution.xml requires components from applications H6 cannot install on a fresh " +
            "environment (F13). Remove the dependency at source, or — only with evidence that every fresh Production " +
            "environment carries the application — add it to PlatformApplications.\n\n  " + string.Join("\n  ", violations));

        // Non-vacuous: the package is the one-solution SpaarkeMaster and it does declare platform dependencies.
        Assert.Equal("SpaarkeMaster", solutionXml.Descendants("UniqueName").First().Value);
        Assert.True(solutionXml.Descendants("MissingDependency").Count() > 50,
            "the reader found too few MissingDependency entries — the Solution.xml shape changed");
    }

    [Fact(DisplayName = "Task 253 control: the F13 Power BI Extensions dependency fails the rule")]
    public void UnknownApplicationDependencies_NegativeControl_F13PowerBiDependencyFails()
    {
        // The dependency the 2026-08-22 import reported (lessons-learned F13), in Solution.xml form.
        var f13 = XDocument.Parse("""
            <ImportExportXml><SolutionManifest><MissingDependencies>
              <MissingDependency>
                <Required type="1" schemaName="powerbimashupparameter" displayName="Power BI Mashup Parameter" solution="msft_PowerBI_Entities (1.0.0.193)">
                  <package appName="Power BI" version="1.0.0.193">msft_PowerBI_Anchor (1.0.0.193)</package>
                </Required>
                <Dependent type="1" schemaName="environmentvariabledefinition" />
              </MissingDependency>
              <MissingDependency>
                <Required type="1" schemaName="appnotification" solution="AppNotifications (10.0.0.17)">
                  <package appName="PowerAppsUnifiedClientExtensionApp Package" version="1.0.0.103">PowerAppsUnifiedClientExtensionApp_Anchor (1.0.0.103)</package>
                </Required>
                <Dependent type="1" schemaName="appnotification" />
              </MissingDependency>
            </MissingDependencies></SolutionManifest></ImportExportXml>
            """);

        var violations = UnknownApplicationDependencies(f13);

        Assert.Equal(new[] { "Power BI (powerbimashupparameter ← msft_PowerBI_Entities (1.0.0.193))" }, violations);
    }
}
