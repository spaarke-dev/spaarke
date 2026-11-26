using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>One numbered section of a draft, as the accelerator's <c>segment_clauses.py</c> reports it.</summary>
/// <param name="Index">The paragraph's position in the document body.</param>
/// <param name="Number">The clause number as written or as auto-numbered, when there is one.</param>
/// <param name="Heading">The clause heading, when there is one.</param>
/// <param name="ParaKey">
/// The shortest leading text that occurs in exactly one paragraph: the only value an edit's <c>para</c> may take. Null when no unique
/// key could be made, and such a section cannot be edited.
/// </param>
/// <param name="Text">The paragraph's visible text.</param>
internal sealed record AgreementSection(int Index, string? Number, string? Heading, string? ParaKey, string Text);

/// <summary>
/// The deterministic segmentation of a draft that <c>agreement-edit-plan</c> reads instead of free document text, and the exact-match
/// anchor pre-check that runs on every edit the model returns (spaarke-intake-pipeline-r1 task 004, FR-02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Anchoring = a closed <c>para_key</c> list plus an exact-match pre-check; there is no fuzzy matching anywhere.</b> An edit's
/// <c>para</c> must equal one of the supplied <c>para_key</c> values character for character. Its <c>find</c> or <c>after</c> must occur
/// exactly once in that section's text, and on whole words. Its <c>old</c> must occur exactly once inside <c>find</c>. An edit that fails
/// any of these is refused with a code from a closed set. It is never repaired, never re-anchored to the closest text, and no code path
/// searches the whole document for a quote (ADR-049: the write path does not search text; every anchor the AI returns is validated
/// before apply).
/// </para>
/// <para>
/// The pre-check mirrors the redline engine's own anchor rules (<c>ooxml_redline.py</c>: <c>find_para</c>, <c>_locate</c>,
/// <c>word_edge_ok</c>) so a bad anchor is stopped early and can be handed back for one rewrite. The engine stays the final authority:
/// it also checks the editable text, existing revisions, fields and content controls, which the segmentation does not describe. Whitespace
/// is collapsed on both sides before comparing (the engine's <c>norm()</c>), so a tab in the section and a space in the quote are the same.
/// </para>
/// </remarks>
internal sealed class AgreementSegmentation
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, AgreementSection> _byParaKey;
    private readonly HashSet<int> _indexes;

    private AgreementSegmentation(
        IReadOnlyList<AgreementSection> sections, bool hasTrackedChanges, bool hasComments, bool hasHiddenText)
    {
        Sections = sections;
        HasTrackedChanges = hasTrackedChanges;
        HasComments = hasComments;
        HasHiddenText = hasHiddenText;
        _byParaKey = sections.Where(s => s.ParaKey is not null).ToDictionary(s => s.ParaKey!, StringComparer.Ordinal);
        _indexes = sections.Select(s => s.Index).ToHashSet();
    }

    public IReadOnlyList<AgreementSection> Sections { get; }

    public bool HasTrackedChanges { get; }

    public bool HasComments { get; }

    public bool HasHiddenText { get; }

    public bool HasSection(int index) => _indexes.Contains(index);

    /// <summary>
    /// Reads a <c>segment_clauses.py</c> result. A caller error (the engine's output is malformed) is an <see cref="ArgumentException"/>
    /// that names the member and never quotes the document.
    /// </summary>
    public static AgreementSegmentation Parse(string segmentationJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(segmentationJson);
        }
        catch (JsonException)
        {
            throw new ArgumentException("The segmentation is not valid JSON.", nameof(segmentationJson));
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("sections", out var sectionsElement)
                || sectionsElement.ValueKind != JsonValueKind.Array)
            {
                throw Invalid("sections");
            }

            var sections = new List<AgreementSection>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            var seenIndexes = new HashSet<int>();
            var i = 0;
            foreach (var item in sectionsElement.EnumerateArray())
            {
                var path = $"sections[{i++}]";
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid(path);
                }

                if (!item.TryGetProperty("index", out var indexElement) || !indexElement.TryGetInt32(out var index) || !seenIndexes.Add(index))
                {
                    throw Invalid($"{path}.index");
                }

                if (!item.TryGetProperty("text", out var textElement)
                    || textElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(textElement.GetString()))
                {
                    throw Invalid($"{path}.text");
                }

                var paraKey = NullableString(item, "para_key", path);
                if (paraKey is not null && (paraKey.Length == 0 || !seenKeys.Add(paraKey)))
                {
                    throw Invalid($"{path}.para_key");
                }

                sections.Add(new AgreementSection(
                    index, NullableString(item, "number", path), NullableString(item, "heading", path), paraKey, textElement.GetString()!));
            }

            return new AgreementSegmentation(
                sections,
                RequireBool(root, "has_tracked_changes"),
                RequireBool(root, "has_comments"),
                RequireBool(root, "has_hidden_text"));
        }
    }

    /// <summary>
    /// The refusal code for an edit whose anchors do not fit, or null when every anchor is exact. The model's strings are compared as they
    /// are; nothing is trimmed, case-folded or approximated.
    /// </summary>
    /// <returns>One of <see cref="AgreementRefusalCodes"/>, or null.</returns>
    public string? CheckAnchors(string para, string? find, string? old, string? after)
    {
        if (!_byParaKey.TryGetValue(para, out var section))
        {
            return AgreementRefusalCodes.ParaNotFound;
        }

        var text = Whitespace.Replace(section.Text, " ");

        if (after is not null)
        {
            var refusal = Locate(text, Whitespace.Replace(after, " "), out _);
            if (refusal is not null)
            {
                return refusal;
            }
        }

        if (find is null)
        {
            return null;
        }

        // find "*" is the whole paragraph body: there is no quote to locate, and old (if any) is searched in the section text.
        var findText = find == "*" ? text : Whitespace.Replace(find, " ");
        var findStart = 0;
        if (find != "*")
        {
            var refusal = Locate(text, findText, out findStart);
            if (refusal is not null)
            {
                return refusal;
            }
        }

        if (old is null)
        {
            return null;
        }

        var oldText = Whitespace.Replace(old, " ");
        var inFind = CountOccurrences(findText, oldText);
        if (oldText.Length == 0 || inFind != 1)
        {
            return AgreementRefusalCodes.OldNotInFind;
        }

        var oldStart = findStart + findText.IndexOf(oldText, StringComparison.Ordinal);
        return WordEdgeOk(text, oldStart, oldStart + oldText.Length) ? null : AgreementRefusalCodes.WordBoundary;
    }

    private static string? Locate(string text, string needle, out int start)
    {
        start = -1;
        var count = CountOccurrences(text, needle);
        if (needle.Length == 0 || count == 0)
        {
            return AgreementRefusalCodes.NotFound;
        }

        if (count > 1)
        {
            return AgreementRefusalCodes.NotUnique;
        }

        start = text.IndexOf(needle, StringComparison.Ordinal);
        return WordEdgeOk(text, start, start + needle.Length) ? null : AgreementRefusalCodes.WordBoundary;
    }

    /// <summary>Non-overlapping occurrences, like Python's <c>str.count</c> in the engine. An empty needle never matches.</summary>
    private static int CountOccurrences(string text, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>The engine's <c>word_edge_ok</c>: the span [start, end) must not cut a word at either end.</summary>
    private static bool WordEdgeOk(string text, int start, int end)
    {
        static bool Cut(char a, char b) => char.IsLetterOrDigit(a) && char.IsLetterOrDigit(b);

        if (start > 0 && start < text.Length && Cut(text[start - 1], text[start]))
        {
            return false;
        }

        return !(end > 0 && end < text.Length && Cut(text[end - 1], text[end]));
    }

    private static string? NullableString(JsonElement parent, string name, string path)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : throw Invalid($"{path}.{name}");
    }

    private static bool RequireBool(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw Invalid(name);

    private static ArgumentException Invalid(string member) =>
        new($"The segmentation is not a segment_clauses result: '{member}' is missing or malformed.", "segmentationJson");
}
