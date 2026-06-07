using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Per-handler contract test template — each typed handler task (101–108) copies this
/// shape into its own test class and substitutes <c>TemplateHandler</c> with the concrete
/// handler under test.
/// </summary>
/// <remarks>
/// <para>
/// Purpose: provides the 4 binding asserts every typed handler PR must ship so the R6
/// Pillar 2 contract (project CLAUDE.md "Every handler MUST...") is verified mechanically
/// in CI. The template runs against a SAMPLE handler defined in
/// <c>src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/TemplateHandler.cs</c> so the
/// test class itself stays green and serves as a working reference.
/// </para>
/// <para>
/// What each typed handler test class MUST include (the 4-point contract):
/// </para>
/// <list type="number">
/// <item>
/// <c>HandlerType_IsRegisteredInDi</c>: the concrete type IS discovered via the assembly
/// scan and IS resolvable as <see cref="IToolHandler"/>.
/// </item>
/// <item>
/// <c>Handler_IsDiscoverableByHandlerClassName</c>: <c>handler.HandlerId</c> matches the
/// C# class name (the <c>sprk_handlerclass</c> column lookup convention).
/// </item>
/// <item>
/// <c>Metadata_IsValid</c>: <c>handler.Metadata</c> is non-null, version is semver, name
/// + description are non-empty.
/// </item>
/// <item>
/// <c>SupportedToolTypes_IsNonEmpty</c>: handler declares at least one tool type so the
/// registry can index it.
/// </item>
/// </list>
/// <para>
/// Wave 1 (101–104) and Wave 2 (105–108) handler test classes copy these 4 tests
/// verbatim and replace <see cref="TemplateHandler"/> with their concrete handler. The
/// template itself stays in the repo as a living reference + a "wires-up" green test that
/// proves the contract is enforceable.
/// </para>
/// </remarks>
public sealed class HandlerContractTestTemplate
{
    private static readonly Assembly BffAssembly = typeof(IToolHandler).Assembly;

    // ─────────────────────────────────────────────────────────────────────────────
    // (1) Registration test — auto-discovery picks up the handler.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HandlerType_IsRegisteredInDi()
    {
        // Arrange
        var services = BuildToolFrameworkServiceCollection();

        // Act
        var registeredImplementations = services
            .Where(d => d.ServiceType == typeof(IToolHandler) && d.ImplementationType is not null)
            .Select(d => d.ImplementationType!)
            .ToList();

        // Assert — the handler type IS registered.
        // Wave 1/Wave 2 tasks substitute typeof(TemplateHandler) with their handler type.
        registeredImplementations.Should().Contain(
            typeof(TemplateHandler),
            because: "the handler type must be auto-discovered by the assembly scan (R6 Pillar 2: no manual DI lines per handler)");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (2) Discoverability test — HandlerId matches the C# class name.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Handler_IsDiscoverableByHandlerClassName()
    {
        // Arrange
        var handler = new TemplateHandler();

        // Act + Assert — HandlerId must equal the C# class name so the
        // sprk_analysistool.sprk_handlerclass Dataverse column can route to this handler.
        handler.HandlerId.Should().Be(
            nameof(TemplateHandler),
            because: "R6 Pillar 2 binding: HandlerId == nameof(handler class) so the Dataverse sprk_handlerclass field routes to this handler at runtime");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (3) Metadata test — non-null + semver + non-empty descriptive fields.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Metadata_IsValid()
    {
        // Arrange
        var handler = new TemplateHandler();

        // Act
        var metadata = handler.Metadata;

        // Assert — metadata must be non-null and minimally well-formed.
        metadata.Should().NotBeNull();
        metadata.Name.Should().NotBeNullOrWhiteSpace(
            because: "tool registry uses metadata.Name in UI surfaces (admin views, debug pages)");
        metadata.Description.Should().NotBeNullOrWhiteSpace(
            because: "tool registry exposes Description to the LLM at chat-tool registration time");
        metadata.Version.Should().MatchRegex(
            @"^\d+\.\d+\.\d+$",
            because: "semver enables compatibility tracking across handler versions");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // (4) Supported-types test — at least one ToolType for registry indexing.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SupportedToolTypes_IsNonEmpty()
    {
        // Arrange
        var handler = new TemplateHandler();

        // Act
        var supportedTypes = handler.SupportedToolTypes;

        // Assert — the registry indexes handlers by ToolType (GetHandlersByType).
        // Empty list = handler is never resolvable by type and is dead code in the registry.
        supportedTypes.Should().NotBeNullOrEmpty(
            because: "ToolHandlerRegistry.GetHandlersByType requires at least one ToolType per handler for type-based lookup to work");
    }

    /// <summary>
    /// Builds a minimal <see cref="ServiceCollection"/> with the tool framework registered
    /// (mirrors what <c>AnalysisServicesModule.AddToolFramework</c> does in production DI).
    /// </summary>
    private static IServiceCollection BuildToolFrameworkServiceCollection()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddToolFramework(configuration);
        return services;
    }
}
