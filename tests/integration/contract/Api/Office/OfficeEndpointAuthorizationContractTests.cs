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

    /// <summary>
    /// Task 120 (GitHub #1015) — an UNKNOWN owner is a denial, not an exemption.
    ///
    /// <para>The ownership condition used to be
    /// <c>!string.IsNullOrEmpty(jobStatus.CreatedBy) &amp;&amp; !string.Equals(...)</c>, which skipped the
    /// comparison entirely when <c>CreatedBy</c> was null or empty — so a job whose owner could not be
    /// determined was readable by ANY authenticated caller, from a route that reads
    /// <c>.AddJobOwnershipFilter()</c> at the call site. Job status carries document filenames, progress
    /// and failure detail.</para>
    ///
    /// <para>These two cases are the regression guard: revert the filter to the short-circuit form and
    /// both go green-to-red. The owner-match and wrong-owner cases above are unaffected, which is what
    /// makes this a fix rather than a behaviour change.</para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Get_OfficeJobStatus_WhenOwnerIsUnknown_Returns403(string? createdBy)
    {
        var jobId = Guid.NewGuid();
        using var factory = new OfficeJobOwnershipTestWebAppFactory();

        factory.OfficeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJob(jobId, createdBy: createdBy!));

        // Deliberately ALSO wired for the handler overload. If the filter were to let the request
        // through, the request would reach the handler and return 200 — so a 403 here proves the filter
        // denied, rather than the handler happening to fail for some unrelated reason.
        factory.OfficeServiceMock
            .Setup(s => s.GetJobStatusAsync(jobId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildJob(jobId, createdBy: createdBy!));

        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/office/jobs/{jobId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static JobStatusResponse BuildJob(Guid jobId, string createdBy) => new()
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
