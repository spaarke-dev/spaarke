using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
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
            if (odata!.Contains("$orderby=createdon desc"))
                return AnswerThreadList(odata);

            var hit = _threads.Where(t => odata!.Contains(t.Key.ToString())).Select(t => t.Value);
            // Dataverse applies the $filter: a query that carries the Open-only clause (D9, fix round 6) returns only Open threads.
            if (odata.Contains("sprk_privacystate eq 100000000"))
                hit = hit.Where(IsOpenRow);
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

    private static bool IsOpenRow(Dictionary<string, JsonElement> row) =>
        row.TryGetValue("sprk_privacystate", out var v) && v.ValueKind == JsonValueKind.Number && v.GetInt32() == PrivacyOpen;

    // ── thread LIST fake (ListThreadsAsync / ReadByRegardingAsync): the $filter is evaluated the way Dataverse would ──

    private readonly List<(Guid Id, string Name, DateTimeOffset CreatedOn, int Privacy)> _listedThreads = new();
    private readonly List<string> _listQueries = new();

    private Guid AddListedThread(string name, string createdOn, int privacy)
    {
        var id = Guid.NewGuid();
        _listedThreads.Add((id, name, DateTimeOffset.Parse(createdOn), privacy));
        return id;
    }

    private IReadOnlyList<Dictionary<string, JsonElement>> AnswerThreadList(string odata)
    {
        _listQueries.Add(odata);
        var filter = odata.Split("$filter=")[1].Split('&')[0];
        var top = int.Parse(odata.Split("$top=")[1].Split('&')[0]);
        var rows = _listedThreads
            .Where(t => !filter.Contains("sprk_privacystate eq 100000000") || t.Privacy == PrivacyOpen)
            .OrderByDescending(t => t.CreatedOn).ThenByDescending(t => t.Id)
            .Where(t => !filter.Contains("createdon lt ") || t.CreatedOn < CursorOf(filter) || (t.CreatedOn == CursorOf(filter) && filter.Contains("sprk_communicationthreadid lt ") && t.Id.CompareTo(CursorIdOf(filter)) < 0))
            .Take(top)
            .Select(t => new Dictionary<string, JsonElement>
            {
                ["sprk_communicationthreadid"] = El(t.Id.ToString("D")),
                ["sprk_name"] = El(t.Name),
                ["createdon"] = El(t.CreatedOn.ToString("o")),
                ["sprk_ispinned"] = El(false),
            });
        return AppOnlySelectProjection.Project(rows, odata);
    }

    private static DateTimeOffset CursorOf(string filter) =>
        DateTimeOffset.Parse(Uri.UnescapeDataString(filter.Split("createdon lt ")[1].Split(' ')[0]));

    private static Guid CursorIdOf(string filter) =>
        Guid.Parse(filter.Split("sprk_communicationthreadid lt ")[1].Split(')')[0]);

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

    // ── fix round 6 (F3): a systemuser flagged sprk_isexternal on the EXTERNAL routes ───────────────

    [Fact]
    public async Task ExternalRoutes_ForAFlaggedExternalSystemUser_ApplyTheGate_NotTheInternalBypass()
    {
        // ReadRegardingAppOnlyAsync is handed the caller's systemuserid. A flagged-external systemuser HAS an id, so the
        // gate must key on the resolved IsExternalAsync bit, never on "no systemuser id" (that would let this caller see
        // Private-thread, no-thread and internal-only items exactly as an internal user does).
        CallerIsFlaggedExternal();
        var open = AddThread(PrivacyOpen);
        var shown = AddMessage(open);
        AddMessage(AddThread(PrivacyPrivate));
        AddMessage(threadId: null);
        AddMessage(open, internalOnly: true);

        (await ExternalRegardingReadAsync(CallerSystemUserId)).Should().Equal(shown);
        _threadQueries.Should().ContainSingle().Which.Identity.Should().Be("app-only");
    }

    [Fact]
    public async Task ExternalFeed_ForAFlaggedExternalSystemUser_ApplyTheGate_NotTheInternalBypass()
    {
        CallerIsFlaggedExternal();
        var open = AddMessage(AddThread(PrivacyOpen));
        AddMessage(AddThread(PrivacyPrivate));
        AddMessage(threadId: null);

        var result = await Sut().ReadRecentForRecordsAppOnlyAsync(
            new[] { ("sprk_matter", MatterId) }, DateTimeOffset.Parse("2026-10-01T00:00:00Z"), CallerSystemUserId, 50, AllRootsReadable, CancellationToken.None);

        result.Messages.Select(m => m.Message.MessageId).Should().Equal(open);
    }

    // ── fix round 6 (F1): thread LISTS never carry a Private thread's name to an external caller ─────

    private const string OpenOnlyClause = "sprk_privacystate eq 100000000";

    [Fact]
    public async Task ListThreads_ForAFlaggedExternalUser_AsksDataverseForOpenThreadsOnly_AndNeverReceivesAPrivateName()
    {
        CallerIsFlaggedExternal();
        var open = AddListedThread("Open thread", "2026-10-14T09:00:00Z", PrivacyOpen);
        AddListedThread("Secret settlement strategy", "2026-10-14T10:00:00Z", PrivacyPrivate);

        var result = await Sut().ListThreadsAsync(Caller(), null, null, null, CancellationToken.None);

        result.Threads.Select(t => t.ThreadId).Should().Equal(open);
        result.Threads.Select(t => t.Name).Should().NotContain("Secret settlement strategy");
        _listQueries.Should().ContainSingle().Which.Should().Contain($"$filter=statecode eq 0 and {OpenOnlyClause}&$orderby=");
    }

    [Fact]
    public async Task ListThreads_ForAFlaggedExternalUser_PagesOverVisibleThreads_NotOverAPageThatAPrivateThreadFilled()
    {
        // The clause is in the query, so $top (page + 1) and the next-page token are computed over visible threads. A filter
        // applied after the query would see [Private, B] for the first page and drop a row the page was sized for.
        CallerIsFlaggedExternal();
        var older = AddListedThread("A", "2026-10-14T09:00:00Z", PrivacyOpen);
        var newer = AddListedThread("B", "2026-10-14T10:00:00Z", PrivacyOpen);
        AddListedThread("Newest private", "2026-10-14T11:00:00Z", PrivacyPrivate);

        var first = await Sut().ListThreadsAsync(Caller(), null, 1, null, CancellationToken.None);
        first.Threads.Select(t => t.ThreadId).Should().Equal(newer);
        first.HasMore.Should().BeTrue();
        first.NextPageToken.Should().NotBeNull();

        var second = await Sut().ListThreadsAsync(Caller(), null, 1, first.NextPageToken, CancellationToken.None);
        second.Threads.Select(t => t.ThreadId).Should().Equal(older);
        second.HasMore.Should().BeFalse();
        second.NextPageToken.Should().BeNull();
        _listQueries.Should().OnlyContain(q => q.Contains(OpenOnlyClause));
    }

    [Fact]
    public async Task ListThreads_ForAnInternalUser_IsUnchanged_APrivateThreadIsListed_AndNoPrivacyClauseIsSent()
    {
        var open = AddListedThread("Open thread", "2026-10-14T09:00:00Z", PrivacyOpen);
        var priv = AddListedThread("Private thread", "2026-10-14T10:00:00Z", PrivacyPrivate);

        var result = await Sut().ListThreadsAsync(Caller(), null, null, null, CancellationToken.None);

        result.Threads.Select(t => t.ThreadId).Should().Equal(priv, open);
        _listQueries.Should().ContainSingle().Which.Should().NotContain("sprk_privacystate");
    }

    [Fact]
    public async Task ByRegarding_ThreadQuery_CarriesTheOpenOnlyClauseForAnExternalCaller_AndNotForAnInternalOne()
    {
        var threadQueries = new List<string>();
        _impersonated.Setup(q => q.QueryAsync(ThreadSet, It.Is<string>(o => o.Contains("sprk_regardingmatter")), CallerSystemUserId, It.IsAny<CancellationToken>()))
            .Callback<string, string, Guid, CancellationToken>((_, odata, _, _) => threadQueries.Add(odata))
            .ReturnsAsync(new List<Dictionary<string, JsonElement>>());

        await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);
        CallerIsFlaggedExternal();
        await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        threadQueries.Should().HaveCount(2);
        threadQueries[0].Should().NotContain("sprk_privacystate");
        threadQueries[1].Should().Contain($"$filter=_sprk_regardingmatter_value eq {MatterId} and statecode eq 0 and {OpenOnlyClause}&$orderby=");
    }

    // ── fix round 6 (D9): an external caller cannot rename, pin or deactivate a thread that is not explicitly Open ──

    private readonly Mock<IThreadResolver> _threadActions = new();

    private async Task<IResult> RunThreadActionAsync(string action, Guid threadId)
    {
        var http = new DefaultHttpContext { User = Caller() };
        return action switch
        {
            "rename" => await CommunicationEndpoints.RenameThreadAsync(
                threadId, new RenameThreadRequest { Name = "New name" }, Sut(), _threadActions.Object, http, CancellationToken.None),
            "pin" => await CommunicationEndpoints.SetThreadPinnedAsync(
                threadId, new SetThreadPinnedRequest { Pinned = true }, Sut(), _threadActions.Object, http, CancellationToken.None),
            "deactivate" => await CommunicationEndpoints.DeactivateThreadAsync(
                threadId, Sut(), _threadActions.Object, http, CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    private void VerifyNoThreadWrite()
    {
        _threadActions.Verify(t => t.RenameThreadAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _threadActions.Verify(t => t.SetPinnedAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _threadActions.Verify(t => t.DeactivateThreadAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rename_ByAFlaggedExternalUser_OfAPrivateThread_Is403AndRenamesNothing()
    {
        CallerIsFlaggedExternal();
        var priv = AddThread(PrivacyPrivate);

        var act = () => RunThreadActionAsync("rename", priv);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.StatusCode.Should().Be(403);
        ex.Code.Should().Be("THREAD_RENAME_FORBIDDEN");
        VerifyNoThreadWrite();
    }

    [Fact]
    public async Task Pin_ByAFlaggedExternalUser_OfAPrivateThread_Is403AndPinsNothing()
    {
        CallerIsFlaggedExternal();
        var priv = AddThread(PrivacyPrivate);

        var act = () => RunThreadActionAsync("pin", priv);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.StatusCode.Should().Be(403);
        ex.Code.Should().Be("THREAD_PIN_FORBIDDEN");
        VerifyNoThreadWrite();
    }

    [Fact]
    public async Task Deactivate_ByAFlaggedExternalUser_OfAPrivateThread_Is403AndDeactivatesNothing()
    {
        CallerIsFlaggedExternal();
        var priv = AddThread(PrivacyPrivate);

        var act = () => RunThreadActionAsync("deactivate", priv);

        var ex = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        ex.StatusCode.Should().Be(403);
        ex.Code.Should().Be("THREAD_DELETE_FORBIDDEN");
        VerifyNoThreadWrite();
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("pin")]
    [InlineData("deactivate")]
    public async Task ThreadAction_ByAFlaggedExternalUser_OfAThreadWithNoPrivacyValue_IsAlsoRefused(string action)
    {
        // "Not explicitly Open" includes a thread with no privacy value: the same refusal, fail closed.
        CallerIsFlaggedExternal();
        var blank = AddThread(privacy: null);

        var act = () => RunThreadActionAsync(action, blank);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(403);
        VerifyNoThreadWrite();
    }

    [Theory]
    [InlineData("rename", true, PrivacyOpen)]
    [InlineData("pin", true, PrivacyOpen)]
    [InlineData("deactivate", true, PrivacyOpen)]
    [InlineData("rename", false, PrivacyPrivate)]
    [InlineData("pin", false, PrivacyPrivate)]
    [InlineData("deactivate", false, PrivacyPrivate)]
    public async Task ThreadAction_IsAllowed_ForAnExternalUserOnAnOpenThread_AndForAnInternalUserOnAnyThread(
        string action, bool external, int privacy)
    {
        if (external)
            CallerIsFlaggedExternal();
        var thread = AddThread(privacy);
        _threadActions.Setup(t => t.RenameThreadAsync(thread, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("New name");
        _threadActions.Setup(t => t.SetPinnedAsync(thread, true, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await RunThreadActionAsync(action, thread);

        switch (action)
        {
            case "rename": _threadActions.Verify(t => t.RenameThreadAsync(thread, "New name", It.IsAny<CancellationToken>()), Times.Once); break;
            case "pin": _threadActions.Verify(t => t.SetPinnedAsync(thread, true, It.IsAny<CancellationToken>()), Times.Once); break;
            default: _threadActions.Verify(t => t.DeactivateThreadAsync(thread, It.IsAny<CancellationToken>()), Times.Once); break;
        }
    }

    [Fact]
    public async Task CanCallerSeeThread_AsksDataverseForAnOpenThread_OnlyForAnExternalCaller()
    {
        var thread = AddThread(PrivacyOpen);

        await Sut().CanCallerSeeThreadAsync(thread, Caller(), CancellationToken.None);
        CallerIsFlaggedExternal();
        await Sut().CanCallerSeeThreadAsync(thread, Caller(), CancellationToken.None);

        _threadQueries.Should().HaveCount(2);
        _threadQueries[0].OData.Should().Be($"$select=sprk_communicationthreadid&$filter=sprk_communicationthreadid eq {thread}&$top=1");
        _threadQueries[1].OData.Should().Be($"$select=sprk_communicationthreadid&$filter=sprk_communicationthreadid eq {thread} and {OpenOnlyClause}&$top=1");
    }

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
