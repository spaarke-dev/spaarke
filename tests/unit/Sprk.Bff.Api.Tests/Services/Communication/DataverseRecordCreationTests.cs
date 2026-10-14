using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;
using Sprk.Bff.Api.Tests.TestInfrastructure;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tests that CreateDataverseRecordAsync sets correct field names and values
/// on the sprk_communication entity during SendAsync.
/// </summary>
/// <remarks>
/// 2026-05-31 (task 011 / P1.A2 verify+repair): the sprk_communication writer was migrated to
/// IGenericEntityService.CreateAsync (ISP segregation of IDataverseService). Tests now capture
/// on IGenericEntityService; signal-clarity preserved. Sibling-coordination note: production
/// signature in Services/Communication/CommunicationService.cs is unchanged by this task.
/// </remarks>
[Trait("status", "repaired")]
public class DataverseRecordCreationTests
{
    #region Test Infrastructure

    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<IGenericEntityService> _genericEntityServiceMock;
    private readonly Mock<ILogger<CommunicationService>> _loggerMock;
    private DataverseEntity? _capturedEntity;

    public DataverseRecordCreationTests()
    {
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _genericEntityServiceMock = new Mock<IGenericEntityService>();
        _loggerMock = new Mock<ILogger<CommunicationService>>();

        // Default: Graph succeeds (202 Accepted)
        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateMockGraphClient());

        // Capture the Entity passed to CreateAsync
        _genericEntityServiceMock
            .Setup(ds => ds.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => _capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());
    }

    private CommunicationService CreateService(
        CommunicationOptions? options = null, ICommunicationDataverseService? communicationDataverse = null)
    {
        var opts = options ?? CreateDefaultOptions();
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(opts),
            accountService,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var emlGenerationService = new EmlGenerationService(
            Mock.Of<ILogger<EmlGenerationService>>());

        var fakeGraphFactory = Mock.Of<IGraphClientFactory>();
        var speFileStore = new SpeFileStore(
            new ContainerOperations(fakeGraphFactory, TestSpeOwnership.AllowAll(fakeGraphFactory), Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(fakeGraphFactory, TestSpeOwnership.AllowAll(fakeGraphFactory), Mock.Of<ILogger<DriveItemOperations>>()),
            new UploadSessionManager(fakeGraphFactory, TestSpeOwnership.AllowAll(fakeGraphFactory), Mock.Of<IHttpClientFactory>(), Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(fakeGraphFactory, Mock.Of<ILogger<UserOperations>>()));

        return new CommunicationService(
            CommunicationChannelTestFactory.CreateDispatcher(_graphClientFactoryMock.Object, emlGenerationService),
            senderValidator,
            communicationDataverse ?? Mock.Of<ICommunicationDataverseService>(),
            _genericEntityServiceMock.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService — not tested here
            null!, // JobSubmissionService — not tested here
            Mock.Of<ICommunicationEnrichmentService>(),
            Options.Create(opts),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            _loggerMock.Object,
            scopeFactory: SpeScopeFactoryStub.Create(speFileStore));
    }

    private static CommunicationOptions CreateDefaultOptions() => new()
    {
        ApprovedSenders = new[]
        {
            new ApprovedSenderConfig
            {
                Email = "noreply@contoso.com",
                DisplayName = "Contoso Notifications",
                IsDefault = true
            }
        },
        DefaultMailbox = "noreply@contoso.com"
    };

    private static SendCommunicationRequest CreateValidRequest(
        string[]? to = null,
        string[]? cc = null,
        string[]? bcc = null,
        string subject = "Test Subject",
        string body = "<p>Test body</p>",
        BodyFormat bodyFormat = BodyFormat.HTML,
        string? fromMailbox = null,
        CommunicationType communicationType = CommunicationType.Email,
        CommunicationAssociation[]? associations = null,
        string? correlationId = "test-corr-001") => new()
        {
            To = to ?? new[] { "recipient@example.com" },
            Cc = cc,
            Bcc = bcc,
            Subject = subject,
            Body = body,
            BodyFormat = bodyFormat,
            FromMailbox = fromMailbox,
            CommunicationType = communicationType,
            Associations = associations,
            CorrelationId = correlationId
        };

    private static GraphServiceClient CreateMockGraphClient(
        HttpStatusCode responseCode = HttpStatusCode.Accepted)
    {
        var handler = new MockHttpMessageHandler(responseCode);
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;

        public MockHttpMessageHandler(HttpStatusCode statusCode)
        {
            _statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }

    #endregion

    #region Communication Type Field (sprk_communicationtype)

    [Fact]
    public async Task SendAsync_SetsSprkCommuniationType_ToEmailValue()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(communicationType: CommunicationType.Email);

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var optionSet = _capturedEntity!["sprk_communicationtype"] as OptionSetValue;
        optionSet.Should().NotBeNull();
        optionSet!.Value.Should().Be(100000000, "CommunicationType.Email = 100000000");
    }

    #endregion

    #region Status Code Field (statuscode)

    [Fact]
    public async Task SendAsync_SetsStatusCode_ToSendValue()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var optionSet = _capturedEntity!["statuscode"] as OptionSetValue;
        optionSet.Should().NotBeNull();
        optionSet!.Value.Should().Be(659490002, "CommunicationStatus.Send = 659490002");
    }

    #endregion

    #region State Code Field (statecode)

    [Fact]
    public async Task SendAsync_SetsStateCode_ToActive()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var optionSet = _capturedEntity!["statecode"] as OptionSetValue;
        optionSet.Should().NotBeNull();
        optionSet!.Value.Should().Be(0, "statecode 0 = Active");
    }

    #endregion

    #region Direction Field (sprk_direction)

    [Fact]
    public async Task SendAsync_SetsDirection_ToOutgoing()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var optionSet = _capturedEntity!["sprk_direction"] as OptionSetValue;
        optionSet.Should().NotBeNull();
        optionSet!.Value.Should().Be(100000001, "CommunicationDirection.Outgoing = 100000001");
    }

    #endregion

    #region Body Format Field (sprk_bodyformat)

    [Fact]
    public async Task SendAsync_SetsBodyFormat_ToHtmlValue()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(bodyFormat: BodyFormat.HTML);

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var optionSet = _capturedEntity!["sprk_bodyformat"] as OptionSetValue;
        optionSet.Should().NotBeNull();
        optionSet!.Value.Should().Be(100000001, "BodyFormat.HTML = 100000001");
    }

    [Fact]
    public async Task SendAsync_SetsBodyFormat_ToPlainTextValue()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(bodyFormat: BodyFormat.PlainText);

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var optionSet = _capturedEntity!["sprk_bodyformat"] as OptionSetValue;
        optionSet.Should().NotBeNull();
        optionSet!.Value.Should().Be(100000000, "BodyFormat.PlainText = 100000000");
    }

    #endregion

    #region Recipient Fields (sprk_to, sprk_cc, sprk_bcc)

    [Fact]
    public async Task SendAsync_SetsToField_SemicolonJoined()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(to: new[] { "alice@example.com", "bob@example.com", "carol@example.com" });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_to"].Should().Be("alice@example.com; bob@example.com; carol@example.com");
    }

    [Fact]
    public async Task SendAsync_SetsCcField_WhenProvided()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(cc: new[] { "cc1@example.com", "cc2@example.com" });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_cc"].Should().Be("cc1@example.com; cc2@example.com");
    }

    [Fact]
    public async Task SendAsync_SetsBccField_WhenProvided()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(bcc: new[] { "bcc@secret.com" });

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_bcc"].Should().Be("bcc@secret.com");
    }

    [Fact]
    public async Task SendAsync_DoesNotSetCcField_WhenNull()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(cc: null);

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!.Attributes.ContainsKey("sprk_cc").Should().BeFalse(
            "sprk_cc should not be set when Cc is null");
    }

    [Fact]
    public async Task SendAsync_DoesNotSetBccField_WhenNull()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(bcc: null);

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!.Attributes.ContainsKey("sprk_bcc").Should().BeFalse(
            "sprk_bcc should not be set when Bcc is null");
    }

    [Fact]
    public async Task SendAsync_DoesNotSetCcField_WhenEmptyArray()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(cc: Array.Empty<string>());

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!.Attributes.ContainsKey("sprk_cc").Should().BeFalse(
            "sprk_cc should not be set when Cc is an empty array");
    }

    #endregion

    #region From Field (sprk_from)

    [Fact]
    public async Task SendAsync_SetsFromField_ToResolvedSenderEmail()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(); // uses default sender

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_from"].Should().Be("noreply@contoso.com");
    }

    #endregion

    #region Subject and Body Fields

    [Fact]
    public async Task SendAsync_SetsSubjectField()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(subject: "Important Email Subject");

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_subject"].Should().Be("Important Email Subject");
    }

    [Fact]
    public async Task SendAsync_SetsBodyField()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(body: "<p>Hello World</p>");

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_body"].Should().Be("<p>Hello World</p>");
    }

    #endregion

    #region Graph Message ID and Correlation Fields

    [Fact]
    public async Task SendAsync_SetsGraphMessageId_ToCorrelationId()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(correlationId: "my-corr-id-xyz");

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_graphmessageid"].Should().Be("my-corr-id-xyz");
    }

    [Fact]
    public async Task SendAsync_SetsCorrelationIdField()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(correlationId: "tracking-abc-123");

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_correlationid"].Should().Be("tracking-abc-123");
    }

    #endregion

    #region Sent At Field (sprk_sentat)

    [Fact]
    public async Task SendAsync_SetsSentAtField_ToApproximateUtcNow()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();
        var before = DateTime.UtcNow;

        // Act
        await service.SendAsync(request);

        // Assert
        var after = DateTime.UtcNow;
        _capturedEntity.Should().NotBeNull();
        var sentAt = (DateTime)_capturedEntity!["sprk_sentat"];
        sentAt.Should().BeOnOrAfter(before.AddSeconds(-1));
        sentAt.Should().BeOnOrBefore(after.AddSeconds(1));
    }

    #endregion

    #region Entity Name (sprk_name)

    [Fact]
    public async Task SendAsync_SetsEntityName_ToPrefixedSubject()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest(subject: "Test Subject Line");

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!["sprk_name"].Should().Be("Email: Test Subject Line");
    }

    [Fact]
    public async Task SendAsync_TruncatesEntityName_To200Characters()
    {
        // Arrange
        var service = CreateService();
        var longSubject = new string('A', 300);
        var request = CreateValidRequest(subject: longSubject);

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var name = (string)_capturedEntity!["sprk_name"];
        // "Email: " is 7 chars, so TruncateTo(subject, 200) truncates the subject, then the whole "Email: ..." is the sprk_name
        name.Length.Should().BeLessOrEqualTo(207, "sprk_name = 'Email: ' (7 chars) + TruncateTo(subject, 200)");
    }

    #endregion

    #region Entity Logical Name

    [Fact]
    public async Task SendAsync_CreatesEntityWithCorrectLogicalName()
    {
        // Arrange
        var service = CreateService();
        var request = CreateValidRequest();

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        _capturedEntity!.LogicalName.Should().Be("sprk_communication");
    }

    #endregion

    #region Internal-only flag and sender (task 065, external-access r3)

    // Owner 2026-10-13: every outbound record carries an EXPLICIT sprk_isinternalonly — the request value, else
    // false — because the external read hides a row whose flag is unset (fail closed). The sender is stamped from
    // the caller's oid through the existing oid → systemuserid resolution.

    private const string CallerOid = "33333333-3333-3333-3333-333333333333";
    private static readonly Guid CallerSystemUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static Microsoft.AspNetCore.Http.HttpContext CallerContext() => new Microsoft.AspNetCore.Http.DefaultHttpContext
    {
        User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            new[]
            {
                new System.Security.Claims.Claim("oid", CallerOid),
                new System.Security.Claims.Claim("preferred_username", "caller@contoso.com"),
            },
            "test"))
    };

    private static ICommunicationDataverseService ResolvingCaller()
    {
        var comm = new Mock<ICommunicationDataverseService>();
        comm.Setup(c => c.QuerySystemUserByAzureAdOidAsync(CallerOid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserId);
        return comm.Object;
    }

    private SendCommunicationRequest UserModeRequest(bool? isInternalOnly)
    {
        _graphClientFactoryMock
            .Setup(f => f.ForUserAsync(It.IsAny<Microsoft.AspNetCore.Http.HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateMockGraphClient());
        return CreateValidRequest() with { SendMode = SendMode.User, IsInternalOnly = isInternalOnly };
    }

    [Fact]
    public async Task SendAsync_SharedMailboxWithoutIsInternalOnly_WritesInternalOnlyFalse()
    {
        await CreateService().SendAsync(CreateValidRequest());

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_SharedMailboxWithIsInternalOnlyTrue_WritesInternalOnlyTrue()
    {
        await CreateService().SendAsync(CreateValidRequest() with { IsInternalOnly = true });

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_UserModeWithoutIsInternalOnly_WritesInternalOnlyFalse()
    {
        await CreateService().SendAsync(UserModeRequest(isInternalOnly: null), CallerContext());

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_UserModeWithIsInternalOnlyTrue_WritesInternalOnlyTrue()
    {
        await CreateService().SendAsync(UserModeRequest(isInternalOnly: true), CallerContext());

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    // ── Fix round 1 (adversarial pass): reply/forward quoting a parent, and emails into a Private thread ──

    private static readonly Guid ReplySourceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TargetThreadId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private SendCommunicationRequest EmailRequest(bool userMode, bool? isInternalOnly, Guid? inheritFrom = null, Guid? threadId = null)
    {
        var request = userMode ? UserModeRequest(isInternalOnly) : CreateValidRequest() with { IsInternalOnly = isInternalOnly };
        return request with { InheritRegardingFromCommunicationId = inheritFrom, ThreadId = threadId };
    }

    private Task SendEmailAsync(SendCommunicationRequest request) =>
        CreateService().SendAsync(request, CallerContext());

    private void ReplySourceReturns(bool? internalOnlyFlag)
    {
        var source = new DataverseEntity("sprk_communication", ReplySourceId);
        if (internalOnlyFlag is { } flag)
            source["sprk_isinternalonly"] = flag;
        _genericEntityServiceMock
            .Setup(s => s.RetrieveAsync("sprk_communication", ReplySourceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
    }

    private void ThreadPrivacyReturns(int? privacyState)
    {
        var thread = new DataverseEntity("sprk_communicationthread", TargetThreadId);
        if (privacyState is { } state)
            thread["sprk_privacystate"] = new OptionSetValue(state);
        _genericEntityServiceMock
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", TargetThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);
    }

    [Theory]
    [InlineData(false, true, true)]    // source flagged internal-only -> internal-only
    [InlineData(false, null, true)]    // source flag unset (legacy row) -> internal-only (fail closed)
    [InlineData(false, false, false)]  // source explicitly not internal-only -> not internal-only
    [InlineData(true, true, true)]
    [InlineData(true, null, true)]
    [InlineData(true, false, false)]
    public async Task SendAsync_EmailReplyWithoutExplicitFlag_FollowsTheQuotedParentsFlag(bool userMode, bool? sourceFlag, bool expected)
    {
        ReplySourceReturns(sourceFlag);

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: null, inheritFrom: ReplySourceId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_EmailReplyWhoseParentReadFails_WritesInternalOnly(bool userMode)
    {
        _genericEntityServiceMock
            .Setup(s => s.RetrieveAsync("sprk_communication", ReplySourceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: null, inheritFrom: ReplySourceId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_EmailReplyWhoseParentRowIsNull_WritesInternalOnly(bool userMode)
    {
        _genericEntityServiceMock
            .Setup(s => s.RetrieveAsync("sprk_communication", ReplySourceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity)null!);

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: null, inheritFrom: ReplySourceId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Theory]
    [InlineData(false, true, false, false)]   // explicit false beats a source flagged true
    [InlineData(false, false, true, true)]    // explicit true beats a source explicitly false
    [InlineData(false, null, false, false)]   // explicit false beats an unset source flag
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, null, false, false)]
    public async Task SendAsync_EmailReplyWithExplicitFlag_TheExplicitValueWins(bool userMode, bool? sourceFlag, bool explicitFlag, bool expected)
    {
        ReplySourceReturns(sourceFlag);

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: explicitFlag, inheritFrom: ReplySourceId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_EmailReplyWithExplicitFlagAndFailingParentRead_TheExplicitValueWins(bool userMode)
    {
        _genericEntityServiceMock
            .Setup(s => s.RetrieveAsync("sprk_communication", ReplySourceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: false, inheritFrom: ReplySourceId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 100000001, true)]   // Private thread -> internal-only
    [InlineData(false, 100000000, false)]  // Open thread -> not internal-only
    [InlineData(false, null, true)]        // privacy value missing -> not confirmed Open -> internal-only
    [InlineData(true, 100000001, true)]
    [InlineData(true, 100000000, false)]
    [InlineData(true, null, true)]
    public async Task SendAsync_EmailIntoThreadWithoutExplicitFlag_FollowsThreadPrivacy(bool userMode, int? privacyState, bool expected)
    {
        ThreadPrivacyReturns(privacyState);

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: null, threadId: TargetThreadId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_EmailIntoThreadWhosePrivacyReadThrows_WritesInternalOnly(bool userMode)
    {
        _genericEntityServiceMock
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", TargetThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: null, threadId: TargetThreadId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_EmailIntoThreadWhosePrivacyRowIsNull_WritesInternalOnly(bool userMode)
    {
        _genericEntityServiceMock
            .Setup(s => s.RetrieveAsync("sprk_communicationthread", TargetThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DataverseEntity)null!);

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: null, threadId: TargetThreadId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendAsync_EmailIntoPrivateThreadWithExplicitFalse_TheExplicitValueWins(bool userMode)
    {
        ThreadPrivacyReturns(100000001); // Private

        await SendEmailAsync(EmailRequest(userMode, isInternalOnly: false, threadId: TargetThreadId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_EmailReplyIntoPrivateThreadWhoseParentIsNotInternal_WritesInternalOnly()
    {
        // Either signal saying "internal-only" is enough: an Open parent does not lift a Private target thread.
        ReplySourceReturns(false);
        ThreadPrivacyReturns(100000001); // Private

        await SendEmailAsync(EmailRequest(userMode: false, isInternalOnly: null, inheritFrom: ReplySourceId, threadId: TargetThreadId));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_EmailWithNoParentAndNoThread_StillWritesNotInternalOnly()
    {
        await SendEmailAsync(EmailRequest(userMode: false, isInternalOnly: null));

        _capturedEntity!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_SharedMailbox_StampsSentByWithTheCallingSystemUser()
    {
        await CreateService(communicationDataverse: ResolvingCaller()).SendAsync(CreateValidRequest(), CallerContext());

        var sentBy = _capturedEntity!.GetAttributeValue<EntityReference>("sprk_sentby");
        sentBy.Should().NotBeNull();
        sentBy!.LogicalName.Should().Be("systemuser");
        sentBy.Id.Should().Be(CallerSystemUserId);
    }

    [Fact]
    public async Task SendAsync_SharedMailboxWhenCallerOidUnresolvable_CreatesRecordWithoutSentByAndLogsWarning()
    {
        // The default ICommunicationDataverseService double resolves no systemuser for the oid.
        var response = await CreateService().SendAsync(CreateValidRequest(), CallerContext());

        response.CommunicationId.Should().NotBeNull("the email is sent and its record is still created");
        _capturedEntity!.Attributes.ContainsKey("sprk_sentby").Should().BeFalse();
        _loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("sprk_sentby")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region Dataverse Failure Handling

    [Fact]
    public async Task SendAsync_WhenDataverseCreateFails_StillReturnsSendSuccess()
    {
        // Arrange - make Dataverse throw
        _genericEntityServiceMock
            .Setup(ds => ds.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse is down"));

        var service = CreateService();
        var request = CreateValidRequest();

        // Act
        var response = await service.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(CommunicationStatus.Send, "email was sent successfully; Dataverse failure is non-fatal");
        response.CommunicationId.Should().BeNull("Dataverse record creation failed");
    }

    [Fact]
    public async Task SendAsync_WhenDataverseCreateSucceeds_ReturnsCommunicationId()
    {
        // Arrange
        var expectedId = Guid.NewGuid();
        _genericEntityServiceMock
            .Setup(ds => ds.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedId);

        var service = CreateService();
        var request = CreateValidRequest();

        // Act
        var response = await service.SendAsync(request);

        // Assert
        response.CommunicationId.Should().Be(expectedId);
    }

    #endregion
}
