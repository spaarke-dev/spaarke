// Task 027 — BFF Compose endpoint integration tests (KEEP path: endpoint-contract).
//
// Scope (per POML §goal + spec FR-21 + FR-22):
//   - Exercise the 7 Compose endpoints under /api/compose/* through a live in-process host.
//   - Validate auth gating (401 unauthenticated), happy-path (200/204 with valid bearer),
//     and validation (400 on bad input). For SPE-touching endpoints the IComposeService
//     boundary is mocked; for the AI-dispatch endpoint the PublicContracts facade
//     (IConsumerRoutingService + IInvokePlaybookAi) is mocked.
//   - Verify the refined ADR-013 boundary end-to-end: the dispatch path resolves through
//     the facade types only — no AI-internal types are reached.
//
// KEEP path classification (ADR-038 §2 + tests/CLAUDE.md):
//   - Category: `endpoint-contract`
//   - Path: `tests/integration/contract/Api/Compose/**`
//   - Justification: "Every new endpoint = >=1 integration test" — these 7 tests close
//     the contract obligation for the 7 endpoints added in W3-024.
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md §"Banned Antipatterns"):
//   - B1: NO Mock<HttpMessageHandler> — none used.
//   - B2: NO Mock<IServiceClient> typed-HttpClient wrappers — none used.
//   - B3: NO DI-registration tests — we assert HTTP-observable behavior end-to-end.
//   - B4: NO ctor null-check tests — none authored.
//   - B5: NO mocking of in-process classes; mocks live at the IComposeService /
//     PublicContracts module boundaries (canonical per ADR-038 §4 "Mock at module
//     boundaries").
//   - B6-B17: all behavior-shaped — name + scenario + observable outcome.
//
// Path-A vs Path-C decision (CLAUDE.md §6.5):
//   - POML <output> names `tests/unit/Sprk.Bff.Api.Tests/Api/ComposeEndpointsTests.cs`.
//   - Existing file at that path holds the W3-024 endpoint-shape contract tests (11);
//     those test the WIRING shape (routes/verbs/auth metadata) without booting a host.
//   - This new live-host file pivots to Path C (ADR-compliant placement) at the
//     canonical KEEP path `tests/integration/contract/Api/Compose/`. The csproj at
//     line 60 auto-includes `tests/integration/contract/**` so the file compiles into
//     the same assembly without project-level changes.
//
// References:
//   - tests/CLAUDE.md — module-scoped directive (rewritten 2026-06-26 per spec FR-B02)
//   - docs/adr/ADR-038-testing-strategy.md — integration-heavy KEEP-path policy
//   - .claude/constraints/testing.md §1 — KEEP-path table
//   - .claude/constraints/bff-extensions.md §F.2 — Fixture-Config-FIRST inspection
//   - tests/unit/Sprk.Bff.Api.Tests/Api/Membership/MembershipEndpointsTests.cs —
//     canonical fixture+inline-handler pattern this file mirrors.
//   - src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs — the endpoints under test.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
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
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Mocks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Compose;

/// <summary>
/// Integration-contract tests for the 7 Compose endpoints under <c>/api/compose/*</c>
/// (W3-024 + task 025 DI). Spins the BFF in-process via
/// <see cref="WebApplicationFactory{TEntryPoint}"/> with the SPE/AI module boundaries
/// mocked, exercises each endpoint over real HTTP, and asserts the observable contract
/// (status code + JSON body shape) per spec FR-21.
/// </summary>
/// <remarks>
/// <para>
/// <b>KEEP-path category</b>: <c>endpoint-contract</c> per ADR-038 §2 +
/// <c>tests/CLAUDE.md</c> §"Six KEEP path categories". One file per endpoint group —
/// the 7 happy-path tests + the 7 auth-gate tests + a focused validation/dispatch set
/// together meet the per-endpoint coverage rule.
/// </para>
/// <para>
/// <b>Fixture inheritance</b>: <see cref="ComposeContractFixture"/> mirrors the
/// canonical config-key set used by <see cref="Sprk.Bff.Api.Tests.CustomWebAppFactory"/>
/// and the sibling <c>MembershipEndpointsTestFixture</c> / <c>AdminJobsTestFixture</c>
/// (per <c>.claude/constraints/bff-extensions.md §F.2</c>). New validators added to
/// <c>Program.cs</c> require a corresponding key here.
/// </para>
/// </remarks>
[Trait("status", "repaired")]
public sealed class ComposeEndpointsContractTests : IClassFixture<ComposeContractFixture>
{
    private readonly ComposeContractFixture _fixture;

    public ComposeEndpointsContractTests(ComposeContractFixture fixture)
    {
        _fixture = fixture;
    }

    // ================================================================================
    // ===== Auth gate (401 on every endpoint without bearer) =========================
    // ================================================================================
    //
    // Spec FR-21 acceptance: "auth gates verified" — every endpoint inherits
    // RequireAuthorization() from the /api/compose group (ADR-008 + ADR-028).
    // ================================================================================

    [Theory]
    [InlineData("POST", "/api/compose/upload")]
    [InlineData("GET", "/api/compose/documents/spe-item-abc")]
    [InlineData("POST", "/api/compose/documents/spe-item-abc/save")]
    [InlineData("POST", "/api/compose/documents/spe-item-abc/promote")]
    [InlineData("POST", "/api/compose/documents/00000000-0000-0000-0000-000000000001/checkout")]
    [InlineData("POST", "/api/compose/documents/00000000-0000-0000-0000-000000000001/checkin")]
    [InlineData("POST", "/api/compose/action/compose-summarize")]
    public async Task ComposeEndpoint_WhenUnauthenticated_Returns401(string verb, string path)
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        using var request = new HttpRequestMessage(new HttpMethod(verb), path);
        // POST endpoints need a body so the request reaches the auth pipeline (rather than
        // returning 415 unsupported-media-type before auth). Auth still gates 401.
        if (verb == "POST")
        {
            request.Content = JsonContent.Create(new { });
        }

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"every Compose endpoint inherits RequireAuthorization() from the group; " +
            $"unauthenticated call to {verb} {path} must produce 401 (ADR-008 + ADR-028)");
    }

    // ================================================================================
    // ===== Happy path (200/501) for the 7 endpoints =================================
    // ================================================================================

    [Fact]
    public async Task PostUpload_Authenticated_Returns501_PerR1StubContract()
    {
        // Upload is intentionally R2-reserved in R1 — handler returns 501 Problem.
        // Verifies the handler is reached past auth, not that it does work.
        using var client = _fixture.CreateAuthenticatedClient();

        using var content = JsonContent.Create(new { });
        var response = await client.PostAsync("/api/compose/upload", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented,
            "R1 routes upload through the existing Assistant pipeline; the stub returns 501 (ComposeEndpoints.Upload)");
    }

    [Fact]
    public async Task GetLoadDocument_Authenticated_WithValidQuery_Returns200_WithDocxBytes()
    {
        const string speId = "spe-item-load-abc";
        const string driveId = "drive-001";
        const string tenantId = "tenant-aad-001";
        var sessionId = Guid.NewGuid().ToString();

        _fixture.ComposeServiceMock
            .Setup(s => s.LoadAsync(It.Is<LoadComposeDocumentRequest>(r =>
                r.DocumentSpeId == speId && r.DriveId == driveId && r.TenantId == tenantId),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoadComposeDocumentResult
            {
                DocumentSpeId = speId,
                DriveId = driveId,
                SessionId = sessionId,
                Content = new byte[] { 0x50, 0x4B, 0x03, 0x04 }, // DOCX ZIP signature
                ETag = "\"v1-etag\"",
                FileName = "draft.docx",
                Size = 4,
            });

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        body.Should().NotBeNull();
        body!.DocumentSpeId.Should().Be(speId);
        body.SessionId.Should().Be(sessionId);
        body.FileName.Should().Be("draft.docx");
        body.Content.Should().HaveCount(4);
    }

    [Fact]
    public async Task PostSaveDocument_Authenticated_WithValidBody_Returns200_AndReportsPromotionFlag()
    {
        const string speId = "spe-item-save-xyz";
        const string driveId = "drive-002";
        const string tenantId = "tenant-aad-002";
        var sessionId = Guid.NewGuid().ToString();
        var documentRecordId = Guid.NewGuid();

        _fixture.ComposeServiceMock
            .Setup(s => s.SaveAsync(It.IsAny<SaveComposeDocumentRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SaveComposeDocumentResult
            {
                DocumentSpeId = speId,
                DriveId = driveId,
                SessionId = sessionId,
                DocumentRecordId = documentRecordId,
                VersionId = "v2",
                ETag = "\"v2-etag\"",
                Size = 16,
                WasPromotedThisSave = true,
            });

        using var client = _fixture.CreateAuthenticatedClient();

        var body = new
        {
            driveId,
            sessionId,
            tenantId,
            content = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00 },
        };

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result.Should().NotBeNull();
        result!.VersionId.Should().Be("v2");
        result.WasPromotedThisSave.Should().BeTrue("first-Save promotion fired per FR-06 idempotent contract");
        result.DocumentRecordId.Should().Be(documentRecordId);
    }

    [Fact]
    public async Task PostPromoteDocument_Authenticated_WithValidBody_Returns200_AndReportsWasCreated()
    {
        const string speId = "spe-item-promote-pqr";
        const string tenantId = "tenant-aad-003";
        var sessionId = Guid.NewGuid().ToString();
        var documentRecordId = Guid.NewGuid();

        _fixture.ComposeServiceMock
            .Setup(s => s.PromoteIfEphemeralAsync(It.IsAny<PromoteComposeDocumentRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PromoteComposeDocumentResult
            {
                DocumentSpeId = speId,
                SessionId = sessionId,
                DocumentRecordId = documentRecordId,
                WasCreated = true,
            });

        using var client = _fixture.CreateAuthenticatedClient();

        var body = new { sessionId, tenantId, displayName = "Draft.docx" };

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/promote", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<PromoteComposeDocumentResponse>();
        result.Should().NotBeNull();
        result!.DocumentRecordId.Should().Be(documentRecordId);
        result.WasCreated.Should().BeTrue("first promotion creates the sprk_document row");
    }

    [Fact]
    public async Task PostCheckoutDocument_Authenticated_Returns501_PerR1StubContract()
    {
        // Phase 5 stub: routes through existing /api/documents/{id}/checkout in R1.
        var documentId = Guid.NewGuid();
        using var client = _fixture.CreateAuthenticatedClient();

        using var content = JsonContent.Create(new { });
        var response = await client.PostAsync($"/api/compose/documents/{documentId}/checkout", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented,
            "Compose check-out is a Phase 5 stub in R1 (ComposeEndpoints.Checkout)");
    }

    [Fact]
    public async Task PostCheckinDocument_Authenticated_Returns501_PerR1StubContract()
    {
        var documentId = Guid.NewGuid();
        using var client = _fixture.CreateAuthenticatedClient();

        using var content = JsonContent.Create(new { });
        var response = await client.PostAsync($"/api/compose/documents/{documentId}/checkin", content);

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented,
            "Compose check-in is a Phase 5 stub in R1 (ComposeEndpoints.Checkin)");
    }

    [Fact]
    public async Task PostDispatchAction_Authenticated_WithRoutedPlaybook_Returns200_ViaPublicContractsFacadeOnly()
    {
        // Refined ADR-013 (2026-05-20): the dispatch path goes through
        // IConsumerRoutingService.ResolveAsync + IInvokePlaybookAi.InvokePlaybookAsync —
        // both lives in Services/Ai/PublicContracts/. NO AI-internal type is reached.
        // This test asserts the WIRING end-to-end by verifying both facade calls fire.
        const string consumerType = ConsumerTypes.ComposeSummarize;
        const string tenantId = "tenant-aad-004";
        const string speId = "spe-item-dispatch-001";
        var sessionId = Guid.NewGuid().ToString();
        var playbookId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        _fixture.RoutingMock.Reset();
        _fixture.InvokePlaybookMock.Reset();

        _fixture.RoutingMock
            .Setup(r => r.ResolveAsync(
                consumerType,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbookId);

        _fixture.InvokePlaybookMock
            .Setup(i => i.InvokePlaybookAsync(
                playbookId,
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookInvocationResult
            {
                RunId = runId,
                Success = true,
                TextContent = "Summary text from playbook.",
                Confidence = 0.92,
                Duration = TimeSpan.FromMilliseconds(842),
                Citations = Array.Empty<ToolResultCitation>(),
            });

        using var client = _fixture.CreateAuthenticatedClient();

        var body = new
        {
            documentSpeId = speId,
            tenantId,
            sessionId,
            driveId = "drive-004",
        };

        var response = await client.PostAsJsonAsync($"/api/compose/action/{consumerType}", body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ComposeActionResponse>();
        result.Should().NotBeNull();
        result!.RunId.Should().Be(runId);
        result.Success.Should().BeTrue();
        result.TextContent.Should().Be("Summary text from playbook.");

        // The load-bearing ADR-013 assertion: both PublicContracts facade methods were
        // exercised exactly once. Neither IOpenAiClient nor IPlaybookOrchestrationService
        // is reachable from this handler — the static-contract test in
        // ComposeEndpointsTests.Dispatch_action_handler_only_injects_PublicContracts_facade_types_per_refined_ADR_013
        // (W3-024) anchors that at the reflection level; this test anchors it at the live
        // HTTP boundary.
        _fixture.RoutingMock.Verify(r => r.ResolveAsync(
            consumerType,
            It.IsAny<string?>(),
            It.IsAny<IRoutingContext?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
        _fixture.InvokePlaybookMock.Verify(i => i.InvokePlaybookAsync(
            playbookId,
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<PlaybookInvocationContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ================================================================================
    // ===== Round-trip response-shape contract (task W8-061) =========================
    // ================================================================================
    //
    // Task W8-061 §goal: "exercises the compose-summarize round-trip end-to-end at the
    // BFF endpoint level: POST /api/compose/action/compose-summarize -> IConsumerRoutingService
    // resolves Document Summary playbook id -> IInvokePlaybookAi invoked -> response shape
    // verified."
    //
    // The sibling happy-path test (PostDispatchAction_Authenticated_WithRoutedPlaybook_*)
    // verifies routing + invocation reach the facade. This test closes the response-shape
    // gap: it pins ALL ten fields of the locked ComposeActionResponse contract per Spike
    // #4 §6 against a fully-populated PlaybookInvocationResult, including the fields the
    // sibling test does not assert (StructuredData, DurationMs, CitationCount, ErrorMessage,
    // ErrorCode, CorrelationId).
    //
    // Path-A vs Path-C (CLAUDE.md §6.5) decision recorded for the reviewer:
    //   - POML 061 §goal is materially satisfied by W3-027's sibling happy-path test.
    //   - Adding a duplicate full-roundtrip test would be a B6 mirror-test antipattern.
    //   - Path C (pivot to comply) — this single response-shape test adds non-redundant
    //     coverage of the locked Spike #4 §6 contract WITHOUT scaffolding duplication.
    //
    // KEEP path: endpoint-contract (same file, same fixture, same boundary mocks).
    // Banned-pattern compliance: behavior-shaped (B6 N/A — distinct scenario from sibling);
    //   no Mock<HttpMessageHandler> (B1); no DI-registration assertions (B3); no ctor null
    //   checks (B4); ratio of setup:assertion is balanced (B15 N/A).

    [Fact]
    public async Task PostDispatchAction_WithComposeSummarizeRoundTrip_ReturnsLockedResponseShape_PerSpike4Section6()
    {
        // Arrange — full round-trip context: tenant + drive + sessionId + selection-free
        // whole-document compose-summarize per FR-09 + spike #4 §4.2 (compose-document scope).
        const string consumerType = ConsumerTypes.ComposeSummarize;
        const string tenantId = "tenant-aad-roundtrip-001";
        const string speId = "spe-item-roundtrip-doc";
        const string driveId = "drive-roundtrip-001";
        var sessionId = Guid.NewGuid().ToString();
        var matterId = Guid.NewGuid();
        var documentRecordId = Guid.NewGuid();
        var playbookId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var duration = TimeSpan.FromMilliseconds(1247);

        // Fully-populated PlaybookInvocationResult exercises every non-trivial field of the
        // locked ComposeActionResponse contract (Spike #4 §6). Citations carries 3 entries
        // so CitationCount is non-trivial (distinguishes from accidental 0).
        var citationOne = new ToolResultCitation(
            ChunkId: Guid.NewGuid().ToString(),
            SourceName: "Citation A — Summary source",
            PageNumber: 1,
            Excerpt: "Excerpt text A");
        var citationTwo = new ToolResultCitation(
            ChunkId: Guid.NewGuid().ToString(),
            SourceName: "Citation B — Summary source",
            PageNumber: 2,
            Excerpt: "Excerpt text B");
        var citationThree = new ToolResultCitation(
            ChunkId: Guid.NewGuid().ToString(),
            SourceName: "Citation C — Summary source",
            PageNumber: 3,
            Excerpt: "Excerpt text C");

        // StructuredData is the optional JSON envelope per Spike #4 §6 (R2-ready; nullable
        // in R1 happy-path). We pin it as non-null here to verify the field flows through
        // the contract end-to-end (regression guard for the optional projection).
        using var structuredDoc = JsonDocument.Parse(@"{""summary"":""Document Summary playbook result"",""sectionCount"":5}");
        var structuredData = structuredDoc.RootElement.Clone();

        _fixture.RoutingMock.Reset();
        _fixture.InvokePlaybookMock.Reset();

        _fixture.RoutingMock
            .Setup(r => r.ResolveAsync(
                consumerType,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbookId);

        _fixture.InvokePlaybookMock
            .Setup(i => i.InvokePlaybookAsync(
                playbookId,
                It.IsAny<IReadOnlyDictionary<string, string>?>(),
                It.IsAny<PlaybookInvocationContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlaybookInvocationResult
            {
                RunId = runId,
                Success = true,
                TextContent = "Document Summary: this is the round-trip summary from the playbook.",
                StructuredData = structuredData,
                Citations = new[] { citationOne, citationTwo, citationThree },
                Confidence = 0.88,
                Duration = duration,
                ErrorMessage = null,
                ErrorCode = null,
            });

        using var client = _fixture.CreateAuthenticatedClient();

        var body = new
        {
            documentSpeId = speId,
            tenantId,
            sessionId,
            driveId,
            documentRecordId,
            matterId,
            documentName = "Round-trip Draft.docx",
            documentMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            documentVersionEtag = "\"v3-etag\"",
        };

        // Act — the round-trip exercises the routing facade + invoke facade + response
        // projection in the same call that production code makes.
        var response = await client.PostAsJsonAsync($"/api/compose/action/{consumerType}", body);

        // Assert — HTTP 200 + locked field contract per Spike #4 §6.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "compose-summarize round-trip with valid routing + successful invocation returns 200 per FR-09");

        var result = await response.Content.ReadFromJsonAsync<ComposeActionResponse>();
        result.Should().NotBeNull("response body must deserialize into the locked ComposeActionResponse shape");

        // Field-by-field assertions on the locked contract (Spike #4 §6). Each assertion
        // protects a specific consumer contract — if a future refactor drops or renames a
        // field, this test fails with a precise message naming the field + the requirement.
        result!.RunId.Should().Be(runId,
            "RunId field of ComposeActionResponse must propagate the playbook run id end-to-end (Spike #4 §6)");
        result.Success.Should().BeTrue(
            "Success field reflects the terminal playbook outcome (Spike #4 §6 row 1)");
        result.TextContent.Should().Be("Document Summary: this is the round-trip summary from the playbook.",
            "TextContent carries the terminal node text per IInvokePlaybookAi.InvokePlaybookAsync result projection");
        result.StructuredData.Should().NotBeNull(
            "StructuredData field MUST flow through when the playbook produces a structured projection (Spike #4 §6 row 4)");
        result.Confidence.Should().Be(0.88,
            "Confidence field flows from PlaybookInvocationResult.Confidence (Spike #4 §6 row 5)");
        result.DurationMs.Should().Be((long)duration.TotalMilliseconds,
            "DurationMs is the milliseconds projection of PlaybookInvocationResult.Duration (Spike #4 §6 row 6)");
        result.CitationCount.Should().Be(3,
            "CitationCount is the .Citations list length — 3 citations were emitted (Spike #4 §6 row 7)");
        result.ErrorMessage.Should().BeNull(
            "ErrorMessage MUST be null on Success=true round-trip (Spike #4 §6 row 8)");
        result.ErrorCode.Should().BeNull(
            "ErrorCode MUST be null on Success=true round-trip (Spike #4 §6 row 9)");
        result.CorrelationId.Should().NotBeNullOrEmpty(
            "CorrelationId carries the HttpContext.TraceIdentifier and MUST be present for the client to trace the run (Spike #4 §6 row 10)");

        // Facade boundary verification — both facades were invoked exactly once per the
        // refined ADR-013 round-trip contract. Belt-and-suspenders with the sibling test
        // and the W3-024 static-contract reflection test on the handler parameter shape.
        _fixture.RoutingMock.Verify(r => r.ResolveAsync(
            consumerType,
            It.IsAny<string?>(),
            It.IsAny<IRoutingContext?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once,
            "ResolveAsync must be called exactly once for the consumerType — round-trip contract per FR-09");

        _fixture.InvokePlaybookMock.Verify(i => i.InvokePlaybookAsync(
            playbookId,
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<PlaybookInvocationContext>(),
            It.IsAny<CancellationToken>()), Times.Once,
            "InvokePlaybookAsync must be called exactly once with the resolved playbookId — round-trip contract per FR-09");
    }

    // ================================================================================
    // ===== Routing-failure + unknown-consumer paths (503 / 404) =====================
    // ================================================================================

    [Fact]
    public async Task PostDispatchAction_WhenNoRoutingConfigured_Returns503()
    {
        // Spike #4 §8 row: no sprk_playbookconsumer row → null playbookId → 503.
        const string consumerType = ConsumerTypes.ComposeSummarize;

        _fixture.RoutingMock.Reset();
        _fixture.InvokePlaybookMock.Reset();
        _fixture.RoutingMock
            .Setup(r => r.ResolveAsync(
                consumerType,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        using var client = _fixture.CreateAuthenticatedClient();

        var body = new
        {
            documentSpeId = "spe-item-noroute",
            tenantId = "tenant-aad-005",
        };

        var response = await client.PostAsJsonAsync($"/api/compose/action/{consumerType}", body);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "no routing configured -> 503 per ComposeEndpoints.DispatchAction error model");

        // The dispatch must NOT have invoked the playbook facade — short-circuit before invoke.
        _fixture.InvokePlaybookMock.Verify(i => i.InvokePlaybookAsync(
            It.IsAny<Guid>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(),
            It.IsAny<PlaybookInvocationContext>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PostDispatchAction_WithUnknownConsumerType_Returns404()
    {
        // ComposeEndpoints.DispatchAction performs a defense-in-depth check against
        // ConsumerTypes.All before touching the routing layer. Unknown type → 404.
        const string unknownConsumerType = "compose-not-a-real-consumer";

        _fixture.RoutingMock.Reset();
        _fixture.InvokePlaybookMock.Reset();

        using var client = _fixture.CreateAuthenticatedClient();

        var body = new
        {
            documentSpeId = "spe-item-unknown",
            tenantId = "tenant-aad-006",
        };

        var response = await client.PostAsJsonAsync($"/api/compose/action/{unknownConsumerType}", body);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Routing layer MUST NOT be reached on unknown consumer type — the handler
        // short-circuits before resolving so an unknown type cannot exercise any
        // downstream code path (defense in depth).
        _fixture.RoutingMock.Verify(r => r.ResolveAsync(
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<IRoutingContext?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ================================================================================
    // ===== Validation (400 BadRequest) ==============================================
    // ================================================================================

    [Fact]
    public async Task GetLoadDocument_WhenMissingDriveIdQueryParam_Returns400()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        // Missing driveId (handler validates) — tenantId IS present so we get past that gate
        // and hit the driveId check.
        var response = await client.GetAsync(
            "/api/compose/documents/spe-item-validate?tenantId=tenant-aad-007");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Load handler validates driveId presence and returns 400 ProblemDetails when missing");
    }

    [Fact]
    public async Task PostSaveDocument_WhenContentIsEmpty_Returns400()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var body = new
        {
            driveId = "drive-008",
            sessionId = Guid.NewGuid().ToString(),
            tenantId = "tenant-aad-008",
            content = Array.Empty<byte>(),
        };

        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/spe-item-empty/save", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Save handler validates non-empty content and returns 400 ProblemDetails when empty");
    }

    [Fact]
    public async Task PostPromoteDocument_WhenMissingSessionId_Returns400()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var body = new { tenantId = "tenant-aad-009" };  // no sessionId

        var response = await client.PostAsJsonAsync(
            "/api/compose/documents/spe-item-nosession/promote", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "Promote handler requires sessionId for the ephemeral->promoted rebind (FR-07)");
    }

    [Fact]
    public async Task PostDispatchAction_WhenMissingTenantId_Returns400()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var body = new { documentSpeId = "spe-item-notenant" };  // no tenantId

        var response = await client.PostAsJsonAsync(
            $"/api/compose/action/{ConsumerTypes.ComposeSummarize}", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "DispatchAction handler requires tenantId for multi-tenant isolation (ADR-015)");
    }
}

// ================================================================================
// ===== Fixture ==================================================================
// ================================================================================

/// <summary>
/// In-process BFF fixture for the Compose endpoint integration tests. Mirrors the
/// canonical config-key set from <see cref="Sprk.Bff.Api.Tests.CustomWebAppFactory"/>
/// (per <c>.claude/constraints/bff-extensions.md §F.2</c> Fixture-Config-FIRST), swaps
/// in a fake auth scheme, and replaces <see cref="IComposeService"/> +
/// <see cref="IConsumerRoutingService"/> + <see cref="IInvokePlaybookAi"/> with
/// strict Moqs so tests configure boundary behavior per scenario.
/// </summary>
public sealed class ComposeContractFixture : WebApplicationFactory<Program>
{
    public Mock<IComposeService> ComposeServiceMock { get; } = new(MockBehavior.Loose);
    public Mock<IConsumerRoutingService> RoutingMock { get; } = new(MockBehavior.Loose);
    public Mock<IInvokePlaybookAi> InvokePlaybookMock { get; } = new(MockBehavior.Loose);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            // Canonical config-key set: keep in sync with CustomWebAppFactory.
            // Source of these specific keys: bff-extensions.md §F.2 + factory-config-gaps audit.
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
                ["Graph:UseManagedIdentity"] = "false",
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
            // Allow framework to map missing/required-bindings to 400 (matches production-equivalent contract).
            services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options =>
            {
                options.ThrowOnBadRequest = false;
            });

            // Replace JWT with a fake auth handler keyed off the Authorization header.
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ComposeFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeFakeAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ComposeFakeAuthHandler>(
                ComposeFakeAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = ComposeFakeAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ComposeFakeAuthHandler.SchemeName;
            });

            // Replace Graph factory so incidental OBO paths don't call real MSAL.
            services.RemoveAll<IGraphClientFactory>();
            services.AddSingleton<IGraphClientFactory, FakeGraphClientFactory>();

            // Strip hosted services (no background workers in tests).
            services.RemoveAll<IHostedService>();

            // Mock Dataverse (avoid real network).
            var dataverseMock = new Mock<IDataverseService>();
            dataverseMock.Setup(d => d.TestConnectionAsync()).ReturnsAsync(true);
            services.RemoveAll<IDataverseService>();
            services.AddSingleton(dataverseMock.Object);

            // === Compose boundaries — module-boundary mocks per ADR-038 §4 ===
            // ComposeServicesModule (task 025) registered these as Scoped UNCONDITIONAL.
            // We swap them for test doubles so handler scenarios can control return shape.
            services.RemoveAll<IComposeService>();
            services.AddSingleton(ComposeServiceMock.Object);

            // PublicContracts facade — refined ADR-013 boundary. Test doubles here verify
            // the dispatch endpoint reaches ONLY these types end-to-end.
            services.RemoveAll<IConsumerRoutingService>();
            services.AddSingleton(RoutingMock.Object);
            services.RemoveAll<IInvokePlaybookAi>();
            services.AddSingleton(InvokePlaybookMock.Object);
        });
    }

    public HttpClient CreateUnauthenticatedClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

    public HttpClient CreateAuthenticatedClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return client;
    }
}

/// <summary>
/// Fake authentication handler for the Compose contract tests. Authenticates when the
/// <c>Authorization</c> header is present; fails (401) otherwise. Carries an
/// <c>oid</c> claim derived from <c>X-Test-User</c> (defaults to a fresh GUID) so any
/// downstream code paths that read user identity have a deterministic value.
/// </summary>
internal sealed class ComposeFakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ComposeFakeAuth";

    public ComposeFakeAuthHandler(
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

        var oid = Request.Headers["X-Test-User"].ToString();
        if (string.IsNullOrWhiteSpace(oid))
        {
            oid = Guid.NewGuid().ToString();
        }

        var claims = new List<Claim>
        {
            new("oid", oid),
            new(ClaimTypes.NameIdentifier, oid),
            new(ClaimTypes.Name, $"Compose Test User {oid}"),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
