using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-007: SPE storage seam minimalism (single focused facade)
/// Validates that Microsoft.Graph types only appear in Infrastructure.Graph or SpeFileStore.
/// Controllers and endpoints must not directly reference Graph SDK types.
/// </summary>
public class ADR007_GraphIsolationTests
{
    [Fact(DisplayName = "ADR-007: Graph SDK types must be isolated to Infrastructure layer")]
    public void GraphTypesMustBeIsolatedToInfrastructure()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var allowedNamespaces = new[] { "Infrastructure.Graph", "SpeFileStore" };

        // Act - Check that only allowed namespaces reference Microsoft.Graph
        var allTypes = Types.InAssembly(assembly).GetTypes();
        var violatingTypes = new List<string>();

        foreach (var type in allTypes)
        {
            // Skip types in allowed namespaces
            if (allowedNamespaces.Any(allowed => type.Namespace?.Contains(allowed) == true))
                continue;

            // Check if type uses any Microsoft.Graph types
            var fields = type.GetFields();
            var properties = type.GetProperties();
            var methods = type.GetMethods();

            foreach (var field in fields)
            {
                if (field.FieldType.Namespace?.StartsWith("Microsoft.Graph") == true)
                {
                    violatingTypes.Add($"{type.FullName}.{field.Name} (field type: {field.FieldType.Name})");
                }
            }

            foreach (var property in properties)
            {
                if (property.PropertyType.Namespace?.StartsWith("Microsoft.Graph") == true)
                {
                    violatingTypes.Add($"{type.FullName}.{property.Name} (property type: {property.PropertyType.Name})");
                }
            }

            foreach (var method in methods)
            {
                if (method.ReturnType.Namespace?.StartsWith("Microsoft.Graph") == true)
                {
                    violatingTypes.Add($"{type.FullName}.{method.Name}() (return type: {method.ReturnType.Name})");
                }

                foreach (var param in method.GetParameters())
                {
                    if (param.ParameterType.Namespace?.StartsWith("Microsoft.Graph") == true)
                    {
                        violatingTypes.Add($"{type.FullName}.{method.Name}({param.Name}) (param type: {param.ParameterType.Name})");
                    }
                }
            }
        }

        // Assert
        Assert.Empty(violatingTypes);
    }

    [Fact(DisplayName = "ADR-007: Controllers must not reference Graph SDK")]
    public void ControllersShouldNotReferenceGraphSdk()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .That()
            .ResideInNamespace("Controllers")
            .Or()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Graph")
            .GetResult();

        // Assert
        Assert.True(
            result.IsSuccessful,
            $"ADR-007 violation: Controllers must not reference Microsoft.Graph directly. " +
            $"Use SpeFileStore facade instead. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact(DisplayName = "ADR-007: Endpoints must not reference Graph SDK")]
    public void EndpointsShouldNotReferenceGraphSdk()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act
        var result = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("Endpoints")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Graph")
            .GetResult();

        // Assert
        Assert.True(
            result.IsSuccessful,
            $"ADR-007 violation: Endpoint classes must not reference Microsoft.Graph directly. " +
            $"Use SpeFileStore facade to isolate Graph SDK types. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }
}
