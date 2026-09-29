using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Cluster 7 of the <c>ComposeService</c> decomposition (task 070): what we remember about a
/// document for the assistant.
///
/// <para><b>Its reason to change</b> is the distillation policy — which session facts are durable
/// knowledge, how they map to <see cref="ComposeInsight"/>, and what provenance rides along. That is
/// a different question from "how does a save land bytes", which is why it separates cleanly: the
/// save path calls this once, after a <c>sprk_document</c> id exists, and does not consult the
/// result.</para>
///
/// <para><b>Best-effort by contract.</b> Every failure mode here is a no-op or a swallowed
/// exception. A memory-capture miss must never fail, block, or alter a Save — the Save has already
/// returned its result on its own terms (FR-30, compose-r2, deferral #629).</para>
///
/// <para>An <c>internal sealed</c> collaborator constructed from dependencies <c>ComposeService</c>
/// already holds — <b>no new DI registration</b>, which is the ADR-010 constraint task 070 works
/// under. Behaviour is unchanged from the method it replaces; this is a move, not a rewrite.</para>
/// </summary>
internal sealed class ComposeMemoryCapturer
{
    private readonly IComposeMemoryCapture? _memoryCapture;
    private readonly ChatSessionManager _sessions;
    private readonly ILogger _logger;

    /// <param name="memoryCapture">
    /// The ADR-013 facade (shared <c>IMemoryItemStore</c>, no forked store). NULL is a supported
    /// state, not a defect: AI/persistence is off in that host, and capture becomes a clean no-op.
    /// </param>
    internal ComposeMemoryCapturer(
        IComposeMemoryCapture? memoryCapture,
        ChatSessionManager sessions,
        ILogger logger)
    {
        _memoryCapture = memoryCapture;
        _sessions = sessions;
        _logger = logger;
    }

    /// <summary>
    /// Distils the session's durable insights (defined terms today) into Record-scope MemoryItems
    /// keyed by the newly-saved <c>sprk_document</c>.
    /// </summary>
    internal async Task CaptureDocumentMemoryAsync(
        Guid documentId,
        string tenantId,
        string? sessionId,
        CancellationToken ct)
    {
        // Availability gate + precondition: no facade (AI/persistence off in this host) or no bound
        // session → nothing to capture. Both are clean no-ops, not failures.
        if (_memoryCapture is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            var session = await _sessions.GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
            var definedTerms = session?.DefinedTermsTracking;
            if (definedTerms is null || definedTerms.Count == 0)
            {
                return;
            }

            // DISTILL: a defined term is durable knowledge only when it carries BOTH a non-empty label
            // (the fact Key) AND a definition (the fact Value). Guarding Term too avoids persisting an
            // empty-key fact (two of which would collide on the store's hashed empty key).
            var insights = new List<ComposeInsight>(definedTerms.Count);
            foreach (var term in definedTerms)
            {
                if (string.IsNullOrWhiteSpace(term.Term) || string.IsNullOrWhiteSpace(term.Definition))
                {
                    continue;
                }

                insights.Add(new ComposeInsight(
                    FactType: "defined-term",
                    Key: term.Term,
                    Value: term.Definition!,
                    Origin: string.Equals(term.Source, "ai", StringComparison.OrdinalIgnoreCase)
                        ? MemoryOrigin.AiDerived
                        : MemoryOrigin.User,
                    // Confidence null → the store default (1.0), which keeps AI-extracted terms above the
                    // recall confidence gate so they DO surface in later sessions (the FR-30 point).
                    // ConfirmedByUser=false (set in the facade) is the honest "unverified" marker; DefinedTerm
                    // carries no per-term confidence signal to thread here.
                    Confidence: null,
                    BindingId: term.Provenance?.BindingId,
                    LedgerRef: term.Provenance?.LedgerRef));
            }

            if (insights.Count == 0)
            {
                return;
            }

            var outcome = await _memoryCapture.CaptureRecordInsightsAsync(
                    subjectType: "sprk_document",
                    subjectId: documentId.ToString(),
                    insights: insights,
                    provenance: new ComposeMemoryProvenance
                    {
                        TenantId = tenantId,
                        SessionId = sessionId,
                        CreatedBy = null,   // caller identity not threaded here; envelope stays honest (null)
                    },
                    ct)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Compose memory-capture (FR-30): document {DocumentId} — {Status} {Count} insight(s) (session={SessionId}).",
                documentId, outcome.Status, outcome.Count, sessionId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Honour cancellation quietly — the Save has already returned its result on its own terms.
        }
        catch (Exception ex)
        {
            // Best-effort: a memory-capture failure must never fail or block a Save (swallow + log).
            _logger.LogWarning(ex,
                "Compose memory-capture (FR-30): threw while capturing memory for document {DocumentId} — best-effort, Save unaffected.",
                documentId);
        }
    }
}
