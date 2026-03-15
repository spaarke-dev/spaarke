using System.Text.RegularExpressions;
using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-010: Dependency Injection minimalism and feature modules
/// Validates that concrete services are registered unless a seam is required.
/// Checks for proper lifetime management (Singleton for expensive resources).
/// </summary>
public class ADR010_DITests
{
    /// <summary>
    /// Root path to the source code, resolved from the test output directory.
    /// </summary>
    private static readonly string SourceRoot = ResolveSourceRoot();

    [Fact(DisplayName = "ADR-010: Expensive resources should be registered as Singleton")]
    public void ExpensiveResourcesShouldBeSingleton()
    {
        // Arrange — scan DI module source files to verify lifetime registrations.
        // ADR-010 requires ServiceClient, GraphServiceClient, and HttpClient factory
        // to be registered as Singleton (these are expensive to create: ~500ms init).
        var diModulePath = Path.Combine(SourceRoot,
            "src", "server", "api", "Sprk.Bff.Api", "Infrastructure", "DI");
        var programPath = Path.Combine(SourceRoot,
            "src", "server", "api", "Sprk.Bff.Api", "Program.cs");

        Assert.True(Directory.Exists(diModulePath),
            $"DI module directory not found: {diModulePath}");

        // Collect all DI registration source code
        var diSourceFiles = Directory.GetFiles(diModulePath, "*.cs", SearchOption.AllDirectories).ToList();
        if (File.Exists(programPath))
            diSourceFiles.Add(programPath);

        var allDiSource = string.Join("\n", diSourceFiles.Select(File.ReadAllText));

        var violations = new List<string>();

        // Expensive types that MUST be Singleton
        var expensiveTypes = new[]
        {
            "ServiceClient",       // Dataverse ServiceClient (~500ms init)
            "GraphServiceClient",  // Microsoft Graph client
        };

        foreach (var typeName in expensiveTypes)
        {
            // Check for Scoped or Transient registrations of expensive types
            // Pattern: AddScoped<...TypeName...> or AddTransient<...TypeName...>
            var scopedPattern = $@"Add(?:Scoped|Transient)\s*<[^>]*{Regex.Escape(typeName)}";
            if (Regex.IsMatch(allDiSource, scopedPattern))
            {
                violations.Add($"{typeName} is registered as Scoped or Transient. " +
                    "ADR-010 requires expensive resources to be Singleton.");
            }
        }

        // Verify IHttpClientFactory is used (not new HttpClient()) in service registrations
        // Check across all server source for direct HttpClient construction
        var serverSourcePath = Path.Combine(SourceRoot, "src", "server", "api", "Sprk.Bff.Api");
        var serverCsFiles = Directory.GetFiles(serverSourcePath, "*.cs", SearchOption.AllDirectories);

        // Exclude the plugin project from this check
        var bffFiles = serverCsFiles.Where(f => !f.Contains("CustomApiProxy")).ToList();

        // Check that IHttpClientFactory is registered somewhere
        var hasHttpClientFactory = allDiSource.Contains("AddHttpClient") ||
                                   allDiSource.Contains("IHttpClientFactory");
        if (!hasHttpClientFactory)
        {
            violations.Add("IHttpClientFactory is not registered in DI modules. " +
                "HttpClient instances should be managed via IHttpClientFactory per ADR-010.");
        }

        // Check BFF service files for direct HttpClient construction (new HttpClient())
        // that bypasses IHttpClientFactory
        foreach (var file in bffFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            // Skip test files and DI module files (which register the factory)
            if (fileName.Contains("Test") || fileName.Contains("Module"))
                continue;

            // Detect: new HttpClient( — direct instantiation bypassing factory
            if (Regex.IsMatch(content, @"new\s+HttpClient\s*\("))
            {
                violations.Add($"{fileName}: directly instantiates HttpClient. " +
                    "Use IHttpClientFactory for proper connection pooling per ADR-010.");
            }
        }

        // Assert — test fails when expensive resources are not properly registered
        Assert.Empty(violations);
    }

    [Fact(DisplayName = "ADR-010: Services should be concrete unless seam required")]
    public void ServicesShouldBeConcreteUnlessSeamRequired()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Find interface/implementation pairs in our application namespace
        var allTypes = Types.InAssembly(assembly).GetTypes();
        var interfaces = allTypes.Where(t => t.IsInterface).ToList();
        var concretes = allTypes.Where(t => !t.IsInterface && !t.IsAbstract).ToList();

        // Framework interfaces are always allowed (not subject to ADR-010)
        var frameworkPrefixes = new[] { "System", "Microsoft", "Azure", "StackExchange" };

        // Count 1:1 interface-to-implementation mappings in our application namespace
        var oneToOneInterfaces = new List<string>();

        foreach (var iface in interfaces)
        {
            // Skip framework interfaces
            if (frameworkPrefixes.Any(p => iface.Namespace?.StartsWith(p) == true))
                continue;

            // Only check our application namespace
            if (iface.Namespace?.StartsWith("Sprk") != true &&
                iface.Namespace?.StartsWith("Spaarke") != true)
                continue;

            // Find implementations
            var implementations = concretes.Where(c => iface.IsAssignableFrom(c)).ToList();

            // If exactly 1 implementation, it's a 1:1 mapping that ADR-010 discourages
            if (implementations.Count == 1)
            {
                oneToOneInterfaces.Add($"{iface.Name} -> {implementations[0].Name}");
            }
        }

        // Ceiling-based assertion: the current count of 1:1 interface mappings must not increase.
        // ADR-010 says "register concretes unless a genuine seam is required."
        // Many existing 1:1 interfaces exist for testing (mock injection). These are grandfathered.
        // NEW 1:1 interfaces must be reviewed — either justify the seam or use concrete registration.
        //
        // To update the ceiling: review the new interface, determine if the seam is justified,
        // then increase the ceiling with a comment explaining the addition.
        //
        // Current inventory (as of 2026-03-14):
        //   - Testing seams (mock injection): ~40 interfaces
        //   - Architecture seams (ADR-013 AI, ADR-007 facade): ~15 interfaces
        //   - Worker/Job handler seams (multiple implementations): ~5 interfaces
        //   - Infrastructure seams (resilience, auth): ~8 interfaces
        const int knownOneToOneCeiling = 76;

        Assert.True(
            oneToOneInterfaces.Count <= knownOneToOneCeiling,
            $"ADR-010: 1:1 interface mapping count increased from {knownOneToOneCeiling} to {oneToOneInterfaces.Count}. " +
            $"New interfaces added without seam justification. " +
            $"Either register concrete per ADR-010 or update the ceiling with documentation.\n\n" +
            $"Current 1:1 interfaces ({oneToOneInterfaces.Count}):\n" +
            string.Join("\n", oneToOneInterfaces.Select(i => $"  - {i}")));
    }

    [Fact(DisplayName = "ADR-010: Feature modules should use extension methods")]
    public void FeatureModulesShouldUseExtensionMethods()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Look for service registration extension methods
        var extensionMethods = Types.InAssembly(assembly)
            .That()
            .HaveNameMatching(".*ServiceCollectionExtensions")
            .Or()
            .HaveNameEndingWith("Extensions")
            .GetTypes()
            .SelectMany(t => t.GetMethods())
            .Where(m => m.IsStatic &&
                       m.GetParameters().FirstOrDefault()?.ParameterType.Name == "IServiceCollection")
            .ToList();

        // Assert - Should have extension methods for feature module registration
        // This encourages modular service registration per ADR-010
        Assert.NotEmpty(extensionMethods);
    }

    [Fact(DisplayName = "ADR-010: Options pattern should be used for configuration")]
    public void OptionsPatternShouldBeUsedForConfiguration()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Find types ending with "Options" or "Settings"
        var configTypes = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("Options")
            .Or()
            .HaveNameEndingWith("Settings")
            .GetTypes();

        // Check that config types are POCOs (no complex dependencies)
        var violatingTypes = new List<string>();

        foreach (var type in configTypes)
        {
            var constructors = type.GetConstructors();
            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                // Options classes should have parameterless constructors or simple property injection
                if (parameters.Length > 0)
                {
                    violatingTypes.Add($"{type.Name} has constructor dependencies (should be POCO)");
                }
            }
        }

        // Assert
        Assert.Empty(violatingTypes);
    }

    /// <summary>
    /// Resolve the repository source root by walking up from the test output directory.
    /// </summary>
    private static string ResolveSourceRoot()
    {
        var currentDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(currentDir);

        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return currentDir;
    }
}
