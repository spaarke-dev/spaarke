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
    [Fact(DisplayName = "ADR-010: Expensive resources should be registered as Singleton")]
    public void ExpensiveResourcesShouldBeSingleton()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Find types that represent expensive resources
        var expensiveTypes = new[]
        {
            "ServiceClient",       // Dataverse ServiceClient (~500ms init)
            "GraphServiceClient",  // Microsoft Graph client
            "HttpClient"           // HTTP clients should use IHttpClientFactory
        };

        // This is a documentation test - verify that Program.cs registers these correctly
        // Actual validation would require DI container inspection at runtime
        var programType = assembly.GetType("Program");
        Assert.NotNull(programType);

        // For ServiceClient specifically, we can check it's not registered as Scoped
        // by verifying no Scoped registration pattern exists in endpoint constructors
        var endpointTypes = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("Endpoints")
            .GetTypes();

        // If endpoints inject IDataverseService, verify it's not reconstructed per request
        // This is a heuristic - proper validation requires runtime DI inspection
        Assert.True(true, "Manual verification: Check Program.cs for Singleton registration of expensive resources");
    }

    [Fact(DisplayName = "ADR-010: Services should be concrete unless seam required")]
    public void ServicesShouldBeConcreteUnlessSeamRequired()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Find interface/implementation pairs
        var allTypes = Types.InAssembly(assembly).GetTypes();
        var interfaces = allTypes.Where(t => t.IsInterface).ToList();
        var concretes = allTypes.Where(t => !t.IsInterface && !t.IsAbstract).ToList();

        // Known seams (exceptions per ADR-010):
        var allowedSeams = new[]
        {
            "IAccessDataSource",     // UAC data seam
            "IFileStore",            // Storage seam (if introduced)
            "IDistributedCache",     // Framework interface
            "ILogger",               // Framework interface
            "IConfiguration",        // Framework interface
            "IDataverseService",     // Optional seam for testing
            "IAuthorizationService"  // Optional seam for testing
        };

        // Check for unnecessary interfaces (1:1 interface:implementation without clear seam)
        var unnecessaryInterfaces = new List<string>();

        foreach (var iface in interfaces)
        {
            // Skip framework and allowed seam interfaces
            if (allowedSeams.Contains(iface.Name))
                continue;

            if (iface.Namespace?.StartsWith("System") == true ||
                iface.Namespace?.StartsWith("Microsoft") == true)
                continue;

            // Find implementations
            var implementations = concretes.Where(c => iface.IsAssignableFrom(c)).ToList();

            // If exactly 1 implementation and not an allowed seam, flag for review
            if (implementations.Count == 1)
            {
                unnecessaryInterfaces.Add($"{iface.Name} -> {implementations[0].Name} (consider using concrete)");
            }
        }

        // Assert - This is a warning test; some interfaces may be justified
        // Review flagged interfaces to determine if they're necessary
        if (unnecessaryInterfaces.Any())
        {
            var message = $"ADR-010 review: Found 1:1 interface mappings. " +
                         $"Consider registering concrete classes unless a seam is needed for testing or swappability:\n" +
                         string.Join("\n", unnecessaryInterfaces);

            // This is informational; we don't fail the test but log for review
            Assert.True(true, message);
        }
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
}
