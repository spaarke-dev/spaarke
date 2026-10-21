using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Polly.CircuitBreaker;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Infrastructure.Http;
using Sprk.Bff.Api.Services.SpeAdmin;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Contract.SpeAdmin;

/// <summary>
/// unified-access-control-r2 task 165, owner round 20 item 1 — BOTH BFF container-creation paths stamp the new container
/// with its owning business unit before returning, with the request shape Graph accepts, read it back, and remove a
/// container whose stamp did not land.
/// </summary>
/// <remarks>
/// <para>
/// The two paths: <see cref="SpeAdminGraphService.CreateContainerAsync"/> (the SPE admin plane) and
/// <see cref="ContainerOperations.CreateContainerAsync"/> (secure-record provisioning through <c>SpeFileStore</c>). The
/// stamp is <c>PATCH /storage/fileStorage/containers/{id}/customProperties</c> with the property map as the BODY ROOT —
/// the shape proven live on 2026-08-28 (a <c>{"customProperties":…}</c> wrapper on the container is a 400).
/// </para>
/// <para>Real services against the WireMock Graph (<see cref="GraphWireMockFixture"/>); ADR-038 contract KEEP path.</para>
/// </remarks>
public sealed class ContainerCreationStampContractTests : IDisposable
{
    private const string ContainersPath = "/storage/fileStorage/containers";
    private static readonly Guid Unit = Guid.Parse("1a000000-0000-0000-0000-000000000000");
    private static readonly Guid TypeId = Guid.Parse("77777777-0000-0000-0000-000000000077");

    private readonly GraphWireMockFixture _graph = new();

    public void Dispose() => _graph.Dispose();

    [Fact]
    public async Task AdminPlaneCreate_StampsTheUnit_AsTheBodyRootOfACustomPropertiesPatch_AndReadsItBack()
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        created.Id.Should().Be("c-new");
        AssertStamped(Unit);
        var readBack = _graph.RequestsFor($"{ContainersPath}/c-new").Single(r => r.Method == "GET");
        GraphWireMockFixture.ParseSelect(readBack.RawQuery).Should().BeEquivalentTo("id", "containerTypeId", "customProperties");
    }

    [Theory]
    [InlineData(500, true)]
    [InlineData(200, false)]
    public async Task AdminPlaneCreate_WhoseStampDidNotLand_SoftDeletesTheContainer_AndThrows(int patchStatus, bool readBackStamped)
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}", patchStatus);
        _graph.StubGet($"{ContainersPath}/c-new", Container(readBackStamped ? Unit : null));
        _graph.StubDelete($"{ContainersPath}/c-new");

        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<SpeAdminGraphService.ContainerBindingException>()).Which;
        ex.ContainerId.Should().Be("c-new");
        ex.Removed.Should().BeTrue();
        _graph.RequestsFor($"{ContainersPath}/c-new").Should().Contain(r => r.Method == "DELETE");
    }

    [Fact]
    public async Task AdminPlaneCreate_ActivatesTheNewContainer_BeforeStamping_AndReportsItActive()
    {
        // #2032: Graph creates a container inactive and deletes it after 24 hours unless it is activated. The admin
        // create used to rely on the stamp PATCH activating it implicitly, which nothing had measured.
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        ActivatePosts().Should().Be(1);
        var order = _graph.AllRequests.Select(r => $"{r.Method} {r.Path}").ToList();
        order.IndexOf($"POST {ActivatePath}").Should().BeLessThan(
            order.IndexOf($"PATCH {ContainersPath}/c-new/customProperties"), "the container is activated before it is stamped");
        created.Status.Should().Be("active", "the create response's 'inactive' predates the activation");
    }

    [Fact]
    public async Task AdminPlaneCreate_WhoseActivationFails_RemovesTheContainer_AndNeverStampsIt()
    {
        StubCreated();
        StubActivated(500);
        _graph.StubDelete($"{ContainersPath}/c-new");

        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<SpeAdminGraphService.ContainerBindingException>()).Which;
        ex.Removed.Should().BeTrue("an inactive container is not left behind for Graph to delete in 24 hours");
        _graph.PatchRequestsFor($"{ContainersPath}/c-new").Should().BeEmpty();
    }

    [Fact]
    public async Task AdminPlaneCreate_WhenGraphAnswers503AfterTheCreate_SendsItOnce_AndReportsTheOutcomeUnknown()
    {
        // #2032: through production's Graph resilience handler, which re-sends an unmarked request after a 503 — the
        // duplicate-container defect. The admin create is marked, so it is sent once and the outcome reported unknown.
        _graph.StubPostExact(ContainersPath, GraphError, 503);

        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(ResilienceHandler()), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        (await act.Should().ThrowAsync<ContainerCreateOutcomeUnknownException>()).Which.DisplayName.Should().Be("New");
        CreatePosts().Should().Be(1, "a create Graph may have committed is never re-sent");
    }

    [Fact]
    public async Task AdminPlaneCreate_WhenGraphAnswers429_RetriesTheCreate()
    {
        _graph.StubPostExactSequence(ContainersPath, (GraphError, 429), (CreatedBody, 201));
        StubActivated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(ResilienceHandler()), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        created.Id.Should().Be("c-new");
        CreatePosts().Should().Be(2, "Graph did not process a throttled create, so it is sent again");
    }

    [Fact]
    public async Task AdminPlaneCreate_WhenGraphRefusesTheCreate_IsNotAnUnknownOutcome()
    {
        _graph.StubPostExact(ContainersPath, GraphError, 400);

        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(ResilienceHandler()), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<ContainerCreateOutcomeUnknownException>();
        CreatePosts().Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"displayName\":\"New\",\"status\":\"inactive\"}")]
    public async Task AdminPlaneCreate_WhenGraphAnswers201WithNoContainer_ReportsTheOutcomeUnknown(string body)
    {
        // #2032 review: a 2xx is Graph committing the create; one naming no container is not "nothing was created". It
        // used to be an InvalidOperationException ("unexpected error") or a bind of container "" with an empty id.
        _graph.StubPostExact(ContainersPath, body, 201);

        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(ResilienceHandler()), TypeId.ToString(), "New", null, Unit, CancellationToken.None);

        await act.Should().ThrowAsync<ContainerCreateOutcomeUnknownException>();
        _graph.AllRequests.Should().OnlyContain(r => r.Method == "POST" && r.Path == ContainersPath,
            "nothing is activated, bound or deleted for a container with no id");
    }

    [Fact]
    public async Task AdminPlaneCreate_WhenTheCallerCancelsDuringTheCreatePost_StillReadsTheIdAndBindsIt()
    {
        // #2032 review, as ContainerOperations (#1735/#1755): a client disconnect mid-POST used to abort the read of the
        // only answer that names the new container. The caller cancels when the POST reaches Graph: no timer to race.
        using var caller = new CancellationTokenSource();
        _graph.StubPostExact(ContainersPath, CreatedBody, 201, onReceived: caller.Cancel);
        StubActivated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Unit, caller.Token);

        created.Id.Should().Be("c-new");
        caller.IsCancellationRequested.Should().BeTrue("the caller gave up while the create POST was in flight");
        _graph.RequestsFor($"{ContainersPath}/c-new").Select(r => r.Method).Should().Equal("POST", "PATCH", "GET");
    }

    [Fact]
    public async Task AdminPlaneCreate_WhenTheCallerHasCancelledBeforeTheCreate_SendsNothing()
    {
        StubCreated();
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Unit, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _graph.AllRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task AdminActivate_PostsTheActivateAction_NotAPatchOfTheReadOnlyStatus()
    {
        StubActivated();

        var activated = await AdminGraph().ActivateContainerAsync(_graph.CreateGraphClient(), "c-new", CancellationToken.None);

        activated.Should().BeTrue();
        ActivatePosts().Should().Be(1);
        _graph.AllRequests.Should().NotContain(r => r.Method == "PATCH");
    }

    [Fact]
    public async Task AdminLock_PostsTheLockAction_ReadOnly_NotAPatchOfTheReadOnlyStatus()
    {
        _graph.StubPostExact($"{ContainersPath}/c-new/lock", "", 204);

        var locked = await AdminGraph().LockContainerAsync(_graph.CreateGraphClient(), "c-new", CancellationToken.None);

        locked.Should().BeTrue();
        var post = _graph.AllRequests.Should().ContainSingle(r => r.Method == "POST").Subject;
        post.Path.Should().Be($"{ContainersPath}/c-new/lock");
        post.BodyAsJson().GetProperty("lockState").GetString().Should().Be("lockedReadOnly");
        _graph.AllRequests.Should().NotContain(r => r.Method == "PATCH");
    }

    [Fact]
    public async Task AdminUnlock_PostsTheUnlockAction_NotAPatchOfTheReadOnlyStatus()
    {
        _graph.StubPostExact($"{ContainersPath}/c-new/unlock", "", 204);

        var unlocked = await AdminGraph().UnlockContainerAsync(_graph.CreateGraphClient(), "c-new", CancellationToken.None);

        unlocked.Should().BeTrue();
        _graph.AllRequests.Should().ContainSingle(r => r.Method == "POST")
            .Which.Path.Should().Be($"{ContainersPath}/c-new/unlock");
        _graph.AllRequests.Should().NotContain(r => r.Method == "PATCH");
    }

    [Fact]
    public async Task AdminPlaneCreate_RefusesToCreateWithoutAnOwningUnit()
    {
        var act = () => AdminGraph().CreateContainerAsync(
            _graph.CreateGraphClient(), TypeId.ToString(), "New", null, Guid.Empty, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _graph.AllRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task ProvisioningCreate_StampsTheUnit_AndReadsItBack()
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        created!.Id.Should().Be("c-new");
        AssertStamped(Unit);

        // Then the ownership marker (task 227d) — its own PATCH, after the stamp read back, so a container whose
        // stamp did not land is never marked.
        var requests = _graph.RequestsFor($"{ContainersPath}/c-new").Select(r => r.Method).ToList();
        requests.Should().Equal("POST", "PATCH", "GET", "PATCH");
        var marker = _graph.PatchRequestsFor($"{ContainersPath}/c-new")
            .Single(p => p.Body!.Contains(SpeContainerOwnershipGuard.MarkerPropertyName));
        using var body = JsonDocument.Parse(marker.Body!);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(SpeContainerOwnershipGuard.MarkerPropertyName);
    }

    [Fact]
    public async Task ProvisioningCreate_WhoseStampDidNotLand_DeletesTheContainer_AndThrows()
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}", 500);
        _graph.StubDelete($"{ContainersPath}/c-new");

        var act = () => ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _graph.RequestsFor($"{ContainersPath}/c-new").Should().Contain(r => r.Method == "DELETE");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheCallerCancelsDuringTheBind_StillBindsAndMarks_NeverStrandsIt()
    {
        // #1652 round 1, same pattern: once the container exists, a client disconnect must not skip the bind (or its
        // compensation) — it used to throw TaskCanceledException and leave a live, unbound, unmarked container.
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}", delay: TimeSpan.FromSeconds(2));
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var created = await ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, caller.Token);

        created!.Id.Should().Be("c-new");
        caller.IsCancellationRequested.Should().BeTrue("the caller gave up while the stamp PATCH was in flight");
        _graph.RequestsFor($"{ContainersPath}/c-new").Select(r => r.Method).Should().Equal("POST", "PATCH", "GET", "PATCH");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheCallerCancelsDuringTheCreatePost_StillReadsTheIdBindsAndMarks()
    {
        // #1735/#1755: once the create POST is sent, Graph may commit the container. A client disconnect mid-POST used to
        // throw TaskCanceledException before the answer (the only place the new id is) was read — a container nothing
        // names, binds, marks or compensates.
        // The caller cancels when the POST reaches Graph, before Graph answers: no timer to race.
        using var caller = new CancellationTokenSource();
        _graph.StubPostExact(ContainersPath,
            "{\"id\":\"c-new\",\"displayName\":\"New\",\"containerTypeId\":\"" + TypeId + "\",\"status\":\"inactive\"}", 201,
            onReceived: caller.Cancel);
        StubActivated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, caller.Token);

        created!.Id.Should().Be("c-new");
        caller.IsCancellationRequested.Should().BeTrue("the caller gave up while the create POST was in flight");
        AssertStamped(Unit);
        _graph.RequestsFor($"{ContainersPath}/c-new").Select(r => r.Method).Should().Equal("POST", "PATCH", "GET", "PATCH");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheCallerHasCancelledBeforeTheCreate_SendsNothing()
    {
        // The other side of the boundary: before the POST is sent, the caller's token still stops the create.
        StubCreated();
        using var caller = new CancellationTokenSource();
        caller.Cancel();

        var act = () => ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _graph.AllRequests.Should().BeEmpty("a create the caller cancelled before it was sent must reach Graph not at all");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheBindTimesOut_DeletesTheContainer_AndThrows()
    {
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}", delay: TimeSpan.FromSeconds(5));
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit)); // it WOULD land — only the timeout fails it
        _graph.StubDelete($"{ContainersPath}/c-new");
        var operations = ProvisioningOperations();
        operations.UncancellableStepTimeout = TimeSpan.FromMilliseconds(500);

        var act = () => operations.CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _graph.RequestsFor($"{ContainersPath}/c-new").Should().Contain(r => r.Method == "DELETE",
            "a bind that never completes is compensated like any other failure");
    }

    [Fact]
    public async Task TheBindingOfADeletedContainer_IsReadFromTheRecycleBin()
    {
        _graph.StubGet("/storage/fileStorage/deletedContainers/c-gone", Container(Unit, "c-gone"));

        var read = await AdminGraph().GetContainerBindingAsync(_graph.CreateGraphClient(), "c-gone", deleted: true, CancellationToken.None);

        read!.Binding.Should().Be(SpeContainerBinding.BoundTo(Unit));
        _graph.SelectFieldsFor("/storage/fileStorage/deletedContainers/c-gone")
            .Should().BeEquivalentTo("id", "containerTypeId", "customProperties");
    }

    [Theory]
    [InlineData(404, false)]
    [InlineData(500, true)]
    public async Task ABindingRead_IsNullWhenAbsent_AndThrowsWhenGraphFails(int status, bool throws)
    {
        _graph.StubGet($"{ContainersPath}/c-x", """{"error":{"code":"x","message":"x"}}""", status);

        var act = () => AdminGraph().GetContainerBindingAsync(_graph.CreateGraphClient(), "c-x", deleted: false, CancellationToken.None);

        if (throws)
        {
            await act.Should().ThrowAsync<Exception>("a failed read must never be read as 'unbound'");
        }
        else
        {
            (await act()).Should().BeNull();
        }
    }

    [Fact]
    public async Task ProvisioningCreate_WhenGraphAnswers503AfterTheCreate_SendsItOnce_AndReportsTheOutcomeUnknown()
    {
        // #1755: through production's Graph resilience handler. A 503 may come after Graph committed the container, so
        // re-sending the create would make a second one; the first would never be seen. It is sent once, and the caller
        // is told the outcome is unknown, not that nothing was created.
        _graph.StubPostExact(ContainersPath, GraphError, 503);

        var act = () => ResilientProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<ContainerCreateOutcomeUnknownException>();
        CreatePosts().Should().Be(1, "a create Graph may have committed is never re-sent");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenGraphAnswers429_RetriesTheCreate()
    {
        // A 429 means Graph did not process the request, so the create is retried as before.
        _graph.StubPostExactSequence(ContainersPath, (GraphError, 429), (CreatedBody, 201));
        StubActivated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await ResilientProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        created!.Id.Should().Be("c-new");
        CreatePosts().Should().Be(2, "the throttled create is sent again");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenGraphRefusesTheCreate_IsNotAnUnknownOutcome()
    {
        // A 4xx is Graph refusing the request: nothing was created, and the caller may say so.
        _graph.StubPostExact(ContainersPath, GraphError, 400);

        var act = () => ResilientProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<ContainerCreateOutcomeUnknownException>();
        CreatePosts().Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"displayName\":\"New\",\"status\":\"inactive\"}")]
    public async Task ProvisioningCreate_WhenGraphAnswers201WithNoContainer_ReportsTheOutcomeUnknown(string body)
    {
        // #1755 round 2: a 2xx is Graph committing the create. With no body, or a body without an id, the container may
        // exist and nothing names it. It used to come back as null, which the endpoint reported as "Nothing needs undoing".
        _graph.StubPostExact(ContainersPath, body, 201);

        var act = () => ResilientProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<ContainerCreateOutcomeUnknownException>();
        CreatePosts().Should().Be(1);
        _graph.AllRequests.Should().OnlyContain(r => r.Method == "POST", "nothing is bound or deleted for a container with no id");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheCircuitIsOpen_IsNotAnUnknownOutcome()
    {
        // #1755 round 2: an open circuit throws before the create is sent, so nothing was created.
        _graph.StubGetExact("/probe", GraphError, 503);
        using var http = _graph.CreateHttpClient(ResilienceHandler(o => o.CircuitBreakerFailureThreshold = 1));
        var opened = () => http.GetAsync("/probe");
        await opened.Should().ThrowAsync<BrokenCircuitException>("one failure opens a circuit whose threshold is 1");
        var client = _graph.CreateGraphClient(http);

        var act = () => Operations(() => client).CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<ContainerCreateOutcomeUnknownException>();
        CreatePosts().Should().Be(0, "the open circuit refused the create before it was sent");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheTokenCannotBeAcquired_IsNotAnUnknownOutcome()
    {
        // #1755 round 2: the app-only token is acquired inside PostAsync, before the request is sent.
        StubCreated();
        var client = _graph.CreateGraphClient(
            _graph.CreateHttpClient(ResilienceHandler()), new BaseBearerTokenAuthenticationProvider(new FailingTokenProvider()));

        var act = () => Operations(() => client).CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<ContainerCreateOutcomeUnknownException>();
        CreatePosts().Should().Be(0);
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheTimeoutFiresDuringA429Backoff_IsNotAnUnknownOutcome()
    {
        // #1755 round 2: every answer so far was a 429, which Graph sends for a request it did not process. The timeout
        // fired while the handler waited out Retry-After, so nothing was created.
        _graph.StubPostExact(ContainersPath, GraphError, 429, retryAfterSeconds: 30);
        var operations = ResilientProvisioningOperations(o => { o.TimeoutSeconds = 1; o.HonorRetryAfterHeader = true; });

        var act = () => operations.CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<ContainerCreateOutcomeUnknownException>();
        CreatePosts().Should().Be(1, "the handler was still waiting out the first 429 when the timeout fired");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheConnectionDropsAfterTheCreateArrived_ReportsTheOutcomeUnknown()
    {
        // A transport fault after the create reached the server: a server that reads the request and closes the
        // connection without answering. (WireMock's faults still answer with a status line.)
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var baseUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptSocketAsync();
            var received = await socket.ReceiveAsync(new byte[8192], SocketFlags.None);
            socket.Close();
            return received;
        });
        var handler = ResilienceHandler();
        handler.InnerHandler = new HttpClientHandler();
        var client = new GraphServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri(baseUrl) }, new AnonymousAuthenticationProvider(), baseUrl);

        var act = () => Operations(() => client).CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<ContainerCreateOutcomeUnknownException>();
        (await server).Should().BePositive("the create reached the server before the connection dropped");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheTimeoutFiresAfterTheCreateWasSent_ReportsTheOutcomeUnknown()
    {
        _graph.StubPostExact(ContainersPath, CreatedBody, 201, delay: TimeSpan.FromSeconds(4));
        var operations = ResilientProvisioningOperations(o => o.TimeoutSeconds = 1);

        var act = () => operations.CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<ContainerCreateOutcomeUnknownException>();
    }

    [Fact]
    public async Task ProvisioningCreate_WhenAThrottledCreateThenGets503_ReportsTheOutcomeUnknown()
    {
        // The LAST attempt decides: a 429 then a 503 is unknown.
        _graph.StubPostExactSequence(ContainersPath, (GraphError, 429), (GraphError, 503));

        var act = () => ResilientProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<ContainerCreateOutcomeUnknownException>();
        CreatePosts().Should().Be(2);
    }

    [Fact]
    public async Task ProvisioningCreate_AnUnknownOutcome_IsNotLoggedAtErrorHere()
    {
        // K4 (#1755 round 2): the endpoint logs it once at Error, with what finds the container. This class used to log
        // it twice more (once where it was raised, once in its catch-all).
        _graph.StubPostExact(ContainersPath, GraphError, 503);
        var log = new CapturingLogger();

        var act = () => ResilientProvisioningOperations(logger: log).CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        await act.Should().ThrowAsync<ContainerCreateOutcomeUnknownException>();
        log.Levels.Should().NotContain(l => l >= LogLevel.Error);
    }

    [Fact]
    public async Task AnOrdinaryGraphGet_WhenGraphAnswers503_IsStillRetried()
    {
        // The opt-out is per request: every other Graph call keeps the handler's transient retry.
        _graph.StubGetExact($"{ContainersPath}/c-any", GraphError, 503);
        var handler = ResilienceHandler();
        handler.InnerHandler = new HttpClientHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri(_graph.BaseUrl) };

        using var response = await http.GetAsync($"{ContainersPath}/c-any");

        ((int)response.StatusCode).Should().Be(503);
        _graph.AllRequests.Count(r => r.Method == "GET").Should().Be(1 + RetryCount, "a GET is retried on 503");
    }

    // ── #1755 round 3 ────────────────────────────────────────────────────────

    [Fact]
    public async Task ProvisioningCreate_WhenTheBindFailsAndSoDoesItsDelete_ThrowsContainerLeftActive_WithTheId()
    {
        // F1 (#1755 round 3): the delete that undoes a failed bind failed too. That used to be a plain
        // InvalidOperationException saying "so it was removed", which the endpoint reported as "Nothing needs undoing"
        // while a live, unbound container remained.
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", GraphError, 500);
        _graph.StubDelete($"{ContainersPath}/c-new", GraphError, 500);

        var act = () => ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<ContainerLeftActiveException>()).Which;
        ex.ContainerId.Should().Be("c-new");
        ex.Message.Should().NotContain("so it was removed");
        _graph.RequestsFor($"{ContainersPath}/c-new").Should().Contain(r => r.Method == "DELETE");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheBindFailsAndItsDeleteSucceeds_IsNotContainerLeftActive()
    {
        // The other side: the container was removed, so nothing was left active.
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", GraphError, 500);
        _graph.StubDelete($"{ContainersPath}/c-new");

        var act = () => ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<ContainerLeftActiveException>();
    }

    [Theory]
    [InlineData(204, false)]
    [InlineData(500, true)]
    public async Task OwnershipMark_WhenItFails_SaysWhetherTheContainerWasLeftActive(int deleteStatus, bool leftActive)
    {
        // F1 (#1755 round 3), the guard's compensation: a mark whose delete also failed names the container left active.
        // Still an InvalidOperationException with the same message either way: the SPE admin create shares this path.
        _graph.StubPatch($"{ContainersPath}/c-x/customProperties", GraphError, 500);
        _graph.StubDelete($"{ContainersPath}/c-x", deleteStatus == 204 ? "" : GraphError, deleteStatus);
        var guard = TestSpeOwnership.Real(new WireMockGraphClientFactory(() => _graph.CreateGraphClient()));

        var act = () => guard.MarkOwnedAsync("c-x", CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        if (leftActive)
        {
            ex.Should().BeOfType<ContainerLeftActiveException>().Which.ContainerId.Should().Be("c-x");
            ex.Message.Should().Contain("Deleting it failed too");
        }
        else
        {
            ex.Should().NotBeOfType<ContainerLeftActiveException>();
            ex.Message.Should().Contain("was deleted again");
        }
    }

    [Fact]
    public async Task ProvisioningCreate_ActivatesTheContainerOnce_AfterTheCreate_BeforeTheBind()
    {
        // Item 5 (#1755 round 3): Graph creates a container inactive and deletes it after 24 hours unless it is activated.
        // The provisioning create used to rely on the stamp PATCH activating it implicitly; it now activates it.
        StubCreated();
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        await ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        ActivatePosts().Should().Be(1);
        _graph.AllRequests.Select(r => $"{r.Method} {r.Path}").Take(3).Should().Equal(
            $"POST {ContainersPath}", $"POST {ActivatePath}", $"PATCH {ContainersPath}/c-new/customProperties");
    }

    [Theory]
    [InlineData(204, false)]
    [InlineData(500, true)]
    public async Task ProvisioningCreate_WhenTheActivateFails_DeletesTheContainer_AndSaysWhetherItWasLeftActive(
        int deleteStatus, bool leftActive)
    {
        // Item 5: a failed activate is compensated like a failed bind — the container is deleted, and only a failed
        // delete leaves one active (named by its id).
        _graph.StubPostExact(ContainersPath, CreatedBody, 201);
        StubActivated(400);
        _graph.StubDelete($"{ContainersPath}/c-new", deleteStatus == 204 ? "" : GraphError, deleteStatus);

        var act = () => ProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
        if (leftActive)
        {
            ex.Should().BeOfType<ContainerLeftActiveException>().Which.ContainerId.Should().Be("c-new");
        }
        else
        {
            ex.Should().NotBeOfType<ContainerLeftActiveException>();
        }

        _graph.RequestsFor($"{ContainersPath}/c-new").Select(r => r.Method).Should().Equal(new[] { "POST", "DELETE" },
            "nothing is stamped or marked on a container that could not be activated");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheActivateGets503Then204_RetriesIt_AndSucceeds()
    {
        // Item 5: activate is idempotent, so it keeps the Graph handler's transient retry (no NoTransientRetryOption).
        _graph.StubPostExact(ContainersPath, CreatedBody, 201);
        _graph.StubPostExactSequence(ActivatePath, (GraphError, 503), ("", 204));
        _graph.StubPatch($"{ContainersPath}/c-new/customProperties", "{}");
        _graph.StubGet($"{ContainersPath}/c-new", Container(Unit));

        var created = await ResilientProvisioningOperations().CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        created.Id.Should().Be("c-new");
        ActivatePosts().Should().Be(2, "the 503 activate is sent again");
        CreatePosts().Should().Be(1);
        _graph.AllRequests.Should().NotContain(r => r.Method == "DELETE");
    }

    [Fact]
    public async Task ProvisioningCreate_WhenTheConnectionIsRefused_IsNotAnUnknownOutcome()
    {
        // K2 (#1755 round 3): a refused connect fails before any byte of the create is sent. The handler marked the
        // attempt in flight before sending, so this used to be reported as "a container may have been created".
        var baseUrl = $"http://127.0.0.1:{ClosedLoopbackPort()}";
        var handler = ResilienceHandler();
        handler.InnerHandler = new HttpClientHandler();
        var client = new GraphServiceClient(
            new HttpClient(handler) { BaseAddress = new Uri(baseUrl) }, new AnonymousAuthenticationProvider(), baseUrl);

        var act = () => Operations(() => client).CreateContainerAsync(TypeId, "Secure", Unit, null, CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<Exception>()).Which;
        ex.Should().NotBeOfType<ContainerCreateOutcomeUnknownException>();
        ex.Should().BeAssignableTo<HttpRequestException>("the connect was refused");
    }

    /// <summary>A loopback port nothing listens on: bound once to get a free one, then released.</summary>
    internal static int ClosedLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>The create answers 201 with <c>c-new</c>, and its activate answers 204 (#1755 round 3).</summary>
    private void StubCreated()
    {
        _graph.StubPostExact(ContainersPath,
            "{\"id\":\"c-new\",\"displayName\":\"New\",\"containerTypeId\":\"" + TypeId + "\",\"status\":\"inactive\"}", 201);
        StubActivated();
    }

    private const string ActivatePath = ContainersPath + "/c-new/activate";

    /// <summary><c>POST …/containers/c-new/activate</c> answers <paramref name="status"/> (Graph: 204, no body).</summary>
    private void StubActivated(int status = 204) => _graph.StubPostExact(ActivatePath, status == 204 ? "" : GraphError, status);

    private int ActivatePosts() => _graph.AllRequests.Count(r => r.Method == "POST" && r.Path == ActivatePath);

    private static string Container(Guid? unit, string id = "c-new") =>
        "{\"id\":\"" + id + "\",\"containerTypeId\":\"" + TypeId + "\",\"customProperties\":" +
        (unit is null ? "{}" : "{\"" + SpeContainerBusinessUnitStamp.PropertyName + "\":{\"value\":\"" + unit + "\",\"isSearchable\":false}}") + "}";

    private void AssertStamped(Guid unit)
    {
        // The stamp's own PATCH (the provisioning path then writes the ownership marker in a second one — task 227d).
        var patch = _graph.PatchRequestsFor($"{ContainersPath}/c-new")
            .Should().ContainSingle(p => p.Body!.Contains(SpeContainerBusinessUnitStamp.PropertyName)).Subject;
        patch.Path.Should().EndWith("/customProperties", "customProperties is its own sub-resource");
        using var body = JsonDocument.Parse(patch.Body!);
        body.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal(new[] { SpeContainerBusinessUnitStamp.PropertyName },
            "the stamp is the ONLY property written, as the body root — merge semantics leave every other property alone");
        var stamp = body.RootElement.GetProperty(SpeContainerBusinessUnitStamp.PropertyName);
        stamp.GetProperty("value").GetString().Should().Be(unit.ToString("D"));
        stamp.GetProperty("isSearchable").GetBoolean().Should().BeFalse();
    }

    private ContainerOperations ProvisioningOperations() => Operations(() => _graph.CreateGraphClient());

    private const int RetryCount = 3;

    private const string GraphError = "{\"error\":{\"code\":\"serviceNotAvailable\",\"message\":\"x\"}}";

    private static readonly string CreatedBody =
        "{\"id\":\"c-new\",\"displayName\":\"New\",\"containerTypeId\":\"" + TypeId + "\",\"status\":\"inactive\"}";

    /// <summary>
    /// Production's Graph resilience handler, with no backoff so retries are immediate. <paramref name="tune"/> adjusts
    /// the options (#1755 round 2: a short timeout, an honoured Retry-After, a circuit that opens at once).
    /// </summary>
    private static GraphHttpMessageHandler ResilienceHandler(Action<GraphResilienceOptions>? tune = null)
    {
        var options = new GraphResilienceOptions
        {
            RetryCount = RetryCount,
            RetryBackoffSeconds = 0,
            HonorRetryAfterHeader = false,
            TimeoutSeconds = 30,
            CircuitBreakerFailureThreshold = 100
        };
        tune?.Invoke(options);
        return new(NullLogger<GraphHttpMessageHandler>.Instance, Options.Create(options));
    }

    /// <summary>The provisioning create, sending through production's Graph resilience handler (#1755).</summary>
    private ContainerOperations ResilientProvisioningOperations(
        Action<GraphResilienceOptions>? tune = null, ILogger<ContainerOperations>? logger = null) =>
        Operations(() => _graph.CreateGraphClient(ResilienceHandler(tune)), logger);

    /// <summary>The provisioning create over the client <paramref name="client"/> builds.</summary>
    private static ContainerOperations Operations(Func<GraphServiceClient> client, ILogger<ContainerOperations>? logger = null)
    {
        var factory = new WireMockGraphClientFactory(client);
        return new(factory, TestSpeOwnership.AllowAll(factory), logger ?? NullLogger<ContainerOperations>.Instance);
    }

    private int CreatePosts() => _graph.AllRequests.Count(r => r.Method == "POST" && r.Path == ContainersPath);

    private static SpeAdminGraphService AdminGraph()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://unused.invalid" })
            .Build();

        return new SpeAdminGraphService(
            dataverseClient: new DataverseWebApiClient(configuration, NullLogger<DataverseWebApiClient>.Instance, new UnusableCredential()),
            configuration: configuration,
            logger: NullLogger<SpeAdminGraphService>.Instance);
    }

    /// <summary>A token provider that cannot acquire a token: the failure happens inside the SDK's send, before any request.</summary>
    private sealed class FailingTokenProvider : IAccessTokenProvider
    {
        public AllowedHostsValidator AllowedHostsValidator { get; } = new();

        public Task<string> GetAuthorizationTokenAsync(
            Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The app-only token could not be acquired (seeded).");
    }

    /// <summary>What <see cref="ContainerOperations"/> logged, by level.</summary>
    private sealed class CapturingLogger : ILogger<ContainerOperations>
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }

    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Not used: the client is passed in.");
    }

    private sealed class UnusableCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext r, CancellationToken c) =>
            throw new InvalidOperationException("No credential may be used by these tests.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext r, CancellationToken c) =>
            throw new InvalidOperationException("No credential may be used by these tests.");
    }
}
