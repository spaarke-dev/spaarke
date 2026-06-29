// R4 spaarke-daily-update-service-r4 — Tests for StartNodeExecutor (UAT 2026-06-25
// fix). Covers the four mandatory scenarios from the task prompt plus reentrancy:
//   - Happy path: payload arrives in Parameters; bound to scope variable; success.
//   - Empty Parameters: scope variable bound to empty object; executor succeeds.
//   - InputContract + validateOnExecute: valid passes, invalid returns validation error.
//   - OutputVariable defaulting: when node.OutputVariable is empty, uses "start".
//   - Singleton-with-no-Scoped-deps reentrancy.

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
/// Unit tests for <see cref="StartNodeExecutor"/>. Validates payload binding
/// from the dispatch wrapper's Parameters dictionary into the scope variable,
/// optional input-contract validation, and OutputVariable defaulting.
/// </summary>
public class StartNodeExecutorTests
{
    private readonly Mock<ILogger<StartNodeExecutor>> _loggerMock;
    private readonly StartNodeExecutor _executor;

    public StartNodeExecutorTests()
    {
        _loggerMock = new Mock<ILogger<StartNodeExecutor>>();
        _executor = new StartNodeExecutor(_loggerMock.Object);
    }

    #region SupportedExecutorTypes

    [Fact]
    public void SupportedActionTypes_ContainsStart()
    {
        _executor.SupportedExecutorTypes.Should().Contain(ExecutorType.Start);
        _executor.SupportedExecutorTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate

    [Fact]
    public void Validate_NullConfigJson_Succeeds()
    {
        // ConfigJson is optional for Start nodes (the canonical canvas-only anchor case).
        var context = CreateContext(configJson: null, outputVariable: "start");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyConfigJson_Succeeds()
    {
        var context = CreateContext(configJson: "", outputVariable: "start");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WellFormedConfigJson_Succeeds()
    {
        // R4 deploy shape: configJson has scope + inputContract + description.
        var configJson = """
            {
              "scope": "user-briefing-payload",
              "inputContract": {
                "categories": "Array<...>",
                "channels": "Array<...>"
              }
            }
            """;
        var context = CreateContext(configJson: configJson, outputVariable: "start");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsError()
    {
        var context = CreateContext(configJson: "{not-json}", outputVariable: "start");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not valid JSON"));
    }

    #endregion

    #region ExecuteAsync — happy path: payload binding

    [Fact]
    public async Task ExecuteAsync_HappyPath_BindsBriefingPayloadIntoOutputVariable()
    {
        // Arrange — matches the R4 /narrate dispatch convention:
        // DailyBriefingEndpoints serializes the structured request into
        // parameters["briefingPayload"], and the Start node binds it as `start`.
        var payload = """{"categories":[{"category":"matters","count":3,"items":[]}],"channels":[{"channel":"matters","items":[]}],"priorityItems":[],"totalNotificationCount":3}""";

        var context = CreateContext(
            configJson: """{"scope":"user-briefing-payload"}""",
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = payload,
                ["totalNotificationCount"] = "3"
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("start");
        result.StructuredData.Should().NotBeNull();
        result.StructuredData!.Value.GetProperty("totalNotificationCount")
            .GetInt32().Should().Be(3);
        result.StructuredData!.Value.GetProperty("channels")
            .GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_ExplicitPayloadParameter_PrefersConfigOverDefault()
    {
        // Arrange — author can override the source key via configJson.payloadParameter.
        var payload = """{"foo":"bar"}""";

        var context = CreateContext(
            configJson: """{"payloadParameter":"myCustomKey"}""",
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = """{"shouldnt":"win"}""",
                ["myCustomKey"] = payload
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData!.Value.GetProperty("foo").GetString().Should().Be("bar");
    }

    #endregion

    #region ExecuteAsync — empty Parameters

    [Fact]
    public async Task ExecuteAsync_EmptyParameters_BindsEmptyObject()
    {
        // Arrange — no payload in Parameters at all. Executor must succeed and
        // bind an empty object (downstream {{start.x}} resolves to null without
        // throwing) per ai-architecture-playbook-runtime.md §7 empty-payload contract.
        var context = CreateContext(
            configJson: null,
            outputVariable: "start",
            parameters: new Dictionary<string, string>());

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("start");
        result.StructuredData.Should().NotBeNull();
        result.StructuredData!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        result.StructuredData!.Value.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_NonJsonPayload_WrapsAsRawField()
    {
        // Arrange — payload string is plain text, not JSON. Executor must NOT
        // fail the run; bind it as { raw: "<text>" } and emit a structured warning.
        var context = CreateContext(
            configJson: null,
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = "hello world"
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData!.Value.GetProperty("raw").GetString().Should().Be("hello world");
    }

    #endregion

    #region ExecuteAsync — input-contract validation (opt-in)

    [Fact]
    public async Task ExecuteAsync_InputContractValidateOnExecuteTrue_ValidPayload_Succeeds()
    {
        // Arrange
        var payload = """{"categories":[],"priorityItems":[],"channels":[],"totalNotificationCount":0}""";
        var configJson = """
            {
              "validateOnExecute": true,
              "inputContract": {
                "categories": "Array<...>",
                "priorityItems": "...",
                "channels": "Array<...>",
                "totalNotificationCount": "number"
              }
            }
            """;
        var context = CreateContext(
            configJson: configJson,
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = payload
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData!.Value.GetProperty("totalNotificationCount").GetInt32()
            .Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_InputContractValidateOnExecuteTrue_MissingKey_ReturnsValidationError()
    {
        // Arrange — payload missing the `channels` key the contract requires.
        var payload = """{"categories":[],"priorityItems":[],"totalNotificationCount":0}""";
        var configJson = """
            {
              "validateOnExecute": true,
              "inputContract": {
                "categories": "Array<...>",
                "priorityItems": "...",
                "channels": "Array<...>",
                "totalNotificationCount": "number"
              }
            }
            """;
        var context = CreateContext(
            configJson: configJson,
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = payload
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("channels",
            "the contract required `channels` but the payload omitted it");
    }

    [Fact]
    public async Task ExecuteAsync_InputContractWithoutValidateOnExecuteFlag_DoesNotEnforce()
    {
        // Arrange — contract present, but validateOnExecute=false (default).
        // Missing keys must NOT fail the run (R4 deploy shape — author keeps
        // inputContract as documentation only until they opt into enforcement).
        var payload = """{"onlyOneField":42}""";
        var configJson = """
            {
              "inputContract": {
                "categories": "Array<...>",
                "channels": "Array<...>"
              }
            }
            """;
        var context = CreateContext(
            configJson: configJson,
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = payload
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region ExecuteAsync — OutputVariable defaulting

    [Fact]
    public async Task ExecuteAsync_EmptyOutputVariable_DefaultsToStart()
    {
        // Arrange — node.OutputVariable is empty; executor must fall back to "start".
        var payload = """{"x":1}""";
        var context = CreateContext(
            configJson: null,
            outputVariable: "",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = payload
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("start",
            "Start nodes default to the 'start' scope variable when unset");
    }

    [Fact]
    public async Task ExecuteAsync_CustomOutputVariable_Respected()
    {
        var payload = """{"x":1}""";
        var context = CreateContext(
            configJson: null,
            outputVariable: "myEntry",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = payload
            });

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("myEntry");
    }

    #endregion

    #region ExecuteAsync — fallback: payload by outputVariable name

    [Fact]
    public async Task ExecuteAsync_NoBriefingPayloadKey_FallsBackToOutputVariableName()
    {
        // Arrange — generic dispatcher (not the daily-briefing wrapper) might key
        // its payload under the variable name itself.
        var payload = """{"hello":"world"}""";
        var context = CreateContext(
            configJson: null,
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["start"] = payload  // not "briefingPayload"
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData!.Value.GetProperty("hello").GetString().Should().Be("world");
    }

    #endregion

    #region Reentrancy (Singleton-with-no-Scoped-deps)

    [Fact]
    public async Task ExecuteAsync_BackToBackInvocations_AreIndependent()
    {
        // The pattern-required equivalent of "uses scope per invocation" for
        // executors with no Scoped deps (matches EntityNameValidatorNodeExecutor's
        // reentrancy test). Two back-to-back invocations on the same instance must
        // produce independent results without cross-contamination.
        var ctx1 = CreateContext(
            configJson: null,
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = """{"run":1}"""
            });
        var ctx2 = CreateContext(
            configJson: null,
            outputVariable: "start",
            parameters: new Dictionary<string, string>
            {
                ["briefingPayload"] = """{"run":2}"""
            });

        var r1 = await _executor.ExecuteAsync(ctx1, CancellationToken.None);
        var r2 = await _executor.ExecuteAsync(ctx2, CancellationToken.None);

        r1.StructuredData!.Value.GetProperty("run").GetInt32().Should().Be(1);
        r2.StructuredData!.Value.GetProperty("run").GetInt32().Should().Be(2);
    }

    #endregion

    #region Helpers

    private static NodeExecutionContext CreateContext(
        string? configJson,
        string? outputVariable,
        IReadOnlyDictionary<string, string>? parameters = null)
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
                NodeType = NodeType.Control,
                ActionId = Guid.Empty,  // Start nodes have no Action FK
                Name = "Start",
                ExecutionOrder = 0,
                OutputVariable = outputVariable ?? string.Empty,
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = Guid.Empty,
                Name = "Start",
                ExecutorType = ExecutorType.Start
            },
            ExecutorType = ExecutorType.Start,
            Scopes = new ResolvedScopes([], [], []),
            Parameters = parameters ?? new Dictionary<string, string>(),
            TenantId = "test-tenant",
            CorrelationId = "corr-" + Guid.NewGuid().ToString("N")
        };
    }

    #endregion
}
