using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 4 of the <c>ComposeService</c> decomposition (task 070): how a PDF becomes an editable
/// document, and how that origin is remembered.
///
/// <para><b>Its reason to change</b> is PDF provenance — the bytes-first decision that a source IS a
/// PDF, the one intake leg that turns it into a synthesized <c>.docx</c>, and the two cache keys that
/// remember what a session was opened FROM and what that PDF BECAME. None of that moves when the save
/// path changes, which is what makes it a seam.</para>
///
/// <para><b>Intake and provenance are one cluster, not two.</b> They look separable — a projection
/// step and two cache keys — but the markers exist only because the intake is LOSSY and
/// NON-IDEMPOTENT: projecting the same PDF twice mints a second Word document, so the mapping from
/// source to derived document is the only thing that makes a re-open resume rather than duplicate.
/// Splitting them would leave the markers as unexplained cache plumbing.</para>
///
/// <para><b>Two keys because they answer two different questions</b>, and neither substitutes for the
/// other — this is preserved verbatim from the original block comment because it is the thing most
/// easily lost in a refactor:</para>
/// <list type="bullet">
/// <item><c>pdf-session:{sessionId}</c> — the source PDF's coordinates. Written at load, read at save.
///   Carries the server's own bytes-first determination forward so the save neither re-derives it nor
///   takes the client's word.</item>
/// <item><c>pdf-derived:{driveId}:{speId}</c> — the Word document that PDF became. Written at save,
///   read at the NEXT load of that PDF. This is what survives a page refresh.</item>
/// </list>
///
/// <para><b>Every provenance operation is best-effort and swallows its own failures.</b> Losing either
/// key degrades to the pre-044 behaviour (re-project the PDF, stamp by routing origin) — worse, but
/// never wrong in a way the user cannot see, and never a failed Load or Save. That asymmetry is
/// deliberate: this is a recovery aid, and a recovery aid must not become a new way to fail.</para>
///
/// <para><b><see cref="IsPdfSource"/> is <c>internal static</c></b> because <c>ComposeService</c>'s two
/// mount paths read it to decide whether they are on the PDF branch at all — the decision to enter this
/// type is necessarily made outside it. Same shape as cluster 1's refusal predicate and cluster 3's
/// coordinate check.</para>
///
/// <para>An <c>internal sealed</c> collaborator built from dependencies <c>ComposeService</c> already
/// holds — <b>no new DI registration</b> (ADR-010). Behaviour is unchanged; this is a move.</para>
/// </summary>
internal sealed class ComposePdfIntakeCoordinator
{
    private readonly IComposePdfIntakeSource? _pdfIntakeSource;
    private readonly ComposePdfModelProjector _pdfModelProjector;
    private readonly ComposeDocumentRenderer _documentRenderer;
    private readonly ISpeFileOperations _spe;

    /// <param name="cache">ADR-009 Redis. NULL is supported: a host without a distributed cache simply
    /// keeps no provenance, and every read degrades to "no marker" — a fresh projection, never an
    /// error.</param>
    private readonly IDistributedCache? _cache;
    private readonly ILogger _logger;

    internal ComposePdfIntakeCoordinator(
        IComposePdfIntakeSource? pdfIntakeSource,
        ComposePdfModelProjector pdfModelProjector,
        ComposeDocumentRenderer documentRenderer,
        ISpeFileOperations spe,
        IDistributedCache? cache,
        ILogger logger)
    {
        _pdfIntakeSource = pdfIntakeSource;
        _pdfModelProjector = pdfModelProjector;
        _documentRenderer = documentRenderer;
        _spe = spe;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Task 040 (FR-06): PDF source detection — BYTES FIRST (Step-9.5 MEDIUM-5), extension as
    /// tiebreak, so a mis-named file lands on the branch its bytes belong to: a docx (PK-zip) named
    /// <c>.pdf</c> takes the native full-fidelity OOXML path (NOT the lossy reflow), and a PDF named
    /// <c>.docx</c> takes the intake path (it would otherwise fail-closed on the OOXML projection).
    /// Only when the bytes are neither signature does the extension decide.
    /// </summary>
    internal static bool IsPdfSource(string? fileName, ReadOnlySpan<byte> content)
    {
        // %PDF- → PDF regardless of name.
        if (content.Length >= 5
            && content[0] == (byte)'%' && content[1] == (byte)'P' && content[2] == (byte)'D'
            && content[3] == (byte)'F' && content[4] == (byte)'-')
        {
            return true;
        }

        // PK\x03\x04 (OOXML zip container) → NOT a PDF regardless of name.
        if (content.Length >= 4
            && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04)
        {
            return false;
        }

        return fileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Task 040 (FR-06): the PDF → canonical model → synthesized-docx intake leg. Throws
    /// <see cref="InvalidOperationException"/> with a clear, user-presentable reason on
    /// unavailability/failure — the Compose load endpoint surfaces it as a ProblemDetails failure,
    /// never a silent empty mount. Counts-only logging (privacy — no document text).
    /// </summary>
    internal async Task<(ReadOnlyMemory<byte> DocxBytes, IReadOnlyList<ComposeProjectionWarning> IntakeWarnings)> ProjectPdfToDocxAsync(
        ReadOnlyMemory<byte> pdfBytes,
        string fileName,
        string driveId,
        string documentSpeId,
        CancellationToken cancellationToken)
    {
        if (_pdfIntakeSource is null)
        {
            // Step-9.5 HIGH-1: TYPED throw — the load endpoint maps Unavailable=true to a 503
            // ProblemDetails carrying this exact message (never the generic catch-all 500).
            throw new ComposePdfIntakeException(
                "PDF intake is unavailable: AI document parsing is disabled on this host " +
                "(Analysis:Enabled + DocumentIntelligence:Enabled required).",
                unavailable: true);
        }

        // Task 050 / FR-11 (spaarkeai-compose-r7): consume the CAUSE-DISCRIMINATED intake result (task 073's
        // ParseWithDiagnosticsAsync, now on the IComposePdfIntakeSource facade) so the user sees the SPECIFIC
        // reason — circuit-breaker-open / timeout / corrupt-file / disabled — instead of one collapsed
        // "corrupt or unavailable". This became a clean, downcast-free facade consumption (no ADR-013 breach)
        // once the facade's prior sole owner (spaarke-ai-architecture-redesign-r2) closed and R7 took ownership.
        var intake = await _pdfIntakeSource.ParseWithDiagnosticsAsync(pdfBytes.ToArray(), fileName, cancellationToken)
            .ConfigureAwait(false);
        if (!intake.Succeeded)
        {
            // 503 (retryable) for service-side / transient causes — circuit-open, timeout, unknown, and the
            // ADR-032 gate-off "disabled" (which rides Unknown); 422 (not retryable — the document itself is
            // the problem) ONLY for Corrupt. Mirrors the load endpoint's own 503-vs-422 split. The message is
            // the facade's cause-specific text (honest-lossiness: the real reason crosses the wire).
            var unavailable = intake.FailureCause != PdfIntakeFailureCause.Corrupt;
            throw new ComposePdfIntakeException(
                intake.FailureMessage
                    ?? $"PDF intake failed: the document layout could not be extracted from '{fileName}'.",
                unavailable);
        }
        var layout = intake.Layout!;

        var projection = _pdfModelProjector.Project(layout);
        if (projection.Status == ComposeProjectionStatus.Failed)
        {
            // The projector's only Failed outcome is "nothing projectable" — mounting an empty
            // editor over a non-empty PDF would be a silent lie (projection contract). 422 — the
            // document itself is the problem; retrying won't change the outcome.
            throw new ComposePdfIntakeException(
                $"PDF intake failed: no editable content could be projected from '{fileName}'.",
                unavailable: false);
        }

        // Render the model through the ONE renderer (render-on-save hub) — the synthesized docx is a
        // first-class imported carrier for everything downstream (paraIds minted by the renderer).
        var intakeWarnings = new List<ComposeProjectionWarning>(projection.Warnings);
        var docxBytes = _documentRenderer.SynthesizeDocument(projection.Model, author: "Spaarke Compose", intakeWarnings);

        _logger.LogInformation(
            "Compose load: PDF intake projected drive={DriveId} item={DocumentSpeId} into the canonical model " +
            "({Pages} source pages, {Blocks} blocks); degradations={Warnings}",
            driveId, documentSpeId, layout.PageCount, projection.Model.Blocks.Count,
            string.Join(",", intakeWarnings.Select(w => $"{w.Code}:{w.Count}")));

        return (docxBytes, intakeWarnings);
    }

    // =========================================================================
    // FR-A08/FR-A09 (r8 task 044) — PDF provenance: what a session was opened FROM, and what that PDF
    // BECAME. Two keys because they answer two different questions and neither substitutes for the other:
    //
    //   pdf-session:{sessionId}        -> the source PDF's coordinates. Written at load, read at save.
    //                                    Carries the server's own bytes-first PDF determination forward
    //                                    so the save neither re-derives it nor takes the client's word.
    //   pdf-derived:{driveId}:{speId}  -> the Word document that PDF became. Written at save, read at the
    //                                    NEXT load of that PDF. This is what survives a page refresh.
    //
    // IDistributedCache throughout (ADR-009 — never IMemoryCache): the refresh case is a DIFFERENT request
    // on a possibly different instance, which is exactly the cross-request boundary the ADR is about.
    //
    // Every operation is best-effort and swallows its own failures. Losing either key degrades to the
    // pre-044 behavior (re-project the PDF, stamp by routing origin) — worse, but never wrong in a way the
    // user cannot see, and never a failed Load or Save. That asymmetry is deliberate: this is a recovery
    // aid, and a recovery aid must not become a new way to fail.
    // =========================================================================

    private const string PdfSourceMarkerKeyPrefix = "sdap:compose:pdf-session:";
    private const string PdfDerivedDocumentKeyPrefix = "sdap:compose:pdf-derived:";

    /// <summary>How long a PDF keeps pointing at the document it became. Long enough to cover working on a
    /// document across days; bounded so a PDF that is deleted and replaced at the same drive-item id cannot
    /// redirect indefinitely. On expiry the behavior degrades to a fresh projection, never to an error.</summary>
    private static readonly DistributedCacheEntryOptions PdfProvenanceCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
    };

    /// <summary>The source PDF a Compose session was opened from (FR-A08/FR-A09).</summary>
    internal sealed record ComposePdfSourceMarker(
        [property: JsonPropertyName("driveId")] string DriveId,
        [property: JsonPropertyName("speId")] string SpeId);

    /// <summary>The Word document a PDF became (FR-A09). Pointer only — see the SetPdfDerivedDocumentAsync
    /// call site for why no version id is stored.</summary>
    internal sealed record ComposePdfDerivedDocument(
        [property: JsonPropertyName("driveId")] string DriveId,
        [property: JsonPropertyName("speId")] string SpeId,
        [property: JsonPropertyName("recordId")] Guid? RecordId,
        [property: JsonPropertyName("derivedAtUtc")] DateTimeOffset DerivedAtUtc);

    internal async Task SetPdfSourceMarkerAsync(string sessionId, string driveId, string speId, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            await _cache.SetStringAsync(
                    PdfSourceMarkerKeyPrefix + sessionId,
                    JsonSerializer.Serialize(new ComposePdfSourceMarker(driveId, speId), ComposeCacheJson.Options),
                    PdfProvenanceCacheOptions,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose load: failed to record the PDF source marker for session={SessionId} (drive={DriveId} item={SpeId}) — " +
                "a save on this session will stamp by routing origin and will not record what this PDF became (FR-A08/FR-A09 degrade).",
                sessionId, driveId, speId);
        }
    }

    internal async Task ClearPdfSourceMarkerAsync(string sessionId, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            await _cache.RemoveAsync(PdfSourceMarkerKeyPrefix + sessionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose load: failed to clear the PDF source marker for session={SessionId} — a save on this session could " +
                "stamp a non-PDF document Authored. Logged loudly because this is the marker's one unsafe direction.",
                sessionId);
        }
    }

    internal async Task<ComposePdfSourceMarker?> GetPdfSourceMarkerAsync(string? sessionId, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        try
        {
            var json = await _cache.GetStringAsync(PdfSourceMarkerKeyPrefix + sessionId, ct).ConfigureAwait(false);
            return json is null ? null : JsonSerializer.Deserialize<ComposePdfSourceMarker>(json, ComposeCacheJson.Options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: failed to read the PDF source marker for session={SessionId} — treating the save as not PDF-sourced " +
                "(stamps by routing origin; records no derived-document mapping).",
                sessionId);
            return null;
        }
    }

    internal async Task SetPdfDerivedDocumentAsync(
        ComposePdfSourceMarker source,
        string derivedDriveId,
        string derivedSpeId,
        Guid? derivedRecordId,
        DateTimeOffset derivedAtUtc,
        CancellationToken ct)
    {
        if (_cache is null)
        {
            return;
        }

        try
        {
            var derived = new ComposePdfDerivedDocument(derivedDriveId, derivedSpeId, derivedRecordId, derivedAtUtc);
            await _cache.SetStringAsync(
                    PdfDerivedDocumentKey(source.DriveId, source.SpeId),
                    JsonSerializer.Serialize(derived, ComposeCacheJson.Options),
                    PdfProvenanceCacheOptions,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: failed to record that PDF drive={SourceDriveId} item={SourceSpeId} became drive={DerivedDriveId} " +
                "item={DerivedSpeId} — the save itself succeeded; a re-open of the PDF will project it afresh (FR-A09 degrades).",
                source.DriveId, source.SpeId, derivedDriveId, derivedSpeId);
        }
    }

    /// <summary>
    /// FR-A09: resolves the Word document a PDF already became, or null to project the PDF afresh.
    /// <para>
    /// A mapping is only honored when the derived document is actually reachable by this caller. Someone who
    /// deletes the Word document is entitled to re-open the PDF and start over, and a dangling mapping would
    /// otherwise fail their load with a 404 on an item they never asked for.
    /// </para>
    /// <para>
    /// The entry is deliberately NOT evicted on a miss — see the probe comment below. The reachability
    /// signal is per-caller; the mapping is not.
    /// </para>
    /// </summary>
    internal async Task<ComposePdfDerivedDocument?> ResolvePdfDerivedDocumentAsync(
        string driveId,
        string speId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (_cache is null)
        {
            return null;
        }

        try
        {
            var json = await _cache.GetStringAsync(PdfDerivedDocumentKey(driveId, speId), ct).ConfigureAwait(false);
            if (json is null)
            {
                return null;
            }

            var derived = JsonSerializer.Deserialize<ComposePdfDerivedDocument>(json, ComposeCacheJson.Options);
            if (derived is null || string.IsNullOrWhiteSpace(derived.DriveId) || string.IsNullOrWhiteSpace(derived.SpeId))
            {
                return null;
            }

            // The probe runs under the CALLER's identity (OBO), so a null here means "this user cannot see
            // it" — which is NOT the same as "it is gone". Deleting the mapping on that signal would let one
            // user without access destroy the recovery path for everyone else on a tenant-scoped, per-item
            // mapping. So: fall through for this caller and leave the entry alone. It expires on its own TTL,
            // and a genuinely deleted document simply falls through for every caller until it does.
            var visibleToCaller = await _spe.GetFileMetadataAsUserAsync(httpContext, derived.DriveId, derived.SpeId, ct)
                .ConfigureAwait(false);
            if (visibleToCaller is not null)
            {
                return derived;
            }

            _logger.LogInformation(
                "Compose load: PDF drive={DriveId} item={SpeId} maps to drive={DerivedDriveId} item={DerivedSpeId}, which this " +
                "caller cannot see (deleted, or no access) — projecting the PDF afresh for them; the mapping is left intact " +
                "for other callers (FR-A09).",
                driveId, speId, derived.DriveId, derived.SpeId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose load: failed to resolve the derived-document mapping for PDF drive={DriveId} item={SpeId} — projecting the " +
                "PDF afresh (FR-A09 degrades; never a failed Load).",
                driveId, speId);
            return null;
        }
    }

    private static string PdfDerivedDocumentKey(string driveId, string speId) =>
        PdfDerivedDocumentKeyPrefix + driveId + ":" + speId;
}

