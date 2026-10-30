using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Services.Ai.ContentUnderstanding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api;

/// <summary>
/// CONTRACT — the Content Understanding invoice reader's host wiring (ebilling-r1 task 043). The host starts with the
/// reader not configured and with it configured; a malformed setting stops the host at startup (ValidateOnStart); the
/// check is registered on the catalog tag (never the liveness probe) under the name <c>content-understanding</c>.
/// </summary>
public sealed class ContentUnderstandingHostContractTests : IClassFixture<CustomWebAppFactory>
{
    private const string CheckName = "content-understanding";
    private readonly CustomWebAppFactory _factory;

    public ContentUnderstandingHostContractTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Host_ReaderNotConfigured_StartsAndTheCheckIsHealthy()
    {
        using var client = _factory.CreateClient();

        (await client.GetAsync("/healthz")).StatusCode.Should().Be(HttpStatusCode.OK);
        _factory.Services.GetRequiredService<ContentUnderstandingClient>().IsConfigured.Should().BeFalse();

        var report = await _factory.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Name == CheckName);

        var entry = report.Entries.Should().ContainKey(CheckName).WhoseValue;
        entry.Status.Should().Be(HealthStatus.Healthy);
        entry.Description.Should().Contain("not configured");
        entry.Tags.Should().Contain("catalog", "a missing analyzer must never reach the /healthz liveness probe");
    }

    [Fact]
    public async Task Host_ReaderConfigured_StartsAndAnUnreachableAnalyzerIsReportedAsMissing()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ContentUnderstanding:Endpoint", "https://cu-contract.cognitiveservices.azure.com/");
            builder.UseSetting("ContentUnderstanding:ApiVersion", "2025-11-01");
            builder.UseSetting("ContentUnderstanding:AnalyzerId", "sprk_invoice_v2");
        });
        using var client = factory.CreateClient();

        (await client.GetAsync("/healthz")).StatusCode.Should().Be(HttpStatusCode.OK, "the catalog check is not on the liveness probe");
        var reader = factory.Services.GetRequiredService<ContentUnderstandingClient>();
        reader.IsConfigured.Should().BeTrue();
        reader.AnalyzerId.Should().Be("sprk_invoice_v2");
        reader.ApiVersion.Should().Be("2025-11-01");

        // The test host blocks outbound calls (TestOutboundNetworkGuard), so the analyzer GET cannot succeed.
        var report = await factory.Services.GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Name == CheckName);

        var entry = report.Entries[CheckName];
        entry.Status.Should().Be(HealthStatus.Unhealthy);
        entry.Description.Should().StartWith(ContentUnderstandingHealthCheck.FailureCode);
    }

    [Fact]
    public void Host_MalformedEndpoint_FailsAtStartup()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ContentUnderstanding:Endpoint", "http://not-https.example.com/"));

        var start = () => factory.CreateClient();

        start.Should().Throw<OptionsValidationException>().WithMessage("*ContentUnderstanding:Endpoint*");
    }
}
