// Task 055 prong 1 (spaarkeai-compose-r5, keep-edits graceful degradation) — the THROUGH-THE-WIRE seam
// evidence that a loaded-doc save whose operation log contains an OP-LEVEL anchoring refusal no longer loses
// the WHOLE editing session (the pre-prong-1 behavior: any refusal → 422). Instead ComposeService.SaveAsync
// falls back to best-effort per-paragraph recovery: the resolvable paragraph-units are applied and the
// unresolvable ops are surfaced on the response's `partialApply` summary — never silently applied (the
// paragraph is the atomic unit under the engine's intra-paragraph sequential rebasing) and never silently
// dropped. Prong 2's paraOffset anchor already fixed the common root cause; this is the residual safety net.
//
//   (1) MIXED batch — one op on a resolvable paragraph + one op on an ABSENT paraId. Pre-prong-1 the whole
//       Apply threw ParagraphNotFound → 422 and every edit was lost. Now: 200, the resolvable paragraph's
//       edit is persisted, and the absent-paraId op is surfaced in `partialApply.unresolved` (Kind
//       ParagraphNotFound). This test would FAIL before prong 1 (the save would be a 422 with no body).
//   (2) INTRA-PARAGRAPH all-or-nothing — a MIXED batch: a resolvable paragraph (applied) + a paragraph with
//       two ops whose FIRST refuses (offset out of range). Because op2 was rebased assuming op1 applied,
//       dropping op1 and keeping op2 would mis-anchor op2 → the paragraph is the atomic unit: BOTH of that
//       paragraph's ops surface, NEITHER is applied, while the OTHER paragraph's edit still lands. Proves the
//       recovery never does an unsafe partial-within-paragraph apply (and is a genuine partial, appliedCount>0).
//   (3) BATCH-level refusal still fails hard — an unsupported op-log schema version condemns the whole
//       batch (not a single anchor), so best-effort is meaningless: it must still surface as a 422
//       ProblemDetails, never a partial 200. Proves the recovery's `when (!IsBatchLevelPatchRefusal)` guard.
//
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slices only. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test. Mocks live only at the ISpeFileOperations / IGenericEntityService
// / IPostUploadIndexingEnqueuer module boundaries (the SAME fixture ConcurrencySaveSeamTests uses — CLAUDE.md
// §11: no new fixture class, no new helper extracted).

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposePartialApplyRecoverySeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposePartialApplyRecoverySeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private static readonly string[] Paragraphs =
    {
        "This Master Services Agreement governs the engagement between the parties.",
        "Confidential Information shall not be disclosed to any third party.",
        "This clause shall survive termination of the Agreement.",
    };

    private static readonly string[] ParaIds = { "00000001", "00000002", "00000003" };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. MIXED batch — a resolvable op + an absent-paraId op. Best-effort applies the resolvable one and
    //    surfaces the unresolvable one; the whole session is NOT lost (was a 422 before prong 1).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_OpLevelRefusalInBatch_AppliesResolvable_SurfacesUnresolvable_ThroughTheWire()
    {
        const string speId = "spe-item-055-partial-mixed";
        const string driveId = "drive-055-partial-mixed";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // Non-stale save (no prior version stamp seeded) → the normal apply block runs, not the stale
        // re-anchor path. Live metadata is fetched on every existing-item save; the write captures bytes.
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag\""));

        // op1 → paragraph 00000001 (present, resolves). op2 → DEADBEEF (absent → ParagraphNotFound). In log
        // order the engine applies op1 then throws on op2 → the FULL-batch Apply loses BOTH (pre-prong-1).
        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[APPLIED]" },
                new { type = "insertText", paraId = "DEADBEEF", at = new { runIndex = 0, offset = 0 }, text = "should never land" },
            },
        };

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"an op-level refusal must NOT lose the whole batch — best-effort recovery returns 200 — body: {body}");

        var saveResult = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult.Should().NotBeNull();
        saveResult!.ReanchorSummary.Should().BeNull("this is a NON-stale save — the stale re-anchor path did not fire");
        saveResult.PartialApply.Should().NotBeNull("an op-level refusal triggers best-effort recovery, surfaced on partialApply");
        saveResult.PartialApply!.Total.Should().Be(2);
        saveResult.PartialApply.AppliedCount.Should().Be(1, "the resolvable paragraph's op is applied");
        saveResult.PartialApply.UnresolvedCount.Should().Be(1, "the absent-paraId op is surfaced, never silently dropped");
        var unresolved = saveResult.PartialApply.Unresolved.Single();
        unresolved.ParaId.Should().Be("DEADBEEF");
        unresolved.OpType.Should().Be(nameof(InsertTextOperation));
        unresolved.Kind.Should().Be(nameof(ComposePatchErrorKind.ParagraphNotFound));

        persisted.Should().NotBeNull("the SPE facade must have captured the best-effort patched bytes");
        using var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var body1 = patchedDoc.MainDocumentPart!.Document!.Body!;
        var editedPara = body1.Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, ParaIds[0], StringComparison.OrdinalIgnoreCase));
        editedPara.Descendants<Text>().Select(t => t.Text)
            .Should().Contain(t => t.Contains("[APPLIED]", StringComparison.Ordinal),
                "the resolvable op MUST actually be applied through the Patch Engine, not just reported");
        body1.Descendants<Text>().Select(t => t.Text)
            .Should().NotContain(t => t.Contains("should never land", StringComparison.Ordinal),
                "the unresolvable op must NEVER be silently applied");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. INTRA-PARAGRAPH all-or-nothing — a MIXED batch: paragraph A has one resolvable op, and paragraph B
    //    has TWO ops whose FIRST refuses. The resolvable paragraph applies (so this is a genuine PARTIAL,
    //    appliedCount>0), while paragraph B is the atomic unit: op2 was rebased assuming op1 applied, so
    //    BOTH of B's ops surface and NEITHER is applied — never an unsafe partial-within-paragraph apply.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_IntraParagraphFirstOpRefuses_WholeParagraphSurfaced_ResolvableParagraphStillApplied_ThroughTheWire()
    {
        const string speId = "spe-item-055-partial-intrapara";
        const string driveId = "drive-055-partial-intrapara";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag\""));

        // Paragraph 00000001 → one valid op (resolves → keeps this a genuine partial, appliedCount=1).
        // Paragraph 00000002 → two ops: op1's run-local offset is far past the run length (no paraOffset, so
        // the engine resolves via runIndex/offset) → OffsetOutOfRange; op2 is a valid insert. Because they
        // share a paragraph, the recovery unit is all-or-nothing: op1's refusal condemns op2 too.
        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[PARA-A-OK]" },
                new { type = "insertText", paraId = ParaIds[1], at = new { runIndex = 0, offset = 99999 }, text = "[FIRST]" },
                new { type = "insertText", paraId = ParaIds[1], at = new { runIndex = 0, offset = 0 }, text = "[SECOND]" },
            },
        };

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"best-effort recovery returns 200 — body: {body}");

        var saveResult = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult!.PartialApply.Should().NotBeNull();
        saveResult.PartialApply!.Total.Should().Be(3);
        saveResult.PartialApply.AppliedCount.Should().Be(1, "paragraph A's op is the only resolvable one");
        saveResult.PartialApply.UnresolvedCount.Should().Be(2,
            "the paragraph is the atomic unit — a refusal on paragraph B's first op condemns BOTH of B's ops, never a partial-within-paragraph apply");

        persisted.Should().NotBeNull();
        using var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var body2 = patchedDoc.MainDocumentPart!.Document!.Body!;
        body2.Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, ParaIds[0], StringComparison.OrdinalIgnoreCase))
            .Descendants<Text>().Select(t => t.Text)
            .Should().Contain(t => t.Contains("[PARA-A-OK]", StringComparison.Ordinal),
                "the resolvable paragraph's edit is applied even though another paragraph's unit refused");
        body2.Descendants<Text>().Select(t => t.Text)
            .Should().NotContain(t => t.Contains("[SECOND]", StringComparison.Ordinal),
                "op2 must NOT be applied — its anchor assumed op1 (dropped) had applied, so applying it alone could mis-place bytes");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. BATCH-level refusal (unsupported schema version) still fails HARD — best-effort is meaningless
    //    when the whole log is rejected, so it must surface as a 422 ProblemDetails, never a partial 200.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_BatchLevelSchemaRefusal_StillFailsHard_NoPartialApply_ThroughTheWire()
    {
        const string speId = "spe-item-055-partial-batchfail";
        const string driveId = "drive-055-partial-batchfail";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        // A schema version the engine does not compile against → UnsupportedSchemaVersion, a BATCH-level
        // refusal: it condemns the whole log, so prong-1 recovery is bypassed (the `when` guard) and the
        // save fails hard as a ProblemDetails (422), never a partial 200.
        var operationLog = new
        {
            schemaVersion = "compose-ops-v0-bogus",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[APPLIED]" },
            },
        };

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = (object?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "a batch-level schema refusal is not an anchor refusal — best-effort is meaningless, so it stays a hard failure (UnsupportedSchemaVersion → 409, never a partial 200)");
        // FR-S02 (r8 task 011): the replace path now routes through the If-Match overload when the save
        // resolved a live version, and the etag-less overload when it did not — assert NEITHER ran.
        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a batch-level refusal must never write partial bytes to SPE");
        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "a batch-level refusal must never write partial bytes to SPE");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. COMMENTS-ONLY review save (agreements-r1 UAT round-1 #4) — one comment anchors, one does NOT.
    //    A review places advisory comments but no text edits, so the op-log is EMPTY. Pre-fix, the
    //    best-effort recovery counted only OPS toward `appliedCount`, so even a fully-bakeable comment
    //    contributed 0 → `appliedCount == 0` → the guard RE-THREW the anchor refusal → 422, discarding
    //    the comment that DID anchor. A comment is NON-DESTRUCTIVE: it must degrade gracefully
    //    (skip-with-report), never fail the WHOLE save. Expect 200, the resolvable comment baked as a
    //    native w:comment, and the unbakeable comment surfaced on `partialApply` (never silently dropped).
    //    This test FAILS before the fix (the save is a 422 with no body).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_CommentsOnly_OneCommentUnanchorable_BakesResolvable_SurfacesUnbakeable_ThroughTheWire()
    {
        const string speId = "spe-item-agr-comments-only";
        const string driveId = "drive-agr-comments-only";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag\""));

        // NO operationLog — a pure review save. comment1 → paragraph 00000001 (resolves). comment2 →
        // DEADBEEF (absent → ParagraphNotFound). The empty-op-log Apply throws on comment2 first.
        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            comments = new object[]
            {
                new
                {
                    paraId = ParaIds[0],
                    range = new { start = new { runIndex = 0, offset = 0 }, end = new { runIndex = 0, offset = 8 } },
                    commentText = "RESOLVABLE-COMMENT",
                    author = "AI Advisory Review",
                    date = "2026-08-03T00:00:00Z",
                },
                new
                {
                    paraId = "DEADBEEF",
                    range = new { start = new { runIndex = 0, offset = 0 }, end = new { runIndex = 0, offset = 8 } },
                    commentText = "UNANCHORABLE-COMMENT",
                    author = "AI Advisory Review",
                    date = "2026-08-03T00:00:00Z",
                },
            },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a non-destructive comment that can't anchor must NOT fail the whole review save — body: {body}");

        var saveResult = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult!.PartialApply.Should().NotBeNull("the unbakeable comment is surfaced, never silently dropped");
        saveResult.PartialApply!.AppliedCount.Should().Be(1, "the resolvable comment baked (comments count toward applied)");
        saveResult.PartialApply.UnresolvedCount.Should().Be(1, "the absent-paraId comment is surfaced");
        var unresolved = saveResult.PartialApply.Unresolved.Single();
        unresolved.ParaId.Should().Be("DEADBEEF");
        unresolved.OpType.Should().Be("AdvisoryComment", "a skipped comment is reported as a comment, not an edit op");
        unresolved.Kind.Should().Be(nameof(ComposePatchErrorKind.ParagraphNotFound));

        persisted.Should().NotBeNull("the SPE facade must have captured the best-effort patched bytes");
        using var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var commentsPart = patchedDoc.MainDocumentPart!.WordprocessingCommentsPart;
        commentsPart.Should().NotBeNull("the resolvable advisory comment must bake as a native w:comment");
        var commentTexts = commentsPart!.Comments!.Descendants<Comment>()
            .SelectMany(c => c.Descendants<Text>()).Select(t => t.Text).ToList();
        commentTexts.Should().Contain(t => t.Contains("RESOLVABLE-COMMENT", StringComparison.Ordinal),
            "the comment that anchored MUST be baked, not lost with the whole save");
        commentTexts.Should().NotContain(t => t.Contains("UNANCHORABLE-COMMENT", StringComparison.Ordinal),
            "the comment that could not anchor must NEVER be silently placed on a wrong paragraph");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. TEXT edit + an unanchorable COMMENT (agreements-r1 UAT round-1 #4) — the text edit resolves and
    //    is preserved; the dropped comment is SURFACED (not silently dropped). Pre-fix the recovery
    //    counted only ops, so the save returned 200 but with unresolvedCount 0 — the reviewer never saw
    //    that their comment was lost. Post-fix the comment shows on `partialApply.unresolved`. The
    //    honest-failure contract for TEXT is unchanged (a text op that fails still surfaces / hard-fails).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_TextEditPlusUnanchorableComment_PreservesEdit_SurfacesDroppedComment_ThroughTheWire()
    {
        const string speId = "spe-item-agr-edit-plus-comment";
        const string driveId = "drive-agr-edit-plus-comment";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag\""));

        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[EDIT-KEPT]" },
            },
        };

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = new object[]
            {
                new
                {
                    paraId = "DEADBEEF",
                    range = new { start = new { runIndex = 0, offset = 0 }, end = new { runIndex = 0, offset = 8 } },
                    commentText = "DROPPED-COMMENT",
                    author = "AI Advisory Review",
                    date = "2026-08-03T00:00:00Z",
                },
            },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"the text edit resolves, so the save succeeds — body: {body}");

        var saveResult = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult!.PartialApply.Should().NotBeNull();
        saveResult.PartialApply!.AppliedCount.Should().Be(1, "the text edit applied");
        saveResult.PartialApply.UnresolvedCount.Should().Be(1,
            "the dropped comment is now surfaced — pre-fix it was silently dropped (unresolvedCount 0)");
        saveResult.PartialApply.Unresolved.Single().OpType.Should().Be("AdvisoryComment");

        persisted.Should().NotBeNull();
        using var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        patchedDoc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text)
            .Should().Contain(t => t.Contains("[EDIT-KEPT]", StringComparison.Ordinal),
                "the resolvable text edit MUST be preserved even though a comment could not anchor");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared arrange + OOXML helpers (per-file-local, consistent with this suite's convention).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private void ArrangeIdempotentPromotionAndIndexing()
    {
        var existingDocumentId = Guid.NewGuid();
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
    }

    private async Task<string> CreateSessionAsync(string tenant, string speId)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(tenant, documentId: speId);
        return session.SessionId;
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: "partial-apply-seam.docx", ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);

    /// <summary>Builds a valid DOCX whose paragraphs carry the supplied physical w14:paraIds (mirrors the
    /// sibling seam files' local helper — no shared helper extracted, per this suite's convention).</summary>
    private static byte[] BuildDocxWithParaIds(IReadOnlyList<string> paragraphs, IReadOnlyList<string> paraIds)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            for (var i = 0; i < paragraphs.Count; i++)
            {
                var p = new Paragraph(new Run(new Text(paragraphs[i]) { Space = SpaceProcessingModeValues.Preserve }))
                {
                    ParagraphId = new HexBinaryValue(paraIds[i]),
                };
                body.AppendChild(p);
            }
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }
}
