using System.Text.Json;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// The universal output seam (ADR-040 / FR-P1-02, spaarke-ai-architecture-redesign-r1
/// task 021): every capability execution passes its structured output through
/// <see cref="RouteAsync"/>, which (1) writes an addressable <see cref="SessionOutput"/>
/// ledger entry keyed <c>{bindingId}@t{n}</c> to the session store BEFORE any surface
/// renders, then (2) routes rendering by the Binding's declared
/// <see cref="Binding.Disposition"/> — the ONLY rendering contract.
/// </summary>
/// <remarks>
/// <para>
/// <b>Interface rationale (ADR-010 tension, documented)</b>: ADR-010 prefers concrete
/// registrations, but this seam is a genuine module boundary — P1 tasks 022/023/024 and
/// every P2/P3 executor write through it, P3 added the email (task 043) and work_product
/// (task 047) disposition legs with overlay/record/notification still to land, and callers
/// mock it at the module boundary per ADR-038. That is "multiple consumers + evolving
/// implementations", not interface-for-testability-alone.
/// </para>
/// <para>
/// <b>ADR-039</b>: the disposition consumed here comes exclusively from the resolved
/// <see cref="Binding"/> row (<c>sprk_disposition</c>). This type introduces NO routing
/// config of its own — no appsettings key, no code list, no second intent mechanism.
/// </para>
/// </remarks>
public interface IOutputRouter
{
    /// <summary>
    /// Writes the execution output to the session ledger (storage precedes rendering —
    /// ADR-040 D2/D8), then routes by the Binding's declared disposition.
    /// </summary>
    /// <param name="session">The chat session the execution ran in (ledger carrier).</param>
    /// <param name="binding">
    /// The resolved Binding that produced the output — supplies the ledger key identity
    /// (<see cref="Binding.BindingId"/>), the use-case id, and the disposition.
    /// </param>
    /// <param name="output">The full structured, schema-validated execution output.</param>
    /// <param name="sourceRefs">
    /// Citations / document ids / ledger keys the output was grounded on (identifiers only).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The stored ledger entry + updated session. For the <c>informational</c> disposition
    /// the caller renders FROM the returned <see cref="RoutedOutput.Entry"/> (render follows
    /// store). The <c>email</c> disposition (FR-P3-04) delivers via
    /// <see cref="IEmailDispositionSender"/> AFTER the ledger write (the payload must carry
    /// the capability-supplied <c>email</c> envelope), then returns the stored entry.
    /// The <c>work_product</c> disposition (FR-P3-08) persists the stored entry's envelope
    /// to the session's host Dataverse record via <see cref="IWorkProductRecordPersister"/>
    /// AFTER the ledger write, then returns the stored entry (callers may still render it —
    /// storage, persistence, and rendering are independent contracts).
    /// Dispositions the ADR-043 §3 <see cref="DispositionRoutability"/> registry marks
    /// not-yet-routable (overlay/record/notification) throw <see cref="NotSupportedException"/>
    /// AFTER the ledger write (a loud, registry-single-sourced stub — never a silent fallback to
    /// inline render). The dispatch admit-gate rejects those same dispositions PRE-RUN from the
    /// same registry, so admission and routing cannot disagree.
    /// </returns>
    Task<RoutedOutput> RouteAsync(
        ChatSession session,
        Binding binding,
        JsonElement output,
        IReadOnlyList<string>? sourceRefs = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of <see cref="IOutputRouter.RouteAsync"/>: the ledger entry that was stored
/// (addressable by <see cref="SessionOutput.Key"/>) and the updated session carrying it.
/// </summary>
public sealed record RoutedOutput
{
    /// <summary>The stored, addressable ledger entry. Informational renderers read <see cref="SessionOutput.Payload"/> from HERE — never from pre-store state.</summary>
    public required SessionOutput Entry { get; init; }

    /// <summary>The session with <see cref="ChatSession.Outputs"/> including <see cref="Entry"/> (the instance that was persisted).</summary>
    public required ChatSession Session { get; init; }

    /// <summary>
    /// The Completion Engine's <see cref="OutcomeCard"/> for this stored outcome (task 035 /
    /// FR-A1-06). Composed AFTER the ledger write (store-before-render — ADR-040) by
    /// <see cref="CompletionEngine.ComposeForRoutedOutput"/>, so EVERY completing route on the
    /// auto-executed + event paths yields an OutcomeCard (NFR-09 coverage). The card rides this
    /// existing disposition surface — no second rendering path is introduced.
    /// </summary>
    public OutcomeCard? Outcome { get; init; }
}

/// <summary>
/// Default <see cref="IOutputRouter"/>: persists through <see cref="ChatSessionManager"/>
/// (Redis hot write awaited to completion + Cosmos warm write-through enqueued — the
/// existing 3-tier pipeline; the ledger changes WHAT persists, not WHERE).
/// </summary>
/// <remarks>
/// <para>
/// <b>Turn numbering (documented decision, task 021)</b>: linear (non-loop) executions have
/// no first-class session turn counter yet, so <c>t{n}</c> is allocated as a monotonic
/// per-session output ordinal: <c>max(existing Outputs[].Turn, 0) + 1</c>. This keeps every
/// key unique within the session, makes sequential executions of the same Binding increment
/// <c>t{n}</c>, and is forward-compatible with the P2 agent loop supplying a real
/// conversation-turn number (a loop turn that appends multiple outputs will simply allocate
/// consecutive ordinals — addressability, the contract that matters, is unaffected).
/// </para>
/// <para>
/// <b>NFR-07</b>: log statements carry identifiers only (key, bindingId, ucid, disposition,
/// turn, tenant, session, payload SIZE) — never payload content.
/// </para>
/// <para>
/// <b>Inline payload size cap (ADR-040 — ENFORCED)</b>: payloads exceeding
/// <see cref="SessionLedger.InlinePayloadCapBytes"/> are deterministically replaced by a
/// truncation marker (<see cref="SessionLedger.CapInlinePayload"/>) BEFORE the ledger
/// write, plus a Warning log (sizes only — NFR-07). Promoted from the task-021 warn-only
/// threshold by task 055 per the operator ruling of 2026-07-07 (deferred from tasks
/// 021/047). Consequence for structured dispositions: a truncated payload no longer
/// carries the <c>email</c> / work_product envelope, so those legs fail LOUDLY on the
/// stored marker — deterministic, never a silent partial delivery. Capabilities MUST
/// keep routed payloads under the cap. The blob/SPE-pointer offload remains the designed
/// upgrade path (marker becomes pointer; content stops being lossy).
/// </para>
/// </remarks>
public sealed class OutputRouter : IOutputRouter
{
    private readonly ChatSessionManager _sessionManager;
    private readonly ILogger<OutputRouter> _logger;
    private readonly IEmailDispositionSender? _emailSender;
    private readonly IWorkProductRecordPersister? _workProductPersister;

    /// <param name="sessionManager">Session persistence seam (the ledger write path).</param>
    /// <param name="logger">Logger (identifiers only per NFR-07).</param>
    /// <param name="emailSender">
    /// FR-P3-04 email disposition delivery seam. Optional with a null default so DI (which
    /// registers the sender in the same compound-AI-ON block) injects it while existing
    /// constructions stay valid; when null, the email leg fails LOUDLY at routing time —
    /// never a silent skip.
    /// </param>
    /// <param name="workProductPersister">
    /// FR-P3-08 work_product disposition persistence seam (same optional-with-loud-failure
    /// shape as <paramref name="emailSender"/>): when null, the work_product leg fails
    /// LOUDLY at routing time — never a silent skip.
    /// </param>
    public OutputRouter(
        ChatSessionManager sessionManager,
        ILogger<OutputRouter> logger,
        IEmailDispositionSender? emailSender = null,
        IWorkProductRecordPersister? workProductPersister = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _emailSender = emailSender;
        _workProductPersister = workProductPersister;
    }

    /// <inheritdoc />
    public async Task<RoutedOutput> RouteAsync(
        ChatSession session,
        Binding binding,
        JsonElement output,
        IReadOnlyList<string>? sourceRefs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(binding);

        // ── 1. Allocate the addressable key {bindingId}@t{n} (ADR-040) ──────────────────
        var turn = (session.Outputs is { Count: > 0 } ? session.Outputs.Max(o => o.Turn) : 0) + 1;
        var bindingId = binding.BindingId.ToString();

        // ADR-040 inline size-cap ENFORCEMENT (task 055, operator ruling 2026-07-07):
        // over-cap payloads are deterministically replaced by a truncation marker BEFORE
        // the ledger write. Clone detaches from any caller-owned JsonDocument lifetime.
        var capped = SessionLedger.CapInlinePayload(output.Clone());
        var payloadBytes = capped.OriginalBytes;

        var entry = new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey(bindingId, turn),
            BindingId = bindingId,
            // Legacy Binding rows carry a null sprk_ucid; the consumer-type code is the
            // stable fallback vocabulary id (it IS the Binding's use-case discriminator).
            UcId = binding.Ucid ?? binding.ConsumerType,
            Turn = turn,
            Disposition = binding.Disposition.ToLedgerValue(),
            Payload = capped.Payload,
            SourceRefs = sourceRefs is { Count: > 0 } ? sourceRefs : null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (capped.Truncated)
        {
            // NFR-07: sizes + identifiers only — never payload content.
            _logger.LogWarning(
                "Ledger output {Key} inline payload was {PayloadBytes} bytes (> cap {CapBytes}) — " +
                "stored as a truncation marker (ADR-040 inline size-cap enforcement, task 055). " +
                "Blob/SPE pointer offload is the designed upgrade path.",
                entry.Key, payloadBytes, SessionLedger.InlinePayloadCapBytes);
        }

        // ── 2. STORE — the universal ledger write, BEFORE any rendering (ADR-040 D2/D8).
        // Append-only: a new SessionOutput is appended; existing entries are never mutated.
        // UpdateSessionCacheAsync awaits the Redis hot write to completion and synchronously
        // enqueues the Cosmos warm write-through (the existing D-06 fire-and-forget) — the
        // entry is durably in the session store before this method returns.
        var appended = new List<SessionOutput>(session.Outputs ?? Array.Empty<SessionOutput>()) { entry };
        var updated = session with { Outputs = appended };
        await _sessionManager.UpdateSessionCacheAsync(updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Ledger output stored: key={Key} ucid={Ucid} disposition={Disposition} turn={Turn} " +
            "payloadBytes={PayloadBytes} tenant={TenantId} session={SessionId}",
            entry.Key, entry.UcId, entry.Disposition, entry.Turn,
            payloadBytes, session.TenantId, session.SessionId);

        // ── 2b. COMPLETION ENGINE (task 035 / FR-A1-06): compose the OutcomeCard for this
        // stored outcome AFTER the ledger write (store-before-render — ADR-040) and BEFORE any
        // disposition leg returns. Every completing route on the auto-executed + event paths
        // therefore yields an OutcomeCard (NFR-09), riding this disposition surface with no
        // second rendering path. The card's next-step chips come from the Binding's DECLARED
        // transitions (catalog data); the trace ref defaults to the ledger key.
        //
        // Task 036 live-wiring (audit F-3): when the STORED payload embeds a JobAwareCompletionState
        // (task 014 v1) under the reserved CompletionEngine.JobAwareCompletionStateField, the composer
        // routes through ComposeJobAware so the OutcomeStatus is DERIVED from the job aggregate — a
        // record whose downstream indexing/analysis is still pending renders Partial, never Succeeded
        // (NFR-12 ingestion parity). A single-shot (non-job) payload is byte/behavior-identical to the
        // legacy hardcoded-Succeeded composer.
        var outcome = CompletionEngine.ComposeForRoutedOutput(entry, binding);

        // ── 3. ROUTE by disposition — the ONLY rendering contract (ADR-040 / ADR-039).
        // No branching by capability name, consumer type, or any second routing surface.
        //
        // ADR-043 §3 single-source: a disposition the ONE registry marks NOT routable is a LOUD
        // rejection AFTER the store (never a silent inline render). The dispatch admit-gate rejects
        // these PRE-RUN from the SAME registry (DispositionRoutability.IsAdmissible); this is the
        // post-store backstop for any caller that reaches the router directly (e.g. the event path)
        // with a not-yet-routable disposition. The message is single-sourced in the registry so the
        // admit-gate and the router cannot describe the same gap two different ways.
        if (!DispositionRoutability.IsRoutable(binding.Disposition))
        {
            throw new NotSupportedException(
                DispositionRoutability.NotRoutableMessage(binding.Disposition, entry.Key));
        }

        switch (binding.Disposition)
        {
            // Informational: pass-through — the caller renders from the STORED entry on its
            // existing SSE path (render follows store).
            case BindingDisposition.Informational:
                return new RoutedOutput { Entry = entry, Session = updated, Outcome = outcome };

            // Compose (ComposeDisposition v1 seam — spaarkeai-compose-r2, production routing
            // promotion of the task-010 contract): draft-into-editor. Pass-through like
            // Informational — the compose SessionOutput is STORED above (store-before-render,
            // ADR-040) carrying the Compose-owned structured-edit payload, and the client
            // re-materializes it from the ledger (the compose-outputs read endpoint + the
            // ComposeDisposition SSE frame's ledger_ref — ADR-039, no second dispatch path). The
            // router stores + returns; it NEVER parses the opaque payload (Compose owns it).
            // Returns the Completion Engine OutcomeCard (Outcome = outcome) exactly like the
            // Informational case — the OutcomeCard rides this same disposition surface (task 035 /
            // NFR-09 completion coverage; redesign-r2 reconciliation 2026-07-09). Omitting it would
            // silently drop compose outputs' OutcomeCard.
            case BindingDisposition.Compose:
                return new RoutedOutput { Entry = entry, Session = updated, Outcome = outcome };

            // SurfaceLaunch (assistant-enhancements-r1 — the create-flow fix): pre-seeded surface launch.
            // Pass-through like Compose/Informational — the surface_launch SessionOutput is STORED above
            // (store-before-render, ADR-040) carrying the capability's drafted launch payload, and the
            // CLIENT re-materializes it into a pre-seeded wizard / OOB-form / workspace-tab launch. The
            // leg performs NO server side-effect: "hand the user the pre-seeded UI," not "the server
            // writes the record" — the client owns the hand-off id + sessionStorage rendezvous +
            // target-surface mapping (task 012). The router stores + returns; it NEVER parses the opaque
            // launch payload (the client owns it). Returns the Completion Engine OutcomeCard exactly like
            // the Informational/Compose cases (Outcome = outcome) — the card rides this same disposition
            // surface (task 035 / NFR-09); omitting it would silently drop surface-launch outputs' card.
            case BindingDisposition.SurfaceLaunch:
                return new RoutedOutput { Entry = entry, Session = updated, Outcome = outcome };

            // Email (FR-P3-04, task 043): deliver via the Communication (Email) service.
            // The capability supplies presentation IN the stored payload (`email` object:
            // to[] / subject / htmlBody — see IEmailDispositionSender remarks); the router
            // supplies storage-then-delivery. Storage already happened above (ADR-040
            // store-precedes-render); a delivery failure propagates AFTER the ledger write —
            // the entry stays addressable, the invocation fails loudly.
            case BindingDisposition.Email:
                await DeliverEmailAsync(entry, cancellationToken).ConfigureAwait(false);
                return new RoutedOutput { Entry = entry, Session = updated, Outcome = outcome };

            // Work product (FR-P3-08, task 047): persist the STORED entry's envelope to the
            // session's host Dataverse record via the topic-registry target mapping
            // (widgets-r1 pattern, generalized). Storage already happened above (ADR-040
            // store-precedes-persistence); a persistence failure propagates AFTER the
            // ledger write — the entry stays addressable, the invocation fails loudly.
            case BindingDisposition.WorkProduct:
                await PersistWorkProductAsync(entry, binding, session, cancellationToken).ConfigureAwait(false);
                return new RoutedOutput { Entry = entry, Session = updated, Outcome = outcome };

            // Not-yet-routable dispositions (overlay/record/notification) are rejected by the
            // registry check ABOVE the switch — they never reach here. This default is the
            // registry/router-drift guard: a disposition the registry marks Routable=true but for
            // which no leg above matched (e.g. a new routable disposition registered without its
            // OutputRouter leg). Fail loudly rather than silently drop — the seam tests
            // (tests/integration/seam/**) also assert admit ⇔ route ⇔ store per disposition.
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(binding),
                    binding.Disposition,
                    $"DispositionRoutability marks '{binding.Disposition.ToLedgerValue()}' routable, but OutputRouter " +
                    "has no routing leg for it — registry/router drift (ADR-043 §3). Add the leg here alongside its " +
                    "registry entry. The output WAS stored to the ledger (ADR-040); only the routing leg is missing.");
        }
    }

    /// <summary>
    /// FR-P3-04 email leg: parse the capability-supplied <c>email</c> envelope from the
    /// STORED payload and deliver via <see cref="IEmailDispositionSender"/>. Loud on every
    /// failure mode (missing sender, missing/malformed envelope) — never a silent skip.
    /// </summary>
    private async Task DeliverEmailAsync(SessionOutput entry, CancellationToken cancellationToken)
    {
        if (_emailSender is null)
        {
            throw new InvalidOperationException(
                $"OutputRouter: output '{entry.Key}' declares the email disposition but no IEmailDispositionSender " +
                "is registered. The entry WAS stored (ADR-040); delivery is unconfigured — register " +
                "CommunicationEmailDispositionSender in the compound-AI-ON block.");
        }

        if (!entry.Payload.TryGetProperty("email", out var emailElement)
            || emailElement.ValueKind != JsonValueKind.Object
            || !emailElement.TryGetProperty("subject", out var subjectEl) || subjectEl.ValueKind != JsonValueKind.String
            || !emailElement.TryGetProperty("htmlBody", out var bodyEl) || bodyEl.ValueKind != JsonValueKind.String
            || !emailElement.TryGetProperty("to", out var toEl) || toEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"OutputRouter: output '{entry.Key}' declares the email disposition but its payload carries no " +
                "valid 'email' envelope ({ to: string[], subject: string, htmlBody: string }). The capability " +
                "supplies presentation; the router supplies delivery — fix the capability's routed payload.");
        }

        var recipients = toEl.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        await _emailSender.SendAsync(
            new EmailDispositionEnvelope
            {
                To = recipients,
                Subject = subjectEl.GetString()!,
                HtmlBody = bodyEl.GetString()!,
                CorrelationId = entry.Key,
            },
            cancellationToken).ConfigureAwait(false);

        // NFR-07: identifiers only.
        _logger.LogInformation(
            "Ledger output {Key} routed to email disposition: recipients={RecipientCount}",
            entry.Key, recipients.Count);
    }

    /// <summary>
    /// FR-P3-08 work_product leg: persist the STORED entry's envelope to the session's
    /// host Dataverse record via <see cref="IWorkProductRecordPersister"/> (topic-registry
    /// target mapping; user-OBO write). Loud on every failure mode (missing persister,
    /// missing/invalid host context, persistence failure) — never a silent skip.
    /// </summary>
    private async Task PersistWorkProductAsync(
        SessionOutput entry,
        Binding binding,
        ChatSession session,
        CancellationToken cancellationToken)
    {
        if (_workProductPersister is null)
        {
            throw new InvalidOperationException(
                $"OutputRouter: output '{entry.Key}' declares the work_product disposition but no " +
                "IWorkProductRecordPersister is registered. The entry WAS stored (ADR-040); persistence is " +
                "unconfigured — register TopicRegistryWorkProductPersister in the compound-AI-ON block.");
        }

        if (session.HostContext is null || !session.HostContext.IsValid())
        {
            throw new InvalidOperationException(
                $"OutputRouter: output '{entry.Key}' declares the work_product disposition but the session " +
                "carries no valid host record context (HostContext). Work-product envelopes persist to the " +
                "session's HOST RECORD — invoke this capability from a record-hosted surface (the session " +
                "must be created with a HostContext), or change the Binding's disposition. The entry WAS " +
                "stored to the session ledger (ADR-040).");
        }

        var receipt = await _workProductPersister
            .PersistAsync(entry, binding, session.HostContext, cancellationToken)
            .ConfigureAwait(false);

        // NFR-07: identifiers only.
        _logger.LogInformation(
            "Ledger output {Key} routed to work_product disposition: entity={Entity} recordId={RecordId} targetField={TargetField}",
            receipt.LedgerKey, receipt.EntityLogicalName, receipt.RecordId, receipt.TargetField);
    }
}

/// <summary>
/// Maps <see cref="BindingDisposition"/> (raw <c>sprk_disposition</c> option-set values)
/// to the ledger wire vocabulary on <see cref="SessionOutput.Disposition"/>
/// (<c>informational | work_product | overlay | email | record | notification | compose | surface_launch</c> —
/// canonical §6.2 / ADR-040).
/// </summary>
/// <remarks>
/// <b>ADR-043 §3 single-source</b>: this is a thin delegate to <see cref="DispositionRoutability"/>,
/// the ONE disposition source of truth. The mapping table lives in the registry alongside routability,
/// so the ledger vocabulary and the admit-gate can never disagree (they read the same table).
/// </remarks>
internal static class BindingDispositionLedgerExtensions
{
    internal static string ToLedgerValue(this BindingDisposition disposition) =>
        DispositionRoutability.ToLedgerValue(disposition);
}
