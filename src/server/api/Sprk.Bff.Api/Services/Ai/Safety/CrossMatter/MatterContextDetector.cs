using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Safety.CrossMatter;

/// <summary>
/// Detects matter pivots in a conversation by comparing the incoming matter ID against the
/// matter ID recorded on the most recent conversation turn.
///
/// The matter ID is carried via <see cref="ChatMessage"/> metadata using a marker prefix
/// embedded in System-role messages written by the BFF at context-switch time
/// (format: <c>__matter:{matterId}__</c>).  When no such marker exists, the history has no
/// matter context and no change is reported.
///
/// Lifetime: Scoped — one instance per HTTP request (ADR-010).
/// </summary>
public sealed class MatterContextDetector : IMatterContextDetector
{
    /// <summary>
    /// Prefix used to embed matter ID metadata in system messages within the conversation history.
    /// Written by the BFF when matter context is established or switched.
    /// </summary>
    internal const string MatterMarkerPrefix = "__matter:";

    private readonly ILogger<MatterContextDetector> _logger;

    public MatterContextDetector(ILogger<MatterContextDetector> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public MatterContextChange? DetectChange(
        IReadOnlyList<ChatMessage> history,
        string incomingMatterId)
    {
        // If the incoming matter context is not set, there is nothing to compare against.
        if (string.IsNullOrWhiteSpace(incomingMatterId))
        {
            return null;
        }

        // Walk the history from newest to oldest to find the last recorded matter marker.
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var message = history[i];

            // Matter markers are embedded in System role messages written by the BFF.
            if (message.Role != ChatMessageRole.System)
            {
                continue;
            }

            var matterId = ExtractMatterId(message.Content);
            if (matterId is null)
            {
                continue;
            }

            // Found the most recent matter marker.  Compare to incoming.
            if (string.Equals(matterId, incomingMatterId, StringComparison.OrdinalIgnoreCase))
            {
                // Same matter — no pivot.
                _logger.LogDebug(
                    "MatterContextDetector: same matter {MatterId}, no pivot detected",
                    incomingMatterId);
                return null;
            }

            // Different matter — pivot detected.
            _logger.LogInformation(
                "MatterContextDetector: pivot detected — previous={PreviousMatterId}, new={NewMatterId}, at turn index {TurnIndex}",
                matterId, incomingMatterId, i);

            return new MatterContextChange(
                PreviousMatterId: matterId,
                NewMatterId: incomingMatterId,
                ChangeDetectedAtTurnIndex: i);
        }

        // No matter marker in history.  The incoming matter ID establishes a fresh context;
        // no content stripping is needed.
        return null;
    }

    /// <summary>
    /// Builds a system message content string that embeds the matter ID as a parseable marker.
    /// Called by the BFF when establishing or switching matter context so that future turns can
    /// detect a pivot via <see cref="DetectChange"/>.
    /// </summary>
    public static string BuildMatterMarker(string matterId)
        => $"{MatterMarkerPrefix}{matterId}__";

    /// <summary>
    /// Extracts the matter ID from a system message content string, or returns <c>null</c>
    /// if the string does not contain a matter marker.
    /// </summary>
    internal static string? ExtractMatterId(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        var start = content.IndexOf(MatterMarkerPrefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var valueStart = start + MatterMarkerPrefix.Length;
        var end = content.IndexOf("__", valueStart, StringComparison.Ordinal);
        if (end < 0)
        {
            return null;
        }

        var matterId = content[valueStart..end];
        return string.IsNullOrWhiteSpace(matterId) ? null : matterId;
    }
}
