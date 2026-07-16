using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of the <see cref="CommunicationChannelDispatcher"/> — the CommunicationType-keyed dispatch
/// that isolates channel-specific send/archive behind the ADR-045 seams (NFR-04). These tests protect the
/// "add a channel by registration alone" contract: a second CommunicationType resolves without any change
/// to the dispatch site.
/// </summary>
public class CommunicationChannelDispatcherTests
{
    private static EmailChannelSender EmailSender() =>
        new(Mock.Of<IGraphClientFactory>(), Mock.Of<ILogger<EmailChannelSender>>());

    private static EmailArchiver EmailArchiver() =>
        new(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>()));

    private static CommunicationChannelDispatcher EmailOnlyDispatcher() =>
        new(
            new ICommunicationChannelSender[] { EmailSender() },
            new ICommunicationArchiver[] { EmailArchiver() });

    [Fact]
    public void ResolveSender_ForEmailType_ReturnsEmailSender()
    {
        var sender = EmailOnlyDispatcher().ResolveSender(CommunicationType.Email);

        sender.Should().BeOfType<EmailChannelSender>();
        sender.SupportedType.Should().Be(CommunicationType.Email);
    }

    [Fact]
    public void ResolveArchiver_ForEmailType_ReturnsEmailArchiver()
    {
        var archiver = EmailOnlyDispatcher().ResolveArchiver(CommunicationType.Email);

        archiver.Should().BeOfType<EmailArchiver>();
        archiver.SupportedType.Should().Be(CommunicationType.Email);
    }

    [Fact]
    public void ResolveSender_ForUnregisteredType_ThrowsChannelNotSupported()
    {
        var act = () => EmailOnlyDispatcher().ResolveSender(CommunicationType.TeamsMessage);

        act.Should().Throw<SdapProblemException>()
            .Which.Code.Should().Be("CHANNEL_NOT_SUPPORTED");
    }

    [Fact]
    public void ResolveArchiver_ForUnregisteredType_ThrowsChannelNotSupported()
    {
        var act = () => EmailOnlyDispatcher().ResolveArchiver(CommunicationType.SMS);

        act.Should().Throw<SdapProblemException>()
            .Which.Code.Should().Be("CHANNEL_NOT_SUPPORTED");
    }

    // NFR-04: a second channel is added by REGISTRATION ALONE. Registering a sender for a new
    // CommunicationType alongside the email sender makes it resolvable by type — the dispatch site in
    // CommunicationService is unchanged, proving the "no engine/dispatch edit to add a channel" contract.
    [Fact]
    public void ResolveSender_WithSecondChannelRegistered_ResolvesEachByTypeWithoutDispatchSiteChange()
    {
        var email = EmailSender();
        var teams = new StubChannelSender { SupportedType = CommunicationType.TeamsMessage };

        var dispatcher = new CommunicationChannelDispatcher(
            new ICommunicationChannelSender[] { email, teams },
            new ICommunicationArchiver[] { EmailArchiver() });

        dispatcher.ResolveSender(CommunicationType.Email).Should().BeSameAs(email);
        dispatcher.ResolveSender(CommunicationType.TeamsMessage).Should().BeSameAs(teams);
    }

    [Fact]
    public void Constructor_WithTwoSendersForSameType_Throws()
    {
        var act = () => new CommunicationChannelDispatcher(
            new ICommunicationChannelSender[] { EmailSender(), EmailSender() },
            new ICommunicationArchiver[] { EmailArchiver() });

        act.Should().Throw<ArgumentException>("one sender per CommunicationType is an invariant");
    }

    private sealed class StubChannelSender : ICommunicationChannelSender
    {
        public CommunicationType SupportedType { get; init; }

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChannelSendResult { FromAddress = request.FromAddress });
    }
}
