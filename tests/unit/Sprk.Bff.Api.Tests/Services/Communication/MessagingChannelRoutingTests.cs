using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// NFR-04 proof for the messaging channel (task 020): registering the REAL
/// <see cref="MessagingChannelSender"/> + <see cref="MessagingArchiver"/> alongside the email seams makes
/// <c>CommunicationType.Message</c> resolvable by the dispatcher with NO change to the dispatch site — and
/// the email channel is byte-unaffected (regression). This is the "add a channel by additive registration
/// alone" contract exercised end-to-end with the production channel types.
/// </summary>
public class MessagingChannelRoutingTests
{
    private static EmailChannelSender EmailSender() =>
        new(Mock.Of<IGraphClientFactory>(), Mock.Of<ILogger<EmailChannelSender>>());

    private static EmailArchiver EmailArchiver() =>
        new(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>()));

    private static MessagingChannelSender MessagingSender() =>
        new(
            Mock.Of<IAcsIdentityService>(),
            Mock.Of<IAcsThreadService>(),
            Options.Create(new AcsOptions { Endpoint = "https://unit.communication.azure.com" }),
            Mock.Of<ILogger<MessagingChannelSender>>());

    private static CommunicationChannelDispatcher BothChannelsDispatcher() =>
        new(
            new ICommunicationChannelSender[] { EmailSender(), MessagingSender() },
            new ICommunicationArchiver[] { EmailArchiver(), new MessagingArchiver() });

    [Fact]
    public void ResolveSender_ForMessageType_ReturnsMessagingChannelSender()
    {
        var sender = BothChannelsDispatcher().ResolveSender(CommunicationType.Message);

        sender.Should().BeOfType<MessagingChannelSender>();
        sender.SupportedType.Should().Be(CommunicationType.Message);
    }

    [Fact]
    public void ResolveArchiver_ForMessageType_ReturnsMessagingArchiver()
    {
        var archiver = BothChannelsDispatcher().ResolveArchiver(CommunicationType.Message);

        archiver.Should().BeOfType<MessagingArchiver>();
        archiver.SupportedType.Should().Be(CommunicationType.Message);
    }

    [Fact]
    public void ResolveSender_ForEmailType_StillReturnsEmailSender_WhenMessagingRegistered()
    {
        // Regression: adding the messaging channel does not disturb email dispatch.
        var dispatcher = BothChannelsDispatcher();

        dispatcher.ResolveSender(CommunicationType.Email).Should().BeOfType<EmailChannelSender>();
        dispatcher.ResolveArchiver(CommunicationType.Email).Should().BeOfType<EmailArchiver>();
    }
}
