// Task 103 (E2E-R4) — Word-shuttle POLL-path vertical-slice contract test (KEEP path:
// endpoint-contract). THE anti-recurrence forcing function for Cluster 3.
//
// WHY THIS FILE EXISTS (non-waivable per project CLAUDE.md §"E2E Definition-of-Done" +
// notes/e2e-gap-register.md Cluster 3):
//   The Word round-trip endpoints (push 050, pull 051, webhook+poll check-changes 053, reanchor
//   054) + the 054 reanchor banner/panel were all BUILT + unit-green but NONE were connected:
//   "Push to Word" had no client trigger (3.1), EnsureSubscriptionAsync had zero callers (3.2),
//   pull-annotations had no caller (3.4), and the reanchor UI + poll check-changes were never
//   mounted (3.5). Every layer unit-passed while the return-from-Word slice was E2E-inert — that
//   is exactly how the false-green shipped.
//
//   This test drives the POLL path — check-changes → reanchor — through the REAL routes with the
//   REAL SpeSyncOrchestrator (the Redis-backed delta/etag substrate) + the REAL
//   AnnotationReanchorService (the deterministic scoring engine), mocking ONLY the external SPE
//   boundary (ISpeFileOperations: delta enumeration + document download). A broken wire between the
//   endpoint and the orchestrator/service fails the build — a service-only unit test would NOT
//   catch it. It asserts a detected change RE-ANCHORS: the poll reports Changed=true and the
//   reanchor route bands the prior anchor against the updated document.
//
//   The WEBHOOK-DELIVERY leg is ✅◐ E2E-pending on task 056 (the
//   Compose:Webhook:{SigningKey,ClientState,NotificationUrl} Key Vault secrets / DEF-03) — it is
//   NOT exercised here and is NOT testable in-process without those secrets. The POLL fallback
//   needs no secrets and is the forcing-function path. The subscription-origin-call wiring (gap
//   3.2) is proven by <see cref="Load_OnExistingDocument_FiresSubscriptionOriginCall_TracksContainer"/>.
//
// KEEP-path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract` · Path: `tests/integration/contract/Api/Ai/**`.
//   - "Every changed endpoint contract => >=1 integration test": anchors the poll (check-changes)
//     + reanchor (reanchor-annotations) contracts AND the Load subscription-origin-call wiring.
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; the
// SpeSyncOrchestrator + AnnotationReanchorService + ComposeService under test are REAL; mocks live
// ONLY at the SPE (ISpeFileOperations) / Dataverse / indexing module boundaries. Assertions are
// HTTP-observable + persisted side-effects.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Through-the-wire (anti-recurrence) contract tests for the Word round-trip POLL path (task 103,
/// Cluster 3). Boots the BFF in-process with the REAL <see cref="SpeSyncOrchestrator"/> +
/// <see cref="AnnotationReanchorService"/> (+ <see cref="ComposeService"/> for the Load origin call)
/// and only the external SPE / Dataverse / indexing boundaries mocked, and drives
/// check-changes → reanchor through the real routes.
/// </summary>
public sealed class ComposeWordShuttlePollEndpointContractTests
    : IClassFixture<ComposeWordShuttlePollFixture>
{
    private readonly ComposeWordShuttlePollFixture _fixture;

    public ComposeWordShuttlePollEndpointContractTests(ComposeWordShuttlePollFixture fixture)
    {
        _fixture = fixture;
    }

    private const string TenantId = "tenant-word-shuttle-001";

    // Spike-6 style contract paragraphs — the re-anchor scoring corpus in the updated Word doc.
    private static readonly string[] UpdatedParagraphs =
    {
        "This Master Services Agreement is entered into between the parties in the signature block.",
        "Indemnification is capped at fees paid by Client in the twelve months preceding the claim.",
        "Each Party shall perform its obligations in a professional and workmanlike manner.",
        "Termination requires thirty (30) days written notice to the other Party's contact.",
    };

    // The prior anchor's textPattern — byte-identical to UpdatedParagraphs[1] so it re-anchors AUTO.
    private const string PriorAnchorText =
        "Indemnification is capped at fees paid by Client in the twelve months preceding the claim.";

    /// <summary>Builds a minimal valid DOCX package whose paragraphs are the re-anchor corpus.</summary>
    private static byte[] BuildDocx(IReadOnlyList<string> paragraphs)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            foreach (var text in paragraphs)
            {
                body.AppendChild(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    /// <summary>Arranges the SPE delta boundary to report ONE changed item for the container, and the
    /// download boundary to return the updated DOCX bytes.</summary>
    private void SetupChangedDocument(string driveId, string documentSpeId, byte[] docxBytes)
    {
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string c, CancellationToken _) => c); // a `b!` drive id resolves to itself
        ArrangeCallerCanSee(driveId, documentSpeId);

        _fixture.SpeMock
            .Setup(s => s.EnumerateDriveDeltaAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeDeltaResult(
                new[] { new SpeDriveChange(ItemId: documentSpeId, Name: "contract.docx", ETag: "\"v2-updated\"", Deleted: false) },
                DeltaLink: "delta-token-1"));

        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(docxBytes.ToArray()));
    }

    /// <summary>
    /// uac-r2 task 166 (S-65): check-changes is authorized by an OBO metadata read of the named item in the named
    /// container BEFORE the app-only delta. This arranges SPE answering that read for THIS item in THIS drive only —
    /// any other item or drive answers null (Graph's "not visible to you").
    /// </summary>
    private void ArrangeCallerCanSee(string driveId, string documentSpeId)
    {
        var now = DateTimeOffset.UtcNow;
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, documentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: documentSpeId, Name: "contract.docx", ParentId: null, Size: 64,
                CreatedDateTime: now, LastModifiedDateTime: now, ETag: "\"v1\"",
                IsFolder: false, WebUrl: null, DriveId: driveId));
    }

    private static object BuildReanchorBody(string driveId) => new
    {
        driveId,
        tenantId = TenantId,
        priorAnchors = new[]
        {
            new { id = "anchor-1", type = "comment", textPattern = PriorAnchorText, paragraphHint = 1, preview = "Reviewer note" },
        },
    };

    // ─────────────────────────────────────────────────────────────────────────
    // 1. THE forcing function: poll detects the change → reanchor bands the prior anchor.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PollThenReanchor_WhenDocumentChangedInWord_DetectsChangeAndReanchorsPriorAnchor()
    {
        const string driveId = "b!word-shuttle-poll-001";
        const string documentSpeId = "spe-item-poll-001";
        _fixture.ResetBoundaries();
        SetupChangedDocument(driveId, documentSpeId, BuildDocx(UpdatedParagraphs));

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act 1: POLL — check-changes through the REAL route + REAL SpeSyncOrchestrator ──────────
        var checkResponse = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/check-changes",
            new { containerId = driveId });

        checkResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the poll fallback drives the real orchestrator's Redis-backed delta substrate over the real wire");
        var check = await checkResponse.Content.ReadFromJsonAsync<CheckChangesWire>();
        check!.Changed.Should().BeTrue(
            "the SPE delta enumeration surfaced a net change for this document — the poll DETECTED the Word save");
        check.DocumentSpeId.Should().Be(documentSpeId);

        // ── Act 2: RE-ANCHOR — the detected change drives the reanchor route (REAL scoring engine) ─
        var reanchorResponse = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/reanchor-annotations",
            BuildReanchorBody(driveId));

        reanchorResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a detected change re-anchors the prior Compose anchors against the updated document");
        var reanchor = await reanchorResponse.Content.ReadFromJsonAsync<ReanchorWire>();
        reanchor!.Summary.Total.Should().Be(1, "the one prior anchor was scored");
        (reanchor.Summary.AutoCount + reanchor.Summary.ReviewCount).Should().BeGreaterThan(0,
            "the prior anchor's textPattern matches a paragraph in the updated document — it RE-ANCHORS (not orphaned)");
        reanchor.Summary.Annotations.Should().ContainSingle(a => a.Id == "anchor-1" && a.MatchedParagraphIndex >= 0,
            "the re-anchored annotation landed on a concrete paragraph in the updated document");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2. NEGATIVE CONTRAST — no delta change ⇒ the poll reports Changed=false (the detection is
    //    load-bearing, not incidental).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Poll_WhenNoDeltaChange_ReportsNotChanged()
    {
        const string driveId = "b!word-shuttle-poll-002";
        const string documentSpeId = "spe-item-poll-002";
        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string c, CancellationToken _) => c);
        ArrangeCallerCanSee(driveId, documentSpeId);
        _fixture.SpeMock
            .Setup(s => s.EnumerateDriveDeltaAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeDeltaResult(Array.Empty<SpeDriveChange>(), DeltaLink: "delta-token-empty"));

        using var client = _fixture.CreateAuthenticatedClient();

        var checkResponse = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/check-changes",
            new { containerId = driveId });

        checkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var check = await checkResponse.Content.ReadFromJsonAsync<CheckChangesWire>();
        check!.Changed.Should().BeFalse(
            "no net SPE delta ⇒ the poll reports no change — proving Changed=true in the positive test is load-bearing");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2b. uac-r2 task 166 (S-65) — check-changes is authorized BEFORE the app-only delta runs.
    //
    // EnumerateChangesAsync runs an APP-ONLY delta over the container the body names and creates/advances the
    // SHARED per-container delta + eTag state. A caller who cannot see the named item gets ONE 404, and the
    // delta never runs, so no state is created or advanced for them.
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> InvisibleShapes => new() { "not-visible", "visibility-read-throws", "container-unresolvable" };

    [Theory]
    [MemberData(nameof(InvisibleShapes))]
    public async Task Poll_WhenTheCallerCannotSeeTheItem_Returns404_AndNeverRunsTheDeltaOrTouchesState(string shape)
    {
        var driveId = $"b!word-shuttle-deny-{shape}";
        const string documentSpeId = "spe-item-someone-elses";
        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string c, CancellationToken _) => shape == "container-unresolvable" ? null! : c);
        if (shape == "visibility-read-throws")
        {
            _fixture.SpeMock
                .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Graph 403 accessDenied"));
        }
        // "not-visible": GetFileMetadataAsUserAsync is not arranged — the loose mock answers null, Graph's
        // "you cannot see this item".
        _fixture.SpeMock
            .Setup(s => s.EnumerateDriveDeltaAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpeDeltaResult(
                new[] { new SpeDriveChange(ItemId: documentSpeId, Name: "secret-filename.docx", ETag: "\"v9\"", Deleted: false) },
                DeltaLink: "delta-token-x"));

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/check-changes", new { containerId = driveId });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(Sprk.Bff.Api.Api.ComposeSyncEndpoints.DocumentNotVisibleReasonCode);
        body.Should().NotContain("secret-filename").And.NotContain(documentSpeId);
        _fixture.SpeMock.Verify(
            s => s.EnumerateDriveDeltaAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "the app-only delta must not run for a caller who cannot see the item");

        using var scope = _fixture.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<SpeSyncOrchestrator>();
        (await orchestrator.GetStateAsync(driveId, CancellationToken.None)).Should().BeNull(
            "no per-container delta / eTag state may be created or advanced for a refused caller");
    }

    [Fact]
    public async Task Poll_InvisibleAndThrowingVisibilityRead_AreTheSameResponse()
    {
        _fixture.ResetBoundaries();
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string c, CancellationToken _) => c);
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), "b!throws", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Graph 403 accessDenied"));

        using var client = _fixture.CreateAuthenticatedClient();

        var invisible = await client.PostAsJsonAsync("/api/compose/document/spe-a/check-changes", new { containerId = "b!invisible" });
        var throwing = await client.PostAsJsonAsync("/api/compose/document/spe-a/check-changes", new { containerId = "b!throws" });

        invisible.StatusCode.Should().Be(HttpStatusCode.NotFound);
        throwing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Normalize(await invisible.Content.ReadAsStringAsync()).Should().Be(Normalize(await throwing.Content.ReadAsStringAsync()));
    }

    /// <summary>
    /// S-65, both outcomes in one test (the route ledger's "ProvenByTest" credit): the OBO metadata read of the named
    /// item in the named container AS THE CALLER precedes the app-only delta. An item that read cannot see is the
    /// uniform 404 and the delta never runs; a visible item is 200 and the delta runs for it.
    /// </summary>
    [Fact]
    public async Task ProvenByTest_CheckChanges_AnItemTheCallerCannotSeeIs404_AVisibleItemIs200()
    {
        const string deniedDriveId = "b!word-shuttle-proof-denied";
        const string allowedDriveId = "b!word-shuttle-proof-allowed";
        const string invisibleItem = "spe-item-proof-invisible";
        const string visibleItem = "spe-item-proof-visible";
        _fixture.ResetBoundaries();
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string c, CancellationToken _) => c);
        ArrangeCallerCanSee(allowedDriveId, visibleItem); // the invisible item is never arranged: the OBO read answers null
        _fixture.SpeMock
            .Setup(s => s.EnumerateDriveDeltaAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string drive, string? _, CancellationToken _) => new SpeDeltaResult(
                new[] { new SpeDriveChange(ItemId: drive == allowedDriveId ? visibleItem : invisibleItem, Name: "contract.docx", ETag: "\"v2\"", Deleted: false) },
                DeltaLink: "delta-token-proof"));
        using var client = _fixture.CreateAuthenticatedClient();

        var denied = await client.PostAsJsonAsync(
            $"/api/compose/document/{invisibleItem}/check-changes", new { containerId = deniedDriveId });

        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await denied.Content.ReadAsStringAsync()).Should().Contain(Sprk.Bff.Api.Api.ComposeSyncEndpoints.DocumentNotVisibleReasonCode);
        _fixture.SpeMock.Verify(
            s => s.EnumerateDriveDeltaAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "the app-only delta must not run for an item the caller cannot see");

        var allowed = await client.PostAsJsonAsync(
            $"/api/compose/document/{visibleItem}/check-changes", new { containerId = allowedDriveId });

        allowed.StatusCode.Should().Be(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync());
        var check = await allowed.Content.ReadFromJsonAsync<CheckChangesWire>();
        check!.DocumentSpeId.Should().Be(visibleItem);
        check.Changed.Should().BeTrue("the delta ran for the visible item and surfaced its change");
        _fixture.SpeMock.Verify(
            s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), deniedDriveId, invisibleItem, It.IsAny<CancellationToken>()),
            Times.Once, "the refusal came from the caller's own OBO read of that item in that container");
    }

    private static string Normalize(string problemJson)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(problemJson)!.AsObject();
        node.Remove("correlationId");
        node.Remove("traceId");
        return node.ToJsonString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3. Gap 3.2 — the subscription ORIGIN CALL fires on document load: opening a document tracks
    //    its container in the orchestrator so a return-from-Word change is later detectable (and the
    //    renewal service no longer renews an empty set). The webhook-DELIVERY leg stays ✅◐
    //    E2E-pending on task 056; this proves the ORIGIN CALL wiring (no secrets needed).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Load_OnExistingDocument_FiresSubscriptionOriginCall_TracksContainer()
    {
        const string driveId = "b!word-shuttle-load-003";
        const string documentSpeId = "spe-item-load-003";
        _fixture.ResetBoundaries();

        // SPE Load boundary (metadata + content) + the origin call's drive resolution.
        var now = DateTimeOffset.UtcNow;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string c, CancellationToken _) => c);
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: documentSpeId, Name: "contract.docx", ParentId: null, Size: 64,
                CreatedDateTime: now, LastModifiedDateTime: now, ETag: "\"v1\"",
                IsFolder: false, WebUrl: null, DriveId: driveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(BuildDocx(UpdatedParagraphs)));

        using var client = _fixture.CreateAuthenticatedClient();

        var loadResponse = await client.GetAsync(
            $"/api/compose/documents/{documentSpeId}?driveId={driveId}&tenantId={TenantId}");

        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK, "the document loads over the real wire");

        // The origin call (EnsureSubscriptionAsync) ran on Load: the container is now TRACKED in the
        // orchestrator's Redis state. Without webhook secrets it degraded to poll-fallback — which is
        // exactly the state the poll path needs (and stops the renewal sweep seeing an empty set).
        using var scope = _fixture.Services.CreateScope();
        var orchestrator = scope.ServiceProvider.GetRequiredService<SpeSyncOrchestrator>();
        var state = await orchestrator.GetStateAsync(driveId, CancellationToken.None);

        state.Should().NotBeNull(
            "opening the document fired the EnsureSubscriptionAsync origin call (gap 3.2) — the container is tracked");
        state!.FallbackToPolling.Should().BeTrue(
            "with no webhook secrets (task 056 / DEF-03) the origin call degrades to poll-fallback — the webhook-DELIVERY leg is ✅◐ E2E-pending on 056");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 3b. uac-r2 task 166 r1 (S-80 family, F6) — pull-annotations and reanchor-annotations take the tenant from
    //     the CALLER's claim. The body's tenantId is obsolete: a body without it is accepted, and a token without a
    //     tid claim is refused with 401 BEFORE any SPE download. (The verifier seeded the 401's removal and nothing
    //     went red — these are the tests that now notice.)
    // ─────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> AnnotationRoutes => new() { "pull-annotations", "reanchor-annotations" };

    private static object AnnotationBodyWithoutTenant(string route, string driveId) => route == "pull-annotations"
        ? (object)new { driveId }
        : new
        {
            driveId,
            priorAnchors = new[]
            {
                new { id = "anchor-1", type = "comment", textPattern = PriorAnchorText, paragraphHint = 1, preview = "Reviewer note" },
            },
        };

    [Theory]
    [MemberData(nameof(AnnotationRoutes))]
    public async Task AnnotationRoute_ABodyWithNoTenantId_IsAccepted(string route)
    {
        const string driveId = "b!word-shuttle-annotations-166";
        const string documentSpeId = "spe-item-annotations-166";
        _fixture.ResetBoundaries();
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, documentSpeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(BuildDocx(UpdatedParagraphs)));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/{route}", AnnotationBodyWithoutTenant(route, driveId));

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "tenantId is obsolete on the body — no longer required, never read (the tenant is the tid claim)");
    }

    [Theory]
    [MemberData(nameof(AnnotationRoutes))]
    public async Task AnnotationRoute_ATokenWithNoTidClaim_Is401_AndDownloadsNothing(string route)
    {
        const string driveId = "b!word-shuttle-annotations-notid";
        const string documentSpeId = "spe-item-annotations-notid";
        _fixture.ResetBoundaries();
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(BuildDocx(UpdatedParagraphs)));

        using var client = _fixture.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Add(WordShuttlePollFakeAuthHandler.OmitTidHeader, "1");
        var response = await client.PostAsJsonAsync(
            $"/api/compose/document/{documentSpeId}/{route}",
            new
            {
                driveId,
                tenantId = TenantId, // a body tenant does NOT stand in for the missing claim
                priorAnchors = Array.Empty<object>(),
            });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _fixture.SpeMock.Verify(
            s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "no SPE download happens for a caller whose tenant cannot be established");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 4. Auth — the poll route inherits RequireAuthorization() from the /api/compose group.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Poll_WhenUnauthenticated_Returns401()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateUnauthenticatedClient();

        using var content = JsonContent.Create(new { containerId = "b!x" });
        var response = await client.PostAsync("/api/compose/document/spe-x/check-changes", content);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the check-changes route inherits RequireAuthorization() from the /api/compose group (ADR-008 + ADR-028)");
    }

    // ── Minimal wire mirrors (deserialization only) ──────────────────────────────────────────────
    private sealed record CheckChangesWire(string DocumentSpeId, string ContainerId, bool Changed, bool Deleted);
    private sealed record ReanchorWire(string DocumentSpeId, ReanchorSummaryWire Summary);
    private sealed record ReanchorSummaryWire(int Total, int AutoCount, int ReviewCount, int OrphanCount, IReadOnlyList<ReanchorAnnWire> Annotations);
    private sealed record ReanchorAnnWire(string Id, string Band, int MatchedParagraphIndex);
}

/// <summary>
/// In-process BFF fixture that keeps the REAL <see cref="SpeSyncOrchestrator"/> +
/// <see cref="AnnotationReanchorService"/> (+ <see cref="ComposeService"/> / <see cref="ChatSessionManager"/>
/// for the Load origin call) and replaces only the external SPE / Dataverse / indexing boundaries with
/// Moqs, so the endpoint→orchestrator/service wire is genuinely exercised. Config-key set mirrors
/// <c>ComposeMemoryResumeFixture</c> (task 102; bff-extensions.md §F.2 Fixture-Config-FIRST). Note:
/// NO <c>Compose:Webhook:*</c> keys are set — the webhook-delivery leg is ✅◐ E2E-pending on task 056,
/// and their ABSENCE is exactly what exercises the poll-fallback path (gap 3.2 origin call degrades).
/// </summary>
public sealed class ComposeWordShuttlePollFixture : WebApplicationFactory<Program>
{
    public Mock<ISpeFileOperations> SpeMock { get; } = new(MockBehavior.Loose);
    public Mock<IGenericEntityService> DataverseMock { get; } = new(MockBehavior.Loose);
    public Mock<IPostUploadIndexingEnqueuer> IndexingMock { get; } = new(MockBehavior.Loose);

    public void ResetBoundaries()
    {
        SpeMock.Reset();
        DataverseMock.Reset();
        IndexingMock.Reset();
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
                ["Analysis:Enabled"] = "true",
                ["Analysis:UseStubResolver"] = "true",
                ["DocumentIntelligence:AiSearchEndpoint"] = "https://test.search.windows.net",
                ["DocumentIntelligence:AiSearchKey"] = "test-search-key",
                ["OfficeRateLimit:Enabled"] = "false",
                ["Redis:Enabled"] = "false",
                ["Redis:AllowInMemoryFallback"] = "true",
                ["ModelSelector:DefaultModel"] = "gpt-4o",
                ["AzureOpenAI:Endpoint"] = "https://test.openai.azure.com/",
                ["AzureOpenAI:ChatModelName"] = "gpt-4o",
                ["DocumentIntelligence:RecordMatchingEnabled"] = "true",
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

            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = WordShuttlePollFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = WordShuttlePollFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, WordShuttlePollFakeAuthHandler>(
                WordShuttlePollFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = WordShuttlePollFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = WordShuttlePollFakeAuthHandler.SchemeName;
            });

            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            services.RemoveAll<IHostedService>();

            var dataverseServiceMock = new Mock<IDataverseService>();
            dataverseServiceMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseServiceMock.Object);

            // Cold ChatSession tier → null (Redis miss → Cosmos miss → Dataverse null), so a Load
            // mints a fresh in-memory hot session cleanly (mirrors ComposeMemoryResumeFixture).
            var chatRepoMock = new Mock<IChatDataverseRepository>();
            chatRepoMock
                .Setup(r => r.GetSessionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ChatSession?)null);
            services.RemoveAll<IChatDataverseRepository>();
            services.AddSingleton(chatRepoMock.Object);

            // KEEP the real SpeSyncOrchestrator + AnnotationReanchorService + ComposeService; mock
            // ONLY the external SPE / Dataverse-entity / indexing boundaries they depend on.
            services.RemoveAll<ISpeFileOperations>();
            services.AddSingleton(SpeMock.Object);

            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(DataverseMock.Object);

            services.RemoveAll<IPostUploadIndexingEnqueuer>();
            services.AddSingleton(IndexingMock.Object);
        });
    }

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>Fake auth handler authenticating any request carrying an <c>Authorization</c> header.</summary>
internal sealed class WordShuttlePollFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "WordShuttlePollFakeAuth";

    /// <summary>uac-r2 task 166 r1: a request carrying this header authenticates WITHOUT a tid claim — an Entra
    /// principal whose tenant cannot be established.</summary>
    public const string OmitTidHeader = "X-Test-Omit-Tid";

    public WordShuttlePollFakeAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return Task.FromResult(AuthenticateResult.Fail("No Authorization header"));
        }

        // Issue #863 (fixture repair, bff-extensions.md §F.2): a STABLE oid. This minted a
        // fresh one per request, which Entra never does — every call arrived as a different
        // user, so the suite silently exercised cross-user access on every request.
        var oid = TestSessionOwner.Oid;
        var claims = new List<Claim>
        {
            new("oid", oid),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Word-Shuttle Test User {oid}"),
        };
        if (!Request.Headers.ContainsKey(OmitTidHeader))
        {
            claims.Add(new("tid", "tenant-word-shuttle-001"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
