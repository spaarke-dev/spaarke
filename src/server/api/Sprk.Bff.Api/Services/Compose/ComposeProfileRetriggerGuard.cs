// Task 070 (Track D) — cluster 5a, extracted from `ComposeService`.
//
// WHY THIS IS ITS OWN COMPONENT. One question: WHEN should a document be (re)profiled? It owns the
// G10 storm guard — the profiled-eTag stamp and the compare against the live eTag that decides
// whether a reopen re-dispatches the Document Profile — plus the manual on-demand leg. Its reason to
// change is the re-profiling POLICY, which is independent of how a profile is dispatched
// (`ComposeProfileDispatcher`, cluster 5b) and of anything on the save path.
//
// WHY IT WENT LAST, on purpose. The coverage measurement in the seam map put this cluster at 64.3%
// branch — the WEAKEST code in the file — and said to extract it last or give it tests first. That
// judgement was correct: a mutation seeded BEFORE this move (invert the eTag comparison, so an
// unchanged reopen re-profiles and a genuinely changed document never does) survived the entire
// 1,814-test Compose suite. The storm guard, which exists to stop a profiling storm on every reopen,
// had no test at all in either direction. `ComposeProfileRetriggerGuardSeamTests` closes that, and
// the same mutation is re-run against it to prove it now fails.
//
// ADR-010 — NO NEW DI REGISTRATION. `internal sealed class` constructed in the `ComposeService`
// constructor from fields it already holds. Verified by an EMPTY `git diff` over `Program.cs` +
// `Infrastructure/DI/`.
//
// Bodies moved VERBATIM; the only edits are accessibility on the four declarations.
// `RefreshProfileAsync` stays an `IComposeService` member — `ComposeService` keeps a thin delegating
// override, the split clusters 6 and 2a established.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Models;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 5a — the G10 re-profiling policy: the profiled-eTag stamp, the reopen storm guard, and the
/// manual refresh leg.
/// </summary>
/// <remarks>
/// Every method here is BEST-EFFORT by contract: a null cache, a cache fault, or any exception must
/// leave Load unaffected. That is why the bodies swallow and log rather than throw — the profile is an
/// enrichment, never a precondition for opening a document.
/// </remarks>
internal sealed class ComposeProfileRetriggerGuard
{
    private readonly IDistributedCache? _cache;
    private readonly ComposeProfileDispatcher _profileDispatcher;
    private readonly ILogger _logger;

    internal ComposeProfileRetriggerGuard(
        IDistributedCache? cache,
        ComposeProfileDispatcher profileDispatcher,
        ILogger logger)
    {
        _cache = cache;
        _profileDispatcher = profileDispatcher;
        _logger = logger;
    }

    internal const string ProfiledETagKeyPrefix = "sdap:compose:profiled-etag:";

    internal async Task<string?> GetProfiledETagAsync(string documentSpeId, CancellationToken ct)
    {
        if (_cache is null)
        {
            return null;
        }
        try
        {
            return await _cache.GetStringAsync(ProfiledETagKeyPrefix + documentSpeId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose profile (G10): failed to read the profiled-eTag stamp for driveItem={DocumentSpeId} — treating as never-profiled (may re-trigger once).",
                documentSpeId);
            return null;
        }
    }

    internal async Task SetProfiledETagAsync(string documentSpeId, string eTag, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrEmpty(eTag))
        {
            return;
        }
        try
        {
            await _cache.SetStringAsync(ProfiledETagKeyPrefix + documentSpeId, eTag, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose profile (G10): failed to persist the profiled-eTag stamp for driveItem={DocumentSpeId} — a future reopen may re-trigger once more (best-effort, never a storm).",
                documentSpeId);
        }
    }

    /// <summary>
    /// G10 (FR-09, task 040): the reload/onload re-trigger. On a Path A reopen (an existing
    /// <c>sprk_document</c>), re-dispatch the fire-and-forget Document Profile ONLY when the doc CHANGED
    /// since Compose last profiled it (live eTag ≠ the profiled-eTag stamp) — then stamp the current eTag so
    /// a subsequent unchanged reopen skips (the storm guard closes the loop). Best-effort: never blocks or
    /// fails Load; a null <c>_documentProfileAi</c>/cache simply no-ops.
    /// </summary>
    internal async Task MaybeRetriggerProfileOnLoadAsync(
        Guid documentRecordId, string documentSpeId, string liveETag, HttpContext httpContext, CancellationToken ct)
    {
        try
        {
            var profiledETag = await GetProfiledETagAsync(documentSpeId, ct).ConfigureAwait(false);
            if (string.Equals(profiledETag, liveETag, StringComparison.Ordinal))
            {
                return; // unchanged since the last profile — skip (no storm)
            }

            _profileDispatcher.Dispatch(documentRecordId, httpContext);
            await SetProfiledETagAsync(documentSpeId, liveETag, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Compose reload profile re-trigger (G10): document {DocumentRecordId} (driveItem={DocumentSpeId}) changed since last profile (profiledETag={ProfiledETag}, liveETag={LiveETag}) — profile re-dispatched fire-and-forget.",
                documentRecordId, documentSpeId, profiledETag, liveETag);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose reload profile re-trigger (G10): failed for document {DocumentRecordId} — best-effort, Load unaffected.",
                documentRecordId);
        }
    }

    /// <inheritdoc />
    internal async Task<bool> RefreshProfileAsync(
        RefreshComposeProfileRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentRecordId == Guid.Empty)
        {
            throw new ArgumentException("A DocumentRecordId is required to refresh a Compose document's profile.", nameof(request));
        }

        // G10 manual leg: a user-initiated on-demand re-run. UNCONDITIONAL (unlike the reload guard) — the
        // user explicitly asked to refresh — but still fire-and-forget + best-effort. Stamp the current eTag
        // (when known) so an immediately-following reopen does not redundantly re-trigger.
        _profileDispatcher.Dispatch(request.DocumentRecordId, httpContext);
        if (!string.IsNullOrWhiteSpace(request.DocumentSpeId) && !string.IsNullOrWhiteSpace(request.ETag))
        {
            await SetProfiledETagAsync(request.DocumentSpeId!, request.ETag!, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Compose manual profile refresh (G10): document {DocumentRecordId} — profile re-dispatched fire-and-forget on user request.",
            request.DocumentRecordId);
        return true;
    }
}
