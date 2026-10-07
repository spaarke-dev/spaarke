using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Documents;

/// <summary>
/// unified-access-control-r2 task 171, owner round 72 item 1: <see cref="DocumentPointerBinding"/> — the field-secured copy
/// of a document's item id is written with the SAME value on every write shape the BFF uses, and compared exactly.
/// </summary>
public class DocumentPointerBindingTests
{
    [Fact(DisplayName = "Round 72 F4: Bind copies the item id of an entity write — attribute or (upsert) alternate key")]
    public void Bind_CopiesTheItemId_FromAttributeOrKey()
    {
        var create = DocumentPointerBinding.Bind(new Entity("sprk_document") { ["sprk_graphitemid"] = "01A" });
        var upsert = new Entity("sprk_document");
        upsert.KeyAttributes["sprk_graphitemid"] = "01B";
        DocumentPointerBinding.Bind(upsert);
        var unrelated = DocumentPointerBinding.Bind(new Entity("sprk_document") { ["sprk_name"] = "x" });

        create[DocumentPointerBinding.BoundItemIdColumn].Should().Be("01A");
        upsert[DocumentPointerBinding.BoundItemIdColumn].Should().Be("01B");
        unrelated.Contains(DocumentPointerBinding.BoundItemIdColumn).Should().BeFalse("a write that sets no item id binds nothing");
    }

    [Fact(DisplayName = "Round 72 F4: BindFields copies the item id of both dictionary shapes")]
    public void BindFields_CopiesTheItemId_ForBothDictionaryShapes()
    {
        var generic = new Dictionary<string, object> { ["sprk_graphitemid"] = "01C" };
        var webApi = new Dictionary<string, object?> { ["sprk_graphitemid"] = "01D" };

        DocumentPointerBinding.BindFields(generic);
        DocumentPointerBinding.BindFields(webApi);

        generic[DocumentPointerBinding.BoundItemIdColumn].Should().Be("01C");
        webApi[DocumentPointerBinding.BoundItemIdColumn].Should().Be("01D");
    }

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
