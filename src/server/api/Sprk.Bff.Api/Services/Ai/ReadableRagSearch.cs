using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// The search-then-trim step every AI-internal <see cref="IRagService"/> caller uses (task 176, #1511): over-fetch,
/// search, trim to the caller's readable documents through <see cref="IRetrievalAccessTrim"/>, then cut to the page the
/// caller asked for. Kept out of the facade so CRUD code never sees <see cref="IRagService"/> (ADR-013).
/// </summary>
internal static class ReadableRagSearch
{
    /// <summary>
    /// Runs <paramref name="options"/> with a 2× candidate pool and returns at most <c>options.TopK</c> rows the caller can
    /// read. <see cref="RagSearchResponse.TotalCount"/> is the number of rows returned, never the index count. When no
    /// caller can be verified, the search is not run at all and the result says so.
    /// </summary>
    public static async Task<(RagSearchResponse Response, RetrievalTrimResult<RagSearchResult> Trim)> SearchReadableAsync(
        this IRetrievalAccessTrim accessTrim,
        IRagService ragService,
        string query,
        RagSearchOptions options,
        string? callerObjectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accessTrim);
        ArgumentNullException.ThrowIfNull(ragService);
        ArgumentNullException.ThrowIfNull(options);

        if (!accessTrim.CanEvaluate(callerObjectId))
        {
            // No embedding + search whose rows would all be withheld. TrimAsync of an empty page still reports
            // NoVerifiedCaller and logs the warning, so the outcome and the log are the seam's own.
            var withheld = await accessTrim
                .TrimAsync(Array.Empty<RagSearchResult>(), r => r.DocumentId, callerObjectId, cancellationToken)
                .ConfigureAwait(false);
            return (Empty(query), withheld);
        }

        var pageSize = Math.Max(options.TopK, 1);
        var response = await ragService
            .SearchAsync(query, options with { TopK = RetrievalAccessTrim.CandidatePoolSize(pageSize) }, cancellationToken)
            .ConfigureAwait(false);

        var trim = await accessTrim
            .TrimAsync(response.Results, r => r.DocumentId, callerObjectId, cancellationToken)
            .ConfigureAwait(false);

        var page = trim.Rows.Take(pageSize).ToList();
        return (response with { Results = page, TotalCount = page.Count }, trim);
    }

    private static RagSearchResponse Empty(string query) => new()
    {
        Query = query,
        Results = Array.Empty<RagSearchResult>(),
        TotalCount = 0,
    };
}
