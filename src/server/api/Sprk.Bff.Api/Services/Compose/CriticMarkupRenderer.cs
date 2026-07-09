using System.Text;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-22 (task 023): renders existing track-changes (insertion/deletion/comment spans) as
/// inline CriticMarkup markers — <c>{++insertion++}</c> / <c>{--deletion--}</c> /
/// <c>{&gt;&gt;comment&lt;&lt;}</c> — so the LLM can SEE prior edits before proposing new
/// ones (design §6.1; <c>.claude/adr/ADR-040-session-ledger.md</c> asymmetric read/write
/// pattern).
/// </summary>
/// <remarks>
/// <para>
/// <b>READ DIRECTION ONLY (ADR-040 / bff-extensions.md "CriticMarkup role")</b>: this class
/// is a pure display transform. The LLM reads the rendered CriticMarkup but NEVER emits
/// it — the platform's only write path is the structured edit payload
/// (<c>{target_text, new_text, comment}</c>, FR-19/20). <see cref="Render"/> takes a body
/// string + a list of <see cref="TrackChangeSpan"/> and returns a <see cref="string"/>. It
/// calls no model, injects no AI-internal type, and adds no endpoint (ADR-013).
/// </para>
/// <para>
/// <b>Contract</b>: <paramref name="bodyText"/> in <see cref="Render"/> is read against the
/// CURRENT (post-edit) document state. For <see cref="TrackChangeKind.Insertion"/> and
/// <see cref="TrackChangeKind.Comment"/> spans, <see cref="TrackChangeSpan.Text"/> MUST
/// match <c>bodyText[StartOffset..StartOffset+Text.Length]</c> exactly (the span anchors an
/// existing substring — insertions are wrapped in place; comments are anchored in place).
/// For <see cref="TrackChangeKind.Deletion"/> spans, <see cref="TrackChangeSpan.Text"/> is
/// text that is NO LONGER in <paramref name="bodyText"/> (it was removed) — the renderer
/// re-inserts it, wrapped, at <see cref="TrackChangeSpan.StartOffset"/> so the LLM can see
/// what was removed without the deleted text being live content.
/// </para>
/// </remarks>
public sealed class CriticMarkupRenderer
{
    /// <summary>
    /// Renders <paramref name="spans"/> onto <paramref name="bodyText"/> as inline
    /// CriticMarkup. Returns <paramref name="bodyText"/> unchanged when there are no spans
    /// (EDGE-5: a document with no track-changes yields plain text with no CriticMarkup).
    /// </summary>
    public string Render(string bodyText, IReadOnlyList<TrackChangeSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(bodyText);
        ArgumentNullException.ThrowIfNull(spans);

        if (spans.Count == 0)
        {
            return bodyText;
        }

        foreach (var span in spans)
        {
            ValidateSpan(bodyText, span);
        }

        // EDGE-6: spans are applied strictly in descending StartOffset order so earlier
        // (lower-offset) insertions into the output string never shift the offsets of
        // spans still to be processed — the same "apply bottom-up" idiom used elsewhere
        // in Compose for batched text edits (bff-extensions.md "4-phase batch pipeline").
        // Ties at the same StartOffset are processed in the order supplied by the caller
        // (stable sort) — callers that anchor a Comment at the same offset as an
        // Insertion/Deletion should order them deliberately; this renderer does not
        // second-guess anchor ordering.
        var ordered = spans
            .Select((span, index) => (span, index))
            .OrderByDescending(x => x.span.StartOffset)
            .ThenByDescending(x => x.index)
            .ToList();

        var sb = new StringBuilder(bodyText);

        foreach (var (span, _) in ordered)
        {
            switch (span.Kind)
            {
                case TrackChangeKind.Insertion:
                    // Wrap the already-present substring in place: {++inserted++}.
                    sb.Remove(span.StartOffset, span.Text.Length);
                    sb.Insert(span.StartOffset, $"{{++{span.Text}++}}");
                    break;

                case TrackChangeKind.Deletion:
                    // The deleted text is no longer live content — re-insert it, wrapped,
                    // purely for LLM visibility: {--removed--}.
                    sb.Insert(span.StartOffset, $"{{--{span.Text}--}}");
                    break;

                case TrackChangeKind.Comment:
                    // Leave the anchored text as-is; append the comment marker right after it.
                    sb.Insert(span.StartOffset + span.Text.Length, $"{{>>{span.CommentText}<<}}");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(spans), span.Kind, "Unknown TrackChangeKind.");
            }
        }

        return sb.ToString();
    }

    private static void ValidateSpan(string bodyText, TrackChangeSpan span)
    {
        if (span.StartOffset < 0 || span.StartOffset > bodyText.Length)
        {
            throw new ArgumentException(
                $"TrackChangeSpan.StartOffset {span.StartOffset} is out of range for a body of length {bodyText.Length}.",
                nameof(span));
        }

        switch (span.Kind)
        {
            case TrackChangeKind.Insertion:
            case TrackChangeKind.Comment:
                if (span.StartOffset + span.Text.Length > bodyText.Length
                    || !string.Equals(
                        bodyText.Substring(span.StartOffset, span.Text.Length),
                        span.Text,
                        StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"TrackChangeSpan ({span.Kind}) text \"{span.Text}\" does not match bodyText at offset {span.StartOffset}.",
                        nameof(span));
                }
                break;

            case TrackChangeKind.Deletion:
                // Deleted text is, by definition, absent from bodyText — only the insertion
                // point needs to be in range (already checked above).
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(span), span.Kind, "Unknown TrackChangeKind.");
        }

        if (span.Kind == TrackChangeKind.Comment && string.IsNullOrEmpty(span.CommentText))
        {
            throw new ArgumentException(
                "TrackChangeSpan.CommentText is required when Kind is Comment.",
                nameof(span));
        }
    }
}

/// <summary>
/// The kind of existing track-change a <see cref="TrackChangeSpan"/> represents.
/// </summary>
public enum TrackChangeKind
{
    Insertion,
    Deletion,
    Comment,
}

/// <summary>
/// A single existing track-change to render as inline CriticMarkup (read direction only —
/// see <see cref="CriticMarkupRenderer"/> remarks). <see cref="Text"/> means different
/// things per <see cref="Kind"/>: for <see cref="TrackChangeKind.Insertion"/> and
/// <see cref="TrackChangeKind.Comment"/> it is the substring already present in the body at
/// <see cref="StartOffset"/>; for <see cref="TrackChangeKind.Deletion"/> it is the removed
/// text to be re-inserted (wrapped) for LLM visibility.
/// </summary>
public sealed record TrackChangeSpan(
    TrackChangeKind Kind,
    int StartOffset,
    string Text,
    string? CommentText = null);
