using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Sprk.Bff.Api.Tests;

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
        content.Should().Contain("1.0.0");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public void Services_Should_Be_Registered_Correctly()
    {
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Core services should be registered
        serviceProvider.GetService<Microsoft.AspNetCore.Authorization.IAuthorizationService>().Should().NotBeNull();
        serviceProvider.GetService<Sprk.Bff.Api.Infrastructure.Graph.IGraphClientFactory>().Should().NotBeNull();
        serviceProvider.GetService<Sprk.Bff.Api.Infrastructure.Graph.SpeFileStore>().Should().NotBeNull();
        serviceProvider.GetService<Sprk.Bff.Api.Infrastructure.Graph.UserOperations>().Should().NotBeNull();
    }
}
