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
using System.Net;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tests that CreateDataverseRecordAsync sets correct field names and values
/// on the sprk_communication entity during SendAsync.
/// </summary>
public class DataverseRecordCreationTests
{
    #region Test Infrastructure

    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<CommunicationService>> _loggerMock;
    private DataverseEntity? _capturedEntity;

    public DataverseRecordCreationTests()
    {
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<CommunicationService>>();

        // Default: Graph succeeds (202 Accepted)
        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateMockGraphClient());

        // Capture the Entity passed to CreateAsync
        _dataverseServiceMock
            .Setup(ds => ds.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => _capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());
    }

    private CommunicationService CreateService(CommunicationOptions? options = null)
    {
        var opts = options ?? CreateDefaultOptions();
        var accountService = new CommunicationAccountService(
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
            new ContainerOperations(fakeGraphFactory, Mock.Of<ILogger<ContainerOperations>>()),
            new DriveItemOperations(fakeGraphFactory, Mock.Of<ILogger<DriveItemOperations>>()),
            new UploadSessionManager(fakeGraphFactory, Mock.Of<ILogger<UploadSessionManager>>()),
            new UserOperations(fakeGraphFactory, Mock.Of<ILogger<UserOperations>>()));

        return new CommunicationService(
            _graphClientFactoryMock.Object,
            senderValidator,
            _dataverseServiceMock.Object,
            emlGenerationService,
            speFileStore,
            Options.Create(opts),
            _loggerMock.Object);
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

    #region Communication Type Field (sprk_communiationtype - intentional typo)

    [Fact]
    public async Task SendAsync_SetsSprkCommuniationType_ToEmailValue()
    {
        // Arrange - note the intentional typo "communiation" matching actual Dataverse schema
        var service = CreateService();
        var request = CreateValidRequest(communicationType: CommunicationType.Email);

        // Act
        await service.SendAsync(request);

        // Assert
        _capturedEntity.Should().NotBeNull();
        var optionSet = _capturedEntity!["sprk_communiationtype"] as OptionSetValue;
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

    #region Dataverse Failure Handling

    [Fact]
    public async Task SendAsync_WhenDataverseCreateFails_StillReturnsSendSuccess()
    {
        // Arrange - make Dataverse throw
        _dataverseServiceMock
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
        _dataverseServiceMock
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
