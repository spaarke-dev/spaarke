using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Admin.Models;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Admin;

/// <summary>
/// Task 031: the Signal re-evaluation job is registered through <c>AddScheduledJob</c> (ADR-036) by the real host and is
/// visible on <c>GET /api/admin/jobs</c> with its nightly schedule.
/// </summary>
public sealed class SignalReevaluationJobRegistrationTests : IClassFixture<AdminJobsTestFixture>
{
    private readonly AdminJobsTestFixture _fixture;

    public SignalReevaluationJobRegistrationTests(AdminJobsTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ListJobs_IncludesTheSignalReevaluationJob_WithItsNightlySchedule()
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.GetAsync("/api/admin/jobs");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var summaries = await response.Content.ReadFromJsonAsync<List<JobStatusSummary>>();
        var job = summaries!.Should().ContainSingle(s => s.JobId == SignalReevaluationJob.JobIdConstant).Subject;
        job.CronSchedule.Should().Be("30 2 * * *");
        job.Enabled.Should().BeTrue();
    }
}
