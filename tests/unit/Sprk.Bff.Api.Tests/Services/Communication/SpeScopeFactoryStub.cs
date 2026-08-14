using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Test helper. As of dotnet-10-upgrade task 020 (R9 DI captive-dependency fix), the Singleton
/// <c>CommunicationService</c> resolves the Scoped <see cref="SpeFileStore"/> from an
/// <see cref="IServiceScopeFactory"/> per SPE operation instead of capturing it on the constructor.
/// Tests that exercise an SPE path now supply their <see cref="SpeFileStore"/> double via
/// <c>scopeFactory:</c> using this stub, so the same double flows through the new lifetime.
/// </summary>
internal static class SpeScopeFactoryStub
{
    public static IServiceScopeFactory Create(SpeFileStore speFileStore)
    {
        var scopedProvider = new Mock<IServiceProvider>();
        scopedProvider.Setup(sp => sp.GetService(typeof(SpeFileStore))).Returns(speFileStore);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopedProvider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }
}
