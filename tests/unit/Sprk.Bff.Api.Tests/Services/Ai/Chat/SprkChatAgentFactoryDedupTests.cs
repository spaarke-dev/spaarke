using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Q20 FR-24 binding-invariant tests — render-routing dedup semantics preserved through
/// the new dispatcher path after WP4 cutover (chat-routing-redesign-r1 task 141).
///
/// Pre-141: the directive was driven by <c>CapabilityRoutingResult.SelectedPlaybookId</c>
/// (the now-deleted CapabilityRouter). Post-141: the same directive is driven by the
/// explicit <c>playbookId</c> parameter passed to <see cref="SprkChatAgentFactory.CreateAgentAsync"/>
/// (resolved upstream by the PlaybookDispatcher in ChatEndpoints).
///
/// These tests assert the wire-level invariant: the SYSTEM PROMPT carries the correct
/// directive given (playbookId, terminal destination) — so the chat-agent LLM emits ONE
/// brief acknowledgment per <c>invoke_playbook</c> call instead of duplicating the
/// playbook's primary render. If these tests fail, the WP4 cutover regressed the R6
/// FR-30 dedup invariant (one user intent → one render).
/// </summary>
/// <remarks>
/// <para>
/// Binding-invariant test trait per spec FR-24. Fails loudly on regression.
/// </para>
/// <para>
/// Per task 118a / 141 pivot: integration test scaffold for chat sessions does not exist;
/// these unit tests assert the invariant at the SprkChatAgentFactory boundary with
/// callback-driven INodeService mocks. The mock returns a single terminal node whose
/// <c>ConfigJson</c> declares the destination — equivalent rigor to an integration test
/// for the directive-application code path.
/// </para>
/// </remarks>
[Trait("category", "binding-invariant")]
[Trait("spec", "FR-24")]
[Trait("project", "chat-routing-redesign-r1")]
public class SprkChatAgentFactoryDedupTests
{
    // ── Test fixtures ─────────────────────────────────────────────────────────

    private static readonly Guid TestPlaybookId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string TestDocumentId = "doc-dedup-001";
    private const string TestTenantId = "tenant-dedup";
    private const string TestSessionId = "session-dedup";

    /// <summary>
    /// Signature substring of <c>BuildDedupDirective</c> output — present iff the
    /// non-chat-destination directive was appended to the system prompt.
    /// </summary>
    private const string NonChatDirectiveMarker =
        "Render Routing Directive (R6 task 042 / FR-30, hardened B-G10)";

    /// <summary>
    /// Signature substring of <c>BuildChatDestinationAckDirective</c> output — present
    /// iff the chat-destination directive (Hotfix B-G9b) was appended.
    /// </summary>
    private const string ChatAckDirectiveMarker =
        "Render Routing Directive (Hotfix Wave B-G9b)";

    private const string BaseSystemPrompt = "You are an analyst.";

    // ── Positive: non-chat destinations append the dedup directive ────────────

    [Theory]
    [InlineData("workspace", "the workspace", "workspace tab")]
    [InlineData("form-prefill", "the form", "form pre-fill")]
    [InlineData("side-effect", "the system", "background action")]
    public async Task CreateAgentAsync_AppendsNonChatDedupDirective_WhenPlaybookTerminalIsNonChat(
        string destinationKebab,
        string expectedTargetPhrase,
        string expectedSurfacePhrase)
    {
        // Arrange — ConfigJson declares the destination on the terminal node.
        var configJson = $"{{\"destination\":\"{destinationKebab}\"}}";
        var services = BuildServiceProvider(
            contextProvider: MockContextProvider(BaseSystemPrompt),
            nodeService: MockNodeServiceWithTerminalConfig(configJson));
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — base prompt preserved AND non-chat directive appended.
        agent.Context.SystemPrompt.Should().StartWith(BaseSystemPrompt,
            because: "the dedup directive must APPEND to the playbook prompt, never replace it");
        agent.Context.SystemPrompt.Should().Contain(NonChatDirectiveMarker,
            because: "non-chat destination must append the FR-30 dedup directive");
        agent.Context.SystemPrompt.Should().Contain(expectedTargetPhrase,
            because: "directive wording must name the target render surface");
        agent.Context.SystemPrompt.Should().Contain(expectedSurfacePhrase,
            because: "directive wording must name the surface label");
        agent.Context.SystemPrompt.Should().NotContain(ChatAckDirectiveMarker,
            because: "the chat-ack directive must NOT fire for non-chat destinations");
    }

    // ── Positive: chat destination appends the Hotfix B-G9b chat-ack directive

    [Fact]
    public async Task CreateAgentAsync_AppendsChatAckDirective_WhenPlaybookTerminalIsChat()
    {
        // Arrange — terminal destination is chat (the default when ConfigJson is empty
        // or destination is omitted — covers the production summarize-document-for-chat case).
        var configJson = "{\"destination\":\"chat\"}";
        var services = BuildServiceProvider(
            contextProvider: MockContextProvider(BaseSystemPrompt),
            nodeService: MockNodeServiceWithTerminalConfig(configJson));
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — chat-ack directive present; non-chat directive absent.
        agent.Context.SystemPrompt.Should().StartWith(BaseSystemPrompt);
        agent.Context.SystemPrompt.Should().Contain(ChatAckDirectiveMarker,
            because: "chat-destination playbooks must append the Hotfix B-G9b chat-ack directive " +
                     "(PDF hallucination fix preserved)");
        agent.Context.SystemPrompt.Should().NotContain(NonChatDirectiveMarker,
            because: "the FR-30 non-chat directive must NOT fire for chat destinations");
    }

    // ── Negative: no playbookId → no directive (free-form conversational turn) ─

    [Fact]
    public async Task CreateAgentAsync_AppendsNoDirective_WhenPlaybookIdIsNull()
    {
        // Arrange — even with a node service registered, a null playbookId means
        // this is a free-form / non-playbook turn (e.g., a follow-up question).
        var services = BuildServiceProvider(
            contextProvider: MockContextProvider(BaseSystemPrompt),
            nodeService: MockNodeServiceWithTerminalConfig("{\"destination\":\"workspace\"}"));
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — playbookId is null (no playbook resolution for this turn).
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, playbookId: null, TestTenantId);

        // Assert — neither directive should be appended.
        agent.Context.SystemPrompt.Should().NotContain(NonChatDirectiveMarker,
            because: "free-form (no playbook) turns must not append the dedup directive — " +
                     "NFR-01 conversational primacy");
        agent.Context.SystemPrompt.Should().NotContain(ChatAckDirectiveMarker,
            because: "free-form (no playbook) turns must not append the chat-ack directive");
    }

    // ── Soft failure: INodeService missing → graceful degradation, no directive

    [Fact]
    public async Task CreateAgentAsync_AppendsNoDirective_WhenNodeServiceIsAbsent()
    {
        // Arrange — INodeService is NOT registered. The dedup helper short-circuits
        // and returns null destination → no directive appended.
        var services = BuildServiceProvider(
            contextProvider: MockContextProvider(BaseSystemPrompt),
            nodeService: null);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — graceful degradation; agent still constructed normally.
        agent.Should().NotBeNull();
        agent.Context.SystemPrompt.Should().NotContain(NonChatDirectiveMarker);
        agent.Context.SystemPrompt.Should().NotContain(ChatAckDirectiveMarker);
    }

    [Fact]
    public async Task CreateAgentAsync_AppendsNoDirective_WhenNodeServiceReturnsEmptyArray()
    {
        // Arrange — INodeService returns no nodes (e.g., playbook record is malformed
        // or referenced playbook has no nodes wired). Dedup helper returns null.
        var nodeService = new Mock<INodeService>();
        nodeService
            .Setup(s => s.GetNodesAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PlaybookNodeDto>());

        var services = BuildServiceProvider(
            contextProvider: MockContextProvider(BaseSystemPrompt),
            nodeService: nodeService.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — degrades to no directive.
        agent.Context.SystemPrompt.Should().NotContain(NonChatDirectiveMarker);
        agent.Context.SystemPrompt.Should().NotContain(ChatAckDirectiveMarker);
    }

    [Fact]
    public async Task CreateAgentAsync_AppendsNoDirective_WhenNodeServiceThrows()
    {
        // Arrange — INodeService throws (Dataverse outage simulation). The factory's
        // try/catch around dedup MUST swallow non-cancellation exceptions and proceed —
        // NFR-01 conversational primacy preserved unconditionally.
        var nodeService = new Mock<INodeService>();
        nodeService
            .Setup(s => s.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated Dataverse outage"));

        var services = BuildServiceProvider(
            contextProvider: MockContextProvider(BaseSystemPrompt),
            nodeService: nodeService.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — must NOT throw; degrades silently.
        var agent = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — agent still constructed; no directive present.
        agent.Should().NotBeNull();
        agent.Context.SystemPrompt.Should().NotContain(NonChatDirectiveMarker);
        agent.Context.SystemPrompt.Should().NotContain(ChatAckDirectiveMarker);
    }

    // ── Negative control: directive is per-turn, not memoized ─────────────────

    [Fact]
    public async Task CreateAgentAsync_DirectiveAppliedFreshly_OnEachInvocation()
    {
        // Arrange — same playbookId, same destination, two successive CreateAgentAsync
        // calls. Each must independently append the directive (no "we already applied
        // this" memoization). Verifies that refinement / follow-up turns continue to
        // receive the directive correctly.
        var services = BuildServiceProvider(
            contextProvider: MockContextProvider(BaseSystemPrompt),
            nodeService: MockNodeServiceWithTerminalConfig("{\"destination\":\"workspace\"}"));
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act — two independent invocations.
        var agent1 = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);
        var agent2 = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, TestPlaybookId, TestTenantId);

        // Assert — both prompts contain the directive (per-turn re-evaluation).
        agent1.Context.SystemPrompt.Should().Contain(NonChatDirectiveMarker);
        agent2.Context.SystemPrompt.Should().Contain(NonChatDirectiveMarker);
    }

    // ── Negative control: switching playbook destination switches directive ───

    [Fact]
    public async Task CreateAgentAsync_SwitchesDirective_WhenPlaybookDestinationChanges()
    {
        // Arrange — first call resolves to a workspace playbook; second call resolves
        // to a chat playbook. Each must get its own correct directive — proves the
        // directive is destination-aware, not playbookId-cached.
        var nodeService = new Mock<INodeService>();
        var firstPlaybookId = Guid.NewGuid();
        var secondPlaybookId = Guid.NewGuid();

        nodeService
            .Setup(s => s.GetNodesAsync(firstPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TerminalNode("{\"destination\":\"workspace\"}") });
        nodeService
            .Setup(s => s.GetNodesAsync(secondPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TerminalNode("{\"destination\":\"chat\"}") });

        var services = BuildServiceProvider(
            contextProvider: MockContextProvider(BaseSystemPrompt),
            nodeService: nodeService.Object);
        var factory = services.GetRequiredService<SprkChatAgentFactory>();

        // Act
        var agentForWorkspace = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, firstPlaybookId, TestTenantId);
        var agentForChat = await factory.CreateAgentAsync(
            TestSessionId, TestDocumentId, secondPlaybookId, TestTenantId);

        // Assert
        agentForWorkspace.Context.SystemPrompt.Should().Contain(NonChatDirectiveMarker,
            because: "first call resolves to Workspace destination");
        agentForWorkspace.Context.SystemPrompt.Should().NotContain(ChatAckDirectiveMarker);

        agentForChat.Context.SystemPrompt.Should().Contain(ChatAckDirectiveMarker,
            because: "second call resolves to Chat destination");
        agentForChat.Context.SystemPrompt.Should().NotContain(NonChatDirectiveMarker);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IChatContextProvider MockContextProvider(string systemPrompt)
    {
        var ctx = new ChatContext(
            SystemPrompt: systemPrompt,
            DocumentSummary: null,
            AnalysisMetadata: null,
            PlaybookId: TestPlaybookId);

        var mock = new Mock<IChatContextProvider>();
        mock.Setup(p => p.GetContextAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<ChatHostContext?>(),
                It.IsAny<IReadOnlyList<string>?>(),
                It.IsAny<IReadOnlyList<ChatSessionFile>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ctx);
        return mock.Object;
    }

    private static INodeService MockNodeServiceWithTerminalConfig(string configJson)
    {
        var mock = new Mock<INodeService>();
        mock.Setup(s => s.GetNodesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { TerminalNode(configJson) });
        return mock.Object;
    }

    private static PlaybookNodeDto TerminalNode(string configJson) =>
        new()
        {
            Id = Guid.NewGuid(),
            PlaybookId = TestPlaybookId,
            ExecutionOrder = 999,  // highest ⇒ terminal per ResolvePlaybookTerminalDestinationAsync
            ConfigJson = configJson,
        };

    private static ServiceProvider BuildServiceProvider(
        IChatContextProvider contextProvider,
        INodeService? nodeService)
    {
        var services = new ServiceCollection();

        // Required: IChatClient (default + keyed "raw") for SprkChatAgentFactory ctor.
        services.AddSingleton(Mock.Of<IChatClient>());
        services.AddKeyedSingleton<IChatClient>("raw", Mock.Of<IChatClient>());

        // Required: IChatContextProvider (scoped — resolved from per-call scope).
        services.AddScoped(_ => contextProvider);

        // Optional: INodeService — when registered, dedup directive logic engages.
        if (nodeService is not null)
        {
            services.AddScoped(_ => nodeService);
        }

        services.AddLogging();
        services.AddSingleton<SprkChatAgentFactory>();

        return services.BuildServiceProvider();
    }
}
