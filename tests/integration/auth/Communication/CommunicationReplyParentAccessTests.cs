using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// spaarke-intake-pipeline-r1 task 006 (uac-r2): the reply-parent access check of a shared-mailbox (app-only) email reply. The caller must
/// hold Read, asked as the caller, on a <c>sprk_communication</c> whose Internet-Message-Id or Graph message id is the reply's
/// <c>InReplyToMessageId</c>; otherwise the reply gets the same 404 as a parent missing from the sending mailbox, with no Graph call. Driven
/// through the REAL <see cref="CommunicationService.SendAsync"/> and the REAL <see cref="EmailChannelSender"/> over a recording Graph
/// transport; the app-only Dataverse read is the <see cref="IGenericEntityService"/> module boundary (Moq) and the caller's rights come from
/// the production <see cref="CallerRecordAccessProbe"/> with its virtual seam substituted, as the other probe tests do.
/// </summary>
public class CommunicationReplyParentAccessTests
{
    private const string Mailbox = "legal@contoso.com";
    private const string ParentRfcId = "<parent-1@mail.contoso.com>";
    private const string ParentGraphId = "PARENT-GRAPH-ID";
    private const string DraftId = "DRAFT-ID";
    private const string CallerToken = "caller-token";
    private static readonly Guid ParentRow = Guid.Parse("00000006-0000-4000-8000-000000000001");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly List<QueryExpression> _candidateQueries = new();
    private List<Entity> _candidates = new();
    private readonly List<string> _events = new();

    public CommunicationReplyParentAccessTests()
    {
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                if (query.EntityName != "sprk_communication" || query.TopCount is null)
                    return new EntityCollection();
                _candidateQueries.Add(query);
                return new EntityCollection(_candidates);
            });
        _entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Guid.NewGuid());
    }

    // ── MUST FIRE ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>MUTATION: treating a None answer as Read sends the reply (createReply and send appear).</summary>
    [Fact]
    public async Task SendAsync_AppOnlyReply_ParentTheCallerCannotRead_Returns404_WithNoGraphCall()
    {
        _candidates = [Row(ParentRow)];
        var graph = new GraphTransport();

        var failure = await FailureOf(graph, Request(), Context(new RightsProbe(AccessRights.None, _events)));

        failure.Code.Should().Be(EmailChannelSender.ReplyParentNotFoundCode);
        failure.StatusCode.Should().Be(404);
        graph.Calls.Should().BeEmpty("the mailbox is not even looked at");
        _events.Should().Equal($"probe sprk_communications({ParentRow})");
        AssertNothingWritten();
    }

    [Fact]
    public async Task SendAsync_AppOnlyReply_UnknownParent_Returns404_WithNoGraphCall()
    {
        _candidates = [];
        var graph = new GraphTransport();

        var failure = await FailureOf(graph, Request(), Context(new RightsProbe(AccessRights.Read, _events)));

        failure.Code.Should().Be(EmailChannelSender.ReplyParentNotFoundCode);
        failure.StatusCode.Should().Be(404);
        graph.Calls.Should().BeEmpty();
        _events.Should().BeEmpty("there is no row to ask about");
        AssertNothingWritten();
    }

    [Fact]
    public async Task SendAsync_AppOnlyReply_ReadableParentMissingFromTheMailbox_Returns404_AfterTheLookup_AndSendsNothing()
    {
        _candidates = [Row(ParentRow)];
        var graph = new GraphTransport { ParentExists = false };

        var failure = await FailureOf(graph, Request(), Context(new RightsProbe(AccessRights.Read, _events)));

        failure.Code.Should().Be(EmailChannelSender.ReplyParentNotFoundCode);
        graph.Paths.Should().Contain($"GET /users/{Mailbox}/messages", "the mailbox lookup happens");
        graph.Paths.Should().NotContain(p => p.Contains("createReply") || p.EndsWith("/send") || p.EndsWith("/sendMail"));
        AssertNothingWritten();
    }

    [Fact]
    public async Task SendAsync_AppOnlyReply_UnreadableUnknownAndNotInMailbox_GiveTheSameProblem()
    {
        _candidates = [Row(ParentRow)];
        var unreadable = await FailureOf(new GraphTransport(), Request(), Context(new RightsProbe(AccessRights.None, _events)));
        _candidates = [];
        var unknown = await FailureOf(new GraphTransport(), Request(), Context(new RightsProbe(AccessRights.Read, _events)));
        _candidates = [Row(ParentRow)];
        var notInMailbox = await FailureOf(
            new GraphTransport { ParentExists = false }, Request(), Context(new RightsProbe(AccessRights.Read, _events)));

        foreach (var other in new[] { unknown, notInMailbox })
        {
            other.Code.Should().Be(unreadable.Code);
            other.Title.Should().Be(unreadable.Title);
            other.Detail.Should().Be(unreadable.Detail);
            other.StatusCode.Should().Be(unreadable.StatusCode);
            other.Extensions!.Keys.Should().BeEquivalentTo(unreadable.Extensions!.Keys);
            other.Extensions["correlationId"].Should().Be(unreadable.Extensions["correlationId"]);
        }
    }

    /// <summary>No request services, no bearer token or no probe composed: the reply is refused (fail closed), like an unreadable parent.</summary>
    [Theory]
    [InlineData("no-context")]
    [InlineData("no-token")]
    [InlineData("no-probe")]
    public async Task SendAsync_AppOnlyReply_WhenTheCallerCannotBeAsked_Returns404_WithNoGraphCall(string missing)
    {
        _candidates = [Row(ParentRow)];
        var graph = new GraphTransport();
        var probe = new RightsProbe(AccessRights.Read, _events);
        var context = missing switch
        {
            "no-context" => null,
            "no-token" => Context(probe, bearer: null),
            _ => Context(probe: null),
        };

        var failure = await FailureOf(graph, Request(), context);

        failure.Code.Should().Be(EmailChannelSender.ReplyParentNotFoundCode);
        graph.Calls.Should().BeEmpty();
        _events.Should().BeEmpty();
        AssertNothingWritten();
    }

    /// <summary>MUTATION: mapping a fault to "not readable" answers the 404, which would tell a caller "no such message" during an outage.</summary>
    [Theory]
    [InlineData("probe")]
    [InlineData("candidate-read")]
    public async Task SendAsync_AppOnlyReply_WhenTheCheckFaults_Returns503_AndSendsNothing(string fault)
    {
        _candidates = [Row(ParentRow)];
        if (fault == "candidate-read")
        {
            _entities
                .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.TopCount != null), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));
        }

        var graph = new GraphTransport();
        CallerRecordAccessProbe probe = fault == "probe" ? new ThrowingProbe() : new RightsProbe(AccessRights.Read, _events);

        var failure = await FailureOf(graph, Request(), Context(probe));

        failure.Code.Should().Be(CommunicationService.ReplyParentCheckUnavailableCode);
        failure.StatusCode.Should().Be(503);
        graph.Calls.Should().BeEmpty();
        AssertNothingWritten();
    }

    [Fact]
    public async Task SendAsync_AppOnlyReply_WhenTheCallerCancels_PropagatesTheCancellation()
    {
        _candidates = [Row(ParentRow)];
        using var cts = new CancellationTokenSource();
        var graph = new GraphTransport();

        var act = () => Sut(graph).SendAsync(Request(), Context(new CancellingProbe(cts)), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        graph.Calls.Should().BeEmpty();
    }

    // ── MUST NOT FIRE ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_AppOnlyReply_ReadableParent_ThreadsOnTheParent_AndSendsTheDraft()
    {
        _candidates = [Row(Guid.NewGuid()), Row(ParentRow)];
        var probe = new RightsProbe(AccessRights.None, _events) { ReadOn = ParentRow };
        var graph = new GraphTransport();

        await Sut(graph).SendAsync(Request(inReplyTo: "  " + ParentRfcId + " "), Context(probe));

        graph.Paths.Should().Contain($"POST /users/{Mailbox}/messages/{ParentGraphId}/createReply");
        graph.Paths.Should().Contain($"POST /users/{Mailbox}/messages/{DraftId}/send");
        graph.Paths.Should().NotContain(p => p.EndsWith("/sendMail"));

        // One candidate read, by either id, of the trimmed value, capped and reading the id only.
        var query = _candidateQueries.Should().ContainSingle().Subject;
        query.TopCount.Should().Be(5);
        query.ColumnSet.Columns.Should().Equal("sprk_communicationid");
        var byEitherId = query.Criteria.Filters.Should().ContainSingle().Subject;
        byEitherId.FilterOperator.Should().Be(LogicalOperator.Or);
        byEitherId.Conditions.Select(c => (c.AttributeName, c.Values.Single()))
            .Should().BeEquivalentTo(new[] { ("sprk_internetmessageid", (object)ParentRfcId), ("sprk_graphmessageid", (object)ParentRfcId) });
        probe.Tokens.Should().OnlyContain(t => t == CallerToken, "the rights are asked as the caller");
        _events.Should().HaveCount(2).And.EndWith($"probe sprk_communications({ParentRow})");
    }

    [Fact]
    public async Task SendAsync_ReplyParentSetByServer_AppOnlyReply_ThreadsWithoutAskingTheCaller()
    {
        _candidates = [];
        var graph = new GraphTransport();

        await Sut(graph).SendAsync(Request() with { ReplyParentSetByServer = true }, Context(new RightsProbe(AccessRights.None, _events)));

        graph.Paths.Should().Contain($"POST /users/{Mailbox}/messages/{ParentGraphId}/createReply");
        graph.Paths.Should().Contain($"POST /users/{Mailbox}/messages/{DraftId}/send");
        _events.Should().BeEmpty("a parent set by server code is not checked against the caller");
        _candidateQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_UserModeReply_GoesToTheChannel_WithoutAskingTheCaller()
    {
        var rig = new RecordingUserEmailRig();
        var context = RecordingUserEmailRig.UserContext();
        context.Request.Headers.Authorization = "Bearer " + CallerToken;
        context.RequestServices = new ServiceCollection()
            .AddSingleton<CallerRecordAccessProbe>(new RightsProbe(AccessRights.None, _events)).BuildServiceProvider();

        await rig.Service.SendAsync(Request() with { SendMode = SendMode.User }, context);

        var sent = rig.Channel.Sent.Should().ContainSingle().Subject;
        sent.UserMode.Should().BeTrue();
        sent.Communication.InReplyToMessageId.Should().Be(ParentRfcId);
        _events.Should().BeEmpty("a reply from the caller's own mailbox is not checked");
    }

    // ── GUARDS ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReplyParentSetByServer_IsNotBoundFromJson()
    {
        var json = """{"to":["a@example.com"],"subject":"s","body":"b","inReplyToMessageId":"<p@x>","replyParentSetByServer":true,"ReplyParentSetByServer":true}""";

        var bound = JsonSerializer.Deserialize<SendCommunicationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        bound.ReplyParentSetByServer.Should().BeFalse();
        bound.InReplyToMessageId.Should().Be("<p@x>");
    }

    [Theory]
    [InlineData("user-mode")]
    [InlineData("message")]
    [InlineData("no-parent")]
    public async Task SendAsync_ReplyParentSetByServer_OnAnyOtherSend_IsRefused_BeforeAnythingIsSent(string shape)
    {
        var graph = new GraphTransport();
        var request = Request() with { ReplyParentSetByServer = true };
        request = shape switch
        {
            "user-mode" => request with { SendMode = SendMode.User },
            "message" => request with { CommunicationType = CommunicationType.Message },
            _ => request with { InReplyToMessageId = "  " },
        };

        var act = () => Sut(graph).SendAsync(request, Context(new RightsProbe(AccessRights.Read, _events)));

        await act.Should().ThrowAsync<InvalidOperationException>();
        graph.Calls.Should().BeEmpty();
        _events.Should().BeEmpty();
        AssertNothingWritten();
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────────────

    private void AssertNothingWritten()
    {
        _entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);
        _entities.Verify(
            e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private async Task<SdapProblemException> FailureOf(GraphTransport graph, SendCommunicationRequest request, HttpContext? context)
    {
        var act = () => Sut(graph).SendAsync(request, context);
        return (await act.Should().ThrowAsync<SdapProblemException>()).Which;
    }

    private CommunicationService Sut(GraphTransport graph)
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = Mailbox, DisplayName = "Legal", IsDefault = true }],
            DefaultMailbox = Mailbox,
        };
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            new CommunicationAccountService(
                Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                NullLogger<CommunicationAccountService>.Instance),
            Mock.Of<IDistributedCache>(),
            NullLogger<ApprovedSenderValidator>.Instance);
        var factory = new Mock<IGraphClientFactory>();
        factory.Setup(f => f.ForApp()).Returns(new GraphServiceClient(new HttpClient(graph)));

        return new CommunicationService(
            CommunicationChannelTestFactory.CreateDispatcher(factory.Object),
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            _entities.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService: its daily-limit read is best-effort (a fault proceeds)
            null!, // JobSubmissionService: not reached
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            CoreAncestorResolverFixtures.Inert(),
            new RecordOwnershipResolverDouble(),
            NullLogger<CommunicationService>.Instance);
    }

    private static SendCommunicationRequest Request(string inReplyTo = ParentRfcId) => new()
    {
        To = ["requester@example.com"],
        Subject = "Re: Mutual NDA",
        Body = "<p>Attached is the reviewed draft.</p>",
        BodyFormat = BodyFormat.HTML,
        FromMailbox = Mailbox,
        SendMode = SendMode.SharedMailbox,
        InReplyToMessageId = inReplyTo,
        CorrelationId = "task-006-access",
    };

    private static HttpContext Context(CallerRecordAccessProbe? probe, string? bearer = CallerToken)
    {
        var context = new DefaultHttpContext();
        if (bearer is not null)
            context.Request.Headers.Authorization = "Bearer " + bearer;
        var services = new ServiceCollection();
        if (probe is not null)
            services.AddSingleton(probe);
        context.RequestServices = services.BuildServiceProvider();
        return context;
    }

    private static Entity Row(Guid id) => new("sprk_communication", id);

    /// <summary>Answers <see cref="AccessRights.Read"/> on <see cref="ReadOn"/> (or on every row when unset and constructed with Read), and records each question.</summary>
    private sealed class RightsProbe(AccessRights rights, List<string> events)
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public Guid? ReadOn { get; init; }

        public List<string?> Tokens { get; } = new();

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            events.Add($"probe {entitySet}({recordId})");
            Tokens.Add(callerBearerToken);
            return Task.FromResult(ReadOn is { } readable ? (recordId == readable ? AccessRights.Read : AccessRights.None) : rights);
        }
    }

    private sealed class ThrowingProbe()
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
            => throw new HttpRequestException("RetrievePrincipalAccess transport fault");
    }

    private sealed class CancellingProbe(CancellationTokenSource cts)
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(AccessRights.Read);
        }
    }

    private sealed record Call(string Method, string Path);

    /// <summary>A Graph mailbox that answers each request of the reply sequence and records it.</summary>
    private sealed class GraphTransport : HttpMessageHandler
    {
        public List<Call> Calls { get; } = new();

        public List<string> Paths => Calls.Select(c => $"{c.Method} {c.Path}").ToList();

        public bool ParentExists { get; init; } = true;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            path = path[path.IndexOf('/', 1)..]; // drop "/v1.0"
            Calls.Add(new Call(request.Method.Method, path));

            if (path.Contains("/mailFolders/sentitems/messages", StringComparison.OrdinalIgnoreCase))
                return Json(HttpStatusCode.OK, """{"value":[{"internetMessageId":"<sent-1@mail.contoso.com>"}]}""");
            if (path.EndsWith("/createReply", StringComparison.Ordinal))
                return Json(HttpStatusCode.Created, $$"""{"id":"{{DraftId}}"}""");
            if (path.EndsWith("/send", StringComparison.Ordinal) || path.EndsWith("/sendMail", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted));
            if (request.Method == HttpMethod.Patch)
                return Json(HttpStatusCode.OK, "{}");
            if (request.Method == HttpMethod.Delete)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            if (path.EndsWith("/messages", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, ParentExists ? $$"""{"value":[{"id":"{{ParentGraphId}}"}]}""" : """{"value":[]}""");
            return Json(HttpStatusCode.NotFound, """{"error":{"code":"ErrorItemNotFound","message":"not found"}}""");
        }

        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string body) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }
}
