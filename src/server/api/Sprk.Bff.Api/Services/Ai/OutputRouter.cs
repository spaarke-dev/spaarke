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
    /// Remaining dispositions throw <see cref="NotSupportedException"/> AFTER the ledger
    /// write (loud P3 stubs — never a silent fallback to inline render).
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
/// <b>Inline payload size cap (ADR-040)</b>: payloads exceeding
/// <see cref="InlinePayloadWarnBytes"/> log a Warning (size only) so oversized entries are
/// observable. The blob/SPE-pointer OFFLOAD for over-cap inline payloads remains DEFERRED:
/// task 047 (FR-P3-08) landed the work_product leg, whose envelope persists to the host
/// Dataverse record (a durable out-of-session copy), but the inline ledger payload is not
/// yet pointer-swapped — the POML did not prescribe the offload and building an
/// unprescribed storage path would be scope creep (CLAUDE.md §11). Escalated at task 047
/// for an operator ruling on where the offload lands (P4 hardening / Track B).
/// </para>
/// </remarks>
public sealed class OutputRouter : IOutputRouter
{
    /// <summary>Observability threshold for inline ledger payloads (see class remarks).</summary>
    internal const int InlinePayloadWarnBytes = 128 * 1024;

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
        var entry = new SessionOutput
        {
            Key = SessionLedger.BuildOutputKey(bindingId, turn),
            BindingId = bindingId,
            // Legacy Binding rows carry a null sprk_ucid; the consumer-type code is the
            // stable fallback vocabulary id (it IS the Binding's use-case discriminator).
            UcId = binding.Ucid ?? binding.ConsumerType,
            Turn = turn,
            Disposition = binding.Disposition.ToLedgerValue(),
            // Clone detaches the payload from any caller-owned JsonDocument lifetime.
            Payload = output.Clone(),
            SourceRefs = sourceRefs is { Count: > 0 } ? sourceRefs : null,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var payloadBytes = entry.Payload.GetRawText().Length;
        if (payloadBytes > InlinePayloadWarnBytes)
        {
            _logger.LogWarning(
                "Ledger output {Key} inline payload is {PayloadBytes} bytes (> {WarnBytes}). " +
                "Blob/SPE pointer offload lands with the work_product leg (P3) — see ADR-040 size-cap rule.",
                entry.Key, payloadBytes, InlinePayloadWarnBytes);
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

        // ── 3. ROUTE by disposition — the ONLY rendering contract (ADR-040 / ADR-039).
        // No branching by capability name, consumer type, or any second routing surface.
        switch (binding.Disposition)
        {
            // Informational: pass-through — the caller renders from the STORED entry on its
            // existing SSE path (render follows store).
            case BindingDisposition.Informational:
                return new RoutedOutput { Entry = entry, Session = updated };

            // Email (FR-P3-04, task 043): deliver via the Communication (Email) service.
            // The capability supplies presentation IN the stored payload (`email` object:
            // to[] / subject / htmlBody — see IEmailDispositionSender remarks); the router
            // supplies storage-then-delivery. Storage already happened above (ADR-040
            // store-precedes-render); a delivery failure propagates AFTER the ledger write —
            // the entry stays addressable, the invocation fails loudly.
            case BindingDisposition.Email:
                await DeliverEmailAsync(entry, cancellationToken).ConfigureAwait(false);
                return new RoutedOutput { Entry = entry, Session = updated };

            // Work product (FR-P3-08, task 047): persist the STORED entry's envelope to the
            // session's host Dataverse record via the topic-registry target mapping
            // (widgets-r1 pattern, generalized). Storage already happened above (ADR-040
            // store-precedes-persistence); a persistence failure propagates AFTER the
            // ledger write — the entry stays addressable, the invocation fails loudly.
            case BindingDisposition.WorkProduct:
                await PersistWorkProductAsync(entry, binding, session, cancellationToken).ConfigureAwait(false);
                return new RoutedOutput { Entry = entry, Session = updated };

            // Remaining P3 dispositions: LOUD NotSupported stubs (task-021 contract — no
            // silent fallback to inline render). The ledger entry above IS stored and
            // addressable; only the rendering leg is missing until its task lands
            // (overlay/record/notification → later waves).
            case BindingDisposition.Overlay:
            case BindingDisposition.Record:
            case BindingDisposition.Notification:
                throw new NotSupportedException(
                    $"OutputRouter: disposition '{binding.Disposition.ToLedgerValue()}' routing is not implemented yet " +
                    $"(lands at phase P3 of spaarke-ai-architecture-redesign-r1). The output WAS stored to the session " +
                    $"ledger as '{entry.Key}' (storage precedes rendering — ADR-040); only the rendering leg is missing. " +
                    "Do NOT work around this by rendering inline — that would silently violate the disposition contract.");

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(binding),
                    binding.Disposition,
                    "Unknown BindingDisposition value — Binding contract / OutputRouter drift.");
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
/// (<c>informational | work_product | overlay | email | record | notification</c> —
/// canonical §6.2 / ADR-040).
/// </summary>
internal static class BindingDispositionLedgerExtensions
{
    internal static string ToLedgerValue(this BindingDisposition disposition) => disposition switch
    {
        BindingDisposition.Informational => "informational",
        BindingDisposition.WorkProduct => "work_product",
        BindingDisposition.Overlay => "overlay",
        BindingDisposition.Email => "email",
        BindingDisposition.Record => "record",
        BindingDisposition.Notification => "notification",
        _ => throw new ArgumentOutOfRangeException(nameof(disposition), disposition,
            "Unknown BindingDisposition value — Binding contract / ledger vocabulary drift."),
    };
}
