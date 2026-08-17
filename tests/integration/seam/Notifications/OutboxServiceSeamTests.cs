using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Notifications;
using Sprk.Bff.Api.Services.Notifications.Envelopes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Notifications;

/// <summary>
/// Vertical-slice seam (ADR-038, task 012) for the notification outbox durable-store service:
/// <see cref="OutboxService.WriteAsync{TEnvelope}"/> → <see cref="OutboxService.GetPendingAsync"/>
/// → <see cref="OutboxService.DismissAsync"/>, and the expiry read-time filter. Wires the REAL
/// production <see cref="OutboxService"/> end-to-end (real query construction, real envelope
/// serialization, real kind wire-mapping via the task-013 <see cref="NotificationKindJsonConverter"/>)
/// and doubles only the Dataverse boundary — matching the established pattern in this seam category
/// for Dataverse-backed services (e.g. <c>Communication/AssociationSpineSeamTests.cs</c>), where the
/// external Dataverse system itself is the doubled boundary, not the production business logic.
/// </summary>
/// <remarks>
/// The double here (<see cref="FakeGenericEntityService"/>) is an in-memory store that evaluates the
/// REAL <see cref="QueryExpression"/> the service builds (owner filter, dismissed-is-null filter,
/// expiry OR-filter) — it does not stub the answer, it interprets the actual filter tree
/// <see cref="OutboxService"/> constructs. This proves the query shape is correct, not just that
/// some canned result flows through, which is the standard this seam category holds to (README.md:
/// "a green contract-shape test is NOT sufficient").
/// </remarks>
public sealed class OutboxServiceSeamTests
{
    private static OutboxService BuildService(FakeGenericEntityService store, DateTimeOffset now)
        => new(store, NullLogger<OutboxService>.Instance, new FixedTimeProvider(now));

    private static CommunicationEnvelope BuildCommunicationEnvelope(Guid regardingId) => new()
    {
        Kind = NotificationKind.CommunicationArrived,
        CommunicationId = Guid.NewGuid(),
        ThreadId = Guid.NewGuid(),
        Channel = "email",
        Direction = "inbound",
        RegardingRecordId = regardingId.ToString(),
        SenderDisplay = "Jane Doe",
        BadgeDelta = 1
    };

    [Fact]
    public async Task WriteThenDismiss_RemovesRowFromPending()
    {
        // Arrange — real service, real query construction; only the Dataverse boundary is faked.
        var store = new FakeGenericEntityService();
        var now = DateTimeOffset.Parse("2026-07-21T12:00:00Z");
        var sut = BuildService(store, now);
        var userId = Guid.NewGuid();
        var regardingId = Guid.NewGuid();
        var envelope = BuildCommunicationEnvelope(regardingId).Validate();

        // Act — write
        var outboxId = await sut.WriteAsync(
            userId,
            NotificationKind.CommunicationArrived,
            envelope,
            regardingRecordId: regardingId.ToString(),
            regardingRecordType: "sprk_communication");

        // Assert — pending includes the written row
        var pendingAfterWrite = await sut.GetPendingAsync(userId);
        pendingAfterWrite.Should().ContainSingle(n => n.Id == outboxId);
        var written = pendingAfterWrite.Single(n => n.Id == outboxId);
        written.Kind.Should().Be(NotificationKind.CommunicationArrived);
        written.OwnerId.Should().Be(userId);
        written.Dismissed.Should().BeNull();
        written.EnvelopeJson.Should().Contain(envelope.CommunicationId.ToString());

        // Act — dismiss
        await sut.DismissAsync(outboxId);

        // Assert — no longer pending
        var pendingAfterDismiss = await sut.GetPendingAsync(userId);
        pendingAfterDismiss.Should().NotContain(n => n.Id == outboxId);
    }

    [Fact]
    public async Task Write_WithPastDueExpiry_ExcludedFromPending()
    {
        // Arrange
        var store = new FakeGenericEntityService();
        var now = DateTimeOffset.Parse("2026-07-21T12:00:00Z");
        var sut = BuildService(store, now);
        var userId = Guid.NewGuid();
        var regardingId = Guid.NewGuid();
        var envelope = BuildCommunicationEnvelope(regardingId).Validate();
        var pastExpiry = now.AddHours(-1);

        // Act — write with an expiry already in the past relative to the service's TimeProvider
        var outboxId = await sut.WriteAsync(
            userId,
            NotificationKind.CommunicationArrived,
            envelope,
            regardingRecordId: regardingId.ToString(),
            regardingRecordType: "sprk_communication",
            expiresAt: pastExpiry);

        // Assert — the read-time expiry filter excludes it
        var pending = await sut.GetPendingAsync(userId);
        pending.Should().NotContain(n => n.Id == outboxId);

        // Sanity: the row genuinely exists in the store (proves exclusion is the expiry filter,
        // not a write failure) by confirming a future-expiry row for the SAME user IS returned.
        var futureExpiryId = await sut.WriteAsync(
            userId,
            NotificationKind.CommunicationArrived,
            BuildCommunicationEnvelope(regardingId).Validate(),
            expiresAt: now.AddHours(1));
        var pendingAfterSecondWrite = await sut.GetPendingAsync(userId);
        pendingAfterSecondWrite.Should().ContainSingle(n => n.Id == futureExpiryId);
        pendingAfterSecondWrite.Should().NotContain(n => n.Id == outboxId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test doubles
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Minimal <see cref="System.TimeProvider"/> subclass returning a fixed UTC instant. Same
    /// inline-subclass shape used elsewhere in this codebase (e.g.
    /// <c>PortfolioServiceTests.FixedTimeProvider</c>) rather than the
    /// <c>Microsoft.Extensions.TimeProvider.Testing</c> NuGet, per ADR-029 publish-hygiene +
    /// bff-extensions §B (no new package references without justification).
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _fixedNow;
        public FixedTimeProvider(DateTimeOffset fixedNow) => _fixedNow = fixedNow;
        public override DateTimeOffset GetUtcNow() => _fixedNow;
    }

    /// <summary>
    /// In-memory <see cref="IGenericEntityService"/> double — the Dataverse boundary doubled by
    /// this seam test. Stores <see cref="Entity"/> rows by id and INTERPRETS the real
    /// <see cref="QueryExpression"/> criteria tree <see cref="OutboxService"/> builds (AND/OR
    /// filters, Equal/Null/GreaterThan conditions) rather than returning a canned result — so the
    /// test proves the service's actual query shape, not just that some result flows through.
    /// Only the members <see cref="OutboxService"/> calls are implemented; everything else throws
    /// <see cref="NotImplementedException"/> (unused by this seam).
    /// </summary>
    private sealed class FakeGenericEntityService : IGenericEntityService
    {
        private readonly Dictionary<Guid, Entity> _rows = new();

        public async Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default)
            => (await CreateAsync(entity, ct), true); // compose-r7 task 013: interface member; fake delegates to create

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
