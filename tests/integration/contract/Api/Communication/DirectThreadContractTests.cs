using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Api.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 055: the Direct routes' contract.
/// <list type="bullet">
///   <item><c>POST /api/communications/threads/direct</c> applies the Direct participant rule before the pair's thread is found or
///   created: the other participant must read enabled and not an application user. A missing, disabled or application user is
///   one neutral 400; a read that does not complete is a 503. On every refusal the find-or-create (the only path that creates a
///   thread or writes its share) is never called.</item>
///   <item><c>GET /api/communications/threads?kind=</c>: <c>direct</c> is served (DI supplies the pair check), any value other than
///   <c>all</c> or <c>direct</c> is a 400.</item>
/// </list>
/// The host is <see cref="CommunicationRecordAuthorizationHost"/> (the real auth pipeline and filters over its module-boundary
/// doubles) with <see cref="IDirectThreadAccessService"/> replaced by a mock, so the test sees whether find-or-create ran.
/// </summary>
public class DirectThreadContractTests : IClassFixture<DirectThreadContractHost>
{
    private static readonly Guid Caller = CommunicationRecordAuthorizationHost.CallerSystemUserId;

    private readonly DirectThreadContractHost _fixture;
    private readonly CommunicationRecordAuthorizationHost _host;

    public DirectThreadContractTests(DirectThreadContractHost fixture)
    {
        _fixture = fixture;
        _host = fixture.Host;
        _host.Reset();
        _fixture.DirectThreads.Reset();
    }

    private Task<HttpResponseMessage> StartDirect(Guid other) =>
        _fixture.SendAsync(CommunicationRecordAuthorizationHost.Request(
            HttpMethod.Post, "/api/communications/threads/direct", new { otherParticipantSystemUserId = other }));

    /// <summary>The systemuser read answers <paramref name="row"/> for the asked id (null = no row).</summary>
    private void SystemUserReads(Func<Guid, DataverseEntity?> row) =>
        _host.Entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var collection = new EntityCollection();
                foreach (var id in q.Criteria.Conditions.Single(c => c.AttributeName == "systemuserid").Values.Cast<Guid>())
                {
                    if (row(id) is { } r)
                        collection.Entities.Add(r);
                }

                return collection;
            });

    private static DataverseEntity Person(Guid id, bool? disabled = false, Guid? applicationId = null)
    {
        var row = new DataverseEntity("systemuser", id);
        if (disabled is { } d)
            row["isdisabled"] = d;
        if (applicationId is { } app)
            row["applicationid"] = app;
        return row;
    }

    private void VerifyNothingCreated()
    {
        _fixture.DirectThreads.Verify(
            d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _host.Entities.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The rule refuses ─────────────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> Ineligible() => new() { "missing", "disabled", "disabled-unread", "application-user" };

    [Theory]
    [MemberData(nameof(Ineligible))]
    public async Task StartDirectThread_WithAnIneligibleParticipant_Returns400_AndCreatesNothing(string variant)
    {
        var other = Guid.NewGuid();
        SystemUserReads(id => id != other ? null : variant switch
        {
            "missing" => null,
            "disabled" => Person(id, disabled: true),
            "disabled-unread" => Person(id, disabled: null),
            "application-user" => Person(id, applicationId: Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        });

        var response = await StartDirect(other);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerifyNothingCreated();
    }

    [Fact]
    public async Task StartDirectThread_EveryIneligibleParticipant_GetsTheSameNeutralAnswer()
    {
        var missing = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        var appUser = Guid.NewGuid();
        SystemUserReads(id =>
            id == disabled ? Person(id, disabled: true)
            : id == appUser ? Person(id, applicationId: Guid.NewGuid())
            : null);

        var bodies = new List<string>();
        foreach (var other in new[] { missing, disabled, appUser })
            bodies.Add(await CommunicationRecordAuthorizationHost.NormalizedBodyAsync(await StartDirect(other)));

        bodies.Distinct().Should().ContainSingle("the answer never says which kind of user was named");
        bodies[0].Should().NotContainAny("disabled", "application", "not found", "missing");
    }

    [Fact]
    public async Task StartDirectThread_WhenTheParticipantReadFaults_Returns503_AndCreatesNothing()
    {
        _host.Entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("systemuser read faulted"));

        var response = await StartDirect(Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        VerifyNothingCreated();
    }

    [Fact]
    public async Task StartDirectThread_WithAnExistingPairWhoseParticipantIsNowDisabled_Returns400_AndDoesNotReturnTheThread()
    {
        // find-or-create would answer the existing pair; the rule refuses before it is asked.
        var other = Guid.NewGuid();
        _fixture.DirectThreads
            .Setup(d => d.FindOrCreateDirectThreadAsync(Caller, other, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        SystemUserReads(id => id == other ? Person(id, disabled: true) : null);

        var response = await StartDirect(other);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        VerifyNothingCreated();
    }

    // ── The earlier checks stay first ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartDirectThread_WithAnEmptyOrSelfParticipant_Returns400_BeforeAnyParticipantRead()
    {
        var empty = await StartDirect(Guid.Empty);
        var self = await StartDirect(Caller);

        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        self.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _host.Entities.Verify(
            e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNothingCreated();
    }

    // ── The rule passes ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartDirectThread_WithAnActiveInternalUser_Returns200_WithThePairsThread()
    {
        var other = Guid.NewGuid();
        var thread = Guid.NewGuid();
        SystemUserReads(id => id == other ? Person(id) : null);
        _fixture.DirectThreads
            .Setup(d => d.FindOrCreateDirectThreadAsync(Caller, other, It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

        var response = await StartDirect(other);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("threadId").GetGuid().Should().Be(thread);
        _fixture.DirectThreads.Verify(
            d => d.FindOrCreateDirectThreadAsync(Caller, other, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GET /threads?kind= ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("directs")]
    [InlineData("record")]
    public async Task ListThreads_WithAnUnknownKind_Returns400(string kind)
    {
        var response = await _fixture.SendAsync(CommunicationRecordAuthorizationHost.Request(
            HttpMethod.Get, $"/api/communications/threads?kind={kind}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _host.Query.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("?kind=all")]
    [InlineData("?kind=direct")]
    public async Task ListThreads_WithNoKindAllOrDirect_Returns200(string query)
    {
        var response = await _fixture.SendAsync(CommunicationRecordAuthorizationHost.Request(
            HttpMethod.Get, $"/api/communications/threads{query}"));

        // kind=direct is served (not 503): the host's DI supplies the pair check's reads.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

/// <summary>
/// <see cref="CommunicationRecordAuthorizationHost"/> with <see cref="IDirectThreadAccessService"/> replaced by a mock. The
/// derived factory keeps the host's configuration and doubles; <see cref="Host"/> resets them.
/// </summary>
public sealed class DirectThreadContractHost : IDisposable
{
    public CommunicationRecordAuthorizationHost Host { get; } = new();

    public Mock<IDirectThreadAccessService> DirectThreads { get; } = new();

    private readonly WebApplicationFactory<Program> _factory;

    public DirectThreadContractHost()
    {
        _factory = Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDirectThreadAccessService>();
            services.AddSingleton(DirectThreads.Object);
        }));
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using var client = _factory.CreateClient();
        return await client.SendAsync(request);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Host.Dispose();
    }
}
