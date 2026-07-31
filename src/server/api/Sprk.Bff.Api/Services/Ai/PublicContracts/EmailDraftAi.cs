using System.Text;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Real implementation of <see cref="IEmailDraftAi"/> (Wave E) — a thin single-shot wrapper over
/// <see cref="IChatClient"/> (the SAME client the <c>ChatEndpoints.RefineTextAsync</c> path uses).
/// Registered only when the <c>AzureOpenAI</c> gate is on (see AiModule); the Null-Object mirror covers
/// the off case (ADR-032). Best-effort (NFR-04): any failure returns <c>null</c>.
/// </summary>
/// <remarks>
/// ADR-015: logs identifiers/sizes only — never the body/instruction text.
/// ADR-016: an explicit timeout bounds the upstream AI call.
/// </remarks>
public sealed class EmailDraftAi : IEmailDraftAi
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<EmailDraftAi> _logger;

    /// <summary>Timeout for the Azure OpenAI completion (ADR-016).</summary>
    private static readonly TimeSpan AiCallTimeout = TimeSpan.FromSeconds(30);

    // Preset intent → instruction. The prompt text lives server-side (admin-editable is the growth path:
    // swap this static map for a prompt-library lookup — see memory `email-drafting-templates-and-ai-decisions`).
    private static readonly IReadOnlyDictionary<string, string> IntentInstructions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["reply"] = "Draft a professional reply to the email below.",
            ["summarize"] = "Summarize the email thread below in a few clear, factual sentences.",
            ["concise"] = "Rewrite the email body below to be more concise while preserving all key information.",
            ["formal"] = "Rewrite the email body below in a formal, professional tone.",
            ["friendly"] = "Rewrite the email body below in a warm, friendly, approachable tone.",
            ["grammar"] = "Fix any grammar, spelling, and punctuation issues in the email body below and improve clarity, without changing its meaning.",
        };

    public EmailDraftAi(IChatClient chatClient, ILogger<EmailDraftAi> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> DraftAsync(EmailDraftAiRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var instruction = ResolveInstruction(request);
        if (string.IsNullOrWhiteSpace(instruction))
        {
            _logger.LogWarning("[EMAIL-DRAFT] Unresolvable intent '{Intent}' — no instruction.", request.Intent);
            return null;
        }

        // ADR-015: identifiers/sizes only.
        _logger.LogInformation(
            "[EMAIL-DRAFT] Drafting: Intent={Intent}, IsHtml={IsHtml}, BodyLength={BodyLength}, HasInstruction={HasInstruction}",
            request.Intent, request.IsHtml, request.CurrentBody.Length, !string.IsNullOrWhiteSpace(request.UserInstruction));

        var messages = BuildMessages(request, instruction);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(AiCallTimeout);

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cts.Token);
            var text = response?.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("[EMAIL-DRAFT] Empty completion for Intent={Intent}.", request.Intent);
                return null;
            }

            _logger.LogInformation(
                "[EMAIL-DRAFT] Draft complete: Intent={Intent}, ResultLength={ResultLength}", request.Intent, text.Length);
            return text;
        }
        catch (Exception ex)
        {
            // Best-effort (NFR-04): the endpoint maps null → a clean 503-style problem.
            _logger.LogWarning(ex, "[EMAIL-DRAFT] Draft failed for Intent={Intent}.", request.Intent);
            return null;
        }
    }

    private static string? ResolveInstruction(EmailDraftAiRequest request)
    {
        if (string.Equals(request.Intent, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(request.UserInstruction) ? null : request.UserInstruction!.Trim();
        }
        return IntentInstructions.TryGetValue(request.Intent, out var instruction) ? instruction : null;
    }

    private static List<ChatMessage> BuildMessages(EmailDraftAiRequest request, string instruction)
    {
        var formatHint = request.IsHtml
            ? "Return the result as an HTML fragment matching the input body's formatting."
            : "Return the result as plain text.";

        var system =
            "You are a professional assistant that drafts and refines business email. Apply the user's instruction. " +
            "Output ONLY the resulting email body — no preamble, no explanation, no subject line, no signature block. " +
            formatHint;

        var sb = new StringBuilder();
        sb.Append("Instruction: ").Append(instruction).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(request.Subject))
        {
            sb.Append("Subject: ").Append(request.Subject).Append('\n');
        }
        if (!string.IsNullOrWhiteSpace(request.CurrentBody))
        {
            sb.Append("\nCurrent email body:\n").Append(request.CurrentBody);
        }
        else
        {
            sb.Append("\n(The email body is currently empty.)");
        }

        return
        [
            new ChatMessage(ChatRole.System, system),
            new ChatMessage(ChatRole.User, sb.ToString())
        ];
    }
}
