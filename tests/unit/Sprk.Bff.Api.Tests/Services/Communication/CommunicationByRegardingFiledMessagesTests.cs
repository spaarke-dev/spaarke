using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Ontology task 177 (D-217, owner 2026-11-10): a record's Messages list (<see cref="CommunicationThreadReadService.ReadByRegardingAsync"/>)
/// also shows every message whose OWN typed regarding lookup is that record, grouped under its thread, when the thread is anchored
/// elsewhere (the budget inquiry: thread anchored to the inquiry service request, the email also carries <c>sprk_regardingmatter</c>).
/// Only those messages, never the rest of that thread.
/// </summary>
/// <remarks>
/// The Dataverse read is an in-memory world that answers each IMPERSONATED query from its own <c>$filter</c> and <c>$select</c>, the
/// way Dataverse answers the caller: a row the caller may not read (No Access, Restricted for a flagged-external user, a thread
/// without a grant) is never returned, which is how those rules reach this read. The shared <see cref="CommunicationAccessFilter"/>
/// is the REAL one (internal-only), and the D7 open-thread gate runs for real over the world's thread rows.
/// </remarks>
public class CommunicationByRegardingFiledMessagesTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MatterId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherMatterId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ServiceRequestId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private const int Open = 100000000;
    private const int Private = 100000001;

    private readonly World _world = new();

    // ── must-fire: the inquiry shows under its matter ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MessageFiledToTheMatter_OnAThreadAnchoredToTheServiceRequest_ShowsUnderTheMatter_WithoutTheThreadsOtherMessages()
    {
        var matterThread = _world.Thread("Matter thread", Open, anchor: ("sprk_regardingmatter", MatterId), createdOn: "2026-11-01T09:00:00Z");
        var matterMessage = _world.Message(matterThread, regarding: [("sprk_regardingmatter", MatterId)]);
        var inquiryThread = _world.Thread("Budget inquiry", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId), createdOn: "2026-11-02T09:00:00Z");
        var inquiry = _world.Message(inquiryThread,
            regarding: [("sprk_regardingservicerequest", ServiceRequestId), ("sprk_regardingmatter", MatterId)]);
        var vendorReply = _world.Message(inquiryThread, regarding: [("sprk_regardingservicerequest", ServiceRequestId)]);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        // The anchored thread, exactly as before.
        var first = result.Threads[0];
        first.ThreadId.Should().Be(matterThread);
        first.Name.Should().Be("Matter thread");
        first.Messages.Select(m => m.MessageId).Should().Equal(matterMessage);

        // The inquiry, under ITS thread (id and name), with only the message filed to the matter.
        var added = result.Threads.Should().Contain(t => t.ThreadId == inquiryThread,
            "a message whose own sprk_regardingmatter is the record shows on the record (D-217)").Subject;
        added.Name.Should().Be("Budget inquiry");
        added.Messages.Select(m => m.MessageId).Should().Equal(inquiry);
        added.Count.Should().Be(1, "the count covers only what is shown");
        added.Messages.Select(m => m.MessageId).Should().NotContain(vendorReply, "the thread's other messages are not filed to the matter");

        result.ThreadCount.Should().Be(2);
        result.MessageCount.Should().Be(2);
    }

    [Fact]
    public async Task RecordWithNoAnchoredThread_StillShowsAMessageFiledToIt()
    {
        var inquiryThread = _world.Thread("Budget inquiry", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId));
        var inquiry = _world.Message(inquiryThread,
            regarding: [("sprk_regardingservicerequest", ServiceRequestId), ("sprk_regardingmatter", MatterId)]);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().ContainSingle().Which.Messages.Select(m => m.MessageId).Should().Equal(inquiry);
    }

    [Fact]
    public async Task AddedThreads_FollowTheAnchoredOnes_InCreatedOnOrder()
    {
        var anchored = _world.Thread("Anchored", Open, anchor: ("sprk_regardingmatter", MatterId), createdOn: "2026-11-05T09:00:00Z");
        _world.Message(anchored, regarding: [("sprk_regardingmatter", MatterId)]);
        var later = _world.Thread("Later", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId), createdOn: "2026-11-04T09:00:00Z");
        var earlier = _world.Thread("Earlier", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId), createdOn: "2026-11-03T09:00:00Z");
        _world.Message(later, regarding: [("sprk_regardingmatter", MatterId)], createdOn: "2026-11-04T10:00:00Z");
        _world.Message(earlier, regarding: [("sprk_regardingmatter", MatterId)], createdOn: "2026-11-04T11:00:00Z");

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Select(t => t.ThreadId).Should().Equal(anchored, earlier, later);
    }

    // ── de-duplication ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MessageOnAnAnchoredThread_AlsoFiledToTheRecord_AppearsOnce()
    {
        var matterThread = _world.Thread("Matter thread", Open, anchor: ("sprk_regardingmatter", MatterId));
        var message = _world.Message(matterThread, regarding: [("sprk_regardingmatter", MatterId)]);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().ContainSingle();
        result.Threads.SelectMany(t => t.Messages).Select(m => m.MessageId).Should().Equal(message);
        result.MessageCount.Should().Be(1);
    }

    // ── must-not-fire: the access rules apply to the added messages ────────────────────────────────────────────

    [Fact]
    public async Task InternalOnlyMessageFiledToTheRecord_IsNotShown_ToAFlaggedExternalUser()
    {
        var thread = _world.Thread("Inquiry", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId));
        _world.Message(thread, regarding: [("sprk_regardingmatter", MatterId)], internalOnly: true);

        var result = await Sut(external: true).ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().BeEmpty("the only filed message is internal-only, so its thread contributes nothing");
        result.MessageCount.Should().Be(0);
        Serialized(result).Should().NotContain("Inquiry");
    }

    [Fact]
    public async Task InternalOnlyMessageFiledToTheRecord_IsShown_ToAnInternalUser()
    {
        // Control for the test above: the same row, an internal caller.
        var thread = _world.Thread("Inquiry", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId));
        var message = _world.Message(thread, regarding: [("sprk_regardingmatter", MatterId)], internalOnly: true);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().ContainSingle().Which.Messages.Select(m => m.MessageId).Should().Equal(message);
    }

    [Fact]
    public async Task MessageFiledToTheRecord_OnAPrivateThread_IsNotShown_ToAFlaggedExternalUser_AndTheNameNeverLeaves()
    {
        var privateThread = _world.Thread("Settlement strategy", Private, anchor: ("sprk_regardingservicerequest", ServiceRequestId));
        _world.Message(privateThread, regarding: [("sprk_regardingmatter", MatterId)]);

        var result = await Sut(external: true).ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().BeEmpty();
        Serialized(result).Should().NotContain("Settlement strategy");
    }

    [Fact]
    public async Task ExternalCaller_TheAddedThreadRead_CarriesTheOpenOnlyClause()
    {
        var thread = _world.Thread("Inquiry", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId));
        _world.Message(thread, regarding: [("sprk_regardingmatter", MatterId)]);

        var result = await Sut(external: true).ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().ContainSingle().Which.ThreadId.Should().Be(thread);
        _world.AddedThreadReads.Should().ContainSingle()
            .Which.Should().Contain($"sprk_privacystate eq {Open}").And.Contain("statecode eq 0");
    }

    [Fact]
    public async Task MessageFiledToTheRecord_OnAThreadTheCallerCannotRead_ContributesNothing()
    {
        var hiddenThread = _world.Thread("Board only", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId), readable: false);
        _world.Message(hiddenThread, regarding: [("sprk_regardingmatter", MatterId)]);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().BeEmpty("a thread row the impersonated read does not return contributes nothing");
        result.MessageCount.Should().Be(0);
        Serialized(result).Should().NotContain("Board only");
    }

    [Fact]
    public async Task MessageFiledToTheRecord_TheCallerHasNoAccessTo_IsNotShown()
    {
        // A No Access (deny-list) entry, or Restricted for a flagged-external user, is Dataverse's answer to the impersonated read.
        var thread = _world.Thread("Inquiry", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId));
        _world.Message(thread, regarding: [("sprk_regardingmatter", MatterId)], readable: false);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().BeEmpty();
        Serialized(result).Should().NotContain("Inquiry");
    }

    [Fact]
    public async Task MessageFiledToAnotherRecord_IsNotShown()
    {
        var thread = _world.Thread("Other", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId));
        _world.Message(thread, regarding: [("sprk_regardingmatter", OtherMatterId)]);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().BeEmpty();
    }

    [Fact]
    public async Task MessageFiledToTheRecord_WithNoThread_IsNotAdded()
    {
        _world.Message(threadId: null, regarding: [("sprk_regardingmatter", MatterId)]);

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().BeEmpty("known limit: a message with no thread has no group to show it under");
    }

    // ── bounds ──────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ManyAddedThreads_AreReadInBatches_NotOnePerThread()
    {
        for (var i = 0; i < 60; i++)
        {
            var thread = _world.Thread($"Inquiry {i}", Open, anchor: ("sprk_regardingservicerequest", ServiceRequestId));
            _world.Message(thread, regarding: [("sprk_regardingmatter", MatterId)]);
        }

        var result = await Sut().ReadByRegardingAsync("sprk_matter", MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().HaveCount(60);
        _world.AddedThreadReads.Should().HaveCount(2, "60 distinct threads in chunks of 50: two batched reads");
    }

    [Theory]
    [InlineData("sprk_project", "sprk_regardingproject")]
    [InlineData("contact", "sprk_regardingperson")]
    [InlineData("sprk_servicerequest", "sprk_regardingservicerequest")]
    public async Task TheFiledQuery_UsesTheSharedRegardingFieldForTheEntityType(string entityType, string field)
    {
        var thread = _world.Thread("Elsewhere", Open, anchor: ("sprk_regardingmatter", OtherMatterId));
        var message = _world.Message(thread, regarding: [(field, MatterId)]);

        var result = await Sut().ReadByRegardingAsync(entityType, MatterId, Caller(), CancellationToken.None);

        result.Threads.Should().ContainSingle().Which.Messages.Select(m => m.MessageId).Should().Equal(message);
        _world.MessageQueries.Should().Contain(q => q.Contains($"_{field}_value eq {MatterId}"));
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────────────────────

    private CommunicationThreadReadService Sut(bool external = false)
    {
        var resolver = new Mock<ICallerSystemUserResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(CallerSystemUserId, It.IsAny<CancellationToken>())).ReturnsAsync(external);

        return new CommunicationThreadReadService(
            _world,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            resolver.Object,
            identity.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private static string Serialized(RegardingReadResult result) => JsonSerializer.Serialize(result);

    /// <summary>
    /// The caller's view of Dataverse. Each query is answered from its own <c>$filter</c>: a thread read by anchor or by id
    /// (honouring <c>statecode eq 0</c> and the Open-only clause), a message read by thread ids or by one regarding lookup. A row
    /// marked unreadable is never returned (impersonation). Every answer is projected through the query's <c>$select</c>.
    /// </summary>
    private sealed class World : IImpersonatedCommunicationQuery
    {
        private readonly List<Dictionary<string, JsonElement>> _threads = new();
        private readonly List<Dictionary<string, JsonElement>> _messages = new();
        private readonly HashSet<Guid> _unreadable = new();
        private readonly List<(string Set, string OData)> _queries = new();
        private int _sequence;

        public IReadOnlyList<string> MessageQueries =>
            _queries.Where(q => q.Set == "sprk_communications").Select(q => q.OData).ToList();

        /// <summary>The by-id thread reads that select the thread's name (the added threads' rows), not the D7 gate's privacy read.</summary>
        public IReadOnlyList<string> AddedThreadReads => _queries
            .Where(q => q.Set == "sprk_communicationthreads"
                        && q.OData.Contains("sprk_communicationthreadid eq", StringComparison.Ordinal)
                        && Select(q.OData).Contains("sprk_name"))
            .Select(q => q.OData)
            .ToList();

        public Guid Thread(string name, int privacy, (string Field, Guid Id) anchor, string? createdOn = null, bool readable = true)
        {
            var id = Guid.NewGuid();
            var row = new Dictionary<string, JsonElement>
            {
                ["sprk_communicationthreadid"] = El(id.ToString()),
                ["sprk_name"] = El(name),
                ["sprk_privacystate"] = El(privacy),
                ["sprk_ispinned"] = El(false),
                ["statecode"] = El(0),
                ["createdon"] = El(createdOn ?? NextTime()),
                [$"_{anchor.Field}_value"] = El(anchor.Id.ToString()),
            };
            _threads.Add(row);
            if (!readable)
                _unreadable.Add(id);
            return id;
        }

        public Guid Message(
            Guid? threadId, (string Field, Guid Id)[] regarding, bool internalOnly = false, bool readable = true,
            string? createdOn = null)
        {
            var id = Guid.NewGuid();
            var row = new Dictionary<string, JsonElement>
            {
                ["sprk_communicationid"] = El(id.ToString()),
                ["sprk_body"] = El("body"),
                ["statecode"] = El(0),
                ["createdon"] = El(createdOn ?? NextTime()),
                ["sprk_isinternalonly"] = El(internalOnly),
                ["sprk_privilegeclassification"] = El(100000000),
            };
            if (threadId is { } t)
                row["_sprk_communicationthread_value"] = El(t.ToString());
            foreach (var (field, recordId) in regarding)
                row[$"_{field}_value"] = El(recordId.ToString());
            _messages.Add(row);
            if (!readable)
                _unreadable.Add(id);
            return id;
        }

        public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
            string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
        {
            callerSystemUserId.Should().Be(CallerSystemUserId, "every read runs impersonated as the caller");
            var odata = odataQuery ?? string.Empty;
            _queries.Add((entitySetName, odata));
            var filter = Regex.Match(odata, @"\$filter=([^&]*)").Groups[1].Value;

            IEnumerable<Dictionary<string, JsonElement>> hit = entitySetName switch
            {
                "sprk_communicationthreads" => Threads(filter),
                "sprk_communications" => Messages(filter),
                _ => Array.Empty<Dictionary<string, JsonElement>>(),
            };

            hit = hit.Where(r => !_unreadable.Contains(Guid.Parse(r[Key(entitySetName)].GetString()!)));
            if (filter.Contains("statecode eq 0", StringComparison.Ordinal))
                hit = hit.Where(r => r["statecode"].GetInt32() == 0);
            hit = hit.OrderBy(r => r["createdon"].GetString(), StringComparer.Ordinal);
            var top = Regex.Match(odata, @"\$top=(\d+)");
            if (top.Success)
                hit = hit.Take(int.Parse(top.Groups[1].Value));

            var select = Select(odata);
            IReadOnlyList<Dictionary<string, JsonElement>> projected = hit
                .Select(r => r.Where(kv => select.Count == 0 || select.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value))
                .ToList();
            return Task.FromResult(projected);
        }

        private IEnumerable<Dictionary<string, JsonElement>> Threads(string filter)
        {
            var ids = Ids(filter, "sprk_communicationthreadid");
            var rows = ids.Count > 0
                ? _threads.Where(r => ids.Contains(r["sprk_communicationthreadid"].GetString()!))
                : _threads.Where(r => MatchesLookup(r, filter));
            if (filter.Contains($"sprk_privacystate eq {Open}", StringComparison.Ordinal))
                rows = rows.Where(r => r["sprk_privacystate"].GetInt32() == Open);
            return rows;
        }

        private IEnumerable<Dictionary<string, JsonElement>> Messages(string filter)
        {
            var threadIds = Ids(filter, "_sprk_communicationthread_value");
            return threadIds.Count > 0
                ? _messages.Where(r => r.TryGetValue("_sprk_communicationthread_value", out var t) && threadIds.Contains(t.GetString()!))
                : _messages.Where(r => MatchesLookup(r, filter));
        }

        /// <summary>The one <c>_{lookup}_value eq {id}</c> condition a by-record query carries.</summary>
        private static bool MatchesLookup(Dictionary<string, JsonElement> row, string filter)
        {
            var m = Regex.Match(filter, @"(_sprk_regarding\w+_value) eq ([0-9a-fA-F-]{36})");
            return m.Success
                   && !m.Groups[1].Value.Equals("_sprk_communicationthread_value", StringComparison.Ordinal)
                   && row.TryGetValue(m.Groups[1].Value, out var v)
                   && string.Equals(v.GetString(), m.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> Ids(string filter, string key) =>
            Regex.Matches(filter, $@"(?<![\w]){Regex.Escape(key)} eq ([0-9a-fA-F-]{{36}})")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        private static HashSet<string> Select(string odata)
        {
            var value = Regex.Match(odata, @"\$select=([^&]*)").Groups[1].Value;
            return value.Length == 0 ? new HashSet<string>() : value.Split(',').ToHashSet(StringComparer.Ordinal);
        }

        private static string Key(string set) => set == "sprk_communicationthreads" ? "sprk_communicationthreadid" : "sprk_communicationid";

        private string NextTime() => new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(++_sequence).ToString("yyyy-MM-ddTHH:mm:ssZ");

        private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
    }
}
