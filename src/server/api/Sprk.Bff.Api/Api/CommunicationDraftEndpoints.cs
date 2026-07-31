using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// One thin endpoint backing the Spaarke email composer's AI "sparkle" drafting feature:
/// <c>POST /api/communications/draft</c> generates or refines the message body for a small intent
/// (draft a reply / summarize / make concise / formal / friendly / fix grammar) or a free-text prompt.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placement (BFF §10 / §11).</b> Lives on the Communication surface (the composer's sibling of
/// <see cref="CommunicationTemplateEndpoints"/>). AI capability is consumed ONLY through the
/// <see cref="IEmailDraftAi"/> PublicContracts facade (ADR-013 / §10 bullet 3) — this endpoint never
/// injects <c>IChatClient</c>/<c>IOpenAiClient</c> directly. The facade is registered unconditionally
/// (real when the AzureOpenAI gate is on, Null-Object mirror otherwise — ADR-032), so the endpoint always
/// resolves and degrades to a clean 503 when AI is unconfigured. Cost of doing nothing (§11): the compose
/// sparkle has no server to call. No new NuGet, no new DI beyond the one facade pair; publish-size impact nil.
/// </para>
/// <para>
/// ADR-015: logs identifiers/sizes only (the facade owns the AI call + minimal-text logging).
/// </para>
/// </remarks>
public static class CommunicationDraftEndpoints
{
    public static IEndpointRouteBuilder MapCommunicationDraftEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/communications/draft", DraftAsync)
            .RequireAuthorization()
            .WithName("DraftCommunicationBody")
            .WithTags("Communications")
            .WithDescription("Generate or refine an email body via AI for the compose 'sparkle' feature (preset intents or a free-text prompt).")
            .Produces<CommunicationDraftResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    /// <summary>
    /// Validates the request and delegates generation to <see cref="IEmailDraftAi"/>. A <c>null</c> result
    /// (AI disabled or completion failed) maps to a 503-style ProblemDetails so the composer can surface a
    /// clean "try again later" message rather than a hard error.
    /// </summary>
    internal static async Task<IResult> DraftAsync(
        CommunicationDraftRequest request,
        IEmailDraftAi emailDraftAi,
        ILogger<CommunicationDraftResponse> logger,
        CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Intent))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "intent is required.",
                statusCode: 400);
        }

        if (string.Equals(request.Intent, "custom", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(request.UserInstruction))
        {
            throw new SdapProblemException(
                code: "VALIDATION_ERROR",
                title: "Validation Error",
                detail: "userInstruction is required when intent is 'custom'.",
                statusCode: 400);
        }

        var text = await emailDraftAi.DraftAsync(
            new EmailDraftAiRequest
            {
                Intent = request.Intent,
                UserInstruction = request.UserInstruction,
                CurrentBody = request.CurrentBody ?? string.Empty,
                IsHtml = request.IsHtml,
                Subject = request.Subject,
            },
            ct);

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogInformation("[EMAIL-DRAFT] No draft produced for Intent={Intent} (AI unavailable or empty).", request.Intent);
            return Results.Problem(
                detail: "AI drafting is currently unavailable. Please try again later.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "AI Drafting Unavailable");
        }

        return Results.Ok(new CommunicationDraftResponse
        {
            Text = text,
            IsHtml = request.IsHtml,
        });
    }
}

/// <summary>Request body for <c>POST /api/communications/draft</c>.</summary>
public sealed record CommunicationDraftRequest
{
    /// <summary>Stable intent key (e.g. <c>reply</c>/<c>concise</c>/<c>formal</c>), or <c>custom</c> for a free-text prompt.</summary>
    public string Intent { get; init; } = string.Empty;

    /// <summary>Free-text instruction; required when <see cref="Intent"/> is <c>custom</c>.</summary>
    public string? UserInstruction { get; init; }

    /// <summary>The current message body (HTML or plain per <see cref="IsHtml"/>) — the text to refine or reply to (may be empty).</summary>
    public string? CurrentBody { get; init; }

    /// <summary>Whether <see cref="CurrentBody"/> is HTML.</summary>
    public bool IsHtml { get; init; }

    /// <summary>Current subject, for context (optional).</summary>
    public string? Subject { get; init; }
}

/// <summary>The drafted body returned to the composer.</summary>
public sealed record CommunicationDraftResponse
{
    /// <summary>The generated/refined email body.</summary>
    public string? Text { get; init; }

    /// <summary>Whether <see cref="Text"/> is HTML (echoes the request format).</summary>
    public bool IsHtml { get; init; }
}
