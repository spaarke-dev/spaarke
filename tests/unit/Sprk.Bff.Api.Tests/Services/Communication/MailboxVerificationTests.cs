using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class MailboxVerificationTests
{
    #region Test Infrastructure

    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<IDataverseService> _dataverseMock;
    private readonly Mock<ILogger<MailboxVerificationService>> _loggerMock;
    private readonly MailboxVerificationService _sut;

    public MailboxVerificationTests()
    {
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _dataverseMock = new Mock<IDataverseService>();
        _loggerMock = new Mock<ILogger<MailboxVerificationService>>();

        // Setup default: UpdateAsync succeeds silently
        _dataverseMock
            .Setup(d => d.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Create service with real dependencies where possible
        _sut = CreateService();
    }

    private MailboxVerificationService CreateService()
    {
        // CommunicationAccountService requires IDistributedCache, IDataverseService, ILogger
        // But MailboxVerificationService.VerifyAsync doesn't call it — it queries Dataverse directly.
        // So we pass a minimal stub.
        var cacheMock = new Mock<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();
        var accountServiceLogger = new Mock<ILogger<CommunicationAccountService>>();
        var accountService = new CommunicationAccountService(
            _dataverseMock.Object, _dataverseMock.Object, cacheMock.Object, accountServiceLogger.Object);

        var communicationOptions = Options.Create(new CommunicationOptions
        {
            WebhookNotificationUrl = "https://localhost/api/webhooks/graph",
            WebhookClientState = "test-client-state",
            ApprovedSenders = [new ApprovedSenderConfig { Email = "test@contoso.com", DisplayName = "Test" }]
        });

        return new MailboxVerificationService(
            _graphClientFactoryMock.Object,
            _dataverseMock.Object,
            accountService,
            communicationOptions,
            _loggerMock.Object);
    }

    #endregion

    #region Test Data Builders

    private static DataverseEntity CreateAccountEntity(
        Guid? id = null,
        string email = "shared@contoso.com",
        string name = "Shared Mailbox",
        string? displayName = "Contoso Shared",
        bool sendEnabled = true,
        bool receiveEnabled = false,
        int accountType = 100000000)
    {
        var entity = new DataverseEntity("sprk_communicationaccount");
        entity.Id = id ?? Guid.NewGuid();
        entity["sprk_emailaddress"] = email;
        entity["sprk_name"] = name;
        if (displayName != null) entity["sprk_displayname"] = displayName;
        entity["sprk_sendenabled"] = sendEnabled;
        entity["sprk_receiveenabled"] = receiveEnabled;
        entity["sprk_accounttype"] = new OptionSetValue(accountType);
        return entity;
    }

    private void SetupAccountRetrieval(DataverseEntity entity)
    {
        _dataverseMock
            .Setup(d => d.RetrieveAsync(
                "sprk_communicationaccount",
                entity.Id,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);
    }

    private void SetupAccountNotFound(Guid accountId)
    {
        _dataverseMock
            .Setup(d => d.RetrieveAsync(
                "sprk_communicationaccount",
                accountId,
                It.IsAny<string[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity not found"));
    }

    /// <summary>
    /// Sets up the Graph client factory to return a mock-like client.
    /// For send tests, we use a real GraphServiceClient that will fail with a predictable error
    /// unless we intercept at the HTTP level.
    /// Since we can't easily mock GraphServiceClient's fluent API, we use a custom HttpMessageHandler.
    /// </summary>
    private void SetupGraphClientWithHandler(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        var graphClient = new GraphServiceClient(httpClient);

        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(graphClient);
    }

    #endregion

    #region Successful Verification

    [Fact]
    public async Task VerifyAsync_SendEnabled_ReturnsVerifiedWhenGraphSucceeds()
    {
        // Arrange
        var entity = CreateAccountEntity(sendEnabled: true, receiveEnabled: false);
        SetupAccountRetrieval(entity);

        // Setup Graph to return success for sendMail (202 with empty body as Graph does)
        var handler = new FakeHttpHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["POST:users/shared@contoso.com/sendMail"] = new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
            {
                Content = new StringContent("", System.Text.Encoding.UTF8, "application/json")
            },
        });
        SetupGraphClientWithHandler(handler);

        // Act
        var result = await _sut.VerifyAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result.AccountId.Should().Be(entity.Id);
        result.EmailAddress.Should().Be("shared@contoso.com");
        result.Status.Should().Be(VerificationStatus.Verified);
        result.SendCapabilityVerified.Should().BeTrue();
        result.ReadCapabilityVerified.Should().BeNull(); // Not tested (receiveEnabled=false)
        result.FailureReason.Should().BeNull();
        result.VerifiedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        // Verify Dataverse was updated with Verified status
        _dataverseMock.Verify(d => d.UpdateAsync(
            "sprk_communicationaccount",
            entity.Id,
            It.Is<Dictionary<string, object>>(fields =>
                fields.ContainsKey("sprk_verificationstatus") &&
                fields.ContainsKey("sprk_lastverified") &&
                fields.ContainsKey("sprk_verificationmessage")),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task VerifyAsync_BothCapabilitiesEnabled_VerifiesSendAndRead()
    {
        // Arrange
        var entity = CreateAccountEntity(
            email: "both@contoso.com",
            sendEnabled: true,
            receiveEnabled: true);
        SetupAccountRetrieval(entity);

        var handler = new FakeHttpHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["POST:users/both@contoso.com/sendMail"] = new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
            {
                Content = new StringContent("", System.Text.Encoding.UTF8, "application/json")
            },
            ["GET:users/both@contoso.com/messages"] = CreateJsonResponse(
                """{"value":[],"@odata.context":"https://graph.microsoft.com/v1.0/$metadata#users('both%40contoso.com')/messages"}"""),
        });
        SetupGraphClientWithHandler(handler);

        // Act
        var result = await _sut.VerifyAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(VerificationStatus.Verified);
        result.SendCapabilityVerified.Should().BeTrue();
        result.ReadCapabilityVerified.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_NeitherCapabilityEnabled_ReturnsVerified()
    {
        // Arrange — account has neither send nor receive enabled
        var entity = CreateAccountEntity(sendEnabled: false, receiveEnabled: false);
        SetupAccountRetrieval(entity);

        // Act
        var result = await _sut.VerifyAsync(entity.Id);

        // Assert — nothing to test, so overall status is Verified
        result.Should().NotBeNull();
        result.Status.Should().Be(VerificationStatus.Verified);
        result.SendCapabilityVerified.Should().BeNull();
        result.ReadCapabilityVerified.Should().BeNull();
    }

    #endregion

    #region Graph Failure (Service Unavailable)

    [Fact]
    public async Task VerifyAsync_GraphServiceUnavailable_ReturnsFailedStatus()
    {
        // Arrange
        var entity = CreateAccountEntity(sendEnabled: true, receiveEnabled: false);
        SetupAccountRetrieval(entity);

        var handler = new FakeHttpHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["POST:users/shared@contoso.com/sendMail"] = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    """{"error":{"code":"ServiceUnavailable","message":"The service is temporarily unavailable."}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
        });
        SetupGraphClientWithHandler(handler);

        // Act
        var result = await _sut.VerifyAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(VerificationStatus.Failed);
        result.SendCapabilityVerified.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task VerifyAsync_ReadGraphFailure_ReturnsFailedStatus()
    {
        // Arrange
        var entity = CreateAccountEntity(
            email: "readfail@contoso.com",
            sendEnabled: false,
            receiveEnabled: true);
        SetupAccountRetrieval(entity);

        var handler = new FakeHttpHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["GET:users/readfail@contoso.com/messages"] = new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    """{"error":{"code":"ServiceUnavailable","message":"The service is temporarily unavailable."}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
        });
        SetupGraphClientWithHandler(handler);

        // Act
        var result = await _sut.VerifyAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(VerificationStatus.Failed);
        result.ReadCapabilityVerified.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Permission Denied

    [Fact]
    public async Task VerifyAsync_PermissionDenied_ReturnsFailedWithReason()
    {
        // Arrange
        var entity = CreateAccountEntity(sendEnabled: true, receiveEnabled: false);
        SetupAccountRetrieval(entity);

        var handler = new FakeHttpHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["POST:users/shared@contoso.com/sendMail"] = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    """{"error":{"code":"Authorization_RequestDenied","message":"Insufficient privileges to complete the operation."}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
        });
        SetupGraphClientWithHandler(handler);

        // Act
        var result = await _sut.VerifyAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result.Status.Should().Be(VerificationStatus.Failed);
        result.SendCapabilityVerified.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
        result.FailureReason.Should().Contain("Send test failed");
    }

    #endregion

    #region Account Not Found

    [Fact]
    public async Task VerifyAsync_AccountNotFound_ReturnsNull()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        SetupAccountNotFound(accountId);

        // Act
        var result = await _sut.VerifyAsync(accountId);

        // Assert — null signals 404 to the endpoint handler
        result.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_AccountWithNoEmail_ReturnsNull()
    {
        // Arrange — entity exists but has empty email
        var entity = CreateAccountEntity(email: "", sendEnabled: true);
        SetupAccountRetrieval(entity);

        // Act
        var result = await _sut.VerifyAsync(entity.Id);

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region Dataverse Status Update

    [Fact]
    public async Task VerifyAsync_SetsPendingStatusBeforeTestingCapabilities()
    {
        // Arrange
        var entity = CreateAccountEntity(sendEnabled: false, receiveEnabled: false);
        SetupAccountRetrieval(entity);

        var updateCalls = new List<(VerificationStatus status, DateTime? timestamp)>();
        _dataverseMock
            .Setup(d => d.UpdateAsync(
                "sprk_communicationaccount",
                entity.Id,
                It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) =>
            {
                var statusValue = ((OptionSetValue)fields["sprk_verificationstatus"]).Value;
                var timestamp = fields.ContainsKey("sprk_lastverified")
                    ? (DateTime?)fields["sprk_lastverified"]
                    : null;
                updateCalls.Add(((VerificationStatus)statusValue, timestamp));
            })
            .Returns(Task.CompletedTask);

        // Act
        await _sut.VerifyAsync(entity.Id);

        // Assert — first call should be Pending (no timestamp), second should be Verified (with timestamp)
        updateCalls.Should().HaveCount(2);
        updateCalls[0].status.Should().Be(VerificationStatus.Pending);
        updateCalls[0].timestamp.Should().BeNull();
        updateCalls[1].status.Should().Be(VerificationStatus.Verified);
        updateCalls[1].timestamp.Should().NotBeNull();
    }

    [Fact]
    public async Task VerifyAsync_PersistsVerificationMessage()
    {
        // Arrange
        var entity = CreateAccountEntity(sendEnabled: true, receiveEnabled: false);
        SetupAccountRetrieval(entity);

        // Force a send failure so there's a failure reason to persist
        var handler = new FakeHttpHandler(new Dictionary<string, HttpResponseMessage>
        {
            ["POST:users/shared@contoso.com/sendMail"] = new HttpResponseMessage(System.Net.HttpStatusCode.Forbidden)
            {
                Content = new StringContent(
                    """{"error":{"code":"Authorization_RequestDenied","message":"Insufficient privileges."}}""",
                    System.Text.Encoding.UTF8,
                    "application/json")
            },
        });
        SetupGraphClientWithHandler(handler);

        string? persistedMessage = null;
        _dataverseMock
            .Setup(d => d.UpdateAsync(
                "sprk_communicationaccount",
                entity.Id,
                It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_verificationmessage")),
                It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) =>
            {
                persistedMessage = fields["sprk_verificationmessage"]?.ToString();
            })
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.VerifyAsync(entity.Id);

        // Assert
        result.Status.Should().Be(VerificationStatus.Failed);
        persistedMessage.Should().NotBeNullOrEmpty();
        persistedMessage.Should().Contain("Send test failed");
    }

    #endregion

    #region Helpers

    private static HttpResponseMessage CreateJsonResponse(string json, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
    }

    /// <summary>
    /// Simple HTTP handler that matches requests by "{Method}:{path-prefix}" patterns.
    /// Used to simulate Graph API responses without real network calls.
    /// </summary>
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpResponseMessage> _responses;

        public FakeHttpHandler(Dictionary<string, HttpResponseMessage> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var method = request.Method.Method.ToUpperInvariant();
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            var path = request.RequestUri?.AbsolutePath?.TrimStart('/') ?? string.Empty;

            // Remove /v1.0/ prefix if present
            if (path.StartsWith("v1.0/"))
                path = path[5..];

            // URL-decode the path for matching (Graph SDK may encode @ etc.)
            path = Uri.UnescapeDataString(path);

            foreach (var kvp in _responses)
            {
                var parts = kvp.Key.Split(':', 2);
                if (parts.Length == 2
                    && string.Equals(parts[0], method, StringComparison.OrdinalIgnoreCase)
                    && path.Contains(parts[1], StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(kvp.Value);
                }
            }

            // Default: return 404 for unmatched requests
            var errorJson = "{\"error\":{\"code\":\"Request_ResourceNotFound\",\"message\":\"No fake response for " + method + " " + path + "\"}}";
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent(errorJson, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    #endregion
}
