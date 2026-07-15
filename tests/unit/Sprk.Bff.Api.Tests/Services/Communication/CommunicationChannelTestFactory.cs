using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Shared test helper that builds a <see cref="CommunicationChannelDispatcher"/> wired to the real email
/// seam implementations (<see cref="EmailChannelSender"/> + <see cref="EmailArchiver"/>), so
/// <see cref="CommunicationService"/> tests exercise the production send/archive path through the ADR-045
/// channel seams (the sender resolves the caller-supplied <see cref="IGraphClientFactory"/> mock, keeping
/// existing ForApp/ForUserAsync verifications valid).
/// </summary>
internal static class CommunicationChannelTestFactory
{
    public static CommunicationChannelDispatcher CreateDispatcher(
        IGraphClientFactory graphClientFactory,
        EmlGenerationService? emlGenerationService = null)
    {
        var sender = new EmailChannelSender(graphClientFactory, Mock.Of<ILogger<EmailChannelSender>>());
        var archiver = new EmailArchiver(
            emlGenerationService ?? new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>()));
        return new CommunicationChannelDispatcher(
            new ICommunicationChannelSender[] { sender },
            new ICommunicationArchiver[] { archiver });
    }
}
