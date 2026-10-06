// spaarke-ontology-platform-r1 task 097, second review M3 — the regarding-parent RESOLVER's failure branches and the
// 400 mapping. Before this file only the happy path of ResolveEventRegardingAsync was exercised (through the live
// harness); the branches that REFUSE a write (no catalog row, parent missing, half-specified parent) had no test, so a
// regression that turned a refusal into a half-written regarding would not have failed anything.
//
// The resolver is driven through the production DataverseWebApiService and its protected test constructor, with a
// hand-written recording HttpMessageHandler (ADR-038 bans Mock<HttpMessageHandler>) — the requests it SENDS are the
// contract: a refusal must send no write.

using System.Net;
using System.Text;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Events;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using ApiCreateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.CreateEventRequest;
using ApiUpdateEventRequest = Sprk.Bff.Api.Api.Events.Dtos.UpdateEventRequest;
using DataverseCreateEventRequest = Spaarke.Dataverse.CreateEventRequest;
using DataverseUpdateEventRequest = Spaarke.Dataverse.UpdateEventRequest;

namespace Sprk.Bff.Api.Tests.Api.Events;

public class EventRegardingResolutionTests
{
    private static readonly Guid MatterId = Guid.Parse("491b1efe-e562-f111-ab0c-000d3a4d8152");
    private static readonly Guid MatterTypeRefId = Guid.Parse("e8547bb4-8600-f111-8407-7c1e520aa4df");
    private static readonly Guid EventId = Guid.Parse("97097097-0000-0000-0000-0000000000a3");

    // ── Resolver: the refusing branches ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TypeWithoutId_IsRefused_AndNothingIsSent()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("no request expected"));
        var sut = new OfflineService(handler);

        var act = () => sut.ResolveEventRegardingAsync(RegardingRecordType.Matter, null, null, default);

        await act.Should().ThrowAsync<EventRegardingResolutionException>().WithMessage("*together*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task IdThatIsNotAGuid_IsRefused_AndNothingIsSent()
    {
        var handler = new RecordingHandler(_ => throw new InvalidOperationException("no request expected"));
        var sut = new OfflineService(handler);

        var act = () => sut.ResolveEventRegardingAsync(RegardingRecordType.Matter, "not-a-guid", null, default);

        await act.Should().ThrowAsync<EventRegardingResolutionException>().WithMessage("*GUID*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NoRecordTypeRefRow_IsRefused_AndTheParentIsNeverRead()
    {
        var handler = new RecordingHandler(r => r.RequestUri!.AbsolutePath.EndsWith("/sprk_recordtype_refs")
            ? Json("{\"value\":[]}")
            : throw new InvalidOperationException("the parent must not be read once the catalog row is missing"));
        var sut = new OfflineService(handler);

        var act = () => sut.ResolveEventRegardingAsync(RegardingRecordType.Matter, MatterId.ToString(), null, default);

        await act.Should().ThrowAsync<EventRegardingResolutionException>().WithMessage("*sprk_recordtype_ref*sprk_matter*");
        handler.Requests.Should().ContainSingle()
            .Which.Query.Should().Contain("sprk_recordlogicalname eq 'sprk_matter'");
    }

    [Fact]
    public async Task ParentMissing_404_IsRefused()
    {
        var handler = new RecordingHandler(r => IsCatalog(r)
            ? Json(CatalogRow("sprk_matternumber"))
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = new OfflineService(handler);

        var act = () => sut.ResolveEventRegardingAsync(RegardingRecordType.Matter, MatterId.ToString(), "Caller name", default);

        await act.Should().ThrowAsync<EventRegardingResolutionException>().WithMessage($"*{MatterId:D}*does not exist*");
    }

    [Fact]
    public async Task UpdateWithMissingParent_SendsNoPatch_SoTheOldRegardingStaysIntact()
    {
        var handler = new RecordingHandler(r => IsCatalog(r)
            ? Json(CatalogRow("sprk_matternumber"))
            : r.Method == HttpMethod.Patch
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = new OfflineService(handler);

        var act = () => sut.UpdateEventAsync(EventId, new DataverseUpdateEventRequest
        {
            RegardingRecordType = RegardingRecordType.Matter,
            RegardingRecordId = MatterId.ToString(),
        });

        await act.Should().ThrowAsync<EventRegardingResolutionException>();
        handler.Requests.Should().NotContain(r => r.Method == HttpMethod.Patch);
    }

    // ── Resolver: the success branch reads name and number from the server ────────────────────────────────────

    [Fact]
    public async Task Resolved_ServerNameWins_NumberComesFromTheCatalogsNumberField()
    {
        var handler = new RecordingHandler(r => IsCatalog(r)
            ? Json(CatalogRow("sprk_matternumber"))
            : Json($"{{\"sprk_matterid\":\"{MatterId}\",\"sprk_mattername\":\"Real Matter\",\"sprk_matternumber\":\"MAT-0097\"}}"));
        var sut = new OfflineService(handler);

        var resolved = await sut.ResolveEventRegardingAsync(
            RegardingRecordType.Matter, MatterId.ToString(), "A name the caller made up", default);

        resolved.Should().NotBeNull();
        resolved!.RecordName.Should().Be("Real Matter", "the record's own name wins over a caller-supplied one");
        resolved.RecordNumber.Should().Be("MAT-0097");
        resolved.RecordTypeRefId.Should().Be(MatterTypeRefId);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Query.Should().Contain("$select=sprk_matterid,sprk_mattername,sprk_matternumber");
    }

    [Fact]
    public async Task CatalogRowWithoutNumberField_ReadsNoNumberColumn_AndRecordsNoNumber()
    {
        var handler = new RecordingHandler(r => IsCatalog(r)
            ? Json(CatalogRow(numberField: null))
            : Json($"{{\"sprk_matterid\":\"{MatterId}\",\"sprk_mattername\":\"Real Matter\"}}"));
        var sut = new OfflineService(handler);

        var resolved = await sut.ResolveEventRegardingAsync(RegardingRecordType.Matter, MatterId.ToString(), null, default);

        resolved!.RecordNumber.Should().BeNull();
        handler.Requests[1].Query.Should().EndWith("$select=sprk_matterid,sprk_mattername");
    }

    // ── Endpoint: a resolution failure is a 400, and nothing else is written ──────────────────────────────────

    [Fact]
    public async Task CreateHandler_MapsResolutionFailureTo400()
    {
        var dv = new Mock<IEventDataverseService>(MockBehavior.Strict);
        dv.Setup(d => d.CreateEventAsync(It.IsAny<DataverseCreateEventRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EventRegardingResolutionException("The regarding sprk_matter x does not exist."));
        var callers = new Mock<ICallerSystemUserResolver>();
        callers.Setup(c => c.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved("97097097-0000-4000-8000-0000000000c1"));

        var result = await EventEndpoints.CreateEventAsync(
            new ApiCreateEventRequest("zz-097", RegardingRecordId: MatterId, RegardingRecordType: RegardingRecordType.Matter),
            dv.Object,
            Mock.Of<Sprk.Bff.Api.Services.Ai.Membership.Events.IMembershipEventPublisher>(),
            Mock.Of<IGenericEntityService>(),
            callers.Object,
            Mock.Of<IIdentityNormalizationService>(),
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                    new[] { new System.Security.Claims.Claim("oid", Guid.NewGuid().ToString("D")) }, "Test")),
            },
            NullLogger<Program>.Instance,
            default);

        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
        ErrorsOf(result).Should().ContainKey("Regarding");
        dv.Verify(d => d.CreateEventLogAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "no audit row for an event that was not created");
    }

    [Fact]
    public async Task UpdateHandler_MapsResolutionFailureTo400()
    {
        var dv = ExistingEvent();
        dv.Setup(d => d.UpdateEventAsync(EventId, It.IsAny<DataverseUpdateEventRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EventRegardingResolutionException("No sprk_recordtype_ref row exists for 'sprk_matter'."));

        var result = await EventEndpoints.UpdateEventAsync(
            EventId,
            new ApiUpdateEventRequest(RegardingRecordId: MatterId, RegardingRecordType: RegardingRecordType.Matter),
            dv.Object,
            CoreAncestorResolverFixtures.Inert(),
            NullLogger<Program>.Instance,
            default);

        StatusOf(result).Should().Be(StatusCodes.Status400BadRequest);
        ErrorsOf(result).Should().ContainKey("Regarding");
    }

    [Fact]
    public async Task UpdateHandler_FailedAncestorDerivation_Is500_AndTheEventIsNotWritten()
    {
        var dv = ExistingEvent();

        var result = await EventEndpoints.UpdateEventAsync(
            EventId,
            new ApiUpdateEventRequest(RegardingRecordId: MatterId, RegardingRecordType: RegardingRecordType.Invoice),
            dv.Object,
            CoreAncestorResolverFixtures.Failing(),
            NullLogger<Program>.Instance,
            default);

        StatusOf(result).Should().Be(StatusCodes.Status500InternalServerError);
        dv.Verify(d => d.UpdateEventAsync(It.IsAny<Guid>(), It.IsAny<DataverseUpdateEventRequest>(), It.IsAny<CancellationToken>()),
            Times.Never, "fail closed: an event re-parented without its matter/project stamp would be hidden from that team");
    }

    // ── Round 6 (review F4): the list query really runs AS the caller ─────────────────────────────────────────

    [Fact]
    public async Task QueryEvents_WithACaller_SendsExactlyOneMscrmCallerIdHeader_OnTheEventsRequest()
    {
        var caller = Guid.Parse("97097097-0000-4000-8000-0000000000f4");
        var handler = new RecordingHandler(r => IsCatalog(r)
            ? Json($"{{\"value\":[{{\"sprk_recordtype_refid\":\"{MatterTypeRefId}\",\"sprk_recordlogicalname\":\"sprk_matter\"}}]}}")
            : Json("{\"value\":[]}"));
        var sut = new OfflineService(handler);

        await sut.QueryEventsAsync(new EventQueryFilter { Top = 10, OwnerUserId = caller, ImpersonateSystemUserId = caller });

        var events = handler.Requests.Should().ContainSingle(r => r.Path.EndsWith("/sprk_events")).Subject;
        events.CallerIds.Should().ContainSingle().Which.Should().Be(caller.ToString(),
            "without MSCRMCallerID the list is the APPLICATION's view — every event, whatever the caller may read");
        handler.Requests.Where(r => !r.Path.EndsWith("/sprk_events")).Should().OnlyContain(r => r.CallerIds.Length == 0,
            "the record-type catalogue is environment metadata and stays app-only");
    }

    [Fact]
    public async Task QueryEvents_WithAnEmptyCallerId_IsRefused_NoAppOnlyFallback()
    {
        var handler = new RecordingHandler(r => IsCatalog(r) ? Json("{\"value\":[]}") : Json("{\"value\":[]}"));
        var sut = new OfflineService(handler);

        var act = () => sut.QueryEventsAsync(new EventQueryFilter { Top = 10, ImpersonateSystemUserId = Guid.Empty });

        await act.Should().ThrowAsync<ArgumentException>();
        handler.Requests.Should().NotContain(r => r.Path.EndsWith("/sprk_events"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────────────────────

    private static Mock<IEventDataverseService> ExistingEvent()
    {
        var dv = new Mock<IEventDataverseService>(MockBehavior.Strict);
        dv.Setup(d => d.GetEventAsync(EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EventEntity { Id = EventId, Name = "zz-097", StatusCode = EventStatusCode.Open });
        return dv;
    }

    private static bool IsCatalog(HttpRequestMessage r) => r.RequestUri!.AbsolutePath.EndsWith("/sprk_recordtype_refs");

    private static string CatalogRow(string? numberField) =>
        $"{{\"value\":[{{\"sprk_recordtype_refid\":\"{MatterTypeRefId}\",\"sprk_regardingrecordnumberfield\":"
        + (numberField is null ? "null" : $"\"{numberField}\"") + "}]}";

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static int? StatusOf(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private static IDictionary<string, string[]> ErrorsOf(IResult result) =>
        (result as Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult)?.ProblemDetails is Microsoft.AspNetCore.Http.HttpValidationProblemDetails v
            ? v.Errors
            : new Dictionary<string, string[]>();

    private sealed class OfflineService(RecordingHandler handler) : DataverseWebApiService(
        new HttpClient(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(),
        NullLogger<DataverseWebApiService>.Instance,
        confidentialClients: null,
        credential: new StaticTokenCredential());

    private sealed record SentRequest(HttpMethod Method, string Path, string Query, string[] CallerIds);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                Uri.UnescapeDataString(request.RequestUri!.Query),
                request.Headers.TryGetValues("MSCRMCallerID", out var ids) ? ids.ToArray() : Array.Empty<string>()));
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static readonly AccessToken Token = new("test-token", DateTimeOffset.MaxValue);

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
