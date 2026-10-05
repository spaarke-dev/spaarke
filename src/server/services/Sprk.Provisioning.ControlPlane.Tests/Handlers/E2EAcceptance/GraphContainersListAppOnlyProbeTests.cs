// -----------------------------------------------------------------------------
// GraphContainersListAppOnlyProbeTests.cs
//
// Task 248 — the production T6 Graph half: app-only, as the owning app,
// GET /storage/fileStorage/containers?$filter=containerTypeId eq {id}, following
// @odata.nextLink, looking for the run's container. The real Microsoft Graph SDK runs
// against a fake HttpMessageHandler injected through SpeConfidentialClientGraphFactory's
// internal seam (ADR-038 — SDK marshaling runs; never Mock<HttpMessageHandler>).
//
// COVERAGE: listed on a later page (nextLink followed, filter sent); absent; refused
// with the delegated-token trap phrase; refused without it; 404 (replication
// window); listing still unfinished at the page cap → no verdict, never "absent".
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers.E2EAcceptance;

public sealed class GraphContainersListAppOnlyProbeTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string OwnerAppId = "bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e";
    private const string ContainerTypeId = "fb3817a8-5a55-42ba-8cc9-12cf055168b8";
    private const string RunContainerId = "b!run-container";
    private const string NextLink = "https://graph.microsoft.com/v1.0/storage/fileStorage/containers?$skiptoken=page2";

    [Fact]
    public async Task RunContainerOnALaterPage_FollowsNextLink_Succeeds()
    {
        var graph = new FakeGraphHandler(request => request.RequestUri!.Query.Contains("skiptoken")
            ? Page(nextLink: null, "b!other-2", RunContainerId)
            : Page(NextLink, "b!other-1"));

        var result = await BuildProbe(graph).ProbeAsync(TenantId, OwnerAppId, ContainerTypeId, RunContainerId, CancellationToken.None);

        result.Should().BeOfType<T6GraphAppOnlyProbeResult.SucceededResult>();
        Uri.UnescapeDataString(graph.Requests[0].Query).Should().Contain($"$filter=containerTypeId eq {ContainerTypeId}");
        graph.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListingWithoutTheRunsContainer_ReturnsContainerAbsent()
    {
        var graph = new FakeGraphHandler(_ => Page(nextLink: null, "b!other-1", "b!other-2"));

        var result = await BuildProbe(graph).ProbeAsync(TenantId, OwnerAppId, ContainerTypeId, RunContainerId, CancellationToken.None);

        result.Should().BeOfType<T6GraphAppOnlyProbeResult.ContainerAbsentResult>()
            .Which.ListedCount.Should().Be(2);
    }

    [Fact]
    public async Task RefusedWithTheTrapPhrase_ReturnsDelegatedTokenTrap()
    {
        var graph = new FakeGraphHandler(_ => Error(HttpStatusCode.Forbidden, "Public client not allowed for this resource."));

        var result = await BuildProbe(graph).ProbeAsync(TenantId, OwnerAppId, ContainerTypeId, RunContainerId, CancellationToken.None);

        result.Should().BeOfType<T6GraphAppOnlyProbeResult.DelegatedTokenTrapDetectedResult>()
            .Which.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task RefusedWithoutTheTrapPhrase_ReturnsInfraFault()
    {
        var graph = new FakeGraphHandler(_ => Error(HttpStatusCode.Forbidden, "Access denied."));

        var result = await BuildProbe(graph).ProbeAsync(TenantId, OwnerAppId, ContainerTypeId, RunContainerId, CancellationToken.None);

        result.Should().BeOfType<T6GraphAppOnlyProbeResult.InfraFaultResult>()
            .Which.Diagnostic.Should().Contain("403");
    }

    [Fact]
    public async Task NotFound_ReturnsReplicationPending()
    {
        var graph = new FakeGraphHandler(_ => Error(HttpStatusCode.NotFound, "Not found."));

        var result = await BuildProbe(graph).ProbeAsync(TenantId, OwnerAppId, ContainerTypeId, RunContainerId, CancellationToken.None);

        result.Should().BeOfType<T6GraphAppOnlyProbeResult.ReplicationPendingResult>();
    }

    [Fact]
    public async Task ListingUnfinishedAtThePageCap_IsNoVerdict_NotAbsent()
    {
        var graph = new FakeGraphHandler(_ => Page(NextLink, "b!other"));

        var result = await BuildProbe(graph).ProbeAsync(TenantId, OwnerAppId, ContainerTypeId, RunContainerId, CancellationToken.None);

        result.Should().BeOfType<T6GraphAppOnlyProbeResult.InfraFaultResult>(
            "a truncated listing must not be read as 'container absent' — that would quarantine the run");
        graph.Requests.Should().HaveCount(GraphContainersListAppOnlyProbe.MaxPages);
    }

    // ---------- helpers ----------

    private static GraphContainersListAppOnlyProbe BuildProbe(FakeGraphHandler graph) => new(
        new SpeConfidentialClientGraphFactory((_, _) => new FixedCredential(), graph),
        Options.Create(new H13AcceptanceOptions { TrapVerifierTimeout = TimeSpan.FromSeconds(30) }),
        NullLogger<GraphContainersListAppOnlyProbe>.Instance);

    private static HttpResponseMessage Page(string? nextLink, params string[] containerIds)
    {
        var value = string.Join(",", containerIds.Select(id => $$"""{"id":"{{id}}","containerTypeId":"{{ContainerTypeId}}"}"""));
        var link = nextLink is null ? string.Empty : $$""","@odata.nextLink":"{{nextLink}}" """;
        return Json(HttpStatusCode.OK, $$$"""{"value":[{{{value}}}]{{{link}}}}""");
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string message) =>
        Json(status, $$$"""{"error":{"code":"accessDenied","message":"{{{message}}}"}}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("owner-app-graph-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Stands in for Microsoft Graph; records each request URI.</summary>
    private sealed class FakeGraphHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
