using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Authorization contract tests for the Office endpoints (Task 073).
/// Proves the endpoint filters wired in <c>OfficeEndpoints.cs</c> actually gate the
/// endpoints: <c>OfficeAuthFilter</c> (baseline authentication) rejects unauthenticated
/// callers with 401, and <c>JobOwnershipFilter</c> (job-scoped) rejects a caller who does
/// not own the job with 403 while admitting the owner with 200.
/// </summary>
public class OfficeEndpointAuthorizationContractTests : IClassFixture<OfficeTestWebAppFactory>
{
    private readonly OfficeTestWebAppFactory _factory;

    public OfficeEndpointAuthorizationContractTests(OfficeTestWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_OfficeRecent_WhenUnauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/office/recent");
        request.Headers.Add("X-Test-Unauthenticated", "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_OfficeSave_WhenUnauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/office/save")
        {
            Content = JsonContent.Create(new SaveRequest
            {
                ContentType = SaveContentType.Email,
                Email = new EmailMetadata { Subject = "Test", SenderEmail = "sender@test.com" }
            })
        };
        request.Headers.Add("X-Test-Unauthenticated", "true");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_OfficeJobStatus_WhenCallerIsNotOwner_Returns403()
    {
        var jobId = Guid.NewGuid();
        using var factory = new OfficeJobOwnershipTestWebAppFactory();

        // JobOwnershipFilter resolves the job via the (jobId, ct) overload to check ownership.
        // Owner differs from the TestAuthHandler caller ("test-user-oid") -> must be rejected.
        factory.OfficeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJob(jobId, createdBy: "a-different-owner-oid"));

        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/office/jobs/{jobId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_OfficeJobStatus_WhenCallerIsOwner_Returns200()
    {
        var jobId = Guid.NewGuid();
        using var factory = new OfficeJobOwnershipTestWebAppFactory();

        // Ownership-check overload: owner matches the caller "test-user-oid".
        factory.OfficeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJob(jobId, createdBy: "test-user-oid"));

        // Handler overload: returns the job to the authorized caller.
        factory.OfficeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJob(jobId, createdBy: "test-user-oid"));

        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/office/jobs/{jobId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------------------------------------------------------------------------------------
    // spaarkeai-word-add-in-r1 task 067 (finding F5) — the fail-OPEN.
    //
    // JobOwnershipFilter guarded with `!string.IsNullOrEmpty(jobStatus.CreatedBy) && <mismatch>`, so a job
    // that records NO owner satisfied the guard and was served to anyone. That is not a hypothetical state:
    // it is exactly what the Dataverse fallback produced, because no creator was ever persisted and the
    // fallback mapper set no CreatedBy. Once the in-memory entry was evicted — a restart, or simply a second
    // instance — every job in the system was readable by any authenticated caller.
    //
    // ADR-017: "MUST NOT expose status without authorization checks." A check that skips itself when the
    // data it checks is absent does not satisfy that; it reads as protection while providing none.
    // These two cases are the whole point of the task, so they are stated as their own criteria.
    // ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]      // Dataverse fallback: creator never persisted, never mapped.
    [InlineData("")]        // Defensive: an empty string must not read as "no owner recorded, so allow".
    [InlineData("   ")]     // Whitespace must not slip past a bare IsNullOrEmpty guard either.
    public async Task Get_OfficeJobStatus_WhenJobRecordsNoOwner_IsRefused(string? unownedValue)
    {
        var jobId = Guid.NewGuid();
        using var factory = new OfficeJobOwnershipTestWebAppFactory();

        // The filter resolves the job through the (jobId, ct) overload — the one that deliberately carries
        // no caller, so the filter sees the raw record. With no recorded owner there is nothing to compare
        // the caller against, so the ONLY safe answer is refusal.
        factory.OfficeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJob(jobId, createdBy: unownedValue));

        // Deliberately also satisfy the handler overload. If the filter were to admit the caller, the
        // request would succeed end to end — which is what makes the fail-open exploitable rather than
        // merely untidy, and what makes a 200 here a genuine RED rather than an incidental one.
        factory.OfficeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJob(jobId, createdBy: unownedValue));

        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/office/jobs/{jobId}");

        response.StatusCode.Should().NotBe(
            HttpStatusCode.OK,
            "a job whose owner was never recorded must not be disclosed to an arbitrary authenticated caller (ADR-017)");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The retired production test-job backdoor (task 067). <c>OfficeService.GetJobStatusAsync</c> used to
    /// answer the hard-coded id <c>00000000-0000-0000-0000-000000000001</c> with a fabricated
    /// <c>Running</c> status — in production, to any authenticated caller, stamping
    /// <c>CreatedBy = userId</c> so the caller always "owned" it. This exercises the REAL
    /// <see cref="IOfficeService"/> (the base factory does not mock it), so it fails if the backdoor
    /// is ever reintroduced.
    /// </summary>
    [Fact]
    public async Task Get_OfficeJobStatus_ForRetiredTestJobId_IsNotServedFabricatedStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/office/jobs/00000000-0000-0000-0000-000000000001");

        response.StatusCode.Should().NotBe(
            HttpStatusCode.OK,
            "the hard-coded test job must not serve fabricated status from a production code path");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static JobStatusResponse BuildJob(Guid jobId, string? createdBy) => new()
    {
        JobId = jobId,
        Status = JobStatus.Running,
        JobType = JobType.EmailSave,
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedBy = createdBy
    };
}

/// <summary>
/// Office test host that replaces <see cref="IOfficeService"/> with a mock so job-ownership
/// authorization (<c>JobOwnershipFilter</c>) can be exercised deterministically without a
/// real job store. Inherits all configuration (test auth, in-memory cache, disabled rate
/// limiting) from <see cref="OfficeTestWebAppFactory"/>.
/// </summary>
public class OfficeJobOwnershipTestWebAppFactory : OfficeTestWebAppFactory
{
    public Mock<IOfficeService> OfficeServiceMock { get; } = new(MockBehavior.Loose);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOfficeService>();
            services.AddScoped<IOfficeService>(_ => OfficeServiceMock.Object);
        });
    }
}
