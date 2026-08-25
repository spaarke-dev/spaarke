using Sprk.Bff.Api.Services.Ai.Memory;
using Sprk.Bff.Api.Services.Identity;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Default implementation of <see cref="IPreferenceMemoryCapture"/>: resolves the caller's AAD oid to the
/// canonical Dataverse <c>systemuserid</c>, maps the neutral <see cref="PreferenceCaptureInput"/> onto a
/// governed User-scope <see cref="MemoryItem"/> whose fact is a <see cref="MemoryFactType.Preference"/>,
/// and persists it via the SHARED <see cref="IMemoryItemStore"/> (upsert-by-<c>(scope, fact.Type, fact.Key)</c>
/// supersession). No forked store, no second memory write path (ADR-013 / §11 default-to-reuse) — the same
/// store the AI-initiated <c>memory.write</c> handler and <see cref="ComposeMemoryCapture"/> write through.
/// </summary>
/// <remarks>
/// <para>
/// <b>Per-user ONLY (ADR-042 / ADR-039).</b> The item is written at <see cref="MemoryScope.User"/> keyed by
/// the resolved <c>systemuserid</c> — the SAME canonical key chat-side user memory uses (so the preference
/// recalls via task 030's <c>ToUserPromptFragmentAsync</c> and feeds task 032's producer). This facade NEVER
/// writes Record/global scope, never mutates the ADR-039 capability catalog, and never touches any state
/// beyond the calling user's own memory partition. When the oid cannot be resolved to a canonical user the
/// capture is SKIPPED — a preference is never guessed onto a wrong (or the raw-oid) partition.
/// </para>
/// <para>
/// <b>Provenance envelope is METADATA (ADR-042 FR-B-08).</b> <c>Source</c> (user vs ai-derived) / <c>SessionId</c>
/// describe the capture; they do NOT gate it. <c>TrustLevel</c> is written NULL — the untrusted-origin gate /
/// trustLevel enforcement / memory-poisoning evals are DEFERRED to the security project (#616); carried inert,
/// never acted on here.
/// </para>
/// <para>
/// <b>Best-effort:</b> only an honoured <see cref="OperationCanceledException"/> (on request cancellation)
/// propagates. Any other store failure degrades to <see cref="PreferenceMemoryCaptureOutcome.Failed(string)"/>
/// — never thrown — so the caller's feedback submission is never blocked or failed by preference capture.
/// A store <see cref="ArgumentException"/> (invalid keying) is treated as a Skipped no-op for the same reason.
/// </para>
/// <para><b>Lifetime:</b> Scoped — registered alongside <see cref="IMemoryItemStore"/> in
/// <c>AiPersistenceModule</c>; the resolver it depends on is a Singleton.</para>
/// </remarks>
public sealed class PreferenceMemoryCapture : IPreferenceMemoryCapture
{
    /// <summary>Max stored length of the preference key/value directive text (defensive cap on free-text feedback).</summary>
    internal const int MaxDirectiveLength = 500;

    /// <summary>
    /// Confidence written for an UNCONFIRMED (ai-derived, inferred) preference. Deliberately BELOW the
    /// recall gate (<c>MemoryItemStore.MinUnconfirmedConfidence</c> = 0.7) so an inferred preference is a
    /// DORMANT candidate — persisted + supersession-keyed + visible to the FR-B-03 review surface and the
    /// task-032 producer, but NOT folded into the user prompt fragment until the user CONFIRMS it (ADR-042:
    /// unconfirmed ai-derived facts must not bias behavior until acknowledged — see MemoryFact.ConfirmedByUser).
    /// A confirmed (explicit-directive) preference recalls regardless of confidence via the ConfirmedByUser gate.
    /// </summary>
    internal const double UnconfirmedCandidateConfidence = 0.5;

    private readonly IMemoryItemStore _store;
    private readonly ISystemUserIdentityResolver _identityResolver;
    private readonly ILogger<PreferenceMemoryCapture> _logger;

    public PreferenceMemoryCapture(
        IMemoryItemStore store,
        ISystemUserIdentityResolver identityResolver,
        ILogger<PreferenceMemoryCapture> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PreferenceMemoryCaptureOutcome> CapturePreferenceAsync(
        PreferenceCaptureInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.TenantId))
        {
            return PreferenceMemoryCaptureOutcome.Skipped("no tenant id");
        }

        var key = Trim(input.Key);
        var value = Trim(input.Value);
        if (key.Length == 0 || value.Length == 0)
        {
            return PreferenceMemoryCaptureOutcome.Skipped("empty preference directive");
        }

        // Only two provenance origins are valid for a preference: an explicit user directive, or an
        // ai-derived inference. Anything else is a caller error — normalise to ai-derived (the safer,
        // unconfirmed posture) rather than persisting an unknown provenance value.
        var origin = string.Equals(input.Origin, MemoryOrigin.User, StringComparison.Ordinal)
            ? MemoryOrigin.User
            : MemoryOrigin.AiDerived;

        try
        {
            ct.ThrowIfCancellationRequested();

            // Canonical per-user keying (ADR-042 MUST: User memory keyed by systemuserid). Resolve the AAD
            // oid to the Dataverse systemuserid so the item recalls under the same key chat-side memory uses.
            var systemUserId = await _identityResolver
                .ResolveSystemUserIdAsync(input.UserAadObjectId, ct)
                .ConfigureAwait(false);

            if (systemUserId is not { } resolved || resolved == Guid.Empty)
            {
                // No canonical user → never guess a partition. Skip (best-effort no-op).
                _logger.LogInformation(
                    "PreferenceMemoryCapture: skipped — could not resolve a canonical systemuserid for the caller (origin={Origin}).",
                    origin);
                return PreferenceMemoryCaptureOutcome.Skipped("unresolvable user identity");
            }

            var now = DateTimeOffset.UtcNow;
            var userId = resolved.ToString("D");

            var item = new MemoryItem
            {
                Version = MemoryItemContract.SchemaVersion,
                Scope = MemoryScope.User,
                // User scope → subject is the authenticated principal; Record keying stays null.
                UserId = userId,
                Fact = new MemoryFact
                {
                    Type = MemoryFactType.Preference,
                    Key = key,
                    Value = value,
                    Source = origin,
                    ConfirmedByUser = input.ConfirmedByUser,
                    // Confirmed directive → 1.0 (recalls via the ConfirmedByUser gate anyway); unconfirmed
                    // inference → below the recall gate so it stays a dormant candidate until acknowledged.
                    Confidence = input.ConfirmedByUser ? 1.0 : UnconfirmedCandidateConfidence,
                    RecordedAt = now,
                },
                // ── Provenance envelope (METADATA, not a gate — ADR-042 FR-B-08) ──
                Source = origin,
                SessionId = input.SessionId,
                // TrustLevel DELIBERATELY null: carried inert — enforcement DEFERRED to the security
                // project (#616). Setting/reading it here would imply enforcement.
                TrustLevel = null,
                CreatedBy = userId,
                CreatedAt = now,
                DeletionPolicy = "user-erasable",
                RetentionClass = "tier-3-user-owned",
            };

            var persisted = await _store.UpsertAsync(item, input.TenantId, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "PreferenceMemoryCapture: persisted User-scope preference itemId={ItemId} origin={Origin} confirmed={Confirmed}.",
                persisted.Id, origin, input.ConfirmedByUser);

            return PreferenceMemoryCaptureOutcome.Captured(persisted.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            // Store rejected the keying (should not happen for a well-formed User-scope item). Treat as a
            // Skipped no-op — never fail the caller's feedback submission.
            _logger.LogWarning(
                "PreferenceMemoryCapture: store rejected the preference item ({ErrorType}) — skipped.",
                ex.GetType().Name);
            return PreferenceMemoryCaptureOutcome.Skipped("store rejected preference item");
        }
        catch (Exception ex)
        {
            // Best-effort: never propagate. The caller's feedback write has already succeeded on its own terms.
            // A CosmosException (e.g. the R-5 ttl:null → 400 write outage) lands here — surface it as the
            // alertable cosmos.write_failures{container=memory-items} metric so a persistent memory-write
            // stoppage is detectable, not just a swallowed Warning.
            Sprk.Bff.Api.Telemetry.CosmosPersistenceTelemetry.RecordWriteFailure("memory-items");
            _logger.LogWarning(ex,
                "PreferenceMemoryCapture: threw while capturing a preference — best-effort, caller unaffected.");
            return PreferenceMemoryCaptureOutcome.Failed(ex.GetType().Name);
        }
    }

    /// <summary>Trims and caps free-text directive fields; collapses null/blank to empty.</summary>
    private static string Trim(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var trimmed = s.Trim();
        return trimmed.Length > MaxDirectiveLength ? trimmed[..MaxDirectiveLength] : trimmed;
    }
}
