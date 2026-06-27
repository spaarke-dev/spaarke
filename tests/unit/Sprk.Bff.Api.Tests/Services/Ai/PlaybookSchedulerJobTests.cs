using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Tests for <see cref="PlaybookSchedulerJob"/> — the Spaarke.Scheduling reference consumer
/// that replaced the legacy <c>PlaybookSchedulerService : BackgroundService</c> per R3
/// task 023 (FR-2.8 / D2 / Q1).
/// </summary>
/// <remarks>
/// <para><b>Coverage targets</b>:</para>
/// <list type="bullet">
///   <item>Identity contract: <see cref="IScheduledJob.JobId"/> = "notification-playbook-scheduler"
///     (the seeded sprk_backgroundjob row).</item>
///   <item>Fan-out: N active playbooks produce N children entries in <see cref="JobRunResult.ResultJson"/>.</item>
///   <item>Q1 correlationId contract: each child receives a unique Guid.NewGuid().ToString("N").</item>
///   <item>NFR-07 cancellation: cancellation during fan-out halts dispatch + returns partial result.</item>
///   <item>NFR-04 cadence preservation: <see cref="PlaybookSchedulerJob.IsPlaybookDue"/> respects
///     hourly / daily / weekly schedule semantics from <c>sprk_configjson</c>.</item>
///   <item>Per-playbook isolation: one playbook's exception does NOT abort the remaining fan-out.</item>
/// </list>
/// </remarks>
public class PlaybookSchedulerJobTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactoryMock;
    private readonly Mock<IServiceScope> _scopeMock;
    private readonly Mock<IServiceProvider> _serviceProviderMock;
    private readonly Mock<Spaarke.Dataverse.IGenericEntityService> _entityServiceMock;
    private readonly Mock<IPlaybookOrchestrationService> _orchestrationServiceMock;
    private readonly Mock<ILogger<PlaybookSchedulerJob>> _loggerMock;
    private readonly IConfiguration _configuration;
    private readonly PlaybookSchedulerJob _sut;

    public PlaybookSchedulerJobTests()
    {
        _scopeFactoryMock = new Mock<IServiceScopeFactory>();
        _scopeMock = new Mock<IServiceScope>();
        _serviceProviderMock = new Mock<IServiceProvider>();
        _entityServiceMock = new Mock<Spaarke.Dataverse.IGenericEntityService>();
        _orchestrationServiceMock = new Mock<IPlaybookOrchestrationService>();
        _loggerMock = new Mock<ILogger<PlaybookSchedulerJob>>();

        _scopeFactoryMock.Setup(f => f.CreateScope()).Returns(_scopeMock.Object);
        _scopeMock.Setup(s => s.ServiceProvider).Returns(_serviceProviderMock.Object);
        _serviceProviderMock
            .Setup(p => p.GetService(typeof(Spaarke.Dataverse.IGenericEntityService)))
            .Returns(_entityServiceMock.Object);
        _serviceProviderMock
            .Setup(p => p.GetService(typeof(IPlaybookOrchestrationService)))
            .Returns(_orchestrationServiceMock.Object);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TENANT_ID"] = "test-tenant"
            })
            .Build();

        _sut = new PlaybookSchedulerJob(
            _scopeFactoryMock.Object,
            _configuration,
            _loggerMock.Object);
    }

    // ── Identity contract ─────────────────────────────────────────────────────────────────

    [Fact]
    public void JobId_IsCanonicalNotificationPlaybookSchedulerKey()
    {
        _sut.JobId.Should().Be("notification-playbook-scheduler",
            "FR-2.8 / D2 mandate this exact key for the single sprk_backgroundjob row");
        PlaybookSchedulerJob.JobIdConstant.Should().Be(_sut.JobId,
            "constant + property MUST agree");
    }

    [Fact]
    public void DisplayName_AndDescription_AreNonEmptyForAdminUi()
    {
        _sut.DisplayName.Should().NotBeNullOrWhiteSpace();
        _sut.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Constructor_WithNullScopeFactory_ThrowsArgumentNullException()
    {
        var act = () => new PlaybookSchedulerJob(null!, _configuration, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("scopeFactory");
    }

    [Fact]
    public void Constructor_WithNullConfiguration_ThrowsArgumentNullException()
    {
        var act = () => new PlaybookSchedulerJob(_scopeFactoryMock.Object, null!, _loggerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new PlaybookSchedulerJob(_scopeFactoryMock.Object, _configuration, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── No active playbooks: success / processedItems=0 / empty children ─────────────────

    [Fact]
    public async Task ExecuteAsync_WhenNoActivePlaybooks_ReturnsSuccessWithProcessedItemsZero()
    {
        SetupPlaybookQuery(new List<Entity>());

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(0);
        result.ResultJson.Should().NotBeNullOrEmpty();
        var children = ParseChildren(result.ResultJson!);
        children.Should().BeEmpty("no playbooks found = no children");
    }

    // ── Fan-out cardinality + fresh correlationIds (FR-2.8 / Q1) ────────────────────────

    [Fact]
    public async Task ExecuteAsync_WithSevenActivePlaybooks_FansOutToSevenChildren()
    {
        var playbooks = Enumerable.Range(1, 7)
            .Select(i => CreatePlaybookEntity(Guid.NewGuid(), $"Playbook {i}"))
            .ToList();

        SetupPlaybookQuery(playbooks);
        SetupActiveUsers(new List<Entity>()); // no users — fan-out still completes per-playbook
        SetupUpdateNoop();
        SetupOrchestrationEmpty();

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ProcessedItems.Should().Be(7);

        var children = ParseChildren(result.ResultJson!);
        children.Should().HaveCount(7, "single backgroundjob row fans out across the 7 active playbooks per D2");
    }

    [Fact]
    public async Task ExecuteAsync_EachChild_GetsUniqueCorrelationId_PerQ1()
    {
        var playbooks = Enumerable.Range(1, 7)
            .Select(i => CreatePlaybookEntity(Guid.NewGuid(), $"Playbook {i}"))
            .ToList();

        SetupPlaybookQuery(playbooks);
        SetupActiveUsers(new List<Entity>());
        SetupUpdateNoop();
        SetupOrchestrationEmpty();

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);

        var children = ParseChildren(result.ResultJson!);
        var correlationIds = children.Select(c => c.CorrelationId).ToList();
        correlationIds.Should().AllSatisfy(id => id.Should().NotBeNullOrWhiteSpace());
        correlationIds.Distinct().Should().HaveCount(7,
            "Q1: each child playbook MUST get a fresh correlationId");
    }

    [Fact]
    public async Task ExecuteAsync_ChildCorrelationIds_AreNotEqualToParentCorrelationId()
    {
        var parentCorrelationId = Guid.NewGuid().ToString("N");
        var playbook = CreatePlaybookEntity(Guid.NewGuid(), "Single");

        SetupPlaybookQuery(new List<Entity> { playbook });
        SetupActiveUsers(new List<Entity>());
        SetupUpdateNoop();
        SetupOrchestrationEmpty();

        var context = new JobRunContext(
            RunId: Guid.NewGuid(),
            CorrelationId: parentCorrelationId,
            Trigger: JobRunTrigger.Scheduled,
            Parameters: new Dictionary<string, object>());

        var result = await _sut.ExecuteAsync(context, CancellationToken.None);
        var children = ParseChildren(result.ResultJson!);

        children.Single().CorrelationId.Should().NotBe(parentCorrelationId,
            "Q1: child correlationIds are fresh — not the parent's");
    }

    [Fact]
    public async Task ExecuteAsync_ResultJson_RecordsChildPlaybookIdAndCorrelationId()
    {
        var playbookId = Guid.NewGuid();
        var playbook = CreatePlaybookEntity(playbookId, "Recorded Playbook");

        SetupPlaybookQuery(new List<Entity> { playbook });
        SetupActiveUsers(new List<Entity>());
        SetupUpdateNoop();
        SetupOrchestrationEmpty();

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);
        var children = ParseChildren(result.ResultJson!);

        children.Should().HaveCount(1);
        children[0].PlaybookId.Should().Be(playbookId);
        children[0].PlaybookName.Should().Be("Recorded Playbook");
        children[0].CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    // ── Schedule due-check (NFR-04 cadence preservation) ─────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SkipsPlaybook_WhenNotDueBasedOnSchedule()
    {
        var playbookId = Guid.NewGuid();
        var playbook = CreatePlaybookEntity(playbookId, "Not Due",
            scheduleJson: @"{""schedule"":{""frequency"":""daily"",""time"":""06:00""}}");
        playbook["sprk_lastrundate"] = DateTime.UtcNow.AddMinutes(-5);

        SetupPlaybookQuery(new List<Entity> { playbook });

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);
        var children = ParseChildren(result.ResultJson!);

        children.Should().ContainSingle();
        children[0].Status.Should().Be("Skipped");
        children[0].UserCount.Should().Be(0);

        _orchestrationServiceMock.Verify(
            o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "skipped playbook should not call orchestration");
    }

    [Theory]
    [InlineData("hourly", -2.0, true)]   // 2h elapsed >= 1h
    [InlineData("hourly", -0.5, false)]  // 30 min elapsed < 1h
    [InlineData("daily", -25.0, true)]   // 25h elapsed >= 24h
    [InlineData("daily", -23.0, false)]  // 23h elapsed < 24h
    [InlineData("weekly", -169.0, true)]  // 169h elapsed >= 168h (7 days)
    [InlineData("weekly", -100.0, false)] // 100h elapsed < 168h
    public void IsPlaybookDue_RespectsFrequencySemantics(string frequency, double hoursOffset, bool expectedDue)
    {
        var lastRun = DateTimeOffset.UtcNow.AddHours(hoursOffset);
        var schedule = new PlaybookSchedulerJob.ScheduleConfig(frequency, "00:00");

        PlaybookSchedulerJob.IsPlaybookDue(lastRun, schedule).Should().Be(expectedDue);
    }

    [Fact]
    public void IsPlaybookDue_WithNeverRun_ReturnsTrueImmediately()
    {
        PlaybookSchedulerJob.IsPlaybookDue(null, new PlaybookSchedulerJob.ScheduleConfig("daily", "06:00"))
            .Should().BeTrue();
    }

    // ── Per-playbook failure isolation (preserves legacy behavior) ──────────────────────

    [Fact]
    public async Task ExecuteAsync_ContinuesWithRemainingPlaybooks_WhenOnePlaybookFails()
    {
        var failPlaybook = CreatePlaybookEntity(Guid.NewGuid(), "Fail");
        var okPlaybook = CreatePlaybookEntity(Guid.NewGuid(), "Ok");

        SetupPlaybookQuery(new List<Entity> { failPlaybook, okPlaybook });

        // First user query throws (fail playbook), second returns empty (ok playbook).
        var userCallCount = 0;
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "systemuser"),
                It.IsAny<CancellationToken>()))
            .Returns<QueryExpression, CancellationToken>((_, _) =>
            {
                userCallCount++;
                if (userCallCount == 1)
                    throw new InvalidOperationException("Simulated user query failure");
                return Task.FromResult(new EntityCollection());
            });
        SetupUpdateNoop();
        SetupOrchestrationEmpty();

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue("outer try/catch only fails the run on unhandled exceptions ABOVE the per-playbook loop");
        result.ProcessedItems.Should().Be(2);

        var children = ParseChildren(result.ResultJson!);
        children.Should().HaveCount(2);
        children.Should().ContainSingle(c => c.Status == "Failed");
        children.Should().ContainSingle(c => c.Status == "Succeeded");
        children.Single(c => c.Status == "Failed").ErrorMessage.Should().Contain("Simulated user query failure");
    }

    // ── Per-user fan-out tracking ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PassesUserParametersToOrchestrationService()
    {
        var playbook = CreatePlaybookEntity(Guid.NewGuid(), "Per-User");
        var user = CreateUserEntity(Guid.NewGuid(), "Jane Doe");

        SetupPlaybookQuery(new List<Entity> { playbook });
        SetupActiveUsers(new List<Entity> { user });
        SetupUpdateNoop();

        PlaybookRunRequest? capturedRequest = null;
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<PlaybookRunRequest, string, CancellationToken>((req, _, _) =>
            {
                capturedRequest = req;
                return EmptyStreamEvents();
            });

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Parameters!["userId"].Should().Be(user.Id.ToString());
        capturedRequest.Parameters!["userName"].Should().Be("Jane Doe");
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_RecordsUserCount_InChildPlaybookRun()
    {
        var playbook = CreatePlaybookEntity(Guid.NewGuid(), "Multi-User");
        var users = Enumerable.Range(1, 3)
            .Select(i => CreateUserEntity(Guid.NewGuid(), $"User {i}"))
            .ToList();

        SetupPlaybookQuery(new List<Entity> { playbook });
        SetupActiveUsers(users);
        SetupUpdateNoop();
        SetupOrchestrationEmpty();

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);
        var children = ParseChildren(result.ResultJson!);

        children.Should().ContainSingle();
        children[0].UserCount.Should().Be(3);
        children[0].SuccessCount.Should().Be(3);
        children[0].FailureCount.Should().Be(0);
        children[0].Status.Should().Be("Succeeded");
    }

    // ── Cancellation (NFR-07) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HonorsCancellation_BeforeAnyPlaybooksProcessed()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Setup empty playbook query so we don't even reach the for-each.
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns<QueryExpression, CancellationToken>((_, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new EntityCollection());
            });

        var result = await _sut.ExecuteAsync(BuildContext(), cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cancelled");
    }

    [Fact]
    public async Task ExecuteAsync_HonorsCancellation_BetweenPlaybooks_ReturnsPartialResult()
    {
        // 3 playbooks; cancel before the loop visits the second/third.
        var playbook1 = CreatePlaybookEntity(Guid.NewGuid(), "P1");
        var playbook2 = CreatePlaybookEntity(Guid.NewGuid(), "P2");
        var playbook3 = CreatePlaybookEntity(Guid.NewGuid(), "P3");

        using var cts = new CancellationTokenSource();

        SetupPlaybookQuery(new List<Entity> { playbook1, playbook2, playbook3 });
        SetupActiveUsers(new List<Entity>());

        // Cancel after the first playbook's last-run update completes — so the for-loop's
        // top-of-iteration cancellation check trips before iter 2.
        _entityServiceMock
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, _, _) =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            });
        SetupOrchestrationEmpty();

        var result = await _sut.ExecuteAsync(BuildContext(), cts.Token);

        // Outer catch handles OperationCanceledException → Success=false + ResultJson with partial.
        // BUT the cancellation check inside the loop returns from the inside (success=true, partial).
        // Either path is acceptable per NFR-07 — verify we get a partial children list (< 3).
        var children = ParseChildren(result.ResultJson!);
        children.Count.Should().BeLessThan(3,
            "cancellation should halt fan-out before all 3 playbooks process");
        children.Count.Should().BeGreaterThan(0,
            "first playbook should have completed before cancellation tripped");
    }

    // ── Last-run persistence (preserves legacy behavior) ────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PersistsLastRunTimestamp_AfterPlaybookFanOutCompletes()
    {
        var playbookId = Guid.NewGuid();
        var playbook = CreatePlaybookEntity(playbookId, "Persist Test");

        SetupPlaybookQuery(new List<Entity> { playbook });
        SetupActiveUsers(new List<Entity>());
        SetupOrchestrationEmpty();

        Dictionary<string, object>? captured = null;
        string? capturedName = null;
        Guid? capturedId = null;
        _entityServiceMock
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((name, id, fields, _) =>
            {
                capturedName = name;
                capturedId = id;
                captured = fields;
            })
            .Returns(Task.CompletedTask);

        await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);

        capturedName.Should().Be("sprk_analysisplaybook");
        capturedId.Should().Be(playbookId);
        captured.Should().ContainKey("sprk_lastrundate");
        var persisted = (DateTime)captured!["sprk_lastrundate"];
        persisted.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesGracefully_WhenLastRunPersistenceFails()
    {
        var playbook = CreatePlaybookEntity(Guid.NewGuid(), "Persist Fail");

        SetupPlaybookQuery(new List<Entity> { playbook });
        SetupActiveUsers(new List<Entity>());
        SetupOrchestrationEmpty();

        _entityServiceMock
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Dataverse unavailable"));

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);

        result.Success.Should().BeTrue("persistence failure is non-fatal — next tick re-dispatches");
        result.ProcessedItems.Should().Be(1);
    }

    // ── Schedule parsing fallbacks ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_UsesDefaultSchedule_WhenConfigJsonNull()
    {
        var playbook = CreatePlaybookEntity(Guid.NewGuid(), "No Config", scheduleJson: null);
        // Never run — default (daily) means due immediately.

        SetupPlaybookQuery(new List<Entity> { playbook });
        SetupActiveUsers(new List<Entity>());
        SetupUpdateNoop();
        SetupOrchestrationEmpty();

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);
        var children = ParseChildren(result.ResultJson!);

        children.Should().ContainSingle().Which.Status.Should().Be("Succeeded");
    }

    [Fact]
    public async Task ExecuteAsync_UsesDefaultSchedule_WhenConfigJsonInvalid()
    {
        var playbook = CreatePlaybookEntity(Guid.NewGuid(), "Bad Config", scheduleJson: "not-json!");

        SetupPlaybookQuery(new List<Entity> { playbook });
        SetupActiveUsers(new List<Entity>());
        SetupUpdateNoop();
        SetupOrchestrationEmpty();

        var result = await _sut.ExecuteAsync(BuildContext(), CancellationToken.None);
        var children = ParseChildren(result.ResultJson!);

        children.Should().ContainSingle().Which.Status.Should().Be("Succeeded");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private static JobRunContext BuildContext() => new(
        RunId: Guid.NewGuid(),
        CorrelationId: Guid.NewGuid().ToString("N"),
        Trigger: JobRunTrigger.Scheduled,
        Parameters: new Dictionary<string, object>());

    private void SetupPlaybookQuery(List<Entity> playbooks)
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_analysisplaybook"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(playbooks));
    }

    private void SetupActiveUsers(List<Entity> users)
    {
        _entityServiceMock
            .Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "systemuser"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(users));
    }

    private void SetupUpdateNoop() =>
        _entityServiceMock
            .Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

    private void SetupOrchestrationEmpty() =>
        _orchestrationServiceMock
            .Setup(o => o.ExecuteAppOnlyAsync(It.IsAny<PlaybookRunRequest>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyStreamEvents());

    private static Entity CreatePlaybookEntity(Guid id, string name, string? scheduleJson = null)
    {
        var entity = new Entity("sprk_analysisplaybook", id);
        entity["sprk_name"] = name;
        if (scheduleJson != null)
            entity["sprk_configjson"] = scheduleJson;
        return entity;
    }

    private static Entity CreateUserEntity(Guid id, string fullName)
    {
        var entity = new Entity("systemuser", id);
        entity["fullname"] = fullName;
        return entity;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> EmptyStreamEvents(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    private static IReadOnlyList<ChildPayload> ParseChildren(string resultJson)
    {
        using var doc = JsonDocument.Parse(resultJson);
        // Wrapper key is camelCase "children" per default STJ camelCase policy on record properties? STJ default is PascalCase.
        // PlaybookSchedulerJob serializes with no naming policy override, so the key matches the record property name: "Children".
        var arr = doc.RootElement.GetProperty("Children");
        var list = new List<ChildPayload>();
        foreach (var element in arr.EnumerateArray())
        {
            list.Add(new ChildPayload(
                PlaybookId: element.GetProperty("PlaybookId").GetGuid(),
                PlaybookName: element.GetProperty("PlaybookName").GetString() ?? string.Empty,
                CorrelationId: element.GetProperty("CorrelationId").GetString() ?? string.Empty,
                Status: element.GetProperty("Status").GetString() ?? string.Empty,
                UserCount: element.GetProperty("UserCount").GetInt32(),
                SuccessCount: element.GetProperty("SuccessCount").GetInt32(),
                FailureCount: element.GetProperty("FailureCount").GetInt32(),
                ErrorMessage: element.TryGetProperty("ErrorMessage", out var errEl) && errEl.ValueKind == JsonValueKind.String
                    ? errEl.GetString()
                    : null));
        }
        return list;
    }

    private sealed record ChildPayload(
        Guid PlaybookId,
        string PlaybookName,
        string CorrelationId,
        string Status,
        int UserCount,
        int SuccessCount,
        int FailureCount,
        string? ErrorMessage);
}
