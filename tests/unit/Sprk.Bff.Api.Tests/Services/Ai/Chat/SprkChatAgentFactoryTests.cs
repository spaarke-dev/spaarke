using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Capabilities;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat.Tools;
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
            .Setup(p => p.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
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
                It.IsAny<ChatHostContext?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        await factory.CreateAgentAsync(TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert
        contextProviderMock.Verify(
            p => p.GetContextAsync(TestDocumentId, TestTenantId, TestPlaybookId, It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateAgentAsync_ReturnsNewAgentInstance_OnEachCall()
    {
        // Arrange
        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
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
            .Setup(p => p.GetContextAsync(doc1, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatContext(prompt1, null, null, TestPlaybookId));
        contextProviderMock
            .Setup(p => p.GetContextAsync(doc2, It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
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

    // ── AIPU2-061: Per-turn tool injection tests ──────────────────────────────

    /// <summary>
    /// When the CapabilityRouter returns a confident result for a simple greeting-like
    /// message that matches no capability hints, the factory falls back to the full
    /// playbook capability set (backward compatible).
    ///
    /// Verifies: factory returns an agent and does not throw when the router is
    /// configured but routing produces an uncertain result for low-complexity intent.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithRouter_LowComplexityIntent_UsesFullCapabilitySet()
    {
        // Arrange
        // Router returns "uncertain" for a simple hello message — no capability matched.
        var routerMock = new Mock<ICapabilityRouter>();
        routerMock
            .Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityRoutingResult.Uncertain(0.0, layer: 1, latencyMs: 0));

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object, routerMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — low-complexity greeting-style message
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            latestUserMessage: "Hello");

        // Assert — agent created successfully, router was called
        agent.Should().NotBeNull();
        routerMock.Verify(
            r => r.RouteAsync("Hello", It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// When the CapabilityRouter returns a confident result with a specific capability
    /// selected for a document-analysis-style message, the factory passes the routing
    /// result to ResolveTools. Verifies the agent is returned and the router was called
    /// with the user message text.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithRouter_DocumentAnalysisIntent_CallsRouterWithUserMessage()
    {
        // Arrange
        const string documentAnalysisMessage = "analyze this contract for risk clauses";

        var routerMock = new Mock<ICapabilityRouter>();
        routerMock
            .Setup(r => r.RouteAsync(documentAnalysisMessage, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityRoutingResult.Confident(
                selectedCapabilities: ["analyze"],
                confidence: 0.92,
                layer: 1,
                latencyMs: 3));

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object, routerMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            latestUserMessage: documentAnalysisMessage);

        // Assert — agent created successfully, router was called with the exact user message
        agent.Should().NotBeNull();
        routerMock.Verify(
            r => r.RouteAsync(documentAnalysisMessage, It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// When no user message is provided (latestUserMessage is null), the CapabilityRouter
    /// is NOT called — the factory falls through to the existing full-capability-set path.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithRouter_NullUserMessage_RouterNotCalled()
    {
        // Arrange
        var routerMock = new Mock<ICapabilityRouter>();

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProvider(contextProviderMock.Object, routerMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — no user message = initial session creation
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            latestUserMessage: null);

        // Assert — router never called for null message
        agent.Should().NotBeNull();
        routerMock.Verify(
            r => r.RouteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// When the tool set changes between turns (previous turn had different tools than current),
    /// capability_change SSE events are emitted for added and removed tools.
    ///
    /// Verifies the FR-801 capability_change contract: the factory emits events so the
    /// client can update UI affordances when the active capability profile changes.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_EmitsCapabilityChange_WhenToolSetDiffers()
    {
        // Arrange
        var routerMock = new Mock<ICapabilityRouter>();
        // Router returns a confident result — routing WILL happen but manifest is empty,
        // so tool filtering leaves full set in place. The capability_change event is
        // triggered by comparing current tools against previousTurnToolNames.
        routerMock
            .Setup(r => r.RouteAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CapabilityRoutingResult.Uncertain(0.0, layer: 1, latencyMs: 0));

        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        // Previous turn had a tool called "OldTool" that no longer appears in the current set.
        // The current agent will have no tools (no DI services = no tools resolved).
        // So the factory should emit "unavailable" for "OldTool".
        var capturedEvents = new List<ChatSseEvent>();
        Func<ChatSseEvent, CancellationToken, Task> sseWriter = (evt, _) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        var services = BuildServiceProvider(contextProviderMock.Object, routerMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — previous turn had "OldTool", current turn will have no tools
        await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            sseWriter: sseWriter,
            latestUserMessage: "Hello",
            previousTurnToolNames: ["OldTool"]);

        // Assert — at least one capability_change event was emitted for "OldTool"
        capturedEvents.Should().Contain(e =>
            e.Type == "capability_change",
            "factory must emit capability_change when tool set changes between turns");

        var changeEvent = capturedEvents.First(e => e.Type == "capability_change");
        changeEvent.Data.Should().NotBeNull();
    }

    /// <summary>
    /// When the router is not registered in DI (null ICapabilityRouter), the factory
    /// behaves exactly as before AIPU2-061 — no routing call, full tool set resolved.
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithoutRouter_FallsBackToFullCapabilitySet()
    {
        // Arrange — no router registered (null capabilityRouter)
        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        // Use the original service provider (no router registration)
        var services = BuildServiceProvider(contextProviderMock.Object, capabilityRouter: null);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — any user message; router is absent so no routing occurs
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId,
            latestUserMessage: "summarize this document");

        // Assert — agent created normally; no exception thrown
        agent.Should().NotBeNull();
    }

    // ── R5 task 015 — InvokeSummarizePlaybookTool routing-selection coverage ──

    /// <summary>
    /// When the playbook capability set contains <see cref="PlaybookCapabilities.Summarize"/>
    /// AND <see cref="Sprk.Bff.Api.Services.Ai.Chat.SessionSummarizeOrchestrator"/> is registered
    /// in DI, the factory MUST register <c>invoke_summarize_playbook</c> as an AIFunction on
    /// the resolved agent's tool list. Verified via the <c>capability_change</c> SSE event:
    /// when the previous turn did NOT have this tool, the factory emits a "capability_change"
    /// event listing it as a newly-available tool.
    ///
    /// This satisfies R5 task 015 acceptance criterion: "tool is visible in the agent's tool
    /// catalog when the gating capability is present".
    /// </summary>
    [Fact]
    public async Task CreateAgentAsync_WithSummarizeCapability_RegistersInvokeSummarizePlaybookTool()
    {
        // Arrange — null playbookId means "use CoreCapabilities" which includes Summarize.
        // SessionSummarizeOrchestrator is registered in DI alongside its real (mocked) deps.
        var contextProviderMock = new Mock<IChatContextProvider>();
        contextProviderMock
            .Setup(p => p.GetContextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(), It.IsAny<IReadOnlyList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDefaultContext());

        var services = BuildServiceProviderWithSummarizeOrchestrator(contextProviderMock.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Capture capability_change events so we can inspect the tool catalog.
        var capturedEvents = new List<ChatSseEvent>();
        Func<ChatSseEvent, CancellationToken, Task> sseWriter = (evt, _) =>
        {
            capturedEvents.Add(evt);
            return Task.CompletedTask;
        };

        // Act — playbookId = null → CoreCapabilities (Search/Analyze/SelectionRevise/Summarize).
        // previousTurnToolNames empty → every current tool is reported as "available".
        await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId,
            playbookId: null,
            tenantId: TestTenantId,
            sseWriter: sseWriter,
            latestUserMessage: "summarize the attached files",
            previousTurnToolNames: Array.Empty<string>());

        // Assert — exactly one capability_change event lists `invoke_summarize_playbook`
        // as a newly-available tool. We assert on the event payload to confirm the LLM
        // tool name from R5 task 015 appears in the resolved tool catalog.
        capturedEvents.Should()
            .Contain(e => e.Type == "capability_change",
                because: "factory must emit capability_change when the tool set differs from the previous turn");

        // Convert each capability_change event's serialized payload to a string and
        // search for the tool name. The payload shape is implementation-defined but the
        // tool name appears verbatim in either the JSON or via the Data property fields.
        var changeEvents = capturedEvents.Where(e => e.Type == "capability_change").ToList();
        var allPayloads = string.Join("\n", changeEvents.Select(e =>
            System.Text.Json.JsonSerializer.Serialize(e.Data)));
        allPayloads.Should().Contain(InvokeSummarizePlaybookTool.ToolName,
            because: "the factory MUST emit a capability_change event listing invoke_summarize_playbook as available when Summarize capability is set");
    }

    /// <summary>
    /// Build a service provider that includes the dependencies needed to register
    /// <see cref="Sprk.Bff.Api.Services.Ai.Chat.SessionSummarizeOrchestrator"/> in DI so the
    /// tool's gating block in <c>SprkChatAgentFactory.ResolveTools</c> can wire successfully.
    /// </summary>
    private static ServiceProvider BuildServiceProviderWithSummarizeOrchestrator(
        IChatContextProvider contextProvider)
    {
        var services = new ServiceCollection();

        var chatClientMock = new Mock<IChatClient>();
        services.AddSingleton(chatClientMock.Object);

        var rawChatClientMock = new Mock<IChatClient>();
        services.AddKeyedSingleton<IChatClient>("raw", rawChatClientMock.Object);

        services.AddScoped(_ => contextProvider);
        services.AddLogging();

        // Register dependencies of SessionSummarizeOrchestrator. ChatSessionManager is a
        // concrete class (not an interface) so we cannot Moq.Of() it — construct with
        // mocked I/O deps directly. The tool's registration block ONLY checks "is the
        // orchestrator resolvable?", it does NOT invoke the orchestrator's methods.
        var chatSessionManager = new ChatSessionManager(
            cache: Mock.Of<IDistributedCache>(),
            dataverseRepository: Mock.Of<IChatDataverseRepository>(),
            logger: Mock.Of<ILogger<ChatSessionManager>>(),
            persistence: null,
            cleanupSignal: null);

        services.AddSingleton(chatSessionManager);
        services.AddSingleton(Mock.Of<IRagService>());
        services.AddSingleton(Mock.Of<IOpenAiClient>());
        services.AddSingleton(Mock.Of<Spaarke.Dataverse.IGenericEntityService>());
        services.AddSingleton<Sprk.Bff.Api.Telemetry.R5SummarizeTelemetry>();

        // The actual orchestrator class — concrete, scoped, per task 012 + AnalysisServicesModule.
        services.AddScoped<Sprk.Bff.Api.Services.Ai.Chat.SessionSummarizeOrchestrator>();

        services.AddSingleton<SprkChatAgentFactory>();

        return services.BuildServiceProvider();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static CapabilityManifestEntry MakeManifestEntry(
        string name,
        string[] toolNames,
        string[] keywordHints) =>
        new CapabilityManifestEntry(
            CapabilityName: name,
            Description: $"Description for {name}",
            KeywordHints: keywordHints,
            PlaybookId: null,
            ToolNames: toolNames,
            IsEnabled: true,
            TenantRestrictions: Array.Empty<string>());

    #region Private helpers

    private static ServiceProvider BuildServiceProvider(
        IChatContextProvider contextProvider,
        ICapabilityRouter? capabilityRouter = null)
    {
        var services = new ServiceCollection();

        // Register IChatClient mock (standard chat client)
        var chatClientMock = new Mock<IChatClient>();
        services.AddSingleton(chatClientMock.Object);

        // Register raw IChatClient as keyed service "raw" (task 071 — compound intent detection)
        var rawChatClientMock = new Mock<IChatClient>();
        services.AddKeyedSingleton<IChatClient>("raw", rawChatClientMock.Object);

        // Register IChatContextProvider (scoped — factory will resolve from scope)
        services.AddScoped(_ => contextProvider);

        // Register loggers
        services.AddLogging();

        // Register factory (singleton — matches ADR-010 constraint)
        // AIPU2-061: inject the router (may be null for backward-compat tests).
        if (capabilityRouter is not null)
        {
            services.AddSingleton<SprkChatAgentFactory>(sp =>
                new SprkChatAgentFactory(
                    sp.GetRequiredService<IChatClient>(),
                    sp.GetRequiredKeyedService<IChatClient>("raw"),
                    sp,
                    sp.GetRequiredService<ILogger<SprkChatAgentFactory>>(),
                    capabilityRouter));
        }
        else
        {
            services.AddSingleton<SprkChatAgentFactory>();
        }

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
