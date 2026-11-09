using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.FieldMappings;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse.FieldMapping;

/// <summary>
/// TEST DOUBLE, deliberately permissive (task 022 fix round 4): a REAL <see cref="FieldMappingPushSourceGate"/> over a stub
/// source that is ordinary — owned by a user, its secure flag explicitly off — so the rule allows every child. For tests of
/// the push that are not about the mapping-source rule; the rule's own tests build gates over secure sources
/// (<see cref="FieldMappingPushSourceGateTests"/>).
/// </summary>
internal static class PermissivePushSourceGate
{
    /// <summary>A gate over an ordinary source: every child is allowed.</summary>
    internal static FieldMappingPushSourceGate AllowingEveryChild()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities.Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, Guid id, string[] _, CancellationToken _) => new Entity(table, id)
            {
                ["ownerid"] = new EntityReference("systemuser", Guid.Parse("02200000-0000-4000-8000-00000000a110")),
                ["sprk_issecure"] = false,
            });
        return new FieldMappingPushSourceGate(
            entities.Object, "sprk_matter", Guid.Parse("02200000-0000-4000-8000-00000000a111"),
            _ => Task.FromResult<(Guid? TeamId, string? Refusal)>((null, null)));
    }
}
