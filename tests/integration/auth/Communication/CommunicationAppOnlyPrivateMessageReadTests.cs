using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056 (owner 2026-11-22: a private message is never filed; UAC-r2: fail closed wherever a
/// filing is read). The two APP-ONLY record reads (<see cref="CommunicationThreadReadService.ReadRegardingAppOnlyAsync"/>, the
/// external messages list, and <see cref="CommunicationThreadReadService.ReadRecentForRecordsAppOnlyAsync"/>, the notification
/// feed) keep a private message filed under the record outside the BFF out of the QUERY, for every caller kind. An ordinary
/// filed message is read as before, and an environment without the reference columns reads exactly as before.
/// </summary>
/// <remarks>The REAL <see cref="CommunicationThreadReadService"/> over an app-only query double that applies the
/// <c>_{column}_value eq null</c> clauses as Dataverse does and projects through the query's <c>$select</c>; the column-presence
/// read over <see cref="CommunicationSchemaDouble"/>.</remarks>
public class CommunicationAppOnlyPrivateMessageReadTests
{
    private const string CommunicationSet = "sprk_communications";
    private const string ThreadSet = "sprk_communicationthreads";
    private const int PrivacyOpen = 100000000;
    private const int StatusDelivered = 659490003;

    private static readonly Guid MatterId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000056");
    private static readonly Guid CallerSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222220056");
    private static readonly DateTimeOffset Since = DateTimeOffset.Parse("2026-10-01T00:00:00Z");

    private static readonly MessageRootsFilter AllRootsReadable = (_, _) => Task.FromResult<Func<MessageRootLookups, bool>>(_ => true);

    private readonly Mock<IAppOnlyCommunicationQuery> _appOnly = new();
    private readonly Mock<ISystemUserIdentityResolver> _identity = new();
    private readonly List<Dictionary<string, JsonElement>> _messages = new();
    private readonly Dictionary<Guid, Dictionary<string, JsonElement>> _threads = new();
    private readonly List<string> _messageQueries = new();

    private IGenericEntityService? _entities = CommunicationSchemaDouble.WithReferenceColumns();

    public CommunicationAppOnlyPrivateMessageReadTests()
    {
        _appOnly.Setup(q => q.QueryAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, CancellationToken>((set, odata, _) => Task.FromResult(Answer(set, odata!)));
        _identity.Setup(i => i.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
    }

    // ── the record's messages list (ReadRegardingAppOnlyAsync) ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056 must-fire: a private message filed under the record is not in the app-only list for an internal caller")]
    public async Task List_InternalCaller_DoesNotReadAPrivateMessageFiledUnderTheRecord()
    {
        var thread = AddThread();
        AddMessage(thread, privateAbout: MatterId, internalOnly: true);
        var ordinary = AddMessage(thread);

        (await ListAsync(CallerSystemUserId)).Should().Equal(ordinary);
        _messageQueries.Should().OnlyContain(q => q.Contains(NotPrivateClause), "the exclusion is in the query, not after it");
    }

    [Fact(DisplayName = "fm 056 must-fire: a private message filed under the record is not in the app-only list for a contact, on an Open thread")]
    public async Task List_ContactOnAnOpenThread_DoesNotReadAPrivateMessageFiledUnderTheRecord()
    {
        var thread = AddThread();
        // Not internal-only (a flag written outside the BFF): only the query keeps it out.
        AddMessage(thread, privateAbout: MatterId, internalOnly: false);
        var ordinary = AddMessage(thread);

        (await ListAsync(callerSystemUserId: null)).Should().Equal(ordinary);
    }

    [Fact(DisplayName = "fm 056 must-not-fire: an ordinary message filed under the record is still in the app-only list")]
    public async Task List_AnOrdinaryFiledMessage_IsStillRead()
    {
        var ordinary = AddMessage(AddThread());

        (await ListAsync(CallerSystemUserId)).Should().Equal(ordinary);
        (await ListAsync(callerSystemUserId: null)).Should().Equal(ordinary);
    }

    // ── the notification feed (ReadRecentForRecordsAppOnlyAsync) ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "fm 056 must-fire: a private message filed under the record is not in the app-only feed, for an internal caller or a contact")]
    public async Task Feed_DoesNotReadAPrivateMessageFiledUnderTheRecord()
    {
        var thread = AddThread();
        AddMessage(thread, privateAbout: MatterId, internalOnly: false);
        var ordinary = AddMessage(thread);

        (await FeedAsync(CallerSystemUserId)).Should().Equal(ordinary);
        (await FeedAsync(callerSystemUserId: null)).Should().Equal(ordinary);
        _messageQueries.Should().OnlyContain(q => q.Contains(NotPrivateClause));
    }

    // ── an environment without the columns, and a column-presence read that fails ────────────────────────────────────────────

    [Fact(DisplayName = "fm 056 must-not-fire: without the reference columns in metadata both queries are exactly as before")]
    public async Task ColumnsAbsentFromMetadata_LeaveBothQueriesAsToday()
    {
        _entities = CommunicationSchemaDouble.WithoutReferenceColumns();
        var ordinary = AddMessage(AddThread());

        (await ListAsync(CallerSystemUserId)).Should().Equal(ordinary);
        (await FeedAsync(CallerSystemUserId)).Should().Equal(ordinary);
        _messageQueries.Should().HaveCount(2).And.OnlyContain(q => !q.Contains("privatecontext"));
    }

    [Fact(DisplayName = "fm 056: a column-presence read that faults fails the app-only read; nothing is read")]
    public async Task APresenceReadFault_FailsTheRead()
    {
        var entities = new Mock<IGenericEntityService>();
        entities.Setup(e => e.GetColumnMetadataAsync("sprk_communication", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("metadata unavailable"));
        _entities = entities.Object;
        AddMessage(AddThread());

        await FluentActions.Awaiting(() => ListAsync(CallerSystemUserId)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => FeedAsync(CallerSystemUserId)).Should().ThrowAsync<InvalidOperationException>();
        _messageQueries.Should().BeEmpty();
    }

    [Fact(DisplayName = "fm 056: with no entity service the app-only reads refuse (fail closed)")]
    public async Task NoEntityService_FailsTheRead()
    {
        _entities = null;
        AddMessage(AddThread());

        await FluentActions.Awaiting(() => ListAsync(CallerSystemUserId)).Should().ThrowAsync<InvalidOperationException>();
        await FluentActions.Awaiting(() => FeedAsync(CallerSystemUserId)).Should().ThrowAsync<InvalidOperationException>();
        _messageQueries.Should().BeEmpty();
    }

    // ── fixture ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static readonly string NotPrivateClause =
        "(" + string.Join(" and ", PrivateContextColumns.All.Select(c => $"_{c}_value eq null")) + ")";

    private CommunicationThreadReadService Sut() => new(
        Mock.Of<IImpersonatedCommunicationQuery>(MockBehavior.Strict),
        new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
        Mock.Of<ICallerSystemUserResolver>(MockBehavior.Strict),
        _identity.Object,
        Mock.Of<ILogger<CommunicationThreadReadService>>(),
        _appOnly.Object,
        entities: _entities);

    private async Task<IReadOnlyList<Guid>> ListAsync(Guid? callerSystemUserId) =>
        (await Sut().ReadRegardingAppOnlyAsync("sprk_matter", MatterId, callerSystemUserId, AllRootsReadable, CancellationToken.None))
            .Messages.Select(m => m.MessageId).ToList();

    private async Task<IReadOnlyList<Guid>> FeedAsync(Guid? callerSystemUserId) =>
        (await Sut().ReadRecentForRecordsAppOnlyAsync(
            new[] { ("sprk_matter", MatterId) }, Since, callerSystemUserId, 50, AllRootsReadable, CancellationToken.None))
            .Messages.Select(m => m.Message.MessageId).ToList();

    private IReadOnlyList<Dictionary<string, JsonElement>> Answer(string set, string odata)
    {
        if (set == ThreadSet)
            return AppOnlySelectProjection.Project(_threads.Where(t => odata.Contains(t.Key.ToString())).Select(t => t.Value), odata);

        if (set != CommunicationSet)
            return Array.Empty<Dictionary<string, JsonElement>>();

        _messageQueries.Add(odata);
        // Dataverse applies each `_{column}_value eq null` clause of the $filter.
        var rows = _messages.Where(r => PrivateContextColumns.All.All(c =>
            !odata.Contains($"_{c}_value eq null") || !r.TryGetValue($"_{c}_value", out var v) || v.ValueKind == JsonValueKind.Null));
        return AppOnlySelectProjection.Project(rows.OrderByDescending(r => r["createdon"].GetDateTimeOffset()), odata);
    }

    private Guid AddThread()
    {
        var id = Guid.NewGuid();
        _threads[id] = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationthreadid"] = El(id.ToString("D")),
            ["sprk_privacystate"] = El(PrivacyOpen),
        };
        return id;
    }

    /// <summary>A sent message filed under the matter in <paramref name="threadId"/>; with <paramref name="privateAbout"/>, a
    /// private message about that matter (filed outside the BFF).</summary>
    private Guid AddMessage(Guid threadId, Guid? privateAbout = null, bool internalOnly = false)
    {
        var id = Guid.NewGuid();
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = El(id.ToString("D")),
            ["sprk_subject"] = El("Subject"),
            ["sprk_body"] = El("Body"),
            ["sprk_bodyformat"] = El(100000001),
            ["sprk_direction"] = El(100000000),
            ["createdon"] = El($"2026-10-14T10:00:{_messages.Count:00}Z"),
            ["statuscode"] = El(StatusDelivered),
            ["sprk_privilegeclassification"] = El(100000000),
            ["sprk_isinternalonly"] = El(internalOnly),
            ["sprk_isprivate"] = El(false),
            ["_sprk_regardingmatter_value"] = El(MatterId.ToString("D")),
            ["_sprk_communicationthread_value"] = El(threadId.ToString("D")),
        };
        if (privateAbout is { } about)
            row[$"_{PrivateContextColumns.Matter}_value"] = El(about.ToString("D"));
        _messages.Add(row);
        return id;
    }

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
