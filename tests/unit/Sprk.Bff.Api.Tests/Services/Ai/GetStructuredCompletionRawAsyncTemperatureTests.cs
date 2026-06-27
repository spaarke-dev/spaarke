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
}
