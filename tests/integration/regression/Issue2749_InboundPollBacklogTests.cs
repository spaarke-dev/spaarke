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
// The same PR moved the poll from a hand-rolled PeriodicTimer BackgroundService to an IScheduledJob on ScheduledJobHost
// (ADR-052 §1, ADR-036). Each test drives the job's run entry, ExecuteAsync(JobRunContext): one dispatch = one poll of
// every receive-enabled account. Consecutive ticks can run on different instances under the host's lease; an instance that
// holds an in-memory cursor uses it and ignores the persisted watermark, which it reads only when it has no cursor.
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Store;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.DI;
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
        cache.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never, "a cycle whose read failed writes no watermark");
    }

    [Fact]
    public async Task Poll_WhenOneCacheReadThrows_DoesNotLetTheWatermarkJumpPastAPersistedBacklog()
    {
        // A previous instance was capped mid-backlog and persisted a watermark 41 minutes back, with 120 unread
        // messages after it (all older than the 15-minute default lookback). This instance's first cache read throws
        // once. It must not poll from now − 15 min AND persist a later watermark over the backlog one, or those 120 are
        // never enqueued: the next cycle must re-read the cache and resume from the persisted watermark.
        var seeded = Now.AddMinutes(-41);
        var cache = NewCache();
        cache.SetString(WatermarkKey, seeded.ToString("O", CultureInfo.InvariantCulture));
        var flaky = new FirstReadFailsCache(cache);
        var mailbox = new FakeMailbox();
        for (var i = 0; i < 120; i++)
            mailbox.Add($"m{i:D3}", Now.AddMinutes(-40).AddSeconds(i));
        var fixture = new Fixture(mailbox, cache: flaky);

        await fixture.PollAsync(); // the cache read throws

        fixture.EnqueuedMessageIds.Should().BeEmpty("nothing is newer than the default lookback");
        flaky.SetCount.Should().Be(0, "a cycle whose cache read failed writes no watermark");
        DateTimeOffset.Parse(cache.GetString(WatermarkKey)!, CultureInfo.InvariantCulture)
            .Should().Be(seeded, "the persisted watermark never moves past the oldest un-enqueued message");

        fixture.Advance(TimeSpan.FromMinutes(5));
        await fixture.PollAsync(); // the cache answers again

        fixture.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds, "the backlog after the persisted watermark is reached");
        fixture.EnqueuedMessageIds.Should().OnlyHaveUniqueItems();
        fixture.Logs.Where(l => l.Level == LogLevel.Warning && l.Message.Contains("watermark")).Should().ContainSingle();
    }

    [Fact]
    public async Task Poll_WhenOneSecondHoldsMoreThanTheMaxPage_LogsTheStallAndKeepsTheWatermarkInThatSecond()
    {
        // 1100 unread messages share one second, the processor never marks them read (a mailbox granted only
        // Mail.Read), and one more message arrives a minute later. Poll 1 stops at the per-poll cap; poll 2 widens its
        // page up to MaxPageSize and still cannot get past the second, so it stalls. A stalled poll must keep the
        // watermark in that second like a capped one, not move it to poll start − 2 min over the messages behind it.
        var tie = Now.AddMinutes(-5);
        var mailbox = new FakeMailbox();
        for (var i = 0; i < 1100; i++)
            mailbox.Add($"t{i:D4}", tie);
        mailbox.Add("later", tie.AddMinutes(1));
        var cache = NewCache();
        var fixture = new Fixture(mailbox, cache: cache);

        await fixture.PollAsync();
        fixture.Advance(TimeSpan.FromMinutes(5));
        await fixture.PollAsync();

        var stall = fixture.Logs.Where(l => l.Message.Contains("one second holds more than the max page")).ToList();
        stall.Should().ContainSingle();
        stall.Single().Level.Should().Be(LogLevel.Warning);
        fixture.Logs.Should().NotContain(l => l.Message.Contains(Mailbox, StringComparison.OrdinalIgnoreCase));
        DateTimeOffset.Parse(cache.GetString(WatermarkKey)!, CultureInfo.InvariantCulture)
            .Should().Be(tie, "the watermark stays in the stalled second, not poll start − 2 min");
        fixture.EnqueuedMessageIds.Should().OnlyHaveUniqueItems();
        fixture.EnqueuedMessageIds.Should().NotContain("later", "it is behind the stalled second");
    }

    [Fact]
    public async Task Poll_WhenGraphFails_LogsTheErrorCodeAndNeverTheMailboxAddress()
    {
        // Graph's own error text can quote the address ("The requested user '...' is invalid"), and the exception's
        // Message/ToString carry it. The poll logs the Graph error code and the exception type instead of the exception.
        var mailbox = new FakeMailbox
        {
            FailRequestNumber = 1,
            FailWith = new ODataError
            {
                Error = new MainError { Code = "ErrorInvalidUser", Message = $"The requested user '{Mailbox}' is invalid." },
            },
        };
        mailbox.Add("one", Now.AddMinutes(-3));
        var fixture = new Fixture(mailbox);

        await fixture.PollAsync();

        var failure = fixture.Logs.Single(l => l.Level == LogLevel.Error && l.Message.Contains("Failed to poll account"));
        fixture.Logs.Should().NotContain(l => l.Message.Contains(Mailbox, StringComparison.OrdinalIgnoreCase));
        fixture.Logs.Should().NotContain(
            l => l.Exception != null && l.Exception.ToString().Contains(Mailbox, StringComparison.OrdinalIgnoreCase),
            "the logged exception object would carry the address in its message");
        failure.Message.Should().Contain("ErrorInvalidUser").And.Contain(nameof(ODataError));
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

    // ── The scheduled job (ADR-036): one dispatch = one poll ────────────────────

    [Fact]
    public async Task Run_EachDispatch_PollsEveryReceiveEnabledMailboxOnceAndReportsWhatItEnqueued()
    {
        var mailbox = new FakeMailbox();
        mailbox.Add("one", Now.AddMinutes(-3));
        mailbox.Add("two", Now.AddMinutes(-2));
        var fixture = new Fixture(mailbox);

        var first = await fixture.PollAsync();

        fixture.AccountQueries.Should().Be(1, "one run reads the receive-enabled accounts once");
        mailbox.Queries.Should().ContainSingle("one run polls the mailbox once (one page holds both messages)");
        first.Success.Should().BeTrue();
        first.ProcessedItems.Should().Be(2);
        fixture.EnqueuedMessageIds.Should().Equal("one", "two");
        fixture.Logs.Should().ContainSingle(l => l.Message.Contains("[INBOUND-POLL] heartbeat attempt=1"),
            "one heartbeat per attempt (ADR-036 A1 rule 5)");

        mailbox.Add("three", Now.AddMinutes(1));
        fixture.Advance(TimeSpan.FromMinutes(5));
        var second = await fixture.PollAsync();

        fixture.AccountQueries.Should().Be(2);
        mailbox.Queries.Should().HaveCount(2, "the next dispatch is the next poll — no timer, no startup cycle");
        second.ProcessedItems.Should().Be(1);
        fixture.EnqueuedMessageIds.Should().Equal("one", "two", "three");
    }

    [Fact]
    public async Task Run_WhenAMailboxFails_CompletesAsAFailedRunWithoutThrowingOrNamingTheAddress()
    {
        // The host logs a thrown exception object, and Graph's message can quote the address: the run records the
        // failure instead of throwing, and the next tick (whose watermark did not move) redoes the mailbox.
        var mailbox = new FakeMailbox
        {
            FailRequestNumber = 1,
            FailWith = new ODataError
            {
                Error = new MainError { Code = "ErrorInvalidUser", Message = $"The requested user '{Mailbox}' is invalid." },
            },
        };
        mailbox.Add("one", Now.AddMinutes(-3));
        var fixture = new Fixture(mailbox);

        var result = await fixture.PollAsync();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty().And.NotContain(Mailbox);
        result.ProcessedItems.Should().Be(0);

        fixture.Advance(TimeSpan.FromMinutes(5));
        var retry = await fixture.PollAsync();
        retry.Success.Should().BeTrue();
        fixture.EnqueuedMessageIds.Should().Equal("one");
    }

    [Fact]
    public async Task Run_WhenCancelled_PropagatesTheCancellationInsteadOfLoggingAPollFailure()
    {
        var mailbox = new FakeMailbox();
        mailbox.Add("one", Now.AddMinutes(-3));
        var cache = NewCache();
        var fixture = new Fixture(mailbox, cache: cache);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var run = () => fixture.PollAsync(cancelled.Token);

        await run.Should().ThrowAsync<OperationCanceledException>("the host records a cancelled run (NFR-07)");
        fixture.Logs.Should().NotContain(l => l.Level >= LogLevel.Warning,
            "a shutdown is neither a failed mailbox nor a failed watermark read");
        fixture.EnqueuedMessageIds.Should().BeEmpty();
        cache.GetString(WatermarkKey).Should().BeNull("a cancelled poll moves no watermark");
    }

    [Fact]
    public async Task Run_AfterAnotherInstanceRanALaterTick_ReListsFromItsOwnCursorAndTheProcessorAbsorbsTheDuplicate()
    {
        // Under the host's lease each tick runs on ONE instance, not always the same one. Instance B polls, instance A
        // runs the next tick and advances the persisted watermark, then B runs again with its own older cursor in memory.
        // B trusts its own cursor and ignores the persisted value (which could be a guess), so it re-lists from there and
        // enqueues m2 a second time, which the processor's sprk_graphmessageid check drops. Nothing is skipped.
        var mailbox = new FakeMailbox();
        mailbox.Add("m1", Now.AddMinutes(-3));
        var cache = NewCache();
        var time = new FakeTimeProvider(Now);
        var instanceB = new Fixture(mailbox, cache: cache, time: time);
        var instanceA = new Fixture(mailbox, cache: cache, time: time);

        await instanceB.PollAsync(); // 10:00 — enqueues m1, watermark 09:58

        mailbox.Add("m2", Now.AddMinutes(1));
        time.Advance(TimeSpan.FromMinutes(5));
        await instanceA.PollAsync(); // 10:05 — no cursor yet, resumes from B's persisted 09:58, enqueues m2, watermark 10:03

        time.Advance(TimeSpan.FromMinutes(5));
        var bSecondRun = mailbox.Queries.Count;
        await instanceB.PollAsync(); // 10:10 — B's own cursor says 09:58

        mailbox.Queries[bSecondRun].From.Should().Be(Now.AddMinutes(-2), "B starts from its own cursor, not A's persisted watermark");
        instanceB.EnqueuedMessageIds.Should().Equal("m1", "m2");
        instanceA.EnqueuedMessageIds.Should().Equal("m2");
        instanceB.EnqueuedMessageIds.Concat(instanceA.EnqueuedMessageIds).Distinct().Should().BeEquivalentTo(mailbox.AllIds,
            "the only cost of ignoring the persisted watermark is the duplicate m2");
    }

    [Fact]
    public async Task Run_WhenTheWatermarkKeyIsLostAndAnotherInstanceWritesADefaultLookbackWatermark_TheBacklogHolderStillEnqueuesTheBacklog()
    {
        // Instance A is draining a backlog older than the 15-minute default lookback and holds a real backlog cursor.
        // The persisted key is then lost (TTL, a cache flush). Instance B, with no cursor, starts from the default
        // lookback, finds nothing, and writes now - 2 min. That is a guess, not a claim that everything before it was
        // enqueued. A must not adopt it: its next run still enqueues the backlog.
        var total = InboundPollingBackupService.MaxMessagesPerPoll + 120;
        var mailbox = new FakeMailbox();
        for (var i = 0; i < total; i++)
            mailbox.Add($"m{i:D4}", Now.AddMinutes(-60).AddSeconds(i));
        var cache = NewCache();
        cache.SetString(WatermarkKey, Now.AddMinutes(-61).ToString("O", CultureInfo.InvariantCulture));
        var time = new FakeTimeProvider(Now);
        var instanceA = new Fixture(mailbox, cache: cache, time: time, processorMarksRead: true);
        var instanceB = new Fixture(mailbox, cache: cache, time: time, processorMarksRead: true);

        await instanceA.PollAsync(); // capped: 500 of 620 enqueued, cursor stays inside the backlog
        instanceA.EnqueuedMessageIds.Should().HaveCount(InboundPollingBackupService.MaxMessagesPerPoll);

        cache.Remove(WatermarkKey);
        time.Advance(TimeSpan.FromMinutes(5));
        await instanceB.PollAsync(); // 10:05 — no key, no cursor: lists from 09:50, finds nothing, writes 10:03
        instanceB.EnqueuedMessageIds.Should().BeEmpty("the backlog is older than B's default lookback");
        DateTimeOffset.Parse(cache.GetString(WatermarkKey)!, CultureInfo.InvariantCulture).Should().Be(Now.AddMinutes(3));

        time.Advance(TimeSpan.FromMinutes(5));
        await instanceA.PollAsync(); // 10:10 — must use its own backlog cursor, not B's 10:03

        instanceA.EnqueuedMessageIds.Should().BeEquivalentTo(mailbox.AllIds, "A still reaches the 120 messages left in the backlog");
        instanceA.EnqueuedMessageIds.Should().OnlyHaveUniqueItems();
        mailbox.UnreadIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_WhenTheAccountQueryFaults_RecordsAFailedRunAndLogsAnErrorWithoutAnAddress()
    {
        // The lenient account query swallows a Dataverse fault and answers an empty list, which recorded a Dataverse
        // outage as a successful run with no accounts. The strict query throws, so the run reports the outage.
        var mailbox = new FakeMailbox();
        mailbox.Add("one", Now.AddMinutes(-3));
        var fixture = new Fixture(mailbox, accountQueryFault: new InvalidOperationException("Dataverse unavailable (test)"));

        var result = await fixture.PollAsync();

        result.Success.Should().BeFalse("a Dataverse outage is not a successful run");
        result.ErrorMessage.Should().NotBeNullOrEmpty().And.NotContain(Mailbox);
        result.ProcessedItems.Should().Be(0);
        result.ResultJson.Should().Contain("\"accountQueryFailed\":true");
        fixture.Logs.Should().Contain(l => l.Level == LogLevel.Error && l.Message.Contains("Failed to query receive-enabled accounts"));
        fixture.Logs.Should().NotContain(l => l.Message.Contains(Mailbox, StringComparison.OrdinalIgnoreCase));
        mailbox.Queries.Should().BeEmpty("no mailbox is polled without accounts");

        fixture.AccountQueryFault = null;
        fixture.Advance(TimeSpan.FromMinutes(5));
        var retry = await fixture.PollAsync();
        retry.Success.Should().BeTrue("the next run retries the query");
        fixture.EnqueuedMessageIds.Should().Equal("one");
    }

    [Fact]
    public async Task Run_WhenCancelledDuringTheAccountQuery_PropagatesTheCancellation()
    {
        // The lenient account query also swallowed a cancellation, so a shutdown during the query looked like an empty run.
        using var cts = new CancellationTokenSource();
        var fixture = new Fixture(new FakeMailbox(), accountQueryFault: new OperationCanceledException(cts.Token));
        await cts.CancelAsync();

        var run = () => fixture.PollAsync(cts.Token);

        await run.Should().ThrowAsync<OperationCanceledException>("the host records a cancelled run (NFR-07)");
        fixture.Logs.Should().NotContain(l => l.Level >= LogLevel.Warning, "a shutdown is not a failed account query");
    }

    [Fact]
    public async Task Run_WhenCancelledWhileSavingTheWatermark_PropagatesTheCancellationWithoutAWarning()
    {
        // A shutdown that lands on the watermark write is a cancelled run, not a "failed to persist" warning.
        using var cts = new CancellationTokenSource();
        var mailbox = new FakeMailbox();
        mailbox.Add("one", Now.AddMinutes(-3));
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);
        cache.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, byte[], DistributedCacheEntryOptions, CancellationToken>((_, _, _, token) =>
            {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        var fixture = new Fixture(mailbox, cache: cache.Object);

        var run = () => fixture.PollAsync(cts.Token);

        await run.Should().ThrowAsync<OperationCanceledException>();
        fixture.Logs.Should().NotContain(l => l.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Run_WhenThePersistedWatermarkIsOlderThanTheInMemoryOne_DoesNotMoveTheCursorBackwards()
    {
        // This instance's last watermark write did not land (a Redis blip), so the cache still holds an older value.
        // The in-memory cursor is the later true claim; the run must not go back to the older one.
        var mailbox = new FakeMailbox();
        mailbox.Add("m1", Now.AddMinutes(-3));
        var cache = NewCache();
        var fixture = new Fixture(mailbox, cache: cache);

        await fixture.PollAsync(); // watermark 09:58
        cache.SetString(WatermarkKey, Now.AddMinutes(-10).ToString("O", CultureInfo.InvariantCulture));

        fixture.Advance(TimeSpan.FromMinutes(5));
        var secondRun = mailbox.Queries.Count;
        await fixture.PollAsync();

        mailbox.Queries[secondRun].From.Should().Be(Now.AddMinutes(-2), "the cursor never moves backwards");
        DateTimeOffset.Parse(cache.GetString(WatermarkKey)!, CultureInfo.InvariantCulture)
            .Should().Be(Now.AddMinutes(3), "and the run writes its own later watermark back");
    }

    [Fact]
    public void Registration_SchedulesTheJobEnabledEveryFiveMinutes_AndStartsNoHostedService()
    {
        // The interval the PeriodicTimer kept, now a cron the host dispatches once per tick across instances.
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Mock.Of<ICommunicationDataverseService>());
        services.AddSingleton(Mock.Of<IGenericEntityService>());
        services.AddSingleton(Mock.Of<IDistributedCache>());
        services.AddSingleton(Mock.Of<IGraphClientFactory>());
        services.AddSingleton(new Mock<Azure.Messaging.ServiceBus.ServiceBusClient>().Object);
        services.AddSingleton<JobSubmissionService>();
        services.AddCommunicationModule(configuration);
        using var provider = services.BuildServiceProvider();

        var registration = provider.GetServices<ScheduledJobRegistration>()
            .Single(r => r.Job.JobId == InboundPollingBackupService.JobIdConstant);

        registration.CronSchedule.Should().Be("*/5 * * * *");
        registration.Enabled.Should().BeTrue("the backup poll may be the only working inbound path (#1561)");
        registration.Job.Should().BeSameAs(provider.GetRequiredService<InboundPollingBackupService>(),
            "one singleton keeps the per-mailbox cursors across runs");
        services.Should().NotContain(
            d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
                && d.ImplementationType == typeof(InboundPollingBackupService),
            "the poll is no longer a hand-rolled timer BackgroundService (ADR-052 §1)");
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
        private int _accountQueries;

        public Fixture(
            FakeMailbox mailbox, IDistributedCache? cache = null, FakeTimeProvider? time = null, bool processorMarksRead = false,
            Exception? accountQueryFault = null)
        {
            _time = time ?? new FakeTimeProvider(Now);
            AccountQueryFault = accountQueryFault;
            var graph = new GraphServiceClient(mailbox);
            _sut = new InboundPollingBackupService(
                CreateAccountService(() => _accountQueries++, () => AccountQueryFault),
                Mock.Of<IGraphClientFactory>(f => f.ForApp() == graph),
                CreateJobSubmission(_jobs, processorMarksRead ? mailbox : null),
                cache ?? NewCache(),
                _logger,
                _time);
        }

        /// <summary>What the Dataverse account query throws while set (an outage, or a cancellation).</summary>
        public Exception? AccountQueryFault { get; set; }

        public IReadOnlyList<string> EnqueuedMessageIds
            => _jobs.Select(j => j.SubjectId!).ToList();

        public IReadOnlyList<LogEntry> Logs => _logger.Entries;

        /// <summary>How many times a run asked Dataverse for the receive-enabled accounts.</summary>
        public int AccountQueries => _accountQueries;

        /// <summary>One scheduler dispatch: the job's run entry, as ScheduledJobHost calls it.</summary>
        public Task<JobRunResult> PollAsync(CancellationToken ct = default)
            => _sut.ExecuteAsync(
                new JobRunContext(Guid.NewGuid(), "issue-2749-test", JobRunTrigger.Scheduled, new Dictionary<string, object>()),
                ct);

        public void Advance(TimeSpan by) => _time.Advance(by);
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger : ILogger<InboundPollingBackupService>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }

    /// <summary>
    /// A cache whose first <c>GetAsync</c> throws (a transient Redis fault) and which then behaves as the wrapped
    /// MemoryDistributedCache. Counts writes so a test can show a failed read persisted nothing.
    /// </summary>
    private sealed class FirstReadFailsCache(IDistributedCache inner) : IDistributedCache
    {
        private int _reads;

        public int SetCount { get; private set; }

        public byte[]? Get(string key) => inner.Get(key);

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Interlocked.Increment(ref _reads) == 1
                ? throw new InvalidOperationException("cache read failed (test)")
                : inner.GetAsync(key, token);

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            SetCount++;
            inner.Set(key, value, options);
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            SetCount++;
            return inner.SetAsync(key, value, options, token);
        }

        public void Refresh(string key) => inner.Refresh(key);

        public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

        public void Remove(string key) => inner.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
    }

    private static CommunicationAccountService CreateAccountService(Action onQuery, Func<Exception?> fault)
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
            .Returns<string, string, CancellationToken>((_, _, _) =>
            {
                onQuery();
                return fault() is { } thrown ? throw thrown : Task.FromResult(new[] { account });
            });

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

        /// <summary>What the failing request throws; defaults to an HttpRequestException.</summary>
        public Exception? FailWith { get; set; }

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

            // Like the HTTP stack under the real adapter, a cancelled request does not go out.
            cancellationToken.ThrowIfCancellationRequested();

            Queries.Add((from, top, skip));
            if (Queries.Count == FailRequestNumber)
                throw FailWith ?? new HttpRequestException("Graph unavailable (test).");

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
