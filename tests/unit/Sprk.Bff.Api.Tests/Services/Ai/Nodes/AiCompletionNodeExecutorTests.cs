// R7 spaarke-ai-platform-unification-r7 — AiCompletionNodeExecutor xUnit tests.
//
// Wave 1 test coverage for FR-12 / FR-13 / FR-14, split across three parallel tasks:
//   - Task 007 (Region 007): payload binding + schema rendering + template substitution (this file initial create)
//   - Task 008 (Region 008): temperature override + per-node prompt override (appended in parallel)
//   - Task 009 (Region 009): error paths — missing prompt, malformed JSON, LLM error (appended in parallel)
//
// File structure uses #region blocks scoped per-task to enable parallel-safe Edit-tool appends
// without merge conflicts. Shared helper methods live at the bottom under #region Shared Helpers
// (created by task 007 — the first task to commit; tasks 008/009 reuse them).
//
// Pattern source: EntityNameValidatorNodeExecutorTests + AiAnalysisNodeExecutorTests (siblings).
//   - AAA + Moq + FluentAssertions per tests/CLAUDE.md.
//   - ADR-038 compliance: NO Mock<HttpMessageHandler>; NO DI-registration tests; NO ctor null-check tests.
//   - Mock at executor boundary: IOpenAiClient (LLM call) only. PromptSchemaRenderer is real (pure logic).
//
// References:
//   - tasks 007/008/009 POMLs in projects/spaarke-ai-platform-unification-r7/tasks/
//   - spec FR-12, FR-13, FR-14; ADR-038 Testing Strategy.

using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for <see cref="AiCompletionNodeExecutor"/> (R7 FR-12 / FR-13 / FR-14).
/// </summary>
/// <remarks>
/// Mock surface (ADR-038 compliant):
/// <list type="bullet">
///   <item><c>IOpenAiClient</c> — mocked at executor boundary (returns canned raw JSON).</item>
///   <item><c>PromptSchemaRenderer</c> — real instance (pure function service per its docs).</item>
///   <item><c>ILogger&lt;AiCompletionNodeExecutor&gt;</c> — <c>Mock&lt;ILogger&lt;T&gt;&gt;</c> for log-verification tests.</item>
/// </list>
/// </remarks>
public class AiCompletionNodeExecutorTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly PromptSchemaRenderer _promptSchemaRenderer;
    private readonly Mock<ILogger<AiCompletionNodeExecutor>> _loggerMock;
    private readonly AiCompletionNodeExecutor _sut;

    public AiCompletionNodeExecutorTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>(MockBehavior.Strict);
        _promptSchemaRenderer = new PromptSchemaRenderer(NullLogger<PromptSchemaRenderer>.Instance);
        _loggerMock = new Mock<ILogger<AiCompletionNodeExecutor>>();
        _sut = new AiCompletionNodeExecutor(
            _openAiClientMock.Object,
            _promptSchemaRenderer,
            _loggerMock.Object);
    }

    #region Task 007 — Payload binding + schema rendering + template substitution

    [Fact]
    public void SupportedActionTypes_ContainsAiCompletion()
    {
        // Assert — declared SupportedActionTypes binds NodeExecutorRegistry dispatch.
        _sut.SupportedActionTypes.Should().Contain(ActionType.AiCompletion);
        _sut.SupportedActionTypes.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExecuteAsync_ReadsActionSystemPrompt_PassesToOpenAiClient()
    {
        // Arrange — SystemPrompt is flat text (no JPS / no template params) so the
        // PromptSchemaRenderer returns it verbatim. The asserted invariant is:
        // whatever string the Action carries on SystemPrompt is exactly what the LLM receives.
        const string expectedPrompt = "You are a narrator. Produce a single sentence summary.";
        string? capturedPrompt = null;
        SetupOpenAiClient(rawJson: """{"summary":"ok"}""", capturePrompt: p => capturedPrompt = p);

        var ctx = CreateValidContext(systemPrompt: expectedPrompt);

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue("flat-text prompt with valid schema should produce Ok");
        capturedPrompt.Should().Be(expectedPrompt,
            "the executor must pass Action.SystemPrompt through to the LLM unchanged when no JPS / template params present");
    }

    [Fact]
    public async Task ExecuteAsync_ReadsActionOutputSchema_PassesToOpenAiClient()
    {
        // Arrange — assert the OutputSchemaJson from the Action is the BinaryData payload
        // sent to GetStructuredCompletionRawAsync (constrained-decoding contract).
        const string expectedSchemaJson =
            """{"type":"object","properties":{"summary":{"type":"string"}},"required":["summary"]}""";
        string? capturedSchema = null;
        SetupOpenAiClient(rawJson: """{"summary":"ok"}""", captureSchema: s => capturedSchema = s);

        var ctx = CreateValidContext(outputSchemaJson: expectedSchemaJson);

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedSchema.Should().Be(expectedSchemaJson,
            "the executor must pass Action.OutputSchemaJson through BinaryData.FromString verbatim");
    }

    [Fact]
    public async Task ExecuteAsync_RendersFullPrompt_WhenNoNodeOverrides()
    {
        // Arrange — ConfigJson null (no promptSchemaOverride, no templateParameters) and
        // SystemPrompt is flat text → PromptSchemaRenderer returns rawPrompt as-is.
        // This is the happy-path "no overrides" baseline that proves the pipeline does NOT
        // mutate the prompt when there is nothing to merge.
        const string flatPrompt = "Summarize the following matter in 25 words.";
        string? capturedPrompt = null;
        SetupOpenAiClient(rawJson: """{"summary":"ok"}""", capturePrompt: p => capturedPrompt = p);

        var ctx = CreateValidContext(systemPrompt: flatPrompt, configJson: null);

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedPrompt.Should().Be(flatPrompt,
            "with no ConfigJson overrides, the renderer must pass the flat-text Action.SystemPrompt through unchanged");
    }

    [Fact]
    public async Task ExecuteAsync_SubstitutesTemplateVariables_FromConfigJsonTemplateParameters()
    {
        // Arrange — the executor reads {{key}} bindings from ConfigJson.templateParameters
        // (NOT from PreviousOutputs — the consumer that populates ConfigJson is responsible for
        // resolving prior outputs into the templateParameters dict before invoking). Mirrors
        // AiAnalysisNodeExecutor.ExtractTemplateParameters behavior.
        //
        // We use a JPS-format prompt so the renderer's {{key}} substitution path activates
        // (PromptSchemaRenderer applies {{key}} only to JPS Instruction fields).
        const string jpsPrompt = """
        {
          "$schema": "https://spaarke.com/schemas/jps/v1",
          "instruction": {
            "role": "You are a legal narrator.",
            "task": "Summarize the matter {{matterName}} in 25 words."
          },
          "output": {
            "format": "json",
            "fields": []
          }
        }
        """;
        const string configJson = """{"templateParameters":{"matterName":"ACME Corp"}}""";

        string? capturedPrompt = null;
        SetupOpenAiClient(rawJson: """{"summary":"ok"}""", capturePrompt: p => capturedPrompt = p);

        var ctx = CreateValidContext(systemPrompt: jpsPrompt, configJson: configJson);

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedPrompt.Should().NotBeNull();
        capturedPrompt!.Should().Contain("ACME Corp",
            "the executor must substitute {{matterName}} with ConfigJson.templateParameters.matterName");
        capturedPrompt.Should().NotContain("{{matterName}}",
            "the unsubstituted placeholder must not survive into the LLM prompt");
    }

    [Fact]
    public async Task ExecuteAsync_BindsJsonElementResponse_ToOutputVariable()
    {
        // Arrange — the LLM returns a structured JSON object; the executor must parse it
        // and bind the cloned JsonElement to NodeOutput.StructuredData + raw text to
        // NodeOutput.TextContent + NodeOutput.OutputVariable to context.Node.OutputVariable.
        const string rawJson = """{"summary":"Matter resolved","wordCount":3}""";
        SetupOpenAiClient(rawJson: rawJson);

        var ctx = CreateValidContext(outputVariable: "narrativeResult");

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.NodeId.Should().Be(ctx.Node.Id);
        result.OutputVariable.Should().Be("narrativeResult",
            "NodeOutput.OutputVariable must match the node's OutputVariable for downstream {{var}} lookup");
        result.TextContent.Should().Be(rawJson,
            "TextContent must carry the raw JSON for callers that prefer string handling");
        result.StructuredData.Should().NotBeNull();
        result.StructuredData!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        result.StructuredData.Value.GetProperty("summary").GetString().Should().Be("Matter resolved");
        result.StructuredData.Value.GetProperty("wordCount").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ExecuteAsync_EmitsTelemetry_WithActionTypeTag()
    {
        // Arrange — verify the activity emitted by AiTelemetry.ActivitySource carries
        // the action_type tag = (int)ActionType.AiCompletion so OTEL backends can
        // group all completion calls regardless of node ID.
        SetupOpenAiClient(rawJson: """{"summary":"ok"}""");

        // Force AiTelemetry static initialization BEFORE registering the listener.
        // Reason: ActivityListener.ShouldListenTo is invoked synchronously inside
        // AiTelemetry's .cctor when constructing the static ActivitySource — at that
        // moment AiTelemetry.ActivitySource is still null (the field assignment hasn't
        // completed). Reading the source name first (which triggers .cctor to completion)
        // and capturing it to a local string avoids the NRE in the ShouldListenTo lambda.
        var aiSourceName = AiTelemetry.ActivitySource.Name;

        Activity? capturedActivity = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == aiSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => { if (a.OperationName == "ai.completion.node_execute") capturedActivity = a; }
        };
        ActivitySource.AddActivityListener(listener);

        var ctx = CreateValidContext();

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedActivity.Should().NotBeNull("AiTelemetry activity must start for every node execution");
        var actionTypeTag = capturedActivity!.GetTagItem("action_type");
        actionTypeTag.Should().Be((int)ActionType.AiCompletion,
            "telemetry tag action_type must equal (int)ActionType.AiCompletion for OTEL grouping");
    }

    [Fact]
    public async Task ExecuteAsync_AppliesTemplateParameters_AcrossMultipleInstructionFields()
    {
        // Arrange — {{key}} substitution must apply across Role + Task + Context + Constraints
        // (PromptSchemaRenderer applies the substitution to all instruction fields). Validates
        // that the executor's ExtractTemplateParameters → Renderer chain is wired correctly
        // for non-trivial JPS prompts.
        const string jpsPrompt = """
        {
          "$schema": "https://spaarke.com/schemas/jps/v1",
          "instruction": {
            "role": "You are a {{persona}}.",
            "task": "Process {{itemName}}.",
            "constraints": ["Keep output to {{maxWords}} words."]
          },
          "output": {
            "format": "json",
            "fields": []
          }
        }
        """;
        const string configJson = """
        {
          "templateParameters": {
            "persona": "legal narrator",
            "itemName": "matter X-101",
            "maxWords": "25"
          }
        }
        """;

        string? capturedPrompt = null;
        SetupOpenAiClient(rawJson: """{"summary":"ok"}""", capturePrompt: p => capturedPrompt = p);

        var ctx = CreateValidContext(systemPrompt: jpsPrompt, configJson: configJson);

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        capturedPrompt.Should().NotBeNull();
        capturedPrompt!.Should().Contain("legal narrator", "Role-section substitution must apply");
        capturedPrompt.Should().Contain("matter X-101", "Task-section substitution must apply");
        capturedPrompt.Should().Contain("25 words", "Constraints-section substitution must apply");
        capturedPrompt.Should().NotContain("{{", "no unresolved placeholders may survive rendering");
    }

    #endregion

    #region Shared Helpers (created by task 007; shared with tasks 008 + 009)

    /// <summary>
    /// Configures the strict-mock IOpenAiClient to return the given raw JSON string on
    /// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/> and optionally capture
    /// the prompt and/or schema arguments for assertion.
    /// </summary>
    private void SetupOpenAiClient(
        string rawJson,
        Action<string>? capturePrompt = null,
        Action<string>? captureSchema = null)
    {
        _openAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (prompt, schema, _, _, _, _, _) =>
                {
                    capturePrompt?.Invoke(prompt);
                    captureSchema?.Invoke(schema.ToString());
                })
            .ReturnsAsync(rawJson);
    }

    /// <summary>
    /// Constructs a NodeExecutionContext valid for AiCompletion: Action FK + SystemPrompt
    /// + OutputSchemaJson all populated; Tool + Document both absent per FR-13.
    /// Override individual fields via named parameters for targeted tests.
    /// </summary>
    private static NodeExecutionContext CreateValidContext(
        string? systemPrompt = "You are a test assistant.",
        string? outputSchemaJson = """{"type":"object","properties":{"summary":{"type":"string"}}}""",
        string? configJson = null,
        string outputVariable = "completionResult",
        decimal? temperature = null)
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Test AiCompletion Node",
                ExecutionOrder = 1,
                OutputVariable = outputVariable,
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Test AiCompletion Action",
                SystemPrompt = systemPrompt ?? string.Empty,
                OutputSchemaJson = outputSchemaJson,
                Temperature = temperature
            },
            ActionType = ActionType.AiCompletion,
            Scopes = new ResolvedScopes([], [], []),
            Document = null,
            TenantId = "test-tenant",
            CorrelationId = "corr-" + Guid.NewGuid().ToString("N")
        };
    }

    #endregion

    #region Task 009 — Error path tests (FR-13 Validate contract + FR-14 LLM-failure paths)

    // Scope per task 009 POML: Validate() error contract + LLM-call failure mapping. Coverage target NFR-05.
    // Mock surface stays minimal per ADR-038 (no Mock<HttpMessageHandler>, no DI-registration tests,
    // no ctor null-check tests). Validation-failure tests MUST NOT reach the LLM — they short-circuit
    // in Validate() before ExecuteAsync invokes IOpenAiClient. Strict-mock _openAiClientMock from the
    // class fixture enforces this: any unconfigured GetStructuredCompletionRawAsync call would throw.

    [Fact]
    public void Validate_Fails_WhenActionFkMissing()
    {
        // Arrange — Node.ActionId == Guid.Empty triggers the FR-13 "actionMissing" path in
        // Validate(). Per AiCompletionNodeExecutor lines 149-154 the literal error message is
        // surfaced verbatim by the Playbook Builder UI (Wave 8), so this test asserts the
        // exact contract string from task 005.
        var ctx = CreateValidContext();
        ctx = ctx with
        {
            Node = ctx.Node with { ActionId = Guid.Empty }
        };

        // Act
        var result = _sut.Validate(ctx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("requires an Action FK"),
            "FR-13 Validate must reject AiCompletion node with no Action FK using the literal contract message");
    }

    [Fact]
    public void Validate_Fails_WhenToolPresent()
    {
        // Arrange — Tool presence triggers the FR-13 "PROHIBITS Tool" inversion vs AiAnalysis.
        // NodeExecutionContext.Tool is derived from Scopes.Tools.FirstOrDefault(), so populating
        // the Tools array activates the prohibition.
        var ctx = CreateValidContext();
        ctx = ctx with
        {
            Scopes = new ResolvedScopes([], [],
            [
                new AnalysisTool
                {
                    Id = Guid.NewGuid(),
                    Name = "MisplacedTool",
                    Type = ToolType.EntityExtractor,
                    HandlerClass = "EntityNameValidator"
                }
            ])
        };

        // Act
        var result = _sut.Validate(ctx);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MUST NOT have a Tool"),
            "FR-13 Validate must reject AiCompletion node with a Tool using the literal contract message");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenLlmReturnsMalformedJson()
    {
        // Arrange — LLM returns a string that is NOT valid JSON. JsonDocument.Parse will throw
        // JsonException; the executor's catch block maps it to NodeErrorCodes.InternalError with
        // the contract message "AI completion returned malformed JSON" (AiCompletionNodeExecutor
        // lines 354-373). Validates the malformed-JSON path in FR-14.
        SetupOpenAiClient(rawJson: "not-valid-json-at-all {");
        var ctx = CreateValidContext();

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InternalError,
            "malformed-JSON branch maps to InternalError per AiCompletionNodeExecutor catch (JsonException) handler");
        result.ErrorMessage.Should().Contain("malformed JSON",
            "the literal contract message surfaced by the executor must mention 'malformed JSON'");
        result.NodeId.Should().Be(ctx.Node.Id);
        result.OutputVariable.Should().Be(ctx.Node.OutputVariable);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsError_WhenLlmThrowsHttpException()
    {
        // Arrange — IOpenAiClient throws HttpRequestException. The executor's generic
        // catch (Exception ex) block (lines 416-437) wraps it as InternalError with the
        // original message visible inside "AiCompletion execution failed: {ex.Message}".
        // Validates the LLM-error path in FR-14.
        const string llmErrorMessage = "Azure OpenAI HTTP 500: simulated upstream failure";
        _openAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(llmErrorMessage));

        var ctx = CreateValidContext();

        // Act
        var result = await _sut.ExecuteAsync(ctx, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InternalError,
            "LLM-call failures (HttpRequestException) map to InternalError per executor catch (Exception) handler");
        result.ErrorMessage.Should().Contain(llmErrorMessage,
            "the original exception message must be surfaced for actionable diagnostics");
        result.ErrorMessage.Should().Contain("AiCompletion execution failed",
            "wrap-prefix preserves the executor identity for log/UI consumers");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsCancelledError_WhenTokenCancelled()
    {
        // Arrange — Pre-cancelled CancellationToken. IOpenAiClient throws OperationCanceledException
        // (matches Azure OpenAI SDK behavior under cancellation). The executor's
        // catch (OperationCanceledException) block (lines 400-414) maps it to NodeErrorCodes.Cancelled.
        // Mirrors AiAnalysisNodeExecutor.ExecuteAsync_WhenCancelled_ReturnsCancelledOutput pattern
        // and validates FR-14 cancellation propagation.
        _openAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ctx = CreateValidContext();

        // Act
        var result = await _sut.ExecuteAsync(ctx, cts.Token);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.Cancelled,
            "OperationCanceledException must map to NodeErrorCodes.Cancelled per AiAnalysisNodeExecutor sibling contract");
        result.ErrorMessage.Should().Contain("cancelled",
            "the literal contract message 'Node execution was cancelled' surfaces the cancellation path");
    }

    #endregion
}
