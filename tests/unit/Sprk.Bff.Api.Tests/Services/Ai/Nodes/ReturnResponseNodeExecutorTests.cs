// R4 spaarke-daily-update-service-r4 — Tests for ReturnResponseNodeExecutor (UAT
// 2026-06-26 follow-on to Start fix). Mirrors StartNodeExecutorTests shape:
//   - SupportedExecutorTypes contains ReturnResponse.
//   - Validate: null / empty / well-formed / malformed configJson.
//   - ExecuteAsync: responseBinding resolves all templates against scope.
//   - ExecuteAsync: _validationMetadata sidecar binding (nested name→template map).
//   - ExecuteAsync: missing template variable yields empty/null (does NOT throw).
//   - ExecuteAsync: binds final object to outputVariable (default "response").
//   - OutputVariable defaulting (empty → "response").
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
/// Unit tests for <see cref="ReturnResponseNodeExecutor"/>. Validates that upstream
/// node outputs project into the bound response object via responseBinding templates,
/// the _validationMetadata sidecar resolves correctly, OutputVariable defaulting
/// behaves as documented, and missing scope variables yield empty (do not throw).
/// </summary>
public class ReturnResponseNodeExecutorTests
{
    private readonly Mock<ILogger<ReturnResponseNodeExecutor>> _loggerMock;
    private readonly TemplateEngine _templateEngine;
    private readonly ReturnResponseNodeExecutor _executor;

    public ReturnResponseNodeExecutorTests()
    {
        _loggerMock = new Mock<ILogger<ReturnResponseNodeExecutor>>();
        _templateEngine = new TemplateEngine(new Mock<ILogger<TemplateEngine>>().Object);
        _executor = new ReturnResponseNodeExecutor(_templateEngine, _loggerMock.Object);
    }

    #region SupportedExecutorTypes

    [Fact]
    public void SupportedActionTypes_ContainsReturnResponse()
    {
        _executor.SupportedExecutorTypes.Should().Contain(ExecutorType.ReturnResponse);
        _executor.SupportedExecutorTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate

    [Fact]
    public void Validate_NullConfigJson_Succeeds()
    {
        var context = CreateContext(configJson: null, outputVariable: "response");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_HappyPath_ResponseBindingPresent_Succeeds()
    {
        var configJson = """
            {
              "responseBinding": {
                "tldr": "{{tldrResult}}",
                "channelNarratives": "{{channelNarrationResults}}",
                "generatedAtUtc": "{{run.completedAtUtc}}"
              }
            }
            """;
        var context = CreateContext(configJson: configJson, outputVariable: "response");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_MalformedJson_ReturnsError()
    {
        var context = CreateContext(configJson: "{not-json}", outputVariable: "response");

        var result = _executor.Validate(context);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not valid JSON"));
    }

    #endregion

    #region ExecuteAsync — resolves all templates against scope

    [Fact]
    public async Task ExecuteAsync_ResponseBinding_ResolvesScalarFromScope()
    {
        // Arrange — a simple scalar template references the upstream node output.
        var tldrPayload = """{"summary":"3 mortgage matters today.","keyTakeaways":["k1","k2"]}""";
        var tldrOutput = NodeOutput.Ok(Guid.NewGuid(), "tldrResult",
            JsonDocument.Parse(tldrPayload).RootElement);

        var configJson = """
            {
              "responseBinding": {
                "summary": "{{tldrResult.summary}}"
              }
            }
            """;

        var context = CreateContext(
            configJson: configJson,
            outputVariable: "response",
            previousOutputs: new Dictionary<string, NodeOutput> { ["tldrResult"] = tldrOutput });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("response");
        result.StructuredData!.Value.GetProperty("summary").GetString()
            .Should().Be("3 mortgage matters today.");
    }

    #endregion

    #region ExecuteAsync — _validationMetadata sidecar

    [Fact]
    public async Task ExecuteAsync_ValidationMetadataSidecar_ResolvedAsNestedObject()
    {
        // Arrange — daily-briefing-narrate's _validationMetadata sidecar carries
        // scrubbedText + removedTerms from the EntityNameValidator upstream.
        var validationPayload = """{"scrubbedText":"clean text","removedTerms":["Acme LLP"]}""";
        var validationOutput = NodeOutput.Ok(Guid.NewGuid(), "validationResult",
            JsonDocument.Parse(validationPayload).RootElement);

        var configJson = """
            {
              "responseBinding": {
                "tldr": "{{tldrResult.summary}}",
                "_validationMetadata": {
                  "scrubbedText": "{{validationResult.scrubbedText}}",
                  "removedTerms": "{{validationResult.removedTerms}}"
                }
              }
            }
            """;

        var context = CreateContext(
            configJson: configJson,
            outputVariable: "response",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["validationResult"] = validationOutput
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData!.Value.TryGetProperty("_validationMetadata", out var sidecar)
            .Should().BeTrue("the _validationMetadata sidecar must be present on the bound output");
        sidecar.ValueKind.Should().Be(JsonValueKind.Object,
            "_validationMetadata is a nested object, not a string");
        sidecar.GetProperty("scrubbedText").GetString().Should().Be("clean text");
    }

    #endregion

    #region ExecuteAsync — missing variables yield empty (no throw)

    [Fact]
    public async Task ExecuteAsync_MissingScopeVariable_YieldsEmptyDoesNotThrow()
    {
        // Arrange — responseBinding references {{tldrResult.summary}} but the
        // tldrResult upstream did NOT execute (PreviousOutputs empty). Per playbook
        // design: missing scope variables yield empty/null. The executor must NOT
        // throw and MUST return Success.
        var configJson = """
            {
              "responseBinding": {
                "summary": "{{tldrResult.summary}}",
                "channelNarratives": "{{channelNarrationResults}}"
              }
            }
            """;
        var context = CreateContext(
            configJson: configJson,
            outputVariable: "response");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.StructuredData!.Value.TryGetProperty("summary", out _).Should().BeTrue();
        // Both fields resolve to empty string (Handlebars graceful-missing contract).
    }

    #endregion

    #region ExecuteAsync — binds to outputVariable

    [Fact]
    public async Task ExecuteAsync_BindsFinalObjectToOutputVariableDefaultResponse()
    {
        var configJson = """{"responseBinding":{"hello":"world-literal"}}""";
        var context = CreateContext(
            configJson: configJson,
            outputVariable: "");

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("response",
            "ReturnResponse defaults to 'response' scope variable when unset");
        result.StructuredData!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        result.StructuredData!.Value.GetProperty("hello").GetString().Should().Be("world-literal");
    }

    [Fact]
    public async Task ExecuteAsync_CustomOutputVariable_Respected()
    {
        var context = CreateContext(
            configJson: """{"responseBinding":{"x":"1"}}""",
            outputVariable: "myFinal");

        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.OutputVariable.Should().Be("myFinal");
    }

    #endregion

    #region Reentrancy

    [Fact]
    public async Task ExecuteAsync_BackToBackInvocations_AreIndependent()
    {
        var ctx1 = CreateContext(
            configJson: """{"responseBinding":{"run":"{{tldrResult.x}}"}}""",
            outputVariable: "response",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["tldrResult"] = NodeOutput.Ok(Guid.NewGuid(), "tldrResult",
                    JsonDocument.Parse("""{"x":"first"}""").RootElement)
            });

        var ctx2 = CreateContext(
            configJson: """{"responseBinding":{"run":"{{tldrResult.x}}"}}""",
            outputVariable: "response",
            previousOutputs: new Dictionary<string, NodeOutput>
            {
                ["tldrResult"] = NodeOutput.Ok(Guid.NewGuid(), "tldrResult",
                    JsonDocument.Parse("""{"x":"second"}""").RootElement)
            });

        var r1 = await _executor.ExecuteAsync(ctx1, CancellationToken.None);
        var r2 = await _executor.ExecuteAsync(ctx2, CancellationToken.None);

        r1.StructuredData!.Value.GetProperty("run").GetString().Should().Be("first");
        r2.StructuredData!.Value.GetProperty("run").GetString().Should().Be("second");
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
                Name = "ReturnResponse",
                ExecutionOrder = 5,
                OutputVariable = outputVariable ?? string.Empty,
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = Guid.Empty,
                Name = "ReturnResponse",
                ExecutorType = ExecutorType.ReturnResponse
            },
            ExecutorType = ExecutorType.ReturnResponse,
            Scopes = new ResolvedScopes([], [], []),
            PreviousOutputs = previousOutputs ?? new Dictionary<string, NodeOutput>(),
            Parameters = new Dictionary<string, string>(),
            TenantId = "test-tenant",
            CorrelationId = "corr-" + Guid.NewGuid().ToString("N")
        };
    }

    #endregion
}
