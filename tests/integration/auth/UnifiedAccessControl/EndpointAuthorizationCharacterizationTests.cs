using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sprk.Bff.Api.Tests.Integration.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Endpoint-level characterization + negative suite for the Phase 0 access-control routes, run
/// in-process against the real Minimal API pipeline via <see cref="WorkspaceTestFixture"/>.
///
/// Pins the ROUTE-level half of four confirmed findings (spec NFR-07):
///   A-1 — <c>GET /api/documents/{id}/download</c> carries no per-document authorization filter;
///         the group's bare <c>RequireAuthorization()</c> is the only gate, and the handler then
///         streams app-only (FileAccessEndpoints.cs:101-109, :865). This is R1's January 2026
///         attack scenario, still open.
///   A-3 — the sibling <c>eml-render</c> route DOES attach a filter, but with operation <c>"read"</c>,
///         which is absent from OperationAccessPolicy — so it denies unconditionally instead.
///         Together A-1 and A-3 mean the two routes fail in OPPOSITE directions.
///   A-4 — <c>GET /api/documents/{id}/permissions</c> reports app-scoped capabilities.
///   A-6 — grant / revoke / close-project sit behind bare <c>RequireAuthorization()</c>, so any
///         authenticated caller can mint external grants.
///   A-7 — <c>PATCH /api/v1/external/todos/{id}</c> has no record-scope check.
///
/// The 401 assertions are negatives that must ALREADY hold — they pin the authentication floor that
/// every Phase 0 task must preserve while adding authorization above it. Authentication (are you
/// anyone?) and authorization (may you touch THIS record?) are different gates; these findings are
/// all about the second one being absent, so proving the first still works is what makes the
/// characterizations meaningful rather than ambiguous.
/// </summary>
public class EndpointAuthorizationCharacterizationTests
    : IClassFixture<WorkspaceTestFixture>, IClassFixture<ExternalCollaborationTestFixture>
{
    private readonly WorkspaceTestFixture _fixture;

    /// <summary>
    /// Separate host for the <c>/api/v1/external</c> group — its dual-scheme policy bypasses the
    /// default fake scheme, so it needs the policy override in
    /// <see cref="ExternalCollaborationTestFixture"/>. See that type for why.
    /// </summary>
    private readonly ExternalCollaborationTestFixture _externalFixture;

    public EndpointAuthorizationCharacterizationTests(
        WorkspaceTestFixture fixture,
        ExternalCollaborationTestFixture externalFixture)
    {
        _fixture = fixture;
        _externalFixture = externalFixture;
    }

    private static readonly Guid DocumentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TodoId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid ProjectId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ContactId = Guid.Parse("88888888-8888-8888-8888-888888888888");

    // ─────────────────────────────────────────────────────────────────────────────
    // NEGATIVE — authentication floor. Must already hold; every Phase 0 task preserves these.
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/documents/55555555-5555-5555-5555-555555555555/download")]
    [InlineData("/api/documents/55555555-5555-5555-5555-555555555555/eml-render")]
    [InlineData("/api/documents/55555555-5555-5555-5555-555555555555/permissions")]
    public async Task Get_WhenUnauthenticated_Returns401(string route)
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostGrant_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/external-access/grant", new
        {
            contactId = ContactId,
            projectId = ProjectId,
            accessLevel = 1
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRevoke_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/external-access/revoke", new
        {
            accessRecordId = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostCloseProject_WhenUnauthenticated_Returns401()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/v1/external-access/close-project", new
        {
            projectId = ProjectId
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PatchExternalTodo_WhenUnauthenticated_Returns401()
    {
        using var client = _externalFixture.CreateUnauthenticatedClient();

        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", new
        {
            subject = "renamed by an unauthenticated caller"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARACTERIZATION — A-1 / A-3. Flipped by tasks 002 (FR-01) and 003 (FR-03).
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A-1 — CURRENT (BROKEN) BEHAVIOR. An authenticated caller with no established access to the
    /// document is NOT rejected with 403 by the download route: no per-document authorization filter
    /// is attached, so the request proceeds past authorization into handler execution (where it fails
    /// for unrelated reasons — the document does not exist in the test host). The load-bearing
    /// assertion is the ABSENCE of 403, i.e. authorization never ran.
    ///
    /// FLIPPED BY: task 002 (FR-01) — the route MUST then reject a caller without document access,
    /// and this assertion inverts to Be(HttpStatusCode.Forbidden).
    /// </summary>
    [Fact]
    public async Task Characterization_GetDownload_ForCallerWithoutDocumentAccess_IsNotRejectedByAuthorization()
    {
        // Arrange — authenticated, but holds no grant/share/ownership on this document.
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync($"/api/documents/{DocumentId}/download");

        // Assert — CURRENT behavior: authorization does not reject; the request reaches the handler,
        // which then fails only because the test host has no such document (observed: 409 Conflict).
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "A-1 pins the CURRENT broken state: FileAccessEndpoints.cs:101-109 attaches no " +
            "per-document authorization filter, so no authorization decision is made for this caller " +
            "and the handler streams app-only (:865). Task 002 attaches the filter and flips this to Forbidden.");
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "authentication succeeded — this test isolates the missing AUTHORIZATION gate");

        // Guard against a VACUOUS pass: a 5xx would satisfy both assertions above without the request
        // ever having been authorized-and-handled. Requiring a sub-500 status proves the pipeline ran
        // to the handler, which is the actual content of finding A-1.
        ((int)response.StatusCode).Should().BeLessThan(500,
            "the request must genuinely reach the handler for this characterization to mean anything");
    }

    /// <summary>
    /// ✅ FLIPPED BY TASK 003 (FR-03) — was the A-3 characterization.
    ///
    /// The <c>eml-render</c> route attaches <c>AddDocumentAuthorizationFilter("read")</c>
    /// (FileAccessEndpoints.cs:117-118). Before task 003, <c>"read"</c> was not a policy key, so
    /// <c>OperationAccessRule</c> denied with <c>unknown_operation</c> for every caller regardless of
    /// access — an unconditional 403 that was indistinguishable from a real denial.
    ///
    /// It now makes a genuine rights decision. This caller holds no access to the document, so the
    /// answer is still 403 — but the reason code proves WHY, which is the whole content of the flip.
    /// Asserting the reason (not just the status) is what stops this test from passing for the old,
    /// broken reason.
    /// </summary>
    [Fact]
    public async Task GetEmlRender_ForCallerWithoutDocumentAccess_DeniedForInsufficientRights()
    {
        // Arrange
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync($"/api/documents/{DocumentId}/eml-render");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("sdap.access.deny.insufficient_rights",
            "task 003 registered \"read\", so the denial must now be a RIGHTS decision");
        body.Should().NotContain("unknown_operation",
            "an unknown_operation denial here would mean the \"read\" key regressed out of " +
            "OperationAccessPolicy — findings A-3/A-20 re-opened");
    }

    /// <summary>
    /// A-1 — the contrast that makes the remaining hole unmistakable. Two routes on the SAME document,
    /// reached by the SAME caller in the SAME request context, still disagree: <c>eml-render</c>
    /// evaluates authorization and denies, while <c>download</c> never evaluates it at all and
    /// proceeds to stream app-only.
    ///
    /// Task 003 fixed the eml-render half (the denial is now a real rights decision rather than
    /// <c>unknown_operation</c>). The disagreement that remains is purely A-1: download has no filter.
    ///
    /// FLIPPED BY: task 002 (FR-01) — afterwards both routes MUST reach the same decision for the same
    /// caller on the same document.
    /// </summary>
    [Fact]
    public async Task Characterization_DownloadAndEmlRender_DisagreeOnAuthorizationForSameCallerAndDocument()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var download = await client.GetAsync($"/api/documents/{DocumentId}/download");
        var emlRender = await client.GetAsync($"/api/documents/{DocumentId}/eml-render");

        // eml-render now denies on rights (correct); download is never authorized at all (A-1).
        emlRender.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        download.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "A-1: FileAccessEndpoints.cs:101-109 attaches no per-document filter, so this route reaches " +
            "the handler for a caller the sibling route just denied. Task 002 closes this.");
        ((int)download.StatusCode).Should().BeLessThan(500,
            "guard against a vacuous pass — the download request must genuinely reach the handler");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // CHARACTERIZATION — A-4 / A-6 / A-7. Flipped by tasks 006, 008, 009.
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A-4 — CURRENT (BROKEN) BEHAVIOR, and the sharpest demonstration in this file: an authenticated
    /// caller with NO access to the document receives <c>200 OK</c> with a capabilities payload.
    /// <c>PermissionsEndpoints.cs:76</c> passes <c>userAccessToken: null</c>, so what comes back
    /// describes what the APPLICATION can do, not what this caller can do — and it is returned to
    /// anyone who can authenticate.
    ///
    /// FLIPPED BY: task 006 (FR-05) — capabilities MUST become caller-scoped, so this caller is either
    /// rejected or told it has none.
    /// </summary>
    [Fact]
    public async Task Characterization_GetPermissions_ForCallerWithoutDocumentAccess_Returns200WithCapabilities()
    {
        // Arrange — authenticated, no grant/share/ownership on this document.
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.GetAsync($"/api/documents/{DocumentId}/permissions");

        // Assert — CURRENT behavior: a successful capabilities response for a caller with no access.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "A-4 pins the CURRENT broken state: the endpoint has no per-document authorization gate " +
            "and reports app-scoped capabilities (PermissionsEndpoints.cs:76 passes userAccessToken: " +
            "null), so any authenticated caller learns any document's capabilities. Task 006 makes " +
            "this caller-scoped and flips this away from 200.");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace(
            "the 200 carries a capabilities payload — that payload is the disclosure");
    }

    /// <summary>
    /// A-6 — CURRENT (BROKEN) BEHAVIOR. Grant / revoke / close-project sit behind bare
    /// <c>RequireAuthorization()</c> (ExternalAccessEndpoints.cs:109-111) with no delegation check, so
    /// authentication alone admits the caller to the external-grant write surface. A read-only user is
    /// not distinguished from someone entitled to grant access.
    ///
    /// FLIPPED BY: task 008 (FR-07) — the delegation rule ("you may grant if you have Write on the
    /// target record", decision B-14) MUST reject a caller lacking Write. This is the gate that has to
    /// land BEFORE the PCF "+ User" button (task 065), or that button is one-click privilege
    /// escalation on a confidential matter.
    /// </summary>
    [Theory]
    [InlineData("/api/v1/external-access/grant")]
    [InlineData("/api/v1/external-access/revoke")]
    [InlineData("/api/v1/external-access/close-project")]
    public async Task Characterization_ExternalAccessAdminRoutes_AdmitAnyAuthenticatedCaller(string route)
    {
        // Arrange — authenticated, but with no demonstrated Write on any target record.
        using var client = _fixture.CreateAuthenticatedClient();

        // Act
        var response = await client.PostAsJsonAsync(route, new
        {
            contactId = ContactId,
            projectId = ProjectId,
            accessRecordId = Guid.NewGuid(),
            accessLevel = 1
        });

        // Assert — CURRENT behavior: neither authentication nor authorization rejects this caller.
        // (Downstream the handler fails on the unavailable test-host Dataverse, which is irrelevant
        // here — the finding is that the caller was admitted to the grant-write surface at all.)
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "A-6 pins the CURRENT broken state: {0} is behind bare RequireAuthorization() with no " +
            "delegation check. Task 008 adds the Write-on-target rule and flips this to Forbidden.", route);
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// A-6 — the non-vacuous half of the above. <c>/grant</c> answers <c>400</c> for a malformed body,
    /// which can only happen INSIDE the handler: the caller passed every gate in front of it. So the
    /// grant-write surface is genuinely reachable by an arbitrary authenticated caller, not merely
    /// "not visibly rejected".
    ///
    /// This is the finding that gates task 065: until task 008 lands the delegation rule, the PCF
    /// "+ User" button would be a one-click path to Full Access on a confidential matter.
    ///
    /// FLIPPED BY: task 008 (FR-07) — authorization MUST reject before body validation is reached.
    /// </summary>
    [Fact]
    public async Task Characterization_PostGrant_ReachesHandlerValidationForArbitraryAuthenticatedCaller()
    {
        // Arrange — authenticated caller with no entitlement to grant anything.
        using var client = _fixture.CreateAuthenticatedClient();

        // Act — deliberately incomplete body (no usable grant root).
        var response = await client.PostAsJsonAsync("/api/v1/external-access/grant", new
        {
            contactId = ContactId,
            accessLevel = 1
        });

        // Assert — 400 proves handler entry: authorization admitted the caller, and only the payload
        // stopped the grant.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "A-6 pins the CURRENT broken state: the caller reached the handler's own validation, so " +
            "nothing in front of it asked whether this caller may grant access. Task 008 adds that " +
            "check, and the caller is then rejected before validation runs.");
    }

    /// <summary>
    /// NEGATIVE — the group-level principal gate fails CLOSED for a caller who cannot be resolved to
    /// an external <c>CallerPrincipal</c> (<c>CallerPrincipalAuthorizationFilter</c>). Task 009 adds a
    /// record-scope check INSIDE the handler; this pins the gate in front of it, which task 009 must
    /// not weaken while doing so.
    ///
    /// Scope note: A-7's actual defect — <c>UpdateTodo</c> performing no record-scope check for a
    /// caller who IS resolvable (ExternalProjectDataEndpoints.cs:328-345) — is NOT asserted here and
    /// cannot be asserted offline: reaching the handler requires real Dataverse participation data to
    /// resolve the principal. Recorded in notes/task-001-untestable-findings.md and owned by task 009,
    /// which must add its own coverage against a resolvable principal.
    /// </summary>
    [Fact]
    public async Task PatchExternalTodo_WhenCallerCannotBeResolvedToExternalPrincipal_IsRejected()
    {
        // Arrange — authenticated, but no external participation record backs this identity.
        using var client = _externalFixture.CreateAuthenticatedClient();

        // Act
        var response = await client.PatchAsJsonAsync($"/api/v1/external/todos/{TodoId}", new
        {
            subject = "renamed by a caller with no resolvable external principal"
        });

        // Assert — fail-closed: the request must NOT succeed.
        response.IsSuccessStatusCode.Should().BeFalse(
            "an unresolvable external caller must never reach a write handler");
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }
}
