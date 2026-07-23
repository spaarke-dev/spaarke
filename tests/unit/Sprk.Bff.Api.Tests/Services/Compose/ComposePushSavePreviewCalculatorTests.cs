// FR-28 (task 055) — unit tests for ComposePushSavePreviewCalculator, the pure Tier-2c preview
// computation (comment/track-change counts + the Word-vs-Compose split).
//
// ADR-038 KEEP category: domain-logic — a pure (annotations + int) -> record transform with no
// I/O and no collaborators to mock. Each test names a concrete production behavior (a specific
// count combination) that breaks if the test is deleted.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public class ComposePushSavePreviewCalculatorTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 10, 9, 0, 0, TimeSpan.Zero);

    private static DocxAnnotation Comment(string target = "t") => new()
    { Kind = TrackChangeKind.Comment, TargetText = target, CommentText = "c", Author = "AI", Date = When };

    private static DocxAnnotation Insertion(string? target = "t") => new()
    { Kind = TrackChangeKind.Insertion, TargetText = target, NewText = "n", Author = "AI", Date = When };

    private static DocxAnnotation Deletion(string target = "t") => new()
    { Kind = TrackChangeKind.Deletion, TargetText = target, Author = "AI", Date = When };

    [Fact]
    public void Compute_WithMixedAnnotations_ReturnsPerKindCountsAndTrackChangeTotal()
    {
        var batch = new List<DocxAnnotation> { Comment(), Comment(), Insertion(), Deletion(), Deletion() };

        var preview = ComposePushSavePreviewCalculator.Compute(batch);

        preview.CommentCount.Should().Be(2);
        preview.InsertionCount.Should().Be(1);
        preview.DeletionCount.Should().Be(2);
        preview.TrackChangeCount.Should().Be(3, "insertions + deletions == the track-change half of the split");
        preview.WordBoundCount.Should().Be(5, "every entry in the batch materializes as native OOXML markup");
    }

    [Fact]
    public void Compute_WithEmptyBatch_ReturnsAllZeroCounts()
    {
        var preview = ComposePushSavePreviewCalculator.Compute(Array.Empty<DocxAnnotation>());

        preview.CommentCount.Should().Be(0);
        preview.InsertionCount.Should().Be(0);
        preview.DeletionCount.Should().Be(0);
        preview.WordBoundCount.Should().Be(0);
        preview.ComposeOnlyCount.Should().Be(0);
    }

    [Fact]
    public void Compute_WithComposeOnlyCount_CarriesItThroughUnchanged()
    {
        var preview = ComposePushSavePreviewCalculator.Compute(new[] { Comment() }, composeOnlyCount: 4);

        preview.ComposeOnlyCount.Should().Be(4, "the Compose-only count reflects session DefinedTermsTracking, not the pushed batch");
    }

    [Fact]
    public void Compute_WithNegativeComposeOnlyCount_Throws()
    {
        var act = () => ComposePushSavePreviewCalculator.Compute(new[] { Comment() }, composeOnlyCount: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Compute_WithNullAnnotations_Throws()
    {
        var act = () => ComposePushSavePreviewCalculator.Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
