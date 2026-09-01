using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Engine;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Test helper. As of dotnet-10-upgrade task 020 (R9 DI captive-dependency fix), the Singleton
/// <c>CommunicationService</c> resolves the Scoped <see cref="SpeFileStore"/> from an
/// <see cref="IServiceScopeFactory"/> per SPE operation instead of capturing it on the constructor.
/// Tests that exercise an SPE path now supply their <see cref="SpeFileStore"/> double via
/// <c>scopeFactory:</c> using this stub, so the same double flows through the new lifetime.
///
/// <para><b>Extended by unified-access-control-r2 task 076.</b> The same scope now also yields a
/// <see cref="CommunicationContainerResolver"/>, because <c>ArchiveToSpeAsync</c> resolves the
/// destination container through it rather than reading <c>Communication:ArchiveContainerId</c>
/// directly — so a SECURE matter's archived <c>.eml</c> lands in that matter's own container instead of
/// the shared archive. Without this the five archive tests fail on a null resolver, which is the
/// intended fail-closed direction but not what they are testing.</para>
/// </summary>
internal static class SpeScopeFactoryStub
{
    /// <summary>
    /// A scope serving the supplied <see cref="SpeFileStore"/> and a
    /// <see cref="CommunicationContainerResolver"/> that answers "this communication regards nothing
    /// secure" — i.e. the caller's fallback container is used, which is the behaviour every existing
    /// assertion was written against.
    /// </summary>
    public static IServiceScopeFactory Create(SpeFileStore speFileStore)
        => Create(speFileStore, NonSecureContainerResolver());

    public static IServiceScopeFactory Create(
        SpeFileStore speFileStore,
        CommunicationContainerResolver containerResolver)
    {
        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider.Setup(sp => sp.GetService(typeof(SpeFileStore))).Returns(speFileStore);
        scopedProvider
            .Setup(sp => sp.GetService(typeof(CommunicationContainerResolver)))
            .Returns(containerResolver);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    /// <summary>
    /// A REAL <see cref="CommunicationContainerResolver"/> wired so that no regarding is secure.
    /// </summary>
    /// <remarks>
    /// Real, not mocked: <see cref="CommunicationContainerResolver"/> and
    /// <see cref="RecordContainerResolver"/> are concrete-by-ADR-010 with non-virtual members, so there
    /// is nothing to mock. Its two collaborators are interfaces and are stubbed at that boundary.
    ///
    /// <para>The securable-entity set is deliberately NON-EMPTY: the resolver treats an empty set as
    /// "securability could not be determined" and refuses (<c>securable_entities_unknown</c>), which is
    /// its fail-closed contract, not something to route around. The <c>sprk_communication</c> row is
    /// returned WITHOUT any regarding attribute, so no securable regarding is found and the decision
    /// falls through to the fallback the caller passed in.</para>
    /// </remarks>
    public static CommunicationContainerResolver NonSecureContainerResolver()
    {
        var securableEntities = new Mock<ISecurableEntityRegistry>();
        securableEntities
            .Setup(r => r.GetSecurableEntitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_matter" });

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveAsync(
                "sprk_communication", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string logicalName, Guid id, string[] _, CancellationToken __) =>
                new DataverseEntity(logicalName) { Id = id });

        return new CommunicationContainerResolver(
            new RecordContainerResolver(
                securableEntities.Object,
                entityService.Object,
                Mock.Of<ILogger<RecordContainerResolver>>()),
            entityService.Object,
            securableEntities.Object,
            Mock.Of<ILogger<CommunicationContainerResolver>>());
    }
}
