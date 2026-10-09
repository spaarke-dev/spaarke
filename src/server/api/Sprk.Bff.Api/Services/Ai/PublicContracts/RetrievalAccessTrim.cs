using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default <see cref="IRetrievalAccessTrim"/>: one caller-scoped (OBO) read per page of at most
/// <see cref="MaxIdsPerRead"/> distinct keys per record kind. See the interface for the contract (task 176, #1511).
/// </summary>
/// <remarks>
/// Scoped (it uses the request's <see cref="IDataverseUserClient"/>). ADR-015: logs counts, outcome and status codes only,
/// never ids, names or content. ADR-044: GUID keys are parsed and written to the filter in bare lowercase form, so the
/// comparison never depends on the index's or the response's casing or braces.
/// </remarks>
public sealed class RetrievalAccessTrim : IRetrievalAccessTrim
{
    /// <summary>Most keys sent in one Dataverse read (owner decision 2026-10-09). Larger pages are chunked.</summary>
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
    public Task<RetrievalTrimResult<T>> TrimAsync<T>(
        IReadOnlyList<T> rows,
        Func<T, string?> documentIdOf,
        string? callerObjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIdOf);
        return TrimByRecordAsync(rows, r => RetrievalRecordKey.Document(documentIdOf(r)), callerObjectId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RetrievalTrimResult<T>> TrimByRecordAsync<T>(
        IReadOnlyList<T> rows,
        Func<T, RetrievalRecordKey?> recordOf,
        string? callerObjectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(recordOf);

        // The caller is verified first, even for an empty page, so a path with no signed-in user always reports
        // NoVerifiedCaller whatever the index returned.
        if (VerifiedCaller(callerObjectId) is null)
        {
            if (string.IsNullOrWhiteSpace(callerObjectId))
            {
                // No caller was DECLARED: an unattended path (scheduled run, background triage, app-only profiling).
                // Expected, so Information; the paths that a user could notice log their own warning.
                _logger.LogInformation(
                    "[RETRIEVAL-TRIM] document retrieval withheld ({Count} row(s)): no caller declared (unattended run). "
                    + "Unattended runs get no document results by design (task 176, #1511).",
                    rows.Count);
            }
            else
            {
                // A caller WAS declared but the request has no principal, or a different one: an anomaly.
                _logger.LogWarning(
                    "[RETRIEVAL-TRIM] document retrieval withheld ({Count} row(s)): the declared caller is not the "
                    + "principal of the current request (or there is none). Fail closed (task 176, #1511).",
                    rows.Count);
            }

            return new RetrievalTrimResult<T>(Array.Empty<T>(), RetrievalTrimOutcome.NoVerifiedCaller, rows.Count);
        }

        if (rows.Count == 0)
        {
            return new RetrievalTrimResult<T>(Array.Empty<T>(), RetrievalTrimOutcome.Evaluated, 0);
        }

        var keys = rows.Select(recordOf).ToList();

        // Distinct keys per record kind (entity set + key column), in first-seen order.
        var byKind = new Dictionary<(string Set, string Field), List<string>>();
        var seen = new HashSet<RetrievalRecordKey>();
        foreach (var key in keys)
        {
            if (key is null || !seen.Add(key))
            {
                continue;
            }

            if (!byKind.TryGetValue((key.EntitySetName, key.KeyField), out var list))
            {
                byKind[(key.EntitySetName, key.KeyField)] = list = new List<string>();
            }

            list.Add(key.Value);
        }

        var readable = new HashSet<RetrievalRecordKey>();
        foreach (var ((set, field), values) in byKind)
        {
            for (var offset = 0; offset < values.Count; offset += MaxIdsPerRead)
            {
                var batch = values.Skip(offset).Take(MaxIdsPerRead).ToList();
                var answered = await ReadReadableKeysAsync(set, field, batch, cancellationToken).ConfigureAwait(false);
                if (answered is null)
                {
                    return new RetrievalTrimResult<T>(Array.Empty<T>(), RetrievalTrimOutcome.CheckFailed, rows.Count);
                }

                // Only keys that were ASKED count: a body naming a key outside the batch cannot widen the result.
                foreach (var value in answered)
                {
                    if (batch.Contains(value, StringComparer.Ordinal))
                    {
                        readable.Add(new RetrievalRecordKey(set, field, value));
                    }
                }
            }
        }

        var kept = new List<T>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            if (keys[i] is { } key && readable.Contains(key))
            {
                kept.Add(rows[i]);
            }
        }

        if (kept.Count != rows.Count)
        {
            _logger.LogInformation(
                "[RETRIEVAL-TRIM] kept {Kept} of {Total} retrieved row(s) ({Distinct} distinct record(s) checked as the caller)",
                kept.Count, rows.Count, seen.Count);
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

    /// <summary>
    /// One caller-scoped read of <paramref name="entitySet"/> filtered to <paramref name="keys"/>. Returns the keys
    /// Dataverse returned (the ones the caller can Read), in the same canonical form; null when the read did not produce
    /// a trustworthy answer. Only the (set, column) pairs <see cref="RetrievalRecordKey"/> can build reach here.
    /// </summary>
    private async Task<IReadOnlyList<string>?> ReadReadableKeysAsync(
        string entitySet, string keyField, IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        var isGuid = RetrievalRecordKey.IsGuidKey(entitySet, keyField);
        var filter = new StringBuilder();
        for (var i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                filter.Append(" or ");
            }

            filter.Append(keyField).Append(" eq ");
            if (isGuid)
            {
                filter.Append(keys[i]);
            }
            else
            {
                filter.Append('\'').Append(Uri.EscapeDataString(keys[i].Replace("'", "''"))).Append('\'');
            }
        }

        DataverseUserResponse response;
        try
        {
            response = await _userClient
                .GetAsync($"{entitySet}?$select={keyField}&$filter={filter}", cancellationToken)
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

        var result = new List<string>();
        foreach (var row in value.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object
                && row.TryGetProperty(keyField, out var keyProp)
                && keyProp.ValueKind == JsonValueKind.String)
            {
                var canonical = RetrievalRecordKey.Canonical(entitySet, keyField, keyProp.GetString());
                if (canonical is not null)
                {
                    result.Add(canonical);
                }
            }
        }

        return result;
    }
}
