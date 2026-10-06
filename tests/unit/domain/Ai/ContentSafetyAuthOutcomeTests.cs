using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Safety;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai;

/// <summary>
/// Task 230b — Prompt Shield and groundedness still fail OPEN when Content Safety refuses the BFF's identity (a
/// user's turn is never blocked by a perimeter outage), but the refusal is a DISTINCT outcome: it is not transient,
/// and a stamp has no key to fall back to, so every request rides unshielded until the role or credential is fixed.
/// </summary>
public sealed class ContentSafetyAuthOutcomeTests : IDisposable
{
    private const string BaseAddress = "https://cs.example.cognitiveservices.azure.com/";

    private readonly MeterListener _listener = new();
    private readonly ConcurrentBag<(string Instrument, string? Outcome)> _measurements = new();

    public ContentSafetyAuthOutcomeTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name is "ai.safety.shield_evaluations" or "ai_safety_groundedness_latency_ms" or "ai_safety_prompt_shield_latency_ms")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((i, _, tags, _) => _measurements.Add((i.Name, OutcomeOf(tags))));
        _listener.SetMeasurementEventCallback<double>((i, _, tags, _) => _measurements.Add((i.Name, OutcomeOf(tags))));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task PromptShield_WhenContentSafetyRefusesTheIdentity_FailsOpenAsAuthRefused(HttpStatusCode status)
    {
        var aiTelemetry = new AiTelemetry();
        var service = PromptShield(new StatusHandler(status), new FixedTokenCredential(), aiTelemetry);

        var result = await service.ScanAsync(new PromptShieldRequest("hello"));

        result.IsBlocked.Should().BeFalse("the perimeter fails open — the user's turn is never blocked by it");
        result.FailedOpen.Should().BeTrue();
        result.AuthRefused.Should().BeTrue();
        _measurements.Should().Contain(("ai.safety.shield_evaluations", AiTelemetry.ShieldOutcomeFailedOpenAuth));
    }

    [Fact]
    public async Task PromptShield_AnAuthRefusal_IsOneFailOpenLatencySample_NotAlsoASafeScan()
    {
        var service = PromptShield(new StatusHandler(HttpStatusCode.Forbidden), new FixedTokenCredential(), new AiTelemetry());

        await service.ScanAsync(new PromptShieldRequest("hello"));

        _measurements.Where(m => m.Instrument == "ai_safety_prompt_shield_latency_ms").Select(m => m.Outcome)
            .Should().Equal("fail_open");
    }

    [Fact]
    public async Task PromptShield_WhenNoTokenCanBeAcquired_FailsOpenAsAuthRefused()
    {
        var service = PromptShield(new StatusHandler(HttpStatusCode.OK), new UnavailableCredential(), new AiTelemetry());

        var result = await service.ScanAsync(new PromptShieldRequest("hello"));

        result.FailedOpen.Should().BeTrue();
        result.AuthRefused.Should().BeTrue();
    }

    [Fact]
    public async Task PromptShield_AServerError_FailsOpenButIsNotAnAuthRefusal()
    {
        var service = PromptShield(new StatusHandler(HttpStatusCode.InternalServerError), new FixedTokenCredential(), new AiTelemetry());

        var result = await service.ScanAsync(new PromptShieldRequest("hello"));

        result.FailedOpen.Should().BeTrue();
        result.AuthRefused.Should().BeFalse("a 5xx is transient; only 401/403 or a missing token is an auth refusal");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Groundedness_WhenContentSafetyRefusesTheIdentity_AssumesGrounded_WithTheAuthOutcome(HttpStatusCode status)
    {
        var service = new GroundednessCheckService(
            Client(new StatusHandler(status), new FixedTokenCredential()),
            NullLogger<GroundednessCheckService>.Instance,
            new GroundednessCheckTelemetry());

        var result = await service.CheckAsync(new GroundednessRequest("answer", ["source"]));

        result.IsGrounded.Should().BeTrue("groundedness fails open");
        _measurements.Should().Contain(("ai_safety_groundedness_latency_ms", GroundednessCheckTelemetry.OutcomeFailOpenAuth));
    }

    [Fact]
    public async Task Groundedness_AServerError_KeepsThePlainFailOpenOutcome()
    {
        var service = new GroundednessCheckService(
            Client(new StatusHandler(HttpStatusCode.ServiceUnavailable), new FixedTokenCredential()),
            NullLogger<GroundednessCheckService>.Instance,
            new GroundednessCheckTelemetry());

        await service.CheckAsync(new GroundednessRequest("answer", ["source"]));

        _measurements.Should().Contain(("ai_safety_groundedness_latency_ms", GroundednessCheckTelemetry.OutcomeFailOpen));
        _measurements.Should().NotContain(m => m.Outcome == GroundednessCheckTelemetry.OutcomeFailOpenAuth);
    }

    private static PromptShieldService PromptShield(HttpMessageHandler handler, TokenCredential credential, AiTelemetry aiTelemetry)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiSafety:PromptShield:TimeoutMs"] = "10000" })
            .Build();
        return new PromptShieldService(
            new SingleClientFactory(Client(handler, credential)),
            new PromptShieldTelemetry(),
            aiTelemetry,
            NullLogger<PromptShieldService>.Instance,
            configuration);
    }

    /// <summary>The production pipeline: the real ContentSafetyAuthHandler (managed-identity bearer) over a stub transport.</summary>
    private static HttpClient Client(HttpMessageHandler transport, TokenCredential credential)
    {
        var auth = new ContentSafetyAuthHandler(
            new ConfigurationBuilder().Build(),
            new ContentSafetyTokenProvider(credential),
            NullLogger<ContentSafetyAuthHandler>.Instance)
        {
            InnerHandler = transport,
        };
        return new HttpClient(auth) { BaseAddress = new Uri(BaseAddress) };
    }

    private static string? OutcomeOf(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == "outcome")
                return tag.Value as string;
        }
        return null;
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class UnavailableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new Azure.Identity.CredentialUnavailableException("no identity");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new Azure.Identity.CredentialUnavailableException("no identity");
    }
}
