using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Services.Ai.Chat.Tools;

/// <summary>
/// AI tool class providing text refinement capabilities for the SprkChatAgent.
///
/// Exposes three text manipulation methods that call <see cref="IChatClient"/> directly
/// with focused, minimal prompts — they do NOT use the full agent context or chat history:
///   - <see cref="RefineTextAsync"/> — reformat or improve text clarity per a given instruction
///   - <see cref="ExtractKeyPointsAsync"/> — extract the most important points from text
///   - <see cref="GenerateSummaryAsync"/> — generate a concise summary in a requested format
///
/// By calling <see cref="IChatClient"/> with focused single-turn prompts, these tools
/// avoid token overhead from full agent context injection (system prompt + history).
///
/// Instantiated by <see cref="SprkChatAgentFactory"/>. Not registered in DI — the factory
/// creates instances and registers methods as <see cref="AIFunction"/> objects via
/// <see cref="AIFunctionFactory.Create"/>.
/// </summary>
public sealed class TextRefinementTools
{
    private readonly IChatClient _chatClient;

    public TextRefinementTools(IChatClient chatClient)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
    }

    /// <summary>
    /// Reformats or improves the clarity of a text passage according to the given instruction.
    /// Use this when the user wants to rewrite, simplify, expand, or reformat content —
    /// for example: "rewrite this in formal language", "simplify this paragraph",
    /// "convert to bullet points".
    /// </summary>
    /// <param name="text">Reformat or improve text clarity</param>
    /// <param name="instruction">The specific refinement instruction to apply to the text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refined text output.</returns>
    public async Task<string> RefineTextAsync(
        [Description("Reformat or improve text clarity")] string text,
        string instruction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction, nameof(instruction));

        var messages = BuildRefineMessages(text, instruction);

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// Builds the prompt messages for a text refinement operation.
    ///
    /// This method is also used by the ChatEndpoints refine endpoint to construct
    /// the prompt for streaming responses via <see cref="IChatClient.GetStreamingResponseAsync"/>.
    /// </summary>
    /// <param name="text">The text passage to refine.</param>
    /// <param name="instruction">The refinement instruction.</param>
    /// <param name="surroundingContext">
    /// Optional surrounding paragraphs for additional AI context.
    /// When provided, the model receives context about where the selection appears
    /// in the document, improving refinement quality.
    /// </param>
    /// <returns>A list of chat messages ready for submission to the AI model.</returns>
    public List<ChatMessage> BuildRefineMessages(
        string text,
        string instruction,
        string? surroundingContext = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction, nameof(instruction));

        var systemPrompt = "You are a professional editor. Apply the user's instruction to refine the provided text. " +
            "Output only the refined text — no explanation, preamble, or meta-commentary.";

        var userPrompt = string.IsNullOrWhiteSpace(surroundingContext)
            ? $"Instruction: {instruction}\n\nText to refine:\n{text}"
            : $"Instruction: {instruction}\n\nSurrounding context (for reference only — do NOT include in output):\n{surroundingContext}\n\nText to refine:\n{text}";

        return
        [
            new ChatMessage(ChatRole.System, systemPrompt),
            new ChatMessage(ChatRole.User, userPrompt)
        ];
    }

    /// <summary>
    /// Extracts the most important key points from a text passage.
    /// Use this when the user wants to identify the main takeaways, action items,
    /// or critical information from a document section or analysis output.
    /// </summary>
    /// <param name="text">Extract key points from text</param>
    /// <param name="maxPoints">Maximum number of key points to extract (default: 5).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Formatted bullet list of key points.</returns>
    public async Task<string> ExtractKeyPointsAsync(
        [Description("Extract key points from text")] string text,
        int maxPoints = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));

        var count = Math.Clamp(maxPoints, 1, 20);

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System,
                $"You are an expert analyst. Extract exactly the {count} most important key points from the provided text. " +
                $"Format each point as a bullet (- ) on its own line. " +
                $"Be concise and factual. Output only the bullet points — no introduction or closing."),
            new ChatMessage(ChatRole.User, text)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// Generates a concise summary of the provided text in the requested format.
    /// Use this when the user needs a quick overview of lengthy content — for example
    /// to produce a paragraph summary, a TL;DR, or a structured bullet-point summary.
    /// </summary>
    /// <param name="text">Generate a concise summary</param>
    /// <param name="format">
    /// Desired output format. Supported values: "bullet" (default), "paragraph", "tldr".
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary in the requested format.</returns>
    public async Task<string> GenerateSummaryAsync(
        [Description("Generate a concise summary")] string text,
        string format = "bullet",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text, nameof(text));

        var formatInstruction = format.ToLowerInvariant() switch
        {
            "paragraph" => "Write a single concise paragraph (3–5 sentences) summarising the key content.",
            "tldr" => "Write a TL;DR in 1–2 sentences. Start with 'TL;DR: '.",
            _ => "Summarise the key content as a bullet list (use - for each bullet). Be concise and informative."
        };

        var messages = new List<ChatMessage>
        {
            new ChatMessage(ChatRole.System,
                $"You are an expert summariser. {formatInstruction} " +
                $"Output only the summary — no introduction, preamble, or closing remarks."),
            new ChatMessage(ChatRole.User, text)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        return response.Text ?? string.Empty;
    }
}
