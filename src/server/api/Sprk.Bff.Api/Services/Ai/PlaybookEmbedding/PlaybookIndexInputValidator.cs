using System.Text.Json;
using Sprk.Bff.Api.Models.Ai;

namespace Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

/// <summary>
/// Validates that a playbook has the minimum-required fields to be indexed for routing
/// (chat-routing-redesign-r1 FR-12). Returns the list of missing field names so the
/// caller can emit ProblemDetails per ADR-019.
/// </summary>
/// <remarks>
/// <para>
/// FR-12 requires three fields: <c>description</c> (free-text classifier signal),
/// <c>documentTypes</c> (file-aware pre-filter input), and <c>destinationHint</c>
/// (output routing). All three are checked against <see cref="PlaybookResponse"/>:
/// </para>
/// <list type="bullet">
///   <item><description><c>description</c> ← <see cref="PlaybookResponse.Description"/> non-empty</description></item>
///   <item><description><c>documentTypes</c> ← <see cref="PlaybookResponse.JpsMatchingMetadata"/> JSON has non-empty <c>documentTypes</c> string array</description></item>
///   <item><description><c>destinationHint</c> ← <see cref="PlaybookResponse.JpsMatchingMetadata"/> JSON has non-empty <c>outputDestination</c> string</description></item>
/// </list>
/// <para>
/// Per ADR-015, this validator NEVER logs / surfaces the raw JSON content. The caller is
/// responsible for logging with the playbook ID + the returned <c>missingFields</c> list.
/// </para>
/// <para>
/// Registered as a <c>Singleton</c> (stateless) in <c>AiModule.AddAiServices</c>.
/// </para>
/// </remarks>
public sealed class PlaybookIndexInputValidator
{
    /// <summary>
    /// Returns the list of MissingFields (empty list = valid). Field names use the
    /// spec FR-12 wording: <c>description</c>, <c>documentTypes</c>, <c>destinationHint</c>.
    /// </summary>
    /// <param name="playbook">Playbook to validate. Must not be null.</param>
    /// <returns>
    /// Empty list when valid; otherwise the list of missing field names in stable order
    /// (<c>description</c>, <c>documentTypes</c>, <c>destinationHint</c>) — order is significant
    /// for deterministic ProblemDetails payloads.
    /// </returns>
    public IReadOnlyList<string> Validate(PlaybookResponse playbook)
    {
        ArgumentNullException.ThrowIfNull(playbook);

        var missing = new List<string>(3);

        // description — straightforward null/whitespace check
        if (string.IsNullOrWhiteSpace(playbook.Description))
        {
            missing.Add("description");
        }

        // Parse JPS JSON once for both documentTypes + destinationHint.
        // Use the existing tolerant parser for array fields, and a focused try/parse
        // here for outputDestination (a string, not an array).
        var jpsParse = PlaybookEmbeddingService.ParseJpsMatchingMetadata(playbook.JpsMatchingMetadata);
        var hasDocumentTypes = !jpsParse.Malformed && jpsParse.DocumentTypes.Count > 0;
        var hasDestinationHint = TryReadOutputDestination(playbook.JpsMatchingMetadata, out var destination)
            && !string.IsNullOrWhiteSpace(destination);

        if (!hasDocumentTypes)
        {
            missing.Add("documentTypes");
        }

        if (!hasDestinationHint)
        {
            // FR-12 does not distinguish missing-vs-malformed JSON — same user-facing message.
            missing.Add("destinationHint");
        }

        return missing;
    }

    /// <summary>
    /// Reads <c>outputDestination</c> from the JPS matching metadata JSON. Tolerant of
    /// null / whitespace / malformed / non-object / missing-property / non-string-value;
    /// all degrade to <c>false</c> + empty <paramref name="destination"/>.
    /// </summary>
    private static bool TryReadOutputDestination(string? json, out string? destination)
    {
        destination = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("outputDestination", out var prop) ||
                prop.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            destination = prop.GetString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
