using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Spaarke.Scheduling;
using Sprk.Bff.Api.Api.Admin.Models;
using Xunit;

namespace Sprk.Bff.Api.IntegrationTests.Admin;

/// <summary>
/// End-to-end integration tests for R3 task 025 — exercise the full BFF bootstrap against the
/// production <c>SystemAdmin</c> authorization policy + production <c>SchedulingModule</c> DI
/// graph (real <see cref="ScheduledJobRegistry"/>, <see cref="InMemoryBackgroundJobStore"/>,
/// <see cref="ScheduledJobHost"/> singleton). Covers spec.md acceptance criteria:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><b>AC-2.1</b>: Spaarke.Scheduling library + admin discovery surface — verified via
///     <see cref="List_ReturnsRegisteredJobs_WithStatusSummary"/>.</item>
///   <item><b>AC-2.3</b>: GET /status + POST /trigger — verified via
///     <see cref="GetStatus_ReturnsJobAndLastRun"/> and
///     <see cref="Trigger_RunsJobOutOfBand_RecordsRun"/>.</item>
///   <item><b>AC-2.5</b>: Admin endpoints behind SystemAdmin — verified via
///     <see cref="NonAdmin_Returns403_OnAllEndpoints"/> (Q6 owner clarification: existing
///     SystemAdmin policy, not new PlatformAdmin).</item>
///   <item><b>AC-2.6</b>: Run records carry correlationId + trigger + status + duration —
///     verified via <see cref="Trigger_RunsJobOutOfBand_RecordsRun"/> + enable/disable tests.</item>
///   <item><b>AC-2.7</b>: Failed jobs surface error in /status — verified via
///     <see cref="GetStatus_SurfacesFailedJobErrorMessage"/>.</item>
/// </list>
///
/// <para><b>Coverage complement, not duplicate</b>: in-process WebApplicationFactory tests already
/// live at <c>tests/unit/Sprk.Bff.Api.Tests/Api/Admin/JobsEndpointsTests.cs</c> (~30 tests). This
/// suite intentionally focuses on the headline AC paths (one positive test per AC) so the
/// integration regression suite stays fast while still proving the full production wiring boots +
/// the auth policy is honored end-to-end. The unit-flavored suite covers the exhaustive
/// 4xx/5xx + edge cases (limit clamping, missing-definition fallbacks, enable/disable
/// idempotency, etc.).</para>
/// </remarks>
public sealed class JobsEndpointsTests : IClassFixture<AdminJobsIntegrationFixture>
{
    private readonly AdminJobsIntegrationFixture _fixture;

    public JobsEndpointsTests(AdminJobsIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    // ================================================================================
    // ===== AC-2.1: discovery surface (GET /api/admin/jobs) ==========================
    // ================================================================================

    [Fact]
    public async Task List_ReturnsRegisteredJobs_WithStatusSummary()
    {
        // Arrange — initialize client so the registry/store are resolvable, then seed.
        using var client = _fixture.CreateAdminClient();
        _fixture.ResetSchedulingState();

        var jobId = $"int-list-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new IntegrationFakeJob(jobId, "Integration Job", "Discovery"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            JobId: jobId,
            DisplayName: "Integration Job (def)",
            Description: "Discovery",
            Enabled: true,
            CronSchedule: "0 2 * * *",
            ConfigJson: null));

        // Act
        var response = await client.GetAsync("/api/admin/jobs");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK, "AC-2.1 — admin can discover registered jobs");
        var summaries = await response.Content.ReadFromJsonAsync<List<JobStatusSummary>>();
        summaries.Should().NotBeNull();
        var seeded = summaries!.SingleOrDefault(s => s.JobId == jobId);
        seeded.Should().NotBeNull("the seeded job MUST appear in the list response");
        seeded!.DisplayName.Should().Be("Integration Job (def)",
            "definition row's DisplayName takes precedence over the handler's");
        seeded.Enabled.Should().BeTrue();
        seeded.CronSchedule.Should().Be("0 2 * * *");
        seeded.NextScheduledOn.Should().NotBeNull("Cronos should compute the next 02:00 UTC occurrence");
        seeded.NextScheduledOn!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    // ================================================================================
    // ===== AC-2.3: per-job status + manual trigger ==================================
    // ================================================================================

    [Fact]
    public async Task GetStatus_ReturnsJobAndLastRun()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        _fixture.ResetSchedulingState();

        var jobId = $"int-status-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new IntegrationFakeJob(jobId, "Status Job", "AC-2.3"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            jobId, "Status Job", "AC-2.3", Enabled: true, CronSchedule: "0 * * * *", ConfigJson: null));

        // Seed a completed run so LastRun* fields populate.
        var ranAt = DateTimeOffset.UtcNow.AddMinutes(-3);
        SeedCompletedRun(jobId, ranAt, success: true);

        // Act
        var response = await client.GetAsync($"/api/admin/jobs/{jobId}/status");

        // Assert — full detail surface populated.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<JobStatusDetail>();
        detail.Should().NotBeNull();
        detail!.JobId.Should().Be(jobId);
        detail.LastRunStartedOn.Should().NotBeNull("a completed run was seeded");
        detail.LastRunStartedOn!.Value.Should().BeCloseTo(ranAt, TimeSpan.FromSeconds(2));
        detail.LastRunStatus.Should().Be("Succeeded");
        detail.RecentRuns.Should().HaveCount(1);
        detail.RecentRuns[0].CorrelationId.Should().NotBeNullOrEmpty(
            "AC-2.6 — every run record carries a correlationId");
    }

    [Fact]
    public async Task Trigger_RunsJobOutOfBand_RecordsRun()
    {
        // Arrange
        using var client = _fixture.CreateAdminClient();
        _fixture.ResetSchedulingState();

        var jobId = $"int-trigger-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new IntegrationFakeJob(jobId, "Trigger Job", "AC-2.3 + AC-2.6"));

        var requestedAt = DateTimeOffset.UtcNow;

        // Act — fire the manual trigger.
        var response = await client.PostAsync($"/api/admin/jobs/{jobId}/trigger", content: null);

        // Assert — 202 Accepted with TriggerResponse body + Location header.
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, "AC-2.3 — manual triggers return 202");
        var trigger = await response.Content.ReadFromJsonAsync<TriggerResponse>();
        trigger.Should().NotBeNull();
        trigger!.RunId.Should().NotBe(Guid.Empty);
        trigger.Status.Should().Be("Running");
        trigger.StartedAt.Should().BeCloseTo(requestedAt, TimeSpan.FromSeconds(5));
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Be($"/api/admin/jobs/{jobId}/runs/{trigger.RunId}");

        // Wait for the background dispatch to complete (the no-op handler runs instantly).
        await WaitUntilAsync(
            () => _fixture.Store.RunRecords.Any(r => r.RunId == trigger.RunId && r.CompletedAtUtc is not null),
            TimeSpan.FromSeconds(5));

        // AC-2.6 — verify the run record shape: ManualAdmin trigger + correlationId + duration + status.
        var run = _fixture.Store.RunRecords.Single(r => r.RunId == trigger.RunId);
        run.JobId.Should().Be(jobId);
        run.Trigger.Should().Be(JobRunTrigger.ManualAdmin, "/trigger MUST persist Trigger=ManualAdmin");
        run.CorrelationId.Should().NotBeNullOrEmpty("AC-2.6 — runs MUST carry correlationId");
        run.ScheduledFireUtc.Should().BeNull("manual triggers don't participate in tick idempotency");
        run.Result.Should().NotBeNull();
        run.Result!.Success.Should().BeTrue("IntegrationFakeJob always succeeds");
        run.Result.Duration.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero,
            "AC-2.6 — run records persist duration");
    }

    // ================================================================================
    // ===== AC-2.5 / AC-2.6: history + enable/disable ================================
    // ================================================================================

    [Fact]
    public async Task GetHistory_ReturnsRecentRuns_NewestFirst()
    {
        // Arrange — seed 5 runs, request 3.
        using var client = _fixture.CreateAdminClient();
        _fixture.ResetSchedulingState();

        var jobId = $"int-history-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new IntegrationFakeJob(jobId, "History Job", "AC-2.5/6"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            jobId, "History Job", "AC-2.5/6", true, "0 * * * *", null));

        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        for (var i = 0; i < 5; i++)
        {
            SeedCompletedRun(jobId, baseTime.AddMinutes(i), success: i % 2 == 0);
        }

        // Act
        var response = await client.GetAsync($"/api/admin/jobs/{jobId}/history?limit=3");

        // Assert — exactly 3 records, newest-first.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var details = await response.Content.ReadFromJsonAsync<List<JobRunDetail>>();
        details.Should().NotBeNull();
        details!.Should().HaveCount(3);
        details![0].StartedOn.Should().BeAfter(details[1].StartedOn);
        details[1].StartedOn.Should().BeAfter(details[2].StartedOn);
        details.All(d => !string.IsNullOrEmpty(d.CorrelationId)).Should().BeTrue(
            "AC-2.6 — every run record carries correlationId");
    }

    [Fact]
    public async Task Enable_SetsEnabledTrue_AndDisable_SetsEnabledFalse()
    {
        // Arrange — seed a definition Enabled=false.
        using var client = _fixture.CreateAdminClient();
        _fixture.ResetSchedulingState();

        var jobId = $"int-toggle-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new IntegrationFakeJob(jobId, "Toggle Job", "AC-2.6"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            jobId, "Toggle Job", "AC-2.6", Enabled: false, CronSchedule: "0 * * * *", ConfigJson: null));

        // Act — POST /enable.
        var enableResponse = await client.PostAsync($"/api/admin/jobs/{jobId}/enable", content: null);

        // Assert
        enableResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterEnable = (await _fixture.Store.LoadJobsAsync(CancellationToken.None))
            .Single(d => d.JobId == jobId);
        afterEnable.Enabled.Should().BeTrue("POST /enable MUST flip the definition to Enabled=true");

        // Act — POST /disable.
        var disableResponse = await client.PostAsync($"/api/admin/jobs/{jobId}/disable", content: null);

        // Assert
        disableResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterDisable = (await _fixture.Store.LoadJobsAsync(CancellationToken.None))
            .Single(d => d.JobId == jobId);
        afterDisable.Enabled.Should().BeFalse("POST /disable MUST flip the definition to Enabled=false");
    }

    // ================================================================================
    // ===== AC-2.7: failed-job surfacing =============================================
    // ================================================================================

    [Fact]
    public async Task GetStatus_SurfacesFailedJobErrorMessage()
    {
        // Arrange — seed a failure run with a known error message.
        using var client = _fixture.CreateAdminClient();
        _fixture.ResetSchedulingState();

        var jobId = $"int-failure-{Guid.NewGuid():N}";
        _fixture.Registry.Register(new IntegrationFakeJob(jobId, "Failure Job", "AC-2.7"));
        _fixture.Store.AddOrReplaceJob(new BackgroundJobDefinition(
            jobId, "Failure Job", "AC-2.7", true, "0 * * * *", null));

        const string failureMessage = "Upstream timeout (AC-2.7 integration surface)";
        SeedCompletedRun(jobId, DateTimeOffset.UtcNow.AddMinutes(-1), success: false, errorMessage: failureMessage);

        // Act
        var response = await client.GetAsync($"/api/admin/jobs/{jobId}/status");

        // Assert — the failed run's ErrorMessage surfaces in RecentRuns[0] AND drives LastRunStatus.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await response.Content.ReadFromJsonAsync<JobStatusDetail>();
        detail.Should().NotBeNull();
        detail!.LastRunStatus.Should().Be("Failed");
        detail.RecentRuns.Should().HaveCount(1);
        detail.RecentRuns[0].Status.Should().Be("Failed");
        detail.RecentRuns[0].ErrorMessage.Should().Be(failureMessage,
            "AC-2.7 — failed jobs surface in /status with last error message");
    }

    // ================================================================================
    // ===== AC-2.5 / Q6: SystemAdmin policy ==========================================
    // ================================================================================

    [Theory]
    [InlineData("GET", "/api/admin/jobs")]
    [InlineData("GET", "/api/admin/jobs/any-job/status")]
    [InlineData("POST", "/api/admin/jobs/any-job/trigger")]
    [InlineData("GET", "/api/admin/jobs/any-job/history")]
    [InlineData("POST", "/api/admin/jobs/any-job/enable")]
    [InlineData("POST", "/api/admin/jobs/any-job/disable")]
    public async Task NonAdmin_Returns403_OnAllEndpoints(string method, string path)
    {
        using var client = _fixture.CreateNonAdminClient();
        _fixture.ResetSchedulingState();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"AC-2.5 / Q6 — non-admin tokens MUST receive 403 on {method} {path} (SystemAdmin policy, not PlatformAdmin)");
    }

    [Theory]
    [InlineData("GET", "/api/admin/jobs")]
    [InlineData("GET", "/api/admin/jobs/any-job/status")]
    [InlineData("POST", "/api/admin/jobs/any-job/trigger")]
    [InlineData("GET", "/api/admin/jobs/any-job/history")]
    [InlineData("POST", "/api/admin/jobs/any-job/enable")]
    [InlineData("POST", "/api/admin/jobs/any-job/disable")]
    public async Task Unauthenticated_Returns401_OnAllEndpoints(string method, string path)
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"AC-2.5 — unauthenticated callers MUST receive 401 on {method} {path}");
    }

    // ================================================================================
    // ===== Helpers ==================================================================
    // ================================================================================

    private void SeedCompletedRun(string jobId, DateTimeOffset startedAt, bool success, string? errorMessage = null)
    {
        _fixture.Store.SeedRunRecord(new InMemoryBackgroundJobStore.RunRecord(
            RunId: Guid.NewGuid(),
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

    /// <summary>Test-only <see cref="IScheduledJob"/> — returns success instantly.</summary>
    private sealed class IntegrationFakeJob : IScheduledJob
    {
        public IntegrationFakeJob(string jobId, string displayName, string description)
        {
            JobId = jobId;
            DisplayName = displayName;
            Description = description;
        }

        public string JobId { get; }
        public string DisplayName { get; }
        public string Description { get; }

        public Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new JobRunResult(Success: true, ErrorMessage: null, ProcessedItems: 0, Duration: TimeSpan.Zero));
    }
}
