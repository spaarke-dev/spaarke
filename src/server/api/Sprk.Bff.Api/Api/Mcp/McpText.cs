namespace Sprk.Bff.Api.Api.Mcp;

/// <summary>
/// Text helpers for the model-visible lines of MCP tool results (spaarkeai-mcp-bff-api-r2).
/// </summary>
public static class McpText
{
    /// <summary>
    /// <paramref name="value"/> as one line: every control character (<see cref="char.IsControl(char)"/>: CR, LF, tab, NEL, ...) and every
    /// Unicode line or paragraph separator becomes a space, so a user-authored value (a title, a name) cannot start a line of its own and
    /// pass for another row. Null becomes the empty string. Use it for every user-authored value written into a line of text; a tool's
    /// structured rows keep the value as it is.
    /// </summary>
    public static string SingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Any(IsLineBreaking))
        {
            return value;
        }

        return string.Create(value.Length, value, static (span, v) =>
        {
            for (var i = 0; i < v.Length; i++)
            {
                span[i] = IsLineBreaking(v[i]) ? ' ' : v[i];
            }
        });
    }

    private static bool IsLineBreaking(char c) =>
        char.IsControl(c)
        || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator;
}
