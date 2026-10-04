using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// HTTP contract for <c>POST /api/office/documents/{documentId}/generate-profile</c>
/// (spaarkeai-word-add-in-r1 task 022, FR-08): the "Generate Profile" trigger.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deviation from the POML's default test-file location (authorized by the dispatch brief):</b> this
/// project also runs task 024 (client version save) in parallel, which owns
/// <c>tests/integration/contract/Api/Office/OfficeEndpointsContractTests.cs</c>. Per the authorized
/// deviation, this task's contract tests live in their OWN file instead of appending to that one — this
/// file only. It REUSES (does not modify) the shared fixtures declared there:
/// <see cref="OfficeTestWebAppFactory"/> and <see cref="TestAuthHandler"/>.
/// </para>
/// <para>
/// <b>Fixture layering.</b> <see cref="OfficeGenerateProfileTestWebAppFactory"/> adds exactly two
/// module-boundary doubles on top of the shared base fixture: <see cref="IAccessDataSource"/> (the
/// <c>DocumentAuthorizationFilter</c>'s data seam — same seam <c>OfficeVersionSaveTestWebAppFactory</c>
/// uses) so the resource-authorization decision is controllable per test, and
/// <see cref="JobSubmissionService"/> (the job queue the request goes to since task 068, #1086), recording each
/// queued job. The job is queued before the 202, so the tests assert at once. <see cref="IDocumentProfileAi"/> stays
/// registered: its presence is what says profiling is available. No <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1).
/// </para>
/// </remarks>
public class OfficeGenerateProfileContractTests : IClassFixture<OfficeGenerateProfileTestWebAppFactory>
{
    private static readonly Guid DocumentId = Guid.Parse("3f6d9a2c-1b4e-4c7a-9d5f-8a2b1c3d4e5f");
    private const string RouteFor = "/api/office/documents/{0}/generate-profile";

    private readonly OfficeGenerateProfileTestWebAppFactory _factory;

    public OfficeGenerateProfileContractTests(OfficeGenerateProfileTestWebAppFactory factory)
    {
        _factory = factory;
        _factory.Reset();
    }

    private static string RouteForDocument(Guid id) => string.Format(RouteFor, id);

    private HttpClient AuthorizedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-caller-token");
        return client;
    }

    // ── Happy path ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_GenerateProfile_WithAuthorizedCaller_Returns202_AndQueuesOneProfileJobForTheDocument()
    {
        _factory.GrantWrite(DocumentId);
        var client = AuthorizedClient();

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await ReadJsonAsync(response);
        body.GetProperty("documentId").GetGuid().Should().Be(DocumentId);
        var correlationId = body.GetProperty("correlationId").GetString();
        correlationId.Should().NotBeNullOrEmpty();

        // Task 068 (#1086): the request is on the job queue before the 202, so a restart cannot lose it. The 202 names
        // the job, and its Location is the document read, where this job type records its status (ADR-017).
        var job = _factory.QueuedJobs.Should().ContainSingle().Subject;
        body.GetProperty("jobId").GetGuid().Should().Be(job.JobId);
        response.Headers.Location!.ToString().Should().Be($"/api/v1/documents/{DocumentId}");
        job.JobType.Should().Be(AppOnlyDocumentAnalysisJobHandler.JobTypeName);
        job.SubjectId.Should().Be(DocumentId.ToString());
        job.CorrelationId.Should().Be(correlationId);
        job.IdempotencyKey.Should().Be(AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(DocumentId, job.JobId));
        _factory.ProfileAi.Verify(
            p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the profile runs on the job queue, not in this process");
    }

    [Fact]
    public async Task Post_GenerateProfile_OnACompletedProfile_StillDispatches_NoConfirmationRequired()
    {
        // Spec Assumptions: Generate Profile OVERWRITES an existing (even Completed) profile with no
        // confirmation prompt — there is no "are you sure" step to simulate; a single POST must suffice.
        _factory.GrantWrite(DocumentId);
        var client = AuthorizedClient();

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.QueuedJobs.Should().ContainSingle(job => job.SubjectId == DocumentId.ToString(),
            "the request's own key means an already-profiled document is profiled again");
    }

    // ── Negative: authentication / authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_GenerateProfile_WhenUnauthenticated_Returns401_AndDispatchesNothing()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.QueuedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_GenerateProfile_WithNoCallerBearerToken_Returns403ViaTheEndpointFilter_AndDispatchesNothing()
    {
        // Authenticated (claims present, per TestAuthHandler) but no Authorization header — the ADR-008
        // DocumentAuthorizationFilter's AuthorizationService fails closed (no caller-scoped token), NEVER
        // from inline handler logic.
        _factory.GrantWrite(DocumentId);
        var client = _factory.CreateClient(); // no bearer token

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("reasonCode").WhoseValue.Should().Be("sdap.access.deny.no_caller_token");
        _factory.QueuedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_GenerateProfile_WhenCallerLacksWriteOnTheDocument_Returns403_AndLeaksNoMetadata()
    {
        // Access data source explicitly denies (Read only, not Write) for this specific document — the
        // ADR-008 filter, not the handler, produces the 403.
        _factory.GrantReadOnly(DocumentId);
        var client = AuthorizedClient();

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain(DocumentId.ToString(), "a denial must not echo the resource id back to an unauthorized caller");
        _factory.QueuedJobs.Should().BeEmpty();
    }

    // ── Negative: validation ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_GenerateProfile_WithEmptyGuidDocumentId_Returns400ProblemDetails_AndDispatchesNothing()
    {
        // Guid.Empty is a syntactically valid GUID (matches the {documentId:guid} route constraint) but
        // never a real record — the one "malformed" shape reachable over HTTP without 404ing at routing.
        // No grant is arranged: the validation filter runs BEFORE the authorization filter, so this
        // request never reaches — and never needs — an access decision.
        var client = AuthorizedClient();

        var response = await client.PostAsync(RouteForDocument(Guid.Empty), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_PROFILE_001");
        _factory.QueuedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Post_GenerateProfile_WithNonGuidRouteSegment_Returns404_NotAnUnhandledError()
    {
        // A string that fails the :guid route constraint never reaches routing at all — the same shape
        // Compose's own refresh-profile route accepts (both routes use the {…:guid} constraint). This is
        // a defined, non-crashing response (404), not the literal 400 the empty-guid case above produces;
        // recorded as a deliberate mirror of the reference implementation in the task's final report.
        var client = AuthorizedClient();

        var response = await client.PostAsync("/api/office/documents/not-a-guid/generate-profile", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.QueuedJobs.Should().BeEmpty();
    }

    // ── Negative: feature-gated dependency unavailable (coordinator-review fix) ────────────────────────────

    /// <summary>
    /// Coordinator-review fix: the first draft of this trigger ALWAYS returned 202, even when
    /// <c>IDocumentProfileAi</c> was unavailable (the compound AI gate off) — the pane would show
    /// "Pending" for a job that would never run. Uses its OWN local <see cref="WebApplicationFactory{T}"/>
    /// (not the class-shared <see cref="_factory"/>) — a per-test factory, the SAME idiom
    /// <c>OfficeQuickCreateContractTests</c> uses for its own module-boundary doubles — because this is
    /// the one scenario in this file that needs a DIFFERENT DI shape (the facade genuinely UNREGISTERED,
    /// not a mock standing in for it) than every other test here. Configured entirely in this file — the
    /// shared <c>OfficeTestWebAppFactory</c> in <c>OfficeEndpointsContractTests.cs</c> is untouched.
    /// </summary>
    [Fact]
    public async Task Post_GenerateProfile_WhenTheAiFacadeIsUnavailable_Returns503_NeverA202()
    {
        using var factory = new OfficeGenerateProfileFacadeUnavailableTestWebAppFactory();
        factory.GrantWrite(DocumentId);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-caller-token");

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_PROFILE_002");
        problem.Should().ContainKey("correlationId").WhoseValue.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Task 068 (#1086): a request the job queue refuses was not accepted, so it is a retryable 503 with the route's own
    /// error code, never a 202 and never the global handler's anonymous 500.
    /// </summary>
    [Fact]
    public async Task Post_GenerateProfile_WhenTheJobQueueRefuses_Returns503Retryable_NeverA202()
    {
        _factory.GrantWrite(DocumentId);
        _factory.RefuseNextSubmit();
        var client = AuthorizedClient();

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_PROFILE_004");
        var body = await ReadJsonAsync(response);
        body.GetProperty("retryable").GetBoolean().Should().BeTrue();
        _factory.QueuedJobs.Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>The top-level string members of a ProblemDetails body (extensions are flattened to the root).</summary>
    private static async Task<Dictionary<string, string?>> ReadProblemAsync(HttpResponseMessage response)
    {
        var root = await ReadJsonAsync(response);
        return root.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString());
    }
}

/// <summary>
/// <see cref="OfficeTestWebAppFactory"/> (declared in <c>OfficeEndpointsContractTests.cs</c>, owned by task
/// 024, reused verbatim) with the <see cref="IAccessDataSource"/> and <see cref="IDocumentProfileAi"/>
/// module boundaries doubled locally for the Generate Profile contract — configured entirely in THIS file,
/// per the task's boundary ("configure your test class locally", do not modify shared fixture files).
/// </summary>
public sealed class OfficeGenerateProfileTestWebAppFactory : OfficeTestWebAppFactory
{
    private readonly Dictionary<string, Spaarke.Dataverse.AccessRights> _grants = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<JobContract> _queuedJobs = new();
    private bool _refuseNextSubmit;

    public Mock<IDocumentProfileAi> ProfileAi { get; } = new(MockBehavior.Loose);

    /// <summary>The jobs the route put on the job queue, in order.</summary>
    public IReadOnlyList<JobContract> QueuedJobs => _queuedJobs;

    /// <summary>Resets per-test state (grants, the queued jobs, the refusal, the facade's recorded calls).</summary>
    public void Reset()
    {
        _grants.Clear();
        _queuedJobs.Clear();
        _refuseNextSubmit = false;
        ProfileAi.Invocations.Clear();
    }

    /// <summary>The next submit fails as a busy Service Bus namespace does.</summary>
    public void RefuseNextSubmit() => _refuseNextSubmit = true;

    /// <summary>Grants <c>write</c> (and <c>read</c>) on the given document id — the ADR-008 filter passes.</summary>
    public void GrantWrite(Guid documentId) =>
        _grants[documentId.ToString("D")] = Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write;

    /// <summary>Grants only <c>read</c> — the ADR-008 filter (operation "write") denies with 403.</summary>
    public void GrantReadOnly(Guid documentId) =>
        _grants[documentId.ToString("D")] = Spaarke.Dataverse.AccessRights.Read;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var access = new Mock<IAccessDataSource>();
            access
                .Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string userId, string resourceId, string? _, CancellationToken _) =>
                    new AccessSnapshot
                    {
                        UserId = userId,
                        ResourceId = resourceId,
                        AccessRights = _grants.TryGetValue(resourceId, out var rights) ? rights : Spaarke.Dataverse.AccessRights.None,
                    });
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton(access.Object);

            // Registered, so profiling is available; never called (the profile runs on the job queue).
            services.RemoveAll<IDocumentProfileAi>();
            services.AddScoped(_ => ProfileAi.Object);

            var queue = new Mock<JobSubmissionService>(
                MockBehavior.Loose,
                Options.Create(new ServiceBusOptions { QueueName = "sdap-jobs" }),
                Mock.Of<ILogger<JobSubmissionService>>(),
                new Mock<ServiceBusClient>().Object);
            queue
                .Setup(q => q.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                .Returns((JobContract job, CancellationToken _) =>
                {
                    if (_refuseNextSubmit)
                    {
                        _refuseNextSubmit = false;
                        throw new ServiceBusException("The namespace is busy.", ServiceBusFailureReason.ServiceBusy);
                    }

                    lock (_queuedJobs)
                    {
                        _queuedJobs.Add(job);
                    }

                    return Task.CompletedTask;
                });
            services.RemoveAll<JobSubmissionService>();
            services.AddSingleton(queue.Object);
        });
    }
}

/// <summary>
/// Coordinator-review fix fixture: a host where <see cref="IDocumentProfileAi"/> is REMOVED and never
/// re-registered — simulating the compound AI gate being off. <c>OfficeProfileQueue</c>'s
/// <c>profiling</c> constructor parameter is optional-nullable, so DI resolves it to <see langword="null"/>
/// rather than failing to construct the host, which is exactly the production condition
/// <see cref="GenerateProfileDispatchOutcome.FacadeUnavailable"/> exists to answer honestly instead of with a false 202.
/// </summary>
public sealed class OfficeGenerateProfileFacadeUnavailableTestWebAppFactory : OfficeTestWebAppFactory
{
    private readonly Dictionary<string, Spaarke.Dataverse.AccessRights> _grants = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Grants <c>write</c> (and <c>read</c>) on the given document id — the ADR-008 filter passes,
    /// so the request reaches the handler and this fixture's facade-unavailable condition is what actually
    /// produces the 503 (not an incidental 403).</summary>
    public void GrantWrite(Guid documentId) =>
        _grants[documentId.ToString("D")] = Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var access = new Mock<IAccessDataSource>();
            access
                .Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string userId, string resourceId, string? _, CancellationToken _) =>
                    new AccessSnapshot
                    {
                        UserId = userId,
                        ResourceId = resourceId,
                        AccessRights = _grants.TryGetValue(resourceId, out var rights) ? rights : Spaarke.Dataverse.AccessRights.None,
                    });
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton(access.Object);

            // The defect under test: remove IDocumentProfileAi and DO NOT re-register anything in its
            // place. No Mock<IDocumentProfileAi> substitute here — an actual absence, not a stand-in.
            services.RemoveAll<IDocumentProfileAi>();
        });
    }
}
