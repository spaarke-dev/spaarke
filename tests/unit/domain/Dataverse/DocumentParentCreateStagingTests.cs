using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 034: the parent link a <c>sprk_document</c> is CREATED with —
/// <see cref="DataverseServiceClientImpl.StageDocumentParent"/>, which puts it on the create entity.
/// </summary>
/// <remarks>
/// Every test above this layer mocks <c>CreateDocumentAsync</c>, so a wrong column or a wrong lookup target would pass all of
/// them and fail only against live Dataverse. The stager is pure, so it is asserted directly (the
/// <c>DocumentAssociationCreateStagingTests</c> precedent). KEEP path: tests/unit/domain/** (ADR-038 §2, pure mapping).
/// </remarks>
[Trait("status", "task-034-field-mapping")]
public class DocumentParentCreateStagingTests
{
    private static readonly Guid ParentId = Guid.Parse("034a034a-0000-4000-8000-0000000000b2");

    private static CreateDocumentRequest NewRequest() => new() { Name = "a.pdf", ContainerId = "c1" };

    [Fact]
    public void Stage_WithAParent_WritesTheParentLookup_OnTheCreateEntity()
    {
        var request = NewRequest();
        request.ParentDocumentLookup = ParentId;
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentParent(document, request);

        var lookup = document["sprk_parentdocument"].Should().BeOfType<EntityReference>().Subject;
        lookup.LogicalName.Should().Be("sprk_document");
        lookup.Id.Should().Be(ParentId);
        document.Attributes.Should().ContainSingle("only the parent column is staged");
    }

    [Fact]
    public void Stage_WithNoParent_WritesNoColumn()
    {
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentParent(document, NewRequest());

        document.Attributes.Should().BeEmpty();
    }

    [Fact]
    public void Stage_AParentWithAnEmptyId_IsRefusedBeforeAnyWrite()
    {
        var request = NewRequest();
        request.ParentDocumentLookup = Guid.Empty;
        var document = new Entity("sprk_document");

        var act = () => DataverseServiceClientImpl.StageDocumentParent(document, request);

        act.Should().Throw<InvalidOperationException>();
        document.Attributes.Should().BeEmpty();
    }
}
