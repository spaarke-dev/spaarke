using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-001: Minimal API + BackgroundService; do not use Azure Functions
/// Validates that no Azure Functions or Durable Functions packages are referenced.
/// </summary>
public class ADR001_MinimalApiTests
{
    private static readonly string[] ForbiddenNamespaces = new[]
    {
        "Microsoft.Azure.WebJobs",
        "Microsoft.Azure.Functions",
        "Microsoft.DurableTask",
        "DurableTask.Core",
        "DurableTask.AzureStorage"
    };

    [Fact(DisplayName = "ADR-001: No Azure Functions packages should be referenced")]
    public void NoAzureFunctionsPackages()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act & Assert
        foreach (var forbiddenNamespace in ForbiddenNamespaces)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn(forbiddenNamespace)
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"ADR-001 violation: Found dependency on forbidden namespace '{forbiddenNamespace}'. " +
                $"Azure Functions are not permitted. Use Minimal API + BackgroundService instead. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
        }
    }

    [Fact(DisplayName = "ADR-001: No Azure Functions attributes should be used")]
    public void NoAzureFunctionsAttributes()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var forbiddenAttributes = new[]
        {
            "FunctionNameAttribute",
            "TimerTriggerAttribute",
            "QueueTriggerAttribute",
            "ServiceBusTriggerAttribute",
            "HttpTriggerAttribute"
        };

        // Act
        var allTypes = Types.InAssembly(assembly).GetTypes();

        // Assert
        foreach (var type in allTypes)
        {
            var attributes = type.GetCustomAttributes(false);
            foreach (var attr in attributes)
            {
                var attrTypeName = attr.GetType().Name;
                Assert.DoesNotContain(forbiddenAttributes, forbidden => attrTypeName == forbidden);
            }
        }
    }
}
