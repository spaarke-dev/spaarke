// Task 004 (spaarkeai-compose-r4) — the NFR-01 round-trip byte-diff harness. This is the
// acceptance verifier for "package parts never opened are copied verbatim" (design.md §5.0) and
// the Phase-0 gate's (task 006) evidence input, per the task's own acceptance criteria.
//
// SCOPE (explicit — read before extending): `ComposeShadowPatchEngine` does not exist yet (that is
// task 030). This harness therefore proves the NO-OP invariant: load a corpus doc → save with an
// EMPTY operation log (no `editedParagraphs`, no `annotations`, no `contentModel`) → assert the
// persisted bytes equal the retained original. Per `ComposeService.SaveAsync`'s own baseline-
// resolution remarks (see `Services/Compose/ComposeService.cs` — "An empty edit list is a
// structural round-trip... A no-edit, no-annotation Save persists the baseline byte-identical"),
// this is the EXISTING retained-original / clean-save passthrough path — no new production code
// was needed for this task.
//
// TODO(task 034): once the Patch Engine lands, add a sibling seam test that saves with a
// NON-EMPTY operation log and reuses `ComposeOoxmlPackagePartComparer` with
// `strictDocumentXmlByteIdentity` set per the Unresolved-Question decision — untouched-part
// assertions are identical; only `document.xml` legitimately diverges from the original there.
//
// Seam-slice shape (ADR-038 §"vertical-slice-seam"): drives the REAL `POST
// /api/compose/documents/{id}/save` route via `WebApplicationFactory`, capturing the persisted
// bytes at the `ISpeFileOperations.ReplaceFileContentAsUserAsync` facade boundary — the same
// pattern `ComposeFidelitySeamTests` (tests/integration/seam/Ai/) already established. This test
// REUSES that file's `ComposeFidelitySeamFixture` (host config + SPE/Dataverse/indexing mocks +
// fake auth handler) rather than duplicating ~150 lines of WebApplicationFactory setup — per root
// CLAUDE.md §11 (prefer extending/reusing over introducing a new component); only the boundary
// mocks are module-boundary doubles (ADR-038 mock-boundary rule), the real `ComposeService` /
// `ComposeEndpoints` / SPE-facade wiring is production code.
//
// NEGATIVE (ADR-038 banned patterns): NO `Mock<HttpMessageHandler>`, NO DI-registration test, NO
// ctor-null test anywhere in this file.
//
// Corpus: EVERY `.docx` under `tests/fixtures/compose-corpus/` (task 002), enumerated via
// `ComposeCorpusFixtureLocator` — NOT a hardcoded 3-filename list, so an owner-supplied
// worst-offender dropped into that directory later is automatically covered (see
// corpus-manifest.md §2).

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml.Packaging;
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
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeNoOpRoundTripByteDiffSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeNoOpRoundTripByteDiffSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    /// <summary>xUnit [MemberData] source — every corpus `.docx`, glob-enumerated (not hardcoded).</summary>
    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task NoOpSave_OnEveryCorpusDoc_PersistsRetainedOriginalByteIdentical(string corpusDocPath)
    {
        _fixture.ResetBoundaries();

        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        // Sanity precondition: the fixture must actually be a well-formed, openable .docx package
        // BEFORE we round-trip it — a malformed corpus doc would otherwise surface as a confusing
        // failure deep inside the comparer rather than as a clear "bad fixture" signal.
        using (var sanity = WordprocessingDocument.Open(new MemoryStream(original, writable: false), isEditable: false))
        {
            sanity.MainDocumentPart.Should().NotBeNull($"corpus doc '{docName}' must be a valid OOXML package");
        }

        var speId = $"spe-item-byte-diff-{Guid.NewGuid():N}";
        var driveId = $"drive-byte-diff-{Guid.NewGuid():N}";
        var existingDocumentId = Guid.NewGuid();

        string sessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(ComposeFidelitySeamFixture.TestTenantId, TestSessionOwner.Oid, documentId: speId);
            sessionId = session.SessionId;
        }

        // ── SPE facade boundary: capture the bytes ComposeService actually persists ──────────────
        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: docName, ParentId: null,
                Size: original.Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v2-etag\"", IsFolder: false,
                WebUrl: null, DriveId: driveId));

        // Already-promoted document — a REPLACE save, not a first-Save.
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act: the REAL wire contract for a clean (no-edit, no-annotation) save — the retained
        //    original `content`, and deliberately NO `editedParagraphs` / `annotations` /
        //    `contentModel` (an EMPTY operation log, this task's explicit scope). ──────────────────
        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            driveId,
            content = original,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the real save route must accept a clean (no-op) save for corpus doc '{docName}'");
        var result = await response.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        result.Should().NotBeNull();
        result!.DocumentSpeId.Should().Be(speId);

        persisted.Should().NotBeNull(
            $"the SPE facade boundary must have captured the bytes ComposeService actually persisted for '{docName}'");

        // ── Assert (task 004 scope — STRICT mode): a no-op save (empty operation log) must leave
        //    EVERY package part byte-identical, including document.xml itself. This is the current,
        //    stronger-than-eventually-required invariant (ComposeService.SaveAsync never
        //    re-serializes document.xml when EditedParagraphs/Annotations/ContentModel are all
        //    absent) — see the file-header remarks for why this is expected to hold today, and the
        //    task-034 TODO for the eventual non-empty-operation-log case. ──────────────────────────
        var strict = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: true);
        strict.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"NFR-01: every OOXML package part never opened by a no-op save must be byte-identical for '{docName}' — {strict.DescribeMismatches()}");
        strict.DocumentXmlDiff.ByteIdentical.Should().BeTrue(
            $"a no-op save (empty operation log) must leave document.xml byte-identical too for '{docName}' — {strict.DescribeMismatches()}");
        strict.Passes.Should().BeTrue(strict.DescribeMismatches());

        // ── Assert: the switchable structural-faithfulness hook (task 034's eventual bar for a
        //    REAL edit) ALSO independently confirms equivalence on this no-op case — exercising the
        //    toggle end-to-end, not merely declaring it present. ────────────────────────────────────
        var structural = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: false);
        structural.DocumentXmlStructurallyFaithful.Should().BeTrue(
            $"the loose structural-faithfulness mode must also confirm document.xml equivalence for '{docName}' — {structural.DescribeMismatches()}");
        structural.Passes.Should().BeTrue(structural.DescribeMismatches());

        // ── Assert (whole-file sanity, mirrors the code-level FR-06a guarantee cited in
        //    ComposeService.SaveAsync's own remarks): the ENTIRE persisted byte array equals the
        //    original — the strongest possible statement for this task's empty-operation-log scope. ──
        persisted.Should().Equal(original,
            $"a no-op save (no edits, no annotations, no content model) must persist the retained original whole-file byte-identical for '{docName}'");
    }
}
