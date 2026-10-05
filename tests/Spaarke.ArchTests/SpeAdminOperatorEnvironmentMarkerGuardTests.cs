using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// unified-access-control-r2 task 165, owner round 49 item 1 — the BFF deployment setting
/// <c>SpeAdmin:PlatformOperatorEnvironment</c> opens the SPE admin routes that span the whole SharePoint Embedded tenant
/// or a whole container type to a root-unit admin. It must reach ONLY Spaarke-operated environments: customer
/// environments never carry it. These guards keep every path that configures an App Service honest:
/// <list type="bullet">
///   <item>the marker is named only by the BFF itself and the Spaarke-environment deployment surface — never by customer
///   provisioning (the L2 control plane, the canonical app-settings catalog, the customer/Model 1 Bicep);</item>
///   <item><c>config/environments.json</c> declares it true only for the Spaarke-operated environments listed here;</item>
///   <item>the Bicep stack defaults it to false and only the Spaarke-operated parameter files set it true.</item>
/// </list>
/// </summary>
public sealed class SpeAdminOperatorEnvironmentMarkerGuardTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static readonly Regex Marker = new(@"PlatformOperatorEnvironment", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The environments Spaarke itself operates. Adding one is a deliberate act (owner round 49 item 1: dev, and Spaarke's
    /// own operator environment once it is declared in config/environments.json).
    /// </summary>
    private static readonly HashSet<string> SpaarkeOperatedEnvironments = new(StringComparer.Ordinal) { "dev" };

    /// <summary>Every file outside tests/docs/projects that may name the marker — the BFF and the Spaarke deployment surface.</summary>
    private static readonly HashSet<string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "src/server/api/Sprk.Bff.Api/Configuration/SpeAdminOptions.cs",
        "src/server/api/Sprk.Bff.Api/Api/Filters/SpeAdminTenantScopeFilter.cs",
        "config/environments.json",
        "scripts/Deploy-BffApi.ps1",
        "infrastructure/bicep/stacks/model2-full.bicep",
        "infrastructure/bicep/stacks/model2-full.json",
        "infrastructure/bicep/stacks/dev.bicepparam",
    };

    [Fact(DisplayName = "The operator-environment marker is named only by the BFF and the Spaarke-environment deployment surface")]
    public void TheMarker_IsNamedOnlyWhereSpaarkeEnvironmentsAreConfigured()
    {
        // Everything that can put an App Service setting on a BFF: server code (incl. the L2 control plane and its app-settings
        // handler), app settings files, scripts, Bicep, the environment registry, pipelines.
        var scan = new (string Root, string[] Patterns)[]
        {
            ("src", new[] { "*.cs", "*.ps1", "*.psm1", "*.bicep", "*.bicepparam", "appsettings*.json" }),
            ("scripts", new[] { "*.ps1", "*.psm1", "*.json", "*.yml", "*.yaml", "*.sh", "*.py" }),
            ("infrastructure", new[] { "*.bicep", "*.bicepparam", "*.json", "*.ps1" }),
            ("config", new[] { "*.json" }),
            (".github", new[] { "*.yml", "*.yaml" }),
        };
        var namers = scan
            .Where(s => Directory.Exists(Path.Combine(RepoRoot, s.Root)))
            .SelectMany(s => s.Patterns.SelectMany(e => Directory.EnumerateFiles(Path.Combine(RepoRoot, s.Root), e, SearchOption.AllDirectories)))
            .Where(f => !IsBuildOutput(f))
            .Select(f => (Rel: Rel(f), Text: File.ReadAllText(f)))
            .Where(f => Marker.IsMatch(f.Text))
            .Select(f => f.Rel)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(namers.Count >= AllowedFiles.Count - 1, $"the scan must find the marker's own files (found {namers.Count})");
        var unexpected = namers.Where(f => !AllowedFiles.Contains(f) && !IsTestProject(f)).ToList();
        Assert.True(unexpected.Count == 0,
            "These files name SpeAdmin:PlatformOperatorEnvironment, which must reach ONLY Spaarke-operated environments — customer " +
            "provisioning (the L2 control plane, the app-settings catalog, the customer/Model 1 Bicep) must never emit it:\n  " +
            string.Join("\n  ", unexpected));
    }

    [Fact(DisplayName = "config/environments.json declares the marker true only for the Spaarke-operated environments")]
    public void TheEnvironmentRegistry_DeclaresTheMarkerOnlyForSpaarkeOperatedEnvironments()
    {
        using var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot, "config", "environments.json")));
        var declaredTrue = registry.RootElement.GetProperty("environments").EnumerateObject()
            .Where(e => e.Value.ValueKind == JsonValueKind.Object
                        && e.Value.TryGetProperty("speAdminPlatformOperatorEnvironment", out var v)
                        && v.ValueKind == JsonValueKind.True)
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(SpaarkeOperatedEnvironments, declaredTrue);

        var template = registry.RootElement.GetProperty("environments").GetProperty("_template");
        Assert.True(template.TryGetProperty("speAdminPlatformOperatorEnvironment", out var t) && t.ValueKind == JsonValueKind.False,
            "the template a new CUSTOMER environment is copied from must say false");
    }

    [Fact(DisplayName = "The Bicep stack defaults the marker to false and emits it only when a Spaarke parameter file sets it")]
    public void TheBicepStack_DefaultsTheMarkerToFalse_AndOnlySpaarkeParameterFilesSetIt()
    {
        var stack = File.ReadAllText(Path.Combine(RepoRoot, "infrastructure", "bicep", "stacks", "model2-full.bicep"));
        Assert.Matches(new Regex(@"^param speAdminPlatformOperatorEnvironment bool = false\s*$", RegexOptions.Multiline), stack);
        Assert.Matches(new Regex(@"speAdminPlatformOperatorEnvironment \? \{\s*SpeAdmin__PlatformOperatorEnvironment: 'true'\s*\} : \{\}"), stack);

        var setters = Directory.EnumerateFiles(Path.Combine(RepoRoot, "infrastructure"), "*.bicepparam", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"^param speAdminPlatformOperatorEnvironment = true\b", RegexOptions.Multiline))
            .Select(Rel)
            .ToList();
        Assert.Equal(new[] { "infrastructure/bicep/stacks/dev.bicepparam" }, setters);
    }

    private static bool IsTestProject(string rel) =>
        rel.Split('/').Any(s => s.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase));

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Rel(string path) => Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
