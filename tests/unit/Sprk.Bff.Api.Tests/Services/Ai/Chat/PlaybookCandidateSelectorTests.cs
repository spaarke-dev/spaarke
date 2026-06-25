using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;
using static Sprk.Bff.Api.Services.Ai.Chat.PlaybookDispatcher;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="PlaybookCandidateSelector"/>
/// (chat-routing-redesign-r1 task 113R — FR-47 confidence top-N selector,
/// FR-48 no-auto-execute invariant).
///
/// <para>
/// Coverage matrix:
/// <list type="bullet">
///   <item><description>Empty input → <c>NoMatch</c> sentinel; no rerank.</description></item>
///   <item><description>Single high-confidence candidate, no top-2 → 1 candidate, no rerank, <c>high-confidence-single</c>.</description></item>
///   <item><description>High top-1 with clear gap to top-2 → high-confidence-single; no rerank.</description></item>
///   <item><description>High top-1 with close top-2 (within delta margin) → <c>ambiguous-top-2-within-margin</c>; rerank recommended.</description></item>
///   <item><description>Top-1 below ConfidenceThreshold → <c>ambiguous-below-threshold</c>; rerank recommended.</description></item>
///   <item><description>All candidates below SecondaryThreshold → empty + <c>no-match</c>.</description></item>
///   <item><description>5+ candidates → capped at MaxCandidates (3).</description></item>
///   <item><description>Multi-file: same playbook in two files → MAX confidence wins; ContributingFileCount=2.</description></item>
///   <item><description>Candidates ordered by confidence descending.</description></item>
///   <item><description>FR-48 invariant: result shape contains no <c>auto-execute</c> property.</description></item>
/// </list>
/// </para>
/// </summary>
public class PlaybookCandidateSelectorTests
{
    private static readonly string PlaybookA = "11111111-aaaa-bbbb-cccc-111111111111";
    private static readonly string PlaybookB = "22222222-aaaa-bbbb-cccc-222222222222";
    private static readonly string PlaybookC = "33333333-aaaa-bbbb-cccc-333333333333";
    private static readonly string PlaybookD = "44444444-aaaa-bbbb-cccc-444444444444";
    private static readonly string PlaybookE = "55555555-aaaa-bbbb-cccc-555555555555";

    /// <summary>
    /// Builds a selector with default FR-47 thresholds
    /// (Confidence=0.85, Secondary=0.80, DeltaMargin=0.05, MaxCandidates=3).
    /// </summary>
    private static PlaybookCandidateSelector CreateSelector(PlaybookSelectorOptions? overrideOpts = null)
    {
        var opts = overrideOpts ?? new PlaybookSelectorOptions();
        return new PlaybookCandidateSelector(
            Options.Create(opts),
            NullLogger<PlaybookCandidateSelector>.Instance);
    }

    /// <summary>
    /// Builds a single Phase B per-file result with the given candidate
    /// (playbookId, score) tuples. Tuples are converted into
    /// <see cref="PlaybookSearchResult"/> entries with stub required fields.
    /// </summary>
    private static PhaseBPerFileResult File(
        string fileId,
        params (string PlaybookId, string Name, double Score)[] hits)
    {
        var candidates = hits
            .Select(h => new PlaybookSearchResult
            {
                PlaybookId = h.PlaybookId,
                PlaybookName = h.Name,
                Description = $"desc-{h.Name}",
                RecordType = "sprk_matter",
                EntityType = "matter",
                Score = h.Score,
            })
            .ToList();

        return new PhaseBPerFileResult(
            FileId: fileId,
            Filename: $"{fileId}.pdf",
            ManifestPresent: false,
            Candidates: candidates,
            LatencyMs: 42);
    }

    // ────────────────────────────────────────────────────────────────
    // Step 1: empty / no-match paths
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_EmptyInput_ReturnsNoMatch()
    {
        var selector = CreateSelector();

        var result = selector.Select(Array.Empty<PhaseBPerFileResult>());

        result.TopCandidates.Should().BeEmpty();
        result.RerankRecommended.Should().BeFalse();
        result.Reason.Should().Be("no-match");
    }

    [Fact]
    public void Select_FilesWithNoCandidates_ReturnsNoMatch()
    {
        var selector = CreateSelector();
        var input = new[]
        {
            File("f1"),
            File("f2"),
        };

        var result = selector.Select(input);

        result.TopCandidates.Should().BeEmpty();
        result.RerankRecommended.Should().BeFalse();
        result.Reason.Should().Be("no-match");
    }

    [Fact]
    public void Select_AllCandidatesBelowSecondaryThreshold_ReturnsNoMatch()
    {
        var selector = CreateSelector();
        var input = new[]
        {
            File("f1",
                (PlaybookA, "Playbook A", 0.70),
                (PlaybookB, "Playbook B", 0.65),
                (PlaybookC, "Playbook C", 0.50)),
        };

        var result = selector.Select(input);

        result.TopCandidates.Should().BeEmpty();
        result.RerankRecommended.Should().BeFalse();
        result.Reason.Should().Be("no-match");
    }

    // ────────────────────────────────────────────────────────────────
    // Step 2: high-confidence single path
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_SingleHighConfidenceCandidate_NoTop2_ReturnsHighConfidenceSingle()
    {
        var selector = CreateSelector();
        var input = new[]
        {
            File("f1", (PlaybookA, "Playbook A", 0.90)),
        };

        var result = selector.Select(input);

        result.TopCandidates.Should().HaveCount(1);
        result.TopCandidates[0].PlaybookId.Should().Be(PlaybookA);
        result.TopCandidates[0].Confidence.Should().Be(0.90);
        result.RerankRecommended.Should().BeFalse();
        result.Reason.Should().Be("high-confidence-single");
    }

    [Fact]
    public void Select_HighTop1_LowTop2_GapAboveMargin_ReturnsHighConfidenceSingle()
    {
        var selector = CreateSelector();
        // Gap = 0.90 - 0.82 = 0.08 > 0.05 default margin → not ambiguous.
        var input = new[]
        {
            File("f1",
                (PlaybookA, "Playbook A", 0.90),
                (PlaybookB, "Playbook B", 0.82)),
        };

        var result = selector.Select(input);

        result.TopCandidates.Should().HaveCount(2);
        result.TopCandidates[0].PlaybookId.Should().Be(PlaybookA);
        result.TopCandidates[1].PlaybookId.Should().Be(PlaybookB);
        result.RerankRecommended.Should().BeFalse();
        result.Reason.Should().Be("high-confidence-single");
    }

    // ────────────────────────────────────────────────────────────────
    // Step 3: ambiguous paths (rerank recommended)
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_HighTop1_CloseTop2_WithinMargin_FlagsAmbiguous()
    {
        var selector = CreateSelector();
        // Gap = 0.90 - 0.87 = 0.03 ≤ 0.05 → ambiguous, rerank recommended.
        var input = new[]
        {
            File("f1",
                (PlaybookA, "Playbook A", 0.90),
                (PlaybookB, "Playbook B", 0.87),
                (PlaybookC, "Playbook C", 0.82)),
        };

        var result = selector.Select(input);

        result.TopCandidates.Should().HaveCount(3);
        result.RerankRecommended.Should().BeTrue();
        result.Reason.Should().Be("ambiguous-top-2-within-margin");
    }

    [Fact]
    public void Select_Top1BelowConfidenceThreshold_FlagsAmbiguous()
    {
        var selector = CreateSelector();
        // Top-1 = 0.83 < 0.85 → ambiguous regardless of gap.
        var input = new[]
        {
            File("f1",
                (PlaybookA, "Playbook A", 0.83),
                (PlaybookB, "Playbook B", 0.81)),
        };

        var result = selector.Select(input);

        result.TopCandidates.Should().HaveCount(2);
        result.RerankRecommended.Should().BeTrue();
        result.Reason.Should().Be("ambiguous-below-threshold");
    }

    // ────────────────────────────────────────────────────────────────
    // Step 4: max-candidates cap + ordering
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_FivePlusCandidates_CappedAtMaxCandidates()
    {
        var selector = CreateSelector();
        var input = new[]
        {
            File("f1",
                (PlaybookA, "Playbook A", 0.92),
                (PlaybookB, "Playbook B", 0.88),
                (PlaybookC, "Playbook C", 0.85),
                (PlaybookD, "Playbook D", 0.83),
                (PlaybookE, "Playbook E", 0.81)),
        };

        var result = selector.Select(input);

        result.TopCandidates.Should().HaveCount(3);
        result.TopCandidates.Select(c => c.PlaybookId)
            .Should().ContainInOrder(PlaybookA, PlaybookB, PlaybookC);
    }

    [Fact]
    public void Select_Candidates_AreOrderedByConfidenceDescending()
    {
        var selector = CreateSelector();
        // Deliberately unordered input.
        var input = new[]
        {
            File("f1",
                (PlaybookB, "Playbook B", 0.83),
                (PlaybookA, "Playbook A", 0.91),
                (PlaybookC, "Playbook C", 0.87)),
        };

        var result = selector.Select(input);

        result.TopCandidates.Should().HaveCount(3);
        result.TopCandidates[0].Confidence.Should().BeGreaterThan(result.TopCandidates[1].Confidence);
        result.TopCandidates[1].Confidence.Should().BeGreaterThan(result.TopCandidates[2].Confidence);
        result.TopCandidates[0].PlaybookId.Should().Be(PlaybookA);
        result.TopCandidates[1].PlaybookId.Should().Be(PlaybookC);
        result.TopCandidates[2].PlaybookId.Should().Be(PlaybookB);
    }

    // ────────────────────────────────────────────────────────────────
    // Step 5: multi-file aggregation
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_MultiFile_SamePlaybook_UsesMaxConfidence_AndCountsBothFiles()
    {
        var selector = CreateSelector();
        // PlaybookA scores 0.82 in file 1 and 0.91 in file 2.
        // Expect Confidence=0.91 (max), ContributingFileCount=2.
        var input = new[]
        {
            File("f1",
                (PlaybookA, "Playbook A", 0.82),
                (PlaybookB, "Playbook B", 0.81)),
            File("f2",
                (PlaybookA, "Playbook A", 0.91),
                (PlaybookC, "Playbook C", 0.83)),
        };

        var result = selector.Select(input);

        var playbookA = result.TopCandidates.Single(c => c.PlaybookId == PlaybookA);
        playbookA.Confidence.Should().Be(0.91);
        playbookA.ContributingFileCount.Should().Be(2);
    }

    // ────────────────────────────────────────────────────────────────
    // Step 6: FR-48 invariant — result shape carries NO auto-execute flag
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void FR48_PlaybookCandidateSelection_HasNoAutoExecuteProperty()
    {
        // Reflection-based assertion guarding the FR-48 binding invariant:
        // adding any property whose name contains "auto" or "execute" trips this
        // test. The selector is the system's load-bearing enforcement point —
        // there is no auto-execute path here, ever.
        var properties = typeof(PlaybookCandidateSelection)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        properties.Should().NotContain(name =>
            name.Contains("Auto", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Execute", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FR48_HighConfidenceSingle_StillReturnsCandidates_NeverExecutes()
    {
        // Critical FR-48 scenario: even when the selector classifies a result as
        // "high-confidence-single" (no rerank recommended), it MUST still return
        // candidates rather than auto-dispatching. The candidate list IS the
        // user-confirmation surface — the consumer renders these as link buttons
        // and waits for user click before any execution call.
        var selector = CreateSelector();
        var input = new[]
        {
            File("f1", (PlaybookA, "Playbook A", 0.97)),
        };

        var result = selector.Select(input);

        result.Reason.Should().Be("high-confidence-single");
        result.TopCandidates.Should().NotBeEmpty(
            because: "FR-48 requires the user to always confirm; the selector never auto-executes");
        result.RerankRecommended.Should().BeFalse();
    }

    // ────────────────────────────────────────────────────────────────
    // Step 7: input contract guards
    // ────────────────────────────────────────────────────────────────

    [Fact]
    public void Select_NullInput_Throws()
    {
        var selector = CreateSelector();

        var act = () => selector.Select(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Select_CancellationRequested_Throws()
    {
        var selector = CreateSelector();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => selector.Select(
            new[] { File("f1", (PlaybookA, "Playbook A", 0.9)) },
            cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }
}
