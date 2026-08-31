using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// spaarkeai-assistant-enhancements-r4 task 012 (FR-01/FR-02) — behavior of the
/// <see cref="AdvisoryCapabilityRunner"/> executor: it builds the nested bounded advisory turn via the
/// factory's ADVISORY overload (threading the resolved Action's grounded-tool allow-list + advisory
/// system prompt + Reasoning tier — the wiring that makes the task-011 <c>PreFilter</c> primitive LIVE),
/// drains the streamed narration, and assembles it into the stored-payload
/// <c>{ "acknowledgement": &lt;narration&gt; }</c> shape (ADR-040 store-before-render; the shipped
/// <c>list-tasks</c> wire contract the client already renders).
/// <para>
/// The factory is a capturing test double built on the protected logger-only base ctor (the same seam
/// <c>NullSprkChatAgentFactory</c> uses) so the advisory arguments the runner passes are asserted without
/// standing up an <c>IChatClient</c>; the agent is a stub that streams a known set of text chunks. The
/// actual tool NARROWING (only the allow-list mounts) is the task-011 <c>AgentToolProjection.PreFilter</c>
/// contract, exercised by its own unit tests — here we prove the runner FEEDS that primitive the Action's
/// allow-list, which is the load-bearing 012 wiring.
/// </para>
/// </summary>
public sealed class AdvisoryCapabilityRunnerBehaviorTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000ad";
    private const string SessionId = "77777777-7777-7777-7777-777777777777";

    private static readonly string[] AllowList =
        { "spaarke.grid_overview", "spaarke.daily_briefing_overview" };

    [Fact]
    public async Task RunAsync_ThreadsActionAllowListPromptAndTier_IntoTheAdvisoryFactoryOverload()
    {
        var factory = new CapturingFactory(new StubAgent("ignored"));
        var runner = new AdvisoryCapabilityRunner(factory, Mock.Of<ILogger<AdvisoryCapabilityRunner>>());
        var action = Action(AllowList, systemPrompt: "ADVISORY GROUNDING RULES: call both tools…");

        await runner.RunAsync(action, Session(), Request(), CancellationToken.None);

        factory.CapturedAllowList.Should().BeEquivalentTo(AllowList,
            "the runner must feed the task-011 PreFilter exactly the Action's groundedToolAllowList so the " +
            "nested turn mounts ONLY those grounded tools and drops every capability/refusal tool (no second decider)");
        factory.CapturedSystemPrompt.Should().Be(action.SystemPrompt,
            "the advisory Action's prompt (its ADVISORY GROUNDING RULES) is authoritative for the nested turn");
        factory.CapturedModelTier.Should().Be(AiModelTier.Reasoning,
            "the advisory tier runs on the Action's Reasoning model tier (ADR-016)");
    }

    [Fact]
    public async Task RunAsync_AssemblesDrainedNarration_IntoAcknowledgementPayload()
    {
        var factory = new CapturingFactory(new StubAgent("You have 3 open tasks", " — clear the 2 overdue first [1][2]."));
        var runner = new AdvisoryCapabilityRunner(factory, Mock.Of<ILogger<AdvisoryCapabilityRunner>>());

        var output = await runner.RunAsync(Action(AllowList), Session(), Request(), CancellationToken.None);

        output.GetProperty("acknowledgement").GetString().Should()
            .Be("You have 3 open tasks — clear the 2 overdue first [1][2].",
                "the streamed narration chunks are concatenated into the stored acknowledgement payload (ADR-040)");
    }

    [Fact]
    public async Task RunAsync_ForwardsOperandTextAsTheNestedTurnMessage()
    {
        var agent = new StubAgent("ok");
        var factory = new CapturingFactory(agent);
        var runner = new AdvisoryCapabilityRunner(factory, Mock.Of<ILogger<AdvisoryCapabilityRunner>>());
        var request = Request(operandText: "help me prioritize my tasks");

        await runner.RunAsync(Action(AllowList), Session(), request, CancellationToken.None);

        agent.LastMessage.Should().Be("help me prioritize my tasks",
            "when the dispatch args carry the structured documentText operand, the nested turn echoes it as the user message");
    }

    [Fact]
    public async Task RunAsync_WhenNarrationEmpty_AssemblesHonestFallback()
    {
        var factory = new CapturingFactory(new StubAgent("   "));
        var runner = new AdvisoryCapabilityRunner(factory, Mock.Of<ILogger<AdvisoryCapabilityRunner>>());

        var output = await runner.RunAsync(Action(AllowList), Session(), Request(), CancellationToken.None);

        output.GetProperty("acknowledgement").GetString().Should()
            .Contain("couldn't retrieve",
                "an empty nested turn degrades to an honest fallback (ADR-039 — never fabricate), and the Tasks tab still opens");
    }

    [Fact]
    public async Task RunAsync_WhenAllowListEmpty_Throws()
    {
        var factory = new CapturingFactory(new StubAgent("x"));
        var runner = new AdvisoryCapabilityRunner(factory, Mock.Of<ILogger<AdvisoryCapabilityRunner>>());

        var act = () => runner.RunAsync(Action(Array.Empty<string>()), Session(), Request(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "the runner is the advisory tier only — a fact-tier Action (empty allow-list) must never be routed here");
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static AnalysisAction Action(IReadOnlyList<string> allowList, string systemPrompt = "advisor prompt") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "List Tasks",
            SystemPrompt = systemPrompt,
            OutputSchemaJson = "{}",
            ModelTier = AiModelTier.Reasoning,
            GroundedToolAllowList = allowList,
        };

    private static ChatSession Session() => new(
        SessionId: SessionId,
        TenantId: TenantId,
        DocumentId: null,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>(),
        HostContext: null,
        AdditionalDocumentIds: null,
        UploadedFiles: Array.Empty<ChatSessionFile>()) { OwnerOid = TestSessionOwner.Oid };

    private static SessionDispatchRequest Request(string? operandText = null) => new(
        TenantId, SessionId, Guid.NewGuid(),
        operandText is null ? null : JsonSerializer.SerializeToElement(new { documentText = operandText }));

    /// <summary>Captures the advisory args the runner passes; returns a pre-built stub agent.</summary>
    private sealed class CapturingFactory : SprkChatAgentFactory
    {
        private readonly ISprkChatAgent _agent;

        public CapturingFactory(ISprkChatAgent agent)
            : base(Mock.Of<ILogger<SprkChatAgentFactory>>())
        {
            _agent = agent;
        }

        public IReadOnlyCollection<string>? CapturedAllowList { get; private set; }
        public string? CapturedSystemPrompt { get; private set; }
        public AiModelTier? CapturedModelTier { get; private set; }

        public override Task<ISprkChatAgent> CreateAgentAsync(
            string sessionId,
            string documentId,
            Guid? playbookId,
            string tenantId,
            ChatHostContext? hostContext = null,
            IReadOnlyList<string>? additionalDocumentIds = null,
            HttpContext? httpContext = null,
            Func<ChatSseEvent, CancellationToken, Task>? sseWriter = null,
            string? latestUserMessage = null,
            IReadOnlyList<string>? previousTurnToolNames = null,
            IReadOnlyList<ChatSessionFile>? uploadedFiles = null,
            IReadOnlyList<SessionOutput>? ledgerOutputs = null,
            string? activeSessionFileId = null,
            AiModelTier? modelTierOverride = null,
            string? activeContextTabId = null,
            IReadOnlyList<WorkspaceTab>? liveTabs = null,
            WorkspaceActiveItemHandle? activeItem = null,
            IReadOnlyCollection<string>? advisoryToolAllowList = null,
            string? advisorySystemPrompt = null,
            CancellationToken cancellationToken = default)
        {
            CapturedAllowList = advisoryToolAllowList;
            CapturedSystemPrompt = advisorySystemPrompt;
            CapturedModelTier = modelTierOverride;
            return Task.FromResult(_agent);
        }
    }

    /// <summary>Streams the given text chunks; records the last user message it received.</summary>
    private sealed class StubAgent : ISprkChatAgent
    {
        private readonly string[] _chunks;
        public StubAgent(params string[] chunks) => _chunks = chunks;

        public string? LastMessage { get; private set; }
        public ChatContext Context => null!;
        public CitationContext? Citations => null;

        public async IAsyncEnumerable<ChatResponseUpdate> SendMessageAsync(
            string message,
            IReadOnlyList<AiChatMessage> history,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            LastMessage = message;
            foreach (var chunk in _chunks)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
            }
            await Task.CompletedTask;
        }
    }
}
