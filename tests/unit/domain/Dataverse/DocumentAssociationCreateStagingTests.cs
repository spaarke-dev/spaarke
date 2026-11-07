using System.Text.Json;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 030b (FR-18): the association a <c>sprk_document</c> is CREATED with — the
/// <see cref="DocumentAssociationMap.TryApply(CreateDocumentRequest, string?, Guid?)"/> overload, and
/// <see cref="DataverseServiceClientImpl.StageDocumentAssociation"/>, which puts it on the create entity.
/// </summary>
/// <remarks>
/// Every Office test above this layer mocks <c>CreateDocumentAsync</c>, so a wrong column or a wrong lookup target would pass
/// all of them and fail only against live Dataverse. The map and the stager are pure, so they are asserted directly (the
/// <c>DocumentCommunicationFieldsStagingTests</c> precedent). KEEP path: tests/unit/domain/** (ADR-038 §2, pure mapping).
/// </remarks>
[Trait("status", "task-030b-field-mapping")]
public class DocumentAssociationCreateStagingTests
{
    private static readonly Guid RecordId = Guid.Parse("030b030b-0000-4000-8000-0000000000a2");

    /// <summary>Every spelling the map accepts, the column a create writes and the table it points at.</summary>
    public static TheoryData<string, string, string> Spellings => new()
    {
        { "matter", "sprk_matter", "sprk_matter" },
        { "sprk_matter", "sprk_matter", "sprk_matter" },
        { "project", "sprk_project", "sprk_project" },
        { "sprk_project", "sprk_project", "sprk_project" },
        { "invoice", "sprk_invoice", "sprk_invoice" },
        { "sprk_invoice", "sprk_invoice", "sprk_invoice" },
        { "workassignment", "sprk_workassignment", "sprk_workassignment" },
        { "sprk_workassignment", "sprk_workassignment", "sprk_workassignment" },
        { "event", "sprk_relatedevent", "sprk_event" },
        { "sprk_event", "sprk_relatedevent", "sprk_event" },
        { "todo", "sprk_relatedtodo", "sprk_todo" },
        { "sprk_todo", "sprk_relatedtodo", "sprk_todo" },
        { "contact", "sprk_relatedcontact", "contact" },
    };

    /// <summary>AC-3: the create entity carries the lookup, to the right table, in the one write.</summary>
    [Theory]
    [MemberData(nameof(Spellings))]
    public void TryApply_ThenStage_WritesTheAssociationLookup_OnTheCreateEntity(string spelling, string column, string targetTable)
    {
        var request = NewRequest();
        DocumentAssociationMap.TryApply(request, spelling, RecordId).Should().BeTrue();

        var document = new Entity("sprk_document");
        DataverseServiceClientImpl.StageDocumentAssociation(document, request);

        var lookup = document[column].Should().BeOfType<EntityReference>().Subject;
        lookup.LogicalName.Should().Be(targetTable);
        lookup.Id.Should().Be(RecordId);
        document.Attributes.Should().ContainSingle("only the association column is staged");
    }

    [Fact]
    public void Stage_WithNoAssociation_WritesNoColumn()
    {
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentAssociation(document, NewRequest());

        document.Attributes.Should().BeEmpty();
    }

    [Fact]
    public void Stage_AnAssociationWithAnEmptyId_IsRefusedBeforeAnyWrite()
    {
        var request = NewRequest();
        request.Association = new DocumentAssociationBind(DocumentLinkFields.All[0], Guid.Empty);
        var document = new Entity("sprk_document");

        var act = () => DataverseServiceClientImpl.StageDocumentAssociation(document, request);

        act.Should().Throw<InvalidOperationException>();
        document.Attributes.Should().BeEmpty();
    }

    /// <summary>AC-7: no column for the type, or no id, sets nothing.</summary>
    [Theory]
    [InlineData("account", "030b030b-0000-4000-8000-0000000000a2")]
    [InlineData("sprk_unknown", "030b030b-0000-4000-8000-0000000000a2")]
    [InlineData("", "030b030b-0000-4000-8000-0000000000a2")]
    [InlineData("matter", "00000000-0000-0000-0000-000000000000")]
    public void TryApply_AnUnsupportedTypeOrAnEmptyId_SetsNothing(string spelling, string id)
    {
        var request = NewRequest();

        DocumentAssociationMap.TryApply(request, spelling, Guid.Parse(id)).Should().BeFalse();

        request.Association.Should().BeNull();
    }

    /// <summary>
    /// AC-4: the column a create writes is the column the update path and the owner question use
    /// (<see cref="RecordReparent.ParentChangesOf(UpdateDocumentRequest)"/>), so the owner decided over the association is the
    /// owner of the row it is written on. Contact is not an ownership parent (owner ruling R4-2) and is pinned by name.
    /// </summary>
    [Theory]
    [MemberData(nameof(Spellings))]
    public void TheCreateColumn_IsTheColumnTheUpdateAndTheOwnerQuestionUse(string spelling, string column, string targetTable)
    {
        var create = NewRequest();
        DocumentAssociationMap.TryApply(create, spelling, RecordId);
        var update = new UpdateDocumentRequest();
        DocumentAssociationMap.TryApply(update, spelling, RecordId).Should().BeTrue();

        create.Association!.Field.Should().Be(
            DocumentLinkFields.All.Single(f => f.LogicalName == column), "a create column is a verified document link field");
        create.Association.Field.TargetEntityLogicalName.Should().Be(targetTable);

        if (spelling == "contact")
        {
            RecordReparent.ParentChangesOf(update).Should().BeEmpty("a contact is never an ownership parent");
            update.ContactLookup.Should().Be(RecordId);
            return;
        }

        var parent = RecordReparent.ParentChangesOf(update).Should().ContainSingle().Subject;
        parent.Key.Should().Be(column);
        parent.Value!.LogicalName.Should().Be(targetTable);
        parent.Value.Id.Should().Be(RecordId);
    }

    /// <summary>
    /// AC-4 (I-1): a document has no stamp column; <see cref="CoreAncestorResolver"/> derives its root from its typed root
    /// columns. Every root the save can file a document to is written to a column the stamp class reads.
    /// </summary>
    [Theory]
    [InlineData("matter")]
    [InlineData("project")]
    [InlineData("workassignment")]
    public void ARootAssociation_IsWrittenToAColumnTheStampClassReadsAsTheDocumentsRoot(string spelling)
    {
        var create = NewRequest();
        DocumentAssociationMap.TryApply(create, spelling, RecordId);

        CoreAncestorResolver.IntermediateRootColumns["sprk_document"]
            .Should().Contain((create.Association!.Field.LogicalName, create.Association.Field.TargetEntityLogicalName));
    }

    /// <summary>AC-9: a JSON body (POST /api/v1/documents binds this type) cannot set any server-only member.</summary>
    [Fact]
    public void ABody_NamingTheServerOnlyMembers_BindsNoneOfThem()
    {
        var json = $$"""
            {
              "name": "Summary input.pdf",
              "containerId": "b!c",
              "description": "d",
              "id": "{{Guid.NewGuid()}}",
              "owningTeamId": "{{Guid.NewGuid()}}",
              "createdByPersonId": "{{Guid.NewGuid()}}",
              "relatedCommunicationId": "{{Guid.NewGuid()}}",
              "accessPermission": 100000002,
              "accessInheritanceRecord": "{}",
              "restrictedBySubmission": true,
              "association": { "field": { "logicalName": "sprk_matter", "schemaName": "sprk_Matter", "targetEntityLogicalName": "sprk_matter" }, "recordId": "{{RecordId}}" },
              "filePointer": { "driveId": "b!other", "itemId": "01OTHER", "webUrl": "https://x" },
              "parentDocumentLookup": "{{Guid.NewGuid()}}"
            }
            """;

        var bound = JsonSerializer.Deserialize<CreateDocumentRequest>(json, JsonSerializerOptions.Web)!;

        bound.Name.Should().Be("Summary input.pdf");
        bound.Description.Should().Be("d");
        bound.Id.Should().BeNull();
        bound.OwningTeamId.Should().BeNull();
        bound.CreatedByPersonId.Should().BeNull();
        bound.RelatedCommunicationId.Should().BeNull();
        bound.AccessPermission.Should().BeNull();
        bound.AccessInheritanceRecord.Should().BeNull();
        bound.RestrictedBySubmission.Should().BeFalse();
        bound.Association.Should().BeNull("the association is decided by server code only");
        bound.FilePointer.Should().BeNull("only the server places a file");
        bound.ParentDocumentLookup.Should().BeNull("the parent link is decided by server code only");
    }

    // ── AC-12: the file pointer in the create (the parent link's stager is task 034's, DocumentParentCreateStagingTests) ──

    [Fact]
    public void StageFilePointer_WritesThePointer_ItsBoundCopy_AndTheFilePath()
    {
        var request = NewRequest();
        request.FilePointer = new DocumentFilePointer("b!drive", "01ITEM", "https://contoso.sharepoint.com/x.pdf");
        var document = new Entity("sprk_document");

        DataverseServiceClientImpl.StageDocumentFilePointer(document, request);

        document["sprk_graphdriveid"].Should().Be("b!drive");
        document[DocumentPointerBinding.ItemIdColumn].Should().Be("01ITEM");
        document[DocumentPointerBinding.BoundItemIdColumn].Should().Be("01ITEM", "the bound copy goes with the pointer, same write");
        document["sprk_filepath"].Should().Be("https://contoso.sharepoint.com/x.pdf");
        document.Attributes.Should().HaveCount(4);
    }

    [Fact]
    public void StageFilePointer_WithNone_WritesNoColumn_AndAPointerWithNoUrl_WritesNoFilePath()
    {
        var document = new Entity("sprk_document");
        DataverseServiceClientImpl.StageDocumentFilePointer(document, NewRequest());
        document.Attributes.Should().BeEmpty();

        var request = NewRequest();
        request.FilePointer = new DocumentFilePointer("b!drive", "01ITEM", null);
        DataverseServiceClientImpl.StageDocumentFilePointer(document, request);
        document.Attributes.Keys.Should().BeEquivalentTo("sprk_graphdriveid", "sprk_graphitemid", "sprk_graphitemidbound");
    }

    [Theory]
    [InlineData("", "01ITEM")]
    [InlineData("b!drive", " ")]
    public void StageFilePointer_APointerMissingItsDriveOrItem_IsRefusedBeforeAnyWrite(string driveId, string itemId)
    {
        var request = NewRequest();
        request.FilePointer = new DocumentFilePointer(driveId, itemId, null);
        var document = new Entity("sprk_document");

        var act = () => DataverseServiceClientImpl.StageDocumentFilePointer(document, request);

        act.Should().Throw<InvalidOperationException>();
        document.Attributes.Should().BeEmpty();
    }

    private static CreateDocumentRequest NewRequest() => new() { Name = "Brief", ContainerId = "b!c" };
}
