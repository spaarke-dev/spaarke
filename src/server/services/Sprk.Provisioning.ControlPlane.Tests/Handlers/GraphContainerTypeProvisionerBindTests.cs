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
//
// Owner round 41 item 1 (the resume): GraphContainerTypeProvisioner.CreateAsync,
// over the same transport, reuses a container type the run already created
// (ExistingContainerTypeId — no second, undeletable type) and reports the type
// it DID create when a later step fails (CreatedContainerTypeId), so H8 records it.
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

    // ── CreateAsync: the resume never makes a second container type (owner round 41 item 1) ─────────

    private const string ExistingType = "cccccccc-dddd-eeee-ffff-000000000001";
    private const string OwningApp = "77777777-8888-9999-aaaa-bbbbbbbbbbbb";

    [Fact]
    public async Task AnExistingContainerType_IsReused_OnlyARootContainerIsCreatedInIt()
    {
        var graph = new FakeGraph
        {
            // Owner round 49 item 2: the type is listed first — it holds no container, so one is created.
            OnGet = _ => Json(HttpStatusCode.OK, ListJson()),
            OnPost = r => r.RequestUri!.AbsolutePath.EndsWith("/containerTypeRegistrations", StringComparison.Ordinal)
                ? Json(HttpStatusCode.Conflict, """{"error":{"code":"conflict","message":"already registered"}}""")
                : Json(HttpStatusCode.Created, "{\"id\":\"b!new-root\",\"containerTypeId\":\"" + ExistingType + "\"}"),
        };

        var outcome = await Create(graph, existingContainerTypeId: ExistingType);

        var success = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Success>().Subject;
        success.Outputs.ContainerTypeId.Should().Be(ExistingType);
        success.Outputs.RootContainerId.Should().Be("b!new-root");
        graph.Requests.Should().NotContain(r => r.Method == "POST" && r.Path.EndsWith("/containerTypes", StringComparison.Ordinal),
            "a container type cannot be deleted — a resume must never create a second one");
        var create = graph.Requests.Should().ContainSingle(r => r.Method == "POST" && r.Path.EndsWith("/containers", StringComparison.Ordinal)).Subject;
        using var body = JsonDocument.Parse(create.Body!);
        body.RootElement.GetProperty("containerTypeId").GetString().Should().Be(ExistingType);
    }

    [Fact]
    public async Task AFailureAfterTheTypeWasCreated_ReportsTheType_SoTheHandlerRecordsIt()
    {
        var graph = new FakeGraph
        {
            OnPost = r =>
            {
                var path = r.RequestUri!.AbsolutePath;
                if (path.EndsWith("/containerTypes", StringComparison.Ordinal))
                {
                    return Json(HttpStatusCode.Created, "{\"id\":\"" + ExistingType + "\",\"name\":\"acme\"}");
                }

                return path.EndsWith("/containerTypeRegistrations", StringComparison.Ordinal)
                    ? Json(HttpStatusCode.Created, "{\"id\":\"" + ExistingType + "\"}")
                    : Json(HttpStatusCode.ServiceUnavailable, """{"error":{"code":"serviceNotAvailable","message":"try later"}}""");
            },
        };

        var outcome = await Create(graph, existingContainerTypeId: null);

        var failure = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Failure>().Subject;
        failure.CreatedContainerTypeId.Should().Be(ExistingType,
            "the type exists — H8 records it so the resume creates only the root container in it");
        failure.IsDelegatedTokenTrap.Should().BeFalse();
    }

    // ── Owner round 49 item 2: no fault leaves an orphan type or an unbound root ───────────────────

    private static HttpResponseMessage CreatedType() => Json(HttpStatusCode.Created, "{\"id\":\"" + ExistingType + "\",\"name\":\"acme\"}");

    private static HttpResponseMessage Registered() => Json(HttpStatusCode.Created, "{\"id\":\"" + ExistingType + "\"}");

    private static bool IsTypePost(HttpRequestMessage r) => r.RequestUri!.AbsolutePath.EndsWith("/containerTypes", StringComparison.Ordinal);

    private static bool IsRegistrationPost(HttpRequestMessage r) =>
        r.RequestUri!.AbsolutePath.EndsWith("/containerTypeRegistrations", StringComparison.Ordinal);

    [Fact]
    public async Task ANonODataFaultOnTheRootContainerPost_IsAFailure_ReportingTheTypeAndTheContainerInDoubt_NeverAnException()
    {
        // The verifier's probe: type 201, registration 201, the container POST's connection drops.
        var graph = new FakeGraph
        {
            OnPost = r => IsTypePost(r) ? CreatedType()
                : IsRegistrationPost(r) ? Registered()
                : throw new HttpRequestException("connection reset after the request was sent"),
        };

        var outcome = await Create(graph, existingContainerTypeId: null);

        var failure = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Failure>().Subject;
        failure.CreatedContainerTypeId.Should().Be(ExistingType, "the type exists — H8 must record it, or a resume makes a second");
        failure.RootContainerInDoubt.Should().BeTrue("Graph may have created the container — the resume lists the type first");
        failure.ContainerTypeInDoubt.Should().BeFalse();
    }

    [Fact]
    public async Task AClientSideTimeoutOnTheRootContainerPost_IsAFailure_InDoubt_NotAnOperationCanceledException()
    {
        var graph = new FakeGraph
        {
            OnPost = r => IsTypePost(r) ? CreatedType() : IsRegistrationPost(r) ? Registered() : Json(HttpStatusCode.Created, "{\"id\":\"b!late\"}"),
            DelayPostsTo = "/containers",
            Delay = TimeSpan.FromSeconds(5),
        };

        var outcome = await Create(graph, existingContainerTypeId: null, graphRequestTimeout: TimeSpan.FromMilliseconds(100));

        var failure = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Failure>().Subject;
        failure.CreatedContainerTypeId.Should().Be(ExistingType);
        failure.RootContainerInDoubt.Should().BeTrue("LinkedTimeout's OperationCanceledException leaves the container in doubt");
    }

    [Fact]
    public async Task TheCallersCancellationDuringTheRootContainerPost_IsAFailure_InDoubt_SoItIsStillRecorded()
    {
        using var cts = new CancellationTokenSource();
        var graph = new FakeGraph
        {
            OnPost = r =>
            {
                if (IsTypePost(r)) return CreatedType();
                if (IsRegistrationPost(r)) return Registered();
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            },
        };

        var outcome = await Create(graph, existingContainerTypeId: null, cancellationToken: cts.Token);

        var failure = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Failure>().Subject;
        failure.CreatedContainerTypeId.Should().Be(ExistingType);
        failure.RootContainerInDoubt.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]   // the connection drops
    [InlineData(false)]  // Graph answers 201 without an id
    public async Task AContainerTypePostWithNoAnswer_IsAFailure_WithTheTypeInDoubt(bool drops)
    {
        var graph = new FakeGraph
        {
            OnPost = r => IsTypePost(r)
                ? drops ? throw new HttpRequestException("connection reset") : Json(HttpStatusCode.Created, "{\"name\":\"acme\"}")
                : throw new InvalidOperationException("nothing after the type POST may be sent"),
        };

        var outcome = await Create(graph, existingContainerTypeId: null);

        var failure = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Failure>().Subject;
        failure.ContainerTypeInDoubt.Should().BeTrue("a container type may exist that no one names — H8 quarantines");
        failure.CreatedContainerTypeId.Should().BeNull();
        graph.Requests.Should().ContainSingle(r => r.Method == "POST");
    }

    [Fact]
    public async Task AnODataErrorOnTheRootContainerPost_IsGraphsAnswer_NothingInDoubt()
    {
        var graph = new FakeGraph
        {
            OnPost = r => IsTypePost(r) ? CreatedType() : IsRegistrationPost(r) ? Registered()
                : Json(HttpStatusCode.BadRequest, """{"error":{"code":"invalidRequest","message":"no"}}"""),
        };

        var outcome = await Create(graph, existingContainerTypeId: null);

        var failure = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Failure>().Subject;
        failure.CreatedContainerTypeId.Should().Be(ExistingType);
        failure.RootContainerInDoubt.Should().BeFalse();
        failure.ContainerTypeInDoubt.Should().BeFalse();
    }

    [Fact]
    public async Task OnAResume_AContainerAlreadyInTheType_IsAdopted_AndNothingIsCreated()
    {
        var graph = new FakeGraph
        {
            OnGet = _ => Json(HttpStatusCode.OK, ListJson(("b!found", "2026-10-05T01:00:00Z"))),
            OnPost = _ => throw new InvalidOperationException("an existing container must be adopted, not created again"),
        };

        var outcome = await Create(graph, existingContainerTypeId: ExistingType);

        var success = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Success>().Subject;
        success.Outputs.RootContainerId.Should().Be("b!found");
        success.Outputs.Adopted.Should().BeTrue();
        success.Outputs.AdditionalContainerIds.Should().BeEmpty();
        var list = graph.Requests.Should().ContainSingle(r => r.Method == "GET").Subject;
        list.Path.Should().Be("/v1.0/storage/fileStorage/containers");
        Uri.UnescapeDataString(list.Query).Should().Contain($"containerTypeId eq {ExistingType}");
    }

    [Fact]
    public async Task OnAResume_SeveralContainersInTheType_TheOldestIsTheRoot_TheOthersAreReportedForBinding_AcrossPages()
    {
        var graph = new FakeGraph
        {
            OnGet = r => r.RequestUri!.Query.Contains("skiptoken", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, ListJson(("b!older", "2026-10-05T01:00:00Z")))
                : Json(HttpStatusCode.OK, ListJson(nextLink: $"{BaseUrl}/storage/fileStorage/containers?$skiptoken=p2",
                    ("b!newer", "2026-10-05T02:00:00Z"), ("b!newest", "2026-10-05T03:00:00Z"))),
        };

        var outcome = await Create(graph, existingContainerTypeId: ExistingType);

        var outputs = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Success>().Subject.Outputs;
        outputs.RootContainerId.Should().Be("b!older", "the container found on the second page is the oldest");
        outputs.AdditionalContainerIds.Should().Equal("b!newer", "b!newest");
        graph.Requests.Should().NotContain(r => r.Method == "POST");
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]        // the type lists nothing
    [InlineData(HttpStatusCode.NotFound)]  // the type is not visible yet (replication)
    public async Task OnAResume_ARootContainerInDoubt_ThatIsNotListedYet_IsWaitedFor_NotCreatedAgain(HttpStatusCode listStatus)
    {
        var graph = new FakeGraph
        {
            OnGet = _ => listStatus == HttpStatusCode.OK
                ? Json(HttpStatusCode.OK, ListJson())
                : Json(HttpStatusCode.NotFound, """{"error":{"code":"itemNotFound","message":"not yet"}}"""),
            OnPost = _ => throw new InvalidOperationException("no root container may be created while one may exist unseen"),
        };

        var outcome = await Create(graph, existingContainerTypeId: ExistingType, rootContainerCreationInDoubt: true);

        outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.RootContainerNotYetVisible>()
            .Which.ContainerTypeId.Should().Be(ExistingType);
    }

    [Fact]
    public async Task OnAResume_AListThatFails_CreatesNothing_AndReportsTheType()
    {
        var graph = new FakeGraph
        {
            OnGet = _ => Json(HttpStatusCode.ServiceUnavailable, """{"error":{"code":"serviceNotAvailable","message":"later"}}"""),
            OnPost = _ => throw new InvalidOperationException("a type H8 cannot see into must not get a new container"),
        };

        var outcome = await Create(graph, existingContainerTypeId: ExistingType);

        var failure = outcome.Should().BeOfType<SpeContainerTypeProvisionOutcome.Failure>().Subject;
        failure.CreatedContainerTypeId.Should().Be(ExistingType);
        failure.RootContainerInDoubt.Should().BeFalse("a failed READ creates nothing");
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

    // ── helpers ────────────────────────────────────────────────────────────────

    private static string ListJson(params (string Id, string Created)[] containers) => ListJson(nextLink: null, containers);

    private static string ListJson(string? nextLink, params (string Id, string Created)[] containers)
    {
        var items = string.Join(",", containers.Select(c =>
            "{\"id\":\"" + c.Id + "\",\"containerTypeId\":\"" + ExistingType + "\",\"createdDateTime\":\"" + c.Created + "\"}"));
        return "{\"value\":[" + items + "]" + (nextLink is null ? string.Empty : ",\"@odata.nextLink\":\"" + nextLink + "\"") + "}";
    }

    private static Task<SpeContainerTypeProvisionOutcome> Create(
        FakeGraph transport,
        string? existingContainerTypeId,
        bool rootContainerCreationInDoubt = false,
        TimeSpan? graphRequestTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var options = new SpeContainerTypeOptions();
        if (graphRequestTimeout is { } timeout)
        {
            options.GraphRequestTimeout = timeout;
        }

        var sut = new GraphContainerTypeProvisioner(
            new UnusableCredential(),
            Options.Create(options),
            NullLogger<GraphContainerTypeProvisioner>.Instance);
        var graph = new GraphServiceClient(new HttpClient(transport), new AnonymousAuthenticationProvider(), BaseUrl);
        return sut.CreateAsync(graph, new SpeContainerTypeProvisionRequest(
            CustomerId: "acme", TenantId: "00000000-1111-2222-3333-444444444444", OwningAppId: OwningApp,
            SharePointDomain: "acme.sharepoint.com", VaultName: "kv", CertSecretName: "cert", DisplayName: "acme",
            ExistingContainerTypeId: existingContainerTypeId,
            RootContainerCreationInDoubt: rootContainerCreationInDoubt), cancellationToken);
    }

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
        public Func<HttpRequestMessage, HttpResponseMessage>? OnPost { get; init; }

        /// <summary>POSTs whose path ends with this are delayed by <see cref="Delay"/> (honouring cancellation) — a slow Graph.</summary>
        public string? DelayPostsTo { get; init; }

        public TimeSpan Delay { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, body));
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

    private sealed class UnusableCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The bind step under test takes its Graph client as a parameter.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new InvalidOperationException("The bind step under test takes its Graph client as a parameter.");
    }
}
