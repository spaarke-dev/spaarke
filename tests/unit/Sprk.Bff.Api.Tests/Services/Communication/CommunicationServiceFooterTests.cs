using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Tracking;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// FR-A1 (task 012): the outbound send path injects the transparent, HMAC-signed tracking footer into the sent
/// body when the footer is enabled for the tenant AND the request regards a record — and injects nothing
/// otherwise, never failing the send (NFR-04). Exercised through the public <see cref="CommunicationService.SendAsync"/>
/// with a recording channel sender capturing the sent <see cref="ChannelSendRequest"/> body — the REAL dispatcher,
/// sender validator, and footer helper run; only the transport + Dataverse boundaries are doubled (ADR-038).
/// Both email send branches (shared-mailbox + OBO/user) are covered to prove they share one helper.
/// </summary>
public class CommunicationServiceFooterTests
{
    private const string SenderEmail = "noreply@contoso.com";
    private const string UserEmail = "user@contoso.com";
    private static readonly Guid MatterId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private const string Token = "SIGNEDTOKEN-abc123";

    private sealed class RecordingEmailSender : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Email;
        public ChannelSendRequest? LastRequest { get; private set; }
        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChannelSendResult
            {
                FromAddress = request.FromAddress,
                ProviderMessageId = "mid@example.com",
                ProviderThreadId = null,
            });
        }
    }

    /// <summary>Stub signer: returns a fixed token, or null (KV unavailable), or throws — no crypto/Key Vault.</summary>
    private sealed class StubTokenSigner : ITrackingTokenSigner
    {
        private readonly string? _token;
        private readonly bool _throws;
        public StubTokenSigner(string? token, bool throws = false) { _token = token; _throws = throws; }
        public Task<string?> SignAsync(string recordType, Guid recordId, string? tenantId, DateTimeOffset issued, CancellationToken ct = default)
            => _throws ? throw new InvalidOperationException("signer boom") : Task.FromResult(_token);
        public Task<TrackingTokenVerification> VerifyAsync(string? token, CancellationToken ct = default)
            => Task.FromResult(TrackingTokenVerification.Invalid);
    }

    private static TrackingFooterGate Gate(bool enabled, string? template = null) =>
        new(Mock.Of<IOptionsMonitor<TrackingFooterOptions>>(m => m.CurrentValue == new TrackingFooterOptions
        {
            Enabled = enabled,
            MessageTemplate = template ?? "Filed with Spaarke re {record-ref}. Ref: {signed-token}",
            SigningKeySecretName = "kv-secret-name",
        }));

    private static CommunicationOptions MinimalOptions() => new()
    {
        ApprovedSenders = new[] { new ApprovedSenderConfig { Email = SenderEmail, DisplayName = "Contoso", IsDefault = true } },
        DefaultMailbox = SenderEmail,
    };

    private static CommunicationService BuildSut(RecordingEmailSender sender, ITrackingTokenSigner signer, TrackingFooterGate gate, List<Entity>? created = null)
    {
        var options = MinimalOptions();
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options), null!,
            Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var dispatcher = new CommunicationChannelDispatcher(
            new ICommunicationChannelSender[] { sender },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        var entity = new Mock<IGenericEntityService>();
        entity.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => created?.Add(e))
            .ReturnsAsync(Guid.NewGuid());
        entity.Setup(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new CommunicationService(
            dispatcher, senderValidator,
            // Issue #2033: a send as user needs the caller's enabled systemuser.
            Mock.Of<ICommunicationDataverseService>(c =>
                c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<Guid?>(Guid.NewGuid())),
            entity.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, null!,
            Mock.Of<ICommunicationEnrichmentService>(),
            Options.Create(options),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Mock.Of<ILogger<CommunicationService>>(),
            trackingTokenSigner: signer,
            trackingFooterGate: gate);
    }

    private static SendCommunicationRequest EmailRequest(bool withRegarding, BodyFormat format = BodyFormat.HTML) => new()
    {
        To = new[] { "recipient@contoso.com" },
        Subject = "Kickoff",
        Body = "hello there",
        BodyFormat = format,
        CommunicationType = CommunicationType.Email,
        Associations = withRegarding
            ? new[] { new CommunicationAssociation { EntityType = "sprk_matter", EntityId = MatterId, EntityName = "Acme Matter" } }
            : null,
    };

    private static SendCommunicationRequest EmailRequestFor(string? entityName) => new()
    {
        To = new[] { "recipient@contoso.com" },
        Subject = "Kickoff",
        Body = "hello there",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        Associations = new[] { new CommunicationAssociation { EntityType = "sprk_matter", EntityId = MatterId, EntityName = entityName } },
    };

    private static HttpContext UserContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", UserEmail),
            new Claim("oid", "55555555-5555-5555-5555-555555555555"),
        }, "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    [Fact]
    public async Task SendAsync_FooterEnabledWithRegarding_AppendsSignedFooterToBody()
    {
        var sender = new RecordingEmailSender();
        var sut = BuildSut(sender, new StubTokenSigner(Token), Gate(enabled: true));

        await sut.SendAsync(EmailRequest(withRegarding: true), httpContext: null, CancellationToken.None);

        var body = sender.LastRequest!.Communication.Body;
        body.Should().StartWith("hello there");                 // original body preserved
        body.Should().Contain("Filed with Spaarke re Acme Matter"); // rendered {record-ref}
        body.Should().Contain(Token);                            // rendered {signed-token}
        body.Should().Contain("<hr");                            // transparent, visible HTML disclosure (ADR-028)
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_RegardingWithoutAName_FooterNamesANeutralRecord_NeverTheLogicalNameOrId(string? entityName)
    {
        var sender = new RecordingEmailSender();
        var created = new List<Entity>();
        var defaultTemplate = new TrackingFooterOptions().MessageTemplate;
        var sut = BuildSut(sender, new StubTokenSigner(Token), Gate(enabled: true, defaultTemplate), created);

        await sut.SendAsync(EmailRequestFor(entityName), httpContext: null, CancellationToken.None);

        var body = sender.LastRequest!.Communication.Body;
        body.Should().Contain("regarding a Spaarke record.");
        body.Should().NotContain("sprk_").And.NotContain(MatterId.ToString());
        body.Should().Contain($"Ref: {Token}");
    }

    [Fact]
    public async Task SendAsync_RegardingWithoutAName_StoredRegardingNameIsStillNotSet()
    {
        var sender = new RecordingEmailSender();
        var created = new List<Entity>();
        var sut = BuildSut(sender, new StubTokenSigner(Token), Gate(enabled: true), created);

        await sut.SendAsync(EmailRequestFor(null), httpContext: null, CancellationToken.None);

        var row = created.Should().ContainSingle(e => e.LogicalName == "sprk_communication").Which;
        row.Contains("sprk_regardingrecordname").Should().BeFalse("the footer wording is not stored as the regarding name");
    }

    [Fact]
    public async Task SendAsync_RegardingWithAName_FooterAndStoredNameAreTheName()
    {
        var sender = new RecordingEmailSender();
        var created = new List<Entity>();
        var sut = BuildSut(sender, new StubTokenSigner(Token), Gate(enabled: true, new TrackingFooterOptions().MessageTemplate), created);

        await sut.SendAsync(EmailRequestFor("Acme Matter"), httpContext: null, CancellationToken.None);

        sender.LastRequest!.Communication.Body.Should().Contain("regarding Acme Matter.").And.NotContain("a Spaarke record");
        created.Single(e => e.LogicalName == "sprk_communication").GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Acme Matter");
    }

    [Fact]
    public async Task SendAsync_FooterDisabled_BodyUnchanged()
    {
        var sender = new RecordingEmailSender();
        var sut = BuildSut(sender, new StubTokenSigner(Token), Gate(enabled: false));

        await sut.SendAsync(EmailRequest(withRegarding: true), httpContext: null, CancellationToken.None);

        sender.LastRequest!.Communication.Body.Should().Be("hello there");
    }

    [Fact]
    public async Task SendAsync_NoRegarding_BodyUnchanged()
    {
        var sender = new RecordingEmailSender();
        var sut = BuildSut(sender, new StubTokenSigner(Token), Gate(enabled: true));

        await sut.SendAsync(EmailRequest(withRegarding: false), httpContext: null, CancellationToken.None);

        sender.LastRequest!.Communication.Body.Should().Be("hello there");
    }

    [Fact]
    public async Task SendAsync_SignerThrows_SendStillSucceedsWithoutFooter()
    {
        var sender = new RecordingEmailSender();
        var sut = BuildSut(sender, new StubTokenSigner(token: null, throws: true), Gate(enabled: true));

        var response = await sut.SendAsync(EmailRequest(withRegarding: true), httpContext: null, CancellationToken.None);

        response.CommunicationId.Should().NotBeNull();          // send + persist still succeeded (NFR-04)
        sender.LastRequest!.Communication.Body.Should().Be("hello there"); // no footer
    }

    [Fact]
    public async Task SendAsync_SignerReturnsNull_NoFooterInjected()
    {
        var sender = new RecordingEmailSender();
        var sut = BuildSut(sender, new StubTokenSigner(token: null), Gate(enabled: true)); // enabled but KV key unavailable

        await sut.SendAsync(EmailRequest(withRegarding: true), httpContext: null, CancellationToken.None);

        sender.LastRequest!.Communication.Body.Should().Be("hello there");
    }

    [Fact]
    public async Task SendAsync_PlainTextBody_AppendsTextFooterNotHtml()
    {
        var sender = new RecordingEmailSender();
        var sut = BuildSut(sender, new StubTokenSigner(Token), Gate(enabled: true));

        await sut.SendAsync(EmailRequest(withRegarding: true, format: BodyFormat.PlainText), httpContext: null, CancellationToken.None);

        var body = sender.LastRequest!.Communication.Body;
        body.Should().StartWith("hello there");
        body.Should().Contain("---");   // plain-text disclosure block
        body.Should().Contain(Token);
        body.Should().NotContain("<hr"); // no HTML markup in a text body
    }

    [Fact]
    public async Task SendAsUser_FooterEnabledWithRegarding_AppendsFooter_ProvingBothBranchesShareHelper()
    {
        var sender = new RecordingEmailSender();
        var sut = BuildSut(sender, new StubTokenSigner(Token), Gate(enabled: true));

        var request = EmailRequest(withRegarding: true) with { SendMode = SendMode.User };
        await sut.SendAsync(request, UserContext(), CancellationToken.None);

        sender.LastRequest!.Communication.Body.Should().Contain(Token); // OBO branch injects via the same helper
    }
}
