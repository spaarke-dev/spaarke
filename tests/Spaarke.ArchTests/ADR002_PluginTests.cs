using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-002: Spaarke ships <b>NO Dataverse plugins</b> — no C# plugin assemblies, no plugin-backed Custom
/// APIs, no custom workflow activities (reaffirmed by the owner 2026-09-25; see
/// <c>docs/adr/ADR-002-no-heavy-plugins.md</c>). Server-side Dataverse write logic lives in the BFF.
///
/// <para><b>What this class enforces.</b></para>
/// <list type="bullet">
///   <item><description>Two assembly-level NetArchTest rules over the BFF
///     (<see cref="BffShouldNotContainPluginOrchestration"/>,
///     <see cref="EndpointsShouldNotReferencePluginInterfaces"/>). The latter is named in the Tier-1 CI
///     filter in <c>.github/workflows/ci-tier1-blocking.yml</c> — do NOT rename it.</description></item>
///   <item><description>A <b>repo-wide zero-plugin source guard</b> over <c>src/**</c> (and
///     <c>scripts/**</c> for R3), rules R1–R5. It replaced (2026-09-25) a folder-scoped scan of the
///     now-deleted <c>src/dataverse/plugins/Spaarke.CustomApiProxy</c> that carried a KnownViolations
///     allowlist and a violation-count cap. A folder-scoped scan only watches the folder a plugin used to
///     live in; a new plugin can be added anywhere. This guard watches the whole tree and has NO
///     allowlist.</description></item>
/// </list>
///
/// <para><b>What it deliberately does NOT ban.</b> The <c>Microsoft.Xrm.Sdk</c> namespace. Spaarke.Dataverse
/// and the BFF use it legitimately through <c>ServiceClient</c> (Entity, EntityReference, QueryExpression,
/// IOrganizationServiceAsync2). The rules target the plugin-only surface: <c>IPlugin</c> implementations,
/// <c>IPluginExecutionContext</c>, <c>CodeActivity</c>, <c>IOrganizationServiceFactory</c>, the CrmSdk
/// plugin packages, .NET Framework targets, strong-name keys, IL merging, and plugin registrations in
/// solution XML. The empty <c>&lt;SolutionPluginAssemblies /&gt;</c> element that <c>pac solution init</c>
/// emits into every PCF <c>Solution/customizations.xml</c> is allowed.</para>
///
/// <para><b>Controls</b> (per <c>tests/CLAUDE.md</c> "Structural fitness functions"): a negative control
/// feeds seeded violations to every detector and asserts they fire; a positive control asserts sanctioned
/// shapes do NOT fire; a scanner-sees-tree control asserts the walk actually found the source tree, so the
/// guard cannot pass on an empty scan.</para>
///
/// <para><b>MAINTENANCE — if one of these rules fails.</b> Do not add an allowlist, loosen a regex, or
/// exclude a directory to make it pass. A failure means someone is adding a Dataverse plugin (or plugin
/// tooling), which ADR-002 prohibits outright. The only legitimate routes are the ADR's own reopen
/// criteria (see "Reopen criteria" in <c>docs/adr/ADR-002-no-heavy-plugins.md</c>) and the ADR Conflict
/// Resolution Protocol in root <c>CLAUDE.md</c> §6.5 — i.e. an owner-approved ADR amendment (path B) that
/// lands BEFORE or WITH the plugin code, at which point this class is changed in the same PR. A detector
/// false positive on a genuinely non-plugin shape is fixed by tightening that detector AND adding the
/// shape to <see cref="PositiveControl_SanctionedShapesDoNotFire"/>; never by exempting a file.</para>
/// </summary>
public class ADR002_PluginTests
{
    private const string ReopenGuidance =
        " Spaarke ships NO Dataverse plugins (ADR-002, reaffirmed 2026-09-25). Put server-side Dataverse " +
        "write logic in the BFF instead. If you believe a plugin is genuinely required, do NOT weaken this " +
        "guard: follow the reopen criteria in docs/adr/ADR-002-no-heavy-plugins.md and the ADR Conflict " +
        "Resolution Protocol in root CLAUDE.md §6.5 (owner-approved amendment first).";

    /// <summary>Directory names pruned from the walk (build output, dependencies, archives).</summary>
    private static readonly HashSet<string> ExcludedDirectoryNames =
        new(StringComparer.OrdinalIgnoreCase) { "node_modules", "bin", "obj", "_archive", "dist", ".claude", ".git" };

    // ── Detectors ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>R1 — plugin-only C# surface. Applied to comment-stripped code.</summary>
    private static readonly (Regex Pattern, string Description)[] R1PluginCode =
    {
        // `[:,]` rather than `:` alone: a plugin commonly declares `class X : PluginBase, IPlugin`,
        // which a colon-only pattern misses (the negative control caught exactly that).
        (new Regex(@"[:,]\s*IPlugin\b", RegexOptions.Compiled), "implements IPlugin"),
        (new Regex(@"\bIPluginExecutionContext\b", RegexOptions.Compiled), "uses IPluginExecutionContext"),
        (new Regex(@"\bCodeActivity\b", RegexOptions.Compiled), "uses CodeActivity (custom workflow activity)"),
        (new Regex(@"\bIOrganizationServiceFactory\b", RegexOptions.Compiled), "uses IOrganizationServiceFactory (plugin sandbox service factory)"),
    };

    /// <summary>R2 — plugin packaging in project files.</summary>
    private static readonly (Regex Pattern, string Description)[] R2PluginProject =
    {
        // PackageVersion too: a central pin in Directory.Packages.props is the first half of re-adding the
        // package (the pins lingered there after the last plugin project was deleted, 2026-09-25).
        (new Regex(@"<Package(?:Reference|Version)\b[^>]*\b(?:Include|Update)\s*=\s*""Microsoft\.CrmSdk\.(?:CoreAssemblies|Workflow)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase), "references or pins a Microsoft.CrmSdk plugin package"),
        (new Regex(@"<TargetFrameworks?>[^<]*\bnet4\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "targets .NET Framework (net4x) — the Dataverse plugin sandbox runtime"),
    };

    /// <summary>R3 — IL merging (how plugin assemblies bundle dependencies).</summary>
    private static readonly Regex R3IlMerge = new(@"\bIL(?:Repack|Merge)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// R4 — plugin registrations in solution XML. <c>&lt;PluginAssembly</c> requires the <c>&lt;</c> to
    /// immediately precede <c>PluginAssembly</c>, so the sanctioned empty
    /// <c>&lt;SolutionPluginAssemblies /&gt;</c> never matches; a NON-empty
    /// <c>&lt;SolutionPluginAssemblies&gt;…&lt;/SolutionPluginAssemblies&gt;</c> does.
    /// </summary>
    private static readonly (Regex Pattern, string Description)[] R4PluginSolutionXml =
    {
        (new Regex(@"<PluginAssembly\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "registers a <PluginAssembly>"),
        (new Regex(@"<SolutionPluginAssemblies\b[^>]*(?<!/)>(?!\s*</SolutionPluginAssemblies>)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "has a non-empty <SolutionPluginAssemblies>"),
        (new Regex(@"<SdkMessageProcessingStep\b", RegexOptions.Compiled | RegexOptions.IgnoreCase), "registers an <SdkMessageProcessingStep>"),
        (new Regex(@"<plugintypeid\b[^>]*(?<!/)>(?!\s*</plugintypeid>)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "binds a custom API to a plugin type (<plugintypeid>)"),
        (new Regex(@"\bplugintypeid\s*=\s*""[^""]+""", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            "binds a custom API to a plugin type (plugintypeid attribute)"),
    };

    // ── Assembly rules (kept verbatim; Tier-1 CI depends on the second name) ─────────────────────────

    [Fact(DisplayName = "ADR-002: BFF should not contain plugin orchestration logic")]
    public void BffShouldNotContainPluginOrchestration()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Ensure no classes named with "Plugin" pattern in BFF
        var pluginTypes = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("Plugin")
            .GetTypes();

        // Assert
        Assert.Empty(pluginTypes);
    }

    [Fact(DisplayName = "ADR-002: BFF endpoints should not reference plugin interfaces")]
    public void EndpointsShouldNotReferencePluginInterfaces()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Ensure endpoints don't depend on plugin-specific namespaces
        var result = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("Endpoints")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Xrm.Sdk.IPlugin")
            .GetResult();

        // Assert
        Assert.True(
            result.IsSuccessful,
            $"ADR-002 violation: Endpoint classes should not reference plugin interfaces. " +
            $"Orchestration logic belongs in BFF endpoints/workers, not plugins. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    // ── Repo-wide zero-plugin guard ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ADR-002 R1: no C# source under src/ implements the Dataverse plugin surface")]
    public void R1_NoPluginCodeInSource()
    {
        var violations = new List<string>();
        foreach (var file in Walk("src", "*.cs"))
        {
            violations.AddRange(DetectR1(File.ReadAllText(file)).Select(d => $"{SourceScan.Relative(file)}: {d}"));
        }

        Assert.True(violations.Count == 0,
            "ADR-002 R1 violation — Dataverse plugin code found:\n  " + string.Join("\n  ", violations) + "\n" + ReopenGuidance);
    }

    [Fact(DisplayName = "ADR-002 R2: no project under src/ (or repo-root Directory.*.props) references/pins CrmSdk plugin packages or targets net4x; no .snk keys")]
    public void R2_NoPluginProjectsOrStrongNameKeys()
    {
        var rootProps = new[] { "Directory.Packages.props", "Directory.Build.props", "Directory.Build.targets" }
            .Select(n => Path.Combine(SourceScan.RepoRoot, n))
            .Where(File.Exists);

        var violations = new List<string>();
        foreach (var file in rootProps.Concat(Walk("src", "*.csproj")).Concat(Walk("src", "*.props")).Concat(Walk("src", "*.targets")))
        {
            violations.AddRange(DetectR2(File.ReadAllText(file)).Select(d => $"{SourceScan.Relative(file)}: {d}"));
        }

        violations.AddRange(Walk("src", "*.snk").Select(f =>
            $"{SourceScan.Relative(f)}: strong-name key file (Dataverse plugin assemblies must be strong-named; nothing else in Spaarke is)"));

        Assert.True(violations.Count == 0,
            "ADR-002 R2 violation — plugin project packaging found:\n  " + string.Join("\n  ", violations) + "\n" + ReopenGuidance);
    }

    [Fact(DisplayName = "ADR-002 R3: no ILRepack/ILMerge in build files or scripts under src/ and scripts/")]
    public void R3_NoIlMerging()
    {
        var patterns = new[] { "*.csproj", "*.props", "*.targets", "*.ps1" };
        var files = new[] { "src", "scripts" }.SelectMany(root => patterns.SelectMany(p => Walk(root, p)));

        var violations = files
            .Where(f => DetectR3(File.ReadAllText(f)))
            .Select(f => $"{SourceScan.Relative(f)}: uses ILRepack/ILMerge (plugin dependency bundling)")
            .ToList();

        Assert.True(violations.Count == 0,
            "ADR-002 R3 violation — IL merging found:\n  " + string.Join("\n  ", violations) + "\n" + ReopenGuidance);
    }

    [Fact(DisplayName = "ADR-002 R4: no solution XML under src/ registers a plugin assembly, step, or plugin-backed custom API")]
    public void R4_NoPluginRegistrationsInSolutionXml()
    {
        var violations = new List<string>();
        foreach (var file in Walk("src", "*.xml"))
        {
            violations.AddRange(DetectR4(File.ReadAllText(file)).Select(d => $"{SourceScan.Relative(file)}: {d}"));
        }

        Assert.True(violations.Count == 0,
            "ADR-002 R4 violation — plugin registration in solution XML:\n  " + string.Join("\n  ", violations) + "\n" + ReopenGuidance);
    }

    [Fact(DisplayName = "ADR-002 R5: src/dataverse/plugins/ contains no files")]
    public void R5_NoDataversePluginsDirectory()
    {
        var pluginsDir = Path.Combine(SourceScan.RepoRoot, "src", "dataverse", "plugins");
        var files = Directory.Exists(pluginsDir)
            ? Directory.EnumerateFiles(pluginsDir, "*", SearchOption.AllDirectories).Select(SourceScan.Relative).ToList()
            : new List<string>();

        Assert.True(files.Count == 0,
            "ADR-002 R5 violation — src/dataverse/plugins/ contains files:\n  " + string.Join("\n  ", files.Take(20)) + "\n" + ReopenGuidance);
    }

    // ── Controls ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ADR-002 negative control: every detector fires on a seeded violation")]
    public void NegativeControl_DetectorsFireOnSeededViolations()
    {
        // R1 — each of the four plugin-surface detectors.
        Assert.NotEmpty(DetectR1("public sealed class X : IPlugin { }"));
        Assert.NotEmpty(DetectR1("public class X : BaseThing, IPlugin\n{\n}"));
        Assert.NotEmpty(DetectR1("var ctx = (IPluginExecutionContext)sp.GetService(typeof(IPluginExecutionContext));"));
        Assert.NotEmpty(DetectR1("public class Y : CodeActivity { }"));
        Assert.NotEmpty(DetectR1("var f = (IOrganizationServiceFactory)sp.GetService(typeof(IOrganizationServiceFactory));"));

        // R2 — CrmSdk packages (both), net4x single + multi-target.
        Assert.NotEmpty(DetectR2(@"<PackageReference Include=""Microsoft.CrmSdk.CoreAssemblies"" Version=""9.0.2.56"" />"));
        Assert.NotEmpty(DetectR2(@"<PackageReference Include=""Microsoft.CrmSdk.Workflow"" Version=""9.0.2.56"" />"));
        Assert.NotEmpty(DetectR2(@"<PackageVersion Include=""Microsoft.CrmSdk.CoreAssemblies"" Version=""9.0.2.56"" />"));
        Assert.NotEmpty(DetectR2(@"<PackageVersion Include=""Microsoft.CrmSdk.Workflow"" Version=""9.0.2.56"" />"));
        Assert.NotEmpty(DetectR2("<TargetFramework>net462</TargetFramework>"));
        Assert.NotEmpty(DetectR2("<TargetFrameworks>net10.0;net48</TargetFrameworks>"));

        // R3 — both IL mergers.
        Assert.True(DetectR3(@"<PackageReference Include=""ILRepack.Lib.MSBuild.Task"" Version=""2.0.34"" />"));
        Assert.True(DetectR3("& $ilmerge /out:Merged.dll Plugin.dll  # ILMerge"));

        // R4 — each registration shape.
        Assert.NotEmpty(DetectR4(@"<PluginAssembly Name=""x"" />"));
        Assert.NotEmpty(DetectR4(@"<SolutionPluginAssemblies><PluginAssembly FullName=""x"" /></SolutionPluginAssemblies>"));
        Assert.NotEmpty(DetectR4(@"<SdkMessageProcessingStep Name=""x"" />"));
        Assert.NotEmpty(DetectR4("<customapi uniquename=\"sprk_X\"><plugintypeid><plugintypeexportkey>Spaarke.X</plugintypeexportkey></plugintypeid></customapi>"));
        Assert.NotEmpty(DetectR4(@"<customapi uniquename=""sprk_X"" plugintypeid=""0b5e8f1a-0000-0000-0000-000000000001"" />"));
    }

    [Fact(DisplayName = "ADR-002 positive control: sanctioned shapes do not fire")]
    public void PositiveControl_SanctionedShapesDoNotFire()
    {
        // The empty element pac emits into every PCF Solution/customizations.xml.
        Assert.Empty(DetectR4("<ImportExportXml><SolutionPluginAssemblies /></ImportExportXml>"));
        Assert.Empty(DetectR4("<SolutionPluginAssemblies></SolutionPluginAssemblies>"));
        Assert.Empty(DetectR4("<SolutionPluginAssemblies>\r\n  </SolutionPluginAssemblies>"));
        Assert.Empty(DetectR4("<customapi uniquename=\"sprk_X\"><plugintypeid /></customapi>"));

        // Legitimate ServiceClient use of Microsoft.Xrm.Sdk in a net10 project.
        Assert.Empty(DetectR1(
            "using Microsoft.Xrm.Sdk;\nusing Microsoft.Xrm.Sdk.Query;\n" +
            "public sealed class DataverseService { private readonly IOrganizationServiceAsync2 _svc; " +
            "public Entity Get(EntityReference r) => _svc.Retrieve(r.LogicalName, r.Id, new ColumnSet(true)); }"));
        Assert.Empty(DetectR2(
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>" +
            "<ItemGroup><PackageReference Include=\"Microsoft.PowerPlatform.Dataverse.Client\" /></ItemGroup></Project>"));
        Assert.Empty(DetectR2("<PackageVersion Include=\"Microsoft.PowerPlatform.Dataverse.Client\" Version=\"1.1.32\" />"));
        Assert.Empty(DetectR2("<TargetFramework>netstandard2.0</TargetFramework>"));

        // Prose and look-alike identifiers.
        Assert.Empty(DetectR1("// no Dataverse plugin — this logic lives in the BFF (class X : IPlugin was removed)"));
        Assert.Empty(DetectR1("public interface IPluginRegistry { } public class R : Base, IPluginRegistry { }"));
        Assert.Empty(DetectR1("public class BarcodeActivityLog { }"));
        Assert.False(DetectR3("<!-- no Dataverse plugin; nothing is merged -->"));
    }

    [Fact(DisplayName = "ADR-002 control: the scanner sees the source tree (cannot pass on an empty scan)")]
    public void Control_ScannerSeesTheSourceTree()
    {
        var csproj = Walk("src", "*.csproj").Count();
        var cs = Walk("src", "*.cs").Count();
        var xml = Walk("src", "*.xml").ToList();

        // Conservative floors (2026-09-25 actuals: 8 .csproj, ~1,960 .cs). Lower them only if the tree
        // genuinely shrinks — never to make an empty or mis-rooted scan pass.
        Assert.True(csproj >= 5, $"Scanner found only {csproj} .csproj under src/ (expected >= 5). Repo root resolved to '{SourceScan.RepoRoot}'.");
        Assert.True(cs >= 500, $"Scanner found only {cs} .cs files under src/ (expected >= 500). Repo root resolved to '{SourceScan.RepoRoot}'.");

        // The R4 positive case must be exercised against the REAL tree, not only a seeded string: the PCF
        // solutions carry the empty <SolutionPluginAssemblies /> element (13 of them on 2026-09-25).
        var withEmptyElement = xml.Count(f => File.ReadAllText(f).Contains("<SolutionPluginAssemblies", StringComparison.OrdinalIgnoreCase));
        Assert.True(withEmptyElement >= 5,
            $"Scanner found only {withEmptyElement} solution XML files carrying <SolutionPluginAssemblies (expected >= 5) — R4's allowance of the empty element is not being exercised against the real tree.");
    }

    // ── Detector implementations (shared by the rules and the controls) ──────────────────────────────

    private static IEnumerable<string> DetectR1(string source)
    {
        var code = SourceScan.CodeText(source.Split('\n'));
        return R1PluginCode.Where(r => r.Pattern.IsMatch(code)).Select(r => r.Description);
    }

    private static IEnumerable<string> DetectR2(string projectXml)
        => R2PluginProject.Where(r => r.Pattern.IsMatch(projectXml)).Select(r => r.Description);

    private static bool DetectR3(string text) => R3IlMerge.IsMatch(text);

    private static IEnumerable<string> DetectR4(string xml)
        => R4PluginSolutionXml.Where(r => r.Pattern.IsMatch(xml)).Select(r => r.Description);

    /// <summary>
    /// Files matching <paramref name="pattern"/> under <c>{repo}/{root}</c>, pruning
    /// <see cref="ExcludedDirectoryNames"/> BEFORE descending (so node_modules is never walked).
    /// </summary>
    private static IEnumerable<string> Walk(string root, string pattern)
    {
        var start = Path.Combine(SourceScan.RepoRoot, root);
        if (!Directory.Exists(start))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(dir, pattern))
            {
                yield return file;
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (!ExcludedDirectoryNames.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }
        }
    }
}
