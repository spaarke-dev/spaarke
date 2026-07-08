// FR-P3-04 (spaarke-ai-architecture-redesign-r1 task 043) — the email disposition leg's
// delivery seam. OutputRouter routes `email`-disposition outputs (ADR-040: disposition is
// the ONLY rendering contract) through this narrow sender instead of depending on the
// Communication module's sealed 11-dependency CommunicationService directly.

using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Delivery seam for the <c>email</c> output disposition (ADR-040 / FR-P3-04):
/// <see cref="OutputRouter"/> stores the ledger entry FIRST, then hands the
/// capability-supplied email envelope here for delivery via Spaarke's
/// Communication (Email) service.
/// </summary>
/// <remarks>
/// <para>
/// <b>Interface rationale (ADR-010 tension, documented — CLAUDE.md §11)</b>:
/// (1) Existing overlap: <see cref="CommunicationService"/> IS the email service — this
/// type adds no second send path; it adapts one call. (2) Extension test: injecting the
/// sealed, 11-dependency <c>CommunicationService</c> concrete into <see cref="OutputRouter"/>
/// directly would make the router's email leg untestable (ADR-038 module-boundary mocking
/// is impossible on a sealed concrete) and would couple the AI module to the Communication
/// module's full graph. (3) Cost of doing nothing: the ADR-040 email disposition stays a
/// loud <c>NotSupportedException</c> stub and the Daily Briefing email leg (UC-D-1,
/// FR-P3-04 acceptance "briefing email arrives") cannot ship.
/// </para>
/// <para>
/// <b>Envelope contract</b>: capabilities whose Binding declares
/// <c>disposition = email</c> MUST include the presentation in their routed output payload
/// (an <c>email</c> object: <c>to[]</c>, <c>subject</c>, <c>htmlBody</c>). The router
/// supplies storage + delivery; the capability supplies presentation. This keeps the
/// router generic — it never knows how a briefing (or any future email capability)
/// renders itself.
/// </para>
/// <para>
/// <b>Send mode</b>: delivered as the acting user via OBO (<see cref="SendMode.User"/> —
/// Graph <c>me/sendMail</c>), matching the P3 trigger shape (the briefing email leg is
/// HTTP-invoked under the caller's identity; UC-D-1 scheduled app-only delivery is a
/// later catalog addition). No HttpContext → loud failure, never a silent skip.
/// </para>
/// </remarks>
public interface IEmailDispositionSender
{
    /// <summary>Delivers one email-disposition output. Throws on failure — the caller treats delivery as part of routing (loud, never silent).</summary>
    Task SendAsync(EmailDispositionEnvelope envelope, CancellationToken cancellationToken = default);
}

/// <summary>
/// The capability-supplied email presentation for an <c>email</c>-disposition output.
/// Parsed by <see cref="OutputRouter"/> from the routed payload's <c>email</c> object.
/// </summary>
public sealed record EmailDispositionEnvelope
{
    /// <summary>Recipient addresses (at least one).</summary>
    public required IReadOnlyList<string> To { get; init; }

    /// <summary>Email subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>HTML body rendered by the capability.</summary>
    public required string HtmlBody { get; init; }

    /// <summary>Correlation id for tracking (the ledger entry key by convention).</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Default <see cref="IEmailDispositionSender"/>: delegates to
/// <see cref="CommunicationService.SendAsync"/> with <see cref="SendMode.User"/> (OBO —
/// sends as the authenticated caller; creates the <c>sprk_communication</c> tracking
/// record as a side effect of the Communication service's own contract).
/// </summary>
public sealed class CommunicationEmailDispositionSender : IEmailDispositionSender
{
    private readonly CommunicationService _communication;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<CommunicationEmailDispositionSender> _logger;

    public CommunicationEmailDispositionSender(
        CommunicationService communication,
        IHttpContextAccessor httpContextAccessor,
        ILogger<CommunicationEmailDispositionSender> logger)
    {
        _communication = communication ?? throw new ArgumentNullException(nameof(communication));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task SendAsync(EmailDispositionEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.To.Count == 0)
        {
            throw new InvalidOperationException(
                "Email-disposition envelope has no recipients — the capability must supply email.to[].");
        }

        var httpContext = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException(
                "Email-disposition delivery requires an authenticated HttpContext (SendMode.User OBO). " +
                "Scheduled app-only delivery is not implemented in this phase — do not swallow this; " +
                "the invocation path must be HTTP-triggered.");

        var response = await _communication.SendAsync(
            new SendCommunicationRequest
            {
                To = envelope.To.ToArray(),
                Subject = envelope.Subject,
                Body = envelope.HtmlBody,
                BodyFormat = BodyFormat.HTML,
                SendMode = SendMode.User,
                CorrelationId = envelope.CorrelationId,
            },
            httpContext,
            cancellationToken).ConfigureAwait(false);

        // NFR-07: identifiers only — never subject/body content.
        _logger.LogInformation(
            "Email-disposition output delivered: correlationId={CorrelationId} recipients={RecipientCount} communicationId={CommunicationId}",
            envelope.CorrelationId, envelope.To.Count, response?.CommunicationId);
    }
}
