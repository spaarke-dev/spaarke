using System.Globalization;

namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// Text helpers shared by the MCP tools (spaarkeai-mcp-bff-api-r2 tasks 023, 024, 025). A tool's text content is read by the model line by
/// line, so a user-authored value (a playbook name, a task subject, a document name) must never start a line of its own and pass for another
/// row or for a link line. Structured content keeps every value as it is.
/// </summary>
public static class McpText
{
    /// <summary>
    /// The value on one line: every control character (CR, LF, tab, NEL, ...) and Unicode line or paragraph separator becomes a space.
    /// Nothing else changes (no trim, no collapse, no clip). Null becomes the empty string.
    /// </summary>
    public static string SingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Any(IsLineBreaking)
            ? string.Create(value.Length, value, static (span, v) =>
            {
                for (var i = 0; i < v.Length; i++)
                {
                    span[i] = IsLineBreaking(v[i]) ? ' ' : v[i];
                }
            })
            : value;
    }

    private static bool IsLineBreaking(char c) =>
        char.IsControl(c) || char.GetUnicodeCategory(c) is UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
