using Moq;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// #2091 round 4: the playbook update node reads a lookup target's Web API entity set from metadata
/// (<see cref="IGenericEntityService.GetEntitySetNameAsync"/>) instead of guessing a plural. Test compositions that drive the
/// node with lookups register this answer: the live set names the BFF already pins (the secure-child lineage map and the
/// addressable-set list), so <c>sprk_analysis</c> answers <c>sprk_analysises</c> as Dataverse does.
/// </summary>
internal static class EntitySetNameFixtures
{
    public static IGenericEntityService Service() => Mock().Object;

    public static Mock<IGenericEntityService> Mock()
    {
        var entities = new Mock<IGenericEntityService>();
        entities
            .Setup(e => e.GetEntitySetNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string table, CancellationToken _) => SetOf(table));
        return entities;
    }

    public static string SetOf(string table) =>
        Sprk.Bff.Api.Services.Communication.RegardingNameFields.EntitySetName(table)
        ?? (Sprk.Bff.Api.Services.Access.SecureChildLineage.Children.TryGetValue(table, out var child)
            ? child.EntitySet
            : table switch
            {
                "systemuser" => "systemusers",
                "team" => "teams",
                _ => throw new ArgumentException($"Test: no live entity set is pinned for '{table}'.", nameof(table)),
            });
}
