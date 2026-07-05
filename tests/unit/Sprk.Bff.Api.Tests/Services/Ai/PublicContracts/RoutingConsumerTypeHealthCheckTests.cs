using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// FR-P0-04 boot reconciliation tests (spaarke-ai-architecture-redesign-r1
/// task 005): seeded catalog drift of each class must report
/// <see cref="HealthStatus.Unhealthy"/> with named diagnostics; a healthy
/// catalog, a disabled AI subsystem, and a transient Dataverse error must
/// never report Unhealthy. Mocks sit at the module boundary only
/// (<see cref="IGenericEntityService"/> = the Dataverse boundary, per
/// ADR-038); the handler registry is the REAL <see cref="ToolHandlerRegistry"/>
/// fed with in-memory handler doubles so the bijection is exercised through
/// the same abstraction task 008/009 handlers register through.
/// </summary>
public sealed class RoutingConsumerTypeHealthCheckTests
{
    private readonly Mock<IGenericEntityService> _entityServiceMock = new();

    // ── Row builders ────────────────────────────────────────────────────────

    private static Entity BindingRow(string consumerType)
    {
        var entity = new Entity("sprk_playbookconsumer");
        entity["sprk_consumertype"] = consumerType;
        entity["sprk_enabled"] = true;
        return entity;
    }

    private static Entity[] BindingRowsForAllConstants() =>
        ConsumerTypes.All.Select(BindingRow).ToArray();

    private static Entity ToolRow(string name, string? handlerClass, string? toolId = null)
    {
        var entity = new Entity("sprk_analysistool");
        entity["sprk_name"] = name;
        entity["sprk_handlerclass"] = handlerClass;
        entity["sprk_toolid"] = toolId;
        return entity;
    }

    // ── Mock plumbing ───────────────────────────────────────────────────────

    private void SetupBindingRows(params Entity[] rows) =>
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_playbookconsumer"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    private void SetupToolRows(params Entity[] rows) =>
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysistool"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(rows.ToList()));

    private RoutingConsumerTypeHealthCheck CreateSut(
        IEnumerable<string>? handlerIds = null,
        Dictionary<string, string?>? configValues = null,
        bool registerEntityService = true,
        bool registerRegistry = true)
    {
        var services = new ServiceCollection();

        if (registerEntityService)
        {
            services.AddSingleton(_entityServiceMock.Object);
        }

        if (registerRegistry)
        {
            var handlers = (handlerIds ?? Array.Empty<string>())
                .Select(id => (IToolHandler)new FakeToolHandler(id));
            var registry = new ToolHandlerRegistry(
                handlers,
                Options.Create(new ToolFrameworkOptions()),
                NullLogger<ToolHandlerRegistry>.Instance);
            services.AddSingleton<IToolHandlerRegistry>(registry);
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues ?? new Dictionary<string, string?>())
            .Build();

        return new RoutingConsumerTypeHealthCheck(
            services.BuildServiceProvider(),
            configuration,
            NullLogger<RoutingConsumerTypeHealthCheck>.Instance);
    }

    private static Task<HealthCheckResult> CheckAsync(RoutingConsumerTypeHealthCheck sut) =>
        sut.CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration(
                "ai-catalog-reconciliation", sut, failureStatus: null, tags: null),
        });

    // ── Healthy paths ───────────────────────────────────────────────────────

    [Fact]
    public async Task CheckHealthAsync_HealthyCatalog_ReturnsHealthy()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha"),
            ToolRow("Beta Tool", "BetaHandler", "dataverse.beta"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler", "BetaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("bijection");
    }

    [Fact]
    public async Task CheckHealthAsync_AiSubsystemDisabled_ReturnsHealthyWithoutQueryingDataverse()
    {
        var sut = CreateSut(
            handlerIds: new[] { "AlphaHandler" },
            configValues: new Dictionary<string, string?> { ["Analysis:Enabled"] = "false" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("disabled");
        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckHealthAsync_ToolFrameworkDisabled_SkipsBijectionAndStaysHealthy()
    {
        // Only the binding query should run; the tool query is never issued and
        // an empty registry must NOT be reported as orphan-row drift.
        SetupBindingRows(BindingRowsForAllConstants());
        var sut = CreateSut(
            handlerIds: Array.Empty<string>(),
            configValues: new Dictionary<string, string?> { ["ToolFramework:Enabled"] = "false" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("skipped");
        _entityServiceMock.Verify(
            s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysistool"),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CheckHealthAsync_EntityServiceNotRegistered_ReturnsHealthySkipped()
    {
        var sut = CreateSut(registerEntityService: false);

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("skipped");
    }

    // ── Drift class (a): constants ↔ Binding rows ──────────────────────────

    [Fact]
    public async Task CheckHealthAsync_ConstantWithoutBindingRow_ReturnsUnhealthyNamingConstant()
    {
        var rows = ConsumerTypes.All
            .Where(t => t != ConsumerTypes.MatterPreFill)
            .Select(BindingRow)
            .ToArray();
        SetupBindingRows(rows);
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain(ConsumerTypes.MatterPreFill);
        result.Description.Should().Contain("without Binding row");
    }

    [Fact]
    public async Task CheckHealthAsync_BindingRowWithoutConstant_ReturnsUnhealthyNamingRowValue()
    {
        // The 2026-06-24 UAT-2 incident shape: an admin typo on the Dataverse side.
        var rows = BindingRowsForAllConstants()
            .Append(BindingRow("matter-pre-fil"))
            .ToArray();
        SetupBindingRows(rows);
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("matter-pre-fil");
        result.Description.Should().Contain("without ConsumerTypes constant");
    }

    [Fact]
    public async Task CheckHealthAsync_EmptyBindingTable_ReturnsUnhealthyListingEveryConstant()
    {
        // FR-P0-04 upgrade: an unseeded environment was previously a WARN-and-
        // continue; under the closed-catalog gate it is maximal drift.
        SetupBindingRows();
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        foreach (var constant in ConsumerTypes.All)
        {
            result.Description.Should().Contain(constant);
        }
    }

    // ── Drift class (b): tool row ↔ handler bijection ──────────────────────

    [Fact]
    public async Task CheckHealthAsync_ToolRowWithoutRegisteredHandler_ReturnsUnhealthyNamingHandlerClass()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler"),
            ToolRow("Ghost Tool", "GhostHandler", "dataverse.ghost"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("GhostHandler");
        result.Description.Should().Contain("Ghost Tool");
        result.Description.Should().Contain("without a registered handler");
    }

    [Fact]
    public async Task CheckHealthAsync_HandlerWithoutToolRow_ReturnsUnhealthyNamingOrphanHandler()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(ToolRow("Alpha Tool", "AlphaHandler"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler", "OrphanHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("OrphanHandler");
        result.Description.Should().Contain("orphan handlers");
    }

    [Fact]
    public async Task CheckHealthAsync_DuplicateToolRowsForOneHandler_ReturnsUnhealthyNamingHandler()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler", "dataverse.alpha"),
            ToolRow("Alpha Tool Copy", "AlphaHandler", "dataverse.alpha_copy"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("AlphaHandler (2 rows)");
        result.Description.Should().Contain("bijection violated");
    }

    [Fact]
    public async Task CheckHealthAsync_ToolRowMissingHandlerClass_ReturnsUnhealthyNamingRow()
    {
        SetupBindingRows(BindingRowsForAllConstants());
        SetupToolRows(
            ToolRow("Alpha Tool", "AlphaHandler"),
            ToolRow("Untethered Tool", handlerClass: null));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Untethered Tool");
        result.Description.Should().Contain("missing sprk_handlerclass");
    }

    [Fact]
    public async Task CheckHealthAsync_MultipleDriftClasses_ReportsEveryClassInOneDescription()
    {
        var rows = ConsumerTypes.All
            .Where(t => t != ConsumerTypes.ChatSummarize)
            .Select(BindingRow)
            .Append(BindingRow("typo-consumer"))
            .ToArray();
        SetupBindingRows(rows);
        SetupToolRows(ToolRow("Ghost Tool", "GhostHandler"));
        var sut = CreateSut(handlerIds: new[] { "OrphanHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain(ConsumerTypes.ChatSummarize);
        result.Description.Should().Contain("typo-consumer");
        result.Description.Should().Contain("GhostHandler");
        result.Description.Should().Contain("OrphanHandler");
    }

    // ── Fail-soft: transient errors are not drift ───────────────────────────

    [Fact]
    public async Task CheckHealthAsync_DataverseUnavailable_ReturnsDegradedNotUnhealthy()
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unreachable"));
        var sut = CreateSut(handlerIds: new[] { "AlphaHandler" });

        var result = await CheckAsync(sut);

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    // ── In-memory handler double (module-boundary only; never executed) ─────

    private sealed class FakeToolHandler : IToolHandler
    {
        public FakeToolHandler(string handlerId) => HandlerId = handlerId;

        public string HandlerId { get; }

        public ToolHandlerMetadata Metadata { get; } = new(
            Name: "Fake Tool Handler",
            Description: "Reconciliation test double",
            Version: "1.0.0",
            SupportedInputTypes: Array.Empty<string>(),
            Parameters: Array.Empty<ToolParameterDefinition>());

        public IReadOnlyList<ToolType> SupportedToolTypes { get; } = new[] { ToolType.Custom };

        public ToolValidationResult Validate(ToolExecutionContext context, AnalysisTool tool) =>
            ToolValidationResult.Success();

        public Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            AnalysisTool tool,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not exercised by reconciliation tests.");
    }
}
