namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade (ADR-013 / BFF §10) for the lightweight email "sparkle" draft capability
/// (email-communication-solution-r5 Wave E). Given a small intent + the current email body/subject,
/// produces generated/refined body text for the compose surface.
/// </summary>
/// <remarks>
/// <para>
/// <b>Facade discipline (ADR-013 / §10 bullet 3).</b> The Communication surface
/// (<see cref="Sprk.Bff.Api.Api.CommunicationDraftEndpoints"/>) MUST consume AI text generation through
/// THIS facade — never by injecting the AI-internal <c>IChatClient</c>/<c>IOpenAiClient</c> directly into
/// communication code. Internally the real implementation wraps the SAME <c>IChatClient</c> the thin
/// <c>ChatEndpoints.RefineTextAsync</c> path already uses (single-shot, non-streaming).
/// </para>
/// <para>
/// <b>Null-Object mirror (ADR-032).</b> A <see cref="NullEmailDraftAi"/> is registered whenever the
/// <c>AzureOpenAI</c> gate is off, so this facade is ALWAYS resolvable (no asymmetric registration, §10 § F.1).
/// The Null object returns <c>null</c> → the endpoint reports a clean "unavailable" problem.
/// </para>
/// <para>
/// <b>Best-effort (NFR-04).</b> Returns <c>null</c> on any failure (AI disabled, completion failure) —
/// the endpoint maps <c>null</c> to a 503-style ProblemDetails rather than throwing.
/// </para>
/// </remarks>
public interface IEmailDraftAi
{
    /// <summary>
    /// Generate or refine an email body for the supplied intent + context. Returns <c>null</c> when AI is
    /// unavailable or the completion fails (best-effort — never throws for an expected "no result").
    /// </summary>
    Task<string?> DraftAsync(EmailDraftAiRequest request, CancellationToken ct = default);
}

/// <summary>The intent + body/subject context the sparkle draft operates over (Wave E).</summary>
public sealed record EmailDraftAiRequest
{
    /// <summary>Stable intent key (e.g. <c>reply</c>/<c>summarize</c>/<c>concise</c>/<c>formal</c>/<c>friendly</c>/<c>grammar</c>),
    /// or <c>custom</c> for the free-text "Enter prompt" action (then <see cref="UserInstruction"/> is required).</summary>
    public required string Intent { get; init; }

    /// <summary>Free-text instruction for the <c>custom</c> intent; ignored for preset intents.</summary>
    public string? UserInstruction { get; init; }

    /// <summary>The current message body (HTML or plain text per <see cref="IsHtml"/>) — the text to refine or reply to (may be empty).</summary>
    public required string CurrentBody { get; init; }

    /// <summary>Whether <see cref="CurrentBody"/> is HTML (the model is asked to return the same format).</summary>
    public bool IsHtml { get; init; }

    /// <summary>Current subject, for context (optional).</summary>
    public string? Subject { get; init; }
}
