using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Compose.Operations;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 3 of the <c>ComposeService</c> decomposition (task 070): the storage boundary of a save —
/// which bytes a save starts from, under what precondition it writes, and what it remembers about the
/// version it wrote.
///
/// <para><b>Its reason to change</b> is the storage/concurrency contract: <c>If-Match</c>,
/// last-writer-wins, and the version stamp that makes staleness detectable on the NEXT save. That is
/// independent of what a save DOES to the content — <c>SaveAsync</c> owns the fork between the
/// content-model and op-log paths; this type owns the two ends of it, the read that produces a baseline
/// and the write that lands bytes.</para>
///
/// <para><b>Why the three ends are one cluster and not three.</b> They share a single invariant that is
/// easy to break by touching any one of them alone: the bytes a save starts from, the version it asserts
/// against, and the stamp it leaves behind must describe the SAME version. Resolve a baseline from one
/// version, precondition on another, and stamp a third, and staleness detection silently stops working —
/// no test would fail, and the next save would re-anchor against the wrong base. Keeping them together
/// is what makes that invariant reviewable in one place.</para>
///
/// <para><b>Two members are <c>internal static</c> for callers outside the cluster.</b>
/// <see cref="HasBaselineVersionCoordinates"/> is also read by <c>SaveAsync</c>, which decides whether a
/// save can proceed at all before any baseline work starts — it defines the FR-06 coordinate contract,
/// so it lives here and the outside caller references it, matching the call made for cluster 5b's signal
/// factories and cluster 1's refusal predicate.</para>
///
/// <para><b>Two things deliberately did NOT move</b>, recorded so the omissions read as decisions:
/// <list type="bullet">
/// <item><c>ComposeService.ConcurrentExternalChangeCode</c> — physically co-located with this code and
///   about concurrency, but its reason to change is the CLIENT banner contract it mirrors
///   (<c>ComposeBannerStack.tsx</c>), and its only caller is <c>SaveAsync</c>. Moving it would buy a
///   reference and nothing else.</item>
/// <item><c>ComposeCacheJson.Options</c> — shared with cluster 4 (the PDF provenance markers),
///   so it cannot travel with this cluster without either duplicating a serializer configuration or
///   pulling cluster 4 along early. It stays as the single definition both reference; when cluster 4
///   moves, whichever collaborator ends up owning cache-payload serialization should take it.</item>
/// </list></para>
///
/// <para>An <c>internal sealed</c> collaborator built from dependencies <c>ComposeService</c> already
/// holds — <b>no new DI registration</b> (ADR-010). Behaviour is unchanged; this is a move.</para>
/// </summary>
internal sealed class ComposeSaveStorageCoordinator
{
    private const string SaveVersionStampKeyPrefix = "sdap:compose:save-stamp:";

    private readonly ISpeFileOperations _spe;
    private readonly ComposeDocumentRenderer _documentRenderer;

    /// <param name="cache">
    /// ADR-009 Redis. NULL is a supported state, not a defect: a bare test host has no distributed
    /// cache, and the version stamp degrades to "no prior stamp" — never a false-positive staleness
    /// re-anchor and never a blocked save.
    /// </param>
    private readonly IDistributedCache? _cache;
    private readonly ILogger _logger;

    internal ComposeSaveStorageCoordinator(
        ISpeFileOperations spe,
        ComposeDocumentRenderer documentRenderer,
        IDistributedCache? cache,
        ILogger logger)
    {
        _spe = spe;
        _documentRenderer = documentRenderer;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// E1 baseline resolution (FR-06, task 022, Option C — design §4.3): returns the retained LOAD-TIME
    /// ORIGINAL bytes the save delta applies onto. Resolution order:
    /// <list type="number">
    /// <item><b>Same-session fast-path</b> — <see cref="SaveComposeDocumentRequest.Content"/> when present:
    /// the client still holds the pristine mount payload (<c>state.docxBytes</c>, the ORIGINAL — never a
    /// reconstruction). Also the create-on-save document bytes.</item>
    /// <item><b>FR-06 primary</b> — re-fetch the load-time SPE version by
    /// <see cref="SaveComposeDocumentRequest.BaselineVersionId"/> via
    /// <c>ISpeFileOperations.DownloadFileVersionAsUserAsync</c> (task 002; behind the <c>SpeFileStore</c>
    /// facade — ADR-007). Covers a save after the client lost its in-memory bytes (page refresh); the
    /// load-time version stays addressable even after later dirty saves advance the CURRENT version.</item>
    /// </list>
    /// A dirty save NEVER falls back to a client reconstruction (FR-01) — an unresolvable baseline is a
    /// clear error, not a lossy rebuild.
    /// <para>
    /// <b>Tier-3 Redis fallback (design §4.3, deferred — §6.5 Path-A scoping)</b>: the size-capped Redis
    /// cache of the load-time original is an OPTIMIZATION to avoid the SPE re-fetch, not a correctness
    /// requirement — the <see cref="SaveComposeDocumentRequest.BaselineVersionId"/> fetch already
    /// discharges FR-06 baseline retrieval. Populating it requires a Load-path write (out of task-022's
    /// file scope; Load is task 010/024). Deferred to keep this cutover to the SaveAsync inversion; the
    /// fast-path + versionId cover every real save case.
    /// </para>
    /// </summary>
    internal async Task<(byte[] Bytes, IReadOnlyList<ComposeProjectionWarning>? RenderDegradations)> ResolveSaveBaselineAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.ContentModel is not null)
        {
            // Task 026 (FR-04): the render degradation sink — dropped anchors / format-change records /
            // hrefs surface as SUCCESS-WITH-WARNINGS, never a 422 and never silent.
            var renderDegradations = new List<ComposeProjectionWarning>();
            var revisionAuthor = ComposeService.ResolveRevisionAuthor(httpContext);

            // (a1) IMPORTED RENDER-ON-SAVE (task 010 — the cutover; spec FR-01/FR-02, ADR-049 Path-B
            //      amendment): a ContentModel WITH a resolvable retained baseline renders INTO that
            //      carrier (RenderIntoCarrier) — the model (projected at load through the 020-026
            //      canonical hub, edited in TipTap, re-posted with every server-set fact preserved) is
            //      the authoring source, and the carrier contributes the parts the thin model cannot
            //      carry (styles / numbering / headers / footers / theme / comments part). NO surgical
            //      byte-patch and NO count-gate on this path — the anchor-reconciliation 422 class
            //      (the NDA) is unreachable by construction. Hard-tier constructs accept-flattened at
            //      projection (026) render as degraded prose; the prior version stays retrievable via
            //      SPE version history (FR-07 safety net). Two boundary notes: (1) a born-in-editor doc
            //      must keep OMITTING baselineVersionId on its re-saves (its retained versionId is the
            //      drive-ITEM id, not a real SPE version — echoing it here would 404 the fetch; the
            //      client's bornInEditor branch sends contentModel only); (2) the FR-08 stale-base
            //      re-anchor deliberately does not run on this path — the model is full document state,
            //      so a concurrent out-of-band writer resolves last-writer-wins with version history as
            //      the net (design-accepted; the eTag stamp still updates post-save for the next op-log
            //      save's assert).
            if (!request.Content.IsEmpty)
            {
                var carrierContent = request.Content.ToArray();
                GuardBaselineIsNotPdf(carrierContent);
                var carrierRendered = _documentRenderer.RenderIntoCarrier(
                    carrierContent, request.ContentModel, revisionAuthor, renderDegradations);
                return (carrierRendered, renderDegradations.Count > 0 ? renderDegradations : null);
            }

            if (HasBaselineVersionCoordinates(request))
            {
                var carrierBytes = await FetchBaselineVersionBytesAsync(request, httpContext, cancellationToken)
                    .ConfigureAwait(false);
                GuardBaselineIsNotPdf(carrierBytes);
                var carrierRendered = _documentRenderer.RenderIntoCarrier(
                    carrierBytes, request.ContentModel, revisionAuthor, renderDegradations);
                return (carrierRendered, renderDegradations.Count > 0 ? renderDegradations : null);
            }

            // (a0) BORN-IN-EDITOR (FR-01a, task 026): no retained original at all (AI-drafted / blank) —
            //      the model is the WHOLE document; render the high-fidelity .docx from a blank package
            //      (real styles + style-linked multi-level numbering + native tables + minted
            //      w14:paraId). Deterministic authoring, NOT an AI dispatch (ADR-039 — design §11).
            var synthesized = _documentRenderer.SynthesizeDocument(
                request.ContentModel, revisionAuthor, renderDegradations);
            return (synthesized, renderDegradations.Count > 0 ? renderDegradations : null);
        }

        // (a) Same-session fast-path: the client still holds the retained ORIGINAL bytes.
        if (!request.Content.IsEmpty)
        {
            var retained = request.Content.ToArray();
            GuardBaselineIsNotPdf(retained);
            return (retained, null);
        }

        // (b) FR-06 primary: re-fetch the LOAD-TIME SPE version by versionId (task 002), behind the
        //     SpeFileStore facade (ADR-007 — no Microsoft.Graph type crosses into Services/Compose).
        if (HasBaselineVersionCoordinates(request))
        {
            var baseline = await FetchBaselineVersionBytesAsync(request, httpContext, cancellationToken)
                .ConfigureAwait(false);
            GuardBaselineIsNotPdf(baseline);
            return (baseline, null);
        }

        // No baseline resolvable. A dirty save NEVER falls back to a client reconstruction (FR-01).
        throw new ArgumentException(
            "Compose save: no baseline could be resolved — supply the retained original bytes (Content) for " +
            "a same-session save, or a BaselineVersionId (+ DriveId + DocumentSpeId) to re-fetch the " +
            "load-time version (FR-06). A docx.js reconstruction is not a valid baseline (FR-01).",
            nameof(request));
    }

    /// <summary>
    /// Task 040 Step-9.5 fix (HIGH-2): every resolved SAVE BASELINE must be an OOXML package, never a
    /// PDF. Before 040, a PDF could not reach a save (Load fail-closed on the OOXML projection); now a
    /// PDF load succeeds with SYNTHESIZED docx Content and a rogue/stale caller could hand the engine
    /// %PDF- bytes (a re-fetched PDF-item version, or the raw PDF echoed as "retained bytes") — which
    /// would either throw deep inside the OOXML stack as a generic 500, or worse, write docx bytes
    /// over the .pdf item. Sniff once here (the single choke point every baseline passes through) and
    /// refuse LOUDLY with the honest instruction (the 041 client saves PDFs via create-on-save; the
    /// endpoint maps this to 422).
    /// </summary>
    private static void GuardBaselineIsNotPdf(ReadOnlySpan<byte> baseline)
    {
        if (baseline.Length >= 5
            && baseline[0] == (byte)'%' && baseline[1] == (byte)'P' && baseline[2] == (byte)'D'
            && baseline[3] == (byte)'F' && baseline[4] == (byte)'-')
        {
            throw new ComposePdfIntakeException(
                "Compose save: the save baseline resolved to PDF bytes. A document opened from a PDF " +
                "saves as a NEW Word document (create-on-save) — it cannot replace the PDF in place. " +
                "Re-open the document and save again.",
                unavailable: false);
        }
    }

    /// <summary>Whether the request carries the full coordinate set for an FR-06 load-time-version
    /// re-fetch (versionId + driveId + speId).</summary>
    /// <remarks>
    /// <c>internal</c> rather than private: <c>ComposeService.SaveAsync</c> also reads it, to decide
    /// whether a save can proceed at all before any baseline work starts (see the class remarks).
    /// </remarks>
    internal static bool HasBaselineVersionCoordinates(SaveComposeDocumentRequest request) =>
        !string.IsNullOrWhiteSpace(request.BaselineVersionId)
        && !string.IsNullOrWhiteSpace(request.DriveId)
        && !string.IsNullOrWhiteSpace(request.DocumentSpeId);

    /// <summary>FR-06: downloads the load-time SPE version's exact bytes (the retained baseline / render
    /// carrier) behind the SpeFileStore facade. Throws when the version is gone — a dirty save never
    /// falls back to a reconstruction (FR-01).</summary>
    private async Task<byte[]> FetchBaselineVersionBytesAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var stream = await _spe.DownloadFileVersionAsUserAsync(
                httpContext, request.DriveId!, request.DocumentSpeId!, request.BaselineVersionId!, cancellationToken)
            .ConfigureAwait(false);

        if (stream is not null)
        {
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        throw new InvalidOperationException(
            $"Compose save: the load-time baseline version was not found (drive={request.DriveId} " +
            $"item={request.DocumentSpeId} version={request.BaselineVersionId}). A dirty save must apply " +
            "onto the load-time original — it will not fall back to a reconstruction (FR-01/FR-06).");
    }

    /// <summary>
    /// FR-S02 (r8 task 011): replace the drive-item's content under an `If-Match` precondition, retrying
    /// ONCE against the freshly-read version if a writer landed inside the check-then-act window.
    /// </summary>
    /// <remarks>
    /// The retry is the deliberate resolution of the POML's step-5 question ("retry once, or report
    /// storage-failed?"). Retrying is correct here because the precondition failure carries no information
    /// the user could act on — it means only that our read was microseconds stale, and the save's own
    /// semantics are already last-writer-wins, so re-issuing against the fresh version produces exactly the
    /// outcome the user asked for. Retrying UNBOUNDED would be wrong (a hot document could spin), and
    /// failing immediately would resurrect the dead-end this task exists to remove — so: exactly one retry,
    /// then an honest typed failure the endpoint maps to a defined outcome.
    ///
    /// The second attempt re-reads metadata rather than reusing the failed ETag: reusing it would fail
    /// identically, and the point of the retry is to rebase onto whatever landed.
    /// </remarks>
    /// <param name="rebaseOnConflict">
    /// <c>true</c> (default, the SAVE path): on a failed precondition, retry ONCE against the fresh
    /// version — last-writer-wins. This is only sound because the save path has already rebased the
    /// caller's edits onto those very bytes (<c>ReanchorStaleSaveAsync</c> re-downloaded them), so the
    /// retried write CONTAINS the concurrent writer's change.
    /// <para><c>false</c> (#776, the APPLY-TEMPLATE path): a failed precondition is terminal. That path
    /// merges bytes it downloaded at T1 and never rebases them, so a retry against the fresh version
    /// would write a payload that never contained the other writer's change — silently erasing them at
    /// the head version, which is the exact defect the precondition was added to stop. Retrying there
    /// would make the If-Match decorative.</para>
    /// </param>
    internal async Task<FileHandleDto?> ReplaceWithPreconditionAsync(
        HttpContext httpContext,
        string driveId,
        string itemId,
        byte[] content,
        string? ifMatch,
        CancellationToken cancellationToken,
        bool rebaseOnConflict = true)
    {
        if (string.IsNullOrEmpty(ifMatch))
        {
            // No resolved version to assert against (no metadata read happened — a drive-less or
            // transient path). Nothing to precondition on, so this stays the unchanged R1 blind PUT via
            // the etag-less overload rather than passing an explicit null through the If-Match one.
            using var blindStream = new MemoryStream(content, writable: false);
            return await _spe.ReplaceFileContentAsUserAsync(httpContext, driveId, itemId, blindStream, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            return await _spe.ReplaceFileContentAsUserAsync(httpContext, driveId, itemId, stream, ifMatch, cancellationToken)
                .ConfigureAwait(false);
        }
        // Only reachable with a non-empty `ifMatch` — the guard above returns for the blind-PUT case, so
        // this catch cannot fire on a request that never carried a precondition.
        catch (EtagPreconditionFailedException) when (rebaseOnConflict)
        {
            var fresh = await _spe.GetFileMetadataAsUserAsync(httpContext, driveId, itemId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "Compose save: If-Match precondition failed for driveItem={DocumentSpeId} (sent eTag={SentETag}, " +
                "live eTag={FreshETag}) — a writer landed inside the read-to-write window. Retrying ONCE against " +
                "the fresh version (last-writer-wins).",
                itemId, ifMatch, fresh?.ETag);

            using var retryStream = new MemoryStream(content, writable: false);
            return await _spe.ReplaceFileContentAsUserAsync(
                    httpContext, driveId, itemId, retryStream, fresh?.ETag, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    // =========================================================================
    // FR-08 (task 050) — save-path version stamp + stale-base re-anchor (design §5 "Save + concurrency",
    // NFR-08). The stamp (SPE eTag + operation-schema version) is persisted via IDistributedCache (ADR-009)
    // after every save of an existing item and asserted against the LIVE eTag at the top of the NEXT save.
    // A mismatch re-anchors the operation log via AnnotationReanchorService — REUSED verbatim, never
    // reimplemented (CLAUDE.md §11 / task constraint). The re-anchor itself is cluster 1
    // (ComposeReanchorCoordinator); what lives here is the stamp that makes the mismatch detectable.
    // =========================================================================

    /// <summary>The save-path version stamp persisted per <c>documentSpeId</c> (ADR-009 Redis) — the SPE
    /// eTag + operation-schema version this service last wrote, asserted against the live eTag at the top
    /// of the next save of the same item.</summary>
    internal sealed record ComposeSaveVersionStamp(
        [property: JsonPropertyName("eTag")] string ETag,
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("savedAtUtc")] DateTimeOffset SavedAtUtc);

    /// <summary>Reads the persisted version stamp for <paramref name="documentSpeId"/> (null when absent, no
    /// cache configured, or a Redis read fails — all three degrade to "not stale", never a false-positive
    /// re-anchor and never a blocked save).</summary>
    internal async Task<ComposeSaveVersionStamp?> GetSaveVersionStampAsync(string documentSpeId, CancellationToken ct)
    {
        if (_cache is null)
        {
            return null;
        }

        try
        {
            var json = await _cache.GetStringAsync(SaveVersionStampKeyPrefix + documentSpeId, ct).ConfigureAwait(false);
            return json is null ? null : JsonSerializer.Deserialize<ComposeSaveVersionStamp>(json, ComposeCacheJson.Options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: failed to read the version stamp for driveItem={DocumentSpeId} — treating as no prior stamp (not stale).",
                documentSpeId);
            return null;
        }
    }

    /// <summary>Persists the version stamp for <paramref name="documentSpeId"/> after a successful write
    /// (create or replace). Best-effort: a Redis write failure here never fails the already-successful save
    /// — it only means the NEXT save's staleness assert degrades to "no stamp" (not stale), same as a
    /// freshly-onboarded item that has never been stamped.</summary>
    internal async Task SetSaveVersionStampAsync(string documentSpeId, string? eTag, DateTimeOffset savedAtUtc, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrEmpty(eTag))
        {
            return;
        }

        try
        {
            var stamp = new ComposeSaveVersionStamp(eTag, ComposeOperationSchema.Version, savedAtUtc);
            await _cache.SetStringAsync(SaveVersionStampKeyPrefix + documentSpeId, JsonSerializer.Serialize(stamp, ComposeCacheJson.Options), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: failed to persist the version stamp for driveItem={DocumentSpeId} — the save itself succeeded; a future assert may miss this save.",
                documentSpeId);
        }
    }
}
