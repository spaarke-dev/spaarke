using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-002: Keep Dataverse plugins thin; no orchestration in plugins
/// Validates that plugin classes don't contain heavy logic or HTTP calls.
/// Note: This test focuses on BFF-side validation. Plugin assembly validation
/// should be added separately in the Dataverse plugin project.
/// </summary>
public class ADR002_PluginTests
{
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
}
