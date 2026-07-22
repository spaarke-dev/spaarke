using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Notifications;
using Sprk.Bff.Api.Services.Identity;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Notifications;

/// <summary>
/// Vertical-slice seam (ADR-038, task 022) for the kind-generic pending/poll fallback endpoint
/// (GET /api/notifications/pending, spec FR-06/NFR-04) — the shared ADR-032 degrade path every
/// consumer (task 021's client library, R3's polling-only hosts) relies on.
/// </summary>
/// <remarks>
/// <para>
/// Exercises the REAL production handler
/// <see cref="NotificationsEndpoints.GetPendingAsync"/> (marked <c>internal</c> specifically for this
/// seam per its own XML doc) end to end: real oid claim extraction, the real
/// <see cref="OutboxService"/> query construction/envelope projection, and — for the identity
/// resolver — the REAL <see cref="SystemUserIdentityResolver"/> production class with only its
/// Dataverse (<see cref="IDataverseService"/>) and cache (<see cref="IDistributedCache"/>) boundaries
/// doubled. This matches the established doubling convention in this seam category
/// (<c>OutboxServiceSeamTests</c>, <c>SignalRDeliverySeamTests</c>): double only the external-system
/// boundary, run the actual business logic. No <c>Mock&lt;HttpMessageHandler&gt;</c> is used anywhere.
/// </para>
/// <para>
/// The "SignalR off" degrade path is proven structurally: the handler under test has NO SignalR
/// dependency in its signature at all (it composes only <see cref="OutboxService"/> +
/// <see cref="ISystemUserIdentityResolver"/>) — it is SignalR-agnostic BY CONSTRUCTION, which is the
/// ADR-032 contract task 022 implements. The tests below additionally construct the scenario the
/// task's acceptance criteria describe: a producer writes an outbox row while SignalR is unreachable,
/// then the pending endpoint's handler is called and returns that row.
/// </para>
/// </remarks>
public sealed class PendingPollFallbackSeamTests
{
    private static OutboxService BuildOutbox(FakeGenericEntityService store, DateTimeOffset now)
        => new(store, NullLogger<OutboxService>.Instance, new FixedTimeProvider(now));

    private static CommunicationEnvelope BuildCommunicationEnvelope(Guid regardingId, string sender = "Jane Doe") => new()
    {
        Kind = NotificationKind.CommunicationArrived,
        CommunicationId = Guid.NewGuid(),
        ThreadId = Guid.NewGuid(),
        Channel = "email",
        Direction = "inbound",
        RegardingRecordId = regardingId.ToString(),
        SenderDisplay = sender,
        BadgeDelta = 1
    };

    private static SuggestionEnvelope BuildSuggestionEnvelope(Guid regardingId) => new()
    {
        Kind = NotificationKind.Suggestion,
        SuggestionId = Guid.NewGuid(),
        Source = "daily-briefing",
        RegardingRecordId = regardingId.ToString(),
        Title = "Review upcoming deadline",
        ActionHint = "review",
        ExpiresAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z")
    };

    private static HttpContext BuildAuthenticatedContext(string oid)
    {
        var identity = new ClaimsIdentity(new[] { new Claim("oid", oid) }, authenticationType: "TestAuth");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (a) The ADR-032 degrade path itself: producer writes to the durable outbox
    //     while SignalR is off/unreachable; the poll endpoint still returns the row.
    //     The handler is SignalR-agnostic BY CONSTRUCTION (no SignalR dependency in
    //     its signature at all) — proven here by never touching any SignalR type.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingAsync_AfterOutboxWrite_ReturnsWrittenRow_RegardlessOfSignalRState()
    {
        // Arrange — real OutboxService; Dataverse boundary faked. No SignalR type appears anywhere in
        // this test: the handler under test never depends on SignalR delivery state (ADR-032).
        var store = new FakeGenericEntityService();
        var now = DateTimeOffset.Parse("2026-07-21T12:00:00Z");
        var outbox = BuildOutbox(store, now);
        var systemUserId = Guid.NewGuid();
        var oid = Guid.NewGuid().ToString();
        var regardingId = Guid.NewGuid();
        var envelope = BuildCommunicationEnvelope(regardingId).Validate();

        // Producer writes the durable outbox row FIRST (write-before-ping invariant — no ping ever
        // happens in this test, simulating SignalR being off/unreachable).
        var outboxRowId = await outbox.WriteAsync(
            systemUserId,
            NotificationKind.CommunicationArrived,
            envelope,
            regardingRecordId: regardingId.ToString(),
            regardingRecordType: "sprk_communication");

        var resolver = ResolverMapping(oid, systemUserId);
        var context = BuildAuthenticatedContext(oid);

        // Act — the production pending-endpoint handler, called directly (internal, InternalsVisibleTo).
        var result = await NotificationsEndpoints.GetPendingAsync(context, outbox, resolver, kind: null, ct: default);

        // Assert — the written row comes back through the poll fallback.
        var response = result.Should().BeAssignableTo<IValueHttpResult<NotificationsPendingResponse>>().Subject;
        response.Value!.Items.Should().ContainSingle(i => i.OutboxRowId == outboxRowId);
        var item = response.Value!.Items.Single(i => i.OutboxRowId == outboxRowId);
        item.Kind.Should().Be(NotificationKind.CommunicationArrived);
        item.Envelope.GetProperty("communicationId").GetGuid().Should().Be(envelope.CommunicationId);
        item.Envelope.GetProperty("senderDisplay").GetString().Should().Be("Jane Doe");
        // NFR-02/03: no body/content/token field can ride along — the envelope shape itself has none.
        item.Envelope.TryGetProperty("body", out _).Should().BeFalse();
        item.Envelope.TryGetProperty("actionToken", out _).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (b) Cross-user isolation — user A's call never returns user B's row, and the
    //     handler accepts no target-user parameter that could be used to request it.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingAsync_ForUserA_NeverReturnsUserBsPendingRow()
    {
        // Arrange — two distinct systemusers, each with their own oid mapping and their own outbox row.
        var store = new FakeGenericEntityService();
        var now = DateTimeOffset.Parse("2026-07-21T12:00:00Z");
        var outbox = BuildOutbox(store, now);

        var systemUserA = Guid.NewGuid();
        var oidA = Guid.NewGuid().ToString();
        var systemUserB = Guid.NewGuid();
        var oidB = Guid.NewGuid().ToString();

        var rowA = await outbox.WriteAsync(systemUserA, NotificationKind.CommunicationArrived, BuildCommunicationEnvelope(Guid.NewGuid(), "User A's sender").Validate());
        var rowB = await outbox.WriteAsync(systemUserB, NotificationKind.CommunicationArrived, BuildCommunicationEnvelope(Guid.NewGuid(), "User B's sender").Validate());

        // A resolver that maps BOTH users — proves isolation comes from the QUERY (owner filter), not
        // from the resolver only knowing one mapping.
        var resolver = new Mock<ISystemUserIdentityResolver>();
        resolver.Setup(r => r.ResolveSystemUserIdAsync(oidA, It.IsAny<CancellationToken>())).ReturnsAsync(systemUserA);
        resolver.Setup(r => r.ResolveSystemUserIdAsync(oidB, It.IsAny<CancellationToken>())).ReturnsAsync(systemUserB);

        // Act — user A calls, including a spoofed "kind" is irrelevant here; there is no target-user
        // parameter on the handler signature at all — identity is derived from the JWT context only.
        var resultA = await NotificationsEndpoints.GetPendingAsync(BuildAuthenticatedContext(oidA), outbox, resolver.Object, kind: null, ct: default);

        // Assert — A sees only A's row, never B's, regardless of any query parameter (there is none to supply for target user).
        var responseA = resultA.Should().BeAssignableTo<IValueHttpResult<NotificationsPendingResponse>>().Subject;
        responseA.Value!.Items.Should().ContainSingle(i => i.OutboxRowId == rowA);
        responseA.Value!.Items.Should().NotContain(i => i.OutboxRowId == rowB);
    }

    [Fact]
    public async Task GetPendingAsync_WhenCallerHasNoSystemUserMapping_ReturnsEmptyNotError()
    {
        // A caller with no Dataverse user mapping (resolver returns null) gets 200 + empty list, not
        // an error — they simply have no possible outbox rows (task spec: null resolution is not a
        // failure state).
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store, DateTimeOffset.Parse("2026-07-21T12:00:00Z"));
        var oid = Guid.NewGuid().ToString();

        var resolver = new Mock<ISystemUserIdentityResolver>();
        resolver.Setup(r => r.ResolveSystemUserIdAsync(oid, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        var result = await NotificationsEndpoints.GetPendingAsync(BuildAuthenticatedContext(oid), outbox, resolver.Object, kind: null, ct: default);

        var response = result.Should().BeAssignableTo<IValueHttpResult<NotificationsPendingResponse>>().Subject;
        response.Value!.Items.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (c) Multi-kind: all active kinds surface through the SAME response shape
    //     without a kind filter, and the optional filter narrows correctly.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingAsync_WithMultipleKinds_ReturnsAllKindsInSameShape_AndFilterNarrows()
    {
        var store = new FakeGenericEntityService();
        var now = DateTimeOffset.Parse("2026-07-21T12:00:00Z");
        var outbox = BuildOutbox(store, now);
        var systemUserId = Guid.NewGuid();
        var oid = Guid.NewGuid().ToString();
        var resolver = ResolverMapping(oid, systemUserId);

        var arrivedId = await outbox.WriteAsync(systemUserId, NotificationKind.CommunicationArrived, BuildCommunicationEnvelope(Guid.NewGuid()).Validate());
        var assessedId = await outbox.WriteAsync(systemUserId, NotificationKind.CommunicationAssessed,
            BuildCommunicationEnvelope(Guid.NewGuid()) with { Kind = NotificationKind.CommunicationAssessed });
        var suggestionId = await outbox.WriteAsync(systemUserId, NotificationKind.Suggestion, BuildSuggestionEnvelope(Guid.NewGuid()).Validate());

        // Act (no filter) — all three active kinds come back in the SAME response shape.
        var allResult = await NotificationsEndpoints.GetPendingAsync(BuildAuthenticatedContext(oid), outbox, resolver, kind: null, ct: default);
        var allResponse = allResult.Should().BeAssignableTo<IValueHttpResult<NotificationsPendingResponse>>().Subject;

        allResponse.Value!.Items.Should().HaveCount(3);
        allResponse.Value!.Items.Select(i => i.OutboxRowId).Should().BeEquivalentTo(new[] { arrivedId, assessedId, suggestionId });
        allResponse.Value!.Items.Select(i => i.Kind).Should().BeEquivalentTo(new[]
        {
            NotificationKind.CommunicationArrived, NotificationKind.CommunicationAssessed, NotificationKind.Suggestion
        });

        // Act (filtered) — ?kind=suggestion narrows to just that kind.
        var filteredResult = await NotificationsEndpoints.GetPendingAsync(BuildAuthenticatedContext(oid), outbox, resolver, kind: "suggestion", ct: default);
        var filteredResponse = filteredResult.Should().BeAssignableTo<IValueHttpResult<NotificationsPendingResponse>>().Subject;

        filteredResponse.Value!.Items.Should().ContainSingle(i => i.OutboxRowId == suggestionId);
        filteredResponse.Value!.Items.Should().NotContain(i => i.OutboxRowId == arrivedId || i.OutboxRowId == assessedId);
    }

    [Fact]
    public async Task GetPendingAsync_WithUnrecognizedKindFilter_Returns400()
    {
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store, DateTimeOffset.Parse("2026-07-21T12:00:00Z"));
        var oid = Guid.NewGuid().ToString();
        var resolver = ResolverMapping(oid, Guid.NewGuid());

        var result = await NotificationsEndpoints.GetPendingAsync(BuildAuthenticatedContext(oid), outbox, resolver, kind: "not-a-real-kind", ct: default);

        var problem = result.Should().BeAssignableTo<IStatusCodeHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // (d) The REAL SystemUserIdentityResolver production class, boundary-doubled
    //     only at Dataverse/cache — proves the oid→systemuserid resolution the
    //     handler depends on actually works, not just a pre-wired mock mapping.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPendingAsync_WithRealIdentityResolver_ResolvesOidToSystemUserAndReturnsRow()
    {
        var store = new FakeGenericEntityService();
        var outbox = BuildOutbox(store, DateTimeOffset.Parse("2026-07-21T12:00:00Z"));
        var systemUserId = Guid.NewGuid();
        var oid = Guid.NewGuid().ToString();

        var outboxRowId = await outbox.WriteAsync(systemUserId, NotificationKind.CommunicationArrived, BuildCommunicationEnvelope(Guid.NewGuid()).Validate());

        // IDataverseService is a 9-interface composite (ISP) — Moq stands in for the SINGLE
        // RetrieveMultipleAsync(QueryExpression, ct) call SystemUserIdentityResolver actually issues,
        // interpreting the real oid-filter QueryExpression rather than pattern-matching on `It.IsAny`,
        // so the resolver's real filter-construction logic is what's proven, not a canned return.
        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                var requestedOid = query.Criteria.Conditions
                    .FirstOrDefault(c => c.AttributeName == "azureactivedirectoryobjectid")?.Values.FirstOrDefault();

                if (requestedOid is Guid oidGuid && oidGuid.ToString("D") == oid)
                {
                    var entity = new Entity("systemuser") { Id = systemUserId };
                    entity["systemuserid"] = systemUserId;
                    return new EntityCollection(new List<Entity> { entity });
                }

                return new EntityCollection(new List<Entity>());
            });

        var resolver = new SystemUserIdentityResolver(dataverseMock.Object, new NoOpDistributedCache(), NullLogger<SystemUserIdentityResolver>.Instance);

        var result = await NotificationsEndpoints.GetPendingAsync(BuildAuthenticatedContext(oid), outbox, resolver, kind: null, ct: default);

        var response = result.Should().BeAssignableTo<IValueHttpResult<NotificationsPendingResponse>>().Subject;
        response.Value!.Items.Should().ContainSingle(i => i.OutboxRowId == outboxRowId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────────

    private static ISystemUserIdentityResolver ResolverMapping(string oid, Guid systemUserId)
    {
        var mock = new Mock<ISystemUserIdentityResolver>();
        mock.Setup(r => r.ResolveSystemUserIdAsync(oid, It.IsAny<CancellationToken>())).ReturnsAsync(systemUserId);
        return mock.Object;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Cache double that always misses — forces every resolve through the "live" Dataverse path.</summary>
    private sealed class NoOpDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }

    /// <summary>
    /// In-memory <see cref="IGenericEntityService"/> double — the Dataverse boundary, matching
    /// <c>OutboxServiceSeamTests.FakeGenericEntityService</c>: interprets the REAL
    /// <see cref="QueryExpression"/> criteria tree <see cref="OutboxService"/> builds rather than
    /// returning a canned result.
    /// </summary>
    private sealed class FakeGenericEntityService : IGenericEntityService
    {
        private readonly Dictionary<Guid, Entity> _rows = new();

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default)
        {
            var id = Guid.NewGuid();
            var stored = new Entity(entity.LogicalName) { Id = id };
            foreach (var attribute in entity.Attributes)
            {
                stored[attribute.Key] = attribute.Value;
            }

            _rows[id] = stored;
            return Task.FromResult(id);
        }

        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default)
        {
            if (!_rows.TryGetValue(id, out var existing))
            {
                throw new InvalidOperationException($"No fake row with id {id}.");
            }

            foreach (var (key, value) in fields)
            {
                existing[key] = value;
            }

            return Task.CompletedTask;
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
        {
            var matches = _rows.Values
                .Where(e => e.LogicalName == query.EntityName)
                .Where(e => query.Criteria is null || Matches(e, query.Criteria))
                .ToList();

            return Task.FromResult(new EntityCollection(matches));
        }

        private static bool Matches(Entity entity, FilterExpression filter)
        {
            var isAnd = filter.FilterOperator == LogicalOperator.And;
            var results = new List<bool>();

            foreach (var condition in filter.Conditions)
            {
                results.Add(EvaluateCondition(entity, condition));
            }

            foreach (var nested in filter.Filters)
            {
                results.Add(Matches(entity, nested));
            }

            if (results.Count == 0)
            {
                return true;
            }

            return isAnd ? results.All(r => r) : results.Any(r => r);
        }

        private static bool EvaluateCondition(Entity entity, ConditionExpression condition)
        {
            var hasValue = entity.Contains(condition.AttributeName) && entity[condition.AttributeName] is not null;
            var raw = hasValue ? entity[condition.AttributeName] : null;

            switch (condition.Operator)
            {
                case ConditionOperator.Equal:
                {
                    var expected = condition.Values[0];
                    if (raw is EntityReference er && expected is Guid expectedGuid)
                    {
                        return er.Id == expectedGuid;
                    }

                    return Equals(raw, expected);
                }
                case ConditionOperator.Null:
                    return !hasValue;
                case ConditionOperator.GreaterThan:
                {
                    if (!hasValue)
                    {
                        return false;
                    }

                    var actual = (DateTime)raw!;
                    var threshold = (DateTime)condition.Values[0];
                    return actual > threshold;
                }
                default:
                    throw new NotSupportedException(
                        $"FakeGenericEntityService does not model ConditionOperator.{condition.Operator} — extend the fake if OutboxService starts using it.");
            }
        }

        // ── Unused by OutboxService — not exercised by this seam ──
        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default)
            => throw new NotImplementedException();
    }
}
