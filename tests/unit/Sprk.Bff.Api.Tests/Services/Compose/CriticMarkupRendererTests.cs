using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="CriticMarkupRenderer"/> (FR-22, task 023) — the CriticMarkup
/// READ-DIRECTION-ONLY display transform (ADR-040; <c>bff-extensions.md</c> "CriticMarkup role").
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>domain-logic</c> — <see cref="CriticMarkupRenderer"/> is
/// a pure text transformer with no I/O and no collaborators to mock.
/// </para>
/// </summary>
public class CriticMarkupRendererTests
{
    private readonly CriticMarkupRenderer _sut = new();

    [Fact]
    public void Render_WithNoSpans_ReturnsBodyTextUnchanged()
    {
        // EDGE-5: a document with no track-changes yields plain text with no CriticMarkup.
        var body = "The quick brown fox jumps.";

        var result = _sut.Render(body, Array.Empty<TrackChangeSpan>());

        result.Should().Be(body);
    }

    [Fact]
    public void Render_WithInsertionSpan_WrapsExistingTextInPlace()
    {
        var body = "The quick brown fox jumps.";
        var spans = new[]
        {
            new TrackChangeSpan(TrackChangeKind.Insertion, StartOffset: 4, Text: "quick"),
        };

        var result = _sut.Render(body, spans);

        result.Should().Be("The {++quick++} brown fox jumps.");
    }

    [Fact]
    public void Render_WithDeletionSpan_InsertsWrappedDeletedTextAtOffset()
    {
        // The deleted text ("quick") is NO LONGER in the current body — the renderer
        // re-inserts it, wrapped, purely for LLM visibility.
        var body = "The brown fox jumps.";
        var spans = new[]
        {
            new TrackChangeSpan(TrackChangeKind.Deletion, StartOffset: 4, Text: "quick"),
        };

        var result = _sut.Render(body, spans);

        result.Should().Be("The {--quick--}brown fox jumps.");
    }

    [Fact]
    public void Render_WithCommentSpan_AppendsCommentMarkerRightAfterAnchor()
    {
        var body = "Pay within 30 days.";
        var spans = new[]
        {
            new TrackChangeSpan(
                TrackChangeKind.Comment,
                StartOffset: 4,
                Text: "within 30 days",
                CommentText: "Confirm days with legal"),
        };

        var result = _sut.Render(body, spans);

        result.Should().Be("Pay within 30 days{>>Confirm days with legal<<}.");
    }

    [Fact]
    public void Render_WithMultipleSpansAtDifferentOffsets_AppliesAllWithoutOffsetCorruption()
    {
        // Combines all three kinds in one call; verifies the descending-offset apply order
        // (EDGE-6) leaves earlier offsets valid for spans still to be processed.
        var body = "Alpha Beta Gamma Delta.";
        var spans = new[]
        {
            new TrackChangeSpan(TrackChangeKind.Comment, StartOffset: 0, Text: "Alpha", CommentText: "note1"),
            new TrackChangeSpan(TrackChangeKind.Insertion, StartOffset: 11, Text: "Gamma"),
            new TrackChangeSpan(TrackChangeKind.Deletion, StartOffset: 17, Text: "Zeta "),
        };

        var result = _sut.Render(body, spans);

        result.Should().Be("Alpha{>>note1<<} Beta {++Gamma++} {--Zeta --}Delta.");
    }

    [Fact]
    public void Render_WithInsertionTextMismatchingBody_ThrowsArgumentException()
    {
        var body = "The quick brown fox jumps.";
        var spans = new[]
        {
            new TrackChangeSpan(TrackChangeKind.Insertion, StartOffset: 4, Text: "slow"),
        };

        var act = () => _sut.Render(body, spans);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Render_WithCommentSpanMissingCommentText_ThrowsArgumentException()
    {
        var body = "Pay within 30 days.";
        var spans = new[]
        {
            new TrackChangeSpan(TrackChangeKind.Comment, StartOffset: 4, Text: "within 30 days"),
        };

        var act = () => _sut.Render(body, spans);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Render_WithOutOfRangeStartOffset_ThrowsArgumentException()
    {
        var body = "Short body.";
        var spans = new[]
        {
            new TrackChangeSpan(TrackChangeKind.Insertion, StartOffset: 999, Text: "x"),
        };

        var act = () => _sut.Render(body, spans);

        act.Should().Throw<ArgumentException>();
    }
}
