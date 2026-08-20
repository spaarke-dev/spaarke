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
// corpus loader, no new fixture). ADR-049 (R6): byte-identity is NOT asserted on the save path —
// the gate asserts round-trip success + edit presence + warn-not-fail degradation instead.

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

        return warnings.Count > 0
            ? DocumentFidelityResult.Warn(documentName, warnings)
            : DocumentFidelityResult.Pass(documentName);
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
                })
                .ToArray(),
        };

        var path = Path.Combine(AppContext.BaseDirectory, ResultFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, SerializerOptions));
    }
}

/// <summary>One corpus document's gate classification: pass (clean) | warn (degradation warnings —
/// hard-tier accept-flatten, never a failure) | fail (hard-fail HTTP or fidelity regression).</summary>
public sealed record DocumentFidelityResult(
    string Name,
    string Status,
    IReadOnlyDictionary<string, int> Warnings,
    string? FailureReason)
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
