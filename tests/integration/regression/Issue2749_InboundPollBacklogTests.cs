// GitHub #2749: InboundPollingBackupService lost mail when more than 50 unread messages arrived in one poll interval.
//
// The poll asked Graph for `receivedDateTime ge {last} and isRead eq false`, Top = 50, newest first, followed no
// @odata.nextLink, and then moved its window to the poll start whatever it had seen. With 120 unread messages the
// newest 50 were enqueued and the other 70 fell behind the window for good. Per #1561 this poll may be the only working
// inbound path, so those messages never became a sprk_communication.
//
// Double: the Kiota IRequestAdapter — the Graph SDK's own boundary, which GraphServiceClient is built from (the
// SpeContainerGraphStub / DocumentVersionListPagingTests precedent). The adapter below is a small mailbox that applies the
// request's own $filter / $orderby / $top and serves @odata.nextLink pages, so the real request-builder chain, the real
// paging and the real watermark logic run; no HTTP anywhere. Service Bus is mocked at the JobSubmissionService boundary.
// Time is a FakeTimeProvider.

using System.Collections.Specialized;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Regression;

public class Issue2749_InboundPollBacklogTests
{
    private const string Mailbox = "shared@contoso.com";

    // A realistic "now" for the fake clock (the service's first lookback is 15 minutes behind it).
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Poll_WithMoreThanOnePageOfUnreadMessages_EnqueuesEveryMessage()
    {
        var mailbox = new FakeMailbox();
        for (var i = 0; i < 120; i++)
            mailbox.Add($"m{i:D3}", Now.AddMinutes(-10).AddSeconds(i));
        var fixture = new Fixture(mailbox);

        await fixture.PollAsync();

        fixture.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds);
        fixture.EnqueuedMessageIds.Should().OnlyHaveUniqueItems();
        mailbox.Requests.Should().HaveCountGreaterThan(1, "the extra pages are followed through @odata.nextLink");
    }

    [Fact]
    public async Task Poll_WithBacklogLargerThanThePerPollCap_ResumesFromTheLastEnqueuedMessageWithNoGap()
    {
        var total = InboundPollingBackupService.MaxMessagesPerPoll + 120;
        var mailbox = new FakeMailbox();
        for (var i = 0; i < total; i++)
            mailbox.Add($"m{i:D4}", Now.AddMinutes(-14).AddMilliseconds(i * 1000)); // one per second, all unread
        var fixture = new Fixture(mailbox);

        await fixture.PollAsync();
        fixture.EnqueuedMessageIds.Should().HaveCount(InboundPollingBackupService.MaxMessagesPerPoll,
            "one mailbox may not hold the whole cycle");
        fixture.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds.Take(InboundPollingBackupService.MaxMessagesPerPoll),
            "the OLDEST messages go first, so what is left is a clean tail");

        fixture.Advance(TimeSpan.FromMinutes(5));
        await fixture.PollAsync();

        fixture.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds, "the second poll picks up exactly the tail");
        fixture.EnqueuedMessageIds.Should().OnlyHaveUniqueItems("nothing is enqueued twice");
    }

    [Fact]
    public async Task Poll_WhenTheCapFallsInsideAGroupOfMessagesSharingOneTimestamp_SkipsNoneAndRepeatsNone()
    {
        var cap = InboundPollingBackupService.MaxMessagesPerPoll;
        var tie = Now.AddMinutes(-5);
        var mailbox = new FakeMailbox();
        for (var i = 0; i < cap - 10; i++)
            mailbox.Add($"a{i:D4}", Now.AddMinutes(-14).AddSeconds(i));
        // 30 messages stamped with the SAME second; the cap lands after the 10th of them.
        for (var i = 0; i < 30; i++)
            mailbox.Add($"t{i:D2}", tie);
        for (var i = 0; i < 5; i++)
            mailbox.Add($"z{i:D2}", tie.AddSeconds(30 + i));
        var fixture = new Fixture(mailbox);

        await fixture.PollAsync();
        fixture.EnqueuedMessageIds.Should().HaveCount(cap);

        fixture.Advance(TimeSpan.FromMinutes(5));
        await fixture.PollAsync();

        fixture.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds, "the 20 tied messages past the cap are not skipped");
        fixture.EnqueuedMessageIds.Should().OnlyHaveUniqueItems("the 10 tied messages already enqueued are not enqueued again");
    }

    [Fact]
    public async Task Poll_AfterDrainingTheMailbox_StillPicksUpALateVisibleMessageStampedBeforeThePollStart()
    {
        var mailbox = new FakeMailbox();
        mailbox.Add("early", Now.AddMinutes(-3));
        var fixture = new Fixture(mailbox);

        await fixture.PollAsync();
        fixture.EnqueuedMessageIds.Should().Equal("early");

        // Exchange stamped this 30 seconds before the first poll started, but Graph only listed it afterwards.
        mailbox.Add("late", Now.AddSeconds(-30));
        fixture.Advance(TimeSpan.FromMinutes(5));
        await fixture.PollAsync();

        fixture.EnqueuedMessageIds.Should().Equal("early", "late");
    }

    // ── Harness ─────────────────────────────────────────────────────────────────

    private sealed class Fixture
    {
        private readonly FakeTimeProvider _time = new(Now);
        private readonly InboundPollingBackupService _sut;
        private readonly List<JobContract> _jobs = [];

        public Fixture(FakeMailbox mailbox)
        {
            var graph = new GraphServiceClient(mailbox);
            _sut = new InboundPollingBackupService(
                CreateAccountService(),
                Mock.Of<IGraphClientFactory>(f => f.ForApp() == graph),
                CreateJobSubmission(_jobs),
                Mock.Of<ILogger<InboundPollingBackupService>>(),
                _time);
        }

        public IReadOnlyList<string> EnqueuedMessageIds
            => _jobs.Select(j => j.SubjectId!).ToList();

        public Task PollAsync() => _sut.PollAllAccountsAsync(CancellationToken.None);

        public void Advance(TimeSpan by) => _time.Advance(by);
    }

    private static CommunicationAccountService CreateAccountService()
    {
        var accountId = Guid.NewGuid();
        var account = new DataverseEntity("sprk_communicationaccount") { Id = accountId };
        account["sprk_emailaddress"] = Mailbox;
        account["sprk_name"] = "Shared mailbox";
        account["sprk_accounttype"] = new OptionSetValue(100000000);
        account["sprk_receiveenabled"] = true;

        var dataverse = new Mock<IDataverseService>();
        dataverse
            .Setup(d => d.QueryCommunicationAccountsAsync(
                It.Is<string>(f => f.Contains("sprk_receiveenabled")),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { account });

        return new CommunicationAccountService(
            dataverse.Object,
            dataverse.Object,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
    }

    private static JobSubmissionService CreateJobSubmission(List<JobContract> captured)
    {
        var options = new Mock<IOptions<ServiceBusOptions>>();
        options.Setup(o => o.Value).Returns(new ServiceBusOptions());

        var mock = new Mock<JobSubmissionService>(
            MockBehavior.Loose,
            options.Object,
            Mock.Of<ILogger<JobSubmissionService>>(),
            new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);

        mock.Setup(s => s.SubmitCommunicationJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
            .Returns<JobContract, CancellationToken>((job, _) =>
            {
                captured.Add(job);
                return Task.CompletedTask;
            });

        return mock.Object;
    }

    /// <summary>
    /// One mail folder as Graph answers a `messages` listing: applies the request's own
    /// <c>receivedDateTime ge</c> + <c>isRead eq false</c> filter, <c>$orderby</c> and <c>$top</c>, and pages through an
    /// <c>@odata.nextLink</c> that carries the whole query plus a <c>$skiptoken</c>. Ties keep insertion order. Throws on any
    /// request it does not model.
    /// </summary>
    private sealed class FakeMailbox : IRequestAdapter
    {
        private static readonly Regex FilterPattern = new(
            @"^receivedDateTime ge (?<from>\S+) and isRead eq false$", RegexOptions.Compiled);

        private readonly List<(string Id, DateTimeOffset Received)> _messages = [];

        public List<string> Requests { get; } = [];

        public IReadOnlyList<string> AllIds => _messages.Select(m => m.Id).ToList();

        public void Add(string id, DateTimeOffset received) => _messages.Add((id, received));

        public string? BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

        public ISerializationWriterFactory SerializationWriterFactory => throw new NotSupportedException();

        public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) { }

        public Task<ModelType?> SendAsync<ModelType>(
            RequestInformation requestInfo, ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            where ModelType : IParsable
        {
            Requests.Add($"{requestInfo.HttpMethod} {requestInfo.URI}");
            if (typeof(ModelType) != typeof(MessageCollectionResponse))
                throw new NotSupportedException($"Unexpected Graph read {requestInfo.URI}.");

            NameValueCollection query = HttpUtility.ParseQueryString(requestInfo.URI.Query);
            var filter = FilterPattern.Match(query["$filter"] ?? "");
            if (!filter.Success)
                throw new NotSupportedException($"Unmodelled $filter '{query["$filter"]}'.");

            var from = DateTimeOffset.Parse(filter.Groups["from"].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var top = int.Parse(query["$top"] ?? "10", CultureInfo.InvariantCulture);
            var skip = int.Parse(query["$skiptoken"] ?? "0", CultureInfo.InvariantCulture);
            var orderby = query["$orderby"] ?? "";

            // The listing is unread-only and every message here is unread; none is ever marked read (no processor runs).
            IEnumerable<(string Id, DateTimeOffset Received)> matching =
                _messages.Where(m => TruncateToSecond(m.Received) >= from);
            matching = orderby switch
            {
                "receivedDateTime asc" => matching.OrderBy(m => m.Received),
                "receivedDateTime desc" => matching.OrderByDescending(m => m.Received),
                _ => matching,
            };

            var all = matching.ToList();
            var page = all.Skip(skip).Take(top).ToList();
            var more = skip + page.Count < all.Count;

            var response = new MessageCollectionResponse
            {
                Value = page.Select(m => new Message { Id = m.Id, ReceivedDateTime = m.Received, IsRead = false }).ToList(),
                OdataNextLink = more
                    ? $"{BaseUrl}/users/{Mailbox}/mailFolders/Inbox/messages" +
                      $"?$filter={Uri.EscapeDataString(query["$filter"]!)}&$orderby={Uri.EscapeDataString(orderby)}" +
                      $"&$top={top}&$skiptoken={skip + page.Count}"
                    : null,
            };
            return Task.FromResult((ModelType?)(object)response);
        }

        private static DateTimeOffset TruncateToSecond(DateTimeOffset value)
            => new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

        public Task SendNoContentAsync(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<ModelType?> SendPrimitiveAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IEnumerable<ModelType>?> SendCollectionAsync<ModelType>(
            RequestInformation requestInfo, ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            where ModelType : IParsable
            => throw new NotSupportedException();

        public Task<IEnumerable<ModelType>?> SendPrimitiveCollectionAsync<ModelType>(
            RequestInformation requestInfo,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<NativeResponseType?> ConvertToNativeRequestAsync<NativeResponseType>(
            RequestInformation requestInfo, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
