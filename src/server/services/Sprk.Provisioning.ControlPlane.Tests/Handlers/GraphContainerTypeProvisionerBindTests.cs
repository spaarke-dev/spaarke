// -----------------------------------------------------------------------------
// GraphContainerTypeProvisionerBindTests.cs
//
// unified-access-control-r2 task 165, owner round 35 item 1 — H8 stamps the root
// container it creates with its owning business unit, reads the stamp back, and
// REMOVES the container when the stamp did not land (the rule every SPE
// container-creation path follows; the BFF's admin plane reaches no unbound
// container).
//
// ADR-038 CATEGORY: contract test on the Graph wire shape. The REAL
// GraphContainerTypeProvisioner.BindNewContainerAsync runs against a REAL
// Microsoft.Graph GraphServiceClient whose HttpClient is backed by a
// hand-written fake transport (this project's established pattern — never
// Mock<HttpMessageHandler>). "No DELETE was sent" is the absence of a recorded
// request, not a mock expectation.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class GraphContainerTypeProvisionerBindTests
{
    private const string BaseUrl = "https://graph.fake/v1.0";
    private const string ContainerId = "b!root-container";
    // THE one C# constant (source-linked into the BFF and L2) — the BFF authorizes against exactly this name.
    private const string StampProperty = Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName;
    private static readonly Guid Unit = Guid.Parse("0b0b0b0b-1111-2222-3333-444444444444");

    [Fact]
    public async Task ABoundContainer_GetsTheStampAsTheBodyRootOfACustomPropertiesPatch_ReadsItBack_AndIsKept()
    {
        var graph = new FakeGraph
        {
            OnPatch = _ => Json(HttpStatusCode.OK, "{}"),
            OnGet = _ => Json(HttpStatusCode.OK, ContainerJson(Unit.ToString("D"))),
        };

        var outcome = await Bind(graph, Unit);

        outcome.Should().BeOfType<SpeContainerBindOutcome.Bound>();
        var patch = graph.Requests.Should().ContainSingle(r => r.Method == "PATCH").Subject;
        patch.Path.Should().Be($"/v1.0/storage/fileStorage/containers/{Uri.EscapeDataString(ContainerId)}/customProperties");
        using (var body = JsonDocument.Parse(patch.Body!))
        {
            body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(new[] { StampProperty },
                "the property map is the body ROOT — Graph merges it, nothing else is touched");
            var stamp = body.RootElement.GetProperty(StampProperty);
            stamp.GetProperty("value").GetString().Should().Be(Unit.ToString("D"));
            stamp.GetProperty("isSearchable").GetBoolean().Should().BeFalse();
        }

        var get = graph.Requests.Should().ContainSingle(r => r.Method == "GET").Subject;
        Uri.UnescapeDataString(get.Query).Should().Contain("customProperties",
            "Graph returns customProperties only when a single-container GET selects it");
        graph.Requests.Should().NotContain(r => r.Method == "DELETE");
    }

    [Theory]
    [InlineData(null)]                                        // the PATCH was accepted but nothing reads back
    [InlineData("1b1b1b1b-0000-0000-0000-000000000000")]      // another unit reads back
    [InlineData("not-a-guid")]                                // a malformed stamp reads back
    public async Task AStampThatDoesNotReadBack_RemovesTheContainer(string? readBack)
    {
        var graph = new FakeGraph
        {
            OnPatch = _ => Json(HttpStatusCode.OK, "{}"),
            OnGet = _ => Json(HttpStatusCode.OK, ContainerJson(readBack)),
            OnDelete = _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        };

        var outcome = await Bind(graph, Unit);

        outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Which.Removed.Should().BeTrue();
        graph.Requests.Should().ContainSingle(r => r.Method == "DELETE")
            .Which.Path.Should().Be($"/v1.0/storage/fileStorage/containers/{Uri.EscapeDataString(ContainerId)}");
    }

    [Fact]
    public async Task AStampWriteThatFails_RemovesTheContainer()
    {
        var graph = new FakeGraph
        {
            OnPatch = _ => Json(HttpStatusCode.InternalServerError, """{"error":{"code":"generalException","message":"boom"}}"""),
            OnDelete = _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        };

        var outcome = await Bind(graph, Unit);

        var notBound = outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Subject;
        notBound.Removed.Should().BeTrue();
        notBound.Diagnostic.Should().Contain("500");
        graph.Requests.Should().ContainSingle(r => r.Method == "DELETE");
    }

    [Fact]
    public async Task WhenTheRemovalAlsoFails_TheOutcomeSaysTheContainerIsLeftUnbound()
    {
        var graph = new FakeGraph
        {
            OnPatch = _ => Json(HttpStatusCode.InternalServerError, """{"error":{"code":"generalException","message":"boom"}}"""),
            OnDelete = _ => Json(HttpStatusCode.InternalServerError, """{"error":{"code":"generalException","message":"no"}}"""),
        };

        var outcome = await Bind(graph, Unit);

        var notBound = outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Subject;
        notBound.Removed.Should().BeFalse();
        notBound.Diagnostic.Should().Contain("left unbound");
    }

    [Fact]
    public async Task NoOwner_WritesNoStamp_AndRemovesTheContainer()
    {
        var graph = new FakeGraph { OnDelete = _ => new HttpResponseMessage(HttpStatusCode.NoContent) };

        var outcome = await Bind(graph, Guid.Empty);

        outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Which.Removed.Should().BeTrue();
        graph.Requests.Should().NotContain(r => r.Method == "PATCH");
        graph.Requests.Should().ContainSingle(r => r.Method == "DELETE");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static Task<SpeContainerBindOutcome> Bind(FakeGraph transport, Guid unit)
    {
        var sut = new GraphContainerTypeProvisioner(
            new UnusableCredential(),
            Options.Create(new SpeContainerTypeOptions()),
            NullLogger<GraphContainerTypeProvisioner>.Instance);
        var graph = new GraphServiceClient(new HttpClient(transport), new AnonymousAuthenticationProvider(), BaseUrl);
        return sut.BindNewContainerAsync(graph, ContainerId, unit, CancellationToken.None);
    }

    private static string ContainerJson(string? stamp) =>
        stamp is null
            ? "{\"id\":\"" + ContainerId + "\",\"customProperties\":{}}"
            : "{\"id\":\"" + ContainerId + "\",\"customProperties\":{\"" + StampProperty + "\":{\"value\":\"" + stamp +
              "\",\"isSearchable\":false}}}";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(string Method, string Path, string Query, string? Body);

    /// <summary>A hand-written fake Graph transport: one responder per verb; every request recorded.</summary>
    private sealed class FakeGraph : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPatch { get; init; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnGet { get; init; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnDelete { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body));

            var responder = request.Method.Method switch
            {
                "PATCH" => OnPatch,
                "GET" => OnGet,
                "DELETE" => OnDelete,
                _ => null,
            };

            return responder is null
                ? throw new InvalidOperationException($"unexpected {request.Method} {request.RequestUri}")
                : responder(request);
        }
    }

    private sealed class UnusableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The bind step under test takes its Graph client as a parameter.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The bind step under test takes its Graph client as a parameter.");
    }
}
