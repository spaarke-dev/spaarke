using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression.Analysis;

/// <summary>
/// Regression — the promote "silent-FK gap" (agreements-r1 Q2, 2026-07-31): a promote/bind whose
/// session had NO pre-existing <c>sprk_aichatsummary</c> anchor row must STILL leave the Analysis
/// visible via <c>GET /api/ai/chat/sessions/by-analysis/{analysisId}</c>.
///
/// <para>
/// Root cause (hub-r1, fixed by commit <c>2f8f11123</c> in <see cref="ChatDataverseRepository"/>):
/// hub task 022 made the <c>sprk_aichatsummary</c> create a <i>tolerated</i> Dataverse failure
/// (correct for the ARCHIVE path — Cosmos is the transcript store-of-record). That tolerance leaked
/// into PROMOTE: when a loose session never had a summary row, <c>BindSessionToAnalysisAsync</c>
/// logged a warning and returned <c>false</c> — the durable <c>sprk_analysis</c> FK was never written,
/// yet <c>PromoteSessionToAnalysisAsync</c> ignored the bool and the endpoint returned <b>201 with no
/// durable bind</b>. The Analysis was then INVISIBLE to <see cref="ChatDataverseRepository.GetSessionsByAnalysisAsync"/>
/// (the <c>by-analysis</c> lookup + the hub grid). For a legal work product an "everything is 201 but
/// the bind silently failed" outcome cannot ship. The fix makes <c>BindSessionToAnalysisAsync</c>
/// CREATE the anchor row WITH the FK when none exists (the FK is promotion's entire deliverable), so
/// bind is durable-or-throws — a real write failure propagates to the caller's compensation
/// (Analysis delete), never a silent 201.
/// </para>
///
/// <para>
/// <b>Why THESE tests (not the existing unit coverage):</b>
/// <see cref="ChatDataverseRepository"/>'s own unit suite
/// (<c>BindSessionToAnalysisAsync_WhenNoSummaryRow_CreatesAnchorRowWithFkAndReturnsTrue</c>) asserts,
/// against a stateless Moq, that <c>CreateAsync</c> is invoked carrying the FK. It stops there — it
/// never proves the created row is then RETRIEVABLE by <c>GetSessionsByAnalysisAsync</c>. This
/// regression closes exactly that gap: it wires the REAL repository over a stateful in-memory
/// <see cref="IGenericEntityService"/> that models the <c>sprk_aichatsummary</c> table, then proves
/// the <b>round-trip</b> — bind (create-when-missing OR update-existing) → by-analysis returns the
/// bound session — which is the durable-bind acceptance the FR-17 wizard→review flow relies on, on
/// the exact path the silent-FK gap broke (FR-17 acceptance criterion 2).
/// </para>
///
/// <para>
/// KEEP-path classification (ADR-038 §2 path #2 + tests/CLAUDE.md): regression — "every bug = one
/// regression test". A concrete production behavior breaks if deleted (a promote with no cold-tier
/// anchor row silently drops the durable FK → the Analysis vanishes from <c>by-analysis</c>). The
/// in-memory <see cref="IGenericEntityService"/> is a module-boundary test double (the ADR-038 §7 /
/// B5 preferred alternative to interaction-mocking), not a mock of the class under test.
/// </para>
/// </summary>
public class PromoteDurableFkVisibilityTests
{
    private const string TenantId = "00000000-0000-0000-0000-000000000abc";
    private const string OtherTenantId = "11111111-1111-1111-1111-111111111def";

    private readonly InMemoryAiChatSummaryTable _table = new();
    private readonly ChatDataverseRepository _sut;

    public PromoteDurableFkVisibilityTests()
    {
        _sut = new ChatDataverseRepository(_table, NullLogger<ChatDataverseRepository>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // THE silent-FK-gap regression — bind with NO pre-existing summary row, then
    // prove the Analysis is visible via by-analysis (the round-trip the unit test omits).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BindSessionToAnalysis_WhenNoPreExistingSummaryRow_ByAnalysisReturnsBoundSession()
    {
        // Arrange — a loose session whose cold-tier sprk_aichatsummary create was skipped/failed
        // (CreateSessionAsync tolerates a Dataverse write failure). The table is EMPTY: this is the
        // exact shape that produced a silent 201-without-FK before the fix.
        var sessionId = Guid.NewGuid().ToString("N");
        var analysisId = Guid.NewGuid();
        _table.All.Should().BeEmpty("this session never had an anchor row — the silent-FK-gap precondition");

        // Act — the promote bind leg (ChatSessionManager.PromoteSessionToAnalysisAsync :527 delegates here).
        var bound = await _sut.BindSessionToAnalysisAsync(TenantId, sessionId, analysisId);

        // Assert — bind reports success (the FK was written, not a tolerant no-op).
        bound.Should().BeTrue("the durable FK is promotion's entire deliverable — bind must not silently no-op");

        // Assert — THE regression: the Analysis is now visible on the by-analysis path. Before the fix
        // this returned EMPTY (no anchor row was ever created), leaving the Analysis orphaned.
        var byAnalysis = await _sut.GetSessionsByAnalysisAsync(TenantId, analysisId);
        byAnalysis.Should().ContainSingle("the bound session must be visible via by-analysis after promote")
            .Which.SessionId.Should().Be(sessionId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The pre-existing-row path — a loose session that DID have an anchor row (the
    // update branch) must be equally visible after bind. Guards the fix from a
    // regression that only handled the create branch.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BindSessionToAnalysis_WhenExistingLooseSummaryRow_ByAnalysisReturnsBoundSession()
    {
        // Arrange — a loose session WITH an anchor row (no FK yet), as CreateSessionAsync writes for a
        // session minted without an Analysis-owned HostContext.
        var sessionId = Guid.NewGuid().ToString("N");
        var analysisId = Guid.NewGuid();
        _table.SeedLooseRow(TenantId, sessionId, messageCount: 3);

        // Act
        var bound = await _sut.BindSessionToAnalysisAsync(TenantId, sessionId, analysisId);

        // Assert — bound in place (update branch), no second row created, visible via by-analysis.
        bound.Should().BeTrue();
        _table.All.Should().HaveCount(1, "binding an existing loose row must UPDATE it, not create a second row");

        var byAnalysis = await _sut.GetSessionsByAnalysisAsync(TenantId, analysisId);
        byAnalysis.Should().ContainSingle().Which.SessionId.Should().Be(sessionId);
        byAnalysis[0].MessageCount.Should().Be(3, "the pre-existing row's data is preserved through the bind");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tenant isolation on the durable-bind round-trip (ADR-014 / ADR-028): the FK bind
    // writes sprk_tenantid and by-analysis filters on it — a cross-tenant analysisId
    // guess must NOT leak the bound session.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSessionsByAnalysis_ForADifferentTenant_ReturnsEmptyEvenWhenFkMatches()
    {
        // Arrange — bind under the owning tenant (create-when-missing path).
        var sessionId = Guid.NewGuid().ToString("N");
        var analysisId = Guid.NewGuid();
        await _sut.BindSessionToAnalysisAsync(TenantId, sessionId, analysisId);

        // Act — the SAME analysisId, queried under a DIFFERENT tenant.
        var crossTenant = await _sut.GetSessionsByAnalysisAsync(OtherTenantId, analysisId);
        var ownTenant = await _sut.GetSessionsByAnalysisAsync(TenantId, analysisId);

        // Assert — the FK matches, but the tenant scope does not: no leak across the boundary.
        crossTenant.Should().BeEmpty("by-analysis is tenant-scoped — a cross-tenant analysisId must not leak sessions");
        ownTenant.Should().ContainSingle().Which.SessionId.Should().Be(sessionId);
    }

    // =========================================================================
    // In-memory sprk_aichatsummary table — a module-boundary IGenericEntityService double.
    // Models only the three operations ChatDataverseRepository uses for the bind + by-analysis
    // round-trip (Create / RetrieveMultiple-with-Equal-conditions / Update). Every other member
    // throws — a test that reaches one is exercising an unmodelled path and should fail loudly.
    // =========================================================================
    private sealed class InMemoryAiChatSummaryTable : IGenericEntityService
    {
        private const string SummaryEntityName = "sprk_aichatsummary";
        private readonly List<Entity> _rows = new();

        public IReadOnlyList<Entity> All => _rows;

        /// <summary>Seed a loose (no sprk_analysis FK) anchor row, as CreateSessionAsync writes.</summary>
        public void SeedLooseRow(string tenantId, string sessionId, int messageCount)
        {
            _rows.Add(new Entity(SummaryEntityName, Guid.NewGuid())
            {
                ["sprk_sessionid"] = sessionId,
                ["sprk_tenantid"] = tenantId,
                ["sprk_messagecount"] = messageCount,
                ["sprk_isarchived"] = false,
            });
        }

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default)
        {
            var id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
            entity.Id = id;
            _rows.Add(entity);
            return Task.FromResult(id);
        }

        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default)
        {
            var row = _rows.FirstOrDefault(r => r.Id == id)
                ?? throw new InvalidOperationException($"No {entityLogicalName} row {id} to update.");
            foreach (var kvp in fields)
            {
                row[kvp.Key] = kvp.Value;
            }
            return Task.CompletedTask;
        }

        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
        {
            var matches = _rows
                .Where(r => query.Criteria.Conditions.All(c => Matches(r, c)))
                .ToList();
            return Task.FromResult(new EntityCollection(matches));
        }

        /// <summary>Equal-only condition match; models the FK (EntityReference) vs raw-Guid comparison.</summary>
        private static bool Matches(Entity row, ConditionExpression condition)
        {
            if (condition.Operator != ConditionOperator.Equal)
            {
                throw new NotSupportedException(
                    $"The in-memory sprk_aichatsummary double models only ConditionOperator.Equal (saw {condition.Operator}).");
            }
            if (!row.Contains(condition.AttributeName))
            {
                return false;
            }
            var actual = row[condition.AttributeName];
            var expected = condition.Values.FirstOrDefault();
            // sprk_analysis is stored as an EntityReference; the by-analysis query conditions on the raw Guid.
            if (actual is EntityReference reference)
            {
                return expected is Guid guid && reference.Id == guid;
            }
            return Equals(actual, expected);
        }

        // ── Unmodelled surface — fail loudly if the repository ever reaches these on this path. ──
        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(RetrieveAsync));
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(BulkUpdateAsync));
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(RetrieveByAlternateKeyAsync));
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(GetEntitySetNameAsync));
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(GetLookupNavigationAsync));
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(GetCollectionNavigationAsync));
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(RetrieveMultipleAsync) + "(FetchExpression)");
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(DeleteAsync));
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default)
            => throw new NotSupportedException(nameof(AssociateAsync));
    }
}
