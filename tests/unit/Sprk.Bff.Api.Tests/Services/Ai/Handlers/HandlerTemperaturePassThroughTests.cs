using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Handlers;

/// <summary>
/// Hotfix Wave B-G9c1 (B6) — verifies the structured-output tool handlers forward
/// <see cref="ToolExecutionContext.Temperature"/> through to
/// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Coverage focus: two representative handlers (EntityExtractorHandler, RiskDetectorHandler).
/// The same change applies symmetrically across the 8 LLM-assisted handlers:
/// SummaryHandler, SemanticSearchToolHandler, DocumentClassifierHandler,
/// EntityExtractorHandler, ClauseAnalyzerHandler, RiskDetectorHandler,
/// GenericAnalysisHandler, InvoiceExtractionToolHandler — each now reads
/// <c>context.Temperature</c> (sourced from <c>sprk_analysisaction.sprk_temperature</c>
/// via <see cref="Sprk.Bff.Api.Services.Ai.Nodes.AiAnalysisNodeExecutor"/>) and passes it
/// to <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/>.
/// </para>
/// <para>
/// Per-handler unit-test files already cover positive paths, error cases, telemetry,
/// and caching — those test files still use <c>It.IsAny&lt;float?&gt;()</c> for the
/// temperature param to remain stable across orthogonal changes. These tests are the
/// focused regression guard for B-G9c1.
/// </para>
/// </remarks>
public sealed class HandlerTemperaturePassThroughTests : TypedToolHandlerTestFixture
{
    // FR-05 redis remediation r1: a real in-memory ITenantCache so handlers complete normally.
    private readonly ITenantCache _cache = new TenantCache(
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        NullLogger<TenantCache>.Instance);

    [Theory]
    [InlineData(0.0, "deterministic default (action.Temperature == null)")]
    [InlineData(0.5, "moderate override (action.Temperature == 0.5m)")]
    [InlineData(0.7, "creative override (action.Temperature == 0.7m)")]
    public async Task EntityExtractorHandler_ForwardsContextTemperature_ToOpenAiClient(
        double contextTemperature,
        string scenarioDescription)
    {
        // Arrange
        var capturedTemperature = float.NaN;
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (_, _, _, _, _, temp, _) => capturedTemperature = temp ?? float.NaN)
            .ReturnsAsync("""{"entities":[]}""");

        var handler = new EntityExtractorHandler(
            OpenAiClientMock.Object,
            _cache,
            Options.Create(new ModelSelectorOptions
            {
                ToolHandlerModel = "gpt-4o-mini",
                DefaultModel = "gpt-4o-mini"
            }),
            CreateLogger<EntityExtractorHandler>());

        var context = BuildToolExecutionContext(
            extractedText: $"Test input for temperature {contextTemperature}.",
            temperature: contextTemperature);
        var tool = BuildAnalysisTool(
            handlerClass: nameof(EntityExtractorHandler),
            toolType: ToolType.EntityExtractor);

        // Act
        await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        capturedTemperature.Should().BeApproximately(
            (float)contextTemperature,
            precision: 1e-6f,
            because: $"B-G9c1 wires context.Temperature ({scenarioDescription}) into GetStructuredCompletionRawAsync");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.4)]
    [InlineData(1.0)]
    public async Task RiskDetectorHandler_ForwardsContextTemperature_ToOpenAiClient(
        double contextTemperature)
    {
        // Arrange
        var capturedTemperature = float.NaN;
        OpenAiClientMock
            .Setup(c => c.GetStructuredCompletionRawAsync(
                It.IsAny<string>(),
                It.IsAny<BinaryData>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<int?>(),
                It.IsAny<float?>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, BinaryData, string, string?, int?, float?, CancellationToken>(
                (_, _, _, _, _, temp, _) => capturedTemperature = temp ?? float.NaN)
            .ReturnsAsync("""{"risks":[]}""");

        var handler = new RiskDetectorHandler(
            OpenAiClientMock.Object,
            _cache,
            Options.Create(new ModelSelectorOptions
            {
                ToolHandlerModel = "gpt-4o-mini",
                DefaultModel = "gpt-4o-mini"
            }),
            CreateLogger<RiskDetectorHandler>());

        var context = BuildToolExecutionContext(
            extractedText: $"Test input for temperature {contextTemperature}.",
            temperature: contextTemperature);
        var tool = BuildAnalysisTool(
            handlerClass: nameof(RiskDetectorHandler),
            toolType: ToolType.RiskDetector);

        // Act
        await handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        capturedTemperature.Should().BeApproximately(
            (float)contextTemperature,
            precision: 1e-6f,
            because: "B-G9c1: RiskDetectorHandler must forward the per-action temperature override");
    }
}
