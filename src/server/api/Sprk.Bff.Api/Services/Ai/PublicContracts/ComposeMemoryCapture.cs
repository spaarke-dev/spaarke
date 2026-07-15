using Sprk.Bff.Api.Services.Ai.Memory;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IComposeMemoryCapture"/>: maps each Compose-agnostic
/// <see cref="ComposeInsight"/> onto a governed Record-scope <see cref="MemoryItem"/> and persists it via
/// the SHARED <see cref="IMemoryItemStore"/> (upsert-by-<c>(scope, fact.Type, fact.Key)</c> supersession).
/// No forked store, no parallel memory logic (ADR-013 / §11 default-to-reuse).
/// </summary>
/// <remarks>
/// <para>
/// <b>Provenance envelope is METADATA (FR-B-08).</b> <c>Source</c> / <c>BindingId</c> / <c>LedgerRef</c> /
/// <c>SessionId</c> describe the capture; they do NOT gate it. <c>TrustLevel</c> is written NULL — the
/// untrusted-origin gate is DEFERRED to the memory-governance project (#629); it is carried inert, never
/// acted on here.
/// </para>
/// <para>
/// <b>DataverseFieldMirrorGuard (FR-B-01).</b> The store REJECTS (<see cref="ArgumentException"/>) a
/// Record-scope fact that merely mirrors a live Dataverse field. That rejection is per-ITEM: this facade
/// logs + SKIPS the offending insight and continues the batch — one bad insight never drops the rest.
/// </para>
/// <para>
/// <b>Best-effort:</b> only an honoured <see cref="OperationCanceledException"/> (on request cancellation)
/// propagates. Any other unexpected store failure degrades to
/// <see cref="ComposeMemoryCaptureOutcome.Failed(string)"/> — never thrown — so a caller's Save is never
/// blocked or failed by memory capture.
/// </para>
/// <para><b>Lifetime:</b> Scoped — registered alongside <see cref="IMemoryItemStore"/> in
/// <c>AiPersistenceModule</c> (NOT behind the compound-AI gate).</para>
/// </remarks>
public sealed class ComposeMemoryCapture : IComposeMemoryCapture
{
    private readonly IMemoryItemStore _store;
    private readonly ILogger<ComposeMemoryCapture> _logger;

    public ComposeMemoryCapture(IMemoryItemStore store, ILogger<ComposeMemoryCapture> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ComposeMemoryCaptureOutcome> CaptureRecordInsightsAsync(
        string subjectType,
        string subjectId,
        IReadOnlyList<ComposeInsight> insights,
        ComposeMemoryProvenance provenance,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(provenance);

        if (insights is null || insights.Count == 0)
        {
            return ComposeMemoryCaptureOutcome.Skipped("no durable insights to capture");
        }

        var captured = 0;
        var now = DateTimeOffset.UtcNow;

        try
        {
            foreach (var insight in insights)
            {
                ct.ThrowIfCancellationRequested();

                var item = new MemoryItem
                {
                    Version = MemoryItemContract.SchemaVersion,
                    Scope = MemoryScope.Record,
                    SubjectType = subjectType,
                    SubjectId = subjectId,
                    Fact = new MemoryFact
                    {
                        Type = MapFactType(insight.FactType),
                        Key = insight.Key,
                        Value = insight.Value,
                        Source = insight.Origin,
                        ConfirmedByUser = false,
                        Confidence = insight.Confidence ?? DefaultConfidence,
                        RecordedAt = now,
                    },
                    // ── Provenance envelope (METADATA, not a gate — FR-B-08) ──
                    Source = insight.Origin,
                    BindingId = insight.BindingId,
                    LedgerRef = insight.LedgerRef,
                    SessionId = provenance.SessionId,
                    // TrustLevel DELIBERATELY null: carried inert — enforcement DEFERRED to the memory-
                    // governance project (#629). Setting/reading it here would imply enforcement.
                    TrustLevel = null,
                    CreatedBy = provenance.CreatedBy,
                    CreatedAt = now,
                    DeletionPolicy = "user-erasable",
                    RetentionClass = "tier-3-user-owned",
                };

                try
                {
                    await _store.UpsertAsync(item, provenance.TenantId, ct).ConfigureAwait(false);
                    captured++;
                }
                catch (ArgumentException ex)
                {
                    // FR-B-01 DataverseFieldMirrorGuard (or invalid subject keying) rejects THIS item.
                    // Skip it and continue — a single rejected insight must not drop the whole batch.
                    _logger.LogWarning(
                        "Compose memory-capture: insight '{FactType}/{Key}' rejected by store for {SubjectType}/{SubjectId} ({ErrorType}) — skipped.",
                        insight.FactType, insight.Key, subjectType, subjectId, ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort: never propagate. The caller's Save has already succeeded on its own terms.
            _logger.LogWarning(ex,
                "Compose memory-capture: threw while capturing insights for {SubjectType}/{SubjectId} — best-effort, caller unaffected.",
                subjectType, subjectId);
            return ComposeMemoryCaptureOutcome.Failed(ex.GetType().Name);
        }

        _logger.LogInformation(
            "Compose memory-capture: persisted {Captured}/{Total} Record-scope insight(s) for {SubjectType}/{SubjectId} (session={SessionId}).",
            captured, insights.Count, subjectType, subjectId, provenance.SessionId);

        return ComposeMemoryCaptureOutcome.Captured(captured);
    }

    /// <summary>The store default (<see cref="MemoryFact.Confidence"/> = 1.0) when an insight carries none.</summary>
    private const double DefaultConfidence = 1.0;

    /// <summary>
    /// Maps a descriptive insight fact-type string onto the coarse <see cref="MemoryFactType"/> enum the
    /// store persists + groups by. Compose's durable insights (defined terms today) are structured facts
    /// about the record → <see cref="MemoryFactType.KeyFact"/> (the "any other structured fact" bucket).
    /// <para>
    /// COLLISION NOTE: the store supersedes by <c>(scope, factType, key)</c>. Only <c>defined-term</c> is
    /// produced today (Key = term text), so no collision exists. When a FUTURE second insight kind is added,
    /// map it to a DISTINCT <see cref="MemoryFactType"/> (add a case below) rather than letting it fall
    /// through <c>_ => KeyFact</c> — otherwise its Key could collide with a defined term and silently
    /// supersede it.
    /// </para>
    /// </summary>
    private static MemoryFactType MapFactType(string factType) => factType switch
    {
        "defined-term" => MemoryFactType.KeyFact,
        "party" => MemoryFactType.Party,
        "key-date" => MemoryFactType.KeyDate,
        "prior-analysis" => MemoryFactType.PriorAnalysis,
        _ => MemoryFactType.KeyFact,
    };
}
