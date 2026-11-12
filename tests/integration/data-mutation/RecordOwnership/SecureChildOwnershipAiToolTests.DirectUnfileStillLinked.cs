using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Domain.Dataverse;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// field-mapping-server-write-path-r1 task 054 piece 3b: the ONE re-file core (<see cref="OwnedChildWrite.RefileAsync"/>) judges an
/// unfile on the row as its update leaves it. A Direct message re-filed from a matter to a contact is still linked to a record: it
/// is written, it is never refused for the Direct team, its participants are not granted and its owner does not move.
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid DirectUnfileMessage = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003b3");
    private static readonly Guid DirectUnfileContact = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003d1");
    private static readonly Guid DirectUnfileTeam = Guid.Parse("d1d1d1d1-0054-4054-8054-0000000003d2");

    [Theory(DisplayName = "fm 054 piece 3b: the re-file core moving a Direct message from a matter to a contact writes it, refuses nothing, grants nothing and moves no owner")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefileCore_ADirectMessageMovedFromAMatterToAContact_IsNotAnUnfile(bool lookupResolves)
    {
        _world.WithMessageFiledUnder(OrdinaryMatter, Directory.ChildBu, Directory.ChildTeam);
        var lookup = lookupResolves
            ? new DirectMessageOwnerTeamDouble().WithDirectTeam(DirectUnfileTeam).Lookup()
            : DirectMessageOwnerStepTests.PartiallyConfigured(DirectMessageOwnerTeam.OwnerTeamNameConfigKey);
        var grants = new List<Guid>();

        var outcome = await OwnedChildWrite.RefileAsync(
            _user, _world.Resolver(directMessageOwnerTeam: lookup), "sprk_communication", DirectUnfileMessage, "sprk_communications",
            "sprk_communicationid",
            new DataverseWriteItemMapper.MappedItem(
                JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["sprk_RegardingMatter@odata.bind"] = null,
                    ["sprk_RegardingPerson@odata.bind"] = $"/contacts({DirectUnfileContact:D})",
                }),
                ["sprk_regardingmatter", "sprk_regardingperson"])
            {
                Lookups = [new DataverseWriteItemMapper.MappedLookup("sprk_regardingperson", "contact", "contacts", DirectUnfileContact)],
                ClearedColumns = ["sprk_regardingmatter"],
            },
            callerObjectId: null, CancellationToken.None, directOwner: DirectUnfileStep(lookup, grants));

        outcome.OwnerRefusal.Should().BeNull("a message re-filed to a contact is still linked to a record: no unfile");
        outcome.Written.Should().BeTrue();
        _user.Patches.Should().ContainSingle();
        grants.Should().BeEmpty("no participant is granted for a message that does not move");
        _world.Assignments.Should().BeEmpty();
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("owningteam")?.Id
            .Should().Be(Directory.ChildTeam);
        _world.Row("sprk_communication", DirectUnfileMessage).GetAttributeValue<EntityReference>("sprk_regardingperson")?.Id
            .Should().Be(DirectUnfileContact);
    }

    /// <summary>The real step over the directory: rows read by id, grants recorded.</summary>
    private DirectMessageOwnerStep DirectUnfileStep(DirectMessageOwnerTeam lookup, List<Guid> grants)
    {
        var dataverse = new Mock<IGenericEntityService>();
        dataverse
            .Setup(d => d.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
                q.Criteria.Conditions.Any(c => c.AttributeName == q.EntityName + "id") ? _world.Answer(q) : new EntityCollection());
        var access = new Mock<IDirectThreadAccessService>();
        access
            .Setup(a => a.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ThreadAssignment, CancellationToken>((id, _, _) => grants.Add(id))
            .Returns(Task.CompletedTask);
        return new DirectMessageOwnerStep(access.Object, dataverse.Object, lookup, NullLogger<DirectMessageOwnerStep>.Instance);
    }
}
