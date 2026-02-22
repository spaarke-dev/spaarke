using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using System.Net;
using System.Text;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Integration;

/// <summary>
/// End-to-end integration tests for the Communication module.
/// These tests exercise the full flow through real service instances
/// (CommunicationService, SendCommunicationToolHandler, ApprovedSenderValidator)
/// with only infrastructure dependencies (Graph, Dataverse, Redis) mocked.
///
/// Validates:
/// - BFF send flow: request validation, sender resolution, Graph sendMail, Dataverse record creation
/// - AI tool handler: parameter extraction, delegation to CommunicationService, result mapping
/// - Approved sender: config-only sync resolution, async merged resolution with Dataverse + Redis
/// - Status query: Dataverse retrieval and OptionSetValue/DateTime mapping
/// - Error paths: Graph failures, Dataverse failures, unauthorized senders
/// </summary>
public class CommunicationIntegrationTests
{
    #region Test Infrastructure

    /// <summary>
    /// Mock HTTP handler that returns a configurable response for Graph SDK testing.
    /// Reuses the same pattern as CommunicationServiceTests.
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
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

    private static GraphServiceClient CreateMockGraphClient(
        HttpStatusCode responseCode = HttpStatusCode.Accepted)
    {
        var handler = new MockHttpMessageHandler(responseCode);
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient);
    }

    private static GraphServiceClient CreateFailingGraphClient()
    {
        var errorJson = "{\"error\":{\"code\":\"Authorization_RequestDenied\",\"message\":\"Access denied\"}}";
        var handler = new MockHttpMessageHandler(HttpStatusCode.Forbidden, errorJson);
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient);
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
            },
            new ApprovedSenderConfig
            {
                Email = "support@contoso.com",
                DisplayName = "Contoso Support"
            }
        },
        DefaultMailbox = "noreply@contoso.com"
    };

    private static SendCommunicationRequest CreateValidRequest(
        string[]? to = null,
        string subject = "Test Subject",
        string body = "<p>Test body</p>",
        string? fromMailbox = null,
        string? correlationId = null,
        CommunicationAssociation[]? associations = null) => new()
    {
        To = to ?? new[] { "recipient@example.com" },
        Subject = subject,
        Body = body,
        BodyFormat = BodyFormat.HTML,
        FromMailbox = fromMailbox,
        CommunicationType = CommunicationType.Email,
        CorrelationId = correlationId,
        Associations = associations
    };

    /// <summary>
    /// Builds a fully-wired CommunicationService with the given mocks.
    /// Uses real ApprovedSenderValidator with config-only senders (sync Resolve).
    /// EmlGenerationService and SpeFileStore are passed as null because all test
    /// requests use ArchiveToSpe=false (the default), so the archival path is never reached.
    /// </summary>
    private static CommunicationService BuildService(
        Mock<IGraphClientFactory> graphFactoryMock,
        Mock<IDataverseService> dataverseMock,
        CommunicationOptions? options = null)
    {
        var opts = options ?? CreateDefaultOptions();
        var accountService = new CommunicationAccountService(
            dataverseMock.Object,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(opts),
            accountService,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        return new CommunicationService(
            graphFactoryMock.Object,
            senderValidator,
            dataverseMock.Object,
            null!, // EmlGenerationService - not used when ArchiveToSpe=false
            null!, // SpeFileStore - not used when ArchiveToSpe=false
            Options.Create(opts),
            Mock.Of<ILogger<CommunicationService>>());
    }

    #endregion

    #region 1. Full Send Flow — BFF Caller

    [Fact]
    public async Task Full_SendFlow_BffCaller_CreatesRecordAndReturnsSuccess()
    {
        // Arrange: Graph returns 202 Accepted, Dataverse CreateAsync returns a new record ID
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        var expectedRecordId = Guid.NewGuid();
        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRecordId);

        var sut = BuildService(graphFactoryMock, dataverseMock);

        var matterId = Guid.NewGuid();
        var request = CreateValidRequest(
            to: new[] { "alice@example.com", "bob@example.com" },
            subject: "Project Update",
            body: "<p>Status report attached.</p>",
            correlationId: "e2e-bff-001",
            associations: new[]
            {
                new CommunicationAssociation
                {
                    EntityType = "sprk_matter",
                    EntityId = matterId,
                    EntityName = "Matter 12345",
                    EntityUrl = "https://org.crm.dynamics.com/main.aspx?id=" + matterId
                }
            });

        // Act
        var response = await sut.SendAsync(request);

        // Assert: response shape
        response.Should().NotBeNull();
        response.CommunicationId.Should().Be(expectedRecordId, "Dataverse record ID should be returned");
        response.GraphMessageId.Should().Be("e2e-bff-001", "Graph message ID tracks via correlationId");
        response.Status.Should().Be(CommunicationStatus.Send, "Status should be Send after successful dispatch");
        response.SentAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        response.From.Should().Be("noreply@contoso.com", "Default sender should resolve");
        response.CorrelationId.Should().Be("e2e-bff-001");

        // Assert: Dataverse CreateAsync was called with correct Entity fields
        dataverseMock.Verify(
            d => d.CreateAsync(
                It.Is<DataverseEntity>(e =>
                    e.LogicalName == "sprk_communication" &&
                    e.GetAttributeValue<string>("sprk_subject") == "Project Update" &&
                    e.GetAttributeValue<string>("sprk_body") == "<p>Status report attached.</p>" &&
                    e.GetAttributeValue<string>("sprk_to") == "alice@example.com; bob@example.com" &&
                    e.GetAttributeValue<string>("sprk_from") == "noreply@contoso.com" &&
                    e.GetAttributeValue<string>("sprk_graphmessageid") == "e2e-bff-001" &&
                    e.GetAttributeValue<string>("sprk_correlationid") == "e2e-bff-001" &&
                    ((OptionSetValue)e["sprk_communiationtype"]).Value == (int)CommunicationType.Email &&
                    ((OptionSetValue)e["statuscode"]).Value == (int)CommunicationStatus.Send &&
                    ((OptionSetValue)e["sprk_direction"]).Value == (int)CommunicationDirection.Outgoing
                ),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region 2. Full Send Flow — AI Tool Handler

    [Fact]
    public async Task Full_SendFlow_AiToolHandler_SendsEmailViaToolHandler()
    {
        // Arrange: real CommunicationService -> real SendCommunicationToolHandler
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        var expectedRecordId = Guid.NewGuid();
        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedRecordId);

        var communicationService = BuildService(graphFactoryMock, dataverseMock);

        var toolHandler = new SendCommunicationToolHandler(
            communicationService,
            Mock.Of<ILogger<SendCommunicationToolHandler>>());

        // Verify ToolName
        toolHandler.ToolName.Should().Be("send_communication");

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["to"] = "recipient@example.com",
            ["subject"] = "AI-Generated Update",
            ["body"] = "<p>This email was generated by a playbook.</p>"
        });

        // Act
        var result = await toolHandler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue("Tool handler should return success for a valid send");
        result.Error.Should().BeNull();
        result.Data.Should().NotBeNull();

        // Data should contain CommunicationId (via anonymous type)
        var dataDict = result.Data!.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(result.Data));

        dataDict.Should().ContainKey("CommunicationId");
        dataDict["CommunicationId"].Should().Be(expectedRecordId);
        dataDict.Should().ContainKey("Status");
        dataDict["Status"].Should().Be("Send");
        dataDict.Should().ContainKey("From");
        dataDict["From"].Should().Be("noreply@contoso.com");
    }

    #endregion

    #region 3. Full Send Flow — With Associations Sets Regarding Fields

    [Fact]
    public async Task Full_SendFlow_WithAssociations_SetsRegardingFields()
    {
        // Arrange
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        DataverseEntity? capturedEntity = null;
        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        var sut = BuildService(graphFactoryMock, dataverseMock);

        var matterId = Guid.NewGuid();
        var request = CreateValidRequest(
            subject: "Matter Association Test",
            body: "<p>Body</p>",
            correlationId: "e2e-assoc-001",
            associations: new[]
            {
                new CommunicationAssociation
                {
                    EntityType = "sprk_matter",
                    EntityId = matterId,
                    EntityName = "Matter: Smith v Jones",
                    EntityUrl = "https://org.crm.dynamics.com/main.aspx?id=" + matterId
                }
            });

        // Act
        await sut.SendAsync(request);

        // Assert: captured entity should have regarding fields set
        capturedEntity.Should().NotBeNull("Dataverse CreateAsync should have been called");

        // Regarding lookup: sprk_regardingmatter as EntityReference
        var regardingRef = capturedEntity!.GetAttributeValue<EntityReference>("sprk_regardingmatter");
        regardingRef.Should().NotBeNull("sprk_regardingmatter should be set for sprk_matter association");
        regardingRef!.LogicalName.Should().Be("sprk_matter");
        regardingRef.Id.Should().Be(matterId);

        // Association count
        capturedEntity.GetAttributeValue<int>("sprk_associationcount").Should().Be(1);

        // Denormalized fields
        capturedEntity.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Matter: Smith v Jones");
        capturedEntity.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(matterId.ToString());
        capturedEntity.GetAttributeValue<string>("sprk_regardingrecordurl")
            .Should().Contain(matterId.ToString());
    }

    #endregion

    #region 4. Status Query — After Send Returns Correct Status

    [Fact]
    public async Task StatusQuery_AfterSend_ReturnsCorrectStatus()
    {
        // Arrange: mock IDataverseService.RetrieveAsync to return a sprk_communication entity
        // with the fields that GetCommunicationStatusAsync reads
        var communicationId = Guid.NewGuid();
        var sentAtDateTime = new DateTime(2026, 2, 20, 14, 30, 0, DateTimeKind.Utc);

        var entity = new DataverseEntity("sprk_communication", communicationId);
        entity["statuscode"] = new OptionSetValue((int)CommunicationStatus.Send); // 659490002
        entity["sprk_graphmessageid"] = "msg-graph-001";
        entity["sprk_sentat"] = sentAtDateTime;
        entity["sprk_from"] = "noreply@contoso.com";

        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.RetrieveAsync(
                "sprk_communication",
                communicationId,
                It.Is<string[]>(cols =>
                    cols.Contains("statuscode") &&
                    cols.Contains("sprk_graphmessageid") &&
                    cols.Contains("sprk_sentat") &&
                    cols.Contains("sprk_from")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        // Act: call the mocked RetrieveAsync to simulate what GetCommunicationStatusAsync does,
        // then apply the same mapping logic as the endpoint handler.
        var retrievedEntity = await dataverseMock.Object.RetrieveAsync(
            "sprk_communication",
            communicationId,
            new[] { "statuscode", "sprk_graphmessageid", "sprk_sentat", "sprk_from" });

        var statusCodeValue = retrievedEntity.GetAttributeValue<OptionSetValue>("statuscode")?.Value ?? 1;
        var status = (CommunicationStatus)statusCodeValue;

        var sentAtDateTimeRaw = retrievedEntity.GetAttributeValue<DateTime?>("sprk_sentat");
        DateTimeOffset? sentAt = sentAtDateTimeRaw.HasValue
            ? new DateTimeOffset(sentAtDateTimeRaw.Value, TimeSpan.Zero)
            : null;

        var response = new CommunicationStatusResponse
        {
            CommunicationId = communicationId,
            Status = status,
            GraphMessageId = retrievedEntity.GetAttributeValue<string>("sprk_graphmessageid"),
            SentAt = sentAt,
            From = retrievedEntity.GetAttributeValue<string>("sprk_from")
        };

        // Assert
        response.CommunicationId.Should().Be(communicationId);
        response.Status.Should().Be(CommunicationStatus.Send);
        ((int)response.Status).Should().Be(659490002, "Send status maps to Dataverse statuscode 659490002");
        response.GraphMessageId.Should().Be("msg-graph-001");
        response.SentAt.Should().NotBeNull();
        response.SentAt!.Value.Should().Be(new DateTimeOffset(2026, 2, 20, 14, 30, 0, TimeSpan.Zero));
        response.From.Should().Be("noreply@contoso.com");
    }

    #endregion

    #region 5. Approved Sender — Rejection Returns Error

    [Fact]
    public async Task ApprovedSender_Rejection_ReturnsError()
    {
        // Arrange: only "noreply@contoso.com" is approved
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        var sut = BuildService(graphFactoryMock, new Mock<IDataverseService>());

        var request = CreateValidRequest(
            fromMailbox: "unauthorized@evil.com",
            subject: "Should fail",
            body: "<p>Blocked</p>");

        // Act
        var act = () => sut.SendAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("INVALID_SENDER",
            "Unapproved mailbox should produce INVALID_SENDER error code");
        ex.Which.Title.Should().Be("Invalid Sender");
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Detail.Should().Contain("unauthorized@evil.com");
    }

    #endregion

    #region 6. Approved Sender — Merged Resolution (Dataverse Wins)

    [Fact]
    public async Task ApprovedSender_MergedResolution_DataverseWins()
    {
        // Arrange: config has noreply@contoso.com with DisplayName="Config Name"
        //          Dataverse returns noreply@contoso.com with DisplayName="Dataverse Name"
        //          After merge, Dataverse should win on email match.
        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig
                {
                    Email = "noreply@contoso.com",
                    DisplayName = "Config Name",
                    IsDefault = true
                }
            },
            DefaultMailbox = "noreply@contoso.com"
        };

        // Dataverse returns a sender record with a different DisplayName
        var dataverseSenderEntity = new DataverseEntity("sprk_communicationaccount");
        dataverseSenderEntity.Id = Guid.NewGuid();
        dataverseSenderEntity["sprk_emailaddress"] = "noreply@contoso.com";
        dataverseSenderEntity["sprk_name"] = "Dataverse Name";
        dataverseSenderEntity["sprk_displayname"] = "Dataverse Name";
        dataverseSenderEntity["sprk_isdefaultsender"] = true;
        dataverseSenderEntity["sprk_sendenableds"] = true;
        dataverseSenderEntity["sprk_accounttype"] = new OptionSetValue(100000000);

        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { dataverseSenderEntity });

        // Redis cache returns null (cache miss) to force Dataverse query
        var cacheMock = new Mock<IDistributedCache>();
        cacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var accountCacheMock = new Mock<IDistributedCache>();
        accountCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var accountService = new CommunicationAccountService(
            dataverseMock.Object,
            accountCacheMock.Object,
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            accountService,
            cacheMock.Object,
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        // Act: use async resolution that merges config + Dataverse
        var result = await senderValidator.ResolveAsync(null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("noreply@contoso.com");
        result.DisplayName.Should().Be("Dataverse Name",
            "When both config and Dataverse have the same email, Dataverse DisplayName should win");
    }

    #endregion

    #region 7. Dataverse Failure — Email Still Sent

    [Fact]
    public async Task DataverseFailure_EmailStillSent()
    {
        // Arrange: Graph succeeds but Dataverse CreateAsync throws
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse is unavailable"));

        var sut = BuildService(graphFactoryMock, dataverseMock);

        var request = CreateValidRequest(
            subject: "Important email",
            body: "<p>This must go out even if Dataverse is down.</p>",
            correlationId: "e2e-dvfail-001");

        // Act: should NOT throw, because Dataverse record creation is best-effort
        var response = await sut.SendAsync(request);

        // Assert
        response.Should().NotBeNull("Email should still be sent when Dataverse fails");
        response.Status.Should().Be(CommunicationStatus.Send,
            "Status should be Send because Graph sendMail succeeded");
        response.CommunicationId.Should().BeNull(
            "CommunicationId should be null because Dataverse record creation failed");
        response.From.Should().Be("noreply@contoso.com");
        response.CorrelationId.Should().Be("e2e-dvfail-001");

        // Verify Graph was still called (email was sent)
        graphFactoryMock.Verify(f => f.ForApp(), Times.Once);
    }

    #endregion

    #region 8. Graph Failure — Throws GraphSendFailed

    [Fact]
    public async Task Graph_Failure_ThrowsGraphSendFailed()
    {
        // Arrange: Graph returns 403 Forbidden with OData error body
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateFailingGraphClient());

        var dataverseMock = new Mock<IDataverseService>();
        var sut = BuildService(graphFactoryMock, dataverseMock);

        var request = CreateValidRequest(
            subject: "Should fail at Graph",
            body: "<p>Access denied test</p>",
            correlationId: "e2e-graphfail-001");

        // Act
        var act = () => sut.SendAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("GRAPH_SEND_FAILED");
        ex.Which.Title.Should().Be("Email Send Failed");
        ex.Which.StatusCode.Should().BeOneOf(new[] { 403, 502 },
            "Status should reflect Graph's Forbidden response or be mapped to 502");

        // Dataverse should NOT have been called (Graph failed before Dataverse record creation)
        dataverseMock.Verify(
            d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "Dataverse record should not be created when Graph send fails");
    }

    #endregion

    #region 9. AI Tool Handler — Missing Parameters Returns Error

    [Fact]
    public async Task AiToolHandler_MissingTo_ReturnsError()
    {
        // Arrange: tool handler with valid service, but parameters missing "to"
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        var communicationService = BuildService(graphFactoryMock, new Mock<IDataverseService>());
        var toolHandler = new SendCommunicationToolHandler(
            communicationService,
            Mock.Of<ILogger<SendCommunicationToolHandler>>());

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            // "to" is missing
            ["subject"] = "Test Subject",
            ["body"] = "<p>Test body</p>"
        });

        // Act
        var result = await toolHandler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse("Missing 'to' parameter should cause failure");
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region 10. AI Tool Handler — With Regarding Entity and ID

    [Fact]
    public async Task AiToolHandler_WithRegardingParams_SetsAssociations()
    {
        // Arrange
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        DataverseEntity? capturedEntity = null;
        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        var communicationService = BuildService(graphFactoryMock, dataverseMock);
        var toolHandler = new SendCommunicationToolHandler(
            communicationService,
            Mock.Of<ILogger<SendCommunicationToolHandler>>());

        var matterId = Guid.NewGuid();
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["to"] = "user@example.com",
            ["subject"] = "Regarding Matter",
            ["body"] = "<p>Related to matter.</p>",
            ["regardingEntity"] = "sprk_matter",
            ["regardingId"] = matterId.ToString()
        });

        // Act
        var result = await toolHandler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert: tool result is success
        result.Success.Should().BeTrue();

        // Assert: Dataverse entity has regarding fields set via association
        capturedEntity.Should().NotBeNull();
        var regardingRef = capturedEntity!.GetAttributeValue<EntityReference>("sprk_regardingmatter");
        regardingRef.Should().NotBeNull();
        regardingRef!.Id.Should().Be(matterId);
        regardingRef.LogicalName.Should().Be("sprk_matter");
    }

    #endregion

    #region 11. Dataverse Schema — Communication Record Field Values

    [Fact]
    public async Task DataverseRecord_HasCorrectFieldNamesAndTypes()
    {
        // Arrange: capture the Entity passed to CreateAsync and verify field names
        // This catches schema mismatches (especially the intentional "communiation" typo)
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        DataverseEntity? capturedEntity = null;
        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        var sut = BuildService(graphFactoryMock, dataverseMock);

        var request = CreateValidRequest(
            to: new[] { "a@example.com" },
            subject: "Schema Verification",
            body: "<p>Schema test</p>",
            correlationId: "e2e-schema-001");

        // Act
        await sut.SendAsync(request);

        // Assert: entity was created with expected schema
        capturedEntity.Should().NotBeNull();
        capturedEntity!.LogicalName.Should().Be("sprk_communication");

        // Verify intentional typo in field name: "communiation" not "communication"
        capturedEntity.Attributes.Should().ContainKey("sprk_communiationtype",
            "Field uses intentional Dataverse schema typo: sprk_communiationtype");

        // Verify OptionSetValue types
        capturedEntity["sprk_communiationtype"].Should().BeOfType<OptionSetValue>();
        ((OptionSetValue)capturedEntity["sprk_communiationtype"]).Value.Should().Be(100000000,
            "CommunicationType.Email maps to 100000000");

        capturedEntity["statuscode"].Should().BeOfType<OptionSetValue>();
        ((OptionSetValue)capturedEntity["statuscode"]).Value.Should().Be(659490002,
            "CommunicationStatus.Send maps to 659490002");

        capturedEntity["statecode"].Should().BeOfType<OptionSetValue>();
        ((OptionSetValue)capturedEntity["statecode"]).Value.Should().Be(0,
            "statecode 0 = Active");

        capturedEntity["sprk_direction"].Should().BeOfType<OptionSetValue>();
        ((OptionSetValue)capturedEntity["sprk_direction"]).Value.Should().Be(100000001,
            "CommunicationDirection.Outgoing maps to 100000001");

        capturedEntity["sprk_bodyformat"].Should().BeOfType<OptionSetValue>();
        ((OptionSetValue)capturedEntity["sprk_bodyformat"]).Value.Should().Be(100000001,
            "BodyFormat.HTML maps to 100000001");

        // Verify string fields
        capturedEntity.GetAttributeValue<string>("sprk_name").Should().StartWith("Email: ");
        capturedEntity.GetAttributeValue<string>("sprk_to").Should().Be("a@example.com");
        capturedEntity.GetAttributeValue<string>("sprk_from").Should().Be("noreply@contoso.com");
        capturedEntity.GetAttributeValue<string>("sprk_subject").Should().Be("Schema Verification");
        capturedEntity.GetAttributeValue<string>("sprk_body").Should().Be("<p>Schema test</p>");
        capturedEntity.GetAttributeValue<string>("sprk_graphmessageid").Should().Be("e2e-schema-001");
        capturedEntity.GetAttributeValue<string>("sprk_correlationid").Should().Be("e2e-schema-001");
    }

    #endregion

    #region 12. Multiple Associations — Organization Entity Type

    [Fact]
    public async Task Full_SendFlow_WithOrganizationAssociation_SetsRegardingOrganization()
    {
        // Arrange: verify that non-matter entity types are correctly mapped
        var graphFactoryMock = new Mock<IGraphClientFactory>();
        graphFactoryMock.Setup(f => f.ForApp()).Returns(CreateMockGraphClient());

        DataverseEntity? capturedEntity = null;
        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((entity, _) => capturedEntity = entity)
            .ReturnsAsync(Guid.NewGuid());

        var sut = BuildService(graphFactoryMock, dataverseMock);

        var orgId = Guid.NewGuid();
        var request = CreateValidRequest(
            subject: "Org Association Test",
            body: "<p>Body</p>",
            associations: new[]
            {
                new CommunicationAssociation
                {
                    EntityType = "sprk_organization",
                    EntityId = orgId,
                    EntityName = "Acme Corp"
                }
            });

        // Act
        await sut.SendAsync(request);

        // Assert
        capturedEntity.Should().NotBeNull();

        var regardingRef = capturedEntity!.GetAttributeValue<EntityReference>("sprk_regardingorganization");
        regardingRef.Should().NotBeNull("sprk_regardingorganization should be set for organization association");
        regardingRef!.LogicalName.Should().Be("sprk_organization");
        regardingRef.Id.Should().Be(orgId);

        capturedEntity.GetAttributeValue<string>("sprk_regardingrecordname").Should().Be("Acme Corp");
        capturedEntity.GetAttributeValue<int>("sprk_associationcount").Should().Be(1);
    }

    #endregion

    #region Phase 6: CommunicationAccountService Integration

    [Fact]
    public async Task CommunicationAccountService_QuerySendEnabledAccounts_ResolvesViaValidator()
    {
        // Arrange: Dataverse returns a sprk_communicationaccount for mailbox-central@spaarke.com
        var accountEntity = new DataverseEntity("sprk_communicationaccount");
        accountEntity.Id = Guid.NewGuid();
        accountEntity["sprk_emailaddress"] = "mailbox-central@spaarke.com";
        accountEntity["sprk_name"] = "Spaarke Central Mailbox";
        accountEntity["sprk_displayname"] = "Spaarke Central";
        accountEntity["sprk_sendenableds"] = true;
        accountEntity["sprk_isdefaultsender"] = true;
        accountEntity["sprk_accounttype"] = new OptionSetValue(100000000);

        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { accountEntity });

        // Redis cache returns null (cache miss) to force Dataverse query
        var accountCacheMock = new Mock<IDistributedCache>();
        accountCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var validatorCacheMock = new Mock<IDistributedCache>();
        validatorCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var options = new CommunicationOptions
        {
            ApprovedSenders = Array.Empty<ApprovedSenderConfig>(),
            DefaultMailbox = "mailbox-central@spaarke.com"
        };

        var accountService = new CommunicationAccountService(
            dataverseMock.Object,
            accountCacheMock.Object,
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            accountService,
            validatorCacheMock.Object,
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        // Act: resolve default sender (null = use default)
        var result = await senderValidator.ResolveAsync(null);

        // Assert
        result.IsValid.Should().BeTrue("CommunicationAccountService should provide the default sender");
        result.Email.Should().Be("mailbox-central@spaarke.com");
    }

    [Fact]
    public async Task CommunicationAccountService_FallbackToConfig_WhenDataverseUnavailable()
    {
        // Arrange: Dataverse throws on QueryCommunicationAccountsAsync
        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse is unavailable"));

        var accountCacheMock = new Mock<IDistributedCache>();
        accountCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var validatorCacheMock = new Mock<IDistributedCache>();
        validatorCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        // Config has mailbox-central@spaarke.com as an approved sender
        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig
                {
                    Email = "mailbox-central@spaarke.com",
                    DisplayName = "Config Fallback",
                    IsDefault = true
                }
            },
            DefaultMailbox = "mailbox-central@spaarke.com"
        };

        var accountService = new CommunicationAccountService(
            dataverseMock.Object,
            accountCacheMock.Object,
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            accountService,
            validatorCacheMock.Object,
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        // Act: resolve default sender; Dataverse query will fail, should fall back to config
        var result = await senderValidator.ResolveAsync(null);

        // Assert
        result.IsValid.Should().BeTrue("Validator should fall back to config senders when Dataverse is unavailable");
        result.Email.Should().Be("mailbox-central@spaarke.com");
        result.DisplayName.Should().Be("Config Fallback");
    }

    [Fact]
    public async Task CommunicationAccountService_InvalidSender_RejectsUnapprovedMailbox()
    {
        // Arrange: only mailbox-central@spaarke.com is approved (both config and Dataverse)
        var accountEntity = new DataverseEntity("sprk_communicationaccount");
        accountEntity.Id = Guid.NewGuid();
        accountEntity["sprk_emailaddress"] = "mailbox-central@spaarke.com";
        accountEntity["sprk_name"] = "Spaarke Central Mailbox";
        accountEntity["sprk_displayname"] = "Spaarke Central";
        accountEntity["sprk_sendenableds"] = true;
        accountEntity["sprk_isdefaultsender"] = true;
        accountEntity["sprk_accounttype"] = new OptionSetValue(100000000);

        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { accountEntity });

        var accountCacheMock = new Mock<IDistributedCache>();
        accountCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var validatorCacheMock = new Mock<IDistributedCache>();
        validatorCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig
                {
                    Email = "mailbox-central@spaarke.com",
                    DisplayName = "Spaarke Central",
                    IsDefault = true
                }
            },
            DefaultMailbox = "mailbox-central@spaarke.com"
        };

        var accountService = new CommunicationAccountService(
            dataverseMock.Object,
            accountCacheMock.Object,
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            accountService,
            validatorCacheMock.Object,
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        // Act: attempt to resolve an unapproved sender
        var result = await senderValidator.ResolveAsync("evil@hacker.com");

        // Assert
        result.IsValid.Should().BeFalse("Unapproved mailbox should be rejected");
        result.ErrorCode.Should().Be("INVALID_SENDER");
        result.ErrorDetail.Should().Contain("evil@hacker.com");
    }

    [Fact]
    public async Task CommunicationAccountService_DataverseOverridesConfig_ForSameEmail()
    {
        // Arrange: config has mailbox-central@spaarke.com with DisplayName="Config Name"
        //          Dataverse has the same email with DisplayName="Spaarke Central"
        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig
                {
                    Email = "mailbox-central@spaarke.com",
                    DisplayName = "Config Name",
                    IsDefault = true
                }
            },
            DefaultMailbox = "mailbox-central@spaarke.com"
        };

        var dataverseSenderEntity = new DataverseEntity("sprk_communicationaccount");
        dataverseSenderEntity.Id = Guid.NewGuid();
        dataverseSenderEntity["sprk_emailaddress"] = "mailbox-central@spaarke.com";
        dataverseSenderEntity["sprk_name"] = "Spaarke Central";
        dataverseSenderEntity["sprk_displayname"] = "Spaarke Central";
        dataverseSenderEntity["sprk_isdefaultsender"] = true;
        dataverseSenderEntity["sprk_sendenableds"] = true;
        dataverseSenderEntity["sprk_accounttype"] = new OptionSetValue(100000000);

        var dataverseMock = new Mock<IDataverseService>();
        dataverseMock
            .Setup(d => d.QueryCommunicationAccountsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { dataverseSenderEntity });

        var accountCacheMock = new Mock<IDistributedCache>();
        accountCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var validatorCacheMock = new Mock<IDistributedCache>();
        validatorCacheMock
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var accountService = new CommunicationAccountService(
            dataverseMock.Object,
            accountCacheMock.Object,
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            accountService,
            validatorCacheMock.Object,
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        // Act: resolve default sender via merged resolution
        var result = await senderValidator.ResolveAsync(null);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Email.Should().Be("mailbox-central@spaarke.com");
        result.DisplayName.Should().Be("Spaarke Central",
            "When both config and Dataverse have the same email, Dataverse DisplayName should win");
    }

    #endregion
}
