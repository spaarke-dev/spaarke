using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests;

[Trait("status", "repaired")]
public class PipelineHealthTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;
    private readonly HttpClient _client;

    public PipelineHealthTests(CustomWebAppFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task HealthCheck_Returns_Ok()
    {
        var response = await _client.GetAsync("/healthz");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ping_Returns_Pong()
    {
        // Task 021: /ping returns simple "pong" response for warm-up agents
        var response = await _client.GetAsync("/ping");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("pong");
    }

    [Fact]
    public async Task Status_Returns_Service_Metadata()
    {
        // Task 021: /status returns service metadata JSON
        var response = await _client.GetAsync("/status");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Sprk.Bff.Api");
        content.Should().Contain("1.0.2");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    // `Services_Should_Be_Registered_Correctly` was deleted here by task CICD-094 (issue #864) as the
    // B3 migration that let the B3 guard arm green. It resolved four services and asserted each was
    // non-null:
    //
    //     serviceProvider.GetService<IGraphClientFactory>().Should().NotBeNull();
    //
    // That is ADR-038 §7 B3 verbatim. The four are UNCONDITIONAL core registrations reached by
    // endpoints that carry their own contract tests — if any were unregistered, those endpoints
    // return 500 and the contract tests fail with a message that names the endpoint, which is
    // strictly more diagnostic than "a service was null". The three HTTP tests above it survive and
    // are the real value of this class.
    //
    // Deliberately NOT extended to conditional registrations. Root CLAUDE.md §10 bullet 6 and
    // ADR-032 govern feature-gated services, where the concern is asymmetric registration rather
    // than absence; the sanctioned shape there asserts the resolved TYPE
    // (`.Should().BeOfType<NullFoo>()`) or its absence (`.Should().BeNull()`), and the B3 detector
    // is written not to fire on either. See Adr038TestBanGuardTests.B3_NoDiRegistrationAssertions.
}
