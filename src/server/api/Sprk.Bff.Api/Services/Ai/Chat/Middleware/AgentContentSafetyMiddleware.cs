using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Sprk.Bff.Api.Models.Ai.Chat;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Services.Ai.Chat.Middleware;

/// <summary>
/// Content safety middleware for the SprkChat agent pipeline (AIPL-057).
///
/// Scans response tokens for sensitive content patterns (PII) and replaces
/// detected patterns with "[content filtered]" before the token reaches the caller.
///
/// Supported pattern categories:
///   - SSN (Social Security Numbers): ###-##-####
///   - Credit card numbers: 13-19 digit sequences
///   - Email addresses in response content
///
/// Constraint (ADR-013): Content safety runs at the middleware level.
/// Constraint (AIPL-057): Logs pattern type only — NEVER logs the matched content.
///
/// The pattern list is configurable via constructor injection. When no patterns are
/// supplied, the middleware uses a sensible default set covering SSN, credit card, and email.
///
/// Lifetime: Transient — one instance per agent session, created by <see cref="SprkChatAgentFactory"/>.
/// </summary>
public sealed class AgentContentSafetyMiddleware : ISprkChatAgent
{
    /// <summary>Replacement text inserted when a sensitive pattern is detected.</summary>
    internal const string FilteredPlaceholder = "[content filtered]";

    private readonly ISprkChatAgent _inner;
    private readonly ILogger _logger;
    private readonly IReadOnlyList<ContentSafetyPattern> _patterns;

    public AgentContentSafetyMiddleware(
        ISprkChatAgent inner,
        ILogger logger,
        IReadOnlyList<ContentSafetyPattern>? patterns = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _patterns = patterns ?? DefaultPatterns;
    }

    /// <inheritdoc />
    public ChatContext Context => _inner.Context;

    /// <summary>
    /// Streams the inner agent response, scanning each token for sensitive content
    /// patterns and replacing matches with <see cref="FilteredPlaceholder"/>.
    /// </summary>
    public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
        string message,
        IReadOnlyList<AiChatMessage> history,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var update in _inner.SendMessageAsync(message, history, cancellationToken))
        {
            if (update.Text is not null)
            {
                var filteredText = FilterContent(update.Text);
                if (!ReferenceEquals(filteredText, update.Text))
                {
                    // Content was modified — yield a new update with filtered text.
                    // ChatResponseUpdate.Text is read-only (computed from Contents);
                    // add a TextContent to Contents instead.
                    var filteredUpdate = new ChatResponseUpdate { Role = update.Role };
                    filteredUpdate.Contents.Add(new TextContent(filteredText));
                    yield return filteredUpdate;
                    continue;
                }
            }

            yield return update;
        }
    }

    /// <summary>
    /// Scans <paramref name="text"/> for all configured patterns and replaces matches.
    /// Returns the original string reference unchanged if no patterns match (avoids allocation).
    /// </summary>
    private string FilterContent(string text)
    {
        var result = text;
        var modified = false;

        foreach (var pattern in _patterns)
        {
            if (pattern.Regex.IsMatch(result))
            {
                // Log warning with pattern name only — NEVER log matched content
                _logger.LogWarning(
                    "ChatAgent content safety: detected {PatternName} pattern in response for playbook={PlaybookId}",
                    pattern.Name, _inner.Context.PlaybookId);

                result = pattern.Regex.Replace(result, FilteredPlaceholder);
                modified = true;
            }
        }

        return modified ? result : text;
    }

    // =========================================================================
    // Default PII patterns
    // =========================================================================

    /// <summary>
    /// Default set of content safety patterns covering common PII types.
    /// </summary>
    internal static readonly IReadOnlyList<ContentSafetyPattern> DefaultPatterns = new[]
    {
        // SSN: ###-##-#### (with word boundaries to avoid false positives)
        new ContentSafetyPattern(
            "SSN",
            new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled)),

        // Credit card: 13-19 digit sequences (with optional separators)
        new ContentSafetyPattern(
            "CreditCard",
            new Regex(@"\b(?:\d[ -]*?){13,19}\b", RegexOptions.Compiled)),

        // Email addresses
        new ContentSafetyPattern(
            "Email",
            new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled))
    };
}

/// <summary>
/// Defines a named content safety pattern for PII detection.
/// </summary>
/// <param name="Name">Human-readable name for logging (e.g., "SSN", "CreditCard").</param>
/// <param name="Regex">Compiled regex for pattern matching.</param>
public record ContentSafetyPattern(string Name, Regex Regex);
