using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.ContentUnderstanding;
using Xunit;
using static Sprk.Bff.Api.Tests.Seam.ContentUnderstanding.ContentUnderstandingTestHost;

namespace Sprk.Bff.Api.Tests.Seam.ContentUnderstanding;

/// <summary>
/// ebilling-r1 task 043: the catalog health check GETs the configured analyzer on the configured api-version and
/// reports a missing one as <c>content_understanding.analyzer_missing</c>; an unconfigured reader is Healthy.
/// </summary>
public sealed class ContentUnderstandingHealthCheckTests
{
    private static Task<HealthCheckResult> Check(ContentUnderstandingOptions options, FakeContentUnderstandingService service) =>
        new ContentUnderstandingHealthCheck(Client(options, service)).CheckHealthAsync(new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("content-understanding", _ => null!, HealthStatus.Unhealthy, null),
        });

    [Fact]
    public async Task CheckHealth_NotConfigured_IsHealthyWithoutCallingTheService()
    {
        var service = new FakeContentUnderstandingService();

        var result = await Check(new ContentUnderstandingOptions(), service);

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("not configured");
        service.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckHealth_AnalyzerExists_IsHealthyAndAsksForTheConfiguredAnalyzerAndVersion()
    {
        var service = new FakeContentUnderstandingService
        {
            OnGetAnalyzer = _ => JsonResponse(HttpStatusCode.OK, """{"analyzerId":"sprk_invoice_v2p","status":"ready"}"""),
        };

        var result = await Check(PreviewOptions(), service);

        result.Status.Should().Be(HealthStatus.Healthy);
        var get = service.Requests.Should().ContainSingle().Subject;
        get.Uri.AbsolutePath.Should().Be("/contentunderstanding/analyzers/sprk_invoice_v2p");
        get.Uri.Query.Should().Contain("api-version=2026-06-01-preview");
        get.Authorization.Should().Be($"Bearer {Token}");
    }

    [Fact]
    public async Task CheckHealth_AnalyzerMissing_IsTheFailureStatusWithTheAnalyzerMissingCode()
    {
        var service = new FakeContentUnderstandingService
        {
            OnGetAnalyzer = _ => Error(HttpStatusCode.NotFound, "NotFound", "ModelNotFound"),
        };

        var result = await Check(GaOptions(), service);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().StartWith(ContentUnderstandingHealthCheck.FailureCode)
            .And.Contain("sprk_invoice_v2").And.Contain("2025-11-01").And.Contain("HTTP 404");
    }

    [Fact]
    public async Task CheckHealth_AnalyzerFailed_IsTheFailureStatus()
    {
        var service = new FakeContentUnderstandingService
        {
            OnGetAnalyzer = _ => JsonResponse(HttpStatusCode.OK, """{"analyzerId":"sprk_invoice_v2p","status":"failed"}"""),
        };

        var result = await Check(PreviewOptions(), service);

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().StartWith(ContentUnderstandingHealthCheck.FailureCode);
    }
}
