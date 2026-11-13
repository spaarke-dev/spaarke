// field-mapping-server-write-path-r1 task 052b (review F1-a) — the one-to-many relationships an Assign cascades along
// (IGenericEntityService.GetAssignCascadesAsync), projected from the SDK's relationship metadata: every relationship whose Assign
// cascade is not NoCascade, one whose cascade the metadata does not state included (fail closed); metadata that does not come
// back, or comes back without its relationships, is a fault (review F3), never "nothing cascades".

using FluentAssertions;
using Microsoft.Xrm.Sdk.Metadata;
using Spaarke.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

[Trait("status", "new")]
public sealed class AssignCascadeProjectionTests
{
    [Theory]
    [InlineData(CascadeType.Cascade)]
    [InlineData(CascadeType.Active)]
    [InlineData(CascadeType.UserOwned)]
    public void ARelationshipThatCascadesOnAssign_IsListed(CascadeType assign)
    {
        var cascades = DataverseServiceClientImpl.AssignCascadesOf(Entity(Relationship("Contact_Tasks", "task", assign)));

        var cascade = cascades.Should().ContainSingle().Subject;
        cascade.SchemaName.Should().Be("Contact_Tasks");
        cascade.ReferencingEntity.Should().Be("task");
        cascade.Assign.Should().Be(assign.ToString());
    }

    [Fact]
    public void ARelationshipThatDoesNotCascadeOnAssign_IsNotListed()
    {
        DataverseServiceClientImpl.AssignCascadesOf(Entity(Relationship("contact_sprk_notes", "sprk_note", CascadeType.NoCascade)))
            .Should().BeEmpty();
    }

    [Fact]
    public void ARelationshipWhoseAssignCascadeIsNotStated_IsListed_FailClosed()
    {
        var unstatedAssign = Relationship("contact_unstated", "sprk_memo", assign: null);
        var noConfiguration = new OneToManyRelationshipMetadata { SchemaName = "contact_noconfig", ReferencingEntity = "sprk_event" };

        DataverseServiceClientImpl.AssignCascadesOf(Entity(unstatedAssign, noConfiguration))
            .Select(c => (c.SchemaName, c.Assign))
            .Should().BeEquivalentTo(new (string, string?)[] { ("contact_unstated", null), ("contact_noconfig", null) });
    }

    [Fact]
    public void NoEntity_OrRelationshipsNotReturned_IsAFault_NeverNothingCascades()
    {
        // Metadata that does not come back cannot say "nothing cascades": the read is a fault, which Repair answers with
        // owner_assign_cascade_unknown (transient) and writes no owner.
        FluentActions.Invoking(() => DataverseServiceClientImpl.AssignCascadesOf(null))
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => DataverseServiceClientImpl.AssignCascadesOf(new EntityMetadata { LogicalName = "contact" }))
            .Should().Throw<InvalidOperationException>().WithMessage("*contact*");
    }

    [Fact]
    public void AnEntityWithNoRelationships_GivesNothing()
    {
        DataverseServiceClientImpl.AssignCascadesOf(Entity()).Should().BeEmpty("an empty list is an answer: nothing cascades");
    }

    private static OneToManyRelationshipMetadata Relationship(string schemaName, string child, CascadeType? assign) => new()
    {
        SchemaName = schemaName,
        ReferencingEntity = child,
        CascadeConfiguration = new CascadeConfiguration { Assign = assign },
    };

    private static EntityMetadata Entity(params OneToManyRelationshipMetadata[] relationships)
    {
        var entity = new EntityMetadata { LogicalName = "contact" };
        typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.OneToManyRelationships))!.SetValue(entity, relationships);
        return entity;
    }
}
