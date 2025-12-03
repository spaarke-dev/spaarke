using NetArchTest.Rules;
using Xunit;

namespace Spaarke.ArchTests;

/// <summary>
/// ADR-009: Caching policy — Redis-first with per-request cache
/// Validates that IMemoryCache is not used for cross-request caching.
/// Redis (IDistributedCache) should be the only cross-request cache.
/// </summary>
public class ADR009_CachingTests
{
    [Fact(DisplayName = "ADR-009: IMemoryCache should not be registered as Singleton")]
    public void MemoryCacheShouldNotBeSingleton()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Check Program.cs or startup configuration
        // This test verifies that no service registration adds IMemoryCache as Singleton
        // Note: This is a structural check; runtime DI inspection is more comprehensive
        var programType = assembly.GetType("Program");
        Assert.NotNull(programType);

        // For this test, we verify that controllers/endpoints don't inject IMemoryCache
        // as that would indicate cross-request caching
        var result = Types.InAssembly(assembly)
            .That()
            .HaveNameEndingWith("Endpoints")
            .Or()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOn("Microsoft.Extensions.Caching.Memory.IMemoryCache")
            .GetResult();

        // Assert
        Assert.True(
            result.IsSuccessful,
            $"ADR-009 violation: Endpoints/Controllers should not inject IMemoryCache. " +
            $"Use IDistributedCache (Redis) for cross-request caching. " +
            $"IMemoryCache is only allowed for per-request/scoped scenarios. " +
            $"Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    [Fact(DisplayName = "ADR-009: Services should prefer IDistributedCache")]
    public void ServicesShouldPreferDistributedCache()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;

        // Act - Find cache service implementations
        var cacheServiceTypes = Types.InAssembly(assembly)
            .That()
            .HaveNameMatching(".*CacheService")
            .Or()
            .HaveNameMatching(".*Cache")
            .GetTypes()
            .Where(t => !t.IsInterface && !t.IsAbstract);

        // Check that cache services use IDistributedCache
        var violatingTypes = new List<string>();

        foreach (var type in cacheServiceTypes)
        {
            var constructors = type.GetConstructors();
            foreach (var ctor in constructors)
            {
                var parameters = ctor.GetParameters();
                var hasDistributedCache = parameters.Any(p =>
                    p.ParameterType.Name == "IDistributedCache");
                var hasMemoryCache = parameters.Any(p =>
                    p.ParameterType.Name == "IMemoryCache");

                // If using cache, should prefer IDistributedCache over IMemoryCache
                if (hasMemoryCache && !hasDistributedCache)
                {
                    violatingTypes.Add($"{type.FullName} uses IMemoryCache without IDistributedCache");
                }
            }
        }

        // Assert
        Assert.Empty(violatingTypes);
    }

    [Fact(DisplayName = "ADR-009: No hybrid caching libraries should be used")]
    public void NoHybridCachingLibraries()
    {
        // Arrange
        var assembly = typeof(Program).Assembly;
        var forbiddenLibraries = new[]
        {
            "Microsoft.Extensions.Caching.Hybrid",
            "CacheTower",
            "LazyCache",
            "EasyCaching"
        };

        // Act & Assert
        foreach (var forbidden in forbiddenLibraries)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn(forbidden)
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                $"ADR-009 violation: Hybrid caching library '{forbidden}' detected. " +
                $"Use Redis (IDistributedCache) only for cross-request caching. " +
                $"Add L1 cache only if profiling proves necessary. " +
                $"Failing types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
        }
    }
}
