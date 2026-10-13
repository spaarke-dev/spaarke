// -----------------------------------------------------------------------------
// GraphContainerProvisionerGrantTests.cs
//
// Task 227b (G9) — GraphContainerProvisioner.EnsureGrantsAsync: as the container type's owning app, make
// the registration grant each customer BFF identity. The real Microsoft Graph SDK request runs against a
// scripted fake HttpMessageHandler with a fake owning-app TokenCredential, both injected through
// SpeConfidentialClientGraphFactory's internal seam (ADR-038 — SDK marshaling runs; never
// Mock<HttpMessageHandler>).
//
//   G1  missing grant (GET 404) → PUT .../applicationPermissionGrants/{appId} (Graph v1.0 create), body without
//       appId, as the owning app in the run's tenant.
//   G2  grant already matching → GET only, nothing written (a re-run of H8 changes nothing); "none" and an
//       empty list are the same grant.
//   G3  grant with different permissions → PATCH.
//   G4  Graph refuses the GET or the write → Failure naming the app; later grants not attempted.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class GraphContainerProvisionerGrantTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string ContainerTypeId = "fb3817a8-5a55-42ba-8cc9-12cf055168b8";
    private const string OwnerAppId = "bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e";
    private const string UamiClientId = "55555555-0000-0000-0000-0000000000a1";
    private const string BffAppId = "99999999-0000-0000-0000-00000000bf00";

    private static readonly string GrantsPath =
        $"/v1.0/storage/fileStorage/containerTypeRegistrations/{ContainerTypeId}/applicationPermissionGrants";

    private const string NotFound = """{"error":{"code":"itemNotFound","message":"Grant not found."}}""";

    [Fact]
    public async Task G1_MissingGrants_ArePut_AsTheOwningApp_WithoutAppIdInTheBody()
    {
        var graph = new ScriptedGraph(
            (HttpStatusCode.NotFound, NotFound), (HttpStatusCode.Created, Grant(UamiClientId, ["full"], [])),
            (HttpStatusCode.NotFound, NotFound), (HttpStatusCode.Created, Grant(BffAppId, [], ["full"])));
        var credentials = new List<(string, string)>();

        var outcome = await Provisioner(graph, credentials).EnsureGrantsAsync(Request(), CancellationToken.None);

        outcome.Should().BeEquivalentTo(new SpeContainerTypeGrantOutcome.Success([UamiClientId, BffAppId]));
        credentials.Distinct().Should().Equal(new[] { (TenantId, OwnerAppId) }, "only the owning app may change its registration");
        graph.Requests.Select(r => (r.Method, r.Path)).Should().Equal(
            ("GET", $"{GrantsPath}/{UamiClientId}"), ("PUT", $"{GrantsPath}/{UamiClientId}"),
            ("GET", $"{GrantsPath}/{BffAppId}"), ("PUT", $"{GrantsPath}/{BffAppId}"));

        var uamiBody = JsonDocument.Parse(graph.Requests[1].Body!).RootElement;
        uamiBody.TryGetProperty("appId", out _).Should().BeFalse("Learn: don't include the appId in the body");
        Names(uamiBody, "applicationPermissions").Should().Equal("full");
        Names(uamiBody, "delegatedPermissions").Should().Equal(new[] { "none" }, "an empty set is sent as Graph reports it");
        var bffBody = JsonDocument.Parse(graph.Requests[3].Body!).RootElement;
        Names(bffBody, "applicationPermissions").Should().Equal("none");
        Names(bffBody, "delegatedPermissions").Should().Equal("full");
        graph.Requests.Should().OnlyContain(r => r.Authorization == "Bearer owner-app-graph-token");
    }

    [Fact]
    public async Task G2_GrantsAlreadyInPlace_NothingWritten_NoneEqualsEmpty()
    {
        var graph = new ScriptedGraph(
            (HttpStatusCode.OK, Grant(UamiClientId, ["full"], ["none"])),
            (HttpStatusCode.OK, Grant(BffAppId, ["none"], ["full"])));

        var outcome = await Provisioner(graph, []).EnsureGrantsAsync(Request(), CancellationToken.None);

        outcome.Should().BeEquivalentTo(new SpeContainerTypeGrantOutcome.Success([]));
        graph.Requests.Should().OnlyContain(r => r.Method == "GET", "a re-run of H8 writes nothing");
    }

    [Fact]
    public async Task G3_GrantWithDifferentPermissions_IsPatched()
    {
        var graph = new ScriptedGraph(
            (HttpStatusCode.OK, Grant(UamiClientId, ["readContent"], [])), (HttpStatusCode.OK, Grant(UamiClientId, ["full"], [])),
            (HttpStatusCode.OK, Grant(BffAppId, [], ["full"])));

        var outcome = await Provisioner(graph, []).EnsureGrantsAsync(Request(), CancellationToken.None);

        outcome.Should().BeEquivalentTo(new SpeContainerTypeGrantOutcome.Success([UamiClientId]));
        graph.Requests.Select(r => r.Method).Should().Equal("GET", "PATCH", "GET");
        Names(JsonDocument.Parse(graph.Requests[1].Body!).RootElement, "applicationPermissions").Should().Equal("full");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task G4_GraphRefuses_FailsNamingTheApp_LaterGrantsNotAttempted(bool refuseTheWrite)
    {
        const string denied = """{"error":{"code":"accessDenied","message":"Access denied."}}""";
        var graph = refuseTheWrite
            ? new ScriptedGraph((HttpStatusCode.NotFound, NotFound), (HttpStatusCode.Forbidden, denied))
            : new ScriptedGraph((HttpStatusCode.Forbidden, denied));

        var outcome = await Provisioner(graph, []).EnsureGrantsAsync(Request(), CancellationToken.None);

        var failure = outcome.Should().BeOfType<SpeContainerTypeGrantOutcome.Failure>().Subject;
        failure.AppId.Should().Be(UamiClientId);
        failure.Diagnostic.Should().Contain("403").And.Contain(refuseTheWrite ? "PUT" : "GET");
        graph.Requests.Should().OnlyContain(r => r.Path.EndsWith(UamiClientId, StringComparison.Ordinal));
    }

    // ---------- helpers ----------

    private static SpeContainerTypeGrantRequest Request() => new(
        TenantId, ContainerTypeId, OwnerAppId, H8SpeContainerHandler.BuildGrants(UamiClientId, BffAppId));

    private static GraphContainerProvisioner Provisioner(ScriptedGraph graph, List<(string, string)> credentials) => new(
        new SpeConfidentialClientGraphFactory((tenant, app) =>
        {
            credentials.Add((tenant, app));
            return new FixedCredential();
        }, graph),
        Options.Create(new SpeContainerOptions()),
        NullLogger<GraphContainerProvisioner>.Instance);

    private static string Grant(string appId, string[] application, string[] delegated) => JsonSerializer.Serialize(new
    {
        appId,
        applicationPermissions = application,
        delegatedPermissions = delegated,
    });

    private static List<string?> Names(JsonElement body, string property)
        => body.TryGetProperty(property, out var list) ? list.EnumerateArray().Select(e => e.GetString()).ToList() : [];

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("owner-app-graph-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed record SentRequest(string Method, string Path, string? Body, string? Authorization);

    /// <summary>Stands in for Microsoft Graph: answers each request with the next scripted response; records requests.</summary>
    private sealed class ScriptedGraph(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new(responses);

        public List<SentRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new SentRequest(request.Method.Method, request.RequestUri!.AbsolutePath, body,
                request.Headers.Authorization?.ToString()));
            var (status, json) = _responses.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
