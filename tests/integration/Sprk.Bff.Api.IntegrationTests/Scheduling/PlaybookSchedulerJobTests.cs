using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Scheduling;

/// <summary>
/// Integration tests for R3 task 025 covering the migrated <see cref="PlaybookSchedulerJob"/>
/// (task 023 deliverable). Drives <see cref="PlaybookSchedulerJob.ExecuteAsync"/> directly against
/// in-memory fakes for <see cref="IGenericEntityService"/> and
/// <see cref="IPlaybookOrchestrationService"/> wired through a real
/// <see cref="IServiceProvider"/>, so the scope-factory + per-tick query + child-correlationId
/// invariants run through production code paths without contacting Dataverse.
/// </summary>
/// <remarks>
/// <para><b>Coverage</b>:</para>
/// <list type="bullet">
///   <item><b>AC-2.4 fan-out</b>: <see cref="Tick_DiscoversAllSeededPlaybooks"/> — N seeded
///     notification playbooks all processed in a single tick (single-row fan-out per D2).</item>
///   <item><b>AC-2.4 cadence preservation</b>: <see cref="Tick_PreservesCadence_HonorsHourlyDailyWeekly"/> —
///     verifies <see cref="PlaybookSchedulerJob.IsPlaybookDue"/> respects per-playbook
///     <c>sprk_configjson.schedule.frequency</c> keyed off <c>sprk_lastrundate</c>.</item>
///   <item><b>Q1 fresh correlationId per child</b>:
///     <see cref="Tick_EmitsFreshCorrelationIdPerChild"/> — every child playbook receives a
///     correlationId distinct from the parent AND distinct from sibling children.</item>
///   <item><b>AC-2.4 lastrundate advancement</b>:
///     <see cref="Tick_PersistsLastRunDate_AfterSuccessfulFanOut"/> — after a due playbook
///     dispatches successfully, <c>sprk_lastrundate</c> advances to the tick's UTC now (so
///     the next tick re-checks against the new baseline).</item>
/// </list>
///
/// <para><b>Architectural notes</b>:</para>
/// <list type="bullet">
///   <item><b>No WebApplicationFactory needed</b>: <see cref="PlaybookSchedulerJob"/> consumes
///     <see cref="IServiceScopeFactory"/>, <see cref="IConfiguration"/>, and a logger — all
///     framework primitives that don't require the full BFF bootstrap to exercise. The test
///     <see cref="IServiceProvider"/> wires the real <c>PlaybookSchedulerJob</c> against the
///     in-memory <see cref="FakeGenericEntityService"/> and a stub
///     <see cref="FakePlaybookOrchestrationService"/>.</item>
///   <item><b>In-memory fakes, not Moq</b>: the per-tick query path needs a stable
///     <c>RetrieveMultipleAsync</c> implementation that filters by entity type — easier to
///     express as a typed in-memory store than a chain of Moq returns. Per-test seed → query →
///     mutation lifecycle stays explicit.</item>
///   <item><b>Q6 not applicable here</b>: this suite covers the scheduler implementation, not
///     the admin endpoints. AC-2.5/AC-2.7 are covered in the admin-endpoints integration suite
///     (sibling file <c>Admin/JobsEndpointsTests.cs</c>).</item>
/// </list>
/// </remarks>
public sealed class PlaybookSchedulerJobTests
{
    private const int NotificationPlaybookType = 2;

    // ================================================================================
    // ===== AC-2.4: discovery — all seeded playbooks processed per tick ==============
    // ================================================================================

    [Fact]
    public async Task Tick_DiscoversAllSeededPlaybooks()
    {
        // Arrange — seed 7 notification playbooks (matches the legacy production count) + 2
        // active users. All playbooks marked "due" by setting lastrundate=null.
        var entityService = new FakeGenericEntityService();
        SeedActiveUsers(entityService, count: 2);
        for (var i = 0; i < 7; i++)
        {
            SeedNotificationPlaybook(entityService,
                name: $"playbook-{i:D2}",
                frequency: "daily",
                lastRun: null);
        }

        var orchestration = new FakePlaybookOrchestrationService();
        var job = CreateJob(entityService, orchestration);

        // Act
        var context = new JobRunContext(
            RunId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid().ToString("N"),
            Trigger: JobRunTrigger.Scheduled,
            Parameters: new Dictionary<string, object>());
        var result = await job.ExecuteAsync(context, CancellationToken.None);

        // Assert — single-row fan-out (D2): one ExecuteAsync call → all 7 children processed →
        // ProcessedItems=7 (one entry per playbook in ResultJson).
        result.Success.Should().BeTrue("AC-2.4 — entire tick succeeded");
        result.ProcessedItems.Should().Be(7, "all 7 seeded playbooks must be processed (D2 single-row fan-out)");
        result.ResultJson.Should().NotBeNull();

        var children = DeserializeChildren(result.ResultJson!);
        children.Should().HaveCount(7, "the children array MUST mirror the seeded playbook count");
        children.All(c => c.Status == "Succeeded").Should()
            .BeTrue("every child playbook completed without RunFailed events");
        children.Select(c => c.PlaybookName).Distinct().Should().HaveCount(7,
            "each child's PlaybookName MUST match a unique seeded playbook");

        // Each child dispatched to all 2 active users → 7 * 2 = 14 orchestration calls.
        orchestration.InvocationCount.Should().Be(7 * 2,
            "each playbook fans out to every active user (2 users × 7 playbooks = 14 orchestration calls)");
    }

    // ================================================================================
    // ===== AC-2.4: cadence preservation =============================================
    // ================================================================================

    [Fact]
    public async Task Tick_PreservesCadence_HonorsHourlyDailyWeekly()
    {
        // Arrange — three playbooks with different cadences, lastrundate just under each
        // cadence's threshold (so each one is NOT due) plus one CLEARLY due (lastrundate
        // weeks ago) — verify the not-due ones are Skipped + the due one is Succeeded.
        var entityService = new FakeGenericEntityService();
        SeedActiveUsers(entityService, count: 1);

        var now = DateTimeOffset.UtcNow;

        // 30 min ago + hourly → NOT due (need ≥1h elapsed).
        SeedNotificationPlaybook(entityService, "pb-hourly-not-due", "hourly",
            lastRun: now.AddMinutes(-30));
        // 12h ago + daily → NOT due (need ≥24h elapsed).
        SeedNotificationPlaybook(entityService, "pb-daily-not-due", "daily",
            lastRun: now.AddHours(-12));
        // 3d ago + weekly → NOT due (need ≥7d elapsed).
        SeedNotificationPlaybook(entityService, "pb-weekly-not-due", "weekly",
            lastRun: now.AddDays(-3));
        // 30d ago + weekly → DUE (≥7d elapsed).
        SeedNotificationPlaybook(entityService, "pb-weekly-overdue", "weekly",
            lastRun: now.AddDays(-30));

        var orchestration = new FakePlaybookOrchestrationService();
        var job = CreateJob(entityService, orchestration);
        var context = new JobRunContext(
            RunId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid().ToString("N"),
            Trigger: JobRunTrigger.Scheduled,
            Parameters: new Dictionary<string, object>());

        // Act
        var result = await job.ExecuteAsync(context, CancellationToken.None);

        // Assert — fan-out happens for all 4 (every playbook gets a children entry, even
        // when Skipped), but only the overdue weekly playbook actually dispatched.
        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(4);

        var children = DeserializeChildren(result.ResultJson!);
        children.Should().HaveCount(4);

        children.Single(c => c.PlaybookName == "pb-hourly-not-due").Status.Should().Be("Skipped");
        children.Single(c => c.PlaybookName == "pb-daily-not-due").Status.Should().Be("Skipped");
        children.Single(c => c.PlaybookName == "pb-weekly-not-due").Status.Should().Be("Skipped");

        var overdue = children.Single(c => c.PlaybookName == "pb-weekly-overdue");
        overdue.Status.Should().Be("Succeeded", "AC-2.4 — the overdue weekly playbook MUST dispatch this tick");

        // Only the overdue playbook touched orchestration (1 user × 1 playbook).
        orchestration.InvocationCount.Should().Be(1,
            "AC-2.4 — only the due playbook should hit IPlaybookOrchestrationService");
    }

    /// <summary>
    /// Direct unit-style smoke on <see cref="PlaybookSchedulerJob.IsPlaybookDue"/> covering
    /// each cadence boundary explicitly. Lives in the integration suite alongside the
    /// fan-out tests so the AC-2.4 cadence contract is exercised both ways (via the static
    /// helper AND via the full tick path above).
    /// </summary>
    [Theory]
    [InlineData("hourly", 59, false)]  // 59min ago — not due (need ≥1h)
    [InlineData("hourly", 61, true)]   // 61min ago — due
    [InlineData("daily", 23, false)]   // 23h ago — not due (need ≥24h)
    [InlineData("daily", 25, true)]    // 25h ago — due
    [InlineData("weekly", 6, false)]   // 6d ago (in days param, repurposed below) — see assertion
    [InlineData("weekly", 8, true)]    // 8d ago — due
    public void IsPlaybookDue_HonorsFrequencyBoundary(string frequency, int amount, bool expectedDue)
    {
        // For weekly cases interpret 'amount' as days; otherwise as minutes/hours per cadence.
        DateTimeOffset lastRun = frequency.ToLowerInvariant() switch
        {
            "hourly" => DateTimeOffset.UtcNow.AddMinutes(-amount),
            "daily" => DateTimeOffset.UtcNow.AddHours(-amount),
            "weekly" => DateTimeOffset.UtcNow.AddDays(-amount),
            _ => throw new ArgumentOutOfRangeException(nameof(frequency))
        };

        var schedule = new PlaybookSchedulerJob.ScheduleConfig(frequency, "06:00");

        var isDue = PlaybookSchedulerJob.IsPlaybookDue(lastRun, schedule);

        isDue.Should().Be(expectedDue,
            $"AC-2.4 — IsPlaybookDue must respect the {frequency} boundary at amount={amount}");
    }

    // ================================================================================
    // ===== Q1: fresh correlationId per child playbook ===============================
    // ================================================================================

    [Fact]
    public async Task Tick_EmitsFreshCorrelationIdPerChild()
    {
        // Arrange — 4 due playbooks; one tick → 4 children. Verify each child has a unique
        // correlationId AND none of them equals the parent's correlationId.
        var entityService = new FakeGenericEntityService();
        SeedActiveUsers(entityService, count: 1);
        for (var i = 0; i < 4; i++)
        {
            SeedNotificationPlaybook(entityService, $"q1-pb-{i}", "daily", lastRun: null);
        }

        var orchestration = new FakePlaybookOrchestrationService();
        var job = CreateJob(entityService, orchestration);

        var parentCorrelationId = Guid.NewGuid().ToString("N");
        var context = new JobRunContext(
            RunId: Guid.NewGuid(),
            CorrelationId: parentCorrelationId,
            Trigger: JobRunTrigger.Scheduled,
            Parameters: new Dictionary<string, object>());

        // Act
        var result = await job.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var children = DeserializeChildren(result.ResultJson!);
        children.Should().HaveCount(4);

        var childCorrelationIds = children.Select(c => c.CorrelationId).ToList();
        childCorrelationIds.All(id => !string.IsNullOrEmpty(id))
            .Should().BeTrue("Q1 — every child MUST carry a non-empty correlationId");
        childCorrelationIds.Distinct().Should().HaveCount(4,
            "Q1 — every child playbook MUST receive a DISTINCT correlationId (no sharing across children)");
        childCorrelationIds.Should().NotContain(parentCorrelationId,
            "Q1 — child correlationIds MUST be DISTINCT from the parent's correlationId (no reuse)");
    }

    // ================================================================================
    // ===== AC-2.4: sprk_lastrundate advancement after successful fan-out ===========
    // ================================================================================

    [Fact]
    public async Task Tick_PersistsLastRunDate_AfterSuccessfulFanOut()
    {
        // Arrange — single due playbook, single active user. Capture the playbook id so we
        // can read the post-tick sprk_lastrundate value back from the fake store.
        var entityService = new FakeGenericEntityService();
        SeedActiveUsers(entityService, count: 1);
        var playbookId = SeedNotificationPlaybook(entityService, "advance-lastrundate", "daily", lastRun: null);

        var orchestration = new FakePlaybookOrchestrationService();
        var job = CreateJob(entityService, orchestration);

        var tickStartedAt = DateTimeOffset.UtcNow;

        // Act
        var context = new JobRunContext(
            RunId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid().ToString("N"),
            Trigger: JobRunTrigger.Scheduled,
            Parameters: new Dictionary<string, object>());
        var result = await job.ExecuteAsync(context, CancellationToken.None);

        // Assert — child succeeded.
        result.Success.Should().BeTrue();
        var children = DeserializeChildren(result.ResultJson!);
        children.Single().Status.Should().Be("Succeeded");

        // The scheduler MUST persist sprk_lastrundate to UpdateAsync on the playbook entity.
        entityService.UpdateCalls.Should().ContainSingle(
            "AC-2.4 — UpdateAsync MUST be invoked exactly once per due playbook (to persist lastrundate)");
        var updateCall = entityService.UpdateCalls.Single();
        updateCall.EntityLogicalName.Should().Be("sprk_analysisplaybook");
        updateCall.Id.Should().Be(playbookId);
        updateCall.Fields.Should().ContainKey("sprk_lastrundate");

        var persisted = (DateTime)updateCall.Fields["sprk_lastrundate"];
        // The persisted timestamp must be ≥ the tick start (lastrundate advances forward).
        new DateTimeOffset(persisted, TimeSpan.Zero)
            .Should().BeOnOrAfter(tickStartedAt,
                "AC-2.4 — sprk_lastrundate MUST advance to (at least) the tick's UTC now after a successful child");
    }

    // ================================================================================
    // ===== Helpers ==================================================================
    // ================================================================================

    /// <summary>
    /// Build a <see cref="PlaybookSchedulerJob"/> instance wired through a real
    /// <see cref="IServiceProvider"/> so <see cref="IServiceScopeFactory.CreateScope"/> and
    /// the scoped resolution paths in <c>ExecuteAsync</c> run through production code.
    /// </summary>
    private static PlaybookSchedulerJob CreateJob(
        FakeGenericEntityService entityService,
        FakePlaybookOrchestrationService orchestration)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGenericEntityService>(entityService);
        services.AddSingleton<IPlaybookOrchestrationService>(orchestration);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TENANT_ID"] = "integration-test-tenant",
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);

        var sp = services.BuildServiceProvider();

        return new PlaybookSchedulerJob(
            scopeFactory: sp.GetRequiredService<IServiceScopeFactory>(),
            configuration: config,
            logger: NullLogger<PlaybookSchedulerJob>.Instance);
    }

    private static Guid SeedNotificationPlaybook(
        FakeGenericEntityService entityService,
        string name,
        string frequency,
        DateTimeOffset? lastRun)
    {
        var id = Guid.NewGuid();
        var entity = new Entity("sprk_analysisplaybook", id);
        entity["sprk_analysisplaybookid"] = id;
        entity["sprk_name"] = name;
        entity["sprk_playbooktype"] = new OptionSetValue(NotificationPlaybookType);
        entity["statecode"] = new OptionSetValue(0);
        entity["sprk_configjson"] = JsonSerializer.Serialize(new
        {
            schedule = new { frequency, time = "06:00" }
        });
        if (lastRun is not null)
        {
            entity["sprk_lastrundate"] = lastRun.Value.UtcDateTime;
        }
        entityService.AddEntity(entity);
        return id;
    }

    private static void SeedActiveUsers(FakeGenericEntityService entityService, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            var user = new Entity("systemuser", id);
            user["systemuserid"] = id;
            user["fullname"] = $"User {i}";
            user["isdisabled"] = false;
            user["accessmode"] = new OptionSetValue(0);
            entityService.AddEntity(user);
        }
    }

    private static List<ChildPlaybookRunDto> DeserializeChildren(string resultJson)
    {
        // PlaybookSchedulerJob.SerializeChildren uses default STJ options (no naming-policy override),
        // so the property names follow the record's C# casing — PascalCase: "Children", "PlaybookId",
        // "PlaybookName", "CorrelationId", "Status", "UserCount", "SuccessCount", "FailureCount",
        // "ErrorMessage".
        using var doc = JsonDocument.Parse(resultJson);
        var children = new List<ChildPlaybookRunDto>();
        foreach (var child in doc.RootElement.GetProperty("Children").EnumerateArray())
        {
            children.Add(new ChildPlaybookRunDto(
                PlaybookId: child.GetProperty("PlaybookId").GetGuid(),
                PlaybookName: child.GetProperty("PlaybookName").GetString() ?? string.Empty,
                CorrelationId: child.GetProperty("CorrelationId").GetString() ?? string.Empty,
                Status: child.GetProperty("Status").GetString() ?? string.Empty,
                UserCount: child.GetProperty("UserCount").GetInt32(),
                SuccessCount: child.GetProperty("SuccessCount").GetInt32(),
                FailureCount: child.GetProperty("FailureCount").GetInt32(),
                ErrorMessage: child.TryGetProperty("ErrorMessage", out var em) && em.ValueKind != JsonValueKind.Null
                    ? em.GetString()
                    : null));
        }
        return children;
    }

    private sealed record ChildPlaybookRunDto(
        Guid PlaybookId,
        string PlaybookName,
        string CorrelationId,
        string Status,
        int UserCount,
        int SuccessCount,
        int FailureCount,
        string? ErrorMessage);
}

/// <summary>
/// In-memory test double for <see cref="IGenericEntityService"/>. Stores entities by logical
/// name, services <c>RetrieveMultipleAsync(QueryExpression)</c> with simple condition matching
/// against the two query shapes used by <see cref="PlaybookSchedulerJob"/>, and records every
/// <c>UpdateAsync</c> call so the test can assert sprk_lastrundate advancement.
/// </summary>
internal sealed class FakeGenericEntityService : IGenericEntityService
{
    private readonly Dictionary<string, List<Entity>> _entitiesByType = new(StringComparer.OrdinalIgnoreCase);

    public List<UpdateCall> UpdateCalls { get; } = new();

    public void AddEntity(Entity entity)
    {
        if (!_entitiesByType.TryGetValue(entity.LogicalName, out var list))
        {
            list = new List<Entity>();
            _entitiesByType[entity.LogicalName] = list;
        }
        list.Add(entity);
    }

    public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default)
    {
        var entities = _entitiesByType.TryGetValue(query.EntityName, out var list)
            ? list.Where(e => MatchesQuery(e, query)).ToList()
            : new List<Entity>();
        var ec = new EntityCollection(entities) { EntityName = query.EntityName };
        return Task.FromResult(ec);
    }

    public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default)
    {
        UpdateCalls.Add(new UpdateCall(entityLogicalName, id, new Dictionary<string, object>(fields)));

        // Apply the mutation to the in-memory entity so subsequent reads see the new state.
        if (_entitiesByType.TryGetValue(entityLogicalName, out var list))
        {
            var entity = list.FirstOrDefault(e => e.Id == id);
            if (entity is not null)
            {
                foreach (var kvp in fields)
                {
                    entity[kvp.Key] = kvp.Value;
                }
            }
        }
        return Task.CompletedTask;
    }

    private static bool MatchesQuery(Entity entity, QueryExpression query)
    {
        foreach (var condition in query.Criteria.Conditions)
        {
            var actual = entity.Contains(condition.AttributeName) ? entity[condition.AttributeName] : null;
            var expected = condition.Values.FirstOrDefault();

            // Normalize OptionSetValue → int for comparison.
            if (actual is OptionSetValue osv) actual = osv.Value;

            switch (condition.Operator)
            {
                case ConditionOperator.Equal:
                    if (!Equals(Convert.ToInt64(actual ?? -1), Convert.ToInt64(expected ?? -1)))
                        return false;
                    break;
                case ConditionOperator.NotEqual:
                    if (Equals(Convert.ToInt64(actual ?? -1), Convert.ToInt64(expected ?? -1)))
                        return false;
                    break;
                default:
                    // Defensive: tests don't exercise other operators against this fake.
                    throw new NotSupportedException(
                        $"FakeGenericEntityService does not implement operator '{condition.Operator}'.");
            }
        }
        return true;
    }

    // Members not used by PlaybookSchedulerJob — implement as throw to surface accidental coupling.
    public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default) =>
        throw new NotImplementedException();
    public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default) =>
        throw new NotImplementedException();

    /// <summary>Recorded call to <see cref="UpdateAsync"/> — captured for test assertions.</summary>
    internal sealed record UpdateCall(string EntityLogicalName, Guid Id, Dictionary<string, object> Fields);
}

/// <summary>
/// Stub <see cref="IPlaybookOrchestrationService"/> that records every invocation to
/// <see cref="ExecuteAppOnlyAsync"/> and yields a synthetic success event stream.
/// </summary>
internal sealed class FakePlaybookOrchestrationService : IPlaybookOrchestrationService
{
    private int _invocations;

    public int InvocationCount => Volatile.Read(ref _invocations);

    public async IAsyncEnumerable<PlaybookStreamEvent> ExecuteAppOnlyAsync(
        PlaybookRunRequest request,
        string tenantId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocations);

        // Yield a RunStarted + RunCompleted so PlaybookSchedulerJob's enumeration completes
        // without observing a RunFailed event. async to satisfy the IAsyncEnumerable signature.
        await Task.Yield();
        yield return PlaybookStreamEvent.RunStarted(Guid.NewGuid(), request.PlaybookId, nodeCount: 0);
        yield return PlaybookStreamEvent.RunCompleted(
            Guid.NewGuid(),
            request.PlaybookId,
            new PlaybookRunMetrics());
    }

    // ── Unused members — throw to surface accidental coupling. ──────────────────────────
    public IAsyncEnumerable<PlaybookStreamEvent> ExecuteAsync(
        PlaybookRunRequest request,
        Microsoft.AspNetCore.Http.HttpContext httpContext,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
    public Task<PlaybookValidationResult> ValidateAsync(Guid playbookId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
    public Task<PlaybookRunStatus?> GetRunStatusAsync(Guid runId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
    public Task<bool> CancelAsync(Guid runId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
    public Task<PlaybookRunHistoryResponse> GetRunHistoryAsync(
        Guid playbookId, int page = 1, int pageSize = 20, string? stateFilter = null, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
    public Task<PlaybookRunDetail?> GetRunDetailAsync(Guid runId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
