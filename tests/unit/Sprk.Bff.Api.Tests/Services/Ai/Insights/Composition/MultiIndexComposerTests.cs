using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Insights.Composition;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Composition;

/// <summary>
/// Tests for <see cref="MultiIndexComposer"/> — the Q5-audit-extracted helper that both
/// <c>AiAnalysisNodeExecutor</c> and the new <c>IndexRetrieveNode</c> (D-P12) compose
/// multi-tier knowledge blocks through.
/// </summary>
public sealed class MultiIndexComposerTests
{
    [Fact]
    public void Merge_BothNull_ReturnsNull()
    {
        MultiIndexComposer.Merge(null, null).Should().BeNull();
    }

    [Fact]
    public void Merge_BothWhitespace_ReturnsNull()
    {
        MultiIndexComposer.Merge("   ", "\n\t").Should().BeNull();
    }

    [Fact]
    public void Merge_OnlyReference_ReturnsReferenceUnchanged()
    {
        MultiIndexComposer.Merge("ref content", null).Should().Be("ref content");
        MultiIndexComposer.Merge("ref content", "  ").Should().Be("ref content");
    }

    [Fact]
    public void Merge_OnlyScope_ReturnsScopeUnchanged()
    {
        MultiIndexComposer.Merge(null, "scope content").Should().Be("scope content");
    }

    [Fact]
    public void Merge_BothPresent_JoinsWithBlankLine()
    {
        MultiIndexComposer.Merge("A", "B").Should().Be("A\n\nB");
    }

    [Fact]
    public void Merge_PreservesOrder_ReferenceFirst()
    {
        // Per the existing AiAnalysisNodeExecutor convention.
        MultiIndexComposer.Merge("FIRST", "SECOND").Should().StartWith("FIRST");
        MultiIndexComposer.Merge("FIRST", "SECOND").Should().EndWith("SECOND");
    }

    [Fact]
    public void Merge_Params_ConcatenatesInOrder_SkippingNullsAndWhitespace()
    {
        MultiIndexComposer.Merge("A", null, "B", "  ", "C")
            .Should().Be("A\n\nB\n\nC");
    }

    [Fact]
    public void Merge_Params_AllEmpty_ReturnsNull()
    {
        MultiIndexComposer.Merge(null, "  ", null).Should().BeNull();
    }

    [Fact]
    public void Merge_Params_NullArray_ReturnsNull()
    {
        MultiIndexComposer.Merge((string?[])null!).Should().BeNull();
    }

    [Fact]
    public void Merge_Params_EmptyArray_ReturnsNull()
    {
        MultiIndexComposer.Merge(Array.Empty<string?>()).Should().BeNull();
    }
}
