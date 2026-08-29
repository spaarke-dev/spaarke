using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// A test double for <see cref="IAccessDataSource"/> that answers PER CALLER, mirroring what the real
/// OBO path does (Dataverse answers as the impersonated user).
///
/// This double is the load-bearing part of this suite. With the REAL data source, the offline test host
/// fails closed to <see cref="AccessRights.None"/> for every request — so "all capabilities false" is
/// true both before and after the task-006 fix, and an endpoint test asserting only that would pass
/// VACUOUSLY. Substituting a source that CAN answer <c>true</c> for the right caller is what makes the
/// negative case meaningful.
/// </summary>
public sealed class CallerScopedAccessDataSource : IAccessDataSource
{
    /// <summary>Every call received, so tests can assert on the caller-scoping arguments themselves.</summary>
    public List<(string UserId, string ResourceId, string? UserAccessToken)> Calls { get; } = new();

    /// <summary>Rights granted to the matching caller on the accessible document.</summary>
    public const AccessRights GrantedRights = AccessRights.Read | AccessRights.Write;

    /// <summary>
    /// Every right the model can express, AppendTo included. Task 005 (FR-04) lifted the
    /// <see cref="AccessRights.Read"/> ceiling in <c>DataverseAccessDataSource</c>, so a snapshot like
    /// this is now producible in production; before it, only Read ever reached a consumer.
    /// </summary>
    public const AccessRights AllRights =
        AccessRights.Read | AccessRights.Write | AccessRights.Delete | AccessRights.Create
        | AccessRights.Append | AccessRights.AppendTo | AccessRights.Share;

    /// <summary>
    /// Rights this caller holds per resource. A map rather than a single mutable field so tests never
    /// depend on execution order — the class fixture is shared across the whole test class.
    /// </summary>
    private static readonly Dictionary<string, AccessRights> ResourceRights = new(StringComparer.OrdinalIgnoreCase)
    {
        [CallerScopedAccessTestFixture.AccessibleDocumentId] = GrantedRights,
        [CallerScopedAccessTestFixture.FullRightsDocumentId] = AllRights
        // InaccessibleDocumentId is deliberately absent → AccessRights.None.
    };

    public Task<AccessSnapshot> GetUserAccessAsync(
        string userId,
        string resourceId,
        string? userAccessToken = null,
        CancellationToken ct = default)
    {
        lock (Calls)
        {
            Calls.Add((userId, resourceId, userAccessToken));
        }

        // Simulates a failure to resolve rights, so the endpoint's fail-closed path is exercised
        // end-to-end rather than assumed (task 005 acceptance criterion 4).
        if (string.Equals(resourceId, CallerScopedAccessTestFixture.ResolutionErrorDocumentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("simulated Dataverse rights-resolution failure");
        }

        // Access requires BOTH the caller's own token AND a resource they hold rights on. Keying on the
        // token is what makes this caller-scoped; keying on the resource is what lets one caller have
        // access to one document and not another.
        var callerMatches =
            string.Equals(userAccessToken, WorkspaceTestConstants.TestBearerToken, StringComparison.Ordinal);

        var rights = callerMatches && ResourceRights.TryGetValue(resourceId, out var granted)
            ? granted
            : AccessRights.None;

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = rights
        });
    }

    /// <summary>Calls received by <see cref="GetRecordAccessAsync"/> — kept separate from
    /// <see cref="Calls"/> because this method carries an extra dimension (<c>entitySetName</c>) the
    /// document-scoped method doesn't have. No test currently exercises this path; it exists so a
    /// future entity-scoped permissions test can observe it rather than have the recorder silently
    /// drop the call. Locked the same way as <see cref="Calls"/>, for the same reason (the endpoint
    /// under test appends from request threads).</summary>
    public List<(string UserId, string EntitySetName, Guid RecordId, string? UserAccessToken)> RecordCalls { get; } = new();

    /// <summary>
    /// Mirrors <see cref="GetUserAccessAsync"/> for the entity-agnostic path (unified-access-control-r2
    /// task 070): same caller-token match against <see cref="WorkspaceTestConstants.TestBearerToken"/>,
    /// same <see cref="ResourceRights"/> lookup (keyed on <paramref name="recordId"/>'s string form — the
    /// seeded document ids are guid-shaped strings, so this reaches the same entries), and the same
    /// <see cref="CallerScopedAccessTestFixture.ResolutionErrorDocumentId"/> sentinel.
    /// </summary>
    public Task<AccessSnapshot> GetRecordAccessAsync(
        string userId,
        string entitySetName,
        Guid recordId,
        string? userAccessToken,
        CancellationToken ct = default)
    {
        lock (RecordCalls)
        {
            RecordCalls.Add((userId, entitySetName, recordId, userAccessToken));
        }

        var resourceId = recordId.ToString();

        if (string.Equals(resourceId, CallerScopedAccessTestFixture.ResolutionErrorDocumentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("simulated Dataverse rights-resolution failure");
        }

        var callerMatches =
            string.Equals(userAccessToken, WorkspaceTestConstants.TestBearerToken, StringComparison.Ordinal);

        var rights = callerMatches && ResourceRights.TryGetValue(resourceId, out var granted)
            ? granted
            : AccessRights.None;

        return Task.FromResult(new AccessSnapshot
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = rights
        });
    }

    /// <summary>Calls recorded for one resource, so assertions do not depend on test execution order.</summary>
    public IReadOnlyList<(string UserId, string ResourceId, string? UserAccessToken)> CallsFor(string resourceId)
    {
        lock (Calls)
        {
            return Calls
                .Where(c => string.Equals(c.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>
    /// A locked snapshot of every recorded call. Assertions MUST go through this rather than enumerating
    /// <see cref="Calls"/> directly: the endpoint under test appends from request threads, and enumerating
    /// a <see cref="List{T}"/> while another thread appends throws
    /// <see cref="InvalidOperationException"/> — a flake that would surface as an unrelated failure.
    /// </summary>
    public IReadOnlyList<(string UserId, string ResourceId, string? UserAccessToken)> Snapshot()
    {
        lock (Calls)
        {
            return Calls.ToList();
        }
    }
}

/// <summary>
/// Test host with <see cref="IAccessDataSource"/> replaced by <see cref="CallerScopedAccessDataSource"/>.
/// <see cref="IAccessDataSource"/> is the module boundary ADR-003 names as a seam and ADR-038 §4 names as
/// the correct substitution point — no transport-level mocking (ADR-038 §7 ban B1).
///
/// Replacing the registration also removes the <c>CachedAccessDataSource</c> decorator from the chain,
/// which is deliberate: a 60s-TTL cache between the assertion and the double would make these tests
/// order-dependent.
/// </summary>
public sealed class CallerScopedAccessTestFixture : WorkspaceTestFixture
{
    /// <summary>The one document the test caller has been granted Read+Write on.</summary>
    public const string AccessibleDocumentId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    /// <summary>A document the test caller has NO access to.</summary>
    public const string InaccessibleDocumentId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    /// <summary>A document the test caller holds every right on (task 005 / FR-04).</summary>
    public const string FullRightsDocumentId = "cccccccc-cccc-cccc-cccc-cccccccccccc";

    /// <summary>A document whose rights resolution throws, exercising the fail-closed path.</summary>
    public const string ResolutionErrorDocumentId = "dddddddd-dddd-dddd-dddd-dddddddddddd";

    /// <summary>Another principal's oid, used to prove a body-supplied identity is not honoured.</summary>
    public const string OtherUserId = "victim-user-00000000-0000-0000-0000-000000000009";

    public CallerScopedAccessDataSource AccessDataSource { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton<IAccessDataSource>(AccessDataSource);
        });
    }
}

/// <summary>
/// Caller-scoping suite for <c>PermissionsEndpoints</c> — finding A-4, spec FR-05, closed by task 006.
///
/// A-4: <c>PermissionsEndpoints.cs:76</c> and <c>:159</c> called <see cref="IAccessDataSource"/> directly
/// with <c>userAccessToken: null</c>, so the capabilities described what the APPLICATION could do and
/// were handed to any caller who could authenticate. Task 004 could not reach this path — it bypassed
/// <c>AuthorizationService</c> entirely — which is why FR-02's acceptance criterion needed this task too.
///
/// The endpoint now resolves rights through <c>AuthorizationService.GetCallerAccessAsync</c>, the same
/// snapshot accessor <c>AuthorizeAsync</c> uses for enforcement.
/// </summary>
public class PermissionsEndpointCallerScopedTests : IClassFixture<CallerScopedAccessTestFixture>
{
    private readonly CallerScopedAccessTestFixture _fixture;

    public PermissionsEndpointCallerScopedTests(CallerScopedAccessTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string Route(string documentId) => $"/api/documents/{documentId}/permissions";

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — authentication floor. Must already hold; task 006 preserves it.
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPermissions_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync(Route(CallerScopedAccessTestFixture.AccessibleDocumentId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetBatchPermissions_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/documents/permissions/batch", new
        {
            documentIds = new[] { CallerScopedAccessTestFixture.AccessibleDocumentId }
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // A-4 — FR-05 acceptance. Flipped by task 006.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ FR-05 ACCEPTANCE, verbatim: "a user without access receives <c>CanPreview=false</c>".
    ///
    /// Non-vacuous because <see cref="GetPermissions_ForCallerWithAccess_ReturnsCapabilitiesMatchingTheirRights"/>
    /// proves the SAME fixture and the SAME caller do get <c>true</c> on a document they hold rights on.
    /// Without that pair this assertion would also pass against the old app-scoped code, since the
    /// offline host fails closed to None either way.
    /// </summary>
    [Fact]
    public async Task GetPermissions_ForCallerWithoutAccessToDocument_ReturnsEveryCapabilityFalse()
    {
        // Arrange — authenticated, but holds nothing on THIS document.
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync(Route(CallerScopedAccessTestFixture.InaccessibleDocumentId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CapabilitiesResponse>(Json);
        body.Should().NotBeNull();

        body!.CanPreview.Should().BeFalse("FR-05 acceptance: a user without access receives CanPreview=false");
        body.AccessRights.Should().Be("None");

        // Every other capability must be false too — a partial fix that left one affordance enabled
        // would still be a disclosure.
        body.AllCapabilities().Should().OnlyContain(c => c.Value == false,
            "no capability may be reported for a document the caller cannot access");
    }

    /// <summary>
    /// The positive half, and what makes the negative meaningful: the same caller on a document they DO
    /// hold rights on gets capabilities matching those rights exactly — not blanket true.
    ///
    /// The double grants Read|Write, so: preview (Read) and download (Write — the deliberate Spaarke
    /// policy that download needs Write) are allowed, while delete (Delete) and share (Share) are not.
    /// That triple is what proves the mapping is still rights-based rather than waved through.
    /// </summary>
    [Fact]
    public async Task GetPermissions_ForCallerWithAccess_ReturnsCapabilitiesMatchingTheirRights()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(Route(CallerScopedAccessTestFixture.AccessibleDocumentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CapabilitiesResponse>(Json);
        body.Should().NotBeNull();

        body!.CanPreview.Should().BeTrue("driveitem.preview requires Read, which this caller holds");
        body.CanDownload.Should().BeTrue("driveitem.content.download requires Write, which this caller holds");
        body.CanReadMetadata.Should().BeTrue();
        body.CanUpdateMetadata.Should().BeTrue();

        body.CanDelete.Should().BeFalse("driveitem.delete requires Delete, which this caller does NOT hold");
        body.CanShare.Should().BeFalse("driveitem.createlink requires Share, which this caller does NOT hold");
        body.CanUpload.Should().BeFalse("driveitem.content.upload requires Write|Create; Create is absent");

        body.AccessRights.Should().Be("Read, Write");
    }

    /// <summary>
    /// The A-4 flip stated at the level the finding was written at: the CALLER's token now reaches the
    /// data source. Before task 006 the recorded value here was always <c>null</c> — that null WAS the
    /// finding, because it selected app-only evaluation, and on the SPA/Teams surface app-only always
    /// answers yes.
    ///
    /// Asserting the argument rather than the status code is deliberate: the status code cannot
    /// distinguish an app-scoped answer from a caller-scoped one when both happen to be all-false.
    /// </summary>
    [Fact]
    public async Task GetPermissions_ForAuthenticatedCaller_ForwardsTheirTokenToTheDataSource()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        await client.GetAsync(Route(CallerScopedAccessTestFixture.AccessibleDocumentId));

        var calls = _fixture.AccessDataSource.CallsFor(CallerScopedAccessTestFixture.AccessibleDocumentId);
        calls.Should().NotBeEmpty("the endpoint must consult the access data source");

        calls.Should().OnlyContain(c => c.UserAccessToken == WorkspaceTestConstants.TestBearerToken,
            "A-4 is closed: the endpoint forwards the caller's bearer token, so DataverseAccessDataSource " +
            "takes its OBO path and answers 'what may THIS CALLER do'. A null here is the app-only " +
            "evaluation the finding describes.");

        calls.Should().OnlyContain(c => c.UserId == WorkspaceTestConstants.TestUserId,
            "the identity asked about must be the authenticated caller");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // A-20 Read-ceiling — task 005 (FR-04). This is where the ceiling is OBSERVABLE.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ✅ TASK 005 (FR-04) — the Read ceiling's observable outcome.
    ///
    /// <c>DataverseAccessDataSource.QueryUserPermissionsAsync</c> used to return a hard-coded
    /// <see cref="AccessRights.Read"/> on success, so a snapshot carrying Write/Create/Delete/Share was
    /// not producible in production and the eleven Write+ capabilities were false for every caller
    /// however privileged. It now calls <c>RetrievePrincipalAccess</c> and maps the full flag set.
    ///
    /// This test asserts the consequence at the surface a user actually sees. It is the endpoint half of
    /// task 006's binding constraint: "verify those flags actually light up — a Read-ceiling fix that
    /// does not surface in the capabilities response means the snapshot widened somewhere the endpoint
    /// does not read."
    /// </summary>
    [Fact]
    public async Task GetPermissions_ForCallerWithEveryRight_ReportsEveryCapabilityTrue()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(Route(CallerScopedAccessTestFixture.FullRightsDocumentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CapabilitiesResponse>(Json);
        body.Should().NotBeNull();

        body!.AllCapabilities().Should().OnlyContain(c => c.Value == true,
            "with every Dataverse right held, every capability must be reported — the eleven Write+ " +
            "flags (CanDownload, CanUpload, CanReplace, CanDelete, CanUpdateMetadata, CanShare, " +
            "CanRestoreVersion, CanMove, CanCopy, CanCheckOut, CanCheckIn) were unreachable under the " +
            "A-20 Read ceiling that task 005 removed");
    }

    /// <summary>
    /// Task 005 acceptance criterion 5: the reported capabilities never EXCEED the rights the snapshot
    /// carries. Lifting a ceiling is only a fix if nothing downstream amplifies — a change that widened
    /// rights AND loosened the projection would pass a "capabilities light up" test while over-granting.
    ///
    /// The Read|Write caller is the discriminating case: Write-requiring capabilities are true, while
    /// Delete-, Share- and Create-requiring ones stay false.
    /// </summary>
    [Fact]
    public async Task GetPermissions_ForPartialRights_ReportsOnlyCapabilitiesThoseRightsSatisfy()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(Route(CallerScopedAccessTestFixture.AccessibleDocumentId));
        var body = await response.Content.ReadFromJsonAsync<CapabilitiesResponse>(Json);

        body.Should().NotBeNull();

        // Satisfied by Read|Write.
        body!.CanPreview.Should().BeTrue();
        body.CanDownload.Should().BeTrue();
        body.CanReplace.Should().BeTrue();
        body.CanCheckOut.Should().BeTrue();
        body.CanRestoreVersion.Should().BeTrue();

        // NOT satisfied — each needs a right this caller does not hold.
        body.CanDelete.Should().BeFalse("driveitem.delete requires Delete");
        body.CanShare.Should().BeFalse("driveitem.createlink requires Share");
        body.CanUpload.Should().BeFalse("driveitem.content.upload requires Write|Create; Create is absent");
        body.CanCopy.Should().BeFalse("driveitem.copy requires Read|Create; Create is absent");
        body.CanMove.Should().BeFalse("driveitem.move requires Write|Delete; Delete is absent");
    }

    /// <summary>
    /// Task 005 acceptance criterion 4: an error while resolving rights denies — it must never fall back
    /// to default or app-scoped rights. Exercised end-to-end through the endpoint rather than asserted
    /// of the catch block by inspection.
    /// </summary>
    [Fact]
    public async Task GetPermissions_WhenRightsResolutionThrows_ReportsNoCapabilities()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(Route(CallerScopedAccessTestFixture.ResolutionErrorDocumentId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CapabilitiesResponse>(Json);
        body.Should().NotBeNull();
        body!.AllCapabilities().Should().OnlyContain(c => c.Value == false,
            "a rights-resolution failure must yield no capabilities — fail closed, never a default grant");
        body.AccessRights.Should().Be("None (Error)",
            "the error is surfaced distinctly from a genuine no-rights answer");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // BATCH — same guarantee, plus the body-supplied-identity spoof.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The batch endpoint exists so a gallery can avoid N+1 calls; it must not become a way to learn
    /// about documents one by one. Each entry is scoped independently to the caller, in one response.
    /// </summary>
    [Fact]
    public async Task GetBatchPermissions_ForMixedDocuments_ScopesEachEntryToTheCaller()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/documents/permissions/batch", new
        {
            documentIds = new[]
            {
                CallerScopedAccessTestFixture.AccessibleDocumentId,
                CallerScopedAccessTestFixture.InaccessibleDocumentId
            }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<BatchResponse>(Json);
        body.Should().NotBeNull();
        body!.Permissions.Should().HaveCount(2);
        body.Errors.Should().BeEmpty();

        var accessible = body.Permissions.Single(p =>
            string.Equals(p.DocumentId, CallerScopedAccessTestFixture.AccessibleDocumentId, StringComparison.OrdinalIgnoreCase));
        var inaccessible = body.Permissions.Single(p =>
            string.Equals(p.DocumentId, CallerScopedAccessTestFixture.InaccessibleDocumentId, StringComparison.OrdinalIgnoreCase));

        accessible.CanPreview.Should().BeTrue();
        inaccessible.CanPreview.Should().BeFalse();
        inaccessible.AllCapabilities().Should().OnlyContain(c => c.Value == false);
    }

    /// <summary>
    /// The second disclosure task 006 found and closed (not part of A-4's original wording): the batch
    /// handler used to accept a <c>UserId</c> in the request BODY and prefer it over the caller's claims,
    /// so a caller could ask about another principal's capabilities.
    ///
    /// This was not cosmetic. <c>DataverseAccessDataSource.cs:184-199</c> treats <c>userId</c> and
    /// <c>userAccessToken</c> as INDEPENDENT inputs — the id selects whose Dataverse principal is
    /// queried, the token only selects the auth mode. So a body-supplied id would have run the query as
    /// the caller (OBO) while asking about someone else, and task 014's cache key
    /// <c>sdap:auth:access:obo:{userId}:{resourceId}</c> would have been written under the VICTIM's oid.
    ///
    /// The member is now absent from the request DTO. Sending it is inert rather than honoured, which is
    /// asserted here at the boundary that matters — what identity reached the data source.
    /// </summary>
    [Fact]
    public async Task GetBatchPermissions_WhenBodySuppliesAnotherUserId_IgnoresItAndScopesToTheCaller()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/documents/permissions/batch", new
        {
            documentIds = new[] { CallerScopedAccessTestFixture.InaccessibleDocumentId },
            userId = CallerScopedAccessTestFixture.OtherUserId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<BatchResponse>(Json);
        body.Should().NotBeNull();
        body!.Permissions.Should().ContainSingle();
        body.Permissions[0].UserId.Should().Be(WorkspaceTestConstants.TestUserId,
            "capabilities are reported for the AUTHENTICATED caller, never for a body-supplied identity");

        _fixture.AccessDataSource.Snapshot().Should().NotContain(
            c => c.UserId == CallerScopedAccessTestFixture.OtherUserId,
            "the spoofed identity must never reach the access data source — reaching it would both " +
            "answer about the wrong principal and poison that principal's auth cache entry");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Response shapes — only the members these assertions need.
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed record CapabilitiesResponse
    {
        public string DocumentId { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
        public bool CanPreview { get; init; }
        public bool CanDownload { get; init; }
        public bool CanUpload { get; init; }
        public bool CanReplace { get; init; }
        public bool CanDelete { get; init; }
        public bool CanReadMetadata { get; init; }
        public bool CanUpdateMetadata { get; init; }
        public bool CanShare { get; init; }
        public bool CanViewVersions { get; init; }
        public bool CanRestoreVersion { get; init; }
        public bool CanMove { get; init; }
        public bool CanCopy { get; init; }
        public bool CanCheckOut { get; init; }
        public bool CanCheckIn { get; init; }
        public string AccessRights { get; init; } = string.Empty;

        /// <summary>
        /// Every capability flag by name, so "all false" is asserted over the WHOLE surface rather than
        /// over whichever handful a test happened to list.
        /// </summary>
        public IReadOnlyDictionary<string, bool> AllCapabilities() => new Dictionary<string, bool>
        {
            [nameof(CanPreview)] = CanPreview,
            [nameof(CanDownload)] = CanDownload,
            [nameof(CanUpload)] = CanUpload,
            [nameof(CanReplace)] = CanReplace,
            [nameof(CanDelete)] = CanDelete,
            [nameof(CanReadMetadata)] = CanReadMetadata,
            [nameof(CanUpdateMetadata)] = CanUpdateMetadata,
            [nameof(CanShare)] = CanShare,
            [nameof(CanViewVersions)] = CanViewVersions,
            [nameof(CanRestoreVersion)] = CanRestoreVersion,
            [nameof(CanMove)] = CanMove,
            [nameof(CanCopy)] = CanCopy,
            [nameof(CanCheckOut)] = CanCheckOut,
            [nameof(CanCheckIn)] = CanCheckIn
        };
    }

    private sealed record BatchResponse
    {
        public List<CapabilitiesResponse> Permissions { get; init; } = new();
        public List<JsonElement> Errors { get; init; } = new();
        public int TotalProcessed { get; init; }
        public int SuccessCount { get; init; }
        public int ErrorCount { get; init; }
    }
}
