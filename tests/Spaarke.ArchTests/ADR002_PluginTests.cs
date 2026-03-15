using NetArchTest.Rules;
using Xunit;
using Xunit.Abstractions;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-002: Keep Dataverse plugins thin; no orchestration in plugins.
/// Validates that:
///   1. BFF assembly does not contain plugin orchestration logic.
///   2. Plugin source code does not contain prohibited patterns (HTTP calls, Thread.Sleep,
///      HttpClient instantiation) that violate ADR-002 constraints.
///
/// Note: The plugin assembly (Spaarke.Dataverse.CustomApiProxy) targets net462 and cannot
/// be loaded as a ProjectReference in this net8.0 test project. Plugin validation uses
/// source-file scanning to detect prohibited patterns reliably across framework boundaries.
/// </summary>
public class ADR002_PluginTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>
    /// Root path to the plugin source directory, resolved at runtime.
    /// </summary>
    private static readonly string PluginSourcePath = ResolvePluginSourcePath();

    /// <summary>
    /// Known ADR-002 violations in legacy/experimental plugin code.
    /// These are tracked here so the test passes for existing code but fails
    /// when NEW violations are introduced. Each entry is {FileName}:{Pattern}.
    ///
    /// Context: BaseProxyPlugin.cs and SimpleAuthHelper.cs are legacy proxy plugins
    /// that make HTTP calls to the BFF API. These are documented as experimental
    /// and will be assessed for deprecation (see project task for BaseProxyPlugin assessment).
    /// </summary>
    private static readonly HashSet<string> KnownViolations = new()
    {
        // BaseProxyPlugin.cs — legacy proxy plugin that makes HTTP calls to BFF API
        "BaseProxyPlugin.cs:new HttpClient",
        "BaseProxyPlugin.cs:Thread.Sleep",
        "BaseProxyPlugin.cs:HttpWebRequest",

        // GetFilePreviewUrlPlugin.cs — uses HttpClient via BaseProxyPlugin
        "GetFilePreviewUrlPlugin.cs:.GetAsync(",

        // SimpleAuthHelper.cs — OAuth2 token helper using HttpWebRequest
        "SimpleAuthHelper.cs:HttpWebRequest",
        "SimpleAuthHelper.cs:WebRequest.Create",
    };

    public ADR002_PluginTests(ITestOutputHelper output)
    {
        _output = output;
    }

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

    [Fact(DisplayName = "ADR-002: Plugin source must not have NEW HttpClient instantiation")]
    public void PluginSourceMustNotHaveNewHttpClientInstantiation()
    {
        // Arrange — scan plugin .cs source files for HttpClient instantiation
        var sourceFiles = GetPluginSourceFiles();
        Assert.NotEmpty(sourceFiles);

        var newViolations = new List<string>();

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            if (content.Contains("new HttpClient"))
            {
                var key = $"{fileName}:new HttpClient";
                if (KnownViolations.Contains(key))
                {
                    _output.WriteLine($"[KNOWN] {key} — legacy/experimental, tracked for deprecation");
                }
                else
                {
                    newViolations.Add($"{fileName}: instantiates HttpClient directly (new HttpClient). " +
                        "ADR-002 prohibits HTTP calls from plugins.");
                }
            }
        }

        // Assert — fails only on NEW violations not in the known exceptions list
        Assert.Empty(newViolations);
    }

    [Fact(DisplayName = "ADR-002: Plugin source must not have NEW Thread.Sleep usage")]
    public void PluginSourceMustNotHaveNewThreadSleepUsage()
    {
        // Arrange — scan plugin .cs source files for Thread.Sleep usage
        var sourceFiles = GetPluginSourceFiles();
        Assert.NotEmpty(sourceFiles);

        var newViolations = new List<string>();

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            if (content.Contains("Thread.Sleep"))
            {
                var key = $"{fileName}:Thread.Sleep";
                if (KnownViolations.Contains(key))
                {
                    _output.WriteLine($"[KNOWN] {key} — legacy/experimental, tracked for deprecation");
                }
                else
                {
                    newViolations.Add($"{fileName}: uses Thread.Sleep. " +
                        "ADR-002 prohibits blocking calls in plugins (must complete in <50ms p95).");
                }
            }
        }

        // Assert — fails only on NEW violations
        Assert.Empty(newViolations);
    }

    [Fact(DisplayName = "ADR-002: Plugin source must not have NEW HTTP call patterns")]
    public void PluginSourceMustNotHaveNewHttpCallPatterns()
    {
        // Arrange — scan plugin .cs source files for HTTP call patterns
        var sourceFiles = GetPluginSourceFiles();
        Assert.NotEmpty(sourceFiles);

        // Prohibited patterns per ADR-002: no remote I/O calls from plugins
        var prohibitedPatterns = new (string Pattern, string Description)[]
        {
            ("HttpWebRequest", "uses HttpWebRequest for HTTP calls"),
            ("WebRequest.Create", "uses WebRequest.Create for HTTP calls"),
            ("new HttpClient", "instantiates HttpClient directly"),
            (".GetAsync(", "makes HTTP GET calls"),
            (".PostAsync(", "makes HTTP POST calls"),
            (".SendAsync(", "makes HTTP calls via SendAsync"),
            (".GetStringAsync(", "makes HTTP calls via GetStringAsync"),
        };

        var newViolations = new List<string>();

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            foreach (var (pattern, description) in prohibitedPatterns)
            {
                if (content.Contains(pattern))
                {
                    var key = $"{fileName}:{pattern}";
                    if (KnownViolations.Contains(key))
                    {
                        _output.WriteLine($"[KNOWN] {key} — {description} (legacy/experimental)");
                    }
                    else
                    {
                        newViolations.Add($"{fileName}: {description}. " +
                            "ADR-002 prohibits HTTP/Graph/remote I/O calls from plugins.");
                    }
                }
            }
        }

        // Assert — fails only on NEW violations not in the known exceptions list.
        // Known violations in BaseProxyPlugin.cs and SimpleAuthHelper.cs are legacy
        // code tracked for deprecation/refactoring.
        Assert.Empty(newViolations);
    }

    [Fact(DisplayName = "ADR-002: Plugin source files should exist for scanning")]
    public void PluginSourceFilesShouldExist()
    {
        // Arrange & Act
        var sourceFiles = GetPluginSourceFiles();

        // Assert — ensure we're actually scanning plugin source
        Assert.NotEmpty(sourceFiles);
    }

    [Fact(DisplayName = "ADR-002: Known plugin violations count should not increase")]
    public void KnownPluginViolationCountShouldNotIncrease()
    {
        // Arrange — count all violations (known + new) across all plugin source files
        var sourceFiles = GetPluginSourceFiles();
        Assert.NotEmpty(sourceFiles);

        var prohibitedPatterns = new[]
        {
            "HttpWebRequest", "WebRequest.Create", "new HttpClient",
            ".GetAsync(", ".PostAsync(", ".SendAsync(", ".GetStringAsync(",
            "Thread.Sleep"
        };

        var totalViolationCount = 0;

        foreach (var file in sourceFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var pattern in prohibitedPatterns)
            {
                if (content.Contains(pattern))
                    totalViolationCount++;
            }
        }

        // The current known violation count. If this number increases, it means
        // new ADR-002 violations were introduced. Update KnownViolations set
        // only after review and approval.
        const int knownViolationCount = 6; // See KnownViolations set above

        _output.WriteLine($"Total ADR-002 violations detected: {totalViolationCount}");
        _output.WriteLine($"Known/accepted violations: {knownViolationCount}");

        Assert.True(
            totalViolationCount <= knownViolationCount,
            $"ADR-002 violation count increased from {knownViolationCount} to {totalViolationCount}. " +
            $"New plugin code must not introduce HTTP calls, Thread.Sleep, or HttpClient instantiation. " +
            $"If these are justified legacy exceptions, add them to KnownViolations with documentation.");
    }

    /// <summary>
    /// Get all .cs source files from the plugin project directory.
    /// </summary>
    private static string[] GetPluginSourceFiles()
    {
        if (!Directory.Exists(PluginSourcePath))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(PluginSourcePath, "*.cs", SearchOption.AllDirectories);
    }

    /// <summary>
    /// Resolve the plugin source path by walking up from the test output directory
    /// to find the repository root, then navigating to the plugin source.
    /// </summary>
    private static string ResolvePluginSourcePath()
    {
        var currentDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(currentDir);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return Path.Combine(dir.FullName,
                    "src", "dataverse", "plugins", "Spaarke.CustomApiProxy",
                    "Plugins", "Spaarke.Dataverse.CustomApiProxy");
            }
            dir = dir.Parent;
        }

        return Path.Combine(currentDir, "nonexistent-plugin-source");
    }
}
