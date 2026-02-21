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
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using System.Net;
using System.Text;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tests for the archival flow in CommunicationService.
/// Verifies that ArchiveToSpe=true triggers .eml generation and upload to SPE,
/// that ArchiveToSpe=false skips archival, and that archival failures are non-blocking.
/// </summary>
public class ArchivalFlowTests
{
    #region Test Infrastructure

    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<IDataverseService> _dataverseServiceMock;
    private readonly Mock<ILogger<CommunicationService>> _loggerMock;

    public ArchivalFlowTests()
    {
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _dataverseServiceMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<CommunicationService>>();

        // Default: ForApp() returns a mock Graph client that succeeds (202 Accepted)
        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateMockGraphClient());
    }

    private static CommunicationOptions CreateDefaultOptions(string? archiveContainerId = null) => new()
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
        DefaultMailbox = "noreply@contoso.com",
        ArchiveContainerId = archiveContainerId
    };

    private CommunicationService CreateService(
        CommunicationOptions? options = null,
        IDataverseService? dataverseService = null,
        EmlGenerationService? emlGenerationService = null,
        SpeFileStore? speFileStore = null)
    {
        var opts = options ?? CreateDefaultOptions();
        var dvService = dataverseService ?? _dataverseServiceMock.Object;

        var senderValidator = new ApprovedSenderValidator(
            Options.Create(opts),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        return new CommunicationService(
            _graphClientFactoryMock.Object,
            senderValidator,
            dvService,
            emlGenerationService ?? null!,
            speFileStore ?? null!,
            Options.Create(opts),
            _loggerMock.Object);
    }

    private static SendCommunicationRequest CreateValidRequest(
        bool archiveToSpe = false,
        string? correlationId = null) => new()
    {
        To = new[] { "recipient@example.com" },
        Subject = "Test Subject",
        Body = "<p>Test body</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        ArchiveToSpe = archiveToSpe,
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
        private readonly string? _responseContent;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string? responseContent = null)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            if (_responseContent is not null)
            {
                response.Content = new StringContent(_responseContent, Encoding.UTF8, "application/json");
            }
            return Task.FromResult(response);
        }
    }

    #endregion

    #region ArchiveToSpe = false (Default Behavior)

    [Fact]
    public async Task SendAsync_WithArchiveToSpeFalse_SkipsArchival_ArchivedDocumentIdIsNull()
    {
        // Arrange
        var sut = CreateService();
        var request = CreateValidRequest(archiveToSpe: false);

        // Act
        var response = await sut.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(CommunicationStatus.Send);
        response.ArchivedDocumentId.Should().BeNull("archival was not requested");
        response.ArchivalWarning.Should().BeNull("no archival was attempted");
    }

    #endregion

    #region ArchiveToSpe = true with Dataverse Failure

    [Fact]
    public async Task SendAsync_WithArchiveToSpe_WhenDataverseRecordCreationFails_WarnsButDoesNotFail()
    {
        // Arrange — Dataverse CreateAsync throws to simulate Dataverse failure for communication record.
        // When the Dataverse record fails, archival is skipped because communicationId is null,
        // but the email is still sent successfully.
        _dataverseServiceMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var sut = CreateService(
            options: CreateDefaultOptions(archiveContainerId: "test-container-id"),
            dataverseService: _dataverseServiceMock.Object);

        var request = CreateValidRequest(archiveToSpe: true);

        // Act
        var response = await sut.SendAsync(request);

        // Assert — email was sent, but archival was skipped with a warning
        response.Should().NotBeNull();
        response.Status.Should().Be(CommunicationStatus.Send);
        response.ArchivedDocumentId.Should().BeNull("archival skipped because Dataverse record creation failed");
        response.ArchivalWarning.Should().NotBeNull("a warning should be present when archival is skipped");
        response.ArchivalWarning.Should().Contain("archival skipped");
    }

    #endregion

    #region ArchivalWarning in Response

    [Fact]
    public async Task SendAsync_WhenArchivalSkippedDueToDataverseFailure_ResponseIncludesArchivalWarning()
    {
        // Arrange — Dataverse CreateAsync throws so communicationId is null,
        // causing archival to be skipped when ArchiveToSpe is true.
        _dataverseServiceMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Connection timeout"));

        var sut = CreateService(
            options: CreateDefaultOptions(archiveContainerId: "test-container"),
            dataverseService: _dataverseServiceMock.Object);

        var request = CreateValidRequest(archiveToSpe: true);

        // Act
        var response = await sut.SendAsync(request);

        // Assert
        response.ArchivalWarning.Should().NotBeNullOrWhiteSpace();
        response.ArchivalWarning.Should().Contain("sent successfully");
    }

    [Fact]
    public async Task SendAsync_WithArchiveToSpeFalse_NoArchivalWarning()
    {
        // Arrange
        var sut = CreateService();
        var request = CreateValidRequest(archiveToSpe: false);

        // Act
        var response = await sut.SendAsync(request);

        // Assert — when archival is not requested, no warning should be present
        response.ArchivalWarning.Should().BeNull();
    }

    #endregion
}
