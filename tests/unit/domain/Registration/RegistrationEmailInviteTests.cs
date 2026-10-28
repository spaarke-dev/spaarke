using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// unified-access-control-r2 task 185 slice D: the two CIAM invite emails, through the REAL
/// <see cref="RegistrationEmailService"/> and the REAL <see cref="CommunicationService"/> (only the channel sender, Dataverse
/// and enrichment are doubled).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Every link and button opens the invite link (<c>{portal}/?customer={key}#invite={code}</c>, §12 I-12); its code matches
/// the stored hash. The visible link text stays the plain portal URL, so the code is never printed as text.</item>
/// <item>Both are SYSTEM MAIL: the stored <c>sprk_body</c> is the same email with "[invitation link removed]" in place of the
/// link, and the send is not enriched.</item>
/// <item>The ExistingAccount email carries provisioning R2's "Forgot password" line.</item>
/// <item>No log line carries the code or its hash.</item>
/// </list>
/// </remarks>
public class RegistrationEmailInviteTests
{
    private const string Mailbox = "mailbox-central@spaarke.com";
    private const string PortalUrl = "https://external.test";
    private const string Recipient = "partner@outside.test";

    private readonly RecordingChannelSender _channel = new();
    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<ICommunicationEnrichmentService> _enrichment = new();
    private readonly LogCapture _log = new();
    private Entity? _stored;

    public RegistrationEmailInviteTests()
    {
        _entities
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) =>
            {
                if (entity.LogicalName == "sprk_communication")
                {
                    _stored = entity;
                }
            })
            .ReturnsAsync(Guid.NewGuid());
    }

    private RegistrationEmailService Sut()
    {
        var options = new CommunicationOptions
        {
            ApprovedSenders = [new ApprovedSenderConfig { Email = Mailbox, DisplayName = "Spaarke", IsDefault = true }],
            DefaultMailbox = Mailbox,
        };
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            new CommunicationAccountService(
                Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                NullLogger<CommunicationAccountService>.Instance),
            Mock.Of<IDistributedCache>(),
            NullLogger<ApprovedSenderValidator>.Instance);
        var dispatcher = new CommunicationChannelDispatcher(
            new ICommunicationChannelSender[] { _channel },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(NullLogger<EmlGenerationService>.Instance)) });

        var communication = new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            _entities.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService — not reached by a shared-mailbox send without attachments
            null!, // JobSubmissionService — not reached
            _enrichment.Object,
            Microsoft.Extensions.Options.Options.Create(options),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            _log.For<CommunicationService>());

        return new RegistrationEmailService(
            communication, Microsoft.Extensions.Options.Options.Create(options), _log.For<RegistrationEmailService>());
    }

    private Task Send(string email, string inviteUrl) => email == "onboarding"
        ? Sut().SendCiamOnboardingEmailAsync(Recipient, "Pat", PortalUrl, inviteUrl)
        : Sut().SendCiamExistingAccountEmailAsync(Recipient, "Pat", PortalUrl, inviteUrl);

    private static (Guid Contact, string Code, string Stored, string InviteUrl) NewInvite()
    {
        var contact = Guid.NewGuid();
        var (code, stored) = InviteCode.Create(contact);
        return (contact, code, stored, InviteExternalUserEndpoint.BuildInviteLink(PortalUrl, "acme", code)!);
    }

    /// <summary>
    /// MUTATIONS: leaving the templates' <c>href</c>s on <c>{{PortalUrl}}</c> fails the link and code assertions; storing the
    /// sent body fails the <c>sprk_body</c> assertions.
    /// </summary>
    [Theory]
    [InlineData("onboarding")]
    [InlineData("existing-account")]
    public async Task BothInviteEmails_LinkTheInviteUrl_AndAreStoredWithTheLinkRemoved(string email)
    {
        var invite = NewInvite();

        await Send(email, invite.InviteUrl);

        var sent = _channel.LastRequest.Should().NotBeNull().And.Subject.As<ChannelSendRequest>().Communication;
        var encodedLink = System.Net.WebUtility.HtmlEncode(invite.InviteUrl);
        encodedLink.Should().Be($"{PortalUrl}/?customer=acme#invite={invite.Code}", "§12 I-12: the code rides in the fragment");

        // Every link and button opens the invite link, and its code is the one whose hash was stored.
        Regex.Matches(sent.Body, "href=\"([^\"]*)\"").Select(m => m.Groups[1].Value)
            .Where(href => !href.StartsWith("mailto:", StringComparison.Ordinal))
            .Should().HaveCount(3).And.OnlyContain(href => href == encodedLink);
        var emailedCode = Regex.Match(sent.Body, "invite=([A-Za-z0-9_-]+)").Groups[1].Value;
        InviteCode.Matches(invite.Stored, emailedCode, invite.Contact).Should().BeTrue();

        // The code is never printed as text: outside the hrefs it does not occur, and the visible link text is the portal URL.
        Regex.Replace(sent.Body, "href=\"[^\"]*\"", string.Empty).Should().NotContain(invite.Code);
        sent.Body.Should().Contain($">{PortalUrl}</a>");

        // System mail: the stored copy has the link removed, and the send is not enriched.
        sent.SystemMail.Should().NotBeNull();
        var storedBody = _stored.Should().NotBeNull().And.Subject.As<Entity>().GetAttributeValue<string>("sprk_body");
        storedBody.Should().NotContain(invite.Code).And.NotContain("invite=");
        storedBody.Should().Be(sent.Body.Replace(encodedLink, RegistrationEmailService.RemovedInviteLinkText),
            "the stored body is the sent email with only the link replaced");
        _enrichment.Verify(
            e => e.EnrichAsync(It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // No log line carries the code or its hash.
        _log.Lines.Should().NotBeEmpty();
        _log.Lines.Should().NotContain(line => line.Contains(invite.Code, StringComparison.Ordinal)
            || line.Contains(invite.Stored, StringComparison.Ordinal)
            || line.Contains("invite=", StringComparison.Ordinal));
    }

    /// <summary>
    /// UAC task 193 (F-final): the REAL mailer, through the real communication pipeline, writes no log line naming the
    /// invitee's email (the recipient) — on both invite emails. MUTATION: putting the recipient back into either
    /// "Sending CIAM …" log template fails its row.
    /// </summary>
    [Theory]
    [InlineData("onboarding")]
    [InlineData("existing-account")]
    public async Task NoLogLine_NamesTheInviteeEmail(string email)
    {
        await Send(email, NewInvite().InviteUrl);

        _log.Lines.Should().Contain(line => line.StartsWith("Sending CIAM", StringComparison.Ordinal),
            "the mailer logs the send");
        _log.Lines.Should().NotContain(line => line.Contains(Recipient, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Provisioning R2 (owner round 98): the ExistingAccount email tells the person what to do with an account they don't recognise.</summary>
    [Fact]
    public async Task TheExistingAccountEmail_CarriesTheForgotPasswordLine()
    {
        await Send("existing-account", NewInvite().InviteUrl);

        _channel.LastRequest!.Communication.Body.Should().Contain(
            "If you don't recognise this account or its password, use <strong>Forgot password</strong> on the sign-in page.");
    }

    private sealed class RecordingChannelSender : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Email;

        public ChannelSendRequest? LastRequest { get; private set; }

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChannelSendResult { FromAddress = request.FromAddress });
        }
    }

    /// <summary>Captures every formatted log line, with its exception text, from every logger it hands out.</summary>
    private sealed class LogCapture
    {
        public List<string> Lines { get; } = new();

        public ILogger<T> For<T>() => new Logger<T>(this);

        private sealed class Logger<T>(LogCapture owner) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner.Lines)
                {
                    owner.Lines.Add(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
                }
            }
        }
    }
}
