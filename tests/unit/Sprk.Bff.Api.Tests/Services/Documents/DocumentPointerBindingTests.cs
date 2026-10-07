using FluentAssertions;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// unified-access-control-r2 task 171, owner round 72 item 1: <see cref="DocumentPointerBinding.Compare"/> — the ONE
/// comparison of a document's item id with its field-secured copy (the pointer check and the relocator both use it).
/// </summary>
public class DocumentPointerBindingTests
{
    [Theory(DisplayName = "Round 72 F4: Compare — equal is Bound, empty is Unbound, anything else is Mismatch (exact, trimmed)")]
    [InlineData("01ITEM", "01ITEM", DocumentPointerBinding.BindingState.Bound)]
    [InlineData(" 01ITEM ", "01ITEM", DocumentPointerBinding.BindingState.Bound)]
    [InlineData(null, "01ITEM", DocumentPointerBinding.BindingState.Unbound)]
    [InlineData("  ", "01ITEM", DocumentPointerBinding.BindingState.Unbound)]
    [InlineData("01OTHER", "01ITEM", DocumentPointerBinding.BindingState.Mismatch)]
    [InlineData("01item", "01ITEM", DocumentPointerBinding.BindingState.Mismatch)]
    public void Compare_IsExact(string? bound, string pointer, DocumentPointerBinding.BindingState expected)
        => DocumentPointerBinding.Compare(bound, pointer).Should().Be(expected);
}
