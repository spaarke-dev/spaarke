using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.EventRules;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// spaarkeai-assistant-enhancements-r4 task 012 (FR-01/FR-02) vertical-slice seam — the
/// definition-of-done for the ADVISORY grounded-recommend routing decision (ADR-038's
/// <c>tests/integration/seam/**</c> DoD for dispatch-spine changes). Drives the PRODUCTION
/// <see cref="SessionDispatchOrchestrator"/> to prove that the executor leg is selected DETERMINISTICALLY
/// from the resolved <see cref="AnalysisAction.GroundedToolAllowList"/> (task 010, the materialized
/// advisory signal): a non-empty allow-list routes the <see cref="IAdvisoryCapabilityRunner"/> (nested
/// bounded agent turn), an empty allow-list routes the linear <see cref="IActionRunner"/> unchanged, and a
/// null runner (hand-built orchestrator, e.g. a leaner deployment) falls back to the ActionRunner
/// byte-identically. The Action is selected by binding id BEFORE this leg — the ONE probabilistic dispatch
/// decider stays the top-level Text-path turn (ADR-039); this seam only proves the deterministic executor
/// selection + the ADR-040 store-before-render tail.
/// <para>
/// <b>Real vs mocked collaborators</b>: the session store (<see cref="ChatSessionManager"/>), the output
/// router (<see cref="OutputRouter"/>), and the confirmation-gate manager (<see cref="PendingPlanManager"/>)
/// are real production instances. Routing (<see cref="IConsumerRoutingService"/>) and Scope
/// (<see cref="IScopeResolverService"/>) are mocked to supply the resolved Binding + Action;
/// <see cref="IContextBinder"/> is short-circuited to the structured-operand path; the two executors
/// (<see cref="IActionRunner"/>, <see cref="IAdvisoryCapabilityRunner"/>) are mocks that record which leg
/// the orchestrator invoked. The nested-turn tool NARROWING itself (ONLY the allow-list mounts; every
/// capability/refusal tool dropped) is proven by the task-011 <c>AgentToolProjection.PreFilter</c> unit
/// tests + <see cref="AdvisoryCapabilityRunnerBehaviorTests"/> (which asserts the runner threads the
/// allow-list + system prompt into the factory); this file proves the routing decision + render tail.
/// </para>
/// </summary>
public sealed class AdvisoryDispatchSeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000ad";
    private const string SessionId = "77777777-7777-7777-7777-777777777777";
    private static readonly Guid BindingId = Guid.Parse("5b1870b9-0000-0000-0000-000000000012");
    private static readonly Guid ActionId = Guid.Parse("57651aad-0000-0000-0000-000000000012");

    private static readonly string[] AdvisoryAllowList =
        { "spaarke.grid_overview", "spaarke.daily_briefing_overview" };

    [Fact]
    public async Task Dispatch_WhenActionHasGroundedToolAllowList_RoutesToAdvisoryRunner()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenAdvisoryAction(AdvisoryAllowList);

        await h.DispatchAsync();

        h.AdvisoryRunner.Verify(
            r => r.RunAsync(
                It.Is<AnalysisAction>(a => a.GroundedToolAllowList.Count == 2),
                It.IsAny<ChatSession>(),
                It.IsAny<SessionDispatchRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "a non-empty groundedToolAllowList is the deterministic advisory routing signal — the " +
            "nested grounded-recommend runner executes this Action, not the linear ActionRunner");
        h.ActionRunner.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the advisory tier and the fact tier are mutually exclusive — the linear ActionRunner never runs an advisory Action");
    }

    [Fact]
    public async Task Dispatch_WhenActionHasEmptyAllowList_RoutesToLinearActionRunner()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenAdvisoryAction(Array.Empty<string>());

        await h.DispatchAsync();

        h.ActionRunner.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "an empty allow-list is a fact-tier Action — the linear ActionRunner runs it, unchanged (pre-012 behavior)");
        h.AdvisoryRunner.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<ChatSession>(), It.IsAny<SessionDispatchRequest>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "no advisory opt-in (empty allow-list) → the nested advisory runner is never invoked");
    }

    [Fact]
    public async Task Dispatch_WhenAdvisoryRunnerIsNull_FallsBackToLinearActionRunner()
    {
        // A leaner orchestrator construction with NO advisory runner (the ADR-032 Null-friendly guard):
        // even an advisory Action degrades to the linear ActionRunner rather than failing the dispatch.
        var h = new Harness(withAdvisoryRunner: false);
        await h.SeedSessionAsync();
        h.GivenAdvisoryAction(AdvisoryAllowList);

        await h.DispatchAsync();

        h.ActionRunner.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "a null advisory runner (hand-built orchestrator) must not fail the dispatch — it falls back to the linear ActionRunner");
    }

    [Fact]
    public async Task Dispatch_AdvisoryOutput_IsStoredAndRenderedFromLedger()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenAdvisoryAction(AdvisoryAllowList);
        h.AdvisoryReturns("You have 3 open tasks; I'd clear the 2 overdue first [1][2].");

        var terminal = await h.DispatchAndReadTerminalAsync();

        // ADR-040 store-before-render: the terminal chunk is built FROM the stored ledger entry, and a
        // non-DAR capability payload passes THROUGH verbatim into the chunk's Result (the same JsonElement
        // BindingCapabilityTool relays to the text path). So the advisory runner's assembled
        // acknowledgement round-trips through the store into the rendered payload.
        terminal.Should().NotBeNull();
        terminal!.Result.Should().BeOfType<JsonElement>();
        ((JsonElement)terminal.Result!).GetProperty("acknowledgement").GetString().Should().Contain("overdue",
            "the advisory narration the runner assembled was stored, then the terminal chunk rendered it from the ledger");
    }

    private sealed class Harness
    {
        public ChatSessionManager Sessions { get; }
        public Mock<IConsumerRoutingService> Routing { get; } = new();
        public Mock<IScopeResolverService> Scope { get; } = new();
        public Mock<IContextBinder> ContextBinder { get; } = new();
        public Mock<IActionRunner> ActionRunner { get; } = new();
        public Mock<IAdvisoryCapabilityRunner> AdvisoryRunner { get; } = new();
        public SessionDispatchOrchestrator Orchestrator { get; }

        public Harness(bool withAdvisoryRunner = true)
        {
            Sessions = new ChatSessionManager(
                new InMemoryTenantCache(),
                Mock.Of<IChatDataverseRepository>(),
                Mock.Of<ILogger<ChatSessionManager>>());

            ContextBinder
                .Setup(c => c.HasStructuredOperand(It.IsAny<string?>(), It.IsAny<JsonElement?>()))
                .Returns(true);
            ContextBinder
                .Setup(c => c.BindAsync(It.IsAny<ContextBindingRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BoundInputs
                {
                    Context = ContextEnvelopeReferenceProducer.Assemble(),
                    Operand = ResolvedOperand.None,
                });

            ActionRunner
                .Setup(r => r.RunAsync(
                    It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(JsonSerializer.SerializeToElement(new { acknowledgement = "linear ack" }));

            AdvisoryReturns("advisory summary");

            var router = new OutputRouter(
                Sessions, Mock.Of<ILogger<OutputRouter>>(), Mock.Of<IEmailDispositionSender>());
            var pending = new PendingPlanManager(
                new InMemoryTenantCache(), Sessions, Mock.Of<ILogger<PendingPlanManager>>());
            var registry = new Mock<ICodedWorkflowRegistry>();

            Orchestrator = new SessionDispatchOrchestrator(
                Sessions, Routing.Object, Scope.Object,
                ActionRunner.Object, ContextBinder.Object,
                registry.Object, Mock.Of<ISessionFileTextSource>(), router, pending,
                Options.Create(new EventRulesOptions { ReadinessProbeAttempts = 1, ReadinessProbeDelayMs = 0 }),
                new Sprk.Bff.Api.Telemetry.AiTelemetry(),
                Mock.Of<ILogger<SessionDispatchOrchestrator>>(),
                advisoryRunner: withAdvisoryRunner ? AdvisoryRunner.Object : null);
        }

        public void AdvisoryReturns(string acknowledgement) =>
            AdvisoryRunner
                .Setup(r => r.RunAsync(
                    It.IsAny<AnalysisAction>(), It.IsAny<ChatSession>(), It.IsAny<SessionDispatchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(JsonSerializer.SerializeToElement(new { acknowledgement }));

        public Task SeedSessionAsync() => Sessions.UpdateSessionCacheAsync(new ChatSession(
            SessionId: SessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: Array.Empty<ChatSessionFile>()) { OwnerOid = TestSessionOwner.Oid });

        public void GivenAdvisoryAction(IReadOnlyList<string> groundedToolAllowList)
        {
            Routing
                .Setup(c => c.GetBindingByIdAsync(BindingId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Binding
                {
                    BindingId = BindingId,
                    ConsumerType = "list-tasks",
                    ActionId = ActionId,
                    ActionKind = ActionKind.Prompted,
                    Disposition = BindingDisposition.Informational,
                    Risk = BindingRisk.None,
                });
            Scope
                .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "List Tasks",
                    SystemPrompt = "advisory task-agenda advisor prompt",
                    OutputSchemaJson = "{}",
                    ModelTier = AiModelTier.Reasoning,
                    GroundedToolAllowList = groundedToolAllowList,
                });
        }

        public async Task DispatchAsync()
        {
            await foreach (var _ in Orchestrator.DispatchAsync(NewRequest()))
            {
            }
        }

        public async Task<AnalysisChunk?> DispatchAndReadTerminalAsync()
        {
            AnalysisChunk? terminal = null;
            await foreach (var chunk in Orchestrator.DispatchAsync(NewRequest()))
            {
                if (chunk.Done)
                {
                    terminal = chunk;
                }
            }
            return terminal;
        }

        private static SessionDispatchRequest NewRequest() => new(
            TenantId, SessionId, BindingId,
            JsonSerializer.SerializeToElement(new { documentText = "what do I need to do today" }));
    }
}
