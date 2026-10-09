using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default <see cref="IRetrievalAccessTrim"/>: one caller-scoped (OBO) read of <c>sprk_documents</c> per page of at most
/// <see cref="MaxIdsPerRead"/> distinct ids. See the interface for the contract (task 176, #1511).
/// </summary>
/// <remarks>
/// Scoped (it uses the request's <see cref="IDataverseUserClient"/>). ADR-015: logs counts, outcome and status codes only,
/// never ids, names or content. ADR-044: ids are parsed as GUIDs and written to the filter in bare lowercase form, so the
/// comparison never depends on the index's or the response's casing or braces.
/// </remarks>
public sealed class RetrievalAccessTrim : IRetrievalAccessTrim
{
    /// <summary>Most document ids sent in one Dataverse read (owner decision 2026-10-09). Larger pages are chunked.</summary>
    public const int MaxIdsPerRead = 20;

    /// <summary>Over-fetch factor for callers that trim a ranked page (owner decision: "up to 2×").</summary>
    public const int CandidatePoolFactor = 2;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDataverseUserClient _userClient;
    private readonly ILogger<RetrievalAccessTrim> _logger;

    public RetrievalAccessTrim(
        IHttpContextAccessor httpContextAccessor,
        IDataverseUserClient userClient,
        ILogger<RetrievalAccessTrim> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _userClient = userClient ?? throw new ArgumentNullException(nameof(userClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// How many rows a caller asking for <paramref name="topK"/> should draw from the index before trimming: 2×, so a
    /// caller entitled to some of the top documents still gets a full page more often. Cost: the extra ids ride the same
    /// read (chunked at <see cref="MaxIdsPerRead"/>), so the over-fetch costs at most one extra round trip.
    /// </summary>
    public static int CandidatePoolSize(int topK) => Math.Max(topK, 1) * CandidatePoolFactor;

    /// <inheritdoc />
    public bool CanEvaluate(string? callerObjectId) => VerifiedCaller(callerObjectId) is not null;

    /// <inheritdoc />
    public async Task<RetrievalTrimResult<T>> TrimAsync<T>(
        IReadOnlyList<T> rows,
        Func<T, string?> documentIdOf,
        string? callerObjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(documentIdOf);

        // The caller is verified first, even for an empty page, so a path with no signed-in user always reports
        // NoVerifiedCaller (and logs it) whatever the index returned.
        if (VerifiedCaller(callerObjectId) is null)
        {
            _logger.LogWarning(
                "[RETRIEVAL-TRIM] document retrieval withheld ({Count} row(s)): no verified caller for this retrieval "
                + "(no declared caller, no request principal, or the request principal is not the declared caller). "
                + "Unattended runs get no document results by design (task 176, #1511).",
                rows.Count);
            return new RetrievalTrimResult<T>(Array.Empty<T>(), RetrievalTrimOutcome.NoVerifiedCaller, rows.Count);
        }

        if (rows.Count == 0)
        {
            return new RetrievalTrimResult<T>(Array.Empty<T>(), RetrievalTrimOutcome.Evaluated, 0);
        }

        var distinct = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var row in rows)
        {
            if (TryParseDocumentId(documentIdOf(row), out var id) && seen.Add(id))
            {
                distinct.Add(id);
            }
        }

        var readable = new HashSet<Guid>();
        for (var offset = 0; offset < distinct.Count; offset += MaxIdsPerRead)
        {
            var batch = distinct.Skip(offset).Take(MaxIdsPerRead).ToList();
            var answered = await ReadReadableIdsAsync(batch, cancellationToken).ConfigureAwait(false);
            if (answered is null)
            {
                return new RetrievalTrimResult<T>(Array.Empty<T>(), RetrievalTrimOutcome.CheckFailed, rows.Count);
            }

            // Only ids that were ASKED count: a body naming an id outside the batch cannot widen the result.
            foreach (var id in answered)
            {
                if (batch.Contains(id))
                {
                    readable.Add(id);
                }
            }
        }

        var kept = rows
            .Where(r => TryParseDocumentId(documentIdOf(r), out var id) && readable.Contains(id))
            .ToList();

        if (kept.Count != rows.Count)
        {
            _logger.LogInformation(
                "[RETRIEVAL-TRIM] kept {Kept} of {Total} retrieved row(s) ({Distinct} distinct document(s) checked as the caller)",
                kept.Count, rows.Count, distinct.Count);
        }

        return new RetrievalTrimResult<T>(kept, RetrievalTrimOutcome.Evaluated, rows.Count - kept.Count);
    }

    /// <summary>
    /// The declared caller when it is the principal of the current request; otherwise null. Both must be present: the
    /// OBO read runs on the request's bearer token, so a declared caller that differs from it would be checked as the
    /// wrong user.
    /// </summary>
    private string? VerifiedCaller(string? callerObjectId)
    {
        if (string.IsNullOrWhiteSpace(callerObjectId))
        {
            return null;
        }

        var requestOid = CallerResolution.ResolveObjectId(_httpContextAccessor.HttpContext?.User);
        if (string.IsNullOrWhiteSpace(requestOid))
        {
            return null;
        }

        var same = Guid.TryParse(callerObjectId, out var declared) && Guid.TryParse(requestOid, out var actual)
            ? declared == actual
            : string.Equals(callerObjectId.Trim(), requestOid.Trim(), StringComparison.OrdinalIgnoreCase);

        return same ? requestOid : null;
    }

    /// <summary>One caller-scoped read. Null when the read did not produce a trustworthy answer.</summary>
    private async Task<IReadOnlyList<Guid>?> ReadReadableIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
    {
        var filter = new StringBuilder();
        for (var i = 0; i < ids.Count; i++)
        {
            if (i > 0)
            {
                filter.Append(" or ");
            }

            filter.Append("sprk_documentid eq ").Append(ids[i].ToString("D"));
        }

        DataverseUserResponse response;
        try
        {
            response = await _userClient
                .GetAsync($"sprk_documents?$select=sprk_documentid&$filter={filter}", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[RETRIEVAL-TRIM] access read threw; withholding all rows (fail closed)");
            return null;
        }

        if (!response.IsSuccess)
        {
            _logger.LogWarning(
                "[RETRIEVAL-TRIM] access read failed ({StatusCode} {ErrorCode}); withholding all rows (fail closed)",
                response.StatusCode, response.ErrorCode);
            return null;
        }

        if (response.Body is not { ValueKind: JsonValueKind.Object } body
            || !body.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("[RETRIEVAL-TRIM] access read returned no row array; withholding all rows (fail closed)");
            return null;
        }

        var result = new List<Guid>();
        foreach (var row in value.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object
                && row.TryGetProperty("sprk_documentid", out var idProp)
                && idProp.ValueKind == JsonValueKind.String
                && TryParseDocumentId(idProp.GetString(), out var id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static bool TryParseDocumentId(string? value, out Guid id)
    {
        if (!string.IsNullOrWhiteSpace(value) && Guid.TryParse(value.Trim(), out id) && id != Guid.Empty)
        {
            return true;
        }

        id = Guid.Empty;
        return false;
    }
}
