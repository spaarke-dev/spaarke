// Task 030 — CIAM external-access surface integration/contract tests (KEEP path: contract + security-auth).
//
// Scope (per POML 030 §goal + spec FR-08 / NFR-03 + project CLAUDE.md §"Broker-only" + "Authz-before-stream"):
//   - The download authz-before-stream property (NFR-03, the single highest-consequence property):
//     an unauthorized caller receives 403 with NO bytes and NO SPE-pointer resolution / Graph read.
//   - The CIAM external group's auth gate (401 when unauthenticated — CiamExternal policy).
//   - The admin invite-and-grant composition: provisioner idempotency (oid-present ⇒ no second CIAM
//     account, FR-08) and the broker-only grant (writes sprk_externalrecordaccess + invalidates the
//     participation cache, NO synthetic SPE permission — task 026 / ADR-028 Amendment A1).
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Categories: endpoint-contract (route + status + payload shape) and security-auth (authz gate,
//     authz-before-side-effect). Physically at tests/integration/contract/**; the Sprk.Bff.Api.Tests
//     csproj auto-includes ../../integration/contract/**/*.cs so this compiles into that assembly.
//
// Placement note (CLAUDE.md §6.5 — Path C, ADR-compliant placement): POML 030 <outputs> names
//   tests/unit/Sprk.Bff.Api.Tests/. That path predates the ADR-038 KEEP-path reorg; these are
//   integration tests through the HTTP surface, so they live at the canonical contract KEEP path
//   (mirrors ComposeEndpointsContractTests). The wrap-up test-diet references them there.
//
// Banned-pattern compliance (ADR-038 §4 / tests/CLAUDE.md 17 bans):
//   - B1: NO Mock<HttpMessageHandler>. The Dataverse-backed services (ExternalParticipationService,
//     ExternalDataService, DataverseWebApiClient) are raw-HttpClient designs; mocking them at the
//     HttpClient/handler level is banned. Instead we mock at the MODULE BOUNDARY via the (additive,
//     backward-compatible) `virtual` seams added in this task + the existing ISpeFileOperations /
//     IDocumentStorageResolver / ITenantCache interfaces.
//   - B3/B4: NO DI-registration or ctor-null tests. Every test asserts HTTP-observable behavior.
//   - B5: in-process collaborators are NOT mocked except at the sanctioned module boundaries above.
//   - B13: names are {Method}_{Scenario}_{ExpectedResult}. B6/B7/B9/B10: behavior-shaped assertions.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// Integration-contract + security-auth tests for the CIAM external-access surface (Phase-2 tasks
/// 020–027). Boots the BFF in-process via <see cref="WebApplicationFactory{TEntryPoint}"/>, replaces
/// the Dataverse-backed services at their module boundaries (the <c>virtual</c> seams + existing
/// interfaces), and asserts the observable HTTP contract — with the download authz-before-stream
/// negative case (NFR-03) as the centerpiece.
/// </summary>
public sealed class ExternalAccessContractTests : IClassFixture<ExternalAccessContractFixture>
{
    private static readonly Guid ProjectA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DocumentX = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly ExternalAccessContractFixture _fixture;

    public ExternalAccessContractTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ================================================================================
    // ===== (1) CIAM external group auth gate ========================================
    // ================================================================================

    [Theory]
    [InlineData("/api/v1/external/me")]
    [InlineData("/api/v1/external/projects/11111111-1111-1111-1111-111111111111/documents/33333333-3333-3333-3333-333333333333/content")]
    public async Task ExternalGroup_WhenUnauthenticated_Returns401(string path)
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the /api/v1/external group is pinned to the CiamExternal policy (RequireAuthenticatedUser); " +
            "a call with no token must be rejected with 401 before any handler or filter runs");
    }

    // ================================================================================
    // ===== (2) Download authz-before-stream — NEGATIVE (THE centerpiece, NFR-03) =====
    // ================================================================================

    [Fact]
    public async Task DownloadDocument_WhenCallerLacksProjectAccess_Returns403_AndNeverResolvesPointersOrReadsContent()
    {
        // Caller is authenticated but has NO participation for ProjectA.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>());

        var response = await client.GetAsync(
            $"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/content");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "project access is checked BEFORE any storage work; a caller with no participation gets 403");

        // Authz-before-stream: neither the SPE pointer resolver NOR the SPE content read may be touched.
        _fixture.StorageResolverMock.Verify(
            r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "an unauthorized caller must never trigger SPE pointer resolution (no Graph-adjacent read)");
        _fixture.SpeFileOperationsMock.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "an unauthorized caller must never trigger the app-only SPE content download (no bytes)");
    }

    // ================================================================================
    // ===== (3) Download document→project scoping — NEGATIVE ==========================
    // ================================================================================

    [Fact]
    public async Task DownloadDocument_WhenDocumentNotInRequestedProject_Returns403_AndNeverResolvesPointers()
    {
        // Caller HAS access to ProjectA, but the document belongs to ProjectB (scoping mismatch).
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            documentProjectId: ProjectB);

        var response = await client.GetAsync(
            $"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/content");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "document→project scoping is enforced before streaming; a doc outside the requested project is 403");

        _fixture.StorageResolverMock.Verify(
            r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "scoping denial must also happen before any SPE pointer resolution");
    }

    // ================================================================================
    // ===== (4) Download authz-before-stream — POSITIVE ==============================
    // ================================================================================

    [Fact]
    public async Task DownloadDocument_WhenAuthorizedAndDocumentInProject_Returns200_AndStreamsBytes()
    {
        var payload = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // "%PDF"
        _fixture.StorageResolverMock
            .Setup(r => r.GetSpePointersAsync(DocumentX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("drive-ext-1", "item-ext-1"));
        _fixture.SpeFileOperationsMock
            .Setup(s => s.DownloadFileAsync("drive-ext-1", "item-ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(payload));

        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            documentProjectId: ProjectA);

        var response = await client.GetAsync(
            $"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/content");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsByteArrayAsync();
        body.Should().Equal(payload, "an authorized, in-project download streams the app-only SPE bytes");
    }

    [Fact]
    public async Task DocumentVersions_OfAMovedFile_ReportTheOriginalDates_AndNoAuthor_ToAnExternalParticipant()
    {
        // unified-access-control-r2 task 166 f1-v2, owner round 45 item 1: a relocation replays a file's history; Graph
        // dates each replayed version at the move. The relocation's record keeps the ORIGINAL date, and the history an
        // external participant sees is unchanged by the move — which includes never being shown who wrote a version.
        var original = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero);
        var replayedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        _fixture.StorageResolverMock
            .Setup(r => r.GetSpePointersAsync(DocumentX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("b!drive-ext-2", "item-ext-2"));
        _fixture.SpeFileOperationsMock
            .Setup(s => s.ListFileVersionsAsync("b!drive-ext-2", "item-ext-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<VersionInfoDto>
            {
                new("2.0", null, replayedAt.AddMinutes(1), 200, "SharePoint App"),
                new("1.0", null, replayedAt, 100, "SharePoint App"),
            });
        _fixture.DataverseServiceMock
            .Setup(d => d.RetrieveAsync("sprk_document", DocumentX, It.Is<string[]>(c => c.Contains(RelocatedVersionHistory.Column)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Microsoft.Xrm.Sdk.Entity("sprk_document", DocumentX)
            {
                [RelocatedVersionHistory.Column] =
                    $$"""{"v":1,"item":"item-ext-2","versions":[{"id":"1.0","by":"Alice Original","at":"{{original:O}}","size":100}]}""",
            });

        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA }, documentProjectId: ProjectA);

        var response = await client.GetAsync($"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "body was: {0}", await response.Content.ReadAsStringAsync());
        var body = System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var versions = body["versions"]!.AsArray();
        var replayed = versions.Single(v => v!["versionId"]!.GetValue<string>() == "1.0")!;
        DateTimeOffset.Parse(replayed["createdAt"]!.GetValue<string>()).Should().Be(original, "the replayed version's ORIGINAL date");
        versions.Should().OnlyContain(v => v!["createdByName"] == null, "no author is shown on the external surface");
    }

    // ================================================================================
    // ===== (4b) Task 136 · defect C2 — read gates test RIGHTS, not key presence ======
    // ================================================================================
    //
    // The world: ProjectA is Secure and the caller's only grant on it is organization-inherited (no direct
    // level). FR-22 makes that grant worth nothing. Before task 136 the record still sat in the caller's map
    // as a None-rights key, and every project read route below asked "is the id present?" — so the caller
    // listed ProjectA's documents and streamed their content app-only. Each route must now 403 BEFORE any data
    // read, on BOTH planes (the routes are plane-agnostic; the principal is built by a different strategy).

    private static readonly string[] Planes = { "ciam", "workforce" };

    private static readonly string[] ProjectReadRoutes =
    {
        $"/api/v1/external/projects/{ProjectA}",
        $"/api/v1/external/projects/{ProjectA}/documents",
        $"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/content",
        $"/api/v1/external/projects/{ProjectA}/documents/{DocumentX}/versions",
        $"/api/v1/external/projects/{ProjectA}/events",
        $"/api/v1/external/projects/{ProjectA}/contacts",
        $"/api/v1/external/projects/{ProjectA}/organizations",
    };

    public static TheoryData<string, string> ProjectReadRoutesOnBothPlanes()
    {
        var data = new TheoryData<string, string>();
        foreach (var plane in Planes)
            foreach (var route in ProjectReadRoutes)
                data.Add(plane, route);
        return data;
    }

    public static TheoryData<string> BothPlanes() => new() { "ciam", "workforce" };

    [Theory]
    [MemberData(nameof(ProjectReadRoutesOnBothPlanes))]
    public async Task ProjectReadRoute_SecureProjectReachedOnlyThroughAnOrganizationGrant_Returns403BeforeAnyRead(
        string plane, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            documentProjectId: ProjectA,
            workforce: plane == "workforce",
            secureProjects: new[] { ProjectA }); // no direct level ⇒ organization-inherited only

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{plane}: an organization grant confers nothing on a Secure project (FR-22), so the caller holds no " +
            "Read on it and the read must be denied — presence of the id is not authorization (C2)");
        _fixture.DataReads.Should().BeEmpty("the gate runs BEFORE the app-only data read, never after it");
        _fixture.StorageResolverMock.Verify(
            r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never(),
            "a denied caller must never trigger SPE pointer resolution");
        _fixture.SpeFileOperationsMock.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never(),
            "a denied caller must never trigger the app-only content download (no bytes)");
        _fixture.SpeFileOperationsMock.Verify(
            s => s.ListFileVersionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never(),
            "a denied caller must never trigger the app-only version read");
    }

    public static TheoryData<string> ProjectReadRouteData()
    {
        var data = new TheoryData<string>();
        foreach (var route in ProjectReadRoutes)
            data.Add(route);
        return data;
    }

    [Theory]
    [MemberData(nameof(ProjectReadRouteData))]
    public async Task ProjectReadRoute_PrincipalHoldingTheProjectAtNoneRights_Returns403BeforeAnyRead(string route)
    {
        // The route's OWN Read test, isolated: the evaluator and both strategies prune None-rights entries, so this
        // principal is built directly (PowerlessProjectCiamStrategy). A route that went back to asking "is the id
        // present?" would admit it here and nowhere else.
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>(), documentProjectId: ProjectA);
        client.DefaultRequestHeaders.Add("X-Test-PowerlessProject", ProjectA.ToString());

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the read routes test Read on the record, not presence of its id in the caller's map");
        _fixture.DataReads.Should().BeEmpty("the gate runs BEFORE the app-only data read");
        _fixture.StorageResolverMock.Verify(
            r => r.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task ProjectListAndMe_PrincipalHoldingTheProjectAtNoneRights_ListNothing()
    {
        using var client = _fixture.CreateAuthenticatedClient(accessibleProjects: Array.Empty<Guid>());
        client.DefaultRequestHeaders.Add("X-Test-PowerlessProject", ProjectA.ToString());

        var list = await client.GetAsync("/api/v1/external/projects");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("value").GetArrayLength().Should().Be(0, "nothing readable, nothing listed");
        }

        _fixture.DataReads.Should().BeEmpty("no id is sent to the data read when nothing is readable");

        var me = await client.GetAsync("/api/v1/external/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("projects").GetArrayLength().Should().Be(0,
                "/me does not disclose the GUID of a project the caller holds nothing on");
        }
    }

    [Theory]
    [MemberData(nameof(ProjectReadRoutesOnBothPlanes))]
    public async Task ProjectReadRoute_DirectViewOnlyGrantOnTheSecureProject_IsAdmitted(string plane, string route)
    {
        _fixture.StorageResolverMock
            .Setup(r => r.GetSpePointersAsync(DocumentX, It.IsAny<CancellationToken>()))
            .ReturnsAsync(("drive-ext-1", "item-ext-1"));
        _fixture.SpeFileOperationsMock
            .Setup(s => s.DownloadFileAsync("drive-ext-1", "item-ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(new byte[] { 0x25, 0x50 }));
        _fixture.SpeFileOperationsMock
            .Setup(s => s.ListFileVersionsAsync("drive-ext-1", "item-ext-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Sprk.Bff.Api.Models.VersionInfoDto> { new("1.0", null, DateTimeOffset.UnixEpoch, 2) });

        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            documentProjectId: ProjectA,
            accessLevel: ExternalAccessLevel.ViewOnly,
            workforce: plane == "workforce",
            secureProjects: new[] { ProjectA },
            directAccessLevel: ExternalAccessLevel.ViewOnly);

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"{plane}: a named DIRECT ViewOnly grant is what Secure keeps for a contact, and it carries Read");
    }

    public static TheoryData<string, string> MutatingRoutesOnBothPlanes()
    {
        var data = new TheoryData<string, string>();
        foreach (var plane in Planes)
            foreach (var route in new[] { "todos", "events", "documents" })
                data.Add(plane, route);
        return data;
    }

    [Theory]
    [MemberData(nameof(MutatingRoutesOnBothPlanes))]
    public async Task MutatingRoute_DirectViewOnlyGrantOnTheSecureProject_Returns403InsufficientRights(
        string plane, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA },
            accessLevel: ExternalAccessLevel.ViewOnly,
            workforce: plane == "workforce",
            secureProjects: new[] { ProjectA },
            directAccessLevel: ExternalAccessLevel.ViewOnly);

        var path = $"/api/v1/external/projects/{ProjectA}/{route}";
        HttpResponseMessage response;
        if (route == "documents")
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "upload.bin");
            response = await client.PostAsync(path, form);
        }
        else
        {
            response = await client.PostAsJsonAsync(path, new { sprk_name = "x" });
        }

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{plane}: Read admits the read routes, but ViewOnly carries no Create");
        (await ReasonCode(response)).Should().Be("sdap.access.deny.insufficient_rights",
            "the refusal is the Create gate, not the read gate — the caller DOES hold Read");
        _fixture.SpeFileOperationsMock.Verify(
            s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<Sprk.Bff.Api.Models.ConflictBehavior>(), It.IsAny<CancellationToken>()), Times.Never(),
            "nothing is written for a caller without Create");
    }

    [Theory]
    [MemberData(nameof(BothPlanes))]
    public async Task ProjectListAndMe_SecureProjectReachedOnlyThroughAnOrganizationGrant_AreNotListed(string plane)
    {
        using var client = _fixture.CreateAuthenticatedClient(
            accessibleProjects: new[] { ProjectA, ProjectB },
            workforce: plane == "workforce",
            secureProjects: new[] { ProjectA }); // A: org-inherited only on a Secure root; B: open

        var list = await client.GetAsync("/api/v1/external/projects");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("value").EnumerateArray()
                .Select(p => p.GetProperty("sprk_projectid").GetString())
                .Should().Equal(new[] { ProjectB.ToString() },
                    $"{plane}: GET /projects lists only Read-bearing projects — never one the caller holds nothing on");
        }

        _fixture.DataReads.Should().Equal(new[] { $"GetProjectsAsync:{ProjectB}" },
            "the Secure project's id is not even sent to the data read");

        var me = await client.GetAsync("/api/v1/external/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        using (var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync()))
        {
            var entries = doc.RootElement.GetProperty("projects").EnumerateArray()
                .Select(p => (Id: p.GetProperty("projectId").GetGuid(), Level: p.GetProperty("accessLevel").GetString() ?? ""))
                .ToList();

            entries.Should().Equal(new[] { (ProjectB, "FullAccess") },
                $"{plane}: /me tells the client only about projects it can read — not the GUID of a Secure project");
            entries.Should().NotContain(e => e.Level == "None", "/me never emits the level string \"None\"");
        }
    }

    // ================================================================================
    // ===== (4c) Task 105 · ISS-002 — a cut-short list says so (additive field) =======
    // ================================================================================

    private static readonly string[] ListRoutes =
    {
        "/api/v1/external/projects",
        $"/api/v1/external/projects/{ProjectA}/documents",
        $"/api/v1/external/projects/{ProjectA}/todos",
        $"/api/v1/external/projects/{ProjectA}/events",
        $"/api/v1/external/projects/{ProjectA}/contacts",
        $"/api/v1/external/projects/{ProjectA}/organizations",
    };

    public static TheoryData<string> ListRouteData()
    {
        var data = new TheoryData<string>();
        foreach (var route in ListRoutes)
            data.Add(route);
        return data;
    }

    /// <summary>
    /// The list envelope carries <c>truncated: true</c> when the read was cut short, and OMITS the field for a complete
    /// list — so a complete response is exactly the <c>{ "value": [...] }</c> shape the SPA has always read.
    /// </summary>
    [Theory]
    [MemberData(nameof(ListRouteData))]
    public async Task ListRoute_CarriesTruncatedOnlyWhenTheReadWasCutShort(string route)
    {
        using (var complete = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA }))
        {
            var response = await complete.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.OK, "body was: {0}", await response.Content.ReadAsStringAsync());
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.TryGetProperty("value", out _).Should().BeTrue();
            doc.RootElement.TryGetProperty("truncated", out _).Should().BeFalse("a complete list does not carry the field");
        }

        using (var cut = _fixture.CreateAuthenticatedClient(accessibleProjects: new[] { ProjectA }))
        {
            cut.DefaultRequestHeaders.Add("X-Test-Truncated", "1");
            var response = await cut.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.OK, "body was: {0}", await response.Content.ReadAsStringAsync());
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("truncated").GetBoolean().Should().BeTrue("a cut-short list says so (NFR-03)");
        }
    }

    // ================================================================================
    // ===== (5) Provisioner idempotency (FR-08) ======================================
    // ================================================================================

    [Fact]
    public async Task InviteAndGrant_WhenContactAlreadyHasOid_ReturnsAlreadyProvisioned_AndCreatesNoSecondCiamAccount()
    {
        // The Contact lookup returns an existing Contact already bound on the CIAM plane.
        var boundContactId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        _fixture.IdentityStore.AddContact(boundContactId, email: "attorney@firm.example",
            oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);

        using var client = _fixture.CreateAdminClient();

        var request = new
        {
            email = "attorney@firm.example",
            projectId = ProjectA,
            accessLevel = (int)ExternalAccessLevel.Collaborate,
            firstName = "Ada",
            lastName = "Counsel"
        };

        var response = await client.PostAsJsonAsync("/api/v1/external-access/invite-and-grant", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("onboardStatus").GetString().Should().Be("AlreadyProvisioned",
            "an oid-bound Contact takes the idempotent path — the CIAM provisioner is never invoked, " +
            "so no second CIAM account is created (FR-08). If it HAD been invoked, the real provisioner " +
            "would attempt live MSAL/Key-Vault and the request would 500 — a 200/AlreadyProvisioned proves " +
            "the early return.");
        _fixture.Dataverse.ContactUpdates.Should().BeEmpty(
            "the idempotent path must not re-bind the oid (no contacts UpdateAsync)");
        _fixture.IdentityStore.Writes.Should().BeEmpty("nor through the identity store");
    }

    // ================================================================================
    // ===== (6) Broker-only grant: writes record + invalidates cache, no synthetic SPE ==
    // ================================================================================

    [Fact]
    public async Task InviteAndGrant_WhenGranting_WritesExternalRecordAccess_InvalidatesCache_AndGrantsNoSyntheticSpePermission()
    {
        var boundContactId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        _fixture.IdentityStore.AddContact(boundContactId, email: "counsel@firm.example",
            oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);

        using var client = _fixture.CreateAdminClient();

        var request = new
        {
            email = "counsel@firm.example",
            projectId = ProjectA,
            accessLevel = (int)ExternalAccessLevel.ViewOnly,
            firstName = "Grace",
            lastName = "Advocate"
        };

        var response = await client.PostAsJsonAsync("/api/v1/external-access/invite-and-grant", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("accessRecordId").GetGuid().Should().NotBeEmpty();

        _fixture.Dataverse.CreatedEntitySets.Should().ContainSingle()
            .Which.Should().Be("sprk_externalrecordaccesses",
                "the grant writes exactly one sprk_externalrecordaccess record (grantee = Contact)");

        _fixture.TenantCacheMock.Verify(
            c => c.RemoveAsync(It.IsAny<string>(), "external-access-grant", boundContactId.ToString(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once(),
            "granting must invalidate the Contact's participation cache (ADR-009)");

        _fixture.SpeFileOperationsMock.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never());
        _fixture.SpeFileOperationsMock.VerifyNoOtherCalls();
        // Broker-only (ADR-028 A1 / task 026): no synthetic SPE container membership is written on grant —
        // the grant path touches no SPE facade at all.
    }

    // ================================================================================
    // ===== (7) Grant expiry — spec FR-33 / task 097 =================================
    // ================================================================================
    // Every grant is bounded. A past expiry is refused BEFORE any write — and, on invite-and-grant, before
    // any onboarding; expiry = today is valid (task 007's Date Only rule); an ABSENT expiry is defaulted
    // server-side to today + 90 days (owner decision 2026-09-10: no client sends one). "Today" is the
    // fixture's FakeTimeProvider date, 2026-09-10. The reason code and the 90 are asserted as LITERALS:
    // they are the wire contract, not implementation details.

    private static readonly Guid GranteeContactId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static DateOnly FixtureToday => DateOnly.FromDateTime(ExternalAccessContractFixture.ClockStart.UtcDateTime);

    private static object GrantBody(DateOnly? expiry) => new
    {
        contactId = GranteeContactId,
        projectId = ProjectA,
        accessLevel = (int)ExternalAccessLevel.ViewOnly,
        expiryDate = expiry?.ToString("yyyy-MM-dd")
    };

    private static object InviteAndGrantBody(DateOnly? expiry) => new
    {
        email = "expiry@firm.example",
        projectId = ProjectA,
        accessLevel = (int)ExternalAccessLevel.ViewOnly,
        expiryDate = expiry?.ToString("yyyy-MM-dd")
    };

    /// <summary>The single grant row written by the request, as the payload the BFF sent to Dataverse.</summary>
    private IDictionary<string, object?> WrittenGrant() =>
        (IDictionary<string, object?>)_fixture.Dataverse.Creates
            .Should().ContainSingle(c => c.EntitySet == "sprk_externalrecordaccesses").Which.Payload;

    private void GivenAnAlreadyProvisionedContact() =>
        _fixture.IdentityStore.AddContact(GranteeContactId, email: "expiry@firm.example",
            oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);

    private static async Task<string?> ReasonCode(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("reasonCode", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task PostGrant_WithAnExpiryBeforeToday_Returns400WithReasonCode_AndWritesNothing()
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/external-access/grant", GrantBody(FixtureToday.AddDays(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReasonCode(response)).Should().Be("sdap.access.grant.expiry_in_past");
        _fixture.Dataverse.CreatedEntitySets.Should().BeEmpty("a refused grant writes no row");
    }

    [Fact]
    public async Task PostGrant_WithAnExpiryOfToday_IsAccepted_AndStoresThatDate()
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/v1/external-access/grant", GrantBody(FixtureToday));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "\"access until 30 June\" means 30 June works — today is not in the past (task 007)");
        WrittenGrant()["sprk_expiresdate"].Should().Be(FixtureToday.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task PostGrant_WithNoExpiry_StoresTodayPlusNinetyDays()
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/v1/external-access/grant", GrantBody(null));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "an absent expiry is defaulted, not rejected");
        WrittenGrant()["sprk_expiresdate"].Should().Be(FixtureToday.AddDays(90).ToString("yyyy-MM-dd"),
            "FR-33: no grant is written unbounded — the server supplies today + 90 when the client sends none");
    }

    [Fact]
    public async Task InviteAndGrant_WithAnExpiryBeforeToday_Returns400_AndOnboardsNothing()
    {
        // ContactQueryResult is "[]": had the request got past validation it would CREATE a Contact and
        // provision a CIAM account. A refused request must leave neither behind.
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/external-access/invite-and-grant", InviteAndGrantBody(FixtureToday.AddDays(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReasonCode(response)).Should().Be("sdap.access.grant.expiry_in_past");
        _fixture.Dataverse.CreatedEntitySets.Should().BeEmpty("no Contact and no grant row is created");
        _fixture.Dataverse.ContactUpdates.Should().BeEmpty("no CIAM oid is bound");
    }

    [Fact]
    public async Task InviteAndGrant_WithAnExpiryOfToday_IsAccepted_AndStoresThatDate()
    {
        GivenAnAlreadyProvisionedContact();
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/external-access/invite-and-grant", InviteAndGrantBody(FixtureToday));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WrittenGrant()["sprk_expiresdate"].Should().Be(FixtureToday.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task InviteAndGrant_WithNoExpiry_StoresTodayPlusNinetyDays()
    {
        GivenAnAlreadyProvisionedContact();
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/external-access/invite-and-grant", InviteAndGrantBody(null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        WrittenGrant()["sprk_expiresdate"].Should().Be(FixtureToday.AddDays(90).ToString("yyyy-MM-dd"));
    }

    // ================================================================================
    // ===== (8) Record-wide share expiry — spec FR-33 / task 098 =====================
    // ================================================================================
    // One date for every active share of a record, written in one transaction (the Manage Access toolbar
    // Expiration). The wire contract lives here: the route, 200 + { updatedCount, expiresDate }, and the two
    // 400 reason codes, asserted as LITERALS. Which rows change, the single-transaction property and the stored
    // value are owned by tests/integration/auth/UnifiedAccessControl/RecordShareExpiryTests.

    private const string SetRecordShareExpiryPath = "/api/v1/external-access/set-record-share-expiry";

    private static object ShareExpiryBody(DateOnly? expiry) => new
    {
        recordType = "project",
        recordId = ProjectA,
        expiryDate = expiry?.ToString("yyyy-MM-dd")
    };

    [Fact]
    public async Task PostSetRecordShareExpiry_WithAnActiveShare_Returns200WithTheCountAndTheAppliedDate()
    {
        var shareId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        _fixture.Dataverse.ContactQueryResult =
            $$"""[{"sprk_externalrecordaccessid":"{{shareId}}","_sprk_contact_value":"{{GranteeContactId}}","statecode":0}]""";
        var expiry = FixtureToday.AddDays(91);
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(SetRecordShareExpiryPath, ShareExpiryBody(expiry));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("updatedCount").GetInt32().Should().Be(1);
        doc.RootElement.GetProperty("expiresDate").GetString().Should().Be(expiry.ToString("yyyy-MM-dd"));
        _fixture.TenantCacheMock.Verify(
            c => c.RemoveAsync(It.IsAny<string>(), "external-access-grant", GranteeContactId.ToString(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once(),
            "the grantee's participation cache is cleared after the write (ADR-009)");
    }

    [Theory]
    [InlineData(null, "sdap.access.share_expiry.expiry_required")]
    [InlineData(-1, "sdap.access.grant.expiry_in_past")]
    public async Task PostSetRecordShareExpiry_WithAMissingOrPastExpiry_Returns400WithReasonCode(int? offsetDays, string reasonCode)
    {
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(
            SetRecordShareExpiryPath, ShareExpiryBody(offsetDays is { } days ? FixtureToday.AddDays(days) : null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReasonCode(response)).Should().Be(reasonCode);
    }

    // ================================================================================
    // ===== (9) Invite identity collisions — task 141 (defect C7 item 7) =============
    // ================================================================================
    // One contact carries one sign-in. An invite whose email resolves to a contact the WORKFORCE plane owns, or
    // one an internal user links to, used to come back "AlreadyProvisioned" — no CIAM account, nobody told, and
    // the external person could never sign in. Now: HTTP 409, a ProblemDetails message, a distinct reason code,
    // a durable flag on the contact — and on /invite-and-grant, NO grant. If the CIAM provisioner HAD been
    // reached it would attempt live MSAL/Key Vault and the request would 500, so a 409 also proves no CIAM
    // account was created. The literals are the wire contract.

    private static object InviteBody(string email) => new { email, projectId = ProjectA, accessLevel = (int)ExternalAccessLevel.ViewOnly };

    [Theory]
    [InlineData("/api/v1/external-access/invite")]
    [InlineData("/api/v1/external-access/invite-and-grant")]
    public async Task Invite_WhenTheEmailBelongsToAWorkforceBoundContact_Returns409_FlagsIt_AndWritesNoGrant(string route)
    {
        var employeeContact = Guid.Parse("88888888-8888-8888-8888-888888888888");
        _fixture.IdentityStore.AddContact(employeeContact, email: "employee@customer.example",
            oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.Workforce);
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(route, InviteBody("employee@customer.example"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be("sdap.access.invite.workforce_bound_contact");
        using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace("a refusal carries a message");
            doc.RootElement.TryGetProperty("onboardStatus", out _).Should().BeFalse("never 'AlreadyProvisioned'");
        }

        _fixture.IdentityStore.Contacts[employeeContact].Flag!.Reason
            .Should().Be(IdentityCollisionReason.InviteMatchesWorkforceContact);
        _fixture.Dataverse.CreatedEntitySets.Should().BeEmpty("no Contact and no sprk_externalrecordaccess grant");
        _fixture.IdentityStore.Writes.Should().NotContain(w => w.Op == "bind", "no CIAM oid is bound");
    }

    [Theory]
    [InlineData("/api/v1/external-access/invite")]
    [InlineData("/api/v1/external-access/invite-and-grant")]
    public async Task Invite_WhenAnInternalUserLinksTheContact_Returns409_EvenThoughItIsCiamBound(string route)
    {
        // Ralph's dev shape: a CIAM-bound contact that a systemuser's sprk_primarycontact points at.
        var linkedContact = Guid.Parse("99999999-9999-9999-9999-999999999999");
        _fixture.IdentityStore.AddContact(linkedContact, email: "internal@firm.example",
            oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);
        _fixture.IdentityStore.AddSystemUser(Guid.NewGuid(), Guid.NewGuid(), "internal@firm.example", primaryContactId: linkedContact);
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(route, InviteBody("internal@firm.example"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be("sdap.access.invite.contact_linked_to_internal_user");
        _fixture.Dataverse.CreatedEntitySets.Should().BeEmpty();
    }

    [Fact]
    public async Task Invite_WhenTwoActiveContactsCarryTheEmail_Returns409WithItsOwnReasonCode()
    {
        _fixture.IdentityStore.AddContact(Guid.NewGuid(), email: "dup@firm.example");
        _fixture.IdentityStore.AddContact(Guid.NewGuid(), email: "dup@firm.example");
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/v1/external-access/invite", InviteBody("dup@firm.example"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReasonCode(response)).Should().Be("sdap.access.invite.email_ambiguous");
    }

    // Verifier finding 5: an unreadable lookup used to be thrown into the generic catch — a 500 "Failed to onboard
    // the external user" — so the decision's reason code and its message never reached the client. It is now its
    // own answer on BOTH routes: 503, reasonCode sdap.access.invite.contact_lookup_failed, the decision's message,
    // and nothing created, bound, flagged or granted. (Had the CIAM provisioner been reached it would attempt live
    // MSAL/Key Vault and 500, so a 503 also proves no CIAM account was created.)
    [Theory]
    [InlineData("/api/v1/external-access/invite")]
    [InlineData("/api/v1/external-access/invite-and-grant")]
    public async Task Invite_WhenTheContactLookupCannotBeRead_Returns503_WithItsOwnReasonCodeAndMessage(string route)
    {
        _fixture.IdentityStore.EmailLookupStatus = LookupStatus.Failed;
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(route, InviteBody("anyone@firm.example"));

        await ShouldBeTheLookupFailure(response);
    }

    [Theory]
    [InlineData("/api/v1/external-access/invite")]
    [InlineData("/api/v1/external-access/invite-and-grant")]
    public async Task Invite_WhenTheSystemUserReferenceLookupCannotBeRead_Returns503_AndWritesNothing(string route)
    {
        // The second read of the invite decision: is the one matching contact some internal user's contact?
        // Not knowing is not "no" — it fails the invite exactly like an unreadable email lookup.
        _fixture.IdentityStore.AddContact(Guid.NewGuid(), email: "unbound@firm.example");
        _fixture.IdentityStore.FailReferences = true;
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync(route, InviteBody("unbound@firm.example"));

        await ShouldBeTheLookupFailure(response);
    }

    private async Task ShouldBeTheLookupFailure(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ReasonCode(response)).Should().Be("sdap.access.invite.contact_lookup_failed");
        using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            // Pinned to the DECISION's message (ContactIdentityBinder.InviteMessage), which the endpoint's last-resort
            // fallback deliberately does not share — so this cannot pass on the fallback (third fix round).
            doc.RootElement.GetProperty("detail").GetString().Should().Be(
                ContactIdentityBinder.InviteMessage(ContactBindingDecision.InviteContactLookupFailed),
                "the decision's own message reaches the client");
            doc.RootElement.TryGetProperty("onboardStatus", out _).Should().BeFalse();
        }

        _fixture.Dataverse.CreatedEntitySets.Should().BeEmpty("no Contact and no sprk_externalrecordaccess grant");
        _fixture.IdentityStore.Writes.Should().BeEmpty("nothing is bound and nothing is flagged on an unreadable lookup");
    }

    [Fact]
    public async Task Invite_ACiamBoundContact_KeepsTodaysIdempotentResponse()
    {
        _fixture.IdentityStore.AddContact(Guid.NewGuid(), email: "outside@firm.example",
            oid: Guid.NewGuid().ToString("D"), plane: IdentityPlaneMarker.External);
        using var client = _fixture.CreateAdminClient();

        var response = await client.PostAsJsonAsync("/api/v1/external-access/invite", InviteBody("outside@firm.example"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("status").GetString().Should().Be("AlreadyProvisioned");
    }
}

// ================================================================================
// ===== Fixture ==================================================================
// ================================================================================

/// <summary>
/// In-process BFF fixture for the external-access contract tests. Mirrors the canonical config-key set
/// (per <c>.claude/constraints/bff-extensions.md §F.2</c>) plus the <c>Ciam:*</c> keys so the "Ciam"
/// JwtBearer scheme + <c>CiamGraphClientFactory</c> construct at startup, then swaps the Dataverse-backed
/// services + SPE facade for module-boundary doubles that scenarios drive via request headers.
/// </summary>
public sealed class ExternalAccessContractFixture : WebApplicationFactory<Program>
{
    private const string TestScheme = "TestExternalAuth";

    public Mock<IDocumentStorageResolver> StorageResolverMock { get; } = new(MockBehavior.Loose);
    public Mock<ISpeFileOperations> SpeFileOperationsMock { get; } = new(MockBehavior.Loose);
    public Mock<ITenantCache> TenantCacheMock { get; } = new(MockBehavior.Loose);
    public StubDataverseWebApiClient Dataverse { get; } = new();

    /// <summary>The app-only Dataverse seam (<c>IDataverseService</c>, also <c>IGenericEntityService</c>).</summary>
    public Mock<IDataverseService> DataverseServiceMock { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// The identity-binding row store (task 141). CIAM contact resolution and the invite path read it; a CIAM
    /// caller's contact is header-driven on top of it (<see cref="HeaderDrivenIdentityStore"/>).
    /// </summary>
    public InMemoryContactIdentityStore IdentityStore { get; } = new();

    /// <summary>The POA share table behind task 063's system-user share routes (in memory; see its remarks).</summary>
    public Sprk.Bff.Api.Tests.AccessControl.FakeRecordShareTable RecordShares { get; } = new();

    /// <summary>
    /// The instant the fixture's clock is fixed at (task 097). Grant-expiry decisions read "today" from the
    /// injected <see cref="TimeProvider"/>, so every expiry assertion here is deterministic. Never advanced —
    /// <see cref="FakeTimeProvider"/> cannot move backwards, so a test that advanced it would leak.
    /// </summary>
    public static readonly DateTimeOffset ClockStart = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    public FakeTimeProvider Clock { get; } = new(ClockStart);

    /// <summary>Reset per-test mutable double state so tests are independent.</summary>
    public void Reset()
    {
        StorageResolverMock.Reset();
        SpeFileOperationsMock.Reset();
        TenantCacheMock.Reset();
        Dataverse.Reset();
        IdentityStore.Reset();
        RecordShares.Reset();
        DataReads.Clear();
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            var dict = new Dictionary<string, string?>
            {
                ["ConnectionStrings:ServiceBus"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["Cors:AllowedOrigins:0"] = "https://localhost:5173",
                ["UAMI_CLIENT_ID"] = "test-client-id",
                ["TENANT_ID"] = "test-tenant-id",
                ["API_APP_ID"] = "test-app-id",
                ["API_CLIENT_SECRET"] = "test-secret",
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = "test-tenant-id",
                ["AzureAd:ClientId"] = "test-app-id",
                ["AzureAd:Audience"] = "api://test-app-id",
                // CIAM second scheme (task 020) — keys required so AddJwtBearer("Ciam") + CiamGraphClientFactory bind.
                ["Ciam:Instance"] = "https://spaarketest.ciamlogin.com",
                ["Ciam:TenantId"] = "00000000-0000-0000-0000-0000000000c1",
                ["Ciam:ClientId"] = "ciam-api-client-id",
                ["Ciam:Audience"] = "api://ciam-api-client-id",
                ["Ciam:Domain"] = "spaarketest.onmicrosoft.com",
                ["Ciam:GraphProvisioner:ClientId"] = "ciam-graph-provisioner-id",
                ["Ciam:GraphProvisioner:CertificateName"] = "ciam-graph-cert",
                ["ExternalAccess:PortalUrl"] = "https://external.spaarke.test",
                ["Graph:TenantId"] = "test-tenant-id",
                ["Graph:ClientId"] = "test-client-id",
                ["Graph:ClientSecret"] = "test-client-secret",
                ["Graph:ManagedIdentity:Enabled"] = "false",
                ["Graph:Scopes:0"] = "https://graph.microsoft.com/.default",
                ["Dataverse:EnvironmentUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
                ["Dataverse:ClientId"] = "test-client-id",
                ["Dataverse:ClientSecret"] = "test-client-secret",
                ["Dataverse:TenantId"] = "test-tenant-id",
                ["ServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test",
                ["ServiceBus:QueueName"] = "sdap-jobs",
                ["DocumentIntelligence:Enabled"] = "true",
                ["DocumentIntelligence:OpenAiEndpoint"] = "https://test.openai.azure.com/",
                ["DocumentIntelligence:OpenAiKey"] = "test-key",
                ["DocumentIntelligence:OpenAiDeployment"] = "gpt-4o",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["AiSearchResilience:MaxRetryAttempts"] = "3",
                ["AiSearchResilience:CircuitBreakerFailureThreshold"] = "5",
                ["AiSearchResilience:CircuitBreakerDuration"] = "00:00:30",
                ["GraphResilience:MaxRetryAttempts"] = "3",
                ["GraphResilience:RetryDelay"] = "00:00:01",
                ["GraphResilience:CircuitBreakerFailureThreshold"] = "5",
                ["GraphResilience:CircuitBreakerDuration"] = "00:00:30",
                ["SpeAdmin:KeyVaultUri"] = "https://test.vault.azure.net/",
                ["ManagedIdentity:ClientId"] = "test-managed-identity-client-id",
                ["CosmosPersistence:Endpoint"] = "https://test.documents.azure.com:443/",
                ["CosmosPersistence:DatabaseName"] = "spaarke-ai-test",
                ["AgentService:Enabled"] = "false",
                ["AgentService:Endpoint"] = "https://test.services.ai.azure.com/api/projects/test-project",
                ["AgentService:AgentId"] = "test-agent-id",
                ["AgentService:MaxConcurrency"] = "4",
                ["AgentService:ThreadCacheExpiryMinutes"] = "60",
            };
            config.AddInMemoryCollection(dict);
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = false;
            options.ValidateOnBuild = false;
        });

        builder.ConfigureTestServices(services =>
        {
            // Test hosts must not authenticate for real — see TestTokenCredential.
            services.UseStubTokenCredential();

            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(o => o.ThrowOnBadRequest = false);

            // Fake auth scheme serving BOTH the workforce default (admin group) and the CiamExternal policy.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestScheme;
                options.DefaultChallengeScheme = TestScheme;
            }).AddScheme<AuthenticationSchemeOptions, ExternalTestAuthHandler>(TestScheme, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestScheme;
                options.DefaultChallengeScheme = TestScheme;
            });

            // Re-point the external collaboration policies at the fake scheme (the real "Ciam" + workforce
            // JwtBearer schemes need live token infra). ExternalCollaboration (task 025) is the dual-scheme
            // policy now on /api/v1/external; CiamExternal is retained for reference.
            services.Configure<AuthorizationOptions>(options =>
            {
                options.AddPolicy(AuthPolicies.CiamExternal, policy =>
                {
                    policy.AuthenticationSchemes = new[] { TestScheme };
                    policy.RequireAuthenticatedUser();
                });
                options.AddPolicy(AuthPolicies.ExternalCollaboration, policy =>
                {
                    policy.AuthenticationSchemes = new[] { TestScheme };
                    policy.RequireAuthenticatedUser();
                });
            });

            services.RemoveAll<IHostedService>();

            // Avoid real MSAL on incidental Graph paths.
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            DataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(DataverseServiceMock.Object);

            // ── Module-boundary doubles ──────────────────────────────────────────
            services.RemoveAll<IDocumentStorageResolver>();
            services.AddScoped(_ => StorageResolverMock.Object);

            services.RemoveAll<ISpeFileOperations>();
            services.AddScoped(_ => SpeFileOperationsMock.Object);

            services.RemoveAll<ITenantCache>();
            services.AddSingleton(TenantCacheMock.Object);

            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton<DataverseWebApiClient>(Dataverse);

            // Task 063: the system-user share routes read and write POA shares through the one seam. In memory, so
            // their wire contract can be asserted without a Dataverse.
            services.RemoveAll<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>();
            services.AddSingleton<Sprk.Bff.Api.Services.Access.IDataverseRecordShareService>(RecordShares);

            // Task 149: the share routes fan out to a secure record's children through the secure-child synchronizer.
            // The contract here is the routes' wire shape, so it runs over an environment with no Secure Record BU (no
            // record is secure): the fan-out reads nothing more and writes nothing. The fan-out itself is pinned in
            // SecureChildShareMirrorTests.
            var noSecureRecords = Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.WithoutSecureBusinessUnit();
            services.RemoveAll<Sprk.Bff.Api.Services.Access.SecureChildShareSynchronizer>();
            services.AddSingleton(Sprk.Bff.Api.Tests.DataMutation.ExternalAccess.SecureChildShareWorld.SynchronizerOver(
                () => noSecureRecords, RecordShares));

            // Task 158 r1 (owner round 30): the share routes also fan out to the secure work assignments and projects filed
            // under the record (both directions), and a fan-out that cannot run is now children_incomplete. The contract
            // here is the routes' wire shape: the inheritance reads a Dataverse with no rows and an empty provenance ledger,
            // so nothing is filed under anything. The fan-out itself is pinned in SecureRootInheritanceTests.
            services.RemoveAll<Sprk.Bff.Api.Services.Access.SecureRootInheritance>();
            services.AddScoped(_ => Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.InheritanceOverNothing());

            // Task 158 r1c-v2 (round 39 item 2): the No Access guard also walks what a work assignment or project is filed
            // under, through IGenericEntityService — here the same Dataverse with no rows, so nothing is filed under anything
            // and only the record's own list applies (the production guard otherwise, over this fixture's deny list).
            services.RemoveAll<SecureShareNoAccessGuard>();
            services.AddScoped(sp => new SecureShareNoAccessGuard(
                sp.GetRequiredService<ExternalParticipationService>(), sp.GetRequiredService<INoAccessListReader>(),
                sp.GetRequiredService<IContactIdentityStore>(),
                Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.NoFilingRows(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SecureShareNoAccessGuard>.Instance));

            // Fixed clock for grant-expiry decisions (task 097).
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Delegation rule (unified-access-control-r2 task 008, FR-07): every /api/v1/external-access
            // route now requires Write on the target record, evaluated as the caller via an OBO probe.
            // These are CONTRACT tests — they assert what /invite-and-grant does for an ENTITLED caller,
            // not who is entitled — so the fixture's caller is given Write. Without this the real probe
            // has no Dataverse offline, correctly answers "no rights", and every case 403s before the
            // contract under test is ever exercised.
            //
            // Deliberately NOT an "allow everything" stub: it reports Write on the record it is asked
            // about, so if a future change aimed the check at the wrong record these tests would still
            // pass — that discrimination is owned by DelegationRuleCharacterizationTests, which asserts
            // the probed target. Here the point is only that an entitled caller gets through.
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new EntitledCallerRecordAccessProbe());

            // Replace the Dataverse-backed external services with header-driven stubs (virtual seams).
            services.RemoveAll<ExternalParticipationService>();
            services.AddScoped<ExternalParticipationService>(sp =>
                new StubExternalParticipationService(
                    sp.GetRequiredService<IHttpContextAccessor>(),
                    // Task 137: the grant-write paths invalidate through this service's ONE routine, which runs for real
                    // over the fixture's ITenantCache — so the RemoveAsync verifications read what production removed.
                    sp.GetRequiredService<ITenantCache>()));

            services.RemoveAll<ExternalDataService>();
            services.AddScoped<ExternalDataService>(sp =>
                new StubExternalDataService(sp.GetRequiredService<IHttpContextAccessor>(), DataReads));

            // Task 141: contact resolution (CIAM) and the invite's collision checks read the identity store.
            services.RemoveAll<IContactIdentityStore>();
            services.AddSingleton<IContactIdentityStore>(sp =>
                new HeaderDrivenIdentityStore(IdentityStore, sp.GetRequiredService<IHttpContextAccessor>()));

            // Task 135 (C1): CIAM callers now pass the deny-list veto. Offline, the real reader fails CLOSED
            // and would deny every record; these contract tests assert an entitled caller's contract, so the
            // reader at its module boundary denies nothing. Veto behaviour is owned by UnifiedEvaluatorSeamTests.
            services.RemoveAll<INoAccessListReader>();
            services.AddSingleton(Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NeverDeniesReader());

            // Task 136: the read routes are plane-agnostic, so the C2 denials are asserted on BOTH planes. A
            // workforce-token request (X-Test-Plane: workforce) resolves to a contact-only principal named by
            // its X-Test-Contact header; with no standing grant anywhere, its contact plane composes exactly
            // the explicit grants the stub participation service returns — the same world the CIAM request sees.
            services.RemoveAll<IWorkforcePrincipalResolver>();
            services.AddSingleton<IWorkforcePrincipalResolver, HeaderWorkforcePrincipalResolver>();
            services.RemoveAll<ISubjectStandingGrantReader>();
            services.AddSingleton<ISubjectStandingGrantReader, NoStandingGrantReader>();

            // Task 136: the route gate is the THIRD layer (after the evaluator's and the strategies' pruning),
            // so no real strategy can hand a route a None-rights entry. The CIAM strategy is wrapped — at the
            // ICallerPrincipalStrategy plug-in seam — so a request carrying X-Test-PowerlessProject receives a
            // hand-built principal holding that project at None, and the handlers' own Read test is observable.
            // Every other request is delegated to the real CIAM strategy unchanged.
            services.RemoveAll<ICallerPrincipalStrategy>();
            services.AddScoped<CiamContactPrincipalStrategy>();
            services.AddScoped<ICallerPrincipalStrategy>(sp =>
                new PowerlessProjectCiamStrategy(sp.GetRequiredService<CiamContactPrincipalStrategy>()));
            services.AddScoped<ICallerPrincipalStrategy, WorkforcePrincipalStrategy>();
        });
    }

    /// <summary>
    /// Every app-only data read the project routes reached (task 136), as "Method:projectId". A denied read
    /// must leave this empty: the gate runs before the read, never after it.
    /// </summary>
    public System.Collections.Concurrent.ConcurrentQueue<string> DataReads { get; } = new();

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>External (CIAM) caller. Scenario is carried on request headers read by the stub services.</summary>
    /// <param name="accessibleProjects">Project grant rows (all-sources level = <paramref name="accessLevel"/>).</param>
    /// <param name="documentProjectId">The project the requested document belongs to.</param>
    /// <param name="accessLevel">The grant rows' all-sources level. Omitted ⇒ FullAccess.</param>
    /// <param name="workforce">Task 136: sign in on a WORKFORCE token (contact-only principal) instead of CIAM.</param>
    /// <param name="secureProjects">Task 136: projects whose <c>sprk_issecure</c> flag reads true.</param>
    /// <param name="directAccessLevel">Task 136: the DIRECT level of the project grant rows. Omitted ⇒ null, i.e.
    /// every row looks organization-inherited — invisible on an open project, and nothing on a Secure one.</param>
    /// <param name="nullLevelMatters">Task 136: matter grant rows with NO level (written outside the BFF).</param>
    /// <param name="nullLevelWorkAssignments">Task 136: work-assignment grant rows with NO level.</param>
    public HttpClient CreateAuthenticatedClient(
        IReadOnlyList<Guid> accessibleProjects,
        Guid? documentProjectId = null,
        ExternalAccessLevel? accessLevel = null,
        bool workforce = false,
        IReadOnlyList<Guid>? secureProjects = null,
        ExternalAccessLevel? directAccessLevel = null,
        IReadOnlyList<Guid>? nullLevelMatters = null,
        IReadOnlyList<Guid>? nullLevelWorkAssignments = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        client.DefaultRequestHeaders.Add("X-Test-Contact", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Test-Projects", string.Join(",", accessibleProjects));
        if (documentProjectId.HasValue)
            client.DefaultRequestHeaders.Add("X-Test-DocProject", documentProjectId.Value.ToString());
        // Optional: participation access level. Omitted ⇒ FullAccess, preserving the behaviour every
        // pre-existing caller of this helper relies on. Set it to assert that a read-capable but
        // write-incapable participant is refused on a write route (unified-access-control-r2).
        if (accessLevel.HasValue)
            client.DefaultRequestHeaders.Add("X-Test-AccessLevel", accessLevel.Value.ToString());
        if (workforce)
            client.DefaultRequestHeaders.Add("X-Test-Plane", "workforce");
        if (secureProjects is { Count: > 0 })
            client.DefaultRequestHeaders.Add("X-Test-SecureProjects", string.Join(",", secureProjects));
        if (directAccessLevel.HasValue)
            client.DefaultRequestHeaders.Add("X-Test-DirectAccessLevel", directAccessLevel.Value.ToString());
        if (nullLevelMatters is { Count: > 0 })
            client.DefaultRequestHeaders.Add("X-Test-NullLevelMatters", string.Join(",", nullLevelMatters));
        if (nullLevelWorkAssignments is { Count: > 0 })
            client.DefaultRequestHeaders.Add("X-Test-NullLevelWorkAssignments", string.Join(",", nullLevelWorkAssignments));
        return client;
    }

    /// <summary>Workforce (admin) caller for /api/v1/external-access/*.</summary>
    public HttpClient CreateAdminClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

// ================================================================================
// ===== Test doubles =============================================================
// ================================================================================

/// <summary>
/// Authenticates when an <c>Authorization</c> header is present; fails (⇒ 401) otherwise. Emits stable
/// <c>oid</c> + <c>tid</c> claims so the auth filter, audited grant (sprk_grantedby), and cache tenant
/// scoping have deterministic values.
/// </summary>
internal sealed class ExternalTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public ExternalTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));

        var claims = new List<Claim>
        {
            new Claim("oid", "00000000-0000-0000-0000-0000000000a1"),
            new Claim("tid", "00000000-0000-0000-0000-0000000000b1"),
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-0000000000a1"),
        };

        // teams-app-r1 task 025: the CallerPrincipalResolver selects the plane by token issuer.
        if (string.Equals(Request.Headers["X-Test-Plane"], "workforce", StringComparison.OrdinalIgnoreCase))
        {
            // Task 136: a WORKFORCE token (tid b1 is not the configured CIAM tenant c1, and the issuer is not
            // ciamlogin.com) → the workforce strategy. Its contact-only principal comes from
            // HeaderWorkforcePrincipalResolver, keyed on the test_contact claim.
            claims.Add(new Claim("iss", "https://login.microsoftonline.com/00000000-0000-0000-0000-0000000000b1/v2.0"));
            claims.Add(new Claim("test_contact", Request.Headers["X-Test-Contact"].ToString()));
        }
        else
        {
            // These external tests exercise the CIAM plane by default, so the fake token carries a
            // *.ciamlogin.com issuer → the CIAM strategy (StubExternalParticipationService).
            claims.Add(new Claim("iss", "https://spaarketest.ciamlogin.com/00000000-0000-0000-0000-0000000000c1/v2.0"));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

/// <summary>
/// Header-driven <see cref="ExternalParticipationService"/> stub (overrides the <c>virtual</c> module-boundary
/// methods added in task 030). <c>X-Test-Contact</c> = "none" ⇒ unresolvable Contact; <c>X-Test-Projects</c> =
/// comma-separated project GUIDs the caller may access.
/// </summary>
internal sealed class StubExternalParticipationService : ExternalParticipationService
{
    private readonly IHttpContextAccessor _accessor;

    public StubExternalParticipationService(IHttpContextAccessor accessor, ITenantCache? cache = null)
        : base(new HttpClient(), cache ?? Mock.Of<ITenantCache>(), new ConfigurationBuilder().Build(),
               Mock.Of<TokenCredential>(), accessor, NullLogger<ExternalParticipationService>.Instance)
    {
        _accessor = accessor;
    }

    private string? Header(string name) =>
        _accessor.HttpContext?.Request.Headers.TryGetValue(name, out var v) == true ? v.ToString() : null;

    // Task 037: without this override the base implementation runs, hits `credential: null!`, throws,
    // and fails CLOSED — every record would read as secure AND restricted and this double would
    // compose to nothing. Unflagged is the right default for a test that predates the vetoes.
    // Task 136: X-Test-SecureProjects flags the named ids sprk_issecure.
    // Task 138: X-Test-RestrictedProjects / X-Test-LimitedProjects set sprk_accesspermission for the named ids
    // (combinable with Secure). X-Test-RootFlags: "unreadable" answers RootRecordFlags.Unreadable for every
    // id (a failed read); "absent" leaves every id OUT of the map (the non-flag-bearing-type shape).
    public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
        string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
    {
        var mode = Header("X-Test-RootFlags");
        if (string.Equals(mode, "absent", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IReadOnlyDictionary<Guid, RootRecordFlags>>(new Dictionary<Guid, RootRecordFlags>());

        var unreadable = string.Equals(mode, "unreadable", StringComparison.OrdinalIgnoreCase);
        var secure = ParseGuidHeader("X-Test-SecureProjects");
        var restricted = ParseGuidHeader("X-Test-RestrictedProjects");
        var limited = ParseGuidHeader("X-Test-LimitedProjects");
        return Task.FromResult<IReadOnlyDictionary<Guid, RootRecordFlags>>(
            recordIds.Distinct().ToDictionary(
                id => id,
                id => unreadable
                    ? RootRecordFlags.Unreadable
                    : new RootRecordFlags(
                        IsSecure: secure.Contains(id),
                        IsRestricted: restricted.Contains(id),
                        IsLimited: limited.Contains(id))));
    }

    // Task 135 (C1): a CIAM caller is now composed by the unified evaluator, which also reads the contact's
    // organization memberships (the deny-veto subject) and each candidate's referenced organizations. Without
    // these overrides the base reads run against no Dataverse and fail CLOSED — every candidate denied — so
    // this double would compose to nothing. "Belongs to no organization / references none" is the honest
    // default for contract tests that assert what an ENTITLED caller gets; the vetoes themselves are owned by
    // UnifiedEvaluatorSeamTests.
    // Task 137: the contact's live state. Active, so this double's grants compose exactly as before;
    // the inactive-contact guard itself is pinned by UnifiedEvaluatorSeamTests (task 137 section).
    internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
        => Task.FromResult(ContactRecordState.Active);

    internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(
        Guid contactId, CancellationToken ct = default)
        => Task.FromResult(ActiveOrgMemberships.None);

    public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
        string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, ReferencedOrganizations>>(
            recordIds.Distinct().ToDictionary(id => id, _ => ReferencedOrganizations.None));

    public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
    {
        var raw = Header("X-Test-Projects");

        // Default FullAccess — every test written before X-Test-AccessLevel existed assumes it.
        var levelRaw = Header("X-Test-AccessLevel");
        var level = Enum.TryParse<ExternalAccessLevel>(levelRaw, ignoreCase: true, out var parsed)
            ? parsed
            : ExternalAccessLevel.FullAccess;

        // Task 136: the DIRECT level. Absent ⇒ null — the row reads as organization-inherited, which is
        // invisible on an open project (the all-sources level is used) and confers nothing on a Secure one.
        ExternalAccessLevel? directLevel = Enum.TryParse<ExternalAccessLevel>(
            Header("X-Test-DirectAccessLevel"), ignoreCase: true, out var direct)
            ? direct
            : null;

        IReadOnlyList<ExternalParticipation> projects = string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<ExternalParticipation>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Where(p => Guid.TryParse(p, out _))
                 .Select(p => new ExternalParticipation { ProjectId = Guid.Parse(p), AccessLevel = level, DirectAccessLevel = directLevel })
                 .ToList();

        // Optional matter / work-assignment grants for polymorphic tests (task 028).
        var matters = ParseGuidHeader("X-Test-Matters");
        var was = ParseGuidHeader("X-Test-WorkAssignments");

        // Task 136: grant rows with NO level — written outside the BFF. Owner 2026-09-30: no level = not granted.
        var nullLevelMatters = ParseGuidHeader("X-Test-NullLevelMatters");
        var nullLevelWas = ParseGuidHeader("X-Test-NullLevelWorkAssignments");

        return Task.FromResult(new ExternalGrantSet
        {
            Projects = projects,
            // Task 032: matter/WA grants carry levels. This harness drives ids from test headers, so it
            // converts at Collaborate — the level a bare id effectively resolved to before levels existed.
            MatterGrants = matters.Select(id => new ExternalRootGrant
            {
                RecordId = id,
                AccessLevel = ExternalAccessLevel.Collaborate
            })
            .Concat(nullLevelMatters.Select(id => new ExternalRootGrant { RecordId = id, AccessLevel = null }))
            .ToList(),
            WorkAssignmentGrants = was.Select(id => new ExternalRootGrant
            {
                RecordId = id,
                AccessLevel = ExternalAccessLevel.Collaborate
            })
            .Concat(nullLevelWas.Select(id => new ExternalRootGrant { RecordId = id, AccessLevel = null }))
            .ToList(),
        });
    }

    private IReadOnlySet<Guid> ParseGuidHeader(string name)
    {
        var raw = Header(name);
        return string.IsNullOrWhiteSpace(raw)
            ? new HashSet<Guid>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                 .Where(v => Guid.TryParse(v, out _))
                 .Select(Guid.Parse)
                 .ToHashSet();
    }
}

/// <summary>
/// Header-driven <see cref="ExternalDataService"/> stub. <c>X-Test-DocProject</c> = the project GUID the
/// requested document belongs to (absent ⇒ document not found / null project).
/// </summary>
internal sealed class StubExternalDataService : ExternalDataService
{
    private readonly IHttpContextAccessor _accessor;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _reads;

    public StubExternalDataService(
        IHttpContextAccessor accessor, System.Collections.Concurrent.ConcurrentQueue<string> reads)
        : base(new HttpClient(), new ConfigurationBuilder().Build(),
               Mock.Of<TokenCredential>(), NullLogger<ExternalDataService>.Instance)
    {
        _accessor = accessor;
        _reads = reads;
    }

    // ── Task 136: the project READ seams, recorded. A route that admits a caller reaches one of these and
    // returns 200; a route that denies must never reach them (asserted through ExternalAccessContractFixture.DataReads).

    public override Task<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalProjectDto>> GetProjectsAsync(
        IEnumerable<Guid> projectIds, CancellationToken ct = default)
    {
        var ids = projectIds.ToList();
        _reads.Enqueue($"{nameof(GetProjectsAsync)}:{string.Join(",", ids)}");
        return Task.FromResult(new Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalProjectDto>
        {
            Value = ids
                .Select(id => new Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalProjectDto { SprkProjectid = id.ToString(), SprkName = "Project" })
                .ToList(),
            Truncated = TruncatedRequested,
        });
    }

    /// <summary>Task 105: <c>X-Test-Truncated</c> present ⇒ the read reports itself cut short.</summary>
    private bool TruncatedRequested =>
        _accessor.HttpContext?.Request.Headers.ContainsKey("X-Test-Truncated") == true;

    public override Task<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalProjectDto?> GetProjectByIdAsync(
        Guid projectId, CancellationToken ct = default)
    {
        _reads.Enqueue($"{nameof(GetProjectByIdAsync)}:{projectId}");
        return Task.FromResult<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalProjectDto?>(
            new Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalProjectDto { SprkProjectid = projectId.ToString(), SprkName = "Project" });
    }

    public override Task<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalDocumentDto>> GetDocumentsAsync(
        Guid projectId, CancellationToken ct = default)
    {
        _reads.Enqueue($"{nameof(GetDocumentsAsync)}:{projectId}");
        return Task.FromResult(new Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalDocumentDto> { Truncated = TruncatedRequested });
    }

    // Task 105: the to-do list seam, so the list-envelope contract covers the to-dos route too.
    public override Task<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalTodoDto>> GetTodosAsync(
        TodoRootKind rootKind, Guid rootId, CancellationToken ct = default)
    {
        _reads.Enqueue($"{nameof(GetTodosAsync)}:{rootKind}:{rootId}");
        return Task.FromResult(new Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalTodoDto> { Truncated = TruncatedRequested });
    }

    public override Task<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalEventDto>> GetEventsAsync(
        Guid projectId, CancellationToken ct = default)
    {
        _reads.Enqueue($"{nameof(GetEventsAsync)}:{projectId}");
        return Task.FromResult(new Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalEventDto> { Truncated = TruncatedRequested });
    }

    public override Task<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalContactDto>> GetContactsAsync(
        Guid projectId, CancellationToken ct = default)
    {
        _reads.Enqueue($"{nameof(GetContactsAsync)}:{projectId}");
        return Task.FromResult(new Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalContactDto> { Truncated = TruncatedRequested });
    }

    public override Task<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalOrganizationDto>> GetOrganizationsAsync(
        Guid projectId, CancellationToken ct = default)
    {
        _reads.Enqueue($"{nameof(GetOrganizationsAsync)}:{projectId}");
        return Task.FromResult(new Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalCollectionResponse<Sprk.Bff.Api.Api.ExternalAccess.Dtos.ExternalOrganizationDto> { Truncated = TruncatedRequested });
    }

    public override Task<(Guid? ProjectId, string? DocumentName)> GetDocumentProjectAndNameAsync(Guid documentId, CancellationToken ct = default)
    {
        var raw = _accessor.HttpContext?.Request.Headers.TryGetValue("X-Test-DocProject", out var v) == true ? v.ToString() : null;
        return Guid.TryParse(raw, out var projectId)
            ? Task.FromResult<(Guid?, string?)>((projectId, "external-doc.bin"))
            : Task.FromResult<(Guid?, string?)>((null, null));
    }
}

/// <summary>
/// Task 136: resolves a workforce-token request to a CONTACT-ONLY principal named by its <c>test_contact</c>
/// claim (from the X-Test-Contact header), so the read-route denials can be asserted on the workforce plane
/// through the real <see cref="WorkforcePrincipalStrategy"/> and the real evaluator. Substituted at the
/// <see cref="IWorkforcePrincipalResolver"/> seam (ADR-010), which exists for exactly this.
/// </summary>
internal sealed class HeaderWorkforcePrincipalResolver : IWorkforcePrincipalResolver
{
    public Task<WorkforcePrincipalResolution> ResolveAsync(ClaimsPrincipal user, CancellationToken ct)
        => Task.FromResult(Guid.TryParse(user.FindFirst("test_contact")?.Value, out var contactId)
            ? WorkforcePrincipalResolution.ForContact(
                contactId, user.FindFirst("oid")!.Value, user.FindFirst("tid")!.Value)
            : WorkforcePrincipalResolution.Denied(
                WorkforceDenyReason.PrincipalNotResolved, WorkforcePrincipalResolver.DenyPrincipalNotResolved));
}

/// <summary>
/// Task 136: the CIAM strategy, except that a request carrying <c>X-Test-PowerlessProject</c> resolves to a
/// principal BUILT DIRECTLY with that project at <see cref="AccessRights.None"/> — the entry the evaluator and both
/// strategies now refuse to produce. It isolates the read routes' own Read test, the third of task 136's layers.
/// </summary>
internal sealed class PowerlessProjectCiamStrategy : ICallerPrincipalStrategy
{
    private readonly CiamContactPrincipalStrategy _inner;

    public PowerlessProjectCiamStrategy(CiamContactPrincipalStrategy inner) => _inner = inner;

    public CallerPrincipalPlane Plane => CallerPrincipalPlane.CiamContact;

    public Task<CallerPrincipalResolution> ResolveAsync(HttpContext httpContext, CancellationToken ct)
    {
        if (!Guid.TryParse(httpContext.Request.Headers["X-Test-PowerlessProject"].ToString(), out var projectId))
        {
            return _inner.ResolveAsync(httpContext, ct);
        }

        return Task.FromResult(CallerPrincipalResolution.Resolved(new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.NewGuid(),
            ProjectAccess = new[] { new CallerProjectAccess { ProjectId = projectId, Rights = AccessRights.None } },
        }));
    }
}

/// <summary>
/// Task 136: no contact or organization holds a standing grant, so the workforce contact plane's derived-member
/// terms contribute nothing and it composes the same explicit grants the CIAM plane does.
/// </summary>
internal sealed class NoStandingGrantReader : ISubjectStandingGrantReader
{
    public Task<StandingGrantState> ReadForContactAsync(Guid contactId, CancellationToken ct)
        => Task.FromResult(StandingGrantState.NotHeld);

    public Task<StandingGrantState> ReadForOrganizationAsync(Guid organizationId, CancellationToken ct)
        => Task.FromResult(StandingGrantState.NotHeld);
}

/// <summary>
/// A caller who holds Write on whatever record the delegation rule asks about — i.e. someone entitled
/// to manage external access, which is the caller these contract tests are written from the
/// perspective of (task 008, FR-07).
/// </summary>
/// <remarks>
/// Substituted at the <c>virtual</c> seam on <see cref="CallerRecordAccessProbe"/>, so no OBO exchange
/// or Dataverse call is attempted. Whether an UNENTITLED caller is refused — the actual subject of
/// FR-07 — is asserted in <c>tests/integration/auth/UnifiedAccessControl/</c>, not here.
/// </remarks>
public sealed class EntitledCallerRecordAccessProbe : CallerRecordAccessProbe
{
    public EntitledCallerRecordAccessProbe()
        : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    { }

    public override Task<AccessRights> GetCallerRightsAsync(
        string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        // Write is all the delegation gate needs. The rest matter since task 063 made a share the INTERSECTION of the
        // requested level with the caller's OWN rights (owner 2026-09-16): an entitled caller must hold a full working
        // set, or these contract tests would silently be exercising the narrowing path instead of the contract. The
        // narrowing itself is owned by InternalUserShareTests. Share joined the working set with task 139 (Collaborate and
        // Full Access carry it), so without it every Collaborate share here would be narrowed.
        => Task.FromResult(
            AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete
            | AccessRights.Share);
}

/// <summary>
/// <see cref="DataverseWebApiClient"/> double driven off the (additive, backward-compatible) <c>virtual</c>
/// seams. Returns a configured JSON query result, records creates/updates by entity set. No HTTP.
/// </summary>
public sealed class StubDataverseWebApiClient : DataverseWebApiClient
{
    public StubDataverseWebApiClient()
        : base(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
        }).Build(), NullLogger<DataverseWebApiClient>.Instance, NoOpCredential.Instance)
    { }

    /// <summary>
    /// Never used — this double overrides every virtual seam and issues no HTTP. It exists so the
    /// base constructor needs no credential CONFIGURATION at all.
    ///
    /// <para>History, because this stub has now broken twice for unrelated reasons. It originally
    /// passed <c>Dataverse:ClientId</c> / <c>:ClientSecret</c> / <c>:TenantId</c> — keys
    /// <see cref="DataverseWebApiClient"/> never reads (it reads <c>API_APP_ID</c> /
    /// <c>API_CLIENT_SECRET</c> / <c>TENANT_ID</c>) — and worked only because the constructor
    /// silently fell through to <c>DefaultAzureCredential</c>. Task 010 replaced that silent fallback
    /// with fail-fast validation, deliberately, since credential-selection-by-accident is the exact
    /// defect FR-A1 exists to fix; 13 tests failed. Setting the managed-identity flag fixed it, but
    /// bound the stub to the branch tasks 020/022/033 are about to rewrite. Injecting a credential
    /// decouples it from both branches permanently. Code-review finding W-6.</para>
    /// </summary>
    private sealed class NoOpCredential : TokenCredential
    {
        public static readonly NoOpCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "StubDataverseWebApiClient issues no HTTP; if this is reached, a virtual seam was left unoverridden.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException(
                "StubDataverseWebApiClient issues no HTTP; if this is reached, a virtual seam was left unoverridden.");
    }

    /// <summary>JSON array returned from the next <see cref="QueryAsync{T}"/> (deserialized to List&lt;T&gt;).</summary>
    public string ContactQueryResult { get; set; } = "[]";
    public List<string> CreatedEntitySets { get; } = new();
    public List<string> ContactUpdates { get; } = new();

    /// <summary>Every entity set queried (task 138: a refused invite must not even look the Contact up).</summary>
    public List<string> QueriedEntitySets { get; } = new();

    /// <summary>Every CREATE with its payload, so a test can read what was written (task 097: the expiry).</summary>
    public List<(string EntitySet, object Payload)> Creates { get; } = new();

    public void Reset()
    {
        ContactQueryResult = "[]";
        CreatedEntitySets.Clear();
        ContactUpdates.Clear();
        QueriedEntitySets.Clear();
        Creates.Clear();
    }

    public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
        int? top = null, int? skip = null, CancellationToken cancellationToken = default)
    {
        QueriedEntitySets.Add(entitySetName);
        return Task.FromResult(JsonSerializer.Deserialize<List<T>>(ContactQueryResult) ?? new List<T>());
    }

    public override Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
    {
        CreatedEntitySets.Add(entitySetName);
        Creates.Add((entitySetName, entity));
        return Task.FromResult(Guid.NewGuid());
    }

    public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
    {
        if (entitySetName == "contacts") ContactUpdates.Add(id.ToString());
        return Task.CompletedTask;
    }
}

/// <summary>
/// The identity store as the CIAM contract tests need it (task 141): a CIAM caller's contact is driven by the
/// <c>X-Test-Contact</c> header — "none" ⇒ no contact bound to the caller's oid; a GUID ⇒ that contact, active
/// and bound to the caller's oid on the External plane; absent ⇒ a fresh contact. Everything else (the invite
/// path's email lookup, references, flags) is the shared in-memory store.
/// </summary>
internal sealed class HeaderDrivenIdentityStore : IContactIdentityStore
{
    private readonly InMemoryContactIdentityStore _inner;
    private readonly IHttpContextAccessor _accessor;

    public HeaderDrivenIdentityStore(InMemoryContactIdentityStore inner, IHttpContextAccessor accessor)
    {
        _inner = inner;
        _accessor = accessor;
    }

    public Task<ContactLookup> FindContactsByOidAsync(Guid oid, CancellationToken ct)
    {
        var header = _accessor.HttpContext?.Request.Headers.TryGetValue("X-Test-Contact", out var v) == true
            ? v.ToString()
            : null;
        if (header is null)
        {
            return _inner.FindContactsByOidAsync(oid, ct);
        }

        if (string.Equals(header, "none", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(ContactLookup.Of());
        }

        var id = Guid.TryParse(header, out var g) ? g : Guid.NewGuid();
        return Task.FromResult(ContactLookup.Of(
            new ContactBindingRow(id, 0, oid.ToString("D"), (int)IdentityPlaneMarker.External)));
    }

    public Task<ContactLookup> FindContactsByKeyMirrorAsync(Guid oid, CancellationToken ct) => _inner.FindContactsByKeyMirrorAsync(oid, ct);
    public Task<ContactLookup> FindActiveContactsByEmailAsync(string email, CancellationToken ct) => _inner.FindActiveContactsByEmailAsync(email, ct);
    public Task<ContactLookup> GetContactAsync(Guid contactId, CancellationToken ct) => _inner.GetContactAsync(contactId, ct);
    public Task<ReferenceLookup> FindSystemUsersLinkingAsync(IReadOnlyCollection<Guid> contactIds, CancellationToken ct) => _inner.FindSystemUsersLinkingAsync(contactIds, ct);
    public Task<SystemUserLookup> GetSystemUserAsync(Guid systemUserId, CancellationToken ct) => _inner.GetSystemUserAsync(systemUserId, ct);
    public Task<BindingReadability> ProbeBindingReadabilityAsync(CancellationToken ct) => _inner.ProbeBindingReadabilityAsync(ct);
    public Task<StoreWriteResult> BindOidAsync(Guid contactId, string? etag, Guid oid, IdentityPlaneMarker plane, CancellationToken ct) => _inner.BindOidAsync(contactId, etag, oid, plane, ct);
    public Task<StoreWriteResult> CreateContactForOidAsync(Guid oid, IdentityPlaneMarker plane, NewContactDetails details, CancellationToken ct) => _inner.CreateContactForOidAsync(oid, plane, details, ct);
    public Task<StoreWriteResult> SetPrimaryContactAsync(Guid systemUserId, string? etag, Guid contactId, CancellationToken ct) => _inner.SetPrimaryContactAsync(systemUserId, etag, contactId, ct);
    public Task<StoreWriteResult> WriteCollisionFlagAsync(Guid contactId, CollisionFlag flag, string? etag, CancellationToken ct) => _inner.WriteCollisionFlagAsync(contactId, flag, etag, ct);
    public Task<StoreWriteResult> ClearCollisionFlagAsync(Guid contactId, string? etag, CancellationToken ct) => _inner.ClearCollisionFlagAsync(contactId, etag, ct);
    public Task<StorePage<SystemUserIdentityRow>> ScanInteractiveSystemUsersAsync(string? continuation, CancellationToken ct) => _inner.ScanInteractiveSystemUsersAsync(continuation, ct);
    public Task<StorePage<ContactBindingRow>> ScanFlaggedContactsAsync(string? continuation, CancellationToken ct) => _inner.ScanFlaggedContactsAsync(continuation, ct);
}
