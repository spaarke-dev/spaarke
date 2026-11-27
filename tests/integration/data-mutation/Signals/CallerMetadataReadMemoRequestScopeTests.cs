using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// ISS-010 fix 1 (#3184): the request-scoped table-metadata memo (<see cref="CallerMetadataReadMemo.RequestScope"/>) at the
/// caller-client boundary. Within one request, a successful <c>EntityDefinitions(…)</c> read is asked once across memos, across
/// DISTINCT <see cref="DataverseUserClient"/> instances (production resolves a fresh transient client per create) and across row
/// writes; a failure, a row, a catalog row and an access answer are asked every time; a different request shares nothing; any
/// client that is not the <see cref="DataverseUserClient"/> keeps the per-create memo even inside a request; and a create with no
/// scope (a create that does not go through <c>ChildRecordEndpoints.CreateAsync</c>, a job) keeps the per-create memo.
/// </summary>
/// <remarks>The clients here are the REAL <see cref="DataverseUserClient"/> over a counting wire (<see cref="RealCallerClientRig"/>), so
/// the type the production gate tests is the type under test.</remarks>
public sealed class CallerMetadataReadMemoRequestScopeTests
{
    private const string Metadata = "EntityDefinitions(LogicalName='sprk_matter')?$select=EntitySetName";

    private readonly RealCallerClientRig _rig = RealCallerClientRig.Create();
    private readonly CountingWire _wire = new();

    /// <summary>The caller's Dataverse wire, answered in memory and counted per Web API path.</summary>
    private sealed class CountingWire : HttpMessageHandler
    {
        public ConcurrentDictionary<string, int> Gets { get; } = new(StringComparer.Ordinal);

        /// <summary>Every GET in arrival order with the Authorization header it carried (the caller's Dataverse token).</summary>
        public ConcurrentQueue<(string Path, string? Authorization)> Calls { get; } = new();

        public Func<string, HttpStatusCode?>? Fault { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

            var original = request.RequestUri!.OriginalString;
            var path = original[(original.IndexOf("/api/data/v9.2/", StringComparison.Ordinal) + "/api/data/v9.2/".Length)..];
            Gets.AddOrUpdate(path, 1, (_, n) => n + 1);
            Calls.Enqueue((path, request.Headers.Authorization?.ToString()));
            return Task.FromResult(Fault?.Invoke(path) is { } status
                ? new HttpResponseMessage(status)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { EntitySetName = "sprk_matters" })),
                });
        }
    }

    /// <summary>A caller client that is NOT the <see cref="DataverseUserClient"/> (a test double standing for any other implementation).</summary>
    private sealed class OtherClient : IDataverseUserClient
    {
        public Dictionary<string, int> Gets { get; } = new(StringComparer.Ordinal);

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            Gets[relativePath] = Gets.GetValueOrDefault(relativePath) + 1;
            return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { EntitySetName = "sprk_matters" })));
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            Task.FromResult(DataverseUserResponse.Ok(204, null));
    }

    /// <summary>A request that carries the caller's bearer token, made the ambient request (what the real client reads its token from).</summary>
    private static DefaultHttpContext AmbientRequest()
    {
        var http = RealCallerClientRig.Request();
        new HttpContextAccessor().HttpContext = http;
        return http;
    }

    [Fact]
    public async Task InOneRequest_MemosOverTwoDistinctClients_ReadAMetadataPathOnce_EvenAcrossARowWrite()
    {
        var scope = CallerMetadataReadMemo.RequestScope.Of(AmbientRequest());
        var clientA = _rig.NewClient(_wire);
        var clientB = _rig.NewClient(_wire); // production resolves a fresh transient client per create
        clientB.Should().NotBeSameAs(clientA);

        var first = CallerMetadataReadMemo.For(clientA, scope);
        (await first.GetAsync(Metadata, default)).IsSuccess.Should().BeTrue();
        await first.PostAsync("/api/data/v9.2/sprk_todos", "{}", default); // a row write does not change a table's schema
        var answer = await CallerMetadataReadMemo.For(clientB, scope).GetAsync(Metadata, default);

        _wire.Gets[Metadata].Should().Be(1, "the second, distinct client's memo is answered from the request's store");
        answer.Body!.Value.GetProperty("EntitySetName").GetString().Should().Be("sprk_matters");
    }

    [Fact]
    public async Task InOneRequest_AFailedMetadataRead_IsAskedAgain()
    {
        var faults = 1;
        _wire.Fault = path => path == Metadata && faults-- > 0 ? HttpStatusCode.ServiceUnavailable : null;
        var scope = CallerMetadataReadMemo.RequestScope.Of(AmbientRequest());

        (await CallerMetadataReadMemo.For(_rig.NewClient(_wire), scope).GetAsync(Metadata, default)).IsSuccess.Should().BeFalse();
        (await CallerMetadataReadMemo.For(_rig.NewClient(_wire), scope).GetAsync(Metadata, default)).IsSuccess.Should().BeTrue();

        _wire.Gets[Metadata].Should().Be(2, "a failure is never remembered");
    }

    [Theory]
    [InlineData("sprk_recordtype_refs(c0430000-0000-4000-8000-000000000010)?$select=sprk_recordtype_refid")]
    [InlineData("sprk_matters(c0430000-0000-4000-8000-000000000011)?$select=sprk_matterid")]
    [InlineData("systemusers(c0430000-0000-4000-8000-000000000001)/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)?@p1=x")]
    [InlineData("systemusers(c0430000-0000-4000-8000-000000000001)/Microsoft.Dynamics.CRM.RetrieveUserSetOfPrivilegesByNames(PrivilegeNames=@p1)?@p1=x")]
    public async Task InOneRequest_ARowACatalogRowOrAnAccessQuestion_IsAskedEveryTime(string path)
    {
        var scope = CallerMetadataReadMemo.RequestScope.Of(AmbientRequest());

        await CallerMetadataReadMemo.For(_rig.NewClient(_wire), scope).GetAsync(path, default);
        await CallerMetadataReadMemo.For(_rig.NewClient(_wire), scope).GetAsync(path, default);

        _wire.Gets[path].Should().Be(2);
    }

    [Fact]
    public async Task TwoRequests_ShareNothing()
    {
        var one = AmbientRequest();
        var clientOne = _rig.NewClient(_wire);
        await CallerMetadataReadMemo.For(clientOne, CallerMetadataReadMemo.RequestScope.Of(one)).GetAsync(Metadata, default);

        var two = AmbientRequest();
        var clientTwo = _rig.NewClient(_wire);
        await CallerMetadataReadMemo.For(clientTwo, CallerMetadataReadMemo.RequestScope.Of(two)).GetAsync(Metadata, default);

        CallerMetadataReadMemo.RequestScope.Of(one).Should().NotBeSameAs(CallerMetadataReadMemo.RequestScope.Of(two));
        _wire.Gets[Metadata].Should().Be(2, "a second request asks again, whatever client it resolves");
    }

    [Fact]
    public async Task TwoRequestsOfTwoDifferentCallers_ShareNothing_AndEachCallCarriesItsOwnCallersToken()
    {
        var rigA = RealCallerClientRig.Create("bearer-caller-A");
        var rigB = RealCallerClientRig.Create("bearer-caller-B");

        var requestA = RealCallerClientRig.Request("bearer-caller-A");
        new HttpContextAccessor().HttpContext = requestA;
        var scopeA = CallerMetadataReadMemo.RequestScope.Of(requestA);
        var answerA = await CallerMetadataReadMemo.For(rigA.NewClient(_wire), scopeA).GetAsync(Metadata, default);

        var requestB = RealCallerClientRig.Request("bearer-caller-B");
        new HttpContextAccessor().HttpContext = requestB;
        var scopeB = CallerMetadataReadMemo.RequestScope.Of(requestB);
        var answerB = await CallerMetadataReadMemo.For(rigB.NewClient(_wire), scopeB).GetAsync(Metadata, default);

        scopeA.Should().NotBeSameAs(scopeB);
        answerA.IsSuccess.Should().BeTrue();
        answerB.IsSuccess.Should().BeTrue();
        _wire.Gets[Metadata].Should().Be(2, "caller B's request does not see caller A's answer: each asked Dataverse for itself");
        _wire.Calls.Select(c => c.Authorization).Should().Equal(
            new[]
            {
                "Bearer " + RealCallerClientRig.DataverseTokenFor("bearer-caller-A"),
                "Bearer " + RealCallerClientRig.DataverseTokenFor("bearer-caller-B"),
            },
            "each request's read went out with its own caller's Dataverse token, in order, and neither carried the other's");
    }

    [Fact]
    public async Task AClientThatIsNotTheDataverseUserClient_GetsNoSharedAnswers_EvenInARequestWithAScope()
    {
        var scope = CallerMetadataReadMemo.RequestScope.Of(AmbientRequest());
        var other = new OtherClient();
        var otherToo = new OtherClient();

        await CallerMetadataReadMemo.For(other, scope).GetAsync(Metadata, default); // would populate the request's store if it were shared
        await CallerMetadataReadMemo.For(otherToo, scope).GetAsync(Metadata, default);
        var real = await CallerMetadataReadMemo.For(_rig.NewClient(_wire), scope).GetAsync(Metadata, default);

        other.Gets[Metadata].Should().Be(1);
        otherToo.Gets[Metadata].Should().Be(1, "another non-DataverseUserClient memo of the same request does not see the first one's answer");
        _wire.Gets[Metadata].Should().Be(1, "the real client's memo asks for itself: the double's answer never entered the request's store");
        real.IsSuccess.Should().BeTrue();

        // And it is the per-create memo: its own answers, forgotten on a write.
        var memo = CallerMetadataReadMemo.For(other, scope);
        await memo.GetAsync(Metadata, default);
        await memo.GetAsync(Metadata, default);
        await memo.PatchAsync("sprk_todos(c0430000-0000-4000-8000-000000000051)", "{}", default);
        await memo.GetAsync(Metadata, default);
        other.Gets[Metadata].Should().Be(3, "once more for the new memo, and once again after the write");
    }

    /// <summary>A caller-client view (<see cref="ICallerClientView"/>) over whatever client it is given: the shape of the access window's view.</summary>
    private sealed class ViewOver : IDataverseUserClient, ICallerClientView
    {
        private readonly IDataverseUserClient _inner;

        public ViewOver(IDataverseUserClient inner) => _inner = inner;

        public IDataverseUserClient Inner => _inner;

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken) => _inner.GetAsync(relativePath, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            _inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            _inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            _inner.PatchAsync(relativePath, jsonBody, cancellationToken);

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, long expectedVersion, CancellationToken cancellationToken) =>
            _inner.PatchAsync(relativePath, jsonBody, expectedVersion, cancellationToken);

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(relativePath, cancellationToken);
    }

    [Fact]
    public async Task AViewOverAClientThatIsNotTheDataverseUserClient_GetsNoSharedAnswers_EvenInARequestWithAScope()
    {
        var scope = CallerMetadataReadMemo.RequestScope.Of(AmbientRequest());
        var other = new OtherClient();
        var otherToo = new OtherClient();

        await CallerMetadataReadMemo.For(new ViewOver(other), scope).GetAsync(Metadata, default); // would enter the request's store if shared
        await CallerMetadataReadMemo.For(new ViewOver(otherToo), scope).GetAsync(Metadata, default);
        var real = await CallerMetadataReadMemo.For(_rig.NewClient(_wire), scope).GetAsync(Metadata, default);

        other.Gets[Metadata].Should().Be(1);
        otherToo.Gets[Metadata].Should().Be(1, "a second view over another non-DataverseUserClient does not see the first one's answer");
        _wire.Gets[Metadata].Should().Be(1, "the real client's memo asked for itself: the views' answers never entered the request's store");
        real.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TheAccessWindowsViewOverTheDataverseUserClient_SharesTheRequestsStore_WithTheClientItself()
    {
        var http = AmbientRequest();
        var scope = CallerMetadataReadMemo.RequestScope.Of(http);
        using var window = DecisionAccessWindow.Open(http, Guid.NewGuid());

        var viaView = window.CallerClient(_rig.NewClient(_wire));
        viaView.Should().BeOfType<DecisionAccessWindowCallerClient>().Which.Should().BeAssignableTo<ICallerClientView>();
        await CallerMetadataReadMemo.For(viaView, scope).GetAsync(Metadata, default);
        await CallerMetadataReadMemo.For(window.CallerClient(_rig.NewClient(_wire)), scope).GetAsync(Metadata, default);
        await CallerMetadataReadMemo.For(_rig.NewClient(_wire), scope).GetAsync(Metadata, default);

        _wire.Gets[Metadata].Should().Be(1, "the window's views over the real client and the real client share one store per request");
    }

    [Fact]
    public async Task WithNoRequestScope_TheMemoIsPerInstance_AndForgetsOnAWrite_AsBefore()
    {
        AmbientRequest();
        CallerMetadataReadMemo.RequestScope.Of(null).Should().BeNull("off-request (a job) there is no scope");
        var client = _rig.NewClient(_wire);

        var memo = CallerMetadataReadMemo.For(client, null);
        await memo.GetAsync(Metadata, default);
        await memo.GetAsync(Metadata, default);
        await memo.PatchAsync("sprk_todos(c0430000-0000-4000-8000-000000000051)", "{}", default);
        await memo.GetAsync(Metadata, default);
        await CallerMetadataReadMemo.For(client, null).GetAsync(Metadata, default);

        _wire.Gets[Metadata].Should().Be(3, "once, again after the write, and again by another memo");
    }

    [Fact]
    public void OneRequest_HasOneScope()
    {
        var http = new DefaultHttpContext();

        CallerMetadataReadMemo.RequestScope.Of(http).Should().BeSameAs(CallerMetadataReadMemo.RequestScope.Of(http));
    }
}
