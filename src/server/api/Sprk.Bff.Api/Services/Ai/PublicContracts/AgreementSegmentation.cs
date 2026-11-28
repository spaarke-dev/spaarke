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
/// <param name="Text">The paragraph's visible text, stripped: what the model is shown and quotes from.</param>
/// <param name="VisibleText">
/// <c>anchor_text.visible</c>: the raw text the redline engine checks a quote's uniqueness in (<c>Redliner.visible_text</c>: not
/// deleted or moved away; whitespace not collapsed). Null only for a section with no <c>para_key</c>.
/// </param>
/// <param name="EditableText">
/// <c>anchor_text.editable</c>: the raw text the engine places an edit in (<c>Redliner.text</c>: the paragraph's own runs, outside
/// any existing revision, hyperlink or content control). Null only for a section with no <c>para_key</c>.
/// </param>
internal sealed record AgreementSection(
    int Index, string? Number, string? Heading, string? ParaKey, string Text, string? VisibleText = null, string? EditableText = null);

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
/// <b>The pre-check runs the engine's own anchor rules on the engine's own texts</b> (<c>ooxml_redline.py</c>: <c>_apply</c>,
/// <c>_locate</c>, <c>_span</c>, <c>word_edge_ok</c>), so an edit the engine would refuse for an anchor reason is refused here first,
/// with the engine's code, and can be handed back for one rewrite. The texts come from <c>segment_clauses.py --anchor-text</c>
/// (<see cref="AgreementSection.VisibleText"/>, <see cref="AgreementSection.EditableText"/>): raw, so a tab in the clause and a space
/// in the quote are different characters, as they are to the engine (<c>_locate</c> does not collapse whitespace).
/// </para>
/// <para>
/// <b>Edits are checked in plan order against the clause as the earlier kept edits leave it</b> (<see cref="AnchorChecker"/>), because
/// the engine applies them one after another: a replaced span is no longer visible and an inserted one is visible but not editable, so
/// a later quote that overlaps an earlier edit's words is no longer in the clause (live run 2026-11-26, task 004 notes: D2).
/// </para>
/// <para>
/// The engine stays the final authority on what the segmentation does not describe: fields and field results, drawings, the exact
/// position of earlier reviewers' markup inside a clause, and a <c>para</c> key that an inserted text repeats in another paragraph
/// (task 004 notes, known limits).
/// </para>
/// </remarks>
internal sealed class AgreementSegmentation
{
    /// <summary>
    /// The engine's <c>LEAD_RE</c> (<c>ooxml_redline.py</c>): a leading "5." / "5.1" / "(a)" / "A." / "Section 5" plus an optional
    /// "Heading.", which <c>find "*"</c> keeps. .NET and Python agree on every construct used here.
    /// </summary>
    private static readonly Regex Lead = new(
        @"^\s*(?:(?:section|article|clause)\s+)?(?:\d+(?:\.\d+)*\.?|\([a-z0-9]{1,4}\)|[A-Z]\.)\s+(?:[A-Z][^.:]{0,60}[.:]\s+)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

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

                // An editable section must carry the engine's texts (segment_clauses.py --anchor-text): without them the pre-check
                // could only guess at what the engine searches, so a segmentation without them is refused, never checked loosely.
                string? visible = null, editable = null;
                if (paraKey is not null)
                {
                    if (!item.TryGetProperty("anchor_text", out var anchor)
                        || anchor.ValueKind != JsonValueKind.Object
                        || !anchor.TryGetProperty("visible", out var visibleElement) || visibleElement.ValueKind != JsonValueKind.String
                        || !anchor.TryGetProperty("editable", out var editableElement) || editableElement.ValueKind != JsonValueKind.String)
                    {
                        throw Invalid($"{path}.anchor_text");
                    }

                    visible = visibleElement.GetString();
                    editable = editableElement.GetString();
                }

                sections.Add(new AgreementSection(
                    index, NullableString(item, "number", path), NullableString(item, "heading", path), paraKey, textElement.GetString()!,
                    visible, editable));
            }

            return new AgreementSegmentation(
                sections,
                RequireBool(root, "has_tracked_changes"),
                RequireBool(root, "has_comments"),
                RequireBool(root, "has_hidden_text"));
        }
    }

    /// <summary>
    /// Starts the anchor check of one plan. Edits are checked in plan order, each against the clause as the edits kept before it leave
    /// it, as the engine applies them.
    /// </summary>
    public AnchorChecker BeginPlan() => new(this);

    /// <summary>
    /// The exact-match anchor pre-check of one plan's edits, in plan order. <see cref="Check"/> returns the engine's refusal code for an
    /// edit whose anchors do not fit, or null; an edit that fits changes the checker's copy of its clause the way the engine's tracked
    /// change does, so the next edit is checked against that. The model's strings are compared as they are: nothing is trimmed,
    /// case-folded, whitespace-collapsed or approximated, and only the named clause is searched.
    /// </summary>
    internal sealed class AnchorChecker
    {
        private readonly AgreementSegmentation _segmentation;
        private readonly Dictionary<string, ClauseState> _clauses = new(StringComparer.Ordinal);

        internal AnchorChecker(AgreementSegmentation segmentation) => _segmentation = segmentation;

        /// <summary>
        /// Checks one edit (its members as the model wrote them, null when unused). On success the edit is recorded as applied.
        /// </summary>
        /// <returns>One of <see cref="AgreementRefusalCodes"/>, or null when the edit fits.</returns>
        public string? Check(string para, string? find, string? old, string? replace, string? after, string? insert)
        {
            if (!_segmentation._byParaKey.TryGetValue(para, out var section))
            {
                return AgreementRefusalCodes.ParaNotFound;
            }

            if (find is null && after is null)
            {
                throw new ArgumentException("An edit has find or after; the assembler checks its shape first.", nameof(find));
            }

            if (!_clauses.TryGetValue(para, out var clause))
            {
                clause = new ClauseState(section.VisibleText!, section.EditableText!, []);
            }

            var refusal = after is not null
                ? clause.Insert(after, insert ?? string.Empty, out var next)
                : clause.Replace(find!, old, replace, out next);
            if (refusal is null)
            {
                _clauses[para] = next;
            }

            return refusal;
        }
    }

    /// <summary>
    /// One clause as the engine sees it after the plan's earlier edits: <see cref="Visible"/> (where a quote must occur exactly once),
    /// <see cref="Editable"/> (the paragraph's own runs, where the edit is placed) and the offsets in <see cref="Editable"/> where an
    /// earlier edit's tracked insertion or deletion now sits between runs (a later replaced span may not cross one).
    /// </summary>
    private sealed record ClauseState(string Visible, string Editable, IReadOnlyList<int> MarkupPoints)
    {
        /// <summary>The engine's pure insertion: <c>after</c> is located, <c>insert</c> becomes visible after it, nothing editable changes.</summary>
        public string? Insert(string after, string insert, out ClauseState next)
        {
            next = this;
            var refusal = Locate(after, out var start);
            if (refusal is not null)
            {
                return refusal;
            }

            var visibleAt = Visible.IndexOf(after, StringComparison.Ordinal) + after.Length;
            var point = start + after.Length;
            next = new ClauseState(Visible.Insert(visibleAt, insert), Editable, [.. MarkupPoints.Where(p => p != point), point]);
            return null;
        }

        /// <summary>
        /// The engine's replacement, deletion or comment-only edit: <c>find</c> ("*" = the clause after its number and heading) is
        /// located, <c>old</c> (default: all of <c>find</c>) is the span changed or commented on.
        /// </summary>
        public string? Replace(string find, string? old, string? replace, out ClauseState next)
        {
            next = this;
            var findText = find;
            if (find == "*")
            {
                var lead = Lead.Match(Editable);
                findText = lead.Success ? Editable[lead.Length..] : Editable;
                if (string.IsNullOrWhiteSpace(findText))
                {
                    return AgreementRefusalCodes.NothingAfterHeading;
                }
            }

            var refusal = Locate(findText, out var findStart);
            if (refusal is not null)
            {
                return refusal;
            }

            var oldText = old ?? findText;
            if (CountOccurrences(findText, oldText) != 1)
            {
                return AgreementRefusalCodes.OldNotInFind;
            }

            var offset = findText.IndexOf(oldText, StringComparison.Ordinal);
            var start = findStart + offset;
            var end = start + oldText.Length;
            if (!WordEdgeOk(Editable, start, end))
            {
                return AgreementRefusalCodes.WordBoundary;
            }

            if (MarkupPoints.Any(p => p > start && p < end))
            {
                return AgreementRefusalCodes.CrossesMarkup;
            }

            if (replace is null)
            {
                return null; // comment-only: the comment marks sit outside the runs, and no text changes
            }

            // The old words go into a tracked deletion (neither visible nor editable) and the new ones into a tracked insertion (visible,
            // not editable); both sit at 'start' in the editable text from now on.
            var visibleStart = Visible.IndexOf(findText, StringComparison.Ordinal) + offset;
            var points = MarkupPoints.Select(p => p >= end ? p - oldText.Length : p).Where(p => p != start).Append(start).ToList();
            next = new ClauseState(
                Visible.Remove(visibleStart, oldText.Length).Insert(visibleStart, replace),
                Editable.Remove(start, oldText.Length),
                points);
            return null;
        }

        /// <summary>
        /// The engine's <c>_locate</c>: exactly once in the visible text (else <c>not_found</c> / <c>not_unique</c>), exactly once in the
        /// editable text (else the quote touches earlier reviewers' markup: <c>in_existing_markup</c>), on whole words.
        /// </summary>
        private string? Locate(string needle, out int start)
        {
            start = -1;
            var visible = CountOccurrences(Visible, needle);
            if (visible == 0)
            {
                return AgreementRefusalCodes.NotFound;
            }

            if (visible > 1)
            {
                return AgreementRefusalCodes.NotUnique;
            }

            if (CountOccurrences(Editable, needle) != 1)
            {
                return AgreementRefusalCodes.InExistingMarkup;
            }

            start = Editable.IndexOf(needle, StringComparison.Ordinal);
            return WordEdgeOk(Editable, start, start + needle.Length) ? null : AgreementRefusalCodes.WordBoundary;
        }
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

    /// <summary>
    /// The engine's <c>word_edge_ok</c>: the span [start, end) must not cut a word at either end. Python's <c>isalnum</c> is a letter or
    /// a numeric character.
    /// </summary>
    private static bool WordEdgeOk(string text, int start, int end)
    {
        static bool IsAlnum(char c) => char.IsLetter(c) || char.IsNumber(c);
        static bool Cut(char a, char b) => IsAlnum(a) && IsAlnum(b);

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
