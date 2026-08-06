// Task 034 (spaarkeai-compose-r4, NFR-01/NFR-02, Success Criteria 1/2/5) — the THROUGH-THE-WIRE proof
// for the Patch Engine that tasks 030 (core) and 031 (structural ops) built, and task 032 wired into the
// real save path. Where task 030's ComposeShadowPatchEngineByteDiffSeamTests drives the engine's public
// Apply() surface directly (byte[] in / byte[] out), THIS file drives the REAL
// `POST /api/compose/documents/{id}/save` route (WebApplicationFactory) end to end:
// endpoint -> ComposeService.SaveAsync -> ComposeShadowPatchEngine.Apply -> SPE facade boundary.
//
// Reuses (per root CLAUDE.md §11 "prefer extending over introducing a new component" — no duplication):
//   - ComposeFidelitySeamFixture (tests/integration/seam/Ai/) — the host + SPE/Dataverse/indexing
//     module-boundary mocks + fake auth handler already proven by ComposeNoOpRoundTripByteDiffSeamTests
//     (task 004) and ComposeFidelitySeamTests (fidelity corpus proof).
//   - ComposeCorpusFixtureLocator / ComposeOoxmlPackagePartComparer (task 004) — corpus glob-enumeration
//     and the "untouched package parts stay byte-identical" acceptance instrument. NFR-01's corpus
//     no-op-save-is-byte-identical proof ALREADY EXISTS through the wire via
//     ComposeNoOpRoundTripByteDiffSeamTests (task 004) — this file does NOT duplicate that case; it
//     extends coverage to REAL (non-empty) operation logs, exactly the TODO that file's own header and
//     ComposeOoxmlPackagePartComparer's header both flag as "task 034's eventual bar."
//
// CORPUS NOTE (per corpus-manifest.md §1, note 1a): the 3-doc seed corpus (incl. the CIPO doc, row 1) is
// TRACK-CHANGES-CLEAN as staged — none of the 3 docs carry live w:ins/w:del content, and none exercise
// the TrackedChangeReconciliationUnsupported escalation boundary (that case has no corpus doc; it is
// covered at the unit level only — see ComposeShadowPatchEngineTests.Apply_EditInsideExistingTrackedChange_ThrowsTrackedChangeReconciliationUnsupported).
// Every slice below (interior edit / redline / comment / structural ops) targets addressable PLAIN
// paragraphs the corpus DOES supply (all 3 docs are flat, single-section, table-free per the manifest) —
// no synthetic doc was needed for those cases. The one thing the corpus genuinely cannot supply (a
// pre-existing tracked change to reconcile against) is out of this task's negative-case scope (that is
// the unknown-paraId refusal, which the corpus CAN exercise via a paraId literally absent from any doc).
//
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slices only. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test anywhere in this file.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

public sealed class ComposePatchEngineSaveSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private static readonly DateTimeOffset When = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposePatchEngineSaveSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    /// <summary>xUnit [MemberData] source — every corpus `.docx`, glob-enumerated (not hardcoded). The
    /// seed corpus is exactly 3 docs incl. the CIPO doc (corpus-manifest.md §1), satisfying this task's
    /// "≥3 corpus docs incl. CIPO" acceptance bar automatically.</summary>
    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Placement determinism — interior edit (redline) lands at the intended paraId, through the wire.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task Save_InteriorInsertText_LandsAtIntendedParaId_NoDriftToOtherParagraphs(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var target = FindAddressableParagraphWithRunText(original);
        if (target is null)
        {
            // No corpus doc lacks an addressable paragraph today (all 3 are flat prose per the
            // manifest), but this mirrors the task-030 engine-level test's own guard — nothing to prove
            // for a doc with no addressable paragraph.
            return;
        }

        var (targetParaId, otherParagraphsBefore) = target.Value;

        var operationLog = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation
                {
                    ParaId = targetParaId,
                    At = new ComposeRunPoint(0, 0), // start of run 0 — always a valid anchor
                    Text = "[R4-SEAM-EDIT]",
                },
            },
        };

        var (response, persisted, _) = await PostSaveThroughWireAsync(docName, original, operationLog, comments: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a well-formed interior insertText op must be accepted for '{docName}'");
        persisted.Should().NotBeNull($"the SPE facade must have captured the patched bytes for '{docName}'");

        // ── Placement determinism: the edit landed EXACTLY at the targeted paraId — resolved by
        //    dictionary lookup (see ComposeShadowPatchEngine.PatchSession.Resolve), never by scanning
        //    document text for a match. ─────────────────────────────────────────────────────────────
        using (var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var editedPara = ParagraphById(patchedDoc.MainDocumentPart!.Document!.Body!, targetParaId);
            editedPara.Should().NotBeNull($"paraId '{targetParaId}' must still resolve after the save for '{docName}'");
            var ins = editedPara!.Descendants<InsertedRun>().ToList();
            ins.Should().NotBeEmpty($"paraId '{targetParaId}' must carry the native w:ins for '{docName}'");
            ins.SelectMany(i => i.Descendants<Text>()).Select(t => t.Text)
                .Should().Contain("[R4-SEAM-EDIT]", $"the inserted text must appear at the targeted paraId for '{docName}'");
        }

        // ── Zero drift: every OTHER paragraph's OuterXml is byte-for-byte unchanged — the edit did not
        //    touch a neighbouring node (the failure mode a text-search-based writer is prone to). ─────
        var otherParagraphsAfter = ReadOtherParagraphOuterXml(persisted!, targetParaId);
        otherParagraphsAfter.Should().Equal(otherParagraphsBefore,
            $"only the targeted paraId may change; every other paragraph must stay byte-identical for '{docName}'");

        // ── NFR-01 (task 004 harness, reused): every package part OTHER than document.xml is
        //    byte-identical — the interior edit legitimately changes document.xml only. ───────────────
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"NFR-01: a single interior edit must leave every non-document.xml package part byte-identical for '{docName}' — {comparison.DescribeMismatches()}");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. Placement determinism — a durable-anchored comment lands at the intended paraId.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task Save_AnchoredComment_LandsAtIntendedParaId_NoDriftToOtherParagraphs(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var target = FindAddressableParagraphWithRunText(original);
        if (target is null)
        {
            return;
        }

        var (targetParaId, otherParagraphsBefore) = target.Value;

        // Hand-authored anonymous payload with EXACT camelCase keys (ComposeAnchoredComment itself
        // carries no [JsonPropertyName] attributes, so its wire shape is whatever the ACTIVE naming
        // policy says; the rest of this test suite avoids that question by spelling the wire shape
        // directly — same convention ComposeNoOpRoundTripByteDiffSeamTests uses for its top-level body).
        var commentsPayload = new object[]
        {
            new
            {
                paraId = targetParaId,
                range = new
                {
                    start = new { runIndex = 0, offset = 0 },
                    end = new { runIndex = 0, offset = 1 },
                },
                commentText = "R4 seam comment — durable paraId+range anchor, zero text-search.",
                author = "Seam Test",
                date = When,
            },
        };

        var (response, persisted, _) = await PostSaveThroughWireAsync(docName, original, operationLog: null, comments: commentsPayload);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a well-formed anchored comment must be accepted for '{docName}'");
        persisted.Should().NotBeNull($"the SPE facade must have captured the patched bytes for '{docName}'");

        // ── Placement determinism: the comment range mark landed at the targeted paraId. ──────────────
        using (var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var editedPara = ParagraphById(patchedDoc.MainDocumentPart!.Document!.Body!, targetParaId);
            editedPara.Should().NotBeNull($"paraId '{targetParaId}' must still resolve after the save for '{docName}'");
            editedPara!.Descendants<CommentRangeStart>().Should().NotBeEmpty(
                $"paraId '{targetParaId}' must carry the comment range start for '{docName}'");
            editedPara.Descendants<CommentRangeEnd>().Should().NotBeEmpty(
                $"paraId '{targetParaId}' must carry the comment range end for '{docName}'");

            var commentsPart = patchedDoc.MainDocumentPart!.WordprocessingCommentsPart;
            commentsPart.Should().NotBeNull($"a native w:comment part must exist after the comment save for '{docName}'");
            commentsPart!.Comments!.Descendants<Comment>()
                .SelectMany(c => c.Descendants<DocumentFormat.OpenXml.Wordprocessing.Text>())
                .Select(t => t.Text)
                .Should().Contain(t => t.Contains("R4 seam comment", StringComparison.Ordinal),
                    $"the comment body must be emitted natively for '{docName}'");
        }

        // ── Zero drift: every OTHER paragraph's OuterXml is unchanged. ─────────────────────────────────
        var otherParagraphsAfter = ReadOtherParagraphOuterXml(persisted!, targetParaId);
        otherParagraphsAfter.Should().Equal(otherParagraphsBefore,
            $"only the targeted paraId may change; every other paragraph must stay byte-identical for '{docName}'");

        // ── NFR-01 (task 004 harness, reused): every package part OTHER than document.xml AND the
        //    comment part itself is byte-identical. word/comments.xml is a LEGITIMATE new part here —
        //    this corpus doc previously had no comments part at all (none of the 3 seed docs carry
        //    comments per corpus-manifest.md §1), so its very existence is the comment landing, not
        //    drift. Every OTHER part (styles, numbering, headers/footers, theme, media, ...) must still
        //    be untouched. ───────────────────────────────────────────────────────────────────────────
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: false);
        var mismatchesOtherThanTheNewCommentsPart = comparison.UntouchedMismatches
            .Where(m => !m.Uri.EndsWith("comments.xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        mismatchesOtherThanTheNewCommentsPart.Should().BeEmpty(
            $"NFR-01: an anchored comment must leave every package part OTHER than document.xml and the new " +
            $"comments.xml part byte-identical for '{docName}' — mismatches: " +
            string.Join("; ", mismatchesOtherThanTheNewCommentsPart.Select(m => m.Uri)));
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Structural operations — split / merge / insert / delete paragraph, each round-tripping through
    //    the real save route (task 031's four structural ops, sequenced last per the engine's two-pass
    //    design so a structural node change never shifts an earlier text-op anchor).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task Save_SplitParagraph_RoundTrips_ThroughTheWire(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var splittable = FindSplittableParagraph(original);
        if (splittable is null)
        {
            return; // no paragraph with a splittable run — nothing to prove for this doc.
        }

        var (paraId, run0Length) = splittable.Value;
        var newParaId = MintFreshParaId(original);
        var splitOffset = Math.Max(1, run0Length / 2);

        var operationLog = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new SplitParagraphOperation
                {
                    ParaId = paraId,
                    At = new ComposeRunPoint(0, splitOffset),
                    NewParaId = newParaId,
                },
            },
        };

        var (response, persisted, _) = await PostSaveThroughWireAsync(docName, original, operationLog, comments: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"a well-formed splitParagraph op must be accepted for '{docName}'");
        persisted.Should().NotBeNull($"the SPE facade must have captured the patched bytes for '{docName}'");

        using (var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var body = patchedDoc.MainDocumentPart!.Document!.Body!;
            var trailing = ParagraphById(body, newParaId);
            trailing.Should().NotBeNull($"the new trailing paragraph '{newParaId}' must exist after the split for '{docName}'");
            var leading = ParagraphById(body, paraId);
            leading.Should().NotBeNull($"the leading paragraph '{paraId}' must still resolve after the split for '{docName}'");
            leading!.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Inserted>().Should().NotBeNull(
                $"the leading paragraph's mark must be a tracked insertion (Enter-with-track-changes) for '{docName}'");
        }

        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"NFR-01: a splitParagraph op must leave every non-document.xml package part byte-identical for '{docName}' — {comparison.DescribeMismatches()}");
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task Save_MergeParagraph_RoundTrips_ThroughTheWire(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var pair = FindAdjacentParagraphIdPair(original);
        if (pair is null)
        {
            return; // no two adjacent id-bearing paragraphs — nothing to prove for this doc.
        }

        var (targetParaId, sourceParaId) = pair.Value;

        var operationLog = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new MergeParagraphOperation { ParaId = sourceParaId, TargetParaId = targetParaId },
            },
        };

        var (response, persisted, _) = await PostSaveThroughWireAsync(docName, original, operationLog, comments: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"a well-formed mergeParagraph op must be accepted for '{docName}'");
        persisted.Should().NotBeNull($"the SPE facade must have captured the patched bytes for '{docName}'");

        using (var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var body = patchedDoc.MainDocumentPart!.Document!.Body!;
            var target = ParagraphById(body, targetParaId);
            target.Should().NotBeNull($"the target paragraph '{targetParaId}' must still resolve after the merge for '{docName}'");
            target!.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Deleted>().Should().NotBeNull(
                $"the target's paragraph mark must be a tracked deletion (the merge boundary) for '{docName}'");
            // The source paragraph node stays physically present (content moves logically on Word's accept,
            // not at patch time — the tracked-mark representation per design §5.6(b)); it must still resolve.
            ParagraphById(body, sourceParaId).Should().NotBeNull(
                $"the source paragraph must still be addressable (tracked, not physically removed) for '{docName}'");
        }

        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"NFR-01: a mergeParagraph op must leave every non-document.xml package part byte-identical for '{docName}' — {comparison.DescribeMismatches()}");
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task Save_InsertParagraph_RoundTrips_ThroughTheWire(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var referenceParaId = FindAnyAddressableParagraphId(original);
        if (referenceParaId is null)
        {
            return;
        }

        var newParaId = MintFreshParaId(original);

        var operationLog = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertParagraphOperation
                {
                    ParaId = referenceParaId,
                    NewParaId = newParaId,
                    Position = ComposeParagraphPosition.After,
                },
            },
        };

        var (response, persisted, _) = await PostSaveThroughWireAsync(docName, original, operationLog, comments: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"a well-formed insertParagraph op must be accepted for '{docName}'");
        persisted.Should().NotBeNull($"the SPE facade must have captured the patched bytes for '{docName}'");

        using (var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var inserted = ParagraphById(patchedDoc.MainDocumentPart!.Document!.Body!, newParaId);
            inserted.Should().NotBeNull($"the new paragraph '{newParaId}' must exist after insertParagraph for '{docName}'");
            inserted!.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Inserted>().Should().NotBeNull(
                $"the brand-new paragraph's mark must be a tracked insertion for '{docName}'");
        }

        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"NFR-01: an insertParagraph op must leave every non-document.xml package part byte-identical for '{docName}' — {comparison.DescribeMismatches()}");
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task Save_DeleteParagraph_RoundTrips_ThroughTheWire(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var targetParaId = FindAnyAddressableParagraphId(original);
        if (targetParaId is null)
        {
            return;
        }

        var operationLog = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new DeleteParagraphOperation { ParaId = targetParaId },
            },
        };

        var (response, persisted, _) = await PostSaveThroughWireAsync(docName, original, operationLog, comments: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"a well-formed deleteParagraph op must be accepted for '{docName}'");
        persisted.Should().NotBeNull($"the SPE facade must have captured the patched bytes for '{docName}'");

        using (var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var target = ParagraphById(patchedDoc.MainDocumentPart!.Document!.Body!, targetParaId);
            target.Should().NotBeNull($"paraId '{targetParaId}' must still resolve after deleteParagraph for '{docName}'");
            target!.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Deleted>().Should().NotBeNull(
                $"the struck paragraph's mark must be a tracked deletion for '{docName}'");
        }

        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, persisted!, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"NFR-01: a deleteParagraph op must leave every non-document.xml package part byte-identical for '{docName}' — {comparison.DescribeMismatches()}");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. NEGATIVE — an unknown paraId is REFUSED with a typed ProblemDetails (task 032's
    //    ComposePatchException -> MapPatchException mapping), and nothing is partially written.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_UnknownParaId_IsRefusedWith422ProblemDetails_NothingPersisted()
    {
        var corpusDocPath = ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First();
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        // A syntactically-valid 8-hex ST_LongHexNumber that provably resolves to NO paragraph in this
        // document (minted the same collision-free way the structural-op tests mint a NEW id) — never a
        // text-search fallback (I-7); the engine's dictionary lookup simply misses and refuses.
        var unknownParaId = MintFreshParaId(original);

        var operationLog = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation
                {
                    ParaId = unknownParaId,
                    At = new ComposeRunPoint(0, 0),
                    Text = "should never land",
                },
            },
        };

        var (response, persisted, _) = await PostSaveThroughWireAsync(docName, original, operationLog, comments: null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an unresolvable paraId must be refused as a typed 422 ProblemDetails (ComposePatchErrorKind.ParagraphNotFound), never a 500 or a silent partial write");

        using var problemDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problemDoc.RootElement.GetProperty("title").GetString().Should().Be("Change Could Not Be Applied");
        problemDoc.RootElement.GetProperty("status").GetInt32().Should().Be((int)HttpStatusCode.UnprocessableEntity);

        persisted.Should().BeNull(
            "nothing may be partially written — Apply() throws ComposePatchException BEFORE ComposeService.SaveAsync reaches the SPE write");
        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the SPE facade must never be called when the patch engine refuses the operation log");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared through-the-wire save helper — same fixture/boundary-mock pattern
    // ComposeNoOpRoundTripByteDiffSeamTests (task 004) established, extended to carry a NON-EMPTY
    // operation log / comments.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private async Task<(HttpResponseMessage Response, byte[]? Persisted, string DocName)> PostSaveThroughWireAsync(
        string docName,
        byte[] original,
        ComposeOperationLog? operationLog,
        IReadOnlyList<object>? comments)
    {
        _fixture.ResetBoundaries();

        var speId = $"spe-item-patch-seam-{Guid.NewGuid():N}";
        var driveId = $"drive-patch-seam-{Guid.NewGuid():N}";
        var existingDocumentId = Guid.NewGuid();

        string sessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(ComposeFidelitySeamFixture.TestTenantId, documentId: speId);
            sessionId = session.SessionId;
        }

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

        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            driveId,
            content = original,
            operationLog,
            comments,
        });

        return (response, persisted, docName);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Corpus-reading helpers — resolve well-known addressable structure WITHOUT text-search (they read
    // OOXML structure/ids, never scan run text for a content match). Mirrors the established pattern in
    // ComposeShadowPatchEngineByteDiffSeamTests.ReadParagraphSnapshot (task 030).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>First addressable paragraph carrying a non-empty run of text, plus the OuterXml of every
    /// OTHER id-bearing paragraph (the untouched-subtree baseline for the "zero drift" assertion).</summary>
    private static (string ParaId, List<string> OtherParagraphOuterXmlBefore)? FindAddressableParagraphWithRunText(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToList();

        var target = paragraphs.FirstOrDefault(p =>
            !string.IsNullOrEmpty(p.ParagraphId?.Value) && p.Descendants<Run>().Any(r => r.GetFirstChild<Text>() is not null));

        if (target?.ParagraphId?.Value is not { } targetId)
        {
            return null;
        }

        var others = paragraphs
            .Where(p => !ReferenceEquals(p, target))
            .Select(p => p.OuterXml)
            .ToList();

        return (targetId, others);
    }

    private static List<string> ReadOtherParagraphOuterXml(byte[] bytes, string excludedParaId)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Where(p => !string.Equals(p.ParagraphId?.Value, excludedParaId, StringComparison.OrdinalIgnoreCase))
            .Select(p => p.OuterXml)
            .ToList();
    }

    /// <summary>The first addressable, top-level paragraph whose run-0 text is long enough to split
    /// meaningfully (>= 4 chars) — the split point lands at the midpoint.</summary>
    private static (string ParaId, int Run0TextLength)? FindSplittableParagraph(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        foreach (var p in doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>())
        {
            var id = p.ParagraphId?.Value;
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var text = p.Descendants<Run>().FirstOrDefault()?.GetFirstChild<Text>()?.Text;
            if (!string.IsNullOrEmpty(text) && text!.Length >= 4)
            {
                return (id!, text.Length);
            }
        }

        return null;
    }

    /// <summary>The first TWO adjacent top-level paragraphs that both carry a paraId — the
    /// mergeParagraph precondition (immediate predecessor/successor siblings, same container).</summary>
    private static (string TargetParaId, string SourceParaId)? FindAdjacentParagraphIdPair(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToList();

        for (var i = 0; i < paragraphs.Count - 1; i++)
        {
            var targetId = paragraphs[i].ParagraphId?.Value;
            var sourceId = paragraphs[i + 1].ParagraphId?.Value;
            if (!string.IsNullOrEmpty(targetId) && !string.IsNullOrEmpty(sourceId)
                && !string.Equals(targetId, sourceId, StringComparison.OrdinalIgnoreCase))
            {
                return (targetId!, sourceId!);
            }
        }

        return null;
    }

    private static string? FindAnyAddressableParagraphId(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .FirstOrDefault(id => !string.IsNullOrEmpty(id));
    }

    /// <summary>Mints a fresh, VALID (<c>ST_LongHexNumber</c>-range) 8-hex paraId that does not collide
    /// with any id already present in <paramref name="bytes"/>. Deterministic linear probe from a fixed
    /// seed — no randomness, so a failing assertion is reproducible.</summary>
    private static string MintFreshParaId(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var existing = new HashSet<string>(
            doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
                .Select(p => p.ParagraphId?.Value)
                .Where(v => !string.IsNullOrEmpty(v))!,
            StringComparer.OrdinalIgnoreCase);

        for (uint candidate = 0x10000000; candidate < 0x20000000; candidate++)
        {
            var hex = candidate.ToString("X8");
            if (!existing.Contains(hex))
            {
                return hex;
            }
        }

        throw new InvalidOperationException("Could not mint a fresh paraId (search space exhausted) — should never happen for a corpus doc.");
    }

    /// <summary>Resolves a paragraph by <c>w14:paraId</c> from an ALREADY-OPEN document's <paramref name="body"/>
    /// (never a live <see cref="WordprocessingDocument"/> disposal-boundary crossing — callers open the
    /// document in a <c>using</c> block and query within it). Returns null (not a thrown exception) when
    /// absent, so callers assert presence/absence with a clear FluentAssertions message.</summary>
    private static Paragraph? ParagraphById(Body body, string paraId) =>
        body.Descendants<Paragraph>()
            .FirstOrDefault(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));
}
