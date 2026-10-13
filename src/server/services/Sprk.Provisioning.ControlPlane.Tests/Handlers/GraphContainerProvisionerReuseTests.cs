// -----------------------------------------------------------------------------
// GraphContainerProvisionerReuseTests.cs
//
// Task 227e — the Graph calls H8 adds for "one root container per customer, ever": EnsureCustomerMarkerAsync (the
// spaarkeCustomerId marker the customer's BFF recognises its containers by, T227d) and the bind of a REUSED container
// (SpeContainerBindRequest.RemoveIfNotBound = false — the customer's existing container is never deleted). The real
// Microsoft Graph SDK runs against a routed fake HttpMessageHandler with a fake owning-app TokenCredential, both injected
// through SpeConfidentialClientGraphFactory's internal seam (ADR-038 — SDK marshaling runs; never
// Mock<HttpMessageHandler>). Pattern: GraphContainerProvisionerGrantTests.
//
//   M1  marker absent → PATCH /containers/{id}/customProperties with {spaarkeCustomerId:{value,isSearchable:false}}.
//   M2  marker already this customer's → GET only (a re-run writes nothing).
//   M3  marker naming another customer, or not a readable string → Failure, nothing written.
//   M4  PATCH refused → Failure naming the verb and status.
//   B1  a reused container whose stamp does not land is NOT deleted (a new one still is — GraphContainerProvisionerBindTests).
// Marker-name single source: src/server/shared/Contracts/SpeContainerCustomerMarker.cs (ArchTest SpeContainerMarkerParityTests).
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

public sealed class GraphContainerProvisionerReuseTests
{
    private const string TenantId = "11111111-2222-3333-4444-555555555555";
    private const string OwnerAppId = "bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e";
    private const string CustomerId = "acme";
    private const string ContainersPath = "/v1.0/storage/fileStorage/containers";
    private const string Marker = Spaarke.Contracts.Spe.SpeContainerCustomerMarker.PropertyName;
    private const string Denied = """{"error":{"code":"accessDenied","message":"Access denied."}}""";

    // ---------- M1..M4 EnsureCustomerMarkerAsync ----------

    [Fact]
    public async Task EnsureCustomerMarker_Absent_IsPatched_OnTheCustomPropertiesSubResource()
    {
        var graph = new RoutedGraph(request => request.Method switch
        {
            "GET" => Ok("""{"id":"b!mine","customProperties":{"other":{"value":"x"}}}"""),
            "PATCH" => (HttpStatusCode.Created, "{}"),
            _ => Unexpected(request),
        });

        var outcome = await Provisioner(graph).EnsureCustomerMarkerAsync(MarkerRequest(), CancellationToken.None);

        outcome.Should().Be(new SpeContainerMarkerOutcome.Success(Written: true));
        var patch = graph.Requests.Should().ContainSingle(r => r.Method == "PATCH").Subject;
        patch.Path.Should().Be($"{ContainersPath}/b!mine/customProperties");
        var marker = JsonDocument.Parse(patch.Body!).RootElement.GetProperty(Marker);
        marker.GetProperty("value").GetString().Should().Be(CustomerId);
        marker.GetProperty("isSearchable").GetBoolean().Should().BeFalse();
        JsonDocument.Parse(patch.Body!).RootElement.EnumerateObject().Should().ContainSingle("the PATCH merges — other properties are untouched");
    }

    [Fact]
    public async Task EnsureCustomerMarker_AlreadyThisCustomers_WritesNothing()
    {
        var graph = new RoutedGraph(request => request.Method == "GET" ? Ok(WithMarker("b!mine", CustomerId)) : Unexpected(request));

        var outcome = await Provisioner(graph).EnsureCustomerMarkerAsync(MarkerRequest(), CancellationToken.None);

        outcome.Should().Be(new SpeContainerMarkerOutcome.Success(Written: false));
        graph.Requests.Should().OnlyContain(r => r.Method == "GET");
    }

    [Fact]
    public async Task EnsureCustomerMarker_AnotherCustomers_IsFailure_NothingWritten()
    {
        var graph = new RoutedGraph(request => request.Method == "GET" ? Ok(WithMarker("b!mine", "globex")) : Unexpected(request));

        var outcome = await Provisioner(graph).EnsureCustomerMarkerAsync(MarkerRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerMarkerOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("different customer").And.NotContain("globex");
        graph.Requests.Should().OnlyContain(r => r.Method == "GET");
    }

    [Fact]
    public async Task EnsureCustomerMarker_Unreadable_IsFailure_NothingWritten()
    {
        var graph = new RoutedGraph(request => request.Method == "GET"
            ? Ok(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["id"] = "b!mine",
                ["customProperties"] = new Dictionary<string, object> { [Marker] = "acme" },
            }))
            : Unexpected(request));

        var outcome = await Provisioner(graph).EnsureCustomerMarkerAsync(MarkerRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerMarkerOutcome.Failure>().Which.Diagnostic.Should().Contain("cannot read");
        graph.Requests.Should().OnlyContain(r => r.Method == "GET");
    }

    [Fact]
    public async Task EnsureCustomerMarker_PatchRefused_IsFailureNamingTheVerb()
    {
        var graph = new RoutedGraph(request => request.Method == "GET"
            ? Ok("""{"id":"b!mine"}""")
            : (HttpStatusCode.Forbidden, Denied));

        var outcome = await Provisioner(graph).EnsureCustomerMarkerAsync(MarkerRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerMarkerOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("PATCH").And.Contain("403");
    }

    // ---------- B1 bind of a reused container ----------

    [Fact]
    public async Task Bind_ReusedContainerWhoseStampDidNotLand_IsNotDeleted()
    {
        var graph = new RoutedGraph(request => request.Method switch
        {
            "PATCH" => (HttpStatusCode.InternalServerError, """{"error":{"code":"generalException","message":"boom"}}"""),
            _ => Unexpected(request),
        });

        var outcome = await Provisioner(graph).BindRootContainerAsync(
            new SpeContainerBindRequest(CustomerId, TenantId, OwnerAppId, "b!mine", Guid.NewGuid(), RemoveIfNotBound: false),
            CancellationToken.None);

        var notBound = outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Subject;
        notBound.Removed.Should().BeFalse();
        notBound.Diagnostic.Should().Contain("NOT removed");
        graph.Requests.Should().NotContain(r => r.Method == "DELETE", "the customer's existing container holds their files");
    }

    // ---------- helpers ----------

    private static SpeContainerMarkerRequest MarkerRequest() => new(TenantId, OwnerAppId, "b!mine", CustomerId);

    private static GraphContainerProvisioner Provisioner(RoutedGraph graph) => new(
        new SpeConfidentialClientGraphFactory((_, _) => new FixedCredential(), graph),
        Options.Create(new SpeContainerOptions()),
        NullLogger<GraphContainerProvisioner>.Instance);

    private static (HttpStatusCode, string) Ok(string json) => (HttpStatusCode.OK, json);

    private static (HttpStatusCode, string) Unexpected(SentRequest request)
        => throw new InvalidOperationException($"Unexpected Graph request {request.Method} {request.Path}{request.Query}");

    private static string WithMarker(string id, string marker) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["id"] = id,
        ["customProperties"] = new Dictionary<string, object>
        {
            [Marker] = new { value = marker, isSearchable = false },
        },
    });

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("owner-app-graph-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed record SentRequest(string Method, string Path, string Query, string? Body, string? Authorization);

    /// <summary>Stands in for Microsoft Graph: answers each request from <c>respond</c>; records requests (path unescaped).</summary>
    private sealed class RoutedGraph(Func<SentRequest, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var sent = new SentRequest(request.Method.Method, Uri.UnescapeDataString(request.RequestUri!.AbsolutePath),
                request.RequestUri.Query, body, request.Headers.Authorization?.ToString());
            Requests.Add(sent);
            var (status, json) = respond(sent);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
