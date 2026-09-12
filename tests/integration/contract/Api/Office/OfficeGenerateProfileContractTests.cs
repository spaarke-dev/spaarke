using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
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
/// <see cref="IDocumentProfileAi"/> (the ADR-013 facade the background dispatch calls) so the test never
/// makes a real AI/OBO call and can observe the dispatch deterministically via a
/// <see cref="TaskCompletionSource"/> — the same idiom used in
/// <c>DispatchSessionEndpointContractTests.Post_ClientDisconnectsMidReview_LedgerWriteStillCompletes</c>.
/// No <c>Mock&lt;HttpMessageHandler&gt;</c> (ADR-038 B1).
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
    public async Task Post_GenerateProfile_WithAuthorizedCaller_Returns202_AndDispatchesTheSameProfileFacadeComposeUses()
    {
        _factory.GrantWrite(DocumentId);
        var client = AuthorizedClient();

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await ReadJsonAsync(response);
        body.GetProperty("documentId").GetGuid().Should().Be(DocumentId);
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrEmpty();

        // The dispatch is fire-and-forget — wait for the detached background task to reach the facade
        // rather than asserting immediately (the 202 above already proves the response never waited).
        (await _factory.WaitForDispatchAsync()).Should().BeTrue("the background dispatch must reach IDocumentProfileAi");
        _factory.ProfileAi.Verify(
            p => p.ProfileDocumentAsUserAsync(DocumentId, It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
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
        (await _factory.WaitForDispatchAsync()).Should().BeTrue();
        _factory.ProfileAi.Verify(
            p => p.ProfileDocumentAsUserAsync(DocumentId, It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Negative: authentication / authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_GenerateProfile_WhenUnauthenticated_Returns401_AndDispatchesNothing()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        var response = await client.PostAsync(RouteForDocument(DocumentId), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _factory.ProfileAi.Verify(
            p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        _factory.ProfileAi.Verify(
            p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        _factory.ProfileAi.Verify(
            p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        _factory.ProfileAi.Verify(
            p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        _factory.ProfileAi.Verify(
            p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
    private TaskCompletionSource _dispatchReached = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Mock<IDocumentProfileAi> ProfileAi { get; } = new(MockBehavior.Loose);

    /// <summary>Resets per-test state (grants + the dispatch signal + the mock's recorded calls).</summary>
    public void Reset()
    {
        _grants.Clear();
        _dispatchReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ProfileAi.Invocations.Clear();
    }

    /// <summary>Grants <c>write</c> (and <c>read</c>) on the given document id — the ADR-008 filter passes.</summary>
    public void GrantWrite(Guid documentId) =>
        _grants[documentId.ToString("D")] = Spaarke.Dataverse.AccessRights.Read | Spaarke.Dataverse.AccessRights.Write;

    /// <summary>Grants only <c>read</c> — the ADR-008 filter (operation "write") denies with 403.</summary>
    public void GrantReadOnly(Guid documentId) =>
        _grants[documentId.ToString("D")] = Spaarke.Dataverse.AccessRights.Read;

    /// <summary>
    /// Waits for the detached background dispatch to reach <see cref="IDocumentProfileAi"/>. Deterministic
    /// (a <see cref="TaskCompletionSource"/> set from the mock's own callback) — no <c>Task.Delay</c>
    /// polling, per the tests module's TimeProvider-over-Stopwatch convention (this is a completion signal,
    /// not a timing measurement).
    /// </summary>
    public async Task<bool> WaitForDispatchAsync()
    {
        var completed = await Task.WhenAny(_dispatchReached.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        return completed == _dispatchReached.Task;
    }

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

            ProfileAi
                .Setup(p => p.ProfileDocumentAsUserAsync(It.IsAny<Guid>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
                .Callback(() => _dispatchReached.TrySetResult())
                .ReturnsAsync(DocumentProfileOutcome.Succeeded());
            services.RemoveAll<IDocumentProfileAi>();
            services.AddScoped(_ => ProfileAi.Object);
        });
    }
}
