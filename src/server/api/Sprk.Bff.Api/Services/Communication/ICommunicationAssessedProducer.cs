using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The comms Responsive-Intelligence <c>communication_assessed</c> signal (spec FR-11). Carries the
/// minimal fields task 041's policy layer needs to evaluate tenant/matter rules — exactly the fields
/// <c>CommunicationEnrichmentService</c> step 5 logged before this producer existed
/// (CommunicationId, Direction, Subject, From, RecipientCount). IDs + minimal metadata only — no body,
/// no privileged content.
/// </summary>
public sealed record CommunicationAssessedSignal(
    Guid CommunicationId,
    CommunicationDirection Direction,
    string? Subject,
    string? From,
    int RecipientCount,
    // Assessment confidence [0–1] the comms policy gate (task 041) compares against a rule threshold. Additive
    // and defaults to 0 (conservative — DENY under any positive threshold) because the enrichment pipeline does
    // not yet compute an RI-confidence score; plumbing a real assessment confidence is a downstream concern
    // (see notes/041-rule-store-decision.md "confidence source"). Existing 5-arg constructions stay valid.
    double Confidence = 0);

/// <summary>
/// Producer seam for the <c>communication_assessed</c> signal emitted at enrichment step 5
/// (<see cref="CommunicationEnrichmentService"/>, spec FR-11). A GENUINE seam (ADR-010): the interim
/// default (<see cref="LoggingCommunicationAssessedProducer"/>) is log-only; task 041 registers the real
/// comms-policy-gate consumer behind THIS SAME interface with NO change to the emit point, and task 042
/// (downstream of the 041 gate) is what actually writes the <c>kind=communication-assessed</c> outbox
/// row + mirrors to <c>appnotification</c>.
/// </summary>
/// <remarks>
/// Deliberately NOT <c>IEventRulesService</c>: that seam is chat-session/SSE-shaped
/// (<c>SurfaceEventRequest{SessionId,UserOid,FileIds}</c> → <c>IAsyncEnumerable&lt;ChatSseEvent&gt;</c>)
/// and does not fit a fire-and-forget assessment emission (the original "ESCALATION E5" rationale, which
/// still holds). The emit point (enrichment step 5) already isolates exceptions (NFR-05); implementations
/// should nonetheless avoid throwing.
/// </remarks>
public interface ICommunicationAssessedProducer
{
    /// <summary>Publishes the <paramref name="signal"/>. Safe to call fire-and-forget.</summary>
    Task PublishAsync(CommunicationAssessedSignal signal, CancellationToken ct = default);
}

/// <summary>
/// Interim safe-default (ADR-032) implementation of <see cref="ICommunicationAssessedProducer"/> —
/// log-only, with NO downstream side effect (no Layer-B outbox write, no <c>IEventRulesService.FireAsync</c>
/// — both explicitly out of scope for FR-11). Preserves the exact pre-producer behavior (the structured
/// <c>communication_assessed</c> log line) so the application behaves identically BEFORE and AFTER task 041
/// registers the real policy-gate consumer behind this seam. Registered UNCONDITIONALLY in
/// <c>CommunicationModule</c> so DI resolution always succeeds.
/// </summary>
public sealed class LoggingCommunicationAssessedProducer : ICommunicationAssessedProducer
{
    private readonly ILogger<LoggingCommunicationAssessedProducer> _logger;

    public LoggingCommunicationAssessedProducer(ILogger<LoggingCommunicationAssessedProducer> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public Task PublishAsync(CommunicationAssessedSignal signal, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "communication_assessed | CommunicationId: {CommunicationId}, Direction: {Direction}, Subject: '{Subject}', " +
            "From: {From}, RecipientCount: {RecipientCount}. Interim log-only producer (FR-11); task 041 registers the " +
            "real policy-gate consumer behind ICommunicationAssessedProducer.",
            signal.CommunicationId, signal.Direction, signal.Subject, signal.From, signal.RecipientCount);
        return Task.CompletedTask;
    }
}
