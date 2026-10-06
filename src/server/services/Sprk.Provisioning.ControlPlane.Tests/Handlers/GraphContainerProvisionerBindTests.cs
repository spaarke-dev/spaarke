// -----------------------------------------------------------------------------
// GraphContainerProvisionerBindTests.cs
//
// unified-access-control-r2 task 165, owner round 35 item 1 — H8 stamps the
// container it creates with its owning business unit, reads the stamp back, and
// REMOVES the container when the stamp did not land (the rule every SPE
// container-creation path follows; the BFF's admin plane reaches no unbound
// container). Owner round 49 item 2 — every fault after the container POST was
// sent is RETURNED saying whether a container may exist, never thrown, so H8
// records it and a resume never creates a second container.
//
// Re-homed at the batch-4 integration from GraphContainerTypeProvisionerBindTests
// (customer-provisioning-orchestration-r1 task 214 retired container-TYPE
// creation; the type-creation and list-and-adopt cases went with it).
//
// ADR-038 CATEGORY: contract test on the Graph wire shape. The REAL
// GraphContainerProvisioner runs against a REAL Microsoft.Graph GraphServiceClient
// built by SpeConfidentialClientGraphFactory's test seam, whose HttpClient is
// backed by a hand-written fake transport (this project's established pattern —
// never Mock<HttpMessageHandler>). "No DELETE was sent" is the absence of a
// recorded request, not a mock expectation.
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class GraphContainerProvisionerBindTests
{
    private const string ContainerId = "b!root-container";
    private const string ContainerTypeId = "cccccccc-dddd-eeee-ffff-000000000001";
    private const string OwningApp = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";
    private const string TenantId = "00000000-1111-2222-3333-444444444444";
    // THE one C# constant (source-linked into the BFF and L2) — the BFF authorizes against exactly this name.
    private const string StampProperty = Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName;
    private static readonly Guid Unit = Guid.Parse("0b0b0b0b-1111-2222-3333-444444444444");

    // ── The bind step (owner round 35 item 1) ──────────────────────────────────

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
        patch.HadBearerToken.Should().BeTrue("the bind is sent as the owning app (its app-only token)");
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

    [Fact]
    public async Task ARemovalThatFindsTheContainerAlreadyGone_CountsAsRemoved()
    {
        var graph = new FakeGraph
        {
            OnPatch = _ => Json(HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound","message":"gone"}}"""),
            OnDelete = _ => Json(HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound","message":"gone"}}"""),
        };

        var outcome = await Bind(graph, Unit);

        outcome.Should().BeOfType<SpeContainerBindOutcome.NotBound>().Which.Removed.Should().BeTrue(
            "nothing unbound remains — a resume must not keep binding a container that no longer exists");
    }

    // ── Creation: every fault after the POST is an outcome saying what may exist (owner round 49 item 2) ──

    [Fact]
    public async Task ACreatedAndActivatedContainer_IsASuccess_InThePreExistingType()
    {
        var graph = new FakeGraph
        {
            OnPost = r => IsActivate(r)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : Json(HttpStatusCode.Created, "{\"id\":\"" + ContainerId + "\",\"containerTypeId\":\"" + ContainerTypeId + "\"}"),
        };

        var outcome = await Provision(graph);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.Success>().Which.Outputs.ContainerId.Should().Be(ContainerId);
        var create = graph.Requests.Should().ContainSingle(r => r.Method == "POST" && r.Path == "/v1.0/storage/fileStorage/containers").Subject;
        using var body = JsonDocument.Parse(create.Body!);
        body.RootElement.GetProperty("containerTypeId").GetString().Should().Be(ContainerTypeId);
        graph.Requests.Should().ContainSingle(r => IsActivatePath(r.Path));
    }

    [Fact]
    public async Task AnODataErrorOnTheContainerPost_IsGraphsAnswer_NothingInDoubt_NothingActivated()
    {
        var graph = new FakeGraph
        {
            OnPost = _ => Json(HttpStatusCode.BadRequest, """{"error":{"code":"invalidRequest","message":"no"}}"""),
        };

        var outcome = await Provision(graph);

        var failure = outcome.Should().BeOfType<SpeContainerProvisionOutcome.CreateFailure>().Subject;
        failure.ContainerInDoubt.Should().BeFalse("Graph answered — nothing was created");
        graph.Requests.Should().NotContain(r => IsActivatePath(r.Path));
    }

    [Fact]
    public async Task ANonODataFaultOnTheContainerPost_IsAFailure_WithTheContainerInDoubt_NeverAnException()
    {
        var graph = new FakeGraph
        {
            OnPost = _ => throw new HttpRequestException("connection reset after the request was sent"),
        };

        var outcome = await Provision(graph);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.CreateFailure>().Which.ContainerInDoubt.Should().BeTrue(
            "Graph may have created the container — H8 must record that, or a resume creates a second one");
    }

    [Fact]
    public async Task AClientSideTimeoutOnTheContainerPost_IsAFailure_InDoubt_NotAnOperationCanceledException()
    {
        var graph = new FakeGraph
        {
            OnPost = _ => Json(HttpStatusCode.Created, "{\"id\":\"b!late\"}"),
            DelayPostsTo = "/containers",
            Delay = TimeSpan.FromSeconds(5),
        };

        var outcome = await Provision(graph, graphRequestTimeout: TimeSpan.FromMilliseconds(100));

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.CreateFailure>().Which.ContainerInDoubt.Should().BeTrue(
            "LinkedTimeout's OperationCanceledException leaves the container in doubt");
    }

    [Fact]
    public async Task TheCallersCancellationDuringTheContainerPost_IsAFailure_InDoubt_SoItIsStillRecorded()
    {
        using var cts = new CancellationTokenSource();
        var graph = new FakeGraph
        {
            OnPost = _ =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
        };

        var outcome = await Provision(graph, cancellationToken: cts.Token);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.CreateFailure>().Which.ContainerInDoubt.Should().BeTrue();
    }

    [Fact]
    public async Task A2xxWithoutAnId_IsAFailure_InDoubt()
    {
        var graph = new FakeGraph { OnPost = _ => Json(HttpStatusCode.Created, "{\"displayName\":\"acme\"}") };

        var outcome = await Provision(graph);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.CreateFailure>().Which.ContainerInDoubt.Should().BeTrue();
        graph.Requests.Should().ContainSingle(r => r.Method == "POST", "nothing is activated without an id");
    }

    [Fact]
    public async Task AFailedOwnerTokenExchange_Throws_BeforeAnyRequest_SoTheHandlerKnowsNothingWasCreated()
    {
        var graph = new FakeGraph { OnPost = _ => throw new InvalidOperationException("no request may be sent") };

        var act = () => Provision(graph, credential: new FailingCredential());

        await act.Should().ThrowAsync<AuthenticationFailedException>();
        graph.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]   // Graph answers with an error
    [InlineData(false)]  // the connection drops
    public async Task AnActivationFailure_ReportsTheCreatedContainer(bool odataAnswer)
    {
        var graph = new FakeGraph
        {
            OnPost = r => !IsActivate(r)
                ? Json(HttpStatusCode.Created, "{\"id\":\"" + ContainerId + "\"}")
                : odataAnswer
                    ? Json(HttpStatusCode.InternalServerError, """{"error":{"code":"generalException","message":"boom"}}""")
                    : throw new HttpRequestException("connection reset"),
        };

        var outcome = await Provision(graph);

        var failure = outcome.Should().BeOfType<SpeContainerProvisionOutcome.ActivateFailure>().Subject;
        failure.ContainerId.Should().Be(ContainerId, "the container exists — H8 records it so the resume activates it");
        failure.NoAnswer.Should().Be(!odataAnswer);
    }

    [Fact]
    public async Task ActivatingARecordedContainer_SendsOnlyTheActivation_NeverACreate()
    {
        var graph = new FakeGraph { OnPost = _ => new HttpResponseMessage(HttpStatusCode.OK) };
        var sut = Sut(graph);

        var outcome = await sut.ActivateAsync(
            new SpeContainerActivationRequest("acme", TenantId, OwningApp, ContainerId), CancellationToken.None);

        outcome.Should().BeOfType<SpeContainerProvisionOutcome.Success>().Which.Outputs.ContainerId.Should().Be(ContainerId);
        graph.Requests.Should().ContainSingle().Which.Path.Should().Be(
            $"/v1.0/storage/fileStorage/containers/{Uri.EscapeDataString(ContainerId)}/activate");
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static bool IsActivate(HttpRequestMessage r) => IsActivatePath(r.RequestUri!.AbsolutePath);

    private static bool IsActivatePath(string path) => path.EndsWith("/activate", StringComparison.Ordinal);

    private static GraphContainerProvisioner Sut(
        FakeGraph transport, TimeSpan? graphRequestTimeout = null, TokenCredential? credential = null)
    {
        var options = new SpeContainerOptions();
        if (graphRequestTimeout is { } timeout)
        {
            options.GraphRequestTimeout = timeout;
        }

        var tokens = credential ?? new FixedCredential();
        return new GraphContainerProvisioner(
            new SpeConfidentialClientGraphFactory((_, _) => tokens, transport),
            Options.Create(options),
            NullLogger<GraphContainerProvisioner>.Instance);
    }

    private static Task<SpeContainerProvisionOutcome> Provision(
        FakeGraph transport,
        TimeSpan? graphRequestTimeout = null,
        TokenCredential? credential = null,
        CancellationToken cancellationToken = default)
        => Sut(transport, graphRequestTimeout, credential).ProvisionAsync(
            new SpeContainerProvisionRequest(
                CustomerId: "acme", TenantId: TenantId, ContainerTypeId: ContainerTypeId, OwningAppId: OwningApp,
                DisplayName: "Spaarke Container - acme", Description: "SPE container for customer acme (run r1)"),
            cancellationToken);

    private static Task<SpeContainerBindOutcome> Bind(FakeGraph transport, Guid unit)
        => Sut(transport).BindRootContainerAsync(
            new SpeContainerBindRequest("acme", TenantId, OwningApp, ContainerId, unit), CancellationToken.None);

    private static string ContainerJson(string? stamp) =>
        stamp is null
            ? "{\"id\":\"" + ContainerId + "\",\"customProperties\":{}}"
            : "{\"id\":\"" + ContainerId + "\",\"customProperties\":{\"" + StampProperty + "\":{\"value\":\"" + stamp +
              "\",\"isSearchable\":false}}}";

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record RecordedRequest(string Method, string Path, string Query, string? Body, bool HadBearerToken);

    /// <summary>A hand-written fake Graph transport: one responder per verb; every request recorded.</summary>
    private sealed class FakeGraph : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPatch { get; init; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnGet { get; init; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnDelete { get; init; }
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPost { get; init; }

        /// <summary>POSTs whose path ends with this are delayed by <see cref="Delay"/> (honouring cancellation) — a slow Graph.</summary>
        public string? DelayPostsTo { get; init; }

        public TimeSpan Delay { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body,
                string.Equals(request.Headers.Authorization?.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)));
            if (DelayPostsTo is not null && request.Method == HttpMethod.Post
                && request.RequestUri.AbsolutePath.EndsWith(DelayPostsTo, StringComparison.Ordinal))
            {
                await Task.Delay(Delay, cancellationToken);
            }

            var responder = request.Method.Method switch
            {
                "PATCH" => OnPatch,
                "GET" => OnGet,
                "DELETE" => OnDelete,
                "POST" => OnPost,
                _ => null,
            };

            return responder is null
                ? throw new InvalidOperationException($"unexpected {request.Method} {request.RequestUri}")
                : responder(request);
        }
    }

    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("owner-app-graph-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    private sealed class FailingCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new AuthenticationFailedException("the Worker UAMI's federated credential exchange failed");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new AuthenticationFailedException("the Worker UAMI's federated credential exchange failed");
    }
}
