// -----------------------------------------------------------------------------
// TenantContainerResolverEndpointTests.cs
//
// Unit tests for GET /api/diagnostics/tenant-container-resolver (G-8 Batch 6 —
// customer-provisioning-orchestration-r1, fix #18). Exercises the endpoint
// handler directly with a fake HttpContext + hand-rolled resolver (parity with
// ConsentCallbackEndpointTests; ADR-038 — no Mock<HttpMessageHandler>).
//
// Coverage against the L2 SpeContainerResolverInvariantProbe contract:
//   * happy path (query tenantId) → 200 with { tenantId, containerId,
//     resolverSource, resolvedFromLiteral=false, resolvedAt }
//   * tenantId falls back to the JWT tid claim when the query param is absent
//   * missing tenantId everywhere → 400 (never ambient-tenant resolution)
//   * TenantNotServed → 400 · ContainerNotConfigured/TenantScopeNotPinned → 500
//   * resolver throwing unexpectedly → 500 Problem (not an unhandled exception)
//   * unauthenticated posture → endpoint carries IAuthorizeData metadata so the
//     standard auth middleware 401s missing/invalid JWTs (real-transport 401 is
//     produced by the middleware, asserted here via mapped-endpoint metadata)
// -----------------------------------------------------------------------------

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Endpoints.Diagnostics;
using Xunit;

namespace Sprk.Bff.Api.Tests.Diagnostics;

public sealed class TenantContainerResolverEndpointTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string ContainerId = "b!AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-abcdef";

    [Fact]
    public async Task HandleAsync_QueryTenantIdAndResolverSucceeds_Returns200MatchingProbeContract()
    {
        var resolver = new FakeResolver(TenantContainerResolutionResult.Success(
            new TenantContainerResolution(TenantId, ContainerId, "options", ResolvedFromLiteral: false)));
        var ctx = NewHttpContext(queryTenantId: TenantId);

        var result = await TenantContainerResolverEndpoint.HandleAsync(
            ctx, resolver, NullLoggerFactory.Instance, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status200OK);

        var response = result.Should().BeAssignableTo<IValueHttpResult>()
            .Which.Value.Should().BeOfType<TenantContainerResolverResponse>().Subject;

        // The L2 probe's required fields + semantics:
        response.TenantId.Should().Be(TenantId, "the probe compares the echo ordinally against its request");
        response.ContainerId.Should().Be(ContainerId).And.MatchRegex(
            @"^b![A-Za-z0-9_\-]{20,}$", "the probe enforces the canonical Graph SPE container-id shape");
        response.ResolvedFromLiteral.Should().BeFalse("true is a CATASTROPHIC verdict to the probe");
        response.ResolverSource.Should().Be("options");
        DateTimeOffset.Parse(response.ResolvedAt).Should().BeCloseTo(
            DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        resolver.ResolvedTenantIds.Should().ContainSingle().Which.Should().Be(
            TenantId, "the handler must perform a LIVE resolver call, not mirror inputs");
    }

    [Fact]
    public async Task HandleAsync_NoQueryParam_FallsBackToJwtTidClaim()
    {
        var resolver = new FakeResolver(TenantContainerResolutionResult.Success(
            new TenantContainerResolution(TenantId, ContainerId, "options", ResolvedFromLiteral: false)));
        var ctx = NewHttpContext(queryTenantId: null, tidClaim: TenantId);

        var result = await TenantContainerResolverEndpoint.HandleAsync(
            ctx, resolver, NullLoggerFactory.Instance, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status200OK);
        resolver.ResolvedTenantIds.Should().ContainSingle().Which.Should().Be(TenantId);
    }

    [Fact]
    public async Task HandleAsync_NoTenantIdAnywhere_Returns400_NoResolverCall()
    {
        var resolver = new FakeResolver(TenantContainerResolutionResult.Success(
            new TenantContainerResolution(TenantId, ContainerId, "options", ResolvedFromLiteral: false)));
        var ctx = NewHttpContext(queryTenantId: null, tidClaim: null);

        var result = await TenantContainerResolverEndpoint.HandleAsync(
            ctx, resolver, NullLoggerFactory.Instance, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        resolver.ResolvedTenantIds.Should().BeEmpty(
            "no ambient/default-tenant resolution may be attempted (§4D I1/I4)");
    }

    [Fact]
    public async Task HandleAsync_TenantNotServed_Returns400()
    {
        var resolver = new FakeResolver(TenantContainerResolutionResult.Failure(
            TenantContainerResolutionFailureCode.TenantNotServed, "not the tenant this stamp serves"));
        var ctx = NewHttpContext(queryTenantId: "99999999-8888-7777-6666-555555555555");

        var result = await TenantContainerResolverEndpoint.HandleAsync(
            ctx, resolver, NullLoggerFactory.Instance, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Theory]
    [InlineData(TenantContainerResolutionFailureCode.ContainerNotConfigured)]
    [InlineData(TenantContainerResolutionFailureCode.TenantScopeNotPinned)]
    public async Task HandleAsync_DeploymentMisconfiguration_Returns500WithDiagnostic(
        TenantContainerResolutionFailureCode failureCode)
    {
        var resolver = new FakeResolver(TenantContainerResolutionResult.Failure(
            failureCode, "deployment misconfiguration diagnostic"));
        var ctx = NewHttpContext(queryTenantId: TenantId);

        var result = await TenantContainerResolverEndpoint.HandleAsync(
            ctx, resolver, NullLoggerFactory.Instance, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task HandleAsync_ResolverThrowsUnexpectedly_Returns500Problem_NotUnhandledException()
    {
        var resolver = new FakeResolver(exception: new InvalidOperationException("boom"));
        var ctx = NewHttpContext(queryTenantId: TenantId);

        var result = await TenantContainerResolverEndpoint.HandleAsync(
            ctx, resolver, NullLoggerFactory.Instance, CancellationToken.None);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public void MapTenantContainerResolverEndpoint_MapsGetOnProbePathWithAuthorizationMetadata()
    {
        // Unauthenticated posture: RequireAuthorization() attaches IAuthorizeData, so the
        // standard JWT-bearer middleware 401s missing/invalid tokens exactly like every
        // other BFF endpoint (the probe classifies 401/403 as InfraFault, never a false Pass).
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
        });
        using var app = builder.Build();

        ((IEndpointRouteBuilder)app).MapTenantContainerResolverEndpoint();

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(ds => ds.Endpoints)
            .OfType<RouteEndpoint>()
            .Should().ContainSingle().Subject;

        // Path MUST equal the L2 probe's DiagnosticEndpointPath constant.
        endpoint.RoutePattern.RawText.Should().Be("/api/diagnostics/tenant-container-resolver");
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
            .Should().ContainSingle().Which.Should().Be("GET", "the probe issues a GET");
        endpoint.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull(
            "JWT bearer auth is required — parity with all other BFF endpoints");
        endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>().Should().BeEmpty();
    }

    // ---------------------------------------------------------------- helpers

    private static DefaultHttpContext NewHttpContext(string? queryTenantId, string? tidClaim = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        if (queryTenantId is not null)
        {
            ctx.Request.QueryString = new QueryString($"?tenantId={Uri.EscapeDataString(queryTenantId)}");
        }

        if (tidClaim is not null)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim("tid", tidClaim) }, authenticationType: "TestJwt"));
        }

        return ctx;
    }

    /// <summary>Hand-rolled ITenantContainerResolver capturing calls (ADR-038 — no mocking framework).</summary>
    private sealed class FakeResolver : ITenantContainerResolver
    {
        private readonly TenantContainerResolutionResult? _result;
        private readonly Exception? _exception;

        public FakeResolver(TenantContainerResolutionResult result) => _result = result;

        public FakeResolver(Exception exception) => _exception = exception;

        public List<string> ResolvedTenantIds { get; } = new();

        public Task<TenantContainerResolutionResult> ResolveAsync(
            string tenantId, CancellationToken cancellationToken)
        {
            ResolvedTenantIds.Add(tenantId);
            if (_exception is not null)
            {
                throw _exception;
            }

            return Task.FromResult(_result!);
        }
    }
}
