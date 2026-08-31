using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Test helper for the seam suites that drive the real <c>CommunicationEnrichmentService</c>.
/// As of dotnet-10-upgrade task 020 (R2 DI captive-dependency fix) that Singleton service resolves
/// its four Scoped collaborators — <see cref="IPostUploadIndexingEnqueuer"/> and the three
/// <c>ICommunication*Ai</c> facades — from an <see cref="IServiceScopeFactory"/> at each use-site
/// rather than capturing them on the constructor. This stub wires a mock scope chain whose scope
/// yields the supplied doubles, so the seam tests exercise the SAME behavior through the new lifetime.
/// </summary>
internal static class EnrichmentScopeFactoryStub
{
    public static IServiceScopeFactory Create(
        IPostUploadIndexingEnqueuer enqueuer,
        ICommunicationTriageAi triageAi,
        ICommunicationProposeAi proposeAi,
        ICommunicationCreateTaskAi createTaskAi)
    {
        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider.Setup(sp => sp.GetService(typeof(IPostUploadIndexingEnqueuer))).Returns(enqueuer);
        scopedProvider.Setup(sp => sp.GetService(typeof(ICommunicationTriageAi))).Returns(triageAi);
        scopedProvider.Setup(sp => sp.GetService(typeof(ICommunicationProposeAi))).Returns(proposeAi);
        scopedProvider.Setup(sp => sp.GetService(typeof(ICommunicationCreateTaskAi))).Returns(createTaskAi);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }
}
