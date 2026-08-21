// Task 060 (spaarkeai-compose-r6, spec FR-08 + Success Criterion 5 + ADR-049 R6 amendment point 3) —
// THE corpus round-trip fidelity RELEASE GATE. Every `.docx` under `tests/fixtures/compose-corpus/`
// is enumerated DYNAMICALLY at discovery time (xUnit [MemberData] over the shared
// ComposeCorpusFixtureLocator — task 004's owner rule: new corpus docs land in the gate with zero
// code changes) and driven through ONE full wire round trip on the render-on-save path
// (load → representative edit → POST save → reopen), the same driver flow as task 027's
// ComposeFidelityRoundTripSeamTests.
//
// Per-document classification (the gate contract task 061's CI wiring consumes):
//
//   PASS — the round trip is clean: load 200, save 200 (explicitly never 422), reopen 200, the
//          edit is present in the reopened model, and no degradation warnings surfaced.
//   WARN — the round trip succeeded but degradation warnings are present (hard-tier accept-flatten
//          constructs: text boxes / drawings / fields / content controls / etc. — e.g. the NDA's
//          `text-box-flattened`). Hard-tier warns MUST NOT fail the gate (ADR-049 R6 amendment:
//          accept-flatten + warning, never a 422; version history is the safety net).
//   FAIL — a hard-fail: non-success HTTP anywhere in the round trip (422 / 5xx), a failed
//          projection, or the reopened model missing the applied edit (fidelity regression).
//
// The xUnit assertions themselves ARE the gate: every FAIL-class result fails its [Theory] case,
// which is the non-zero `dotnet test` exit CI keys on. A machine-readable per-document result file
// (`fidelity-gate-result.json`, written to AppContext.BaseDirectory by the class-fixture sink's
// finalizer AFTER all documents ran) gives task 061 the per-document breakdown. The
// Gate_SanityProbe fact proves the gate can go red: a deliberately corrupted (truncated) in-memory
// docx through the SAME driver classifies as `fail` — the classifier demonstrably distinguishes
// fail from pass/warn without committing a corrupt corpus fixture.
//
// MAINTAIN-class (release gate; /test-diet KEEP — tests/integration/seam/** vertical-slice KEEP
// path per ADR-038). Through-the-wire WebApplicationFactory only: NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test, NO reflection over private members; mocks ONLY at the
// ComposeFidelitySeamFixture's ISpeFileOperations / IGenericEntityService /
// IPostUploadIndexingEnqueuer boundaries (CLAUDE.md §11 — reuses the 027 fixture + locator, no new
// corpus loader, no new fixture).
//
// ════════════════════════════════════════════════════════════════════════════════════════════════
// TASK 020 UPGRADE (spaarkeai-compose-r8, spec FR-G01/G02/G03) — FROM "DID IT CRASH" TO "WHAT SURVIVED"
// ════════════════════════════════════════════════════════════════════════════════════════════════
//
// Everything above this block describes the gate as R6 shipped it, and the last sentence of it used to
// read: "ADR-049 (R6): byte-identity is NOT asserted on the save path — the gate asserts round-trip
// success + edit presence + warn-not-fail degradation instead." That sentence is the hole R6's silent
// fidelity loss shipped through. A save could rebuild all 40 pages from a five-node editor view, drop
// every tab stop, indent and section break, and this gate stayed green because the HTTP was 200 and the
// edit marker came back.
//
// Three things are added here, and they are MEASUREMENT, not thresholds:
//
//   FR-G01  PRESERVATION ORACLE — after the single-paragraph edit, every OTHER direct `w:body` child is
//           compared against its original through `ComposeBlockPreservationOracle`. Reported as a
//           percentage, split into a NEAR TIER (character formatting, paragraph properties, indentation,
//           tabs, footnote refs, fields) and overall.
//   FR-G02  OUTCOME HONESTY — every corpus save must terminate in a DEFINED member of task 013's closed
//           `ComposeSaveOutcomes` set, and that claim is cross-checked against whether bytes actually
//           reached the SPE facade boundary. This one IS asserted: a save that reports `persisted` while
//           nothing was written is the exact defect Track S existed to remove, and it is never acceptable.
//   FR-G03  TWO COMPARISON LEVELS — lenient (ignores `paraId`/`textId`; detects content loss) and strict
//           (does not; detects identity drift), over ONE comparison engine.
//
// WHY THE PRESERVATION NUMBERS ARE NOT ASSERTED HERE: this harness must run GREEN on current master.
// It is the CONTROL — task 023 runs it on master to publish today's real loss, and tasks 030/031 assert
// against it once a merge model exists. A red gate before the fix exists cannot measure anything; it
// just fails. The numbers land in `fidelity-gate-result.json` and in the test output.
//
// The oracle's own credibility is proved by the `Oracle_*` facts at the bottom of this file: a
// hand-mutated indent must be SEEN, an rsid-only difference must be IGNORED, and a paraId-only
// difference must separate the two levels. Without those, a vacuously-100% oracle is indistinguishable
// from a working one — which is precisely how we got here.

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeFidelityGateHarnessTests :
    IClassFixture<ComposeFidelitySeamFixture>,
    IClassFixture<FidelityGateResultSink>
{
    /// <summary>The representative edit every document receives — a visible text marker whose
    /// presence in the REOPENED model proves the save→reopen leg served the rendered bytes.</summary>
    private const string EditMarker = " [R6-060-FIDELITY-GATE]";

    /// <summary>FR-G02 (task 020) — task 013's CLOSED outcome set, as it appears on the wire. Kept as an
    /// explicit list rather than reflected off the enum so that ADDING a member is a deliberate edit here
    /// too: a new outcome that nobody taught the gate about is exactly the "undefined outcome" FR-S06
    /// forbids, and it should surface as a failing assertion, not be auto-accepted.</summary>
    private static readonly string[] AllDefinedOutcomes =
    {
        ComposeSaveOutcomes.Persisted,
        ComposeSaveOutcomes.PersistedWithWarnings,
        ComposeSaveOutcomes.RefusedStale,
        ComposeSaveOutcomes.RefusedLocked,
        ComposeSaveOutcomes.RefusedInvalid,
        ComposeSaveOutcomes.StorageFailed,
        ComposeSaveOutcomes.PartiallyRecorded,
    };

    /// <summary>The outcomes that CLAIM the document was written. Each one is cross-checked against
    /// whether bytes actually reached the SPE facade boundary and whether the reopen served them.</summary>
    private static readonly HashSet<string> OutcomesClaimingAWrite = new(StringComparer.Ordinal)
    {
        ComposeSaveOutcomes.Persisted,
        ComposeSaveOutcomes.PersistedWithWarnings,
        ComposeSaveOutcomes.PartiallyRecorded,
    };

    private readonly ComposeFidelitySeamFixture _fixture;
    private readonly FidelityGateResultSink _sink;

    public ComposeFidelityGateHarnessTests(ComposeFidelitySeamFixture fixture, FidelityGateResultSink sink)
    {
        _fixture = fixture;
        _sink = sink;
    }

    /// <summary>EVERY `.docx` currently in `tests/fixtures/compose-corpus/`, discovered dynamically
    /// via the shared task-004 locator — NOT a hand-listed subset. A new corpus doc dropped into the
    /// directory is auto-covered by the gate with zero code changes.</summary>
    public static TheoryData<string> CorpusDocumentNames()
    {
        var data = new TheoryData<string>();
        foreach (var path in ComposeCorpusFixtureLocator.EnumerateDocumentPaths())
        {
            data.Add(Path.GetFileName(path));
        }

        return data;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // THE GATE — one wire round trip per corpus document; FAIL-class results fail the test (that IS
    // the non-zero CI exit). Hard-tier degradation warnings classify WARN and do NOT fail.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(CorpusDocumentNames))]
    public async Task CorpusDocument_RoundTripsThroughRenderOnSave_ClassifiesPassOrWarn(string fileName)
    {
        var result = await ClassifyRoundTripAsync(fileName, LoadCorpusBytes(fileName));

        // Record FIRST so the JSON result file carries this document's row even when the gate fails.
        _sink.Record(result);

        result.Status.Should().NotBe(FidelityGateResultSink.StatusFail,
            $"[{fileName}] the round-trip fidelity gate must never see a hard-fail/regression on a " +
            $"corpus document (ADR-049 R6 amendment point 3: hard-tier constructs accept-flatten and " +
            $"WARN, never fail) — failure reason: {result.FailureReason}");

        // ── FR-G02 OUTCOME HONESTY ── asserted, unlike the preservation numbers. A save that terminates
        // outside the closed enum, or claims a write that never reached storage, is the misreporting
        // Track S removed; it must never come back silently.
        result.Outcome.Should().BeOneOf(AllDefinedOutcomes,
            $"[{fileName}] every save must terminate in a DEFINED member of task 013's closed " +
            $"ComposeSaveOutcome set — an unrecognised wire value means an outcome was invented " +
            $"somewhere off the enum, which is the 'undefined content-refusal' FR-S06 forbids");

        result.OutcomeDishonestyReason.Should().BeNull(
            $"[{fileName}] the reported outcome must match what actually persisted");

        // ── FR-G01/G03 PRESERVATION ── MEASURED, not asserted (this harness is the control; thresholds
        // start at task 030/031). Surfaced in the test output so a human reading a CI log sees the real
        // number rather than a bare green tick.
        result.Preservation.Should().NotBeNull(
            $"[{fileName}] a save that persisted bytes must produce a preservation reading — a missing " +
            $"one means the oracle silently did not run, which looks identical to 100%");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // RED-GATE SANITY PROBE — a deliberately corrupted (truncated) docx through the SAME driver must
    // classify as `fail`, proving the gate demonstrably distinguishes fail from pass/warn. The
    // corrupt bytes are in-memory only (never a committed corpus fixture) and are NOT recorded into
    // the per-corpus-document JSON result.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Gate_SanityProbe_CorruptedDocx_ClassifiedAsFail()
    {
        var valid = BuildMinimalDocx();
        var truncated = valid.Take(valid.Length / 2).ToArray();

        var result = await ClassifyRoundTripAsync("corrupted-truncated-synthetic.docx", truncated);

        result.Status.Should().Be(FidelityGateResultSink.StatusFail,
            "a corrupted document must be CLASSIFIED as fail by the same driver the corpus runs " +
            "through — this is the proof the gate can go red and is not vacuously green");
        result.FailureReason.Should().NotBeNullOrWhiteSpace(
            "a fail classification must carry an actionable failure reason for the CI log");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Classification driver — the task-027 wire flow (load → mutate model → save → reopen), returned
    // as a classification instead of asserted inline, so ONE code path serves both the corpus gate
    // and the red-gate sanity probe.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private async Task<DocumentFidelityResult> ClassifyRoundTripAsync(string documentName, byte[] sourceBytes)
    {
        try
        {
            return await RunRoundTripAsync(documentName, sourceBytes);
        }
        catch (Exception ex)
        {
            // Any unhandled harness/server exception is a hard-fail by definition (the gate's
            // "never 5xx/unhandled" contract) — classified, never rethrown, so the sink still gets
            // a row and the Theory's status assertion produces the red result.
            return DocumentFidelityResult.Fail(
                documentName,
                $"unhandled exception during round trip: {ex.GetType().Name}: {Truncate(ex.Message, 400)}");
        }
    }

    private async Task<DocumentFidelityResult> RunRoundTripAsync(string documentName, byte[] sourceBytes)
    {
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var key = SanitizeForId(documentName);
        var speId = $"spe-060-gate-{key}";
        var driveId = $"drive-060-gate-{key}";

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();
        ArrangeSpeLoad(sourceBytes, speId, driveId, eTag: "\"v1\"", versionId: "1.0");

        using var client = _fixture.CreateAuthenticatedClient();
        var warnings = new Dictionary<string, int>(StringComparer.Ordinal);

        // ── LOAD ─────────────────────────────────────────────────────────────────────────────────
        var loadResponse = await client.GetAsync($"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        var loadBody = await loadResponse.Content.ReadAsStringAsync();
        if (!loadResponse.IsSuccessStatusCode)
        {
            return DocumentFidelityResult.Fail(documentName,
                $"load returned HTTP {(int)loadResponse.StatusCode}: {Truncate(loadBody, 300)}");
        }

        var loadRoot = JsonNode.Parse(loadBody)!.AsObject();
        CollectWarnings(loadRoot, warnings);

        if (loadRoot["contentModel"] is not JsonObject)
        {
            return DocumentFidelityResult.Fail(documentName,
                "the canonical content-model projection failed at load (contentModel is null) — the " +
                "document cannot enter the render-on-save path", warnings);
        }

        var sessionId = loadRoot["sessionId"]!.GetValue<string>();

        // ── EDIT (representative: append a visible marker to the first editable text run) ────────
        var editedModel = loadRoot["contentModel"]!.DeepClone()!.AsObject();
        var mutationError = TryAppendMarkerToFirstEditableRun(editedModel, EditMarker);
        if (mutationError is not null)
        {
            return DocumentFidelityResult.Fail(documentName, mutationError, warnings);
        }

        // ── SAVE (post-cutover shape; capture the rendered bytes at the SPE facade boundary) ─────
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
            .ReturnsAsync(BuildFileHandle(speId, driveId, sourceBytes.Length, "\"v2\""));

        var saveBody = new JsonObject
        {
            ["sessionId"] = sessionId,
            ["tenantId"] = tenant,
            ["driveId"] = driveId,
            ["content"] = loadRoot["content"]!.DeepClone(),
            ["contentModel"] = editedModel,
        };
        var saveResponse = await client.PostAsync(
            $"/api/compose/documents/{speId}/save",
            new StringContent(saveBody.ToJsonString(), Encoding.UTF8, "application/json"));
        var saveResponseBody = await saveResponse.Content.ReadAsStringAsync();
        if ((int)saveResponse.StatusCode == 422)
        {
            return DocumentFidelityResult.Fail(documentName,
                $"save returned HTTP 422 — the hard-fail class render-on-save exists to eliminate " +
                $"(ADR-049 R6 amendment: hard-tier constructs must accept-flatten + warn, never 422): " +
                $"{Truncate(saveResponseBody, 300)}", warnings);
        }

        if (!saveResponse.IsSuccessStatusCode)
        {
            return DocumentFidelityResult.Fail(documentName,
                $"save returned HTTP {(int)saveResponse.StatusCode}: {Truncate(saveResponseBody, 300)}", warnings);
        }

        if (persisted is null)
        {
            return DocumentFidelityResult.Fail(documentName,
                "save reported success but no rendered bytes reached the SPE facade boundary", warnings);
        }

        // Bound here because `persisted` is assigned inside the SPE Callback lambda — C# nullable flow
        // analysis does not carry the narrowing above past a captured local.
        var persistedBytes = persisted;

        // ── REOPEN (the persisted rendered bytes become the next load's source) ──────────────────
        ArrangeSpeLoad(persisted, speId, driveId, eTag: "\"v2\"", versionId: "2.0");
        var reopenResponse = await client.GetAsync($"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        var reopenBody = await reopenResponse.Content.ReadAsStringAsync();
        if (!reopenResponse.IsSuccessStatusCode)
        {
            return DocumentFidelityResult.Fail(documentName,
                $"reopen of the persisted bytes returned HTTP {(int)reopenResponse.StatusCode}: {Truncate(reopenBody, 300)}",
                warnings);
        }

        var reopenRoot = JsonNode.Parse(reopenBody)!.AsObject();
        CollectWarnings(reopenRoot, warnings);

        if (reopenRoot["contentModel"] is not JsonObject reopenedModel)
        {
            return DocumentFidelityResult.Fail(documentName,
                "the reopened persisted bytes failed to project (contentModel is null) — the render " +
                "produced a document the load path cannot read back", warnings);
        }

        var reopenText = string.Concat(EnumerateModelRuns(reopenedModel)
            .Select(r => r["text"]?.GetValue<string>() ?? string.Empty));
        if (!reopenText.Contains(EditMarker, StringComparison.Ordinal))
        {
            return DocumentFidelityResult.Fail(documentName,
                $"the applied edit (\"{EditMarker.Trim()}\") is MISSING from the reopened model — the " +
                "save→reopen round trip lost the edit (fidelity regression)", warnings);
        }

        // ── FR-G02 OUTCOME HONESTY (task 020) ──────────────────────────────────────────────────
        // The wire `outcome` is what the CLIENT decides success from (task 013) — not the HTTP status.
        // So the gate reads the same field the client does, and then checks it against ground truth the
        // client cannot see: whether bytes actually reached the SPE facade boundary in this test.
        var reportedOutcome = JsonNode.Parse(saveResponseBody)?["outcome"]?.GetValue<string>();
        var dishonesty = DescribeOutcomeDishonesty(reportedOutcome, bytesReachedStorage: persisted is not null);

        // ── FR-G01 + FR-G03 PRESERVATION ORACLE (task 020) ─────────────────────────────────────
        // Both gate levels, plus the revision-id diagnostic, over ONE engine. Measured only — see the
        // header block for why thresholds do not belong in the control.
        var preservation = new PreservationSummary(
            Lenient: ComposeBlockPreservationOracle.Compare(
                sourceBytes, persistedBytes, EditMarker, ComposeBlockPreservationOracle.ComparisonLevel.Lenient),
            Strict: ComposeBlockPreservationOracle.Compare(
                sourceBytes, persistedBytes, EditMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict),
            StrictIgnoringRevisionIds: ComposeBlockPreservationOracle.Compare(
                sourceBytes, persistedBytes, EditMarker,
                ComposeBlockPreservationOracle.ComparisonLevel.StrictIgnoringRevisionIds));

        var status = warnings.Count > 0
            ? FidelityGateResultSink.StatusWarn
            : FidelityGateResultSink.StatusPass;

        return new DocumentFidelityResult(
            documentName, status, warnings, FailureReason: null, reportedOutcome, dishonesty, preservation);
    }

    /// <summary>
    /// FR-G02 — returns null when the reported outcome is consistent with what actually happened, or a
    /// human-readable description of the contradiction. This is the assertion that makes "HTTP 200 with
    /// nothing written" impossible to ship again: the gate does not trust the status line OR the outcome
    /// string on its own, it compares them against the bytes it saw at the storage boundary.
    /// </summary>
    private static string? DescribeOutcomeDishonesty(string? reportedOutcome, bool bytesReachedStorage)
    {
        if (string.IsNullOrEmpty(reportedOutcome))
        {
            return "the save response carried NO `outcome` field — the client has nothing to decide " +
                   "success from except the HTTP status, which is the pre-FR-S06 failure mode";
        }

        var claimsAWrite = OutcomesClaimingAWrite.Contains(reportedOutcome);

        if (claimsAWrite && !bytesReachedStorage)
        {
            return $"the save reported `{reportedOutcome}`, which claims the document was written, but " +
                   "NO bytes reached the SPE facade boundary — this is a save presenting as success " +
                   "with nothing stored (FR-S06)";
        }

        if (!claimsAWrite && bytesReachedStorage)
        {
            return $"the save reported `{reportedOutcome}`, which claims nothing was written, but bytes " +
                   "DID reach the SPE facade boundary — a refusal that overwrote the stored document is " +
                   "worse than a failure, because the user is told their original is untouched";
        }

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Warning collection — degradation warnings surface on the wire as the load/reopen response's
    // `contentModelWarnings` (canonical-model flatten codes, e.g. `text-box-flattened`) and
    // `projection.warnings` (HTML-projection codes). Merged per code taking the MAX count seen in any
    // single array (the two projections report the same underlying constructs — summing would
    // double-count them; counts are informational for the CI breakdown, the code set is the signal).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private static void CollectWarnings(JsonObject loadOrReopenRoot, Dictionary<string, int> accumulator)
    {
        MergeWarningArray(loadOrReopenRoot["contentModelWarnings"] as JsonArray, accumulator);
        MergeWarningArray(loadOrReopenRoot["projection"]?["warnings"] as JsonArray, accumulator);
    }

    private static void MergeWarningArray(JsonArray? warnings, Dictionary<string, int> accumulator)
    {
        if (warnings is null)
        {
            return;
        }

        foreach (var warning in warnings)
        {
            if (warning is not JsonObject w)
            {
                continue;
            }

            var code = w["code"]?.GetValue<string>();
            if (string.IsNullOrEmpty(code))
            {
                continue;
            }

            var count = w["count"]?.GetValue<int>() ?? 1;
            accumulator[code] = accumulator.TryGetValue(code, out var existing) ? Math.Max(existing, count) : count;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Mutation + model-walk helpers (same JsonNode client-mapper simulation as the 027 driver, but
    // error-returning instead of assert-throwing so the classifier owns the verdict).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Appends the marker to the first text-carrying, editable run (not a page-break marker,
    /// not a comment anchor, not tracked-deleted text) — the minimal "the user typed something" edit.
    /// Returns an error string when no such run exists (a fail-class condition).</summary>
    private static string? TryAppendMarkerToFirstEditableRun(JsonObject model, string marker)
    {
        var run = EnumerateModelRuns(model)
            .FirstOrDefault(r => !string.IsNullOrEmpty(r["text"]?.GetValue<string>())
                && r["isPageBreak"]?.GetValue<bool>() != true
                && r["commentAnchor"] is null
                && !string.Equals(r["revision"]?["kind"]?.GetValue<string>(), "Deleted", StringComparison.OrdinalIgnoreCase));
        if (run is null)
        {
            return "the loaded model exposes no editable text run to apply the representative edit to";
        }

        run["text"] = run["text"]!.GetValue<string>() + marker;
        return null;
    }

    /// <summary>Enumerates every inline run in the model — top-level blocks AND table-cell blocks
    /// (same walk as the 027 driver).</summary>
    private static IEnumerable<JsonObject> EnumerateModelRuns(JsonObject model)
    {
        static IEnumerable<JsonObject> WalkBlocks(JsonArray blocks)
        {
            foreach (var block in blocks)
            {
                if (block is not JsonObject b) continue;
                if (b["runs"] is JsonArray runs)
                {
                    foreach (var run in runs)
                    {
                        if (run is JsonObject r) yield return r;
                    }
                }

                if (b["table"] is JsonObject table && table["rows"] is JsonArray rows)
                {
                    foreach (var row in rows)
                    {
                        if (row is not JsonObject ro || ro["cells"] is not JsonArray cells) continue;
                        foreach (var cell in cells)
                        {
                            if (cell is JsonObject c && c["blocks"] is JsonArray cellBlocks)
                            {
                                foreach (var r in WalkBlocks(cellBlocks)) yield return r;
                            }
                        }
                    }
                }
            }
        }

        return model["blocks"] is JsonArray topBlocks ? WalkBlocks(topBlocks) : Enumerable.Empty<JsonObject>();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Arrange + fixture helpers (mirrors the 027 driver's SPE/Dataverse/indexing boundary set-up).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private void ArrangeSpeLoad(byte[] bytes, string speId, string driveId, string eTag, string versionId)
    {
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, bytes.Length, eTag));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(bytes.ToArray()));
        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(versionId);
    }

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

    /// <summary>Loads a corpus fixture by exact file name via the shared task-004 locator (LFS-guarded).</summary>
    private static byte[] LoadCorpusBytes(string fileName)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    /// <summary>Builds a minimal valid docx (same SDK-built convention as the sibling seam suites) —
    /// the sanity probe truncates its bytes to manufacture the corrupt input in memory.</summary>
    private static byte[] BuildMinimalDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            body.AppendChild(new Paragraph(
                new Run(new Text("Sanity-probe prose the truncation destroys.") { Space = SpaceProcessingModeValues.Preserve })));
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: "fidelity-gate.docx", ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);

    private static string SanitizeForId(string fileName)
    {
        var sanitized = new string(fileName.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());
        return sanitized.Length <= 40 ? sanitized : sanitized[..40];
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // ORACLE SELF-PROOF (task 020) — the reason to believe the preservation numbers.
    //
    // The gate above MEASURES; it does not assert a threshold. That makes one failure mode invisible by
    // construction: an oracle that normalizes away real signal reads 100% and looks exactly like an
    // oracle that works. `Gate_SanityProbe` proves the ROUND TRIP can go red; these facts prove the
    // COMPARISON can — each one drives a hand-built pair through the same engine the corpus runs
    // through, with exactly one thing changed, and pins what the engine is required to say about it.
    //
    // Every fixture here is synthetic and in-memory. Nothing is committed to the corpus: these prove the
    // instrument, they are not documents under measurement.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private const string OracleMarker = " [ORACLE-EDIT]";

    /// <summary>Every fixture below ends its body with a `w:sectPr`, because a real Word body does. It is
    /// a DIRECT `w:body` child, so the oracle counts it as a block — deliberately: section breaks are one
    /// of the losses the owner reports from dev, and an oracle that silently skipped `w:sectPr` could
    /// never see them. The expectations below therefore include it, which is also what documents that
    /// choice.</summary>
    private const int SectionPropertiesBlock = 1;

    [Fact]
    public void Oracle_SeesANearTierLoss_DroppedIndentation()
    {
        var original = BuildDocxWithBody(
            Paragraph("00000001", "Alpha", paragraphProperties: "<w:ind w:left=\"720\" w:firstLine=\"360\"/>") +
            Paragraph("00000002", "Bravo" + OracleMarker));
        var saved = BuildDocxWithBody(
            Paragraph("00000001", "Alpha") +
            Paragraph("00000002", "Bravo" + OracleMarker));

        foreach (var level in new[]
                 {
                     ComposeBlockPreservationOracle.ComparisonLevel.Lenient,
                     ComposeBlockPreservationOracle.ComparisonLevel.Strict,
                 })
        {
            var report = ComposeBlockPreservationOracle.Compare(original, saved, OracleMarker, level);

            report.EditedBlockIndex.Should().Be(1, "the marked block is the one the harness edited");
            report.ComparedBlockCount.Should().Be(1 + SectionPropertiesBlock,
                "the edited block is excluded from the denominator; the paragraph and the sectPr remain");
            report.OverallPreservationPercent.Should().Be(50d,
                $"[{level}] one of the two comparable blocks lost its indentation — an oracle that cannot " +
                "see a dropped w:ind cannot see the loss the owner is looking at in dev");
            report.NearTierRelevantCount.Should().Be(1, "only the paragraph carried a near-tier construct");
            report.NearTierPreservationPercent.Should().Be(0d,
                $"[{level}] indentation IS the near tier the Phase-3 gate demands 100% of");
            report.Differences.Should().ContainSingle().Which.IsNearTier.Should().BeTrue();
            report.Differences[0].DifferingPaths.Should().Contain(path => path.Contains("pPr"),
                "the difference path must NAME the construct — 'a block changed' is not actionable");
        }
    }

    [Fact]
    public void Oracle_IgnoresRsidOnlyDifference_BecauseWordRegeneratesThemEverySave()
    {
        var original = BuildDocxWithBody(
            Paragraph("00000001", "Alpha", rsid: "00AA00AA") + Paragraph("00000002", "Bravo" + OracleMarker));
        var saved = BuildDocxWithBody(
            Paragraph("00000001", "Alpha", rsid: "00BB00BB") + Paragraph("00000002", "Bravo" + OracleMarker));

        foreach (var level in new[]
                 {
                     ComposeBlockPreservationOracle.ComparisonLevel.Lenient,
                     ComposeBlockPreservationOracle.ComparisonLevel.Strict,
                 })
        {
            ComposeBlockPreservationOracle.Compare(original, saved, OracleMarker, level)
                .OverallPreservationPercent.Should().Be(100d,
                    $"[{level}] w:rsid* is Word's revision-save bookkeeping — it is regenerated on " +
                    "essentially every save and two files differing only in rsids render identically. " +
                    "An oracle that counts this as loss reports every document as damaged and is useless");
        }
    }

    [Fact]
    public void Oracle_TwoLevelsDifferExactlyOnParaId_LenientPassesStrictDoesNot()
    {
        // The ONE bit that separates the gate's two levels (FR-G03). Same content, regenerated ids.
        var original = BuildDocxWithBody(
            Paragraph("00000001", "Alpha") + Paragraph("00000002", "Bravo" + OracleMarker));
        var saved = BuildDocxWithBody(
            Paragraph("0000ABCD", "Alpha") + Paragraph("0000ABCE", "Bravo" + OracleMarker));

        var lenient = ComposeBlockPreservationOracle.Compare(
            original, saved, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Lenient);
        var strict = ComposeBlockPreservationOracle.Compare(
            original, saved, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict);

        lenient.OverallPreservationPercent.Should().Be(100d,
            "LENIENT detects CONTENT loss — a block that survived with a regenerated id is not lost, " +
            "and paraId is explicitly not a durable file key");
        strict.OverallPreservationPercent.Should().Be(50d,
            "STRICT detects IDENTITY DRIFT — the session anchors the edit-capture mechanism depends on. " +
            "One of the two comparable blocks (the paragraph) drifted; the sectPr carries no paraId");

        lenient.ParaIdCorroborationMismatchCount.Should().Be(1,
            "identity drift stays VISIBLE even at the lenient level, where the id is normalized out of " +
            "the comparison — otherwise lenient would hide it entirely");
        strict.ParaIdCorroborationMismatchCount.Should().Be(1);
    }

    [Fact]
    public void Oracle_ToleratesNumberingRemap_ButStillSeesNumberingDropped()
    {
        // Remapped: the same list, different raw numId. Legitimate when numbering definitions merge.
        var original = BuildDocxWithBody(
            Paragraph("00000001", "One", paragraphProperties: NumPr(3)) + Paragraph("00000002", "X" + OracleMarker));
        var remapped = BuildDocxWithBody(
            Paragraph("00000001", "One", paragraphProperties: NumPr(17)) + Paragraph("00000002", "X" + OracleMarker));

        ComposeBlockPreservationOracle.Compare(
                original, remapped, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict)
            .OverallPreservationPercent.Should().Be(100d,
                "numId is legitimately remapped when numbering definitions merge, so raw inequality is " +
                "not loss — the oracle canonicalizes to first-appearance ordinals");

        // Dropped: the list association is GONE. This is why the normalization rewrites the value to an
        // ordinal instead of deleting the attribute — deletion would make this case read as preserved.
        var dropped = BuildDocxWithBody(
            Paragraph("00000001", "One") + Paragraph("00000002", "X" + OracleMarker));

        ComposeBlockPreservationOracle.Compare(
                original, dropped, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict)
            .OverallPreservationPercent.Should().Be(50d,
                "a paragraph that lost its numbering association entirely IS loss — tolerating remapping " +
                "must not tolerate removal. The sectPr is the other, unaffected, comparable block");
    }

    [Fact]
    public void Oracle_TreatsTextBoxContentAsOpaque_NotAsBodyBlocks()
    {
        // The constraint this proves: `body.Descendants<Paragraph>()` would yield FOUR paragraphs here
        // (two body-level, two inside the text box) and mis-pair every block after the text box against
        // the wrong original. Direct `w:body` children yield TWO.
        var body = ParagraphWithTextBox("00000001", "Inside one", "Inside two") +
                   Paragraph("00000002", "Bravo" + OracleMarker);
        var docx = BuildDocxWithBody(body);

        var report = ComposeBlockPreservationOracle.Compare(
            docx, docx, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict);

        report.OriginalBlockCount.Should().Be(2 + SectionPropertiesBlock,
            "the FOUR paragraphs `body.Descendants<Paragraph>()` would yield here (two body-level, two " +
            "inside the text box) are only TWO direct body children — descending into a text box " +
            "interleaves its paragraphs into the body sequence and manufactures loss that is not there");
        report.SavedBlockCount.Should().Be(2 + SectionPropertiesBlock);
        report.OverallPreservationPercent.Should().Be(100d, "a document compared against itself is intact");
    }

    [Fact]
    public void Oracle_ReportsDuplicateParaIdsDistinctly_RatherThanSilentlyMisPairing()
    {
        // [MS-DOCX] permits duplicate paraIds across mc:AlternateContent — this is how Word writes every
        // text box. A document in this state must be FLAGGED, because paraId corroboration is worthless
        // there and a reader needs to know the pairing rests on document order alone.
        var docx = BuildDocxWithBody(
            Paragraph("0000DEAD", "Alpha") +
            Paragraph("0000DEAD", "Alpha again") +
            Paragraph("00000003", "Bravo" + OracleMarker));

        var report = ComposeBlockPreservationOracle.Compare(
            docx, docx, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict);

        report.DuplicateParaIdsInOriginal.Should().BeTrue();
        report.DuplicateParaIdsInSaved.Should().BeTrue();
    }

    [Fact]
    public void Oracle_ClassifiesAReplacedElementAsNearTier_WhenEitherSideIsANearTierConstruct()
    {
        // The current corpus' single most common difference is `p/r|pPr` (42 occurrences): the renderer
        // emits a `w:pPr` where the original had a `w:r`. A path segment of the form `a|b` records both
        // sides, and an earlier draft inspected only the LEFT one — which classified this, the dominant
        // near-tier loss in the whole corpus, as not-near-tier.
        var original = BuildDocxWithBody(
            "<w:p w14:paraId=\"00000001\"><w:r><w:t>Alpha</w:t></w:r></w:p>" +
            Paragraph("00000002", "Bravo" + OracleMarker));
        var saved = BuildDocxWithBody(
            "<w:p w14:paraId=\"00000001\"><w:pPr><w:ind w:left=\"720\"/></w:pPr></w:p>" +
            Paragraph("00000002", "Bravo" + OracleMarker));

        var report = ComposeBlockPreservationOracle.Compare(
            original, saved, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict);

        var difference = report.Differences.Should().ContainSingle().Subject;
        difference.DifferingPaths.Should().Contain(path => path.Contains("|"),
            "the path records what was there and what replaced it");
        difference.IsNearTier.Should().BeTrue(
            "the element that REPLACED the run is a w:pPr — reading only the left-hand side of the " +
            "path would miss the most common near-tier loss in the corpus");

        report.NearTierRelevantCount.Should().Be(1,
            "the original block carried no near-tier construct, but the SAVE introduced one — invented " +
            "formatting must not escape the tier by virtue of not having been there before");
        report.NearTierPreservationPercent.Should().Be(0d);
    }

    [Fact]
    public void Oracle_FlagsDuplicateParaIdsInsideOpaqueRegions_TheCanonicalMsDocxCase()
    {
        // [MS-DOCX] permits duplicate paraIds across `mc:AlternateContent`, and Word produces them for
        // every text box: mc:Choice and mc:Fallback carry the SAME paragraphs, ids included. Those ids
        // live inside a region the pairing walk treats as opaque — but opaque means "not descended into
        // for PAIRING", not "invisible for reporting". This is the exact construct task 021 authors.
        var docx = BuildDocxWithBody(
            ParagraphWithTextBox("00000001", "Inside one") +
            Paragraph("00000002", "Bravo" + OracleMarker));

        var report = ComposeBlockPreservationOracle.Compare(
            docx, docx, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict);

        report.DuplicateParaIdsInOriginal.Should().BeTrue(
            "mc:Choice and mc:Fallback carry the same inner paragraph id — a body-level-only scan " +
            "reports false here and the flag is silent on the one case it exists for");
        report.DuplicateParaIdsInSaved.Should().BeTrue();
    }

    [Fact]
    public void Oracle_ReportsBlockCountDrift_WhenTheSaveDropsAParagraph()
    {
        // The R3 empty-paragraph-drift defect class (48 paragraphs in, 39 out). The percentage alone
        // would be read as "the surviving blocks are fine"; the unpaired count is what says a block is
        // simply GONE, and that a dropped block also mis-pairs everything after it.
        var original = BuildDocxWithBody(
            Paragraph("00000001", "Alpha") + Paragraph("00000002", "Beta") + Paragraph("00000003", "X" + OracleMarker));
        var saved = BuildDocxWithBody(
            Paragraph("00000001", "Alpha") + Paragraph("00000003", "X" + OracleMarker));

        var report = ComposeBlockPreservationOracle.Compare(
            original, saved, OracleMarker, ComposeBlockPreservationOracle.ComparisonLevel.Strict);

        report.BlockCountDrifted.Should().BeTrue();
        report.UnpairedOriginalCount.Should().Be(1, "one original block has no counterpart — it was dropped");
        report.OriginalBlockCount.Should().Be(3 + SectionPropertiesBlock);
        report.SavedBlockCount.Should().Be(2 + SectionPropertiesBlock);
    }

    [Fact]
    public void Gate_CorpusEnumerationIsDynamic_NewDocumentNeedsZeroCodeChanges()
    {
        // FR-G08 — the property that makes tasks 021/022 pure fixture work: dropping a .docx into
        // tests/fixtures/compose-corpus/ puts it through this gate with no edit to any .cs file.
        var enumerated = CorpusDocumentNames().Select(row => (string)row[0]!).ToList();
        var onDisk = ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(Path.GetFileName).ToList();

        enumerated.Should().BeEquivalentTo(onDisk,
            "the [MemberData] source must be the live directory glob, never a hand-maintained list — a " +
            "hard-coded list is how a corpus document silently stops being gated");
        enumerated.Should().NotBeEmpty("an empty corpus makes the gate vacuously green");
    }

    // ── Synthetic OOXML builders for the self-proof facts ────────────────────────────────────────

    private const string WNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string W14Ns = "http://schemas.microsoft.com/office/word/2010/wordml";
    private const string McNs = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private const string WpNs = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private static string Paragraph(
        string paraId, string text, string? paragraphProperties = null, string? rsid = null)
    {
        var rsidAttr = rsid is null ? string.Empty : $" w:rsidR=\"{rsid}\" w:rsidRDefault=\"{rsid}\"";
        var pPr = paragraphProperties is null ? string.Empty : $"<w:pPr>{paragraphProperties}</w:pPr>";
        return $"<w:p w14:paraId=\"{paraId}\" w14:textId=\"{paraId}\"{rsidAttr}>{pPr}" +
               $"<w:r><w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(text)}</w:t></w:r></w:p>";
    }

    private static string NumPr(int numId) => $"<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"{numId}\"/></w:numPr>";

    /// <summary>A body paragraph whose run contains a text box — the `mc:AlternateContent` shape Word
    /// emits, with real `w:p` elements nested inside `w:txbxContent`.</summary>
    private static string ParagraphWithTextBox(string paraId, params string[] innerTexts)
    {
        var inner = string.Concat(innerTexts.Select((t, i) => Paragraph($"BOX0000{i}", t)));
        return $"<w:p w14:paraId=\"{paraId}\" w14:textId=\"{paraId}\"><w:r><mc:AlternateContent>" +
               $"<mc:Choice Requires=\"wps\"><w:drawing><wp:inline><w:txbxContent>{inner}</w:txbxContent>" +
               "</wp:inline></w:drawing></mc:Choice>" +
               $"<mc:Fallback><w:pict><w:txbxContent>{inner}</w:txbxContent></w:pict></mc:Fallback>" +
               "</mc:AlternateContent></w:r></w:p>";
    }

    /// <summary>Builds a real .docx package whose `word/document.xml` body is EXACTLY the supplied XML.
    /// The part is written raw rather than through the SDK's typed object model so the fixture controls
    /// the bytes the oracle reads — an SDK round trip would normalize away the very differences some of
    /// these facts exist to detect.</summary>
    private static byte[] BuildDocxWithBody(string bodyInnerXml)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var xml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                $"<w:document xmlns:w=\"{WNs}\" xmlns:w14=\"{W14Ns}\" xmlns:mc=\"{McNs}\" xmlns:wp=\"{WpNs}\" " +
                "mc:Ignorable=\"w14\">" +
                $"<w:body>{bodyInnerXml}<w:sectPr/></w:body></w:document>";

            using var partStream = mainPart.GetStream(FileMode.Create, FileAccess.Write);
            using var writer = new StreamWriter(partStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(xml);
        }

        return stream.ToArray();
    }
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
// Result sink — an xUnit class fixture: created before the first gate test, disposed AFTER the last
// one, at which point it writes ONE machine-readable JSON result file aggregating every corpus
// document's classification. Task 061's CI wiring consumes this file for the per-document
// breakdown; the red/green signal itself is the `dotnet test` exit code from the Theory asserts.
// ════════════════════════════════════════════════════════════════════════════════════════════════

public sealed class FidelityGateResultSink : IDisposable
{
    public const string StatusPass = "pass";
    public const string StatusWarn = "warn";
    public const string StatusFail = "fail";

    public const string ResultFileName = "fidelity-gate-result.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly ConcurrentDictionary<string, DocumentFidelityResult> _results = new(StringComparer.Ordinal);

    public void Record(DocumentFidelityResult result) => _results[result.Name] = result;

    public void Dispose()
    {
        var payload = new
        {
            harness = nameof(ComposeFidelityGateHarnessTests),
            generatedAtUtc = DateTimeOffset.UtcNow,
            documents = _results.Values
                .OrderBy(r => r.Name, StringComparer.Ordinal)
                .Select(r => new
                {
                    name = r.Name,
                    status = r.Status,
                    warnings = r.Warnings
                        .OrderBy(w => w.Key, StringComparer.Ordinal)
                        .Select(w => new { code = w.Key, count = w.Value })
                        .ToArray(),
                    failureReason = r.FailureReason,
                    // FR-G02 (task 020)
                    outcome = r.Outcome,
                    outcomeDishonestyReason = r.OutcomeDishonestyReason,
                    // FR-G01 + FR-G03 (task 020) — what task 023 publishes as the control and what
                    // tasks 030/031 assert a merge model against.
                    preservation = r.Preservation is null ? null : new
                    {
                        lenient = Describe(r.Preservation.Lenient),
                        strict = Describe(r.Preservation.Strict),
                        strictIgnoringRevisionIds = Describe(r.Preservation.StrictIgnoringRevisionIds),
                    },
                })
                .ToArray(),
        };

        var path = Path.Combine(AppContext.BaseDirectory, ResultFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, SerializerOptions));
    }

    private const int MaxReportedDifferences = 25;

    /// <summary>Flattens one oracle report for the JSON control file. The difference LIST is capped at
    /// the 25 worst so a badly-degraded 100-page document does not produce a megabyte of CI artifact —
    /// the COUNTS above it are complete and are what the gate reasons about; the paths are a debugging
    /// aid. The cap is stated in the payload itself so a truncated list can never be misread as the
    /// whole story.</summary>
    private static object Describe(ComposeBlockPreservationOracle.PreservationReport report) => new
    {
        // Null (not 100) when the denominator is empty — "not measured" must never serialize as
        // "measured, nothing lost". The paired *Measured flags make that explicit for a reader who
        // skims the percentages.
        overallPreservationPercent = report.OverallPreservationPercent is { } o ? Math.Round(o, 2) : (double?)null,
        overallMeasured = report.OverallPreservationPercent is not null,
        nearTierPreservationPercent = report.NearTierPreservationPercent is { } n ? Math.Round(n, 2) : (double?)null,
        nearTierMeasured = report.NearTierPreservationPercent is not null,
        originalBlockCount = report.OriginalBlockCount,
        savedBlockCount = report.SavedBlockCount,
        blockCountDrifted = report.BlockCountDrifted,
        editedBlockIndex = report.EditedBlockIndex,
        comparedBlockCount = report.ComparedBlockCount,
        preservedBlockCount = report.PreservedBlockCount,
        nearTierRelevantCount = report.NearTierRelevantCount,
        nearTierPreservedCount = report.NearTierPreservedCount,
        unpairedOriginalCount = report.UnpairedOriginalCount,
        unpairedSavedCount = report.UnpairedSavedCount,
        duplicateParaIdsInOriginal = report.DuplicateParaIdsInOriginal,
        duplicateParaIdsInSaved = report.DuplicateParaIdsInSaved,
        paraIdCorroborationMismatchCount = report.ParaIdCorroborationMismatchCount,
        differenceCount = report.Differences.Count,
        nearTierDifferenceCount = report.Differences.Count(d => d.IsNearTier),
        differencesTruncatedTo = Math.Min(report.Differences.Count, MaxReportedDifferences),
        differences = report.Differences
            .OrderByDescending(d => d.IsNearTier)
            .ThenBy(d => d.Index)
            .Take(MaxReportedDifferences)
            .Select(d => new
            {
                index = d.Index,
                block = d.BlockElement,
                originalParaId = d.OriginalParaId,
                savedParaId = d.SavedParaId,
                nearTier = d.IsNearTier,
                paths = d.DifferingPaths,
            })
            .ToArray(),
    };
}

/// <summary>FR-G01 + FR-G03 (task 020) — the same document measured at both GATE levels plus the
/// revision-id DIAGNOSTIC. Holding all three lets task 023 publish a control that distinguishes real
/// content loss from identity drift from mere revision-id renumbering, instead of one blended number
/// that cannot tell an architecture decision anything.</summary>
public sealed record PreservationSummary(
    ComposeBlockPreservationOracle.PreservationReport Lenient,
    ComposeBlockPreservationOracle.PreservationReport Strict,
    ComposeBlockPreservationOracle.PreservationReport StrictIgnoringRevisionIds);

/// <summary>One corpus document's gate classification: pass (clean) | warn (degradation warnings —
/// hard-tier accept-flatten, never a failure) | fail (hard-fail HTTP or fidelity regression), plus
/// task 020's terminal save outcome, its honesty verdict, and the preservation reading.</summary>
public sealed record DocumentFidelityResult(
    string Name,
    string Status,
    IReadOnlyDictionary<string, int> Warnings,
    string? FailureReason,
    string? Outcome = null,
    string? OutcomeDishonestyReason = null,
    PreservationSummary? Preservation = null)
{
    private static readonly IReadOnlyDictionary<string, int> NoWarnings =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public static DocumentFidelityResult Pass(string name) =>
        new(name, FidelityGateResultSink.StatusPass, NoWarnings, FailureReason: null);

    public static DocumentFidelityResult Warn(string name, IReadOnlyDictionary<string, int> warnings) =>
        new(name, FidelityGateResultSink.StatusWarn, warnings, FailureReason: null);

    public static DocumentFidelityResult Fail(
        string name, string reason, IReadOnlyDictionary<string, int>? warnings = null) =>
        new(name, FidelityGateResultSink.StatusFail, warnings ?? NoWarnings, reason);
}
