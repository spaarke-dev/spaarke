// -----------------------------------------------------------------------------
// GraphContainerProvisionerReuseTests.cs
//
// Task 227e — the Graph calls H8 needs to reuse a customer's container instead of creating a second one:
// FindCustomerContainersAsync, EnsureCustomerMarkerAsync and ActivateAsync on GraphContainerProvisioner. The real
// Microsoft Graph SDK runs against a routed fake HttpMessageHandler with a fake owning-app TokenCredential, both
// injected through SpeConfidentialClientGraphFactory's internal seam (ADR-038 — SDK marshaling runs; never
// Mock<HttpMessageHandler>). Pattern: GraphContainerProvisionerGrantTests.
//
//   L1  the listing is filtered by the container type and followed across pages; a candidate (H8's display name, or
//       H8's description for this customer — a renamed container) carrying this customer's marker is a match, an
//       unmarked one only with H8's description for this customer; another customer's marker, or unmarked with any
//       other description, is not; a container with neither name nor description is never read.
//   L2  no verdict is never "none found": a refused listing, a listing still unfinished at the page cap, a refused
//       read of a candidate, or a marker that is not a readable string is a Failure; a non-GUID type id is never
//       put into the filter.
//   M1  marker absent → PATCH /containers/{id}/customProperties with {spaarkeCustomerId:{value,isSearchable:false}}.
//   M2  marker already this customer's → GET only (a re-run writes nothing).
//   M3  marker naming another customer → Failure, nothing written.
//   M4  PATCH refused → Failure naming the verb and status.
//   A1  ActivateAsync posts /activate as the owning app; a refusal is ActivateFailure carrying the id; a request that
//       times out is a TimeoutException (H8 classifies it), never a bare cancellation.
// Marker-name parity with the BFF: tests/Spaarke.ArchTests/TenantIsolation/SpeContainerMarkerParityTests.cs.
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
    private const string ContainerTypeId = "fb3817a8-5a55-42ba-8cc9-12cf055168b8";
    private const string OwnerAppId = "bfac7f6e-9fa0-4664-8492-c7a1dfe73d5e";
    private const string CustomerId = "acme";
    private const string Name = "Spaarke Container - acme";
    private static readonly string H8Description = H8SpeContainerHandler.BuildContainerDescription(CustomerId);
    private const string ContainersPath = "/v1.0/storage/fileStorage/containers";
    private const string Denied = """{"error":{"code":"accessDenied","message":"Access denied."}}""";

    // ---------- L1 / L2 FindCustomerContainersAsync ----------

    [Fact]
    public async Task FindCustomerContainers_FollowsPages_MatchesOwnMarkerOrH8Description_SkipsTheRest()
    {
        var graph = new RoutedGraph(request => (request.Method, request.Path, request.Query) switch
        {
            ("GET", ContainersPath, var q) when q.Contains("skiptoken") => Ok(Page(null,
                Container("b!other-customer", Name), Container("b!unmarked-h8", Name.ToUpperInvariant()),
                Container("b!unmarked-foreign", Name), Container("b!renamed", "Acme Documents", H8Description))),
            ("GET", ContainersPath, _) => Ok(Page("https://graph.microsoft.com/v1.0/storage/fileStorage/containers?$skiptoken=p2",
                Container("b!mine", Name), Container("b!different-name", "Matter 123"))),
            ("GET", $"{ContainersPath}/b!mine", _) => Ok(WithMarker("b!mine", CustomerId)),
            ("GET", $"{ContainersPath}/b!other-customer", _) => Ok(WithMarker("b!other-customer", "globex")),
            ("GET", $"{ContainersPath}/b!unmarked-h8", _) => Ok(Read("b!unmarked-h8", H8Description)),
            ("GET", $"{ContainersPath}/b!unmarked-foreign", _) => Ok(Read("b!unmarked-foreign", "Globex documents")),
            ("GET", $"{ContainersPath}/b!renamed", _) => Ok(WithMarker("b!renamed", CustomerId)),
            _ => Unexpected(request),
        });

        var outcome = await Provisioner(graph).FindCustomerContainersAsync(LookupRequest(), CancellationToken.None);

        outcome.Should().BeEquivalentTo(new SpeContainerLookupOutcome.Found(
        [
            new SpeContainerMatch("b!mine", Name, CustomerId),
            new SpeContainerMatch("b!unmarked-h8", Name.ToUpperInvariant(), null),
            new SpeContainerMatch("b!renamed", "Acme Documents", CustomerId),
        ]), "an unmarked same-named container is the customer's only with H8's description for this customer");
        Uri.UnescapeDataString(graph.Requests[0].Query).Should().Contain($"$filter=containerTypeId eq {ContainerTypeId}");
        graph.Requests.Where(r => r.Path.StartsWith($"{ContainersPath}/", StringComparison.Ordinal))
            .Should().OnlyContain(r => Uri.UnescapeDataString(r.Query).Contains("$select=id,description,customProperties"),
                "customProperties is not in the default GET payload");
        graph.Requests.Should().NotContain(r => r.Path.EndsWith("b!different-name", StringComparison.Ordinal),
            "only candidates are read — the type holds every customer's containers");
        graph.Requests.Should().OnlyContain(r => r.Authorization == "Bearer owner-app-graph-token");
    }

    [Fact]
    public async Task FindCustomerContainers_UnreadableMarker_IsFailure()
    {
        var graph = new RoutedGraph(request => request.Path == ContainersPath
            ? Ok(Page(null, Container("b!mine", Name)))
            : Ok(JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["id"] = "b!mine",
                ["customProperties"] = new Dictionary<string, object> { [SpeContainerMarker.PropertyName] = "acme" },
            })));

        var outcome = await Provisioner(graph).FindCustomerContainersAsync(LookupRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerLookupOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("b!mine").And.Contain("cannot read");
    }

    [Fact]
    public async Task FindCustomerContainers_ContainerTypeIdNotAGuid_IsFailure_NothingSent()
    {
        var graph = new RoutedGraph(Unexpected);

        var outcome = await Provisioner(graph).FindCustomerContainersAsync(
            LookupRequest() with { ContainerTypeId = "x or 1 eq 1" }, CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerLookupOutcome.Failure>();
        graph.Requests.Should().BeEmpty("only a GUID may reach the OData filter");
    }

    [Fact]
    public async Task FindCustomerContainers_ListingRefused_IsFailure_NotNoneFound()
    {
        var graph = new RoutedGraph(_ => (HttpStatusCode.Forbidden, Denied));

        var outcome = await Provisioner(graph).FindCustomerContainersAsync(LookupRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerLookupOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("403");
    }

    [Fact]
    public async Task FindCustomerContainers_ListingUnfinishedAtThePageCap_IsFailure_NotNoneFound()
    {
        var graph = new RoutedGraph(_ => Ok(Page("https://graph.microsoft.com/v1.0/storage/fileStorage/containers?$skiptoken=again",
            Container("b!someone-else", "Matter 9"))));

        var outcome = await Provisioner(graph).FindCustomerContainersAsync(LookupRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerLookupOutcome.Failure>()
            .Which.Diagnostic.Should().Contain($"{GraphContainerProvisioner.MaxListingPages} pages");
        graph.Requests.Should().HaveCount(GraphContainerProvisioner.MaxListingPages);
    }

    [Fact]
    public async Task FindCustomerContainers_CandidateUnreadable_IsFailureNamingIt()
    {
        var graph = new RoutedGraph(request => request.Path == ContainersPath
            ? Ok(Page(null, Container("b!mine", Name)))
            : (HttpStatusCode.Forbidden, Denied));

        var outcome = await Provisioner(graph).FindCustomerContainersAsync(LookupRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerLookupOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("b!mine").And.Contain("403");
    }

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
        var marker = JsonDocument.Parse(patch.Body!).RootElement.GetProperty(SpeContainerMarker.PropertyName);
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
    public async Task EnsureCustomerMarker_PatchRefused_IsFailureNamingTheVerb()
    {
        var graph = new RoutedGraph(request => request.Method == "GET"
            ? Ok("""{"id":"b!mine"}""")
            : (HttpStatusCode.Forbidden, Denied));

        var outcome = await Provisioner(graph).EnsureCustomerMarkerAsync(MarkerRequest(), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerMarkerOutcome.Failure>()
            .Which.Diagnostic.Should().Contain("PATCH").And.Contain("403");
    }

    // ---------- A1 ActivateAsync ----------

    [Fact]
    public async Task Activate_PostsActivate_AsTheOwningApp()
    {
        var graph = new RoutedGraph(_ => (HttpStatusCode.NoContent, ""));

        var outcome = await Provisioner(graph).ActivateAsync(
            new SpeContainerActivationRequest(TenantId, OwnerAppId, "b!mine"), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.Success>();
        graph.Requests.Select(r => (r.Method, r.Path)).Should().Equal(("POST", $"{ContainersPath}/b!mine/activate"));
    }

    [Fact]
    public async Task Activate_Refused_IsActivateFailureCarryingTheId()
    {
        var graph = new RoutedGraph(_ => (HttpStatusCode.InternalServerError, """{"error":{"code":"generalException","message":"boom"}}"""));

        var outcome = await Provisioner(graph).ActivateAsync(
            new SpeContainerActivationRequest(TenantId, OwnerAppId, "b!mine"), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.ActivateFailure>()
            .Which.ContainerId.Should().Be("b!mine");
    }

    [Fact]
    public async Task Activate_RequestTimesOut_IsTimeoutException_NotACancellation()
    {
        // HttpClient reports its own timeout as a cancellation; H8 must see a fault it classifies (QuarantineRequired),
        // not a cancellation it lets escape with the run's state unwritten.
        var graph = new RoutedGraph(_ => throw new TaskCanceledException("The request was canceled due to the configured timeout."));

        var act = () => Provisioner(graph).ActivateAsync(
            new SpeContainerActivationRequest(TenantId, OwnerAppId, "b!mine"), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    // ---------- helpers ----------

    private static SpeContainerLookupRequest LookupRequest() => new(TenantId, ContainerTypeId, OwnerAppId, CustomerId, Name, H8Description);

    private static SpeContainerMarkerRequest MarkerRequest() => new(TenantId, OwnerAppId, "b!mine", CustomerId);

    private static GraphContainerProvisioner Provisioner(RoutedGraph graph) => new(
        new SpeConfidentialClientGraphFactory((_, _) => new FixedCredential(), graph),
        Options.Create(new SpeContainerOptions()),
        NullLogger<GraphContainerProvisioner>.Instance);

    private static (HttpStatusCode, string) Ok(string json) => (HttpStatusCode.OK, json);

    private static (HttpStatusCode, string) Unexpected(SentRequest request)
        => throw new InvalidOperationException($"Unexpected Graph request {request.Method} {request.Path}{request.Query}");

    private static object Container(string id, string displayName, string? description = null)
        => description is null
            ? new { id, displayName, containerTypeId = ContainerTypeId }
            : new { id, displayName, description, containerTypeId = ContainerTypeId };

    private static string Read(string id, string description) => JsonSerializer.Serialize(new { id, description });

    private static string Page(string? nextLink, params object[] containers) => nextLink is null
        ? JsonSerializer.Serialize(new { value = containers })
        : JsonSerializer.Serialize(new Dictionary<string, object> { ["value"] = containers, ["@odata.nextLink"] = nextLink });

    private static string WithMarker(string id, string marker) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["id"] = id,
        ["customProperties"] = new Dictionary<string, object>
        {
            [SpeContainerMarker.PropertyName] = new { value = marker, isSearchable = false },
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
