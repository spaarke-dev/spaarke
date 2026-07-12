using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-28 (task 055) — the Tier-2c push/save preview payload: deterministic counts of the
/// Compose annotations about to be (or that were just) materialized as native Word markup, plus
/// the "Word-vs-Compose" split the gate dialog's preview slot will render (design.md §2.4 /
/// HANDOFF-core-r2-A0-contract-requirements.md §4: "what will appear in Word vs what stays in
/// Compose only"). Pure data — no rendering happens here or anywhere in this task; the gate
/// dialog that hosts this payload waits on core Phase A0 <c>GateDecision v2</c>.
/// </summary>
/// <remarks>
/// Computed by <see cref="ComposePushSavePreviewCalculator.Compute"/>, which is called from two
/// sites sharing one source of truth: <see cref="ComposeService.PreviewPushAnnotationsAsync"/>
/// (a pre-confirm, non-mutating call the future gate dialog invokes before the user commits) and
/// <see cref="ComposeService.PushAnnotationsAsync"/> (attached to <see cref="PushAnnotationsResult"/>
/// as post-write completion evidence for the future job-aware OutcomeCard).
/// </remarks>
public sealed record ComposePushSavePreview
{
    /// <summary>Count of <see cref="TrackChangeKind.Comment"/> annotations — each renders as a
    /// native <c>w:comment</c> in Word.</summary>
    [JsonPropertyName("commentCount")]
    public required int CommentCount { get; init; }

    /// <summary>Count of <see cref="TrackChangeKind.Insertion"/> annotations (native <c>w:ins</c>).</summary>
    [JsonPropertyName("insertionCount")]
    public required int InsertionCount { get; init; }

    /// <summary>Count of <see cref="TrackChangeKind.Deletion"/> annotations (native <c>w:del</c>).</summary>
    [JsonPropertyName("deletionCount")]
    public required int DeletionCount { get; init; }

    /// <summary>Insertions + deletions — the "track changes" half of the comment/track-change
    /// count the gate dialog's preview shows.</summary>
    [JsonPropertyName("trackChangeCount")]
    public int TrackChangeCount => InsertionCount + DeletionCount;

    /// <summary>
    /// Count of annotations that WILL appear (or did appear) in Word — the size of the annotation
    /// batch being pushed. Every entry in a push request materializes as native OOXML markup, so
    /// this always equals the input annotation count.
    /// </summary>
    [JsonPropertyName("wordBoundCount")]
    public required int WordBoundCount { get; init; }

    /// <summary>
    /// Count of Compose-domain items that have NO Word-native representation and therefore NEVER
    /// leave Compose — currently the session's <c>DefinedTermsTracking</c> (FR-29, design.md §8):
    /// defined-term tracking has no OOXML equivalent, unlike anchored annotations (which map 1:1
    /// onto the <c>DocxAnnotation</c> push payload). Zero when the caller supplies no session
    /// context — a graceful degrade (a valid, if incomplete, preview), never a computation failure.
    /// </summary>
    [JsonPropertyName("composeOnlyCount")]
    public required int ComposeOnlyCount { get; init; }
}

/// <summary>
/// Pure, deterministic calculator for <see cref="ComposePushSavePreview"/>. No I/O, no AI
/// (ADR-013 / NFR-05) — categorizes an already-in-hand annotation batch by
/// <see cref="TrackChangeKind"/> and combines it with a caller-supplied Compose-only count.
/// </summary>
public static class ComposePushSavePreviewCalculator
{
    /// <param name="annotationsToPush">The annotation batch about to be (or that was) rendered
    /// into native OOXML markup. Required, non-null (may be empty — an empty batch is a valid,
    /// all-zero preview, matching <see cref="DocxAnnotationWriter.Annotate"/>'s own empty-batch
    /// tolerance).</param>
    /// <param name="composeOnlyCount">The count of Compose-domain items with no Word-native
    /// representation (see <see cref="ComposePushSavePreview.ComposeOnlyCount"/> remarks).
    /// Defaults to 0 for callers with no session context.</param>
    public static ComposePushSavePreview Compute(
        IReadOnlyList<DocxAnnotation> annotationsToPush,
        int composeOnlyCount = 0)
    {
        ArgumentNullException.ThrowIfNull(annotationsToPush);
        if (composeOnlyCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(composeOnlyCount), composeOnlyCount,
                "composeOnlyCount cannot be negative.");
        }

        var comments = 0;
        var insertions = 0;
        var deletions = 0;

        foreach (var annotation in annotationsToPush)
        {
            switch (annotation.Kind)
            {
                case TrackChangeKind.Comment:
                    comments++;
                    break;
                case TrackChangeKind.Insertion:
                    insertions++;
                    break;
                case TrackChangeKind.Deletion:
                    deletions++;
                    break;
            }
        }

        return new ComposePushSavePreview
        {
            CommentCount = comments,
            InsertionCount = insertions,
            DeletionCount = deletions,
            WordBoundCount = annotationsToPush.Count,
            ComposeOnlyCount = composeOnlyCount,
        };
    }
}
