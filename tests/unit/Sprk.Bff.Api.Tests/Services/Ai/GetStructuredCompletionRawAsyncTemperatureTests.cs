using FluentAssertions;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Hotfix Wave B-G9c1 (B6) — interface-level contract for
/// <see cref="IOpenAiClient.GetStructuredCompletionRawAsync"/> after the temperature
/// parameter was added.
/// </summary>
/// <remarks>
/// <para>
/// Background: prior to B-G9c1, <c>GetStructuredCompletionRawAsync</c> used
/// <c>_options.Temperature</c> (default 0.3) regardless of caller intent — producing
/// non-deterministic structured-output across the 8 tool handlers. The interface now
/// exposes an optional <c>temperature</c> parameter; the implementation defaults to
/// <c>0.0f</c> (deterministic) when null, matching sibling structured methods.
/// </para>
/// <para>
/// Functional pass-through verification of the parameter is exercised by
/// <see cref="Handlers.HandlerTemperaturePassThroughTests"/>, which Moq-verifies the
/// handlers forward <c>context.Temperature</c> as the per-call value. Integration-style
/// asserts on the Azure OpenAI request body would require mocking the
/// <c>System.ClientModel</c> pipeline (not just WireMock at the HTTP layer), which is
/// not currently part of the BFF test scaffolding.
/// </para>
/// </remarks>
public class GetStructuredCompletionRawAsyncTemperatureTests
{
    [Fact]
    public void Interface_DeclaresTemperatureParameter_AsNullableFloat()
    {
        // Hotfix B-G9c1 contract: the temperature param exists, is nullable float, and
        // precedes the cancellation token (final positional argument).
        var method = typeof(IOpenAiClient)
            .GetMethod(nameof(IOpenAiClient.GetStructuredCompletionRawAsync));
        method.Should().NotBeNull(because: "the interface declares GetStructuredCompletionRawAsync");

        var temperatureParam = Array.Find(
            method!.GetParameters(),
            p => p.Name == "temperature");
        temperatureParam.Should().NotBeNull(
            because: "Hotfix B-G9c1 adds an optional 'temperature' parameter to the signature");

        temperatureParam!.ParameterType.Should().Be(
            typeof(float?),
            because: "the parameter type is nullable float (matches ChatCompletionOptions.Temperature)");

        temperatureParam.HasDefaultValue.Should().BeTrue(
            because: "the parameter is optional (default = null → 0.0f at the implementation)");

        temperatureParam.DefaultValue.Should().BeNull(
            because: "null at the interface level means 'apply implementation default (0.0f)'");

        // Temperature must precede cancellationToken so callers using named arguments
        // remain source-compatible.
        var parameters = method.GetParameters();
        var temperatureIndex = Array.IndexOf(parameters, temperatureParam);
        var cancellationTokenParam = Array.Find(parameters, p => p.Name == "cancellationToken");
        cancellationTokenParam.Should().NotBeNull();
        var cancellationTokenIndex = Array.IndexOf(parameters, cancellationTokenParam!);

        temperatureIndex.Should().BeLessThan(
            cancellationTokenIndex,
            because: "cancellationToken stays the final positional parameter per repository convention");
    }

    // ai-advanced-capabilities-nda-r1 follow-up (post-UAT): behavior contract for
    // OpenAiClient.ResolveEffectiveTemperature — the decision that fixes the live "Sorry — I couldn't
    // run that action" failure when NDA Review runs on gpt-5-reasoning. Reasoning-tier deployments
    // (o-series / gpt-5) return HTTP 400 "Only the default (1) value is supported" if `temperature` is
    // present at all — even at 0.0. The client must OMIT the parameter (null) for the configured
    // ReasoningModel, and keep the deterministic-structured default (0.0) for every other model.
    // This is branched domain logic (maintain-class): reverting it re-breaks the live advisory path.

    [Fact]
    public void ResolveEffectiveTemperature_ForReasoningDeployment_ReturnsNull_ToOmitParameter()
    {
        var result = OpenAiClient.ResolveEffectiveTemperature(
            deploymentName: "gpt-5-reasoning",
            reasoningModel: "gpt-5-reasoning",
            requestedTemperature: null);

        result.Should().BeNull(
            because: "reasoning models reject any temperature — the request must omit the parameter");
    }

    [Fact]
    public void ResolveEffectiveTemperature_ForReasoningDeployment_IgnoresExplicitTemperature_ReturnsNull()
    {
        // Even a caller that explicitly passes a temperature (e.g. a stale sprk_temperature) must not
        // send it to a reasoning model — the omission wins over the request value.
        var result = OpenAiClient.ResolveEffectiveTemperature(
            deploymentName: "gpt-5-reasoning",
            reasoningModel: "gpt-5-reasoning",
            requestedTemperature: 0.3f);

        result.Should().BeNull(
            because: "the reasoning-model omission overrides any explicit per-Action temperature");
    }

    [Fact]
    public void ResolveEffectiveTemperature_ForReasoningDeployment_MatchesCaseInsensitively()
    {
        var result = OpenAiClient.ResolveEffectiveTemperature(
            deploymentName: "GPT-5-Reasoning",
            reasoningModel: "gpt-5-reasoning",
            requestedTemperature: null);

        result.Should().BeNull(
            because: "Azure deployment-name comparison is case-insensitive");
    }

    [Fact]
    public void ResolveEffectiveTemperature_ForNonReasoningDeployment_WithNullRequest_ReturnsDeterministicZero()
    {
        var result = OpenAiClient.ResolveEffectiveTemperature(
            deploymentName: "gpt-4o-mini",
            reasoningModel: "gpt-5-reasoning",
            requestedTemperature: null);

        result.Should().Be(0.0f,
            because: "non-reasoning structured output defaults to 0.0 for determinism (Wave B-G9c1)");
    }

    [Fact]
    public void ResolveEffectiveTemperature_ForNonReasoningDeployment_WithExplicitValue_ReturnsThatValue()
    {
        var result = OpenAiClient.ResolveEffectiveTemperature(
            deploymentName: "gpt-4o-mini",
            reasoningModel: "gpt-5-reasoning",
            requestedTemperature: 0.7f);

        result.Should().Be(0.7f,
            because: "a non-reasoning model honors the caller's explicit per-Action temperature");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveEffectiveTemperature_WhenReasoningModelUnconfigured_TreatsDeploymentAsNonReasoning(
        string? reasoningModel)
    {
        // Interim state: a Reasoning-tier Action whose ReasoningModel is not yet provisioned falls back
        // to the Standard deployment (gpt-4o-mini) in ModelTierDeploymentResolver — a normal chat model
        // that WANTS a temperature. With no reasoning model configured, nothing is ever matched.
        var result = OpenAiClient.ResolveEffectiveTemperature(
            deploymentName: "gpt-4o-mini",
            reasoningModel: reasoningModel,
            requestedTemperature: null);

        result.Should().Be(0.0f,
            because: "an unconfigured ReasoningModel never matches — the fallback deployment keeps temperature");
    }

    // ai-advanced-capabilities-nda-r1 follow-up (post-UAT round 2): IsReasoningDeployment gates BOTH the
    // temperature omission AND the max-output-tokens omission. The live gpt-5 failure was actually
    // "Unsupported parameter: 'max_tokens'": the SDK serializes MaxOutputTokenCount as `max_tokens`
    // (verified at api-version 2025-04-01-preview), which reasoning models reject. GetStructuredCompletionRawAsync
    // omits MaxOutputTokenCount when IsReasoningDeployment is true. This decision is maintain-class:
    // reverting it re-breaks the live NDA Review path.

    [Fact]
    public void IsReasoningDeployment_WhenDeploymentMatchesReasoningModel_ReturnsTrue()
    {
        OpenAiClient.IsReasoningDeployment("gpt-5-reasoning", "gpt-5-reasoning")
            .Should().BeTrue(because: "the deployment IS the configured reasoning model → omit max_tokens + temperature");
    }

    [Fact]
    public void IsReasoningDeployment_MatchesCaseInsensitively()
    {
        OpenAiClient.IsReasoningDeployment("GPT-5-Reasoning", "gpt-5-reasoning")
            .Should().BeTrue(because: "Azure deployment-name comparison is case-insensitive");
    }

    [Fact]
    public void IsReasoningDeployment_ForNonReasoningDeployment_ReturnsFalse()
    {
        OpenAiClient.IsReasoningDeployment("gpt-4o-mini", "gpt-5-reasoning")
            .Should().BeFalse(because: "a normal chat model keeps its max_tokens cap + temperature");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsReasoningDeployment_WhenReasoningModelUnconfigured_ReturnsFalse(string? reasoningModel)
    {
        OpenAiClient.IsReasoningDeployment("gpt-4o-mini", reasoningModel)
            .Should().BeFalse(because: "with no reasoning model configured, nothing is ever treated as reasoning");
    }
}
