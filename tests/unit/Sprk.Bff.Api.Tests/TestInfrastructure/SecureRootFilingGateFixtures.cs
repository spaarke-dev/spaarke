using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// unified-access-control-r2 task 158 — the <see cref="SecureRootFilingGate"/> a writer test passes when it is NOT about the
/// round-6 rule: a REAL gate whose REAL <see cref="SecureRootInheritance"/> reads a Dataverse that holds no rows, so a work
/// assignment or project is never found filed under anything secure — a create or re-file proceeds, and nothing is secured.
/// The provisioning dependencies are absent on purpose: reaching one would mean a secure parent was found, which this world
/// cannot answer.
/// </summary>
internal static class SecureRootFilingGateFixtures
{
    /// <summary>A gate over a Dataverse with no rows: no record is filed under anything secure.</summary>
    internal static SecureRootFilingGate NothingSecure() => Over(EmptyEntities());

    /// <summary>
    /// The inheritance over a Dataverse with no rows — for a test of a route that passes a parent's sharees on (task 158's
    /// <c>/share-user</c> fan-out) and is not about the round-6 rule: nothing is filed under anything, so nothing is written.
    /// </summary>
    internal static SecureRootInheritance InheritanceOverNothing() => InheritanceOver(EmptyEntities());

    private static SecureRootInheritance InheritanceOver(IGenericEntityService entities) =>
        new(entities, null!, null!, null!, null!, null!, null!,
            new Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.FakeAssignedAccessStore(), null!, new ConfigurationBuilder().Build(),
            NullLogger<SecureRootInheritance>.Instance);

    /// <summary>A gate whose inheritance reads <paramref name="entities"/> (provisioning dependencies absent).</summary>
    internal static SecureRootFilingGate Over(IGenericEntityService entities)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => InheritanceOver(entities));
        return new SecureRootFilingGate(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SecureRootFilingGate>.Instance);
    }

    /// <summary>A gate in a host where the inheritance is not registered at all (it must refuse a filing write).</summary>
    internal static SecureRootFilingGate Unregistered() =>
        new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SecureRootFilingGate>.Instance);

    private static IGenericEntityService EmptyEntities()
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Loose);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        return entities.Object;
    }
}
