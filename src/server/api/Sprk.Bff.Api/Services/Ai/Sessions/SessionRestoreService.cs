using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Restores a persisted AI chat session: loads from Cosmos DB, checks Dataverse entity
/// staleness via parallel ETag comparisons, reconstructs the LLM context window, and
/// returns a <see cref="RestoredSession"/> ready for injection into the next streaming request.
///
/// Performance target: &lt;500ms p95 total restore time (ADR-015, AIPU2-031 NFR).
/// Entity staleness checks run in parallel (Task.WhenAll) to minimise latency.
///
/// Context reconstruction strategy:
///   - If a conversation summary exists: summary + last 10 verbatim messages.
///   - If no summary: last 10 verbatim messages only (or all messages if ≤10).
///
/// Dataverse ETag check: issues a single-field OData GET ($select=primary-key) for each
/// entity reference. Dataverse includes the current ETag in the response @odata.etag
/// property. A mismatch against the saved ETag marks the reference as stale.
///
/// Lifetime: Scoped — one instance per HTTP request (injected by session-resume endpoint).
/// Tenant isolation: all operations are scoped by tenantId (ADR-015, NFR-09).
/// </summary>
public class SessionRestoreService : ISessionRestoreService
{
    /// <summary>Number of verbatim messages kept at the tail of the context window.</summary>
    internal const int VerbatimTailLength = 10;

    /// <summary>Section delimiter for the reconstructed context string.</summary>
    internal const string SummaryHeader = "[CONVERSATION SUMMARY]";

    /// <summary>Section delimiter for verbatim messages in the reconstructed context string.</summary>
    internal const string RecentMessagesHeader = "[RECENT MESSAGES]";

    // Dataverse entity logical name → primary key field name (used for the minimal OData $select query).
    // Extend as new entity types are referenced in sessions.
    private static readonly Dictionary<string, string> PrimaryKeyByEntityType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["opportunity"] = "opportunityid",
        ["account"] = "accountid",
        ["contact"] = "contactid",
        ["sprk_matter"] = "sprk_matterid",
        ["sprk_document"] = "sprk_documentid",
        ["sprk_analysis"] = "sprk_analysisid",
    };

    private readonly ISessionPersistenceService _persistence;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly ILogger<SessionRestoreService> _logger;

    public SessionRestoreService(
        ISessionPersistenceService persistence,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        TokenCredential credential,
        ILogger<SessionRestoreService> logger)
    {
        _persistence = persistence;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _credential = credential;
        _logger = logger;
    }

    // =========================================================================
    // ISessionRestoreService
    // =========================================================================

    /// <inheritdoc/>
    public async Task<RestoredSession?> RestoreSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        // ── Step 1: Load session from Cosmos DB (Redis hot path / Cosmos warm path) ──
        var session = await _persistence.LoadSessionAsync(tenantId, sessionId, ct);
        if (session is null)
        {
            _logger.LogDebug(
                "SessionRestoreService: session {SessionId} not found (tenant={TenantId})",
                sessionId, tenantId);
            return null;
        }

        _logger.LogDebug(
            "SessionRestoreService: loaded session {SessionId} ({MessageCount} messages, tenant={TenantId})",
            sessionId, session.Messages.Count, tenantId);

        // ── Step 2: Check entity staleness in parallel ──
        var staleRefs = await CheckEntityStalenessAsync(session.EntityRefs, ct);

        if (staleRefs.Count > 0)
        {
            _logger.LogInformation(
                "SessionRestoreService: session {SessionId} has {StaleCount} stale entity ref(s): {EntityIds}",
                sessionId, staleRefs.Count,
                string.Join(", ", staleRefs.Select(r => $"{r.EntityType}/{r.EntityId}")));
        }

        // ── Step 3: Reconstruct context window ──
        var (reconstructedContext, wasSummarized) = ReconstructContext(session);

        // ── Step 4: Collect widget states ──
        IReadOnlyDictionary<string, string> widgetStates = session.WidgetStates.Count > 0
            ? session.WidgetStates.AsReadOnly()
            : new Dictionary<string, string>().AsReadOnly();

        // ── Step 5: Extract recent messages for frontend conversation restore ──
        var recentMessages = session.Messages.Count <= VerbatimTailLength
            ? (IReadOnlyList<SessionMessage>)session.Messages.AsReadOnly()
            : session.Messages.Skip(session.Messages.Count - VerbatimTailLength).ToList().AsReadOnly();

        sw.Stop();

        _logger.LogInformation(
            "SessionRestoreService: restored session {SessionId} in {LatencyMs}ms " +
            "(messages={MessageCount}, stale={StaleCount}, summarized={WasSummarized}, tenant={TenantId})",
            sessionId, sw.ElapsedMilliseconds,
            session.Messages.Count, staleRefs.Count, wasSummarized, tenantId);

        if (sw.ElapsedMilliseconds > 500)
        {
            _logger.LogWarning(
                "SessionRestoreService: restore for session {SessionId} exceeded 500ms NFR target: {LatencyMs}ms (tenant={TenantId})",
                sessionId, sw.ElapsedMilliseconds, tenantId);
        }

        return new RestoredSession(
            SessionId: session.SessionId,
            TenantId: session.TenantId,
            PlaybookId: session.PlaybookId,
            ReconstructedContext: reconstructedContext,
            StaleEntityRefs: staleRefs,
            WidgetStates: widgetStates,
            WasSummarized: wasSummarized,
            RestoredAt: DateTimeOffset.UtcNow,
            RestoreLatencyMs: sw.ElapsedMilliseconds,
            RecentMessages: recentMessages);
    }

    // =========================================================================
    // Context reconstruction
    // =========================================================================

    /// <summary>
    /// Reconstructs the LLM context string from the session's stored summary (if any)
    /// and the last <see cref="VerbatimTailLength"/> verbatim messages.
    ///
    /// Format when summary exists:
    /// <code>
    /// [CONVERSATION SUMMARY]
    /// {summary text}
    ///
    /// [RECENT MESSAGES]
    /// user: {content}
    /// assistant: {content}
    /// ...
    /// </code>
    ///
    /// Format without summary:
    /// <code>
    /// [RECENT MESSAGES]
    /// user: {content}
    /// ...
    /// </code>
    /// </summary>
    /// <returns>Tuple of (contextString, wasSummarized).</returns>
    internal static (string Context, bool WasSummarized) ReconstructContext(StoredSession session)
    {
        var messages = session.Messages;

        // Prefer the structured SessionSummary (AIPU2-032) over the plain-text ConversationSummary.
        // Both are stored on the session document; the structured summary is produced by a dedicated
        // GPT-4o summarization pass and is richer in legal qualifications.
        var summary = session.Summary?.NarrativeSummary
            ?? session.ConversationSummary;
        var hasSummary = !string.IsNullOrWhiteSpace(summary);

        // Take the last N verbatim messages
        var verbatimMessages = messages.Count <= VerbatimTailLength
            ? messages
            : messages.Skip(messages.Count - VerbatimTailLength).ToList();

        var sb = new StringBuilder();

        if (hasSummary)
        {
            sb.AppendLine(SummaryHeader);
            sb.AppendLine(summary);
            sb.AppendLine();
        }

        sb.AppendLine(RecentMessagesHeader);
        foreach (var msg in verbatimMessages)
        {
            // Format: "role: content" — roles are "user", "assistant", "system"
            sb.Append(msg.Role);
            sb.Append(": ");
            sb.AppendLine(msg.Content);
        }

        return (sb.ToString().TrimEnd(), hasSummary);
    }

    // =========================================================================
    // Dataverse entity staleness checks
    // =========================================================================

    /// <summary>
    /// Checks all entity references in parallel. Returns only the stale ones
    /// (current Dataverse ETag differs from saved ETag).
    /// Non-fatal: individual check failures are caught and logged at Warning;
    /// the reference is NOT added to stale list on error (conservative: don't
    /// falsely warn the user if the check itself fails).
    /// </summary>
    private async Task<IReadOnlyList<SessionEntityRef>> CheckEntityStalenessAsync(
        IEnumerable<SessionEntityRef> entityRefs,
        CancellationToken ct)
    {
        var refs = entityRefs.ToList();
        if (refs.Count == 0)
        {
            return [];
        }

        // Resolve Dataverse base URL once — shared across all parallel checks
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"];
        if (string.IsNullOrWhiteSpace(dataverseUrl))
        {
            _logger.LogWarning(
                "SessionRestoreService: Dataverse:ServiceUrl not configured — skipping ETag staleness check");
            return [];
        }

        // Acquire a Dataverse bearer token once for all parallel checks
        string? bearerToken = null;
        try
        {
            bearerToken = await AcquireDataverseTokenAsync(dataverseUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SessionRestoreService: failed to acquire Dataverse token — skipping ETag staleness check");
            return [];
        }

        // Run all entity checks in parallel (Task.WhenAll) to keep latency low
        var checkTasks = refs.Select(r => CheckSingleEntityAsync(r, dataverseUrl, bearerToken, ct));
        var results = await Task.WhenAll(checkTasks);

        return results.Where(r => r is not null).Select(r => r!).ToList().AsReadOnly();
    }

    /// <summary>
    /// Checks a single entity reference. Returns the ref if stale, null if current or on error.
    /// Issues a minimal OData GET ($select=primaryKey) — Dataverse returns @odata.etag in the body.
    /// </summary>
    private async Task<SessionEntityRef?> CheckSingleEntityAsync(
        SessionEntityRef entityRef,
        string dataverseBaseUrl,
        string bearerToken,
        CancellationToken ct)
    {
        // Skip if we have no saved ETag to compare against
        if (string.IsNullOrWhiteSpace(entityRef.SavedETag))
        {
            return null;
        }

        try
        {
            var http = _httpClientFactory.CreateClient("DataverseETagCheck");
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            http.DefaultRequestHeaders.Add("OData-Version", "4.0");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Determine which primary key field to select.
            // Falls back to "createdon" (always present) if entity type is unknown —
            // Dataverse still includes @odata.etag in the response.
            var primaryKeyField = PrimaryKeyByEntityType.GetValueOrDefault(
                entityRef.EntityType, "createdon");

            var pluralEntitySet = GetEntitySetName(entityRef.EntityType);
            var apiBase = $"{dataverseBaseUrl.TrimEnd('/')}/api/data/v9.2/";
            var url = $"{apiBase}{pluralEntitySet}({entityRef.EntityId})?$select={primaryKeyField}";

            using var response = await http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "SessionRestoreService: ETag check for {EntityType}/{EntityId} returned {StatusCode} — skipping",
                    entityRef.EntityType, entityRef.EntityId, response.StatusCode);
                return null;
            }

            // Dataverse returns ETag in both the OData-EntityId response header
            // and the @odata.etag JSON property.  Use the response header first
            // (more reliable — present even when $select reduces the body).
            string? currentETag = null;

            if (response.Headers.TryGetValues("ETag", out var etagValues))
            {
                currentETag = etagValues.FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(currentETag))
            {
                // Fall back to parsing @odata.etag from the JSON body
                var body = await response.Content.ReadAsStringAsync(ct);
                currentETag = ExtractODataETag(body);
            }

            if (string.IsNullOrWhiteSpace(currentETag))
            {
                _logger.LogDebug(
                    "SessionRestoreService: no ETag in response for {EntityType}/{EntityId} — skipping staleness check",
                    entityRef.EntityType, entityRef.EntityId);
                return null;
            }

            // Compare saved vs current ETag (normalise surrounding quotes for robustness)
            var savedNorm = NormaliseETag(entityRef.SavedETag);
            var currentNorm = NormaliseETag(currentETag);

            if (!string.Equals(savedNorm, currentNorm, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "SessionRestoreService: stale ETag for {EntityType}/{EntityId} — saved={SavedETag}, current={CurrentETag}",
                    entityRef.EntityType, entityRef.EntityId, entityRef.SavedETag, currentETag);
                return entityRef;
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SessionRestoreService: ETag check failed for {EntityType}/{EntityId} — treating as current",
                entityRef.EntityType, entityRef.EntityId);
            return null;
        }
    }

    // =========================================================================
    // Auth helpers
    // =========================================================================

    private async Task<string> AcquireDataverseTokenAsync(string dataverseUrl, CancellationToken ct)
    {
        // Uses the DI-injected TokenCredential (UAMI-pinned via ManagedIdentityCredentialFactory).
        var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
        var tokenResponse = await _credential.GetTokenAsync(
            new TokenRequestContext([scope]),
            ct);

        return tokenResponse.Token;
    }

    // =========================================================================
    // Static helpers
    // =========================================================================

    /// <summary>
    /// Converts a Dataverse entity logical name to its OData entity set (plural) name.
    /// Handles the common cases: "opportunity" → "opportunities", "contact" → "contacts",
    /// "sprk_matter" → "sprk_matters", etc.
    /// </summary>
    internal static string GetEntitySetName(string entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return entityType;
        }

        // Custom entity types ending in 'y' → replace with 'ies' (e.g., "opportunity" → "opportunities")
        if (entityType.EndsWith('y') &&
            entityType.Length > 1 &&
            !"aeiou".Contains(entityType[^2]))
        {
            return entityType[..^1] + "ies";
        }

        // Standard plural: append 's' — e.g., "contact" → "contacts", "sprk_matter" → "sprk_matters"
        return entityType + "s";
    }

    /// <summary>
    /// Extracts the @odata.etag value from a Dataverse OData JSON response body.
    /// Returns null if the property is not present.
    /// Returns the raw substring as it appears in the body between the value's
    /// opening and closing quotes (JSON escape sequences preserved); callers that
    /// need an unescaped value can run the result through their own JSON unescape.
    /// </summary>
    /// <remarks>
    /// 2026-06-01 — RB-T012-01 repaired. Prior implementation used
    /// <c>IndexOf('"', start)</c> which stopped at the first JSON-escaped <c>\"</c>
    /// inside the ETag value and returned a truncated result. The fix scans
    /// character-by-character honoring backslash escapes so the closing quote is
    /// correctly located on the unescaped boundary.
    /// </remarks>
    internal static string? ExtractODataETag(string jsonBody)
    {
        if (string.IsNullOrWhiteSpace(jsonBody))
        {
            return null;
        }

        // Format: "\"@odata.etag\":\"W/\\\"1234567\\\"\""
        const string marker = "\"@odata.etag\":\"";
        var start = jsonBody.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;

        // Escape-aware scan for the closing quote: skip any '"' preceded by an odd
        // number of trailing backslashes (i.e., JSON-escaped quotes). Preserves
        // the raw escaped substring in the returned value.
        var end = -1;
        for (var i = start; i < jsonBody.Length; i++)
        {
            if (jsonBody[i] != '"')
            {
                continue;
            }

            // Count trailing backslashes immediately before this quote.
            var backslashes = 0;
            for (var j = i - 1; j >= start && jsonBody[j] == '\\'; j--)
            {
                backslashes++;
            }

            // Even count (including zero) → this '"' is not escaped → it is the closing delimiter.
            if (backslashes % 2 == 0)
            {
                end = i;
                break;
            }
        }

        if (end < 0)
        {
            return null;
        }

        return jsonBody[start..end];
    }

    /// <summary>
    /// Strips surrounding double-quotes from an ETag value for comparison.
    /// e.g., <c>"W/\"1234\""</c> → <c>W/"1234"</c>.
    /// </summary>
    /// <remarks>
    /// 2026-06-01 — RB-T012-01 repaired. Prior implementation used
    /// <c>etag.Trim('"')</c> which is greedy and over-stripped embedded quote
    /// characters (e.g., <c>W/"1234"</c> became <c>W/"1234</c>). The fix strips at
    /// most one matched leading + trailing <c>"</c> pair, leaving embedded quotes
    /// intact and leaving values without an outer pair untouched.
    /// </remarks>
    internal static string NormaliseETag(string etag)
    {
        if (etag is null)
        {
            return etag!;
        }

        if (etag.Length >= 2 && etag[0] == '"' && etag[^1] == '"')
        {
            return etag[1..^1];
        }

        return etag;
    }
}
