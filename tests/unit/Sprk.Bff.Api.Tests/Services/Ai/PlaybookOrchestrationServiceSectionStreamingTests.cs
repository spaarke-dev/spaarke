using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat.SseEventTypes;
using Sprk.Bff.Api.Services.Ai.Insights.Routing;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for the per-section SSE streaming surface emitted by
/// <see cref="PlaybookOrchestrationService"/> when a
/// <see cref="NodeType.DeliverComposite"/> node completes (FR-53 /
/// chat-routing-redesign-r1 task 114a).
/// </summary>
/// <remarks>
/// <para>
/// Asserts the contract:
/// </para>
/// <list type="bullet">
///   <item>For an N-section composite payload, the orchestrator emits exactly N triples of
///     <see cref="PlaybookEventType.SectionStarted"/>/<see cref="PlaybookEventType.SectionData"/>/<see cref="PlaybookEventType.SectionCompleted"/>
///     events in completion order, keyed by section name.</item>
///   <item>Section names are deterministic configuration identifiers; the payload IS
///     the load-bearing correlation key (NOT schema position).</item>
///   <item>Empty composite payloads emit ZERO section events (FR-52: a partial / empty
///     composite is a valid composite).</item>
///   <item><b>Backward-compat invariant (binding)</b>: a schema-position playbook (i.e.,
///     a non-composite Output node dispatched as <see cref="ExecutorType.DeliverOutput"/>)
///     emits ZERO section events. The existing <c>FieldDelta</c> flow on a different
///     stream surface (<see cref="PlaybookExecutionEngine.ExecuteChatSummarizeAsync"/>)
///     is unaffected by these events — this test asserts the non-emission contract on
///     the orchestrator's stream surface, which is the surface 114a modifies.</item>
///   <item>ADR-015 tier-1 payload safety: serialized event JSON contains ONLY section
///     names, content text, and citation metadata (no user message text / embeddings /
///     internal diagnostic IDs).</item>
/// </list>
/// </remarks>
public class PlaybookOrchestrationServiceSectionStreamingTests
{
    private readonly Mock<INodeService> _nodeServiceMock;
    private readonly Mock<INodeExecutorRegistry> _executorRegistryMock;
    private readonly Mock<IScopeResolverService> _scopeResolverMock;
    private readonly Mock<IAnalysisOrchestrationService> _legacyOrchestratorMock;
    private readonly Mock<IInsightsActionRouter> _insightsRouterMock;
    private readonly Mock<ILogger<PlaybookOrchestrationService>> _loggerMock;
    private readonly PlaybookOrchestrationService _service;
    private readonly HttpContext _mockHttpContext;

    public PlaybookOrchestrationServiceSectionStreamingTests()
    {
        _nodeServiceMock = new Mock<INodeService>();
        _executorRegistryMock = new Mock<INodeExecutorRegistry>();
        _scopeResolverMock = new Mock<IScopeResolverService>();
        _legacyOrchestratorMock = new Mock<IAnalysisOrchestrationService>();
        _insightsRouterMock = new Mock<IInsightsActionRouter>();

        // Pass-through Insights routing: return inputs unchanged so tests behave the
        // same as pre-Wave-D4 (matches PlaybookOrchestrationServiceTests fixture).
        _insightsRouterMock
            .Setup(r => r.ResolveLayer1ActionAsync(It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, AnalysisAction action, CancellationToken _) => action);
        _insightsRouterMock
            .Setup(r => r.ResolveLayer2ActionAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<AnalysisAction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? _, string? __, AnalysisAction action, CancellationToken _) => InsightsLayer2RoutingResult.PassThrough(action));

        _loggerMock = new Mock<ILogger<PlaybookOrchestrationService>>();
        _mockHttpContext = new DefaultHttpContext();

        _service = new PlaybookOrchestrationService(
            _nodeServiceMock.Object,
            _executorRegistryMock.Object,
            _scopeResolverMock.Object,
            _legacyOrchestratorMock.Object,
            _insightsRouterMock.Object,
            // R7 Wave 11 task 111: ITemplateEngine for orchestrator Layer 1 resolution.
            new TemplateEngine(NullLogger<TemplateEngine>.Instance),
            _loggerMock.Object);
    }

    // ===================================================================================
    // Helpers
    // ===================================================================================

    private static PlaybookRunRequest CreateRequest(Guid playbookId) => new()
    {
        PlaybookId = playbookId,
        DocumentIds = new[] { Guid.NewGuid() }
    };

    private static PlaybookNodeDto CreateCompositeNode(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        // Structural node — no Action FK; per R7 FR-07 single-hop dispatch, SprkExecutortype
        // carries the dispatch target directly (task 024). Previously inferred via the
        // NodeType.DeliverComposite switch arm in the now-dead structural fallback.
        ActionId = Guid.Empty,
        OutputVariable = name.ToLowerInvariant().Replace(" ", "_"),
        ExecutionOrder = 1,
        DependsOn = Array.Empty<Guid>(),
        IsActive = true,
        NodeType = NodeType.DeliverComposite,
        SprkExecutortype = ExecutorType.DeliverComposite
    };

    private static PlaybookNodeDto CreateLegacyOutputNode(string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        ActionId = Guid.Empty,
        OutputVariable = name.ToLowerInvariant().Replace(" ", "_"),
        ExecutionOrder = 1,
        DependsOn = Array.Empty<Guid>(),
        IsActive = true,
        // R7 FR-07 single-hop dispatch (task 024) — SprkExecutortype = DeliverOutput.
        NodeType = NodeType.Output,
        SprkExecutortype = ExecutorType.DeliverOutput
    };

    private static CompositeOutputPayload BuildCompositePayload(params string[] sectionNames)
    {
        var sections = sectionNames.Select((name, index) => new CompositeSectionResult
        {
            SectionName = name,
            DisplayLabel = $"Label for {name}",
            TextContent = $"Content for {name}",
            StructuredData = JsonSerializer.SerializeToElement(new { hint = name, ordinal = index }),
            SourceNodeId = Guid.NewGuid(),
            SourceVariable = $"{name}_var"
        }).ToList();

        return new CompositeOutputPayload
        {
            Destination = "workspace",
            WidgetType = "structured-output-stream",
            Sections = sections
        };
    }

    /// <summary>
    /// Configures the executor registry to return a stub executor for
    /// <see cref="ExecutorType.DeliverComposite"/> that produces the given composite payload
    /// as <see cref="NodeOutput.StructuredData"/>. Mirrors the executor's real shape so the
    /// orchestrator's deserialization path lights up.
    /// </summary>
    private void SetupCompositeExecutor(CompositeOutputPayload payload)
    {
        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns((NodeExecutionContext ctx, CancellationToken _) =>
            {
                var output = NodeOutput.Ok(
                    ctx.Node.Id,
                    ctx.Node.OutputVariable,
                    payload) with
                { IsDeliverOutput = true };
                return Task.FromResult(output);
            });

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.DeliverComposite))
            .Returns(mockExecutor.Object);
    }

    /// <summary>
    /// Configures the executor registry to return a stub executor for
    /// <see cref="ExecutorType.DeliverOutput"/> (the legacy schema-position path).
    /// The output carries arbitrary structured data — NOT a composite payload — so the
    /// orchestrator's section-emission guard MUST NOT light up.
    /// </summary>
    private void SetupLegacyDeliverOutputExecutor()
    {
        var mockExecutor = new Mock<INodeExecutor>();
        mockExecutor.Setup(x => x.Validate(It.IsAny<NodeExecutionContext>()))
            .Returns(NodeValidationResult.Success());
        mockExecutor.Setup(x => x.ExecuteAsync(It.IsAny<NodeExecutionContext>(), It.IsAny<CancellationToken>()))
            .Returns((NodeExecutionContext ctx, CancellationToken _) =>
            {
                var output = NodeOutput.Ok(
                    ctx.Node.Id,
                    ctx.Node.OutputVariable,
                    new { tldr = "Legacy schema-position output", fields = new[] { "a", "b" } }) with
                { IsDeliverOutput = true };
                return Task.FromResult(output);
            });

        _executorRegistryMock
            .Setup(x => x.GetExecutor(ExecutorType.DeliverOutput))
            .Returns(mockExecutor.Object);
    }

    // ===================================================================================
    // Tests
    // ===================================================================================

    /// <summary>
    /// Verifies the 9-event emission contract for a 3-section composite payload:
    /// three triples of (started, data, completed), one triple per section, in
    /// completion order (which equals the payload's <see cref="CompositeOutputPayload.Sections"/>
    /// order — the orchestrator iterates sections in the order they appear in the
    /// composite payload).
    /// </summary>
    [Fact]
    public async Task DeliverComposite_ThreeSections_Emits9SectionEvents()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateCompositeNode("Compose");

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });

        SetupCompositeExecutor(BuildCompositePayload("summary", "keyTerms", "actionItems"));

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var sectionEvents = events
            .Where(e => e.Type == PlaybookEventType.SectionStarted
                        || e.Type == PlaybookEventType.SectionData
                        || e.Type == PlaybookEventType.SectionCompleted)
            .ToList();

        sectionEvents.Should().HaveCount(9,
            "FR-53: 3 sections × 3 events (started/data/completed) = 9 section events");

        var startedEvents = sectionEvents.Where(e => e.Type == PlaybookEventType.SectionStarted).ToList();
        var dataEvents = sectionEvents.Where(e => e.Type == PlaybookEventType.SectionData).ToList();
        var completedEvents = sectionEvents.Where(e => e.Type == PlaybookEventType.SectionCompleted).ToList();

        startedEvents.Should().HaveCount(3);
        dataEvents.Should().HaveCount(3);
        completedEvents.Should().HaveCount(3);
    }

    /// <summary>
    /// Verifies sections are emitted in completion order (which for Phase A equals
    /// payload iteration order — the orchestrator iterates
    /// <see cref="CompositeOutputPayload.Sections"/> sequentially), each event carrying
    /// the correct <c>SectionIndex</c> + <c>TotalSections</c>, and the started/data/completed
    /// triple for one section is contiguous before moving to the next section.
    /// </summary>
    [Fact]
    public async Task DeliverComposite_SectionEvents_OrderedByCompletion_WithCorrectIndices()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateCompositeNode("Compose");

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });

        SetupCompositeExecutor(BuildCompositePayload("alpha", "beta", "gamma"));

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — extract section events in stream order and verify the contiguous-triple
        // emission pattern.
        var sectionEvents = events
            .Where(e => e.Type == PlaybookEventType.SectionStarted
                        || e.Type == PlaybookEventType.SectionData
                        || e.Type == PlaybookEventType.SectionCompleted)
            .ToList();

        sectionEvents.Should().HaveCount(9);

        // Expected order: alpha-started, alpha-data, alpha-completed, beta-started, ...
        sectionEvents[0].Type.Should().Be(PlaybookEventType.SectionStarted);
        sectionEvents[0].SectionPayload!.Started!.SectionName.Should().Be("alpha");
        sectionEvents[0].SectionPayload!.Started!.SectionIndex.Should().Be(0);
        sectionEvents[0].SectionPayload!.Started!.TotalSections.Should().Be(3);

        sectionEvents[1].Type.Should().Be(PlaybookEventType.SectionData);
        sectionEvents[1].SectionPayload!.Data!.SectionName.Should().Be("alpha");

        sectionEvents[2].Type.Should().Be(PlaybookEventType.SectionCompleted);
        sectionEvents[2].SectionPayload!.Completed!.SectionName.Should().Be("alpha");

        sectionEvents[3].SectionPayload!.Started!.SectionName.Should().Be("beta");
        sectionEvents[3].SectionPayload!.Started!.SectionIndex.Should().Be(1);

        sectionEvents[6].SectionPayload!.Started!.SectionName.Should().Be("gamma");
        sectionEvents[6].SectionPayload!.Started!.SectionIndex.Should().Be(2);
        sectionEvents[8].SectionPayload!.Completed!.SectionName.Should().Be("gamma");
    }

    /// <summary>
    /// FR-53: section name is the load-bearing correlation key. Verifies the orchestrator
    /// does NOT emit duplicate section names within a single composite payload —
    /// enforced upstream by <see cref="DeliverCompositeNodeExecutor.Validate"/>, but
    /// asserted on the emission surface here so the contract is doubly-bound.
    /// </summary>
    [Fact]
    public async Task DeliverComposite_SectionNames_AreUniqueWithinCompositePayload()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateCompositeNode("Compose");

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });

        SetupCompositeExecutor(BuildCompositePayload("one", "two", "three"));

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — collect started-event section names; they should form a unique set.
        var sectionNames = events
            .Where(e => e.Type == PlaybookEventType.SectionStarted)
            .Select(e => e.SectionPayload!.Started!.SectionName)
            .ToList();

        sectionNames.Should().OnlyHaveUniqueItems("FR-53: section names are unique within a composite payload");
        sectionNames.Should().BeEquivalentTo(new[] { "one", "two", "three" });
    }

    /// <summary>
    /// FR-52 / FR-53: an empty composite payload is a valid composite — the orchestrator
    /// MUST emit zero section events (the <c>NodeCompleted</c> event remains).
    /// </summary>
    [Fact]
    public async Task DeliverComposite_EmptySectionsList_EmitsZeroSectionEvents()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateCompositeNode("Compose");

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });

        var emptyPayload = new CompositeOutputPayload
        {
            Destination = "workspace",
            WidgetType = "structured-output-stream",
            Sections = Array.Empty<CompositeSectionResult>()
        };
        SetupCompositeExecutor(emptyPayload);

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert
        var sectionEvents = events
            .Where(e => e.Type == PlaybookEventType.SectionStarted
                        || e.Type == PlaybookEventType.SectionData
                        || e.Type == PlaybookEventType.SectionCompleted)
            .ToList();

        sectionEvents.Should().BeEmpty(
            "FR-52: an empty composite is a valid composite — emit zero section events; " +
            "no errors thrown");

        // The NodeCompleted event for the composite node must still be present.
        events.Should().Contain(e => e.Type == PlaybookEventType.NodeCompleted);
    }

    /// <summary>
    /// <b>Backward-compat invariant test name (118R re-runs this)</b>:
    /// <c>SchemaPositionPlaybook_DeliverOutputPath_EmitsZeroSectionEvents</c>.
    /// A legacy <see cref="NodeType.Output"/> node dispatched as
    /// <see cref="ExecutorType.DeliverOutput"/> MUST emit zero <c>section_*</c> events on
    /// the orchestrator's stream surface. The existing <c>FieldDelta</c> flow on a
    /// different stream surface
    /// (<see cref="PlaybookExecutionEngine.ExecuteChatSummarizeAsync"/>) is unaffected
    /// by these events — this test asserts the non-emission contract on the surface
    /// 114a modifies. Until 118R migrates legacy playbooks to DeliverComposite, this
    /// invariant is binding.
    /// </summary>
    [Fact]
    public async Task SchemaPositionPlaybook_DeliverOutputPath_EmitsZeroSectionEvents()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateLegacyOutputNode("Schema-position output");

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });

        SetupLegacyDeliverOutputExecutor();

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — ZERO section events on the orchestrator's stream surface.
        var sectionEvents = events
            .Where(e => e.Type == PlaybookEventType.SectionStarted
                        || e.Type == PlaybookEventType.SectionData
                        || e.Type == PlaybookEventType.SectionCompleted)
            .ToList();

        sectionEvents.Should().BeEmpty(
            "BACKWARD-COMPAT INVARIANT (FR-53): schema-position playbooks (NodeType.Output → " +
            "ExecutorType.DeliverOutput) MUST emit zero section_* events on the orchestrator's " +
            "stream surface until migrated by FR-58 (task 118R). The existing FieldDelta flow " +
            "on PlaybookExecutionEngine.ExecuteChatSummarizeAsync's stream surface is " +
            "untouched by 114a.");

        // The legacy NodeCompleted event must still be present (existing-consumer contract).
        events.Should().Contain(e => e.Type == PlaybookEventType.NodeCompleted);
    }

    /// <summary>
    /// ADR-015 tier-1 payload safety: serialize a <c>section_started</c> /
    /// <c>section_data</c> / <c>section_completed</c> event payload and verify it contains
    /// ONLY (a) section names (deterministic configuration identifiers), (b) admin-authored
    /// display labels, (c) section content text (which IS the response being sent — equivalent
    /// to a chat token), (d) optional structured-data JSON, (e) source node ID. It MUST NOT
    /// contain user message text, embedding vectors, internal diagnostic IDs, or any other
    /// tier-2+ content (which we assert by checking the serialized JSON shape against the
    /// known allowed property names).
    /// </summary>
    [Fact]
    public async Task DeliverComposite_SerializedSectionEventPayloads_AreAdr015Tier1Safe()
    {
        // Arrange
        var playbookId = Guid.NewGuid();
        var request = CreateRequest(playbookId);
        var node = CreateCompositeNode("Compose");

        _nodeServiceMock
            .Setup(x => x.GetNodesAsync(playbookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { node });

        SetupCompositeExecutor(BuildCompositePayload("summary"));

        // Act
        var events = new List<PlaybookStreamEvent>();
        await foreach (var evt in _service.ExecuteAsync(request, _mockHttpContext, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Assert — for each section event, serialize the structured payload and confirm
        // the allowed-property-name set.
        var serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        var allowedStartedProps = new HashSet<string>(StringComparer.Ordinal)
            { "sectionName", "displayLabel", "sectionIndex", "totalSections" };
        var allowedDataProps = new HashSet<string>(StringComparer.Ordinal)
            { "sectionName", "textDelta", "structuredData" };
        var allowedCompletedProps = new HashSet<string>(StringComparer.Ordinal)
            { "sectionName", "finalText", "finalStructuredData", "sourceNodeId" };

        foreach (var evt in events.Where(e => e.Type == PlaybookEventType.SectionStarted))
        {
            var json = JsonSerializer.Serialize(evt.SectionPayload!.Started!, serializerOptions);
            var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                allowedStartedProps.Should().Contain(prop.Name,
                    "ADR-015 tier-1: section_started payload contains only allowed configuration + position metadata");
            }
        }

        foreach (var evt in events.Where(e => e.Type == PlaybookEventType.SectionData))
        {
            var json = JsonSerializer.Serialize(evt.SectionPayload!.Data!, serializerOptions);
            var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                allowedDataProps.Should().Contain(prop.Name,
                    "ADR-015 tier-1: section_data payload contains only allowed section content + structured data");
            }
        }

        foreach (var evt in events.Where(e => e.Type == PlaybookEventType.SectionCompleted))
        {
            var json = JsonSerializer.Serialize(evt.SectionPayload!.Completed!, serializerOptions);
            var doc = JsonDocument.Parse(json);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                allowedCompletedProps.Should().Contain(prop.Name,
                    "ADR-015 tier-1: section_completed payload contains only allowed final state + deterministic source node ID");
            }
        }
    }

    /// <summary>
    /// Verifies the <see cref="ChatSseEventFactory"/> factory methods produce correctly
    /// typed <see cref="Api.Ai.ChatSseEvent"/> envelopes for the three section events.
    /// </summary>
    [Fact]
    public void ChatSseEventFactory_CreateSectionStartedEvent_ProducesCorrectEventType()
    {
        var data = new SectionStartedSseEventData(
            SectionName: "summary",
            DisplayLabel: "Summary",
            SectionIndex: 0,
            TotalSections: 3);

        var sseEvent = ChatSseEventFactory.CreateSectionStartedEvent(data);

        sseEvent.Type.Should().Be("section_started");
        sseEvent.Data.Should().Be(data);
    }

    [Fact]
    public void ChatSseEventFactory_CreateSectionDataEvent_ProducesCorrectEventType()
    {
        var data = new SectionDataSseEventData(
            SectionName: "summary",
            TextDelta: "Hello world",
            StructuredData: null);

        var sseEvent = ChatSseEventFactory.CreateSectionDataEvent(data);

        sseEvent.Type.Should().Be("section_data");
        sseEvent.Data.Should().Be(data);
    }

    [Fact]
    public void ChatSseEventFactory_CreateSectionCompletedEvent_ProducesCorrectEventType()
    {
        var data = new SectionCompletedSseEventData(
            SectionName: "summary",
            FinalText: "Hello world",
            FinalStructuredData: null,
            SourceNodeId: Guid.NewGuid());

        var sseEvent = ChatSseEventFactory.CreateSectionCompletedEvent(data);

        sseEvent.Type.Should().Be("section_completed");
        sseEvent.Data.Should().Be(data);
    }
}
