using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="DeliverCompositeNodeExecutor"/> (FR-52 / Phase 5R Wave 5-C task 114R).
/// Validates the section-name-keyed composition pattern that replaces the legacy
/// 5-coordination-point schema-on-action + schema-aware widget model.
/// </summary>
/// <remarks>
/// Backward-compat invariant: existing single-action <see cref="NodeType.Output"/> nodes are
/// dispatched to <see cref="DeliverOutputNodeExecutor"/> via
/// <see cref="ExecutorType.DeliverOutput"/> — UNCHANGED. The
/// <see cref="DeliverOutputNodeExecutorTests"/> test class covers that path; this test class
/// covers only the NEW composite path. The invariant test
/// <see cref="ExistingSingleActionOutputNode_BackwardCompat_DeliverCompositeExecutorDoesNotHandleDeliverOutput"/>
/// asserts that this executor refuses to handle the legacy ExecutorType.
/// </remarks>
public class DeliverCompositeNodeExecutorTests
{
    private readonly Mock<ILogger<DeliverCompositeNodeExecutor>> _loggerMock;
    private readonly DeliverCompositeNodeExecutor _executor;

    public DeliverCompositeNodeExecutorTests()
    {
        _loggerMock = new Mock<ILogger<DeliverCompositeNodeExecutor>>();
        _executor = new DeliverCompositeNodeExecutor(_loggerMock.Object);
    }

    #region SupportedActionTypes

    [Fact]
    public void SupportedActionTypes_ContainsOnlyDeliverComposite()
    {
        _executor.SupportedActionTypes.Should().ContainSingle();
        _executor.SupportedActionTypes.Should().Contain(ExecutorType.DeliverComposite);
    }

    /// <summary>
    /// Backward-compat invariant (FR-52): the composite executor MUST NOT register for
    /// <see cref="ExecutorType.DeliverOutput"/> — that path stays owned by
    /// <see cref="DeliverOutputNodeExecutor"/>. If this assertion ever fires, the asymmetric
    /// dispatch in <c>PlaybookOrchestrationService</c> (NodeType.Output → DeliverOutput vs
    /// NodeType.DeliverComposite → DeliverComposite) would silently collide.
    /// </summary>
    [Fact]
    public void ExistingSingleActionOutputNode_BackwardCompat_DeliverCompositeExecutorDoesNotHandleDeliverOutput()
    {
        _executor.SupportedActionTypes.Should().NotContain(ExecutorType.DeliverOutput,
            "FR-52 backward-compat invariant — legacy single-action Output Node dispatch is " +
            "owned by DeliverOutputNodeExecutor; composite executor MUST NOT collide");
    }

    #endregion

    #region Validate

    [Fact]
    public void Validate_NullConfig_ReturnsSuccess_PermitsEmptyComposite()
    {
        // FR-52: an empty composite is a valid no-op. Consumer decides hard vs soft.
        var context = CreateContext(configJson: null);
        var result = _executor.Validate(context);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyConfig_ReturnsSuccess_PermitsEmptyComposite()
    {
        var context = CreateContext(configJson: "");
        var result = _executor.Validate(context);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MalformedJsonConfig_ReturnsSuccess_TreatedAsAbsent()
    {
        // Parser is lenient — malformed JSON is treated as "no config", same as null.
        // The orchestration layer is responsible for flagging hard config errors upstream.
        var context = CreateContext(configJson: "{not json");
        var result = _executor.Validate(context);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MissingSectionName_ReturnsFailure()
    {
        var context = CreateContext(configJson: """
            {"sections":[{"inputVariable":"foo"}],"destination":"workspace"}
            """);
        var result = _executor.Validate(context);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("sectionName is required"));
    }

    [Fact]
    public void Validate_DuplicateSectionName_ReturnsFailure()
    {
        var context = CreateContext(configJson: """
            {"sections":[
                {"sectionName":"summary","inputVariable":"a"},
                {"sectionName":"summary","inputVariable":"b"}
            ],"destination":"workspace"}
            """);
        var result = _executor.Validate(context);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("duplicated"));
    }

    [Fact]
    public void Validate_MissingInputVariable_ReturnsFailure()
    {
        var context = CreateContext(configJson: """
            {"sections":[{"sectionName":"summary"}],"destination":"chat"}
            """);
        var result = _executor.Validate(context);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("inputVariable is required"));
    }

    [Fact]
    public void Validate_UnknownDestination_ReturnsFailure()
    {
        var context = CreateContext(configJson: """
            {"sections":[{"sectionName":"a","inputVariable":"v"}],"destination":"unknown"}
            """);
        var result = _executor.Validate(context);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("destination") && e.Contains("not valid"));
    }

    [Theory]
    [InlineData("workspace")]
    [InlineData("chat")]
    [InlineData("formPrefill")]
    [InlineData("form-prefill")]
    [InlineData("form_prefill")]
    public void Validate_KnownDestinations_ReturnsSuccess(string destination)
    {
        var context = CreateContext(configJson: $$"""
            {"sections":[{"sectionName":"a","inputVariable":"v"}],"destination":"{{destination}}"}
            """);
        var result = _executor.Validate(context);
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync — composition

    [Fact]
    public async Task ExecuteAsync_ThreeUpstreamsThreeSections_ComposesAllThreeKeyedByName()
    {
        // FR-52 ACCEPTANCE: 3 Action upstreams → 3 sections keyed by section name.
        var summaryOutput = CreateAiOutput("summarize.output", textContent: "Document summary text.");
        var keyTermsOutput = CreateAiOutput("extract_terms",
            structuredData: """{"terms":["term1","term2"]}""");
        var actionItemsOutput = CreateAiOutput("extract_actions",
            structuredData: """{"items":[{"text":"Do X"}]}""");

        var context = CreateContext(
            configJson: """
                {
                  "sections": [
                    { "sectionName": "summary",     "inputVariable": "summarize.output", "displayLabel": "Summary" },
                    { "sectionName": "keyTerms",    "inputVariable": "extract_terms",    "displayLabel": "Key Terms" },
                    { "sectionName": "actionItems", "inputVariable": "extract_actions",  "displayLabel": "Action Items" }
                  ],
                  "destination": "workspace",
                  "widgetType": "structured-output-stream"
                }
                """,
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["summarize.output"] = summaryOutput,
                ["extract_terms"] = keyTermsOutput,
                ["extract_actions"] = actionItemsOutput
            });

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);

        output.Success.Should().BeTrue();
        output.IsDeliverOutput.Should().BeTrue("composite output is a delivery output for SSE/widget routing purposes");
        output.StructuredData.Should().NotBeNull();

        var payload = output.GetData<CompositeOutputPayload>();
        payload.Should().NotBeNull();
        payload!.Destination.Should().Be("workspace");
        payload.WidgetType.Should().Be("structured-output-stream");
        payload.Sections.Should().HaveCount(3);

        var sections = payload.Sections.ToDictionary(s => s.SectionName, s => s);
        sections.Should().ContainKey("summary");
        sections.Should().ContainKey("keyTerms");
        sections.Should().ContainKey("actionItems");
        sections["summary"].DisplayLabel.Should().Be("Summary");
        sections["summary"].TextContent.Should().Be("Document summary text.");
        sections["keyTerms"].DisplayLabel.Should().Be("Key Terms");
        sections["actionItems"].DisplayLabel.Should().Be("Action Items");
    }

    [Fact]
    public async Task ExecuteAsync_PreservesDeclarationOrder()
    {
        // Document the section-ordering contract: declaration order is preserved in the
        // composite payload. 114a may reorder by completion for streaming, but the in-process
        // composition follows author intent.
        var context = CreateContext(
            configJson: """
                {
                  "sections": [
                    { "sectionName": "c", "inputVariable": "c.out" },
                    { "sectionName": "a", "inputVariable": "a.out" },
                    { "sectionName": "b", "inputVariable": "b.out" }
                  ],
                  "destination": "workspace"
                }
                """,
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["a.out"] = CreateAiOutput("a.out", textContent: "A"),
                ["b.out"] = CreateAiOutput("b.out", textContent: "B"),
                ["c.out"] = CreateAiOutput("c.out", textContent: "C")
            });

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);
        var payload = output.GetData<CompositeOutputPayload>();
        payload!.Sections.Select(s => s.SectionName).Should().ContainInOrder("c", "a", "b");
    }

    [Fact]
    public async Task ExecuteAsync_DestinationChat_RoutesToChat()
    {
        var context = CreateContext(
            configJson: """
                {"sections":[{"sectionName":"a","inputVariable":"v"}],"destination":"chat"}
                """,
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["v"] = CreateAiOutput("v", textContent: "X")
            });

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);
        var payload = output.GetData<CompositeOutputPayload>();
        payload!.Destination.Should().Be("chat");
    }

    [Fact]
    public async Task ExecuteAsync_DestinationWorkspace_RoutesToWorkspaceWithWidgetType()
    {
        var context = CreateContext(
            configJson: """
                {
                  "sections":[{"sectionName":"a","inputVariable":"v"}],
                  "destination":"workspace",
                  "widgetType":"structured-output-stream"
                }
                """,
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["v"] = CreateAiOutput("v", textContent: "X")
            });

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);
        var payload = output.GetData<CompositeOutputPayload>();
        payload!.Destination.Should().Be("workspace");
        payload.WidgetType.Should().Be("structured-output-stream");
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsDestinationToWorkspaceWhenAbsent()
    {
        var context = CreateContext(
            configJson: """
                {"sections":[{"sectionName":"a","inputVariable":"v"}]}
                """,
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["v"] = CreateAiOutput("v", textContent: "X")
            });

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);
        var payload = output.GetData<CompositeOutputPayload>();
        payload!.Destination.Should().Be("workspace",
            "absent destination defaults to workspace per Phase 5R Wave 5-C anchor case");
    }

    #endregion

    #region ExecuteAsync — partial / empty / drop cases

    [Fact]
    public async Task ExecuteAsync_EmptyComposite_NoUpstreamsResolved_ReturnsEmptySectionsMapNoError()
    {
        // FR-52: empty composite is a valid no-op — not an error.
        var context = CreateContext(
            configJson: """
                {"sections":[],"destination":"workspace"}
                """);

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);
        output.Success.Should().BeTrue();

        var payload = output.GetData<CompositeOutputPayload>();
        payload!.Sections.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownInputVariable_SilentlyDropsSection()
    {
        // FR-52: unknown sectionName binding (input not found) → silently dropped + log warning,
        // partial composite is still valid.
        var context = CreateContext(
            configJson: """
                {
                  "sections":[
                    {"sectionName":"present","inputVariable":"foundVar"},
                    {"sectionName":"missing","inputVariable":"notFoundVar"}
                  ],
                  "destination":"workspace"
                }
                """,
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["foundVar"] = CreateAiOutput("foundVar", textContent: "FOUND")
            });

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);
        output.Success.Should().BeTrue("partial composite is valid per FR-52");

        var payload = output.GetData<CompositeOutputPayload>();
        payload!.Sections.Should().ContainSingle();
        payload.Sections[0].SectionName.Should().Be("present");
    }

    [Fact]
    public async Task ExecuteAsync_FailedUpstream_SilentlyDropsSection()
    {
        var failedOutput = NodeOutput.Error(
            Guid.NewGuid(), "failedVar", "upstream blew up", NodeErrorCodes.InternalError);

        var context = CreateContext(
            configJson: """
                {
                  "sections":[
                    {"sectionName":"healthy","inputVariable":"healthyVar"},
                    {"sectionName":"broken","inputVariable":"failedVar"}
                  ],
                  "destination":"workspace"
                }
                """,
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["healthyVar"] = CreateAiOutput("healthyVar", textContent: "OK"),
                ["failedVar"] = failedOutput
            });

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);
        output.Success.Should().BeTrue();

        var payload = output.GetData<CompositeOutputPayload>();
        payload!.Sections.Should().ContainSingle();
        payload.Sections[0].SectionName.Should().Be("healthy");
    }

    #endregion

    #region ExecuteAsync — error handling

    [Fact]
    public async Task ExecuteAsync_ValidationFailure_ReturnsErrorOutput()
    {
        var context = CreateContext(configJson: """
            {"sections":[{"sectionName":"a","inputVariable":""}],"destination":"workspace"}
            """);

        var output = await _executor.ExecuteAsync(context, CancellationToken.None);
        output.Success.Should().BeFalse();
        output.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        output.ErrorMessage.Should().Contain("inputVariable is required");
    }

    #endregion

    #region Test helpers

    private static NodeExecutionContext CreateContext(
        string? configJson = null,
        IReadOnlyDictionary<string, NodeOutput>? previousOutputs = null)
    {
        var nodeId = Guid.NewGuid();
        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = Guid.Empty, // composite is a structural node — no Action FK
                Name = "Composite Output",
                NodeType = NodeType.DeliverComposite,
                ExecutionOrder = 99,
                OutputVariable = "composite_result",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = Guid.Empty,
                Name = "Composite Output",
                ExecutorType = ExecutorType.DeliverComposite
            },
            ExecutorType = ExecutorType.DeliverComposite,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant",
            PreviousOutputs = previousOutputs ?? new Dictionary<string, NodeOutput>()
        };
    }

    private static NodeOutput CreateAiOutput(
        string outputVariable,
        string? textContent = null,
        string? structuredData = null)
    {
        JsonElement? jsonData = null;
        if (!string.IsNullOrEmpty(structuredData))
        {
            using var doc = JsonDocument.Parse(structuredData);
            jsonData = doc.RootElement.Clone();
        }

        return new NodeOutput
        {
            NodeId = Guid.NewGuid(),
            OutputVariable = outputVariable,
            Success = true,
            TextContent = textContent,
            StructuredData = jsonData,
            Metrics = NodeExecutionMetrics.Empty
        };
    }

    #endregion
}
