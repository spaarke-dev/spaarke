using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Admin.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Admin;

/// <summary>
/// Integration tests for R3 task 020 — <c>GET /api/admin/jobs</c> and
/// <c>GET /api/admin/jobs/{jobId}/status</c>. Validates the auth contract (401 / 403 / 200),
/// the list/empty/missing-definition shapes, and the per-job detail surface (404 on unknown id,
/// last-10-runs ordering, AC-2.7 error surfacing).
/// </summary>
/// <remarks>
/// <para>Fixture is shared per test class (<see cref="IClassFixture{T}"/>) so the in-process
/// host is built once. Tests seed <see cref="ScheduledJobRegistry"/> + <see cref="InMemoryBackgroundJobStore"/>
/// via the fixture's <see cref="AdminJobsTestFixture.Registry"/> / <see cref="AdminJobsTestFixture.Store"/>
/// accessors. Tests with intersecting JobIds use distinct suffixes to avoid duplicate-registration
/// throws (the registry rejects duplicate JobIds by design — see <c>ScheduledJobRegistry.Register</c>).</para>
/// </remarks>
public sealed class JobsEndpointsTests : IClassFixture<AdminJobsTestFixture>
{
    private readonly AdminJobsTestFixture _fixture;

    public JobsEndpointsTests(AdminJobsTestFixture fixture)
    {
        _fixture = fixture;
    }

    // ================================================================================
    // ===== AC-2.5: 401 unauthenticated, 403 non-admin ==============================
    // ================================================================================

    [Fact]
    public async Task ListJobs_Returns401_WhenUnauthenticated()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/admin/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListJobs_Returns403_WhenAuthenticatedButNotAdmin()
    {
        using var client = _fixture.CreateNonAdminClient();

        var response = await client.GetAsync("/api/admin/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetJobStatus_Returns401_WhenUnauthenticated()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/admin/jobs/any-job/status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetJobStatus_Returns403_WhenAuthenticatedButNotAdmin()
    {
        using var client = _fixture.CreateNonAdminClient();

        var response = await client.GetAsync("/api/admin/jobs/any-job/status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================================
    // ===== GET /api/admin/jobs — list endpoint =====================================
    // ================================================================================

    [Fact]
    public async Task ListJobs_ReturnsSeededJobs_WithStatusSummary()
    {
        // Arrange — initialize client first so registry + store are resolved.
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var jobId = $"list-test-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new FakeScheduledJob(jobId, "Test Job", "A test job"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            JobId: jobId,
            DisplayName: "Test Job (definition)",
            Description: "A test job (from definition)",
            Enabled: true,
            CronSchedule: "0 2 * * *",     // daily 02:00 UTC
            ConfigJson: null));

        // Act
        var summaries = await client.GetFromJsonAsync<List<JobStatusSummary>>("/api/admin/jobs");

        // Assert
        summaries.Should().NotBeNull();
        var seeded = summaries!.FirstOrDefault(s => s.JobId == jobId);
        seeded.Should().NotBeNull("the seeded job must appear in the list response");
        seeded!.DisplayName.Should().Be("Test Job (definition)", "the store's DisplayName should take precedence over the handler's");
        seeded.Enabled.Should().BeTrue();
        seeded.CronSchedule.Should().Be("0 2 * * *");
        seeded.LastRunStartedOn.Should().BeNull("no run records were seeded");
        seeded.NextScheduledOn.Should().NotBeNull("Cronos should compute the next 02:00 UTC occurrence for an enabled job");
        seeded.NextScheduledOn!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ListJobs_ReturnsEmptyList_WhenNoJobsRegistered()
    {
        // Arrange — wipe state first.
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        // Act
        var response = await client.GetAsync("/api/admin/jobs");

        // Assert — empty registry returns 200 + empty list (NOT 404).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaries = await response.Content.ReadFromJsonAsync<List<JobStatusSummary>>();
        summaries.Should().NotBeNull();
        summaries!.Should().BeEmpty();
    }

    [Fact]
    public async Task ListJobs_SurfacesRegisteredJobsMissingDefinition_WithEmptyCronAndDisabled()
    {
        // Arrange — register a handler but DON'T seed a definition row.
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var jobId = $"orphan-handler-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new FakeScheduledJob(jobId, "Orphan", "Handler without definition"));

        // Act
        var summaries = await client.GetFromJsonAsync<List<JobStatusSummary>>("/api/admin/jobs");

        // Assert — surfaces the row with Enabled=false + empty cron + NextScheduledOn=null.
        var seeded = summaries!.FirstOrDefault(s => s.JobId == jobId);
        seeded.Should().NotBeNull("the admin surface MUST show orphan handlers so operators can spot misconfigs");
        seeded!.DisplayName.Should().Be("Orphan", "DisplayName falls back to the handler value when no definition exists");
        seeded.Enabled.Should().BeFalse();
        seeded.CronSchedule.Should().BeEmpty();
        seeded.NextScheduledOn.Should().BeNull("no enabled definition → no next occurrence");
    }

    [Fact]
    public async Task ListJobs_SurfacesLastRunStatus_WhenRunRecordsExist()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var jobId = $"with-runs-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new FakeScheduledJob(jobId, "Run-history Job", "Has runs"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            JobId: jobId,
            DisplayName: "Run-history Job",
            Description: "Has runs",
            Enabled: true,
            CronSchedule: "*/5 * * * *",
            ConfigJson: null));

        var olderStart = DateTimeOffset.UtcNow.AddMinutes(-10);
        var newerStart = DateTimeOffset.UtcNow.AddMinutes(-2);
        SeedCompletedRun(jobId, olderStart, success: true);
        SeedCompletedRun(jobId, newerStart, success: false, errorMessage: "boom");

        // Act
        var summaries = await client.GetFromJsonAsync<List<JobStatusSummary>>("/api/admin/jobs");

        // Assert — the NEWER run is reported as Last*.
        var seeded = summaries!.First(s => s.JobId == jobId);
        seeded.LastRunStartedOn.Should().NotBeNull();
        seeded.LastRunStartedOn!.Value.Should().BeCloseTo(newerStart, TimeSpan.FromSeconds(2));
        seeded.LastRunStatus.Should().Be("Failed");
    }

    // ================================================================================
    // ===== GET /api/admin/jobs/{jobId}/status — detail endpoint ====================
    // ================================================================================

    [Fact]
    public async Task GetJobStatus_Returns200_WithDetailIncludingRecentRuns()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var jobId = $"detail-test-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new FakeScheduledJob(jobId, "Detail Job", "Detail test"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            JobId: jobId,
            DisplayName: "Detail Job",
            Description: "Detail test",
            Enabled: true,
            CronSchedule: "0 * * * *",
            ConfigJson: null));

        // Seed 12 runs — only the newest 10 should come back.
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-60);
        for (var i = 0; i < 12; i++)
        {
            SeedCompletedRun(jobId, baseTime.AddMinutes(i), success: (i % 2) == 0);
        }

        // Act
        var detail = await client.GetFromJsonAsync<JobStatusDetail>($"/api/admin/jobs/{jobId}/status");

        // Assert
        detail.Should().NotBeNull();
        detail!.JobId.Should().Be(jobId);
        detail.DisplayName.Should().Be("Detail Job");
        detail.Enabled.Should().BeTrue();
        detail.RecentRuns.Should().HaveCount(10, "the detail endpoint caps recent runs at 10");
        // Most-recent first
        detail.RecentRuns[0].StartedOn.Should().BeAfter(detail.RecentRuns[1].StartedOn);
        detail.LastRunStartedOn.Should().Be(detail.RecentRuns[0].StartedOn);
        detail.NextScheduledOn.Should().NotBeNull();
    }

    [Fact]
    public async Task GetJobStatus_Returns404_WhenJobIdNotRegistered()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        // Act — no registry seeding.
        var response = await client.GetAsync($"/api/admin/jobs/totally-unknown-{Guid.NewGuid():N}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetJobStatus_SurfacesFailedRunErrorMessage_PerAC27()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var jobId = $"failure-surface-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new FakeScheduledJob(jobId, "Failing Job", "Always fails"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            JobId: jobId,
            DisplayName: "Failing Job",
            Description: "Always fails",
            Enabled: true,
            CronSchedule: "0 3 * * *",
            ConfigJson: null));

        var failureMessage = "Connection to upstream service refused (AC-2.7 surface)";
        SeedCompletedRun(jobId, DateTimeOffset.UtcNow.AddMinutes(-1), success: false, errorMessage: failureMessage);

        // Act
        var detail = await client.GetFromJsonAsync<JobStatusDetail>($"/api/admin/jobs/{jobId}/status");

        // Assert — failed run surfaces via RecentRuns[0].ErrorMessage (AC-2.7).
        detail.Should().NotBeNull();
        detail!.RecentRuns.Should().HaveCount(1);
        detail.RecentRuns[0].Status.Should().Be("Failed");
        detail.RecentRuns[0].ErrorMessage.Should().Be(failureMessage);
        detail.LastRunStatus.Should().Be("Failed");
    }

    // ================================================================================
    // ===== Task 021: POST /api/admin/jobs/{jobId}/trigger ==========================
    // ================================================================================
    //
    // Coverage (matches task 021 brief):
    //   - 202 + TriggerResponse on success (with persisted RunId, "Running" status, StartedAt)
    //   - 404 on unknown jobId (host throws JobNotFoundException → mapped to ProblemDetails)
    //   - 403 for non-admin token (AC-2.5)
    //   - 401 for missing auth (AC-2.5)
    //   - Run row persisted with trigger=ManualAdmin (verify via InMemoryBackgroundJobStore.RunRecords)
    //   - Fresh correlationId per trigger (verify two triggers → two distinct runIds + correlationIds — NFR-08)
    //   - Run with non-null scheduledFireUtc constraint omitted (manual triggers pass scheduledFireUtc=null)

    [Fact]
    public async Task TriggerJob_Returns401_WhenUnauthenticated()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsync("/api/admin/jobs/any-job/trigger", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TriggerJob_Returns403_WhenAuthenticatedButNotAdmin()
    {
        using var client = _fixture.CreateNonAdminClient();

        var response = await client.PostAsync("/api/admin/jobs/any-job/trigger", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TriggerJob_Returns404_WhenJobIdNotRegistered()
    {
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var response = await client.PostAsync(
            $"/api/admin/jobs/totally-unknown-{Guid.NewGuid():N}/trigger",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TriggerJob_Returns202_WithTriggerResponse_OnSuccess()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var jobId = $"trigger-test-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new FakeScheduledJob(jobId, "Trigger Job", "For trigger tests"));
        // Definition is OPTIONAL for manual triggers — host falls back to handler defaults if absent.
        // Seed one anyway so the test exercises the configJson flow as well.
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            JobId: jobId,
            DisplayName: "Trigger Job",
            Description: "For trigger tests",
            Enabled: true,
            CronSchedule: "0 2 * * *",
            ConfigJson: "{\"hello\":\"world\"}"));

        var sentAt = DateTimeOffset.UtcNow;

        // Act
        var response = await client.PostAsync($"/api/admin/jobs/{jobId}/trigger", content: null);

        // Assert — status + body shape.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var trigger = await response.Content.ReadFromJsonAsync<TriggerResponse>();
        trigger.Should().NotBeNull();
        trigger!.RunId.Should().NotBe(Guid.Empty);
        trigger.Status.Should().Be("Running");
        // StartedAt should be within a small window of the request (allows for clock skew + dispatch overhead).
        trigger.StartedAt.Should().BeCloseTo(sentAt, TimeSpan.FromSeconds(5));

        // Assert — Location header should point to the canonical run-resource path.
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should()
            .Be($"/api/admin/jobs/{jobId}/runs/{trigger.RunId}");

        // Give the background task time to execute the (synchronous, fast) handler so the
        // run-complete record is written before the next assertion.
        await WaitUntilAsync(
            () => _fixture.Store.RunRecords.Any(r => r.RunId == trigger.RunId && r.CompletedAtUtc is not null),
            TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TriggerJob_PersistsRunRowWithManualAdminTrigger()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var jobId = $"persist-trigger-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new FakeScheduledJob(jobId, "Persist Job", "Persistence test"));

        // Act
        var response = await client.PostAsync($"/api/admin/jobs/{jobId}/trigger", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var trigger = await response.Content.ReadFromJsonAsync<TriggerResponse>();

        // Wait for run-complete to be persisted (the background task runs the no-op handler instantly).
        await WaitUntilAsync(
            () => _fixture.Store.RunRecords.Any(r => r.RunId == trigger!.RunId && r.CompletedAtUtc is not null),
            TimeSpan.FromSeconds(5));

        // Assert — exactly one run record for this jobId; matches the returned runId; trigger=ManualAdmin.
        var matchingRuns = _fixture.Store.RunRecords
            .Where(r => r.JobId == jobId)
            .ToList();
        matchingRuns.Should().HaveCount(1, "exactly one trigger → exactly one run record");
        var run = matchingRuns[0];
        run.RunId.Should().Be(trigger!.RunId);
        run.Trigger.Should().Be(JobRunTrigger.ManualAdmin, "FR-2.6 / task 021 requires Trigger=ManualAdmin for /trigger dispatches");
        // Manual triggers MUST persist with scheduledFireUtc=null per IBackgroundJobStore contract.
        run.ScheduledFireUtc.Should().BeNull("manual triggers don't participate in tick-level idempotency");
        run.CorrelationId.Should().NotBeNullOrEmpty("NFR-08 requires every run to carry a correlationId");
        run.Result.Should().NotBeNull();
        run.Result!.Success.Should().BeTrue("FakeScheduledJob always succeeds");
    }

    [Fact]
    public async Task TriggerJob_TwoSequentialTriggers_ProduceDistinctRunIdsAndCorrelationIds_NFR08()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        ResetSchedulingState();

        var jobId = $"correlation-test-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new FakeScheduledJob(jobId, "Correlation Job", "Correlation test"));

        // Act — fire twice.
        var firstResponse = await client.PostAsync($"/api/admin/jobs/{jobId}/trigger", content: null);
        var first = await firstResponse.Content.ReadFromJsonAsync<TriggerResponse>();
        var secondResponse = await client.PostAsync($"/api/admin/jobs/{jobId}/trigger", content: null);
        var second = await secondResponse.Content.ReadFromJsonAsync<TriggerResponse>();

        // Wait until both runs have completed so the store has both records.
        await WaitUntilAsync(
            () => _fixture.Store.RunRecords.Count(r => r.JobId == jobId && r.CompletedAtUtc is not null) >= 2,
            TimeSpan.FromSeconds(5));

        // Assert — RunIds distinct from the API response.
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.RunId.Should().NotBe(second!.RunId, "every manual trigger MUST yield a distinct runId");

        // Assert — both run records exist; correlationIds are distinct (NFR-08 fresh-per-run).
        var matchingRuns = _fixture.Store.RunRecords
            .Where(r => r.JobId == jobId)
            .ToList();
        matchingRuns.Should().HaveCount(2);
        matchingRuns.Select(r => r.CorrelationId).Distinct().Should().HaveCount(2,
            "NFR-08 mandates a fresh correlationId per run, even across rapid back-to-back manual triggers");
        matchingRuns.All(r => r.Trigger == JobRunTrigger.ManualAdmin).Should().BeTrue();
    }

    /// <summary>Polls a predicate until satisfied or the deadline elapses (kept local to test class).</summary>
    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (predicate()) return;
            await Task.Delay(25);
        }
        if (!predicate())
        {
            throw new TimeoutException($"Predicate did not become true within {timeout}");
        }
    }

    // ================================================================================
    // ===== Test helpers =============================================================
    // ================================================================================

    /// <summary>
    /// Reset the in-process registry + store between tests. The fixture is shared so the
    /// stores accumulate state across tests; tests that care about exact list shape must
    /// call this first. The registry has no Clear() method (single-host invariant), so we
    /// reflect over its concurrent dictionary backing — pragmatic test-only seam.
    /// </summary>
    private void ResetSchedulingState()
    {
        // Clear the runs by reflecting the private _runs dictionary. The store doesn't expose
        // a public Clear() (it's an in-prod implementation; only test code needs reset).
        var runsField = typeof(InMemoryBackgroundJobStore)
            .GetField("_runs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var jobsField = typeof(InMemoryBackgroundJobStore)
            .GetField("_jobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var runs = runsField!.GetValue(_fixture.Store);
        var jobs = jobsField!.GetValue(_fixture.Store);
        runs!.GetType().GetMethod("Clear")!.Invoke(runs, null);
        jobs!.GetType().GetMethod("Clear")!.Invoke(jobs, null);

        // Registry — also reflect, for the same reason. Production registry is intentionally
        // append-only (single host owns it from startup); tests need symmetric teardown.
        var registryField = typeof(ScheduledJobRegistry)
            .GetField("_jobs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var registryJobs = registryField!.GetValue(_fixture.Registry);
        registryJobs!.GetType().GetMethod("Clear")!.Invoke(registryJobs, null);
    }

    private void SeedCompletedRun(string jobId, DateTimeOffset startedAt, bool success, string? errorMessage = null)
    {
        var runId = Guid.NewGuid();
        _fixture.Store.SeedRunRecord(new InMemoryBackgroundJobStore.RunRecord(
            RunId: runId,
            JobId: jobId,
            Trigger: JobRunTrigger.Scheduled,
            CorrelationId: Guid.NewGuid().ToString("N"),
            ScheduledFireUtc: startedAt,
            StartedAtUtc: startedAt,
            CompletedAtUtc: startedAt.AddSeconds(1),
            Result: new JobRunResult(
                Success: success,
                ErrorMessage: success ? null : (errorMessage ?? "test-failure"),
                ProcessedItems: success ? 5 : null,
                Duration: TimeSpan.FromSeconds(1))));
    }

    /// <summary>Test-only no-op <see cref="IScheduledJob"/> for registry seeding.</summary>
    private sealed class FakeScheduledJob : IScheduledJob
    {
        public FakeScheduledJob(string jobId, string displayName, string description)
        {
            JobId = jobId;
            DisplayName = displayName;
            Description = description;
        }

        public string JobId { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new JobRunResult(true, null, 0, TimeSpan.Zero));
    }
}
