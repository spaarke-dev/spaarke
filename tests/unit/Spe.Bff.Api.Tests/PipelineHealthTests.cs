using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Spe.Bff.Api.Tests;

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
    public async Task Ping_Returns_Ok_With_Service_Info()
    {
        var response = await _client.GetAsync("/ping");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("SPE BFF API");
        content.Should().Contain("ok");
        content.Should().Contain("traceId");
    }

    [Fact]
    public void Services_Should_Be_Registered_Correctly()
    {
        using var scope = _factory.Services.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        // Core services should be registered
        serviceProvider.GetService<Microsoft.AspNetCore.Authorization.IAuthorizationService>().Should().NotBeNull();
        serviceProvider.GetService<Infrastructure.Graph.IGraphClientFactory>().Should().NotBeNull();
        serviceProvider.GetService<Infrastructure.Graph.SpeFileStore>().Should().NotBeNull();
        serviceProvider.GetService<Infrastructure.Graph.UserOperations>().Should().NotBeNull();
    }
}
