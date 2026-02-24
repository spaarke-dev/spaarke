using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for SprkChatAgentFactory.
/// Verifies that the factory creates agents with correct context from IChatContextProvider.
/// </summary>
public class SprkChatAgentFactoryTests
{
    private static readonly Guid TestPlaybookId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string TestDocumentId = "doc-001";
    private const string TestTenantId = "tenant-abc";
    private const string TestSessionId = "session-xyz";

    [Fact]
    public async Task CreateAgentAsync_ReturnsSprkChatAgent_WithContextFromProvider()
    {
        // Arrange
        const string expectedSystemPrompt = "You are a contract analyst.";

        var expectedContext = new ChatContext(
            SystemPrompt: expectedSystemPrompt,
            DocumentSummary: "This is an NDA.",
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId);

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedContext);

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert
        agent.Should().NotBeNull();
        agent.Context.SystemPrompt.Should().Be(expectedSystemPrompt);
        agent.Context.DocumentSummary.Should().Be("This is an NDA.");
        agent.Context.PlaybookId.Should().Be(TestPlaybookId);
    }

    [Fact]
    public async Task CreateAgentAsync_CallsContextProvider_WithCorrectParameters()
    {
        // Arrange
        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        await factory.CreateAgentAsync(TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert
        contextProviderMock.Verify(
            p => p.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAgentAsync_ReturnsNewAgentInstance_OnEachCall()
    {
        // Arrange
        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent1 = await factory.CreateAgentAsync(TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);
        var agent2 = await factory.CreateAgentAsync(TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — each call must return a distinct instance (context switching support)
        agent1.Should().NotBeSameAs(agent2);
    }

    [Fact]
    public async Task CreateAgentAsync_HandlesContextSwitching_ByCreatingNewAgentWithDifferentDocument()
    {
        // Arrange
        const string doc1 = "doc-001";
        const string doc2 = "doc-002";
        const string prompt1 = "Analyze doc 1.";
        const string prompt2 = "Analyze doc 2.";

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(doc1, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatContext(prompt1, null, null, TestPlaybookId));
        contextProviderMock
            .Setup(p => p.GetContextAsync(doc2, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatContext(prompt2, null, null, TestPlaybookId));

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — simulate context switch by creating two agents for different documents
        var agentDoc1 = await factory.CreateAgentAsync(TestSessionId, doc1, TestPlaybookId, TestTenantId);
        var agentDoc2 = await factory.CreateAgentAsync(TestSessionId, doc2, TestPlaybookId, TestTenantId);

        // Assert
        agentDoc1.Context.SystemPrompt.Should().Be(prompt1);
        agentDoc2.Context.SystemPrompt.Should().Be(prompt2);
    }

    #region Private helpers

    private static ServiceProvider BuildServiceProvider(IChatContextProvider contextProvider)
    {
        var services = new ServiceCollection();

        // Register IChatClient mock
        var chatClientMock = new Mock<IChatClient>();
        services.AddSingleton(chatClientMock.Object);

        // Register IChatContextProvider (scoped — factory will resolve from scope)
        services.AddScoped(_ => contextProvider);

        // Register loggers
        services.AddLogging();

        // Register factory (singleton — matches ADR-010 constraint)
        services.AddSingleton<SprkChatAgentFactory>();

        return services.BuildServiceProvider();
    }

    private static ChatContext CreateDefaultContext()
        => new ChatContext(
            SystemPrompt: "Default system prompt.",
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId);

    #endregion
}
