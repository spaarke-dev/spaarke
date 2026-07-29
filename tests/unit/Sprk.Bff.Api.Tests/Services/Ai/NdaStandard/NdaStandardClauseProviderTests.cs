using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.NdaStandard;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.NdaStandard;

/// <summary>
/// UAT round-3 D3 — the NDA-standard clause lookup that backs the review comment's "Standard: {ref}"
/// hover. The behavior that must not break is REF NORMALIZATION: the review's standardRef ("B3 -
/// Definition of Confidential Information") and the prompt taxonomy ("B5 Use &amp; standard of care")
/// differ in their label suffix, so lookup must key on the leading B{n} token only.
/// </summary>
public sealed class NdaStandardClauseProviderTests
{
    private readonly NdaStandardClauseProvider _sut = new();

    [Theory]
    [InlineData("B5")]
    [InlineData("b5")]
    [InlineData("B5 - Use & disclosure obligations")]
    [InlineData("B5 Use & standard of care")]
    [InlineData("B05")] // zero-padded
    public void TryResolve_AnyLabelFormContainingTheToken_ResolvesTheSameClause(string reference)
    {
        var clause = _sut.TryResolve(reference);

        clause.Should().NotBeNull();
        clause!.Ref.Should().Be("B5");
        clause.Title.Should().Contain("Use & disclosure");
        clause.Text.Should().Contain("solely for the Purpose");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a clause")]
    [InlineData("B0")]
    [InlineData("B17")] // outside B1..B16
    public void TryResolve_NoValidToken_ReturnsNull(string? reference)
    {
        _sut.TryResolve(reference).Should().BeNull();
    }

    [Fact]
    public void AllClauses_ContainsB1ToB16_InOrder()
    {
        var refs = _sut.AllClauses.Select(c => c.Ref).ToList();

        refs.Should().HaveCount(16);
        refs.Should().ContainInOrder("B1", "B2", "B3", "B15", "B16");
        _sut.AllClauses.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Text));
    }
}
