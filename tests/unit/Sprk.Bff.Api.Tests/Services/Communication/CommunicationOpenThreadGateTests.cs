using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;


/// <summary>
/// Owner decision D7 (external-access r3 task 065 fix round 5, ISS-009 / #1859): every EXTERNAL read of
/// <c>sprk_communication</c> hides an item unless its <c>sprk_communicationthread</c> exists and reads EXPLICITLY Open at read
/// time, on top of the <c>sprk_isinternalonly</c> flag. Driven through the REAL <see cref="CommunicationThreadReadService"/> over
/// two doubles at the Dataverse seams (app-only and impersonated), each answering by entity set and projecting rows through the
/// query's own <c>$select</c>, so a dropped column or a per-row read shows up as a failure.
/// </summary>
public class CommunicationOpenThreadGateTests
{
    private const string CommunicationSet = "sprk_communications";
    private const string ThreadSet = "sprk_communicationthreads";
    private const int PrivacyOpen = 100000000;
    private const int PrivacyPrivate = 100000001;
    private const int StatusDelivered = 659490003;

    private static readonly Guid MatterId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid CallerSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IAppOnlyCommunicationQuery> _appOnly = new();
    private readonly Mock<IImpersonatedCommunicationQuery> _impersonated = new();
    private readonly Mock<ISystemUserIdentityResolver> _identity = new();
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();

    private readonly List<Dictionary<string, JsonElement>> _messages = new();
    private readonly Dictionary<Guid, Dictionary<string, JsonElement>> _threads = new();
    private readonly List<(string Identity, string OData)> _threadQueries = new();
    private readonly List<string> _messageQueries = new();

    public CommunicationOpenThreadGateTests()
    {
        _appOnly.Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, CancellationToken>((set, odata, _) => Task.FromResult(Answer("app-only", set, odata)));
        _impersonated.Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
            .Returns<string, string, Guid, CancellationToken>((set, odata, _, _) => Task.FromResult(Answer("impersonated", set, odata)));
        _resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        _identity.Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    private IReadOnlyList<Dictionary<string, JsonElement>> Answer(string identity, string set, string? odata)
    {
        if (set == ThreadSet)
        {
            _threadQueries.Add((identity, odata!));
            var hit = _threads.Where(t => odata!.Contains(t.Key.ToString())).Select(t => t.Value);
            return AppOnlySelectProjection.Project(hit, odata);
        }

        if (set == CommunicationSet)
        {
            _messageQueries.Add(odata!);
            // A by-id read ("sprk_communicationid eq {id}") returns just that row, as Dataverse would.
            var rows = _messages.Where(r => !odata!.Contains("$filter=sprk_communicationid eq ")
                                            || odata.Contains($"$filter=sprk_communicationid eq {r["sprk_communicationid"].GetString()}"));
            return AppOnlySelectProjection.Project(rows.OrderByDescending(r => r["createdon"].GetDateTimeOffset()), odata);
        }

        return Array.Empty<Dictionary<string, JsonElement>>();
    }

    private CommunicationThreadReadService Sut() => new(
        _impersonated.Object,
        new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
        _resolver.Object,
        _identity.Object,
        Mock.Of<ILogger<CommunicationThreadReadService>>(),
        _appOnly.Object);

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private static readonly MessageRootsFilter AllRootsReadable = (_, _) => Task.FromResult<Func<MessageRootLookups, bool>>(_ => true);

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Seeds a thread. <paramref name="privacy"/> null models a thread row with no privacy value.</summary>
    private Guid AddThread(int? privacy)
    {
        var id = Guid.NewGuid();
        var row = new Dictionary<string, JsonElement> { ["sprk_communicationthreadid"] = El(id.ToString("D")) };
        if (privacy is { } value)
            row["sprk_privacystate"] = El(value);
        _threads[id] = row;
        return id;
    }

    /// <summary>Seeds a shareable message (flag false) regarding the matter. <paramref name="threadId"/> null = no thread.</summary>
    private Guid AddMessage(Guid? threadId, bool internalOnly = false, string createdOn = "2026-10-14T10:00:00Z")
    {
        var id = Guid.NewGuid();
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(id.ToString("D")),
            ["sprk_subject"] = El("Subject"),
            ["sprk_body"] = El("Body"),
            ["sprk_bodyformat"] = El(100000001),
            ["sprk_direction"] = El(100000000),
            ["createdon"] = El(createdOn),
            ["statuscode"] = El(StatusDelivered),
            ["sprk_privilegeclassification"] = El(100000000),
            ["sprk_isinternalonly"] = El(internalOnly),
            ["sprk_isprivate"] = El(false),
            ["_sprk_regardingmatter_value"] = El(MatterId.ToString("D")),
        };
        if (threadId is { } t)
            row["_sprk_communicationthread_value"] = El(t.ToString("D"));
        _messages.Add(row);
        return id;
    }

    private async Task<IReadOnlyList<Guid>> ExternalRegardingReadAsync(Guid? callerSystemUserId = null)
    {
        var result = await Sut().ReadRegardingAppOnlyAsync("sprk_matter", MatterId, callerSystemUserId, AllRootsReadable, CancellationToken.None);
        return result.Messages.Select(m => m.MessageId).ToList();
    }

    private void CallerIsFlaggedExternal() =>
        _identity.Setup(i => i.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    // ── app-only external read (ReadRegardingAppOnlyAsync -> FilterExternalMessagesAsync) ─────────────

    [Fact]
    public async Task ExternalRead_ItemInAnOpenThread_IsVisible()
    {
        var message = AddMessage(AddThread(PrivacyOpen));

        (await ExternalRegardingReadAsync()).Should().Equal(message);
    }

    [Fact]
    public async Task ExternalRead_ItemInAPrivateThread_IsHidden()
    {
        AddMessage(AddThread(PrivacyPrivate));

        (await ExternalRegardingReadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalRead_ItemInAThreadWithNoPrivacyValue_IsHidden()
    {
        AddMessage(AddThread(privacy: null));

        (await ExternalRegardingReadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalRead_ItemWithNoThread_IsHidden()
    {
        AddMessage(threadId: null);

        (await ExternalRegardingReadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalRead_ItemWhoseThreadRowCannotBeRead_IsHidden()
    {
        // The lookup names a thread the privacy read does not return (deleted, or not readable).
        AddMessage(Guid.NewGuid());

        (await ExternalRegardingReadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalRead_ThreadSwitchedToPrivateAfterTheItemWasSent_HidesTheEarlierItem_D7()
    {
        // The item was sent while its thread was Open (flag false). The thread is then made Private: the earlier item is
        // hidden too, because the gate reads the thread at READ time, not the flag written at send time.
        var thread = AddThread(PrivacyOpen);
        var earlier = AddMessage(thread, createdOn: "2026-10-13T09:00:00Z");
        (await ExternalRegardingReadAsync()).Should().Equal(earlier);

        _threads[thread]["sprk_privacystate"] = El(PrivacyPrivate);

        (await ExternalRegardingReadAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ExternalRead_StillHidesAnInternalOnlyItem_EvenInAnOpenThread()
    {
        var open = AddThread(PrivacyOpen);
        var shareable = AddMessage(open);
        AddMessage(open, internalOnly: true);

        (await ExternalRegardingReadAsync()).Should().Equal(shareable);
    }

    [Fact]
    public async Task ExternalRead_MixedPage_KeepsOnlyOpenThreadItems_WithOneBatchedThreadRead()
    {
        // 30 rows over 3 distinct threads (Open, Private, none-valued): ONE thread query naming each distinct thread once,
        // never one per row.
        var open = AddThread(PrivacyOpen);
        var priv = AddThread(PrivacyPrivate);
        var blank = AddThread(privacy: null);
        var expected = new List<Guid>();
        for (var i = 0; i < 10; i++)
        {
            expected.Add(AddMessage(open, createdOn: $"2026-10-14T10:{i:00}:00Z"));
            AddMessage(priv, createdOn: $"2026-10-14T11:{i:00}:00Z");
            AddMessage(blank, createdOn: $"2026-10-14T12:{i:00}:00Z");
        }

        var visible = await ExternalRegardingReadAsync();

        visible.Should().BeEquivalentTo(expected);
        _threadQueries.Should().ContainSingle();
        var odata = _threadQueries.Single().OData;
        foreach (var id in new[] { open, priv, blank })
            odata.Split(id.ToString()).Length.Should().Be(2, "each distinct thread is named exactly once");
    }

    [Fact]
    public async Task ExternalRead_ThreadReadsAreChunked_NotPerRow()
    {
        // 120 distinct Open threads: ceil(120 / 50) = 3 queries, still no per-row reads.
        var expected = new List<Guid>();
        for (var i = 0; i < 120; i++)
            expected.Add(AddMessage(AddThread(PrivacyOpen), createdOn: $"2026-10-14T10:{i / 60:00}:{i % 60:00}Z"));

        (await ExternalRegardingReadAsync()).Should().BeEquivalentTo(expected);
        _threadQueries.Should().HaveCount(3);
    }

    [Fact]
    public async Task ExternalRead_ThreadQueryShape_IsPinned()
    {
        // A dropped column would make every thread read as "no privacy value" and hide everything. Pin the shape.
        var thread = AddThread(PrivacyOpen);
        AddMessage(thread);

        await ExternalRegardingReadAsync();

        var odata = _threadQueries.Single().OData;
        odata.Should().Be($"$select=sprk_communicationthreadid,sprk_privacystate&$filter=sprk_communicationthreadid eq {thread}&$top=1");
        _threadQueries.Single().Identity.Should().Be("app-only");
    }

    [Fact]
    public async Task ExternalRead_MessageQuerySelectsTheThreadLookup()
    {
        AddMessage(AddThread(PrivacyOpen));

        await ExternalRegardingReadAsync();

        var select = _messageQueries.Single().Split("$select=")[1].Split('&')[0].Split(',');
        select.Should().Contain("_sprk_communicationthread_value");
    }

    [Fact]
    public async Task ExternalRead_WhenTheThreadReadFaults_TheReadFails_NothingIsReturned()
    {
        AddMessage(AddThread(PrivacyOpen));
        _appOnly.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));

        var act = () => ExternalRegardingReadAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ExternalRead_WithoutTheAppOnlySeam_Refuses()
    {
        AddMessage(AddThread(PrivacyOpen));
        var sut = new CommunicationThreadReadService(
            _impersonated.Object, new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object, _identity.Object, Mock.Of<ILogger<CommunicationThreadReadService>>(), appOnlyQuery: null);

        var act = () => sut.ReadRegardingAppOnlyAsync("sprk_matter", MatterId, null, AllRootsReadable, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── internal callers are unchanged ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task InternalCaller_StillSeesEveryCase_AndReadsNoThread()
    {
        var all = new[]
        {
            AddMessage(AddThread(PrivacyOpen)),
            AddMessage(AddThread(PrivacyPrivate)),
            AddMessage(AddThread(privacy: null)),
            AddMessage(threadId: null),
            AddMessage(Guid.NewGuid()),
            AddMessage(AddThread(PrivacyOpen), internalOnly: true),
        };

        // A systemuser that is not flagged external.
        var visible = await ExternalRegardingReadAsync(CallerSystemUserId);

        visible.Should().BeEquivalentTo(all);
        _threadQueries.Should().BeEmpty();
    }

    // ── recent-messages feed (ReadRecentForRecordsAppOnlyAsync) and its unread count ─────────────────

    [Fact]
    public async Task Feed_ReportsOnlyOpenThreadItems_SoItsUnreadCountNeverIncludesAHiddenItem()
    {
        var open = AddMessage(AddThread(PrivacyOpen));
        AddMessage(AddThread(PrivacyPrivate));
        AddMessage(threadId: null);

        var result = await Sut().ReadRecentForRecordsAppOnlyAsync(
            new[] { ("sprk_matter", MatterId) }, DateTimeOffset.Parse("2026-10-01T00:00:00Z"), null, 50, AllRootsReadable, CancellationToken.None);

        // The notification feed derives its unreadCount from exactly these messages (ExternalNotificationFeedEndpoints).
        result.Messages.Select(m => m.Message.MessageId).Should().Equal(open);
    }

    // ── impersonated reads for a licensed user flagged sprk_isexternal ───────────────────────────────

    private async Task<int> UnreadCountAsync(Guid thread) =>
        (await Sut().GetUnreadCountAsync(thread, Caller(), since: null, CancellationToken.None)).UnreadCount;

    [Fact]
    public async Task UnreadCount_ForAFlaggedExternalUser_CountsOnlyAnOpenThreadsItems()
    {
        CallerIsFlaggedExternal();
        var open = AddThread(PrivacyOpen);
        var priv = AddThread(PrivacyPrivate);
        AddMessage(open);
        AddMessage(open);

        (await UnreadCountAsync(open)).Should().Be(2);
        // The same two rows, but the thread is Private now: the badge is zero, the same as the list.
        _threads[open]["sprk_privacystate"] = El(PrivacyPrivate);
        (await UnreadCountAsync(open)).Should().Be(0);
        (await UnreadCountAsync(priv)).Should().Be(0);
    }

    [Fact]
    public async Task UnreadCount_ForAnInternalUser_IsUnchanged_EvenForAPrivateThread()
    {
        var priv = AddThread(PrivacyPrivate);
        AddMessage(priv);
        AddMessage(priv);

        (await UnreadCountAsync(priv)).Should().Be(2);
        _threadQueries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadThread_ForAFlaggedExternalUser_ReturnsNothingForAPrivateThread_AndTheItemsOfAnOpenOne()
    {
        CallerIsFlaggedExternal();
        var open = AddThread(PrivacyOpen);
        var priv = AddThread(PrivacyPrivate);
        var shown = AddMessage(open);

        (await Sut().ReadThreadAsync(open, Caller(), null, null, CancellationToken.None)).Messages.Select(m => m.MessageId).Should().Equal(shown);
        (await Sut().ReadThreadAsync(priv, Caller(), null, null, CancellationToken.None)).Messages.Should().BeEmpty();
        (await Sut().ReadThreadAsync(Guid.NewGuid(), Caller(), null, null, CancellationToken.None)).Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadThread_ForAnInternalUser_IsUnchanged_EvenForAPrivateThread()
    {
        var priv = AddThread(PrivacyPrivate);
        var message = AddMessage(priv);

        (await Sut().ReadThreadAsync(priv, Caller(), null, null, CancellationToken.None)).Messages.Select(m => m.MessageId).Should().Equal(message);
        // Only the existing thread-name read happens; no privacy read.
        _threadQueries.Should().OnlyContain(q => !q.OData.Contains("sprk_privacystate"));
    }

    [Fact]
    public async Task ByRegarding_ForAFlaggedExternalUser_DropsPrivateNoneAndMissingThreadItems()
    {
        CallerIsFlaggedExternal();
        var open = AddThread(PrivacyOpen);
        var shown = AddMessage(open);
        AddMessage(AddThread(PrivacyPrivate));
        AddMessage(threadId: null);

        // The by-regarding read first lists the regarding record's threads, then the messages across them.
        _impersonated.Setup(q => q.QueryAsync("sprk_communicationthreads", It.Is<string>(o => o.Contains("sprk_regardingmatter")), CallerSystemUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_threads.Keys.Select(id => new Dictionary<string, JsonElement> { ["sprk_communicationthreadid"] = El(id.ToString("D")) }).ToList());

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.SelectMany(t => t.Messages).Select(m => m.MessageId).Should().Equal(shown);
        result.MessageCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadVisibleCommunication_ForAFlaggedExternalUser_HidesAnItemInAPrivateThread()
    {
        CallerIsFlaggedExternal();
        var inOpen = AddMessage(AddThread(PrivacyOpen));
        var inPrivate = AddMessage(AddThread(PrivacyPrivate));

        (await Sut().ReadVisibleCommunicationAsync(inOpen, Caller(), null, CancellationToken.None)).Should().NotBeNull();
        (await Sut().ReadVisibleCommunicationAsync(inPrivate, Caller(), null, CancellationToken.None)).Should().BeNull();
    }

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
