namespace Sprk.Bff.Api.Infrastructure.Text;

/// <summary>
/// Pure string helpers shared across BFF features. Lives outside <c>Services/Ai</c> so non-AI code (e.g. the task-181
/// grant notification) can use it without a CRUD→AI dependency (ADR-013, <c>bff-extensions.md</c> §A.4).
/// </summary>
internal static class TextTruncation
{
    /// <summary>
    /// Caps <paramref name="text"/> at <paramref name="maxLength"/> characters, backing
    /// off one char if the cap would split a surrogate pair (e.g. an emoji), which would
    /// produce a malformed UTF-16 string. Appends an ellipsis only when truncated.
    /// </summary>
    internal static string TruncateSurrogateSafe(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = maxLength;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut] + "…";
    }
}
