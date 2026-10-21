using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Handlers.Dataverse;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default <see cref="IRetrievalAccessTrim"/>: one caller-scoped (OBO) read per page of at most
/// <see cref="MaxIdsPerRead"/> distinct keys per record kind. See the interface for the contract (task 176, #1511).
/// </summary>
/// <remarks>
/// <para>Scoped (it uses the request's <see cref="IDataverseUserClient"/>). ADR-015: logs counts, outcome, status codes and
/// reason codes only, never ids, names or content. ADR-044: GUID keys are parsed and written to the filter in bare lowercase
/// form, so the comparison never depends on the index's or the response's casing or braces.</para>
/// <para><b>External callers</b> (unified-access-control-r2 task 188 round 3, D7 T7, F1-A). A systemuser flagged
/// <c>sprk_isexternal</c> with a share on matter M could read M's documents, so Dataverse Read alone handed the model the
/// chunks of an internal-only email's .eml filed to M. After the Read check, a caller who is not an internal systemuser
/// (<see cref="ExternalCallerCommunicationGuard.IsExternalCallerAsync"/>; a lookup that FAILS counts as external) keeps a
/// document key only when THE external document narrowing shows the document. A drive-item key passes only when at least
/// one document row on that item that the caller's own read returned is also shown. Parent-record keys are not document
/// content and are not narrowed. A narrowing that fails withholds every document key (parent-record keys stay).</para>
/// <para><b>The "User-OBO ONLY" rule is AMENDED for ONE dependency</b> (the round-2 amendment of the <c>dataverse.*</c>
/// tools, CLAUDE.md §6.5 path B; approved, owner round 111): the narrowing is reached only through the injected
/// <see cref="AppOnlyDocumentNarrowing"/>, which re-reads the documents BY ID under the BFF's app-only identity and returns
/// only a hide decision. It can only remove a key the caller's own read returned; no app-only data reaches the result.</para>
/// <para><b>Component Justification (CLAUDE.md §11), the three new constructor dependencies.</b> (1) Existing — the
/// externality rule (<see cref="ExternalCallerCommunicationGuard.IsExternalCallerAsync"/> over
/// <see cref="ICallerSystemUserResolver"/> and <see cref="ISystemUserIdentityResolver"/>) and the narrowing
/// (<see cref="AppOnlyDocumentNarrowing"/>) are reused as they are; nothing is copied. (2) Extension — this class is the ONE
/// trim every AI retrieval path applies, so narrowing it here covers every consumer at once. (3) Cost of doing nothing — an
/// external systemuser's chat, Insights or playbook retrieval returns the text of an internal-only email or a Restricted
/// document (F1-A).</para>
/// </remarks>
public sealed class RetrievalAccessTrim : IRetrievalAccessTrim
{
    /// <summary>Most keys sent in one Dataverse read (owner decision 2026-10-09). Larger pages are chunked.</summary>
    public const int MaxIdsPerRead = 20;

    /// <summary>Over-fetch factor for callers that trim a ranked page (owner decision: "up to 2×").</summary>
    public const int CandidatePoolFactor = 2;

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDataverseUserClient _userClient;
    private readonly ICallerSystemUserResolver _callerResolver;
    private readonly ISystemUserIdentityResolver _identityResolver;
    private readonly AppOnlyDocumentNarrowing _narrowDocuments;
    private readonly ILogger<RetrievalAccessTrim> _logger;

    public RetrievalAccessTrim(
        IHttpContextAccessor httpContextAccessor,
        IDataverseUserClient userClient,
        ICallerSystemUserResolver callerResolver,
        ISystemUserIdentityResolver identityResolver,
        AppOnlyDocumentNarrowing narrowDocuments,
        ILogger<RetrievalAccessTrim> logger)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _userClient = userClient ?? throw new ArgumentNullException(nameof(userClient));
        _callerResolver = callerResolver ?? throw new ArgumentNullException(nameof(callerResolver));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _narrowDocuments = narrowDocuments ?? throw new ArgumentNullException(nameof(narrowDocuments));
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
        // Task 188 round 3: the document rows the CALLER's read returned on each readable drive-item key.
        var itemDocuments = new Dictionary<RetrievalRecordKey, HashSet<Guid>>();
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
                foreach (var (value, documentId) in answered)
                {
                    if (batch.Contains(value, StringComparer.Ordinal))
                    {
                        var key = new RetrievalRecordKey(set, field, value);
                        readable.Add(key);
                        if (documentId != Guid.Empty && RetrievalRecordKey.IsDriveItemKey(set, field))
                        {
                            if (!itemDocuments.TryGetValue(key, out var documents))
                            {
                                itemDocuments[key] = documents = new HashSet<Guid>();
                            }

                            documents.Add(documentId);
                        }
                    }
                }
            }
        }

        await NarrowDocumentKeysForExternalCallerAsync(readable, itemDocuments, cancellationToken).ConfigureAwait(false);

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
    /// Task 188 round 3 (F1-A): removes from <paramref name="readable"/> every DOCUMENT key THE external document narrowing
    /// hides, when the caller is not an internal systemuser. A document-id key passes when its document is shown; a drive-item
    /// key passes when at least one of the document rows the caller's own read returned on that item
    /// (<paramref name="itemDocuments"/>) is shown. Parent-record keys are left alone. Nothing is looked up or read when no
    /// document key is readable, or for an internal caller.
    /// </summary>
    /// <remarks>
    /// Fail closed with minimal collateral (main-session decision): a failed externality lookup applies the narrowing, so an
    /// internal user loses only hidden documents during a transient fault; a failed narrowing withholds every document key
    /// (parent-record keys stay). Logs counts and reason codes only (ADR-015).
    /// </remarks>
    private async Task NarrowDocumentKeysForExternalCallerAsync(
        HashSet<RetrievalRecordKey> readable,
        IReadOnlyDictionary<RetrievalRecordKey, HashSet<Guid>> itemDocuments,
        CancellationToken cancellationToken)
    {
        var documentKeys = readable
            .Where(k => RetrievalRecordKey.IsDocumentIdKey(k.EntitySetName, k.KeyField)
                        || RetrievalRecordKey.IsDriveItemKey(k.EntitySetName, k.KeyField))
            .ToList();
        if (documentKeys.Count == 0 || !await IsExternalOrUnknownCallerAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var documentIds = new HashSet<Guid>();
        foreach (var key in documentKeys)
        {
            if (RetrievalRecordKey.IsDocumentIdKey(key.EntitySetName, key.KeyField))
            {
                documentIds.Add(Guid.Parse(key.Value));
            }
            else if (itemDocuments.TryGetValue(key, out var onItem))
            {
                documentIds.UnionWith(onItem);
            }
        }

        DocumentNarrowingResult? narrowing = null;
        if (documentIds.Count > 0)
        {
            try
            {
                narrowing = await _narrowDocuments(documentIds, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[RETRIEVAL-TRIM] external document narrowing failed for {Count} document(s); withholding all "
                    + "{Keys} document record(s) (fail closed; parent records unaffected)",
                    documentIds.Count, documentKeys.Count);
            }
        }

        var hidden = 0;
        foreach (var key in documentKeys)
        {
            var shown = narrowing is not null
                && (RetrievalRecordKey.IsDocumentIdKey(key.EntitySetName, key.KeyField)
                    ? narrowing.IsVisible(Guid.Parse(key.Value))
                    : itemDocuments.TryGetValue(key, out var onItem) && onItem.Any(narrowing.IsVisible));
            if (!shown)
            {
                readable.Remove(key);
                hidden++;
            }
        }

        if (hidden > 0)
        {
            _logger.LogInformation(
                "[RETRIEVAL-TRIM] external caller: {Hidden} of {Keys} readable document record(s) withheld by the external "
                + "document narrowing (faulted={Faulted}; hidden documents by reason: {Reasons})",
                hidden, documentKeys.Count, documentIds.Count > 0 && narrowing is null,
                narrowing?.HiddenSummary(documentIds) ?? "none-decided");
        }
    }

    /// <summary>
    /// True unless the request's caller is a resolved INTERNAL systemuser
    /// (<see cref="ExternalCallerCommunicationGuard.IsExternalCallerAsync"/>: flagged external, unresolved, or no principal is
    /// external). A lookup that FAILS (the guard throws) counts as external, so the narrowing applies: a transient fault
    /// costs an internal user only the documents it hides, never every result.
    /// </summary>
    private async Task<bool> IsExternalOrUnknownCallerAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ExternalCallerCommunicationGuard.IsExternalCallerAsync(
                _httpContextAccessor, _callerResolver, _identityResolver, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[RETRIEVAL-TRIM] the caller's external flag could not be read; applying the external document narrowing "
                + "(fail closed: only documents it hides are withheld)");
            return true;
        }
    }

    /// <summary>
    /// One caller-scoped read of <paramref name="entitySet"/> filtered to <paramref name="keys"/>. Returns the keys
    /// Dataverse returned (the ones the caller can Read), in the same canonical form; null when the read did not produce
    /// a trustworthy answer. Only the (set, column) pairs <see cref="RetrievalRecordKey"/> can build reach here. A drive-item
    /// read also selects <c>sprk_documentid</c> (task 188 round 3) and returns one entry per document row on the item, with
    /// its id (<see cref="Guid.Empty"/> for every other kind, or when the row carries no usable id).
    /// </summary>
    private async Task<IReadOnlyList<(string Key, Guid DocumentId)>?> ReadReadableKeysAsync(
        string entitySet, string keyField, IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        var isGuid = RetrievalRecordKey.IsGuidKey(entitySet, keyField);
        var isDriveItem = RetrievalRecordKey.IsDriveItemKey(entitySet, keyField);
        var select = isDriveItem ? $"{keyField},{RetrievalRecordKey.DocumentIdField}" : keyField;
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
                .GetAsync($"{entitySet}?$select={select}&$filter={filter}", cancellationToken)
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

        var result = new List<(string, Guid)>();
        foreach (var row in value.EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Object
                && row.TryGetProperty(keyField, out var keyProp)
                && keyProp.ValueKind == JsonValueKind.String)
            {
                var canonical = RetrievalRecordKey.Canonical(entitySet, keyField, keyProp.GetString());
                if (canonical is not null)
                {
                    var documentId = isDriveItem
                        && row.TryGetProperty(RetrievalRecordKey.DocumentIdField, out var idProp)
                        && idProp.ValueKind == JsonValueKind.String
                        && Guid.TryParse(idProp.GetString(), out var id)
                            ? id
                            : Guid.Empty;
                    result.Add((canonical, documentId));
                }
            }
        }

        return result;
    }
}
