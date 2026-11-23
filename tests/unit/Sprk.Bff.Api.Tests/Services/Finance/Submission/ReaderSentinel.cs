using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 082c-2 (brief C16; uac-r2 decision option (a), item 5): the sentinel scan over reader-facing text. It finds a record rule's
/// <c>BRR-</c> code and any id-shaped value: 32 hex digits (a <c>Guid</c> in "N" form, which is what <c>BRR-</c> carries) or a <c>Guid</c> in
/// "D" / "B" form. A reader surface built from codes, names and <c>ForReaders()</c> holds none.
/// </summary>
internal static partial class ReaderSentinel
{
    [GeneratedRegex(@"BRR-|(?<![0-9A-Fa-f])[0-9A-Fa-f]{32}(?![0-9A-Fa-f])|[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}")]
    private static partial Regex IdShaped();

    /// <summary>Every <c>BRR-</c> or id-shaped match in <paramref name="text"/> (empty when the text is reader-safe).</summary>
    public static IReadOnlyList<string> Scan(string? text) =>
        string.IsNullOrEmpty(text) ? [] : IdShaped().Matches(text).Select(m => m.Value).ToList();
}
