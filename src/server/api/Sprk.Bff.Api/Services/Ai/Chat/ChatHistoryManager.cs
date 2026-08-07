using System.Text;
using System.Text.Json;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Manages chat message history: addition, retrieval, summarisation, and archiving.
///
/// Design:
///   - Messages are written to Dataverse (audit trail) then the Redis cache is refreshed.
///   - After adding a message, summarisation is triggered if the session has &gt;= 15 messages.
///   - Archive is triggered when approaching the 50-message limit (NFR-12).
///   - History retrieval returns the most recent N messages from Redis (hot path).
///
/// Cosmos durability (FR-D2 / ADR-040, task 030): the Redis-refresh step also write-throughs to
/// Cosmos DB via <see cref="ChatSessionManager.UpdateSessionCacheAsync"/>. For messages[0] — the
/// first message of a brand-new session, which also seeds the History title (FR-D4) — that Cosmos
/// write is CONFIRMED (awaited) so the transcript survives a Redis eviction; every later turn keeps
/// the original fire-and-forget write-through (D-06, NFR-03 — no latency regression on turns 2+).
///
/// Summarisation (NFR / spec):
///   - Trigger: <c>session.Messages.Count &gt;= <see cref="SummarisationThreshold"/></c>
///   - Action: Condenses older messages into a single summary text stored in
///     <c>sprk_aichatsummary.sprk_summary</c>.
///   - In Phase 1 (AIPL-052), summarisation generates a placeholder summary.
///     The real LLM-based summarisation is implemented in AIPL-054 (ChatEndpoints).
///
/// Ledger-aware digest (ADR-040 / FR-P0-02):
///   - The compacted digest covers session-ledger <see cref="SessionOutput"/> entries in
///     addition to conversational history. Each output contributes one digest line carrying
///     its addressable <c>{bindingId}@t{n}</c> key (see <see cref="SessionLedger.BuildOutputKey"/>),
///     disposition, uc id, and a size-capped content snippet — so post-compaction sessions
///     can still resolve ledger references ("email that summary to John" at G-P2).
///   - Both compaction events (summarise@15 and archive@50) emit the output section.
///   - Governance (ADR-015 / NFR-07): the digest is Tier 3 session data persisted to
///     <c>sprk_aichatsummary.sprk_summary</c>; log statements carry counts / ids ONLY —
///     never digest or payload content.
///
/// Lifetime: Scoped — one instance per HTTP request (ADR-010).
/// </summary>
public sealed class ChatHistoryManager
{
    /// <summary>
    /// Number of messages that triggers conversation summarisation.
    /// Matches the spec constraint: "Summarize after 15 messages".
    /// </summary>
    public const int SummarisationThreshold = 15;

    /// <summary>
    /// Maximum messages per session before the history is archived (NFR-12).
    /// </summary>
    public const int ArchiveThreshold = 50;

    /// <summary>
    /// Default maximum number of messages to return from <see cref="GetHistoryAsync"/>.
    /// </summary>
    public const int DefaultMaxMessages = 50;

    /// <summary>
    /// Maximum length of the per-output content snippet embedded in the compacted
    /// digest (ADR-040: digests summarize outputs compactly; full payloads stay in
    /// the ledger, addressable by key).
    /// </summary>
    public const int MaxOutputSnippetLength = 120;

    // Task 053 (FR-B-04): the live-turn ledger-outputs context (BuildLedgerOutputsContext +
    // BuildPayloadContextText) and its MaxContextOutputs / MaxContextPayloadChars caps moved to
    // ContextSliceProducers.ConversationContextProducer — the single production home for the
    // Memory.Conversation primitive shared by the interactive chat endpoint and the Context Binder.
    // The compaction-digest path (BuildOutputDigestSection / MaxOutputSnippetLength) stays here; both
    // paths reuse the shared surrogate-safe truncation below.

    /// <summary>
    /// FR-D4 (task 032) — maximum output tokens for the cheap session-title completion.
    /// A 3-6 word title is a handful of tokens; this bounds cost + latency of the call.
    /// </summary>
    private const int TitleGenerationMaxOutputTokens = 16;

    /// <summary>FR-D4 — display cap for the generated/fallback title (matches the History list's single-line rendering).</summary>
    private const int TitleMaxLength = 60;

    private readonly ChatSessionManager _sessionManager;
    private readonly IChatDataverseRepository _dataverseRepository;
    private readonly ILogger<ChatHistoryManager> _logger;

    /// <summary>
    /// FR-D4 (task 032) — optional cheap-completion client used to grounded-generate the
    /// session title at the first substantive exchange. Null when the AI DocumentIntelligence
    /// compound gate is OFF (<see cref="IOpenAiClient"/> is registered conditionally —
    /// <c>AnalysisServicesModule</c>); <see cref="ChatHistoryManager"/> itself stays registered
    /// UNCONDITIONALLY (B5), so this MUST be optional/nullable to avoid the §F.1
    /// asymmetric-registration anti-pattern (a required dep here would break message-adding
    /// entirely when AI is OFF). When null, title generation degrades to the deterministic
    /// first-user-message fallback (FR-D4 fallback chain) — never a heavyweight new mechanism.
    /// </summary>
    private readonly IOpenAiClient? _openAiClient;

    public ChatHistoryManager(
        ChatSessionManager sessionManager,
        IChatDataverseRepository dataverseRepository,
        ILogger<ChatHistoryManager> logger,
        IOpenAiClient? openAiClient = null)
    {
        _sessionManager = sessionManager;
        _dataverseRepository = dataverseRepository;
        _logger = logger;
        _openAiClient = openAiClient;
    }

    /// <summary>
    /// Adds a message to the session history.
    ///
    /// Execution order:
    ///   1. Persist message to Dataverse (<c>sprk_aichatmessage</c>).
    ///   2. Append message to the session's in-memory list.
    ///   3. Update the Redis cache with the new message and last-activity timestamp.
    ///   4. Update session activity in Dataverse (message count).
    ///   5. Trigger summarisation if threshold reached.
    ///   6. Trigger archive if approaching max.
    /// </summary>
    /// <param name="session">The session to add the message to.  Passed by reference — the
    /// updated session (with the new message) is returned.</param>
    /// <param name="message">The message to add.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <see cref="ChatSession"/> with the new message appended.</returns>
    public async Task<ChatSession> AddMessageAsync(
        ChatSession session,
        ChatMessage message,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Adding {Role} message to session {SessionId} (seq={SeqNum}, tenant={TenantId})",
            message.Role, session.SessionId, message.SequenceNumber, session.TenantId);

        // 1. Persist to Dataverse (audit trail — cold storage)
        // Non-fatal: if chat entities are not yet deployed, log and continue with Redis.
        try
        {
            await _dataverseRepository.AddMessageAsync(message, ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Dataverse message persistence failed for session {SessionId} — continuing with Redis only",
                session.SessionId);
        }

        // 2. Rebuild session with new message appended
        var updatedMessages = new List<ChatMessage>(session.Messages) { message };
        var updatedSession = session with
        {
            Messages = updatedMessages.AsReadOnly(),
            LastActivity = DateTimeOffset.UtcNow
        };

        // FR-D4 (task 032): session.Messages.Count is the PRE-append count — Count == 0 means
        // `message` is minting messages[0] (the very first message of a brand-new session). When
        // that first message is from the user, seed the stored, human-readable session title here
        // — the ONE place a title is ever minted for a new session, so the History list and this
        // write never double-source a title (see StoredSession.Title remarks). Sessions that
        // already carry a title (e.g. restored from an older flow, or renamed before their first
        // message somehow landed) are left untouched.
        //
        // This call happens AFTER SendMessageAsync has already written the SSE "done" event to the
        // client (see ChatEndpoints.SendMessageAsync) — the stream is over from the caller's
        // perspective, so an awaited cheap completion here adds NO perceived latency to the turn,
        // even though it does extend how long the underlying request handler keeps running.
        if (session.Messages.Count == 0 &&
            message.Role == ChatMessageRole.User &&
            string.IsNullOrWhiteSpace(updatedSession.Title))
        {
            var title = await GenerateSessionTitleAsync(message.Content, ct);
            updatedSession = updatedSession with { Title = title };
        }

        // 3. Refresh the Redis hot cache with the updated session.
        //
        // FR-D2 (task 030): session.Messages.Count is the PRE-append count — the index `message`
        // will occupy once appended. Count == 0 means this call is minting messages[0] (the very
        // first message of a brand-new session), which also seeds the History title (FR-D4). That
        // ONE write is CONFIRMED (awaitCosmosWrite: true) so the transcript survives a Redis
        // eviction even if it happens moments after this request completes. Every later turn
        // (Count > 0) keeps the default fire-and-forget write-through (D-06) — no latency
        // regression for turns 2+ (NFR-03).
        var isFirstMessage = session.Messages.Count == 0;
        await _sessionManager.UpdateSessionCacheAsync(updatedSession, ct, awaitCosmosWrite: isFirstMessage);

        // 4. Update session activity in Dataverse (fire-and-forget acceptable for counters)
        _ = _dataverseRepository.UpdateSessionActivityAsync(
            session.TenantId,
            session.SessionId,
            updatedMessages.Count,
            updatedSession.LastActivity,
            ct);

        // 5. Trigger summarisation if threshold reached
        if (updatedMessages.Count >= SummarisationThreshold)
        {
            await TriggerSummarisationAsync(updatedSession, ct);
        }

        // 6. Trigger archive if approaching 50-message limit
        if (updatedMessages.Count >= ArchiveThreshold)
        {
            await ArchiveHistoryAsync(updatedSession, ct);
        }

        return updatedSession;
    }

    // =========================================================================
    // Title generation (FR-D4, task 032)
    // =========================================================================
    //
    // PLACEMENT JUSTIFICATION (CLAUDE.md §10 BFF Hygiene / §11 Component Justification):
    //   - Existing: no stored title existed anywhere; BuildSessionTitle
    //     (SessionPersistenceService.cs) was read-computed-only for the History list, with
    //     nowhere for a generated label to persist and no rename surface.
    //   - Extension: reuses the EXISTING IOpenAiClient.GetCompletionAsync cheap-completion
    //     primitive (the same one SummarizationCompressionService already calls directly,
    //     outside the ADR-039 three-entry-path dispatch protocol) for a bounded, one-shot,
    //     ~16-token label. NOT a second intent-detection/classifier mechanism (ADR-039 MUST
    //     NOT) — it makes no dispatch decision and selects no capability; it produces a
    //     short descriptive string, same class of call as the existing digest/summary
    //     completions in this file's sibling TriggerSummarisationAsync path.
    //   - Cost of doing nothing: sessions would keep showing a bare timestamp or the
    //     read-computed heuristic with nowhere to persist a nicer label, and users would have
    //     no way to rename a session (FR-D4).
    //
    // FALLBACK CHAIN (binding per POML `<constraint source="project">`): generated -> first
    // user message -> NEVER a bare timestamp. Because this method only ever runs at the
    // session's first user message (see the AddMessageAsync call site), the deterministic
    // fallback is ALWAYS available — a bare timestamp is structurally unreachable here.

    /// <summary>
    /// Produces the FR-D4 stored session title for a brand-new session from its first user
    /// message. Tries a cheap grounded completion first (bounded to
    /// <see cref="TitleGenerationMaxOutputTokens"/> output tokens); on any failure — circuit
    /// broken, transient error, disabled AI (<see cref="_openAiClient"/> null), cancellation,
    /// or an unusable model response — degrades to the deterministic first-message fallback.
    /// Never returns a bare timestamp (that fallback is reserved for
    /// <see cref="Sessions.SessionPersistenceService"/>'s legacy read-computed heuristic for
    /// sessions that pre-date this field entirely).
    /// </summary>
    private async Task<string> GenerateSessionTitleAsync(string firstMessageContent, CancellationToken ct)
    {
        var fallback = BuildFallbackTitle(firstMessageContent);

        if (_openAiClient is null)
        {
            return fallback;
        }

        try
        {
            var prompt =
                "Write a short, work-descriptive title (3-6 words, no quotes, no trailing " +
                "punctuation, no markdown) for a conversation that starts with this message:\n\n" +
                TruncateSurrogateSafe(firstMessageContent, 500);

            var raw = await _openAiClient.GetCompletionAsync(
                prompt: prompt,
                maxOutputTokens: TitleGenerationMaxOutputTokens,
                cancellationToken: ct);

            var cleaned = CleanGeneratedTitle(raw);
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller (AddMessageAsync) is invoked with CancellationToken.None from
            // SendMessageAsync (the SSE stream has already completed by this point), so this
            // branch is defensive rather than expected in production traffic.
            return fallback;
        }
        catch (OpenAiCircuitBrokenException ex)
        {
            _logger.LogWarning(ex,
                "GenerateSessionTitleAsync: OpenAI circuit broken — falling back to first-message title");
            return fallback;
        }
        catch (Exception ex)
        {
            // P2 Quiet — title generation is an enhancement; the deterministic fallback always
            // produces a usable, non-timestamp title (FR-D4 fallback chain).
            _logger.LogWarning(ex,
                "GenerateSessionTitleAsync: LLM call failed — falling back to first-message title");
            return fallback;
        }
    }

    /// <summary>
    /// Deterministic, LLM-free title fallback: the first single line of the user's opening
    /// message, capped at <see cref="TitleMaxLength"/> characters. Always non-empty-producing
    /// when called with the session's actual first user message content (FR-D4 fallback chain
    /// — never a bare timestamp at this call site).
    /// </summary>
    private static string BuildFallbackTitle(string firstMessageContent)
    {
        var oneLine = firstMessageContent.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (string.IsNullOrWhiteSpace(oneLine))
        {
            // Defensive only — SendMessageAsync always supplies non-empty request.Message.
            return "New conversation";
        }

        return TruncateSurrogateSafe(oneLine, TitleMaxLength);
    }

    /// <summary>
    /// Strips common LLM title artifacts (surrounding quotes, a trailing period, collapsed
    /// whitespace) and enforces <see cref="TitleMaxLength"/>. Returns an empty string when the
    /// model response is unusable (empty/whitespace-only) so the caller falls back.
    /// </summary>
    private static string CleanGeneratedTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var oneLine = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        oneLine = oneLine.Trim('"', '\'', '“', '”', '`');
        oneLine = oneLine.TrimEnd('.', ' ');

        while (oneLine.Contains("  ", StringComparison.Ordinal))
        {
            oneLine = oneLine.Replace("  ", " ");
        }

        return string.IsNullOrWhiteSpace(oneLine) ? string.Empty : TruncateSurrogateSafe(oneLine, TitleMaxLength);
    }

    /// <summary>
    /// Returns the most recent N messages for a session.
    ///
    /// Hot path: reads from the <see cref="ChatSession.Messages"/> list in Redis.
    /// If the session is not in Redis, falls back to the Dataverse cold path via
    /// <see cref="ChatSessionManager.GetSessionAsync"/>.
    /// </summary>
    /// <param name="tenantId">Tenant ID for multi-tenant isolation.</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="maxMessages">Maximum number of messages to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ordered list of messages (oldest first), up to <paramref name="maxMessages"/>.</returns>
    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string tenantId,
        string sessionId,
        int maxMessages = DefaultMaxMessages,
        CancellationToken ct = default)
    {
        var session = await _sessionManager.GetSessionAsync(tenantId, sessionId, ct);
        if (session is null)
        {
            _logger.LogWarning(
                "GetHistoryAsync: session {SessionId} not found for tenant {TenantId}",
                sessionId, tenantId);
            return Array.Empty<ChatMessage>();
        }

        var messages = session.Messages;
        if (messages.Count <= maxMessages)
        {
            return messages;
        }

        // Return the most recent N messages (tail of the ordered list)
        return messages
            .Skip(messages.Count - maxMessages)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Triggers conversation summarisation when the message count reaches
    /// <see cref="SummarisationThreshold"/> (15 messages).
    ///
    /// Phase 1 implementation: generates a placeholder summary.
    /// Phase D (AIPL-054+): will call the LLM via SprkChatAgent to produce a real summary.
    ///
    /// The summary is stored in <c>sprk_aichatsummary.sprk_summary</c> (cold storage).
    /// </summary>
    /// <param name="session">The session that has reached the summarisation threshold.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task TriggerSummarisationAsync(ChatSession session, CancellationToken ct = default)
    {
        // NFR-07: log counts / ids only — never message, digest, or output content.
        _logger.LogInformation(
            "Summarisation triggered for session {SessionId} (messageCount={Count}, outputCount={OutputCount}, tenant={TenantId})",
            session.SessionId, session.Messages.Count, session.Outputs?.Count ?? 0, session.TenantId);

        // Phase 1: Placeholder summary — real LLM summarisation added in AIPL-054.
        // The summary condenses older messages to free context for newer messages.
        var olderMessages = session.Messages
            .Take(session.Messages.Count - 5) // Keep last 5 in full; summarise the rest
            .Select(m => $"[{m.Role}]: {m.Content[..Math.Min(100, m.Content.Length)]}...")
            .ToList();

        var summaryText = $"[Summary of {olderMessages.Count} earlier messages — "
                         + $"session {session.SessionId}, generated {DateTimeOffset.UtcNow:u}]"
                         + BuildOutputDigestSection(session.Outputs);

        await _dataverseRepository.UpdateSessionSummaryAsync(
            session.TenantId,
            session.SessionId,
            summaryText,
            ct);
    }

    /// <summary>
    /// Archives the session's message history when the 50-message limit is approached (NFR-12).
    ///
    /// In Phase 1 (AIPL-052), archiving logs a warning and persists the current summary.
    /// Full archival strategy (moving older messages to a secondary store) is deferred to Phase D.
    /// </summary>
    /// <param name="session">The session approaching the archive threshold.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ArchiveHistoryAsync(ChatSession session, CancellationToken ct = default)
    {
        // NFR-07: log counts / ids only — never message, digest, or output content.
        _logger.LogWarning(
            "Archive threshold reached for session {SessionId} (messageCount={Count}, outputCount={OutputCount}). "
            + "Archiving history (NFR-12). Tenant={TenantId}",
            session.SessionId, session.Messages.Count, session.Outputs?.Count ?? 0, session.TenantId);

        // Phase 1: Log and persist final summary.
        // Full archival (moving sprk_aichatmessage records to archive entity) is deferred.
        var archiveSummary = $"[ARCHIVED — session {session.SessionId} reached {session.Messages.Count} messages "
                            + $"at {DateTimeOffset.UtcNow:u}]"
                            + BuildOutputDigestSection(session.Outputs);

        await _dataverseRepository.UpdateSessionSummaryAsync(
            session.TenantId,
            session.SessionId,
            archiveSummary,
            ct);
    }

    // =========================================================================
    // Ledger-outputs live turn context (ADR-040 / G-P2 UAT round-1 finding 3)
    // =========================================================================

    // Task 053 (FR-B-04): BuildLedgerOutputsContext + BuildPayloadContextText (the live agent-turn
    // "Session Outputs" context primitive) moved to
    // ContextSliceProducers.ConversationContextProducer.BuildLedgerOutputsContext — the single production
    // home for the Memory.Conversation primitive shared by the interactive chat endpoint (ChatEndpoints)
    // and the Context Binder. The producer reuses TruncateSurrogateSafe below (exposed internal).

    // =========================================================================
    // Ledger-output digest (ADR-040 / FR-P0-02)
    // =========================================================================

    /// <summary>
    /// Builds the ledger-outputs section of the compacted session digest.
    ///
    /// One line per <see cref="SessionOutput"/>, carrying the addressable
    /// <c>{bindingId}@t{n}</c> key VERBATIM (ADR-040: references must remain
    /// resolvable post-compaction), the disposition, the uc id, and a content
    /// snippet capped at <see cref="MaxOutputSnippetLength"/> characters.
    ///
    /// Returns <see cref="string.Empty"/> when the session has no ledger outputs,
    /// leaving the pre-ledger digest shape byte-for-byte unchanged (additive
    /// generalization — spec FR-P0-02 acceptance).
    /// </summary>
    private static string BuildOutputDigestSection(IReadOnlyList<SessionOutput>? outputs)
    {
        if (outputs is null || outputs.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append('\n').Append($"[Ledger outputs ({outputs.Count}) — addressable post-compaction]");

        foreach (var output in outputs)
        {
            sb.Append('\n')
              .Append("- ").Append(output.Key)
              .Append(" [").Append(output.Disposition).Append(']')
              .Append(' ').Append(output.UcId);

            var snippet = BuildPayloadSnippet(output.Payload);
            if (snippet.Length > 0)
            {
                sb.Append(": ").Append(snippet);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts a compact, single-line snippet from an output payload for the digest.
    /// Prefers a summary-like string property on object payloads; falls back to the
    /// raw JSON. Always capped at <see cref="MaxOutputSnippetLength"/> characters —
    /// the digest summarizes, the ledger entry remains the full payload.
    /// </summary>
    private static string BuildPayloadSnippet(JsonElement payload)
    {
        var text = payload.ValueKind switch
        {
            JsonValueKind.Undefined => string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => payload.GetString() ?? string.Empty,
            JsonValueKind.Object => FindSummaryLikeProperty(payload) ?? payload.GetRawText(),
            _ => payload.GetRawText()
        };

        text = text.ReplaceLineEndings(" ").Trim();
        return TruncateSurrogateSafe(text, MaxOutputSnippetLength);
    }

    /// <summary>
    /// Caps <paramref name="text"/> at <paramref name="maxLength"/> characters, backing
    /// off one char if the cap would split a surrogate pair (e.g. an emoji), which would
    /// produce a malformed UTF-16 string. Appends an ellipsis only when truncated.
    /// </summary>
    internal static string TruncateSurrogateSafe(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = maxLength;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut] + "…";
    }

    /// <summary>
    /// Returns the first summary-like string property on an object payload
    /// (<c>summary</c>, <c>title</c>, <c>text</c>, <c>content</c>, <c>name</c> —
    /// case-insensitive), or null when none is present.
    /// </summary>
    private static string? FindSummaryLikeProperty(JsonElement payload)
    {
        string[] preferredNames = ["summary", "title", "text", "content", "name"];

        foreach (var name in preferredNames)
        {
            foreach (var property in payload.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String
                    && string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.GetString();
                }
            }
        }

        return null;
    }
}
