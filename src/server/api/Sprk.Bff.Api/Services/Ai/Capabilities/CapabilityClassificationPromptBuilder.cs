using System.Text;
using Microsoft.Extensions.AI;

namespace Sprk.Bff.Api.Services.Ai.Capabilities;

/// <summary>
/// Builds the compact classification prompt sent to GPT-4o-mini during Layer 2 routing (AIPU2-013).
///
/// Prompt structure (target: &lt;= 600 tokens total):
///   System message: role + task instruction + output format (~80 tokens).
///   Capability index: name + one-line description for each candidate (~20 tokens per entry × up to 20 = ~400 tokens).
///   User message: the user turn text (budget ~120 tokens).
///
/// Token approximation: 4 characters ≈ 1 token (conservative estimate for English + JSON).
/// The builder enforces a hard character limit of 2 400 chars (~600 tokens) to keep costs predictable.
///
/// ADR-015: the user turn text is included in the prompt sent to the LLM but is NEVER
/// stored in logs, OTEL spans, or other observable surfaces.
/// </summary>
public static class CapabilityClassificationPromptBuilder
{
    /// <summary>
    /// Conservative character-to-token ratio (4 chars ≈ 1 token for English text + JSON).
    /// </summary>
    private const int CharsPerToken = 4;

    /// <summary>
    /// Maximum total tokens allowed across the full prompt.
    /// Layer 2 NFR: &lt;= 600 tokens per classification call.
    /// </summary>
    public const int MaxTotalTokens = 600;

    /// <summary>
    /// Character budget for the full prompt (MaxTotalTokens × CharsPerToken).
    /// </summary>
    private const int MaxTotalChars = MaxTotalTokens * CharsPerToken;

    /// <summary>
    /// Character budget reserved for the system + user scaffolding text
    /// (role sentence, instructions, output format, separators).
    /// Approximated from the static template text below.
    /// </summary>
    private const int ScaffoldChars = 480;

    /// <summary>
    /// Character budget available for the capability index + user turn.
    /// </summary>
    private const int ContentChars = MaxTotalChars - ScaffoldChars;

    /// <summary>
    /// Budget split: capability index gets 75 % of content chars; user turn gets 25 %.
    /// At 600 tokens total this gives ~340 tokens for index and ~105 tokens for the turn.
    /// </summary>
    private const int CapabilityIndexCharBudget = (int)(ContentChars * 0.75);
    private const int UserTurnCharBudget = ContentChars - CapabilityIndexCharBudget;

    private const string SystemMessageTemplate =
        "You are a capability classifier for the Spaarke AI platform. " +
        "Given a user message and a list of available capabilities, identify which capability or capabilities " +
        "the user is requesting. Return a JSON object in this exact format:\n" +
        "{ \"capabilities\": [{ \"name\": \"<capability_name>\", \"confidence\": <0.0-1.0> }] }\n" +
        "Rules:\n" +
        "- Only return capability names from the provided list (exact match, case-sensitive).\n" +
        "- Order results by confidence descending.\n" +
        "- Return at most 3 capabilities.\n" +
        "- Return an empty capabilities array if none match.\n" +
        "- Do not explain your reasoning.\n\n" +
        "Available capabilities:\n";

    /// <summary>
    /// Builds the chat messages for the Layer 2 GPT-4o-mini classification call.
    ///
    /// The returned list contains exactly two messages:
    ///   [0] System message: role + capability index.
    ///   [1] User message: the user turn text (truncated if over budget).
    ///
    /// Total prompt is guaranteed to be within <see cref="MaxTotalTokens"/> tokens
    /// (using the 4-chars-per-token approximation).
    /// </summary>
    /// <param name="userTurn">The user's message text.</param>
    /// <param name="candidates">
    /// Ordered list of candidate capabilities to include in the index.
    /// Callers should pre-trim this to <see cref="Layer2Options.MaxCandidates"/> entries.
    /// The builder further truncates if the index would exceed the character budget.
    /// </param>
    /// <returns>Ordered chat messages ready to send to an <see cref="IChatClient"/>.</returns>
    public static IList<ChatMessage> Build(string userTurn, IReadOnlyList<CapabilityManifestEntry> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // Build the capability index within the character budget.
        var index = BuildCapabilityIndex(candidates, CapabilityIndexCharBudget);

        // Truncate the user turn if it exceeds its budget.
        var truncatedTurn = userTurn.Length * CharsPerToken > UserTurnCharBudget * CharsPerToken
            ? userTurn[..(UserTurnCharBudget / CharsPerToken * CharsPerToken)]
            : userTurn;

        return
        [
            new ChatMessage(ChatRole.System, SystemMessageTemplate + index),
            new ChatMessage(ChatRole.User, truncatedTurn)
        ];
    }

    /// <summary>
    /// Approximates the token count of the built prompt for a given set of candidates.
    /// Uses the 4-chars-per-token heuristic.
    ///
    /// Useful in tests to verify the prompt stays within the 600-token budget.
    /// </summary>
    public static int ApproximateTokenCount(string userTurn, IReadOnlyList<CapabilityManifestEntry> candidates)
    {
        var messages = Build(userTurn, candidates);
        var totalChars = messages.Sum(m => m.Text?.Length ?? 0);
        return (int)Math.Ceiling((double)totalChars / CharsPerToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Renders the capability index as a numbered list of "name: description" lines,
    /// adding entries until the character budget is exhausted.
    /// </summary>
    private static string BuildCapabilityIndex(
        IReadOnlyList<CapabilityManifestEntry> candidates,
        int charBudget)
    {
        var sb = new StringBuilder(charBudget);
        var remaining = charBudget;

        for (var i = 0; i < candidates.Count; i++)
        {
            var entry = candidates[i];
            // Format: "{n}. {name}: {description}\n"
            var line = $"{i + 1}. {entry.CapabilityName}: {entry.Description}\n";

            if (line.Length > remaining)
            {
                // No more budget — stop adding capabilities.
                break;
            }

            sb.Append(line);
            remaining -= line.Length;
        }

        return sb.ToString();
    }
}
