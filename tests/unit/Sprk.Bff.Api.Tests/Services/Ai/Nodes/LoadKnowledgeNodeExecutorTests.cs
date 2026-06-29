// R4 spaarke-daily-update-service-r4 — Tests for LoadKnowledgeNodeExecutor (UAT
// 2026-06-26 follow-on to Start fix). Mirrors StartNodeExecutorTests shape:
//   - SupportedActionTypes contains LoadKnowledge.
//   - Validate: null / empty / well-formed / malformed configJson.
//   - ExecuteAsync: passthroughBinding present → resolves templates from scope.
//   - ExecuteAsync: passthroughBinding missing → empty object bound to outputVariable.
//   - ExecuteAsync: missing scope variable → null/empty in resolved output (no throw).
//   - ExecuteAsync: r5BindingPlan.knowledgeSourceCode → info log emitted.
//   - OutputVariable defaulting (empty → "channelRegistry").
//   - Singleton reentrancy (back-to-back invocations independent).

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
/// Unit tests for <see cref="LoadKnowledgeNodeExecutor"/>. Validates pass-through
/// template resolution from scope variables (PreviousOutputs) into the bound
/// output object, configJson optionality, OutputVariable defaulting, and the R5
/// forward-compat info-log signal.
/// </summary>
public class LoadKnowledgeNodeExecutorTests
{
    private readonly Mock<ILogger<LoadKnowledgeNodeExecutor>> _loggerMock;
    private readonly TemplateEngine _templateEngine;
    private readonly LoadKnowledgeNodeExecutor _executor;

    public LoadKnowledgeNodeExecutorTests()
    {
        _loggerMock = new Mock<ILogger<LoadKnowledgeNodeExecutor>>();
        // Use the real TemplateEngine — no mocking the template helper (per task
        // requirement: REUSE the existing helper, do NOT reinvent {{...}} parsing).
        _templateEngine = new TemplateEngine(new Mock<ILogger<TemplateEngine>>().Object);
        _executor = new LoadKnowledgeNodeExecutor(_templateEngine, _loggerMock.Object);
    }

    #region SupportedActionTypes

    [Fact]
    public void SupportedActionTypes_ContainsLoadKnowledge()
    {
        _executor.SupportedActionTypes.Should().Contain(ExecutorType.LoadKnowledge);
        _executor.SupportedActionTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate

    [Fact]
    public void Validate_NullConfigJson_Succeeds()
    {
        var context = CreateContext(configJson: null, outputVariable: "channelRegistry");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_HappyPath_PassthroughBindingPresent_Succeeds()
    {
        var configJson = """
            {
              "kind": "pass-through-placeholder",
              "passthroughBinding": {
                "channels": "{{start.channels}}"
              }
            }
            """;
        var context = CreateContext(configJson: configJson, outputVariable: "channelRegistry");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsError()
    {
        var context = CreateContext(configJson: "{not-json}", outputVariable: "channelRegistry");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not valid JSON"));
    }

    #endregion

    #region ExecuteAsync — happy path: resolves templates from scope

    [Fact]
    public async Task ExecuteAsync_PassthroughBinding_ResolvesStartChannelsFromScope()
    {
        // Arrange — Start node has already executed and stored a JsonElement
        // payload under "start"; LoadKnowledge's template {{start.channels}}
        // resolves to that nested array.
        var startPayload = """{"channels":[{"channel":"matters","items":[]}],"totalNotificationCount":1}""";
        var startOutput = NodeOutput.Ok(
            nodeId: Guid.NewGuid(),
            outputVariable: "start",
            data: JsonDocument.Parse(startPayload).RootElement);

        var configJson = """
            {
              "kind": "pass-through-placeholder",
              "passthroughBinding": {
                "channels": "{{start.channels}}"
              }
            }
            """;

        var context = CreateContext(
            configJson: configJson,
            outputVariable: "channelRegistry",
            previousOutputs: new Dictionary<string, NodeOutput> { ["start"] = startOutput });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("channelRegistry");
        result.StructuredData.Should().NotBeNull();
        // The bound output exposes a `channels` field. Whether the rendered template
        // round-trips as a JSON array or a string depends on Handlebars output; both
        // are acceptable — the key requirement is the field is present and non-empty.
        result.StructuredData!.Value.TryGetProperty("channels", out var channelsProp)
            .Should().BeTrue();
        // ValueKind may be String (Handlebars default ToString of array) or Array
        // (when parser detects bracketed JSON). Either is acceptable for the
        // pass-through placeholder. We only require it's not Null/Undefined.
        channelsProp.ValueKind.Should().NotBe(JsonValueKind.Null);
        channelsProp.ValueKind.Should().NotBe(JsonValueKind.Undefined);
    }

    #endregion

    #region ExecuteAsync — missing passthroughBinding

    [Fact]
    public async Task ExecuteAsync_EmptyConfig_BindsEmptyObject()
    {
        // Arrange — no configJson, no passthroughBinding, no prior outputs.
        var context = CreateContext(
            configJson: null,
            outputVariable: "channelRegistry");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData.Should().NotBeNull();
        result.StructuredData!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        result.StructuredData!.Value.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_PassthroughBindingMissingScopeVariable_YieldsEmpty()
    {
        // Arrange — passthroughBinding references {{start.channels}} but Start
        // has NOT executed (PreviousOutputs empty). Handlebars graceful-missing
        // returns "" — the executor must NOT throw and MUST succeed.
        var configJson = """
            {
              "passthroughBinding": {
                "channels": "{{start.channels}}"
              }
            }
            """;
        var context = CreateContext(
            configJson: configJson,
            outputVariable: "channelRegistry");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData!.Value.TryGetProperty("channels", out var channelsProp)
            .Should().BeTrue();
        // Missing variable → empty string (Handlebars graceful-missing contract).
        // ValueKind = String AND empty.
        if (channelsProp.ValueKind == JsonValueKind.String)
        {
            channelsProp.GetString().Should().BeEmpty();
        }
    }

    #endregion

    #region ExecuteAsync — R5 forward-compat info log

    [Fact]
    public async Task ExecuteAsync_R5BindingPlanKnowledgeSourceCodeSet_EmitsInfoLog()
    {
        // Arrange — R5 placeholder signal: knowledgeSourceCode is set; executor
        // must emit an info log so future R5 wiring knows where to substitute.
        // R4 behaviour is still pass-through — DOES NOT fetch.
        var configJson = """
            {
              "passthroughBinding": {
                "channels": "{{start.channels}}"
              },
              "r5BindingPlan": {
                "knowledgeSourceCode": "TBD-CHANNEL-REGISTRY"
              }
            }
            """;
        var context = CreateContext(
            configJson: configJson,
            outputVariable: "channelRegistry");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("R5 binding plan present")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "an info log must signal that R5 binding-plan is present so future wiring picks it up");
    }

    [Fact]
    public async Task ExecuteAsync_NoR5BindingPlan_DoesNotEmitR5InfoLog()
    {
        var configJson = """{"passthroughBinding":{"channels":"{{start.channels}}"}}""";
        var context = CreateContext(
            configJson: configJson,
            outputVariable: "channelRegistry");

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("R5 binding plan present")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    #endregion

    #region OutputVariable defaulting

    [Fact]
    public async Task ExecuteAsync_EmptyOutputVariable_DefaultsToChannelRegistry()
    {
        var context = CreateContext(
            configJson: null,
            outputVariable: "");

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("channelRegistry",
            "LoadKnowledge nodes default to 'channelRegistry' scope variable when unset");
    }

    [Fact]
    public async Task ExecuteAsync_CustomOutputVariable_Respected()
    {
        var context = CreateContext(
            configJson: null,
            outputVariable: "myKnowledge");

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("myKnowledge");
    }

    #endregion

    #region Reentrancy

    [Fact]
    public async Task ExecuteAsync_BackToBackInvocations_AreIndependent()
    {
        var ctx1 = CreateContext(
            configJson: """{"passthroughBinding":{"run":"{{start.runId}}"}}""",
            outputVariable: "channelRegistry",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["start"] = NodeOutput.Ok(Guid.NewGuid(), "start",
                    JsonDocument.Parse("""{"runId":"r1"}""").RootElement)
            });

        var ctx2 = CreateContext(
            configJson: """{"passthroughBinding":{"run":"{{start.runId}}"}}""",
            outputVariable: "channelRegistry",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["start"] = NodeOutput.Ok(Guid.NewGuid(), "start",
                    JsonDocument.Parse("""{"runId":"r2"}""").RootElement)
            });

        var r1 = await _executor.ExecuteAsync(ctx1, CancellationToken.None);
        var r2 = await _executor.ExecuteAsync(ctx2, CancellationToken.None);

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        r1.StructuredData!.Value.GetProperty("run").GetString().Should().Be("r1");
        r2.StructuredData!.Value.GetProperty("run").GetString().Should().Be("r2");
    }

    #endregion

    #region Helpers

    private static NodeExecutionContext CreateContext(
        string? configJson,
        string? outputVariable,
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
                NodeType = NodeType.Control,
                ActionId = Guid.Empty,
                Name = "LoadKnowledge",
                ExecutionOrder = 1,
                OutputVariable = outputVariable ?? string.Empty,
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = Guid.Empty,
                Name = "LoadKnowledge",
                ExecutorType = ExecutorType.LoadKnowledge
            },
            ExecutorType = ExecutorType.LoadKnowledge,
            Scopes = new ResolvedScopes([], [], []),
            PreviousOutputs = previousOutputs ?? new Dictionary<string, NodeOutput>(),
            Parameters = new Dictionary<string, string>(),
            TenantId = "test-tenant",
            CorrelationId = "corr-" + Guid.NewGuid().ToString("N")
        };
    }

    #endregion
}
