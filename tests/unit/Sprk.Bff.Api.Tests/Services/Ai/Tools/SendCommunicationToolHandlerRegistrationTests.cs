using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Communication;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Tools;

/// <summary>
/// Verifies SendCommunicationToolHandler registration metadata and interface compliance.
/// Since SendCommunicationToolHandler implements IAiToolHandler (not IAnalysisToolHandler),
/// it is NOT auto-discovered by AddToolHandlersFromAssembly — it must be registered
/// explicitly via CommunicationModule (or similar DI registration).
/// These tests confirm correct tool identity and interface implementation.
/// </summary>
public class SendCommunicationToolHandlerRegistrationTests
{
    /// <summary>
    /// Creates a real SendCommunicationToolHandler with a real CommunicationService
    /// backed by mocked infrastructure dependencies.
    /// CommunicationService is sealed, so it cannot be mocked with Moq.
    /// ApprovedSenderValidator is also sealed and requires IDataverseService, IDistributedCache, and ILogger.
    /// </summary>
    private static SendCommunicationToolHandler CreateHandler()
    {
        var options = new CommunicationOptions
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

        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var communicationService = new CommunicationService(
            Mock.Of<IGraphClientFactory>(),
            senderValidator,
            Mock.Of<IDataverseService>(),
            null!, // EmlGenerationService — not tested here
            null!, // SpeFileStore — not tested here
            Options.Create(options),
            Mock.Of<ILogger<CommunicationService>>());

        return new SendCommunicationToolHandler(
            communicationService,
            Mock.Of<ILogger<SendCommunicationToolHandler>>());
    }

    [Fact]
    public void ToolName_ShouldBe_SendCommunication()
    {
        // Arrange
        var handler = CreateHandler();

        // Act & Assert
        handler.ToolName.Should().Be("send_communication");
    }

    [Fact]
    public void ToolNameConst_ShouldBe_SendCommunication()
    {
        // The static constant should match the instance property value
        SendCommunicationToolHandler.ToolNameConst.Should().Be("send_communication");
    }

    [Fact]
    public void ToolName_ShouldMatch_ToolNameConst()
    {
        // Verify the instance property is backed by the constant
        var handler = CreateHandler();
        handler.ToolName.Should().Be(SendCommunicationToolHandler.ToolNameConst);
    }

    [Fact]
    public void Handler_ShouldImplement_IAiToolHandler()
    {
        // SendCommunicationToolHandler must implement IAiToolHandler for playbook integration
        var handler = CreateHandler();
        handler.Should().BeAssignableTo<IAiToolHandler>();
    }

    [Fact]
    public void Handler_ShouldNotImplement_IAnalysisToolHandler()
    {
        // SendCommunicationToolHandler is an IAiToolHandler (playbook tool),
        // NOT an IAnalysisToolHandler (document analysis tool).
        // This distinction matters because AddToolHandlersFromAssembly only scans for IAnalysisToolHandler.
        var handler = CreateHandler();
        handler.Should().NotBeAssignableTo<IAnalysisToolHandler>();
    }

    [Fact]
    public void Constructor_ShouldSucceed_WithValidDependencies()
    {
        // Verify the handler can be constructed without exceptions
        var act = () => CreateHandler();
        act.Should().NotThrow();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenCommunicationServiceIsNull()
    {
        // Arrange & Act
        var act = () => new SendCommunicationToolHandler(
            null!,
            Mock.Of<ILogger<SendCommunicationToolHandler>>());

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("communicationService");
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        // Arrange
        var options = new CommunicationOptions
        {
            ApprovedSenders = new[]
            {
                new ApprovedSenderConfig
                {
                    Email = "noreply@contoso.com",
                    DisplayName = "Contoso Notifications",
                    IsDefault = true
                }
            }
        };

        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());
        var communicationService = new CommunicationService(
            Mock.Of<IGraphClientFactory>(),
            senderValidator,
            Mock.Of<IDataverseService>(),
            null!, // EmlGenerationService — not tested here
            null!, // SpeFileStore — not tested here
            Options.Create(options),
            Mock.Of<ILogger<CommunicationService>>());

        // Act
        var act = () => new SendCommunicationToolHandler(communicationService, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("logger");
    }
}
