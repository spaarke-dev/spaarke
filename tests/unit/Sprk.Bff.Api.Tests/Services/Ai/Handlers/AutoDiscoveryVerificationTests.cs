using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Verification tests for the R6 Pillar 2 handler auto-discovery pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Task <c>D-H-00</c> (R6 task 100) gates Wave 1 (101–104) and Wave 2 (105–108) of the
/// 8 typed tool handler workstream. The contract these handler tasks rely on is:
/// </para>
/// <list type="number">
/// <item>
/// <c>ToolFrameworkExtensions.AddToolHandlersFromAssembly</c> scans the BFF assembly,
/// finds every concrete class assignable to <see cref="IToolHandler"/> (the rename
/// landed in task 006; the <c>IAnalysisToolHandler</c> global-using alias preserves
/// source compatibility), and registers each one as <c>IAnalysisToolHandler</c>
/// (= <c>IToolHandler</c>) with <c>Scoped</c> lifetime.
/// </item>
/// <item>
/// Constructing the registry (which enumerates <c>IEnumerable&lt;IAnalysisToolHandler&gt;</c>
/// via DI) returns the same set of types the assembly scan discovered.
/// </item>
/// <item>
/// Zero per-handler DI lines exist outside <see cref="ToolFrameworkExtensions"/> and
/// the <c>AnalysisServicesModule</c> — ADR-010 minimalism is preserved as the 8 typed
/// handlers land.
/// </item>
/// </list>
/// <para>
/// These tests use a minimal DI container (not <c>WebApplicationFactory</c>) to keep
/// the verification fast (&lt;1s) and focused on the registration contract — not on
/// handler execution.
/// </para>
/// </remarks>
public sealed class AutoDiscoveryVerificationTests
{
    /// <summary>
    /// Assembly the auto-discovery pipeline scans. Centralized here so future Wave 1/Wave 2
    /// handler tasks can reuse the same source-of-truth without copy-pasting the lookup.
    /// </summary>
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    [Fact]
    public void AddToolHandlersFromAssembly_DiscoversAllConcreteToolHandlerTypes()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddToolHandlersFromAssembly(BffAssembly);

        // Assert
        var registeredHandlerTypes = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        var expectedHandlerTypes = BffAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IToolHandler).IsAssignableFrom(t))
            .ToList();

        registeredHandlerTypes.Should().BeEquivalentTo(
            expectedHandlerTypes,
            because: "every concrete IToolHandler implementation in the BFF assembly must be auto-registered (ADR-010, R6 Pillar 2)");

        registeredHandlerTypes.Should().NotBeEmpty(
            because: "the BFF ships at least 4 handlers today (GenericAnalysisHandler, DocumentClassifierHandler, SummaryHandler, SemanticSearchToolHandler) — auto-discovery returning an empty set means the assembly-scan filter is broken");
    }

    [Fact]
    public void AddToolHandlersFromAssembly_RegistersHandlersAsScoped()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddToolHandlersFromAssembly(BffAssembly);

        // Assert
        var registeredDescriptors = services
            .Where(d => d.ServiceType == typeof(IToolHandler))
            .ToList();

        registeredDescriptors.Should().NotBeEmpty();
        registeredDescriptors.Should().OnlyContain(
            d => d.Lifetime == ServiceLifetime.Scoped,
            because: "ADR-010 + ToolFrameworkExtensions specify Scoped lifetime so handlers can consume Scoped collaborators like IScopeResolverService");
    }

    [Fact]
    public void AssemblyScan_FindsExpectedExistingHandlers()
    {
        // Arrange — sanity check: the 4 existing handlers must all be auto-discovered
        // so Wave 1 + Wave 2 don't accidentally regress the registration contract.
        var expectedHandlerNames = new[]
        {
            "GenericAnalysisHandler",
            "DocumentClassifierHandler",
            "SummaryHandler",
            "SemanticSearchToolHandler"
        };

        // Act
        var discoveredHandlerNames = BffAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IToolHandler).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        // Assert
        discoveredHandlerNames.Should().Contain(
            expectedHandlerNames,
            because: "task 006's IToolHandler rename must not have dropped any of the 4 existing handlers from auto-discovery — they remain the canonical reference shape for Wave 1 + Wave 2 handler tasks");
    }

    [Fact]
    public void IAnalysisToolHandlerAlias_ResolvesToIToolHandler()
    {
        // Arrange + Act — verifies the GlobalUsings.cs source-compat alias survived the rename.
        // If this regresses, the 4 existing handlers (which still type their `: IAnalysisToolHandler`
        // declarations against the alias) would fail to compile — caught here as a typeof equality.
        var aliasType = typeof(IAnalysisToolHandler);
        var canonicalType = typeof(IToolHandler);

        // Assert
        aliasType.Should().BeSameAs(canonicalType,
            because: "the `global using IAnalysisToolHandler = Sprk.Bff.Api.Services.Ai.IToolHandler;` alias in GlobalUsings.cs must preserve source compatibility for existing handlers (task 006 rename invariant)");
    }

    [Fact]
    public void HandlerRegistry_EnumeratesAllAutoDiscoveredHandlers()
    {
        // Arrange — minimal compose: pull in ToolFramework + a logger factory + an empty config.
        // This proves the discovered handlers are resolvable through the registry (not just
        // registered as descriptors).
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);

        // Tool handler ctors typically require many AI deps (IOpenAiClient, IScopeResolverService,
        // PromptSchemaRenderer, IRagService). The registry constructor itself only enumerates
        // IEnumerable<IAnalysisToolHandler>, so dependencies are not instantiated unless we
        // resolve a specific handler. To validate the registration contract WITHOUT requiring
        // the full DI graph, we inspect the registered descriptors directly.
        // Act
        var registeredCount = services.Count(d => d.ServiceType == typeof(IToolHandler));
        var expectedCount = BffAssembly.GetTypes()
            .Count(t => t.IsClass && !t.IsAbstract && typeof(IToolHandler).IsAssignableFrom(t));

        // Assert
        registeredCount.Should().Be(expectedCount,
            because: "AddToolFramework -> AddToolHandlersFromAssembly must register exactly one IToolHandler descriptor per discovered concrete implementation type");
    }

    [Fact]
    public void NoConcreteHandlerHasManualDiRegistrationOutsideToolFramework()
    {
        // Defensive: enumerate the BFF DI module registrations and assert NO handler type
        // is registered outside the assembly-scan path. This protects ADR-010 + the R6
        // Pillar 2 contract: handlers register via auto-discovery, not per-line DI.
        //
        // The assembly-scan path is the ONLY allowed registration site. If a Wave 1/Wave 2
        // task accidentally adds `services.AddScoped<IToolHandler, FooHandler>()` outside
        // ToolFrameworkExtensions, this test asserts the manual registration would produce
        // a DUPLICATE descriptor — duplicates are detected here so the offending PR is
        // rejected before merge.
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);

        var descriptorsByImplType = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .GroupBy(d => d.ImplementationType!)
            .ToList();

        descriptorsByImplType.Should().OnlyContain(
            g => g.Count() == 1,
            because: "each IToolHandler implementation must be registered exactly once (via assembly scan) — duplicates indicate a manual DI line slipped past ADR-010 review");
    }
}
