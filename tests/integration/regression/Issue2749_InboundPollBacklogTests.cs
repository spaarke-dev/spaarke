// GitHub #2749: InboundPollingBackupService lost mail when more than 50 unread messages arrived in one poll interval.
//
// The poll asked Graph for `receivedDateTime ge {last} and isRead eq false`, Top = 50, newest first, followed no
// @odata.nextLink, and then moved its window to the poll start whatever it had seen. With 120 unread messages the
// newest 50 were enqueued and the other 70 fell behind the window for good. Per #1561 this poll may be the only working
// inbound path, so those messages never became a sprk_communication.
//
// The first fix followed @odata.nextLink. Graph's nextLink is a $skip offset that re-applies the filter, and the
// processor marks each enqueued message read while the poll is still paging, so the unread set shrank under the offset
// and mail was skipped again. The service now pages by keyset (a fresh `receivedDateTime ge` query per page) and
// persists its watermark in IDistributedCache so a restart does not abandon an older backlog.
//
// Double: the Kiota IRequestAdapter — the Graph SDK's own boundary, which GraphServiceClient is built from (the
// SpeContainerGraphStub / DocumentVersionListPagingTests precedent). The adapter below is a small mailbox that applies the
// request's own $filter (received time AND isRead, per message) / $orderby / $top / $skip and hands out Graph-style
// `$skip` nextLinks, so the real request-builder chain, the real paging and the real watermark logic run; no HTTP
// anywhere. Service Bus is mocked at the JobSubmissionService boundary; that double can act as the processor and mark
// each captured message read before the next page is requested. The cache is a real MemoryDistributedCache. Time is a
// FakeTimeProvider.

using System.Collections.Specialized;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
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

    private static readonly Guid AccountId = Guid.Parse("6b0f7c1e-2749-4a51-9d1e-000000002749");

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
        mailbox.Queries.Should().HaveCountGreaterThan(1, "the backlog is read over several keyset pages");
    }

    [Fact]
    public async Task Poll_WhenTheProcessorMarksMessagesReadBetweenPages_EnqueuesEveryMessageExactlyOnce()
    {
        // The processor runs concurrently with the poll and marks each message read once handled. Following Graph's
        // `$skip` nextLink over the shrinking unread set skipped 50 of these 120; keyset paging skips none.
        var mailbox = new FakeMailbox();
        for (var i = 0; i < 120; i++)
            mailbox.Add($"m{i:D3}", Now.AddMinutes(-10).AddSeconds(i));
        var fixture = new Fixture(mailbox, processorMarksRead: true);

        await fixture.PollAsync();

        fixture.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds, "no unread message is jumped over");
        fixture.EnqueuedMessageIds.Should().OnlyHaveUniqueItems();
        mailbox.UnreadIds.Should().BeEmpty("every message reached the processor");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Poll_WhenAWholePageSharesOneSecond_WidensThatPageAndSkipsNone(bool processorMarksRead)
    {
        // 120 messages in one second: a keyset page anchored on that second cannot move the anchor, so the service
        // re-issues the same query with a larger $top instead of stopping (or following a $skip nextLink).
        var tie = Now.AddMinutes(-5);
        var mailbox = new FakeMailbox();
        for (var i = 0; i < 120; i++)
            mailbox.Add($"t{i:D3}", tie);
        for (var i = 0; i < 5; i++)
            mailbox.Add($"z{i:D2}", tie.AddSeconds(10 + i));
        var fixture = new Fixture(mailbox, processorMarksRead: processorMarksRead);

        await fixture.PollAsync();

        fixture.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds);
        fixture.EnqueuedMessageIds.Should().OnlyHaveUniqueItems();
        mailbox.Queries.Should().Contain(q => q.Top > 50, "the one-second page is widened");
        mailbox.Queries.Should().OnlyContain(q => q.Skip == 0, "no page is read through a $skip offset");
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

    [Fact]
    public async Task Poll_WithAClockAtAFractionalSecond_DoesNotReEnqueueAMessageInTheWatermarksSecond()
    {
        // 09:58:00.700 — the drained watermark is poll start − 2 min = 09:56:00.700. Graph stamps whole seconds and the
        // $filter literal is whole seconds, so a message at 09:56:00 is listed again next poll. Unless the watermark is
        // floored to 09:56:00, its enqueued-id record is pruned (09:56:00 < 09:56:00.700) and it is enqueued twice.
        var start = new DateTimeOffset(2026, 11, 12, 9, 58, 0, 700, TimeSpan.Zero);
        var mailbox = new FakeMailbox();
        mailbox.Add("edge", new DateTimeOffset(2026, 11, 12, 9, 56, 0, TimeSpan.Zero));
        var fixture = new Fixture(mailbox, time: new FakeTimeProvider(start));

        await fixture.PollAsync();
        fixture.Advance(TimeSpan.FromMinutes(5));
        await fixture.PollAsync();

        fixture.EnqueuedMessageIds.Should().Equal("edge");
    }

    [Fact]
    public async Task Poll_WhenALaterPageThrows_KeepsWhatWasEnqueuedAndDoesNotAdvanceTheWatermark()
    {
        var mailbox = new FakeMailbox();
        for (var i = 0; i < 120; i++)
            mailbox.Add($"m{i:D3}", Now.AddMinutes(-10).AddSeconds(i));
        mailbox.FailRequestNumber = 2;
        var cache = NewCache();
        var fixture = new Fixture(mailbox, cache: cache);

        await fixture.PollAsync(); // the service logs and swallows the per-account failure
        fixture.EnqueuedMessageIds.Should().HaveCount(50, "page 1 went before page 2 failed");
        cache.GetString(WatermarkKey).Should().BeNull("a failed poll persists no watermark");

        mailbox.FailRequestNumber = null;
        var firstRetryQuery = mailbox.Queries.Count;
        fixture.Advance(TimeSpan.FromMinutes(5));
        await fixture.PollAsync();

        mailbox.Queries[firstRetryQuery].From.Should().Be(Now.AddMinutes(-15),
            "the retry starts from the old watermark, not past the mail page 2 never listed");
        fixture.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds);
        fixture.EnqueuedMessageIds.Should().OnlyHaveUniqueItems("page 1's ids are remembered and not enqueued again");
    }

    [Fact]
    public async Task Poll_AfterARestart_ResumesFromThePersistedWatermark()
    {
        // A backlog bigger than one poll's cap, then a restart 20 minutes later: now − 15 min is past every message,
        // so only the persisted watermark can reach the 120 left behind.
        var total = InboundPollingBackupService.MaxMessagesPerPoll + 120;
        var mailbox = new FakeMailbox();
        for (var i = 0; i < total; i++)
            mailbox.Add($"m{i:D4}", Now.AddMinutes(-14).AddSeconds(i));
        var cache = NewCache();
        var time = new FakeTimeProvider(Now);

        var beforeRestart = new Fixture(mailbox, cache: cache, time: time, processorMarksRead: true);
        await beforeRestart.PollAsync();
        beforeRestart.EnqueuedMessageIds.Should().HaveCount(InboundPollingBackupService.MaxMessagesPerPoll);
        cache.GetString(WatermarkKey).Should().NotBeNull();

        time.Advance(TimeSpan.FromMinutes(20));
        var afterRestart = new Fixture(mailbox, cache: cache, time: time, processorMarksRead: true);
        await afterRestart.PollAsync();

        var all = beforeRestart.EnqueuedMessageIds.Concat(afterRestart.EnqueuedMessageIds).ToList();
        all.Should().BeEquivalentTo(mailbox.AllIds, "the new instance resumes where the old one stopped");
        all.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Poll_WhenTheWatermarkCacheFails_PollsFromTheDefaultLookbackAndLogsNoAddress()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache down"));
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cache down"));
        var mailbox = new FakeMailbox();
        mailbox.Add("older", Now.AddMinutes(-20));
        mailbox.Add("recent", Now.AddMinutes(-10));
        var fixture = new Fixture(mailbox, cache: cache.Object);

        await fixture.PollAsync();

        fixture.EnqueuedMessageIds.Should().Equal("recent");
        fixture.Logs.Should().Contain(l => l.Level == LogLevel.Warning && l.Message.Contains("watermark"));
        fixture.Logs.Should().NotContain(l => l.Message.Contains(Mailbox, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Poll_DrainedLine_IsInformationOnlyWhenTheListingFoundMail()
    {
        var mailbox = new FakeMailbox();
        var fixture = new Fixture(mailbox);

        await fixture.PollAsync();
        fixture.Logs.Where(l => l.Message.Contains("drained the mailbox")).Should().ContainSingle()
            .Which.Level.Should().Be(LogLevel.Debug, "an empty poll every 5 minutes per mailbox is not Information");

        mailbox.Add("one", Now.AddMinutes(1));
        fixture.Advance(TimeSpan.FromMinutes(5));
        await fixture.PollAsync();
        fixture.Logs.Where(l => l.Message.Contains("drained the mailbox")).Last()
            .Level.Should().Be(LogLevel.Information);

        fixture.Logs.Should().NotContain(l => l.Message.Contains(Mailbox, StringComparison.OrdinalIgnoreCase),
            "no email address in any log line (ADR-015)");
    }

    // ── Harness ─────────────────────────────────────────────────────────────────

    private static string WatermarkKey => "comm:inbound-poll-watermark:" + AccountId;

    private static MemoryDistributedCache NewCache() => new(Options.Create(new MemoryDistributedCacheOptions()));

    private sealed class Fixture
    {
        private readonly FakeTimeProvider _time;
        private readonly InboundPollingBackupService _sut;
        private readonly List<JobContract> _jobs = [];
        private readonly CapturingLogger _logger = new();

        public Fixture(
            FakeMailbox mailbox, IDistributedCache? cache = null, FakeTimeProvider? time = null, bool processorMarksRead = false)
        {
            _time = time ?? new FakeTimeProvider(Now);
            var graph = new GraphServiceClient(mailbox);
            _sut = new InboundPollingBackupService(
                CreateAccountService(),
                Mock.Of<IGraphClientFactory>(f => f.ForApp() == graph),
                CreateJobSubmission(_jobs, processorMarksRead ? mailbox : null),
                cache ?? NewCache(),
                _logger,
                _time);
        }

        public IReadOnlyList<string> EnqueuedMessageIds
            => _jobs.Select(j => j.SubjectId!).ToList();

        public IReadOnlyList<(LogLevel Level, string Message)> Logs => _logger.Entries;

        public Task PollAsync() => _sut.PollAllAccountsAsync(CancellationToken.None);

        public void Advance(TimeSpan by) => _time.Advance(by);
    }

    private sealed class CapturingLogger : ILogger<InboundPollingBackupService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static CommunicationAccountService CreateAccountService()
    {
        var account = new DataverseEntity("sprk_communicationaccount") { Id = AccountId };
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

    /// <summary>
    /// Captures each submitted job. With a mailbox, it also plays the processor: the message is marked read at once,
    /// so it is read before the poll requests its next page (IncomingCommunicationProcessor Step 7, running concurrently).
    /// </summary>
    private static JobSubmissionService CreateJobSubmission(List<JobContract> captured, FakeMailbox? markReadIn)
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
                markReadIn?.MarkRead(job.SubjectId!);
                return Task.CompletedTask;
            });

        return mock.Object;
    }

    /// <summary>
    /// One mail folder as Graph answers a `messages` listing: evaluates the request's own <c>receivedDateTime ge</c> +
    /// <c>isRead eq false</c> filter against each message's CURRENT read state, applies <c>$orderby</c>, <c>$skip</c> and
    /// <c>$top</c>, and, when more match, returns a Graph-style <c>@odata.nextLink</c> carrying the same query plus
    /// <c>$skip</c>. Like Graph, that offset is applied to the result set as it is when the nextLink is requested, so if
    /// messages were marked read in between it jumps over unread ones. Ties keep insertion order. Throws on any request
    /// it does not model, and on request number <see cref="FailRequestNumber"/> (1-based).
    /// </summary>
    private sealed class FakeMailbox : IRequestAdapter
    {
        private static readonly Regex FilterPattern = new(
            @"^receivedDateTime ge (?<from>\S+) and isRead eq false$", RegexOptions.Compiled);

        private readonly List<FakeMessage> _messages = [];

        public List<(DateTimeOffset From, int Top, int Skip)> Queries { get; } = [];

        public int? FailRequestNumber { get; set; }

        public IReadOnlyList<string> AllIds => _messages.Select(m => m.Id).ToList();

        public IReadOnlyList<string> UnreadIds => _messages.Where(m => !m.IsRead).Select(m => m.Id).ToList();

        public void Add(string id, DateTimeOffset received) => _messages.Add(new FakeMessage(id, received));

        public void MarkRead(string id) => _messages.Single(m => m.Id == id).IsRead = true;

        public string? BaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

        public ISerializationWriterFactory SerializationWriterFactory => throw new NotSupportedException();

        public void EnableBackingStore(IBackingStoreFactory backingStoreFactory) { }

        public Task<ModelType?> SendAsync<ModelType>(
            RequestInformation requestInfo, ParsableFactory<ModelType> factory,
            Dictionary<string, ParsableFactory<IParsable>>? errorMapping = default, CancellationToken cancellationToken = default)
            where ModelType : IParsable
        {
            if (typeof(ModelType) != typeof(MessageCollectionResponse))
                throw new NotSupportedException($"Unexpected Graph read {requestInfo.URI}.");

            NameValueCollection query = HttpUtility.ParseQueryString(requestInfo.URI.Query);
            var filter = FilterPattern.Match(query["$filter"] ?? "");
            if (!filter.Success)
                throw new NotSupportedException($"Unmodelled $filter '{query["$filter"]}'.");

            var from = DateTimeOffset.Parse(filter.Groups["from"].Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var top = int.Parse(query["$top"] ?? "10", CultureInfo.InvariantCulture);
            var skip = int.Parse(query["$skip"] ?? "0", CultureInfo.InvariantCulture);
            var orderby = query["$orderby"] ?? "";

            Queries.Add((from, top, skip));
            if (Queries.Count == FailRequestNumber)
                throw new HttpRequestException("Graph unavailable (test).");

            IEnumerable<FakeMessage> matching =
                _messages.Where(m => !m.IsRead && TruncateToSecond(m.Received) >= from);
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
                Value = page.Select(m => new Message { Id = m.Id, ReceivedDateTime = m.Received, IsRead = m.IsRead }).ToList(),
                OdataNextLink = more
                    ? $"{BaseUrl}/users/{Mailbox}/mailFolders/Inbox/messages" +
                      $"?$filter={Uri.EscapeDataString(query["$filter"]!)}&$orderby={Uri.EscapeDataString(orderby)}" +
                      $"&$top={top}&$skip={skip + page.Count}"
                    : null,
            };
            return Task.FromResult((ModelType?)(object)response);
        }

        private sealed class FakeMessage(string id, DateTimeOffset received)
        {
            public string Id { get; } = id;
            public DateTimeOffset Received { get; } = received;
            public bool IsRead { get; set; }
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
