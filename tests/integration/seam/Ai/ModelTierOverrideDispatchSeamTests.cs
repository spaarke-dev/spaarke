using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
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
/// ai-advanced-capabilities-nda-r1 task 011 vertical-slice seam — the definition-of-done for the
/// runtime model-tier picker's server-side composition (ADR-039; ADR-038's <c>tests/integration/seam/**</c>
/// DoD for dispatch-spine changes). Drives the PRODUCTION <see cref="SessionDispatchOrchestrator"/> to
/// prove that <see cref="SessionDispatchRequest.ModelTierOverride"/> (the Assistant's runtime tier-picker
/// selection) composes with the resolved Binding's own <see cref="Binding.ModelTierOverride"/> and the
/// Action's default tier in the documented precedence — request override wins, then Binding override,
/// then Action default — and that leaving both unset is byte-identical to the pre-task-011 behavior (the
/// Action's own tier governs, task 010).
/// <para>
/// <b>Real vs mocked collaborators</b>: the session store (<see cref="ChatSessionManager"/>, backed by
/// <see cref="InMemoryTenantCache"/>), the output router (<see cref="OutputRouter"/>), and the
/// confirmation-gate manager (<see cref="PendingPlanManager"/>) are all real, production instances.
/// <see cref="IConsumerRoutingService"/> (Routing — supplies the resolved <see cref="Binding"/>) and
/// <see cref="IScopeResolverService"/> (Scope — supplies the resolved <see cref="AnalysisAction"/>) are
/// <c>Mock&lt;T&gt;</c>, along with <see cref="IContextBinder"/> (input binding, short-circuited to the
/// structured-operand path) and <see cref="IActionRunner"/> (captures the composed <c>AnalysisAction</c>
/// the orchestrator actually dispatches).
/// </para>
/// <para>
/// <b>Coverage caveat</b>: because Routing is mocked, <see cref="Harness.GivenBinding"/> always seeds
/// <see cref="Binding.ActionModelTier"/> and the mocked <see cref="AnalysisAction.ModelTier"/> with the
/// SAME value — this seam cannot distinguish the orchestrator reading the Binding's (potentially stale,
/// TTL-cached via <c>ConsumerRoutingService</c>) <c>ActionModelTier</c> snapshot from reading the
/// freshly-fetched Action's own <c>ModelTier</c>. The cache-staleness fix (composing off
/// <c>action.ModelTier</c>, not <c>binding.EffectiveModelTier</c>/<c>binding.ActionModelTier</c>) is
/// exercised only by construction/inspection, not by an assertion in this file.
/// </para>
/// </summary>
public sealed class ModelTierOverrideDispatchSeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000dd";
    private const string SessionId = "66666666-6666-6666-6666-666666666666";
    private static readonly Guid BindingId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ActionId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Dispatch_WithRequestModelTierOverride_WinsOverBindingAndActionTiers()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenBinding(bindingModelTierOverride: null, actionModelTier: AiModelTier.Standard);

        await h.DispatchAsync(requestModelTierOverride: AiModelTier.Reasoning);

        h.CapturedAction.Should().NotBeNull();
        h.CapturedAction!.ModelTier.Should().Be(AiModelTier.Reasoning,
            "the picker's per-request override is the most-specific signal — it wins over both the " +
            "Binding's own override (unset here) and the Action's default tier (Standard)");
    }

    [Fact]
    public async Task Dispatch_WithBindingModelTierOverrideAndNoRequestOverride_UsesBindingOverride()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenBinding(bindingModelTierOverride: AiModelTier.Fast, actionModelTier: AiModelTier.Standard);

        await h.DispatchAsync(requestModelTierOverride: null);

        h.CapturedAction.Should().NotBeNull();
        h.CapturedAction!.ModelTier.Should().Be(AiModelTier.Fast,
            "with no per-request override, the Binding's own sprk_modeltieroverride (Binding.ModelTierOverride) " +
            "still governs over the Action's default tier — task 010 behavior is unaffected by task 011's addition");
    }

    [Fact]
    public async Task Dispatch_WithNoOverridesSet_PreservesActionModelTier()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenBinding(bindingModelTierOverride: null, actionModelTier: AiModelTier.Reasoning);

        await h.DispatchAsync(requestModelTierOverride: null);

        h.CapturedAction.Should().NotBeNull();
        h.CapturedAction!.ModelTier.Should().Be(AiModelTier.Reasoning,
            "unset request override + unset Binding override is byte-identical to pre-task-011 " +
            "behavior: the Action's own tier governs, unchanged");
    }

    private sealed class Harness
    {
        public ChatSessionManager Sessions { get; }
        public Mock<IConsumerRoutingService> Routing { get; } = new();
        public Mock<IScopeResolverService> Scope { get; } = new();
        public Mock<IContextBinder> ContextBinder { get; } = new();
        public Mock<IActionRunner> ActionRunnerMock { get; } = new();
        public AnalysisAction? CapturedAction { get; private set; }
        public SessionDispatchOrchestrator Orchestrator { get; }

        public Harness()
        {
            Sessions = new ChatSessionManager(
                new InMemoryTenantCache(),
                Mock.Of<IChatDataverseRepository>(),
                Mock.Of<ILogger<ChatSessionManager>>());

            // Structured-operand short-circuit (ADR-043 E-10): bypasses the file-operand / session-file
            // requirement entirely — this seam is about tier composition, not operand resolution.
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

            ActionRunnerMock
                .Setup(r => r.RunAsync(
                    It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
                .Callback<AnalysisAction, BoundInputs, LinearRunContext, CancellationToken>(
                    (action, _, _, _) => CapturedAction = action)
                .ReturnsAsync(JsonSerializer.SerializeToElement(new { result = "ok" }));

            var router = new OutputRouter(
                Sessions, Mock.Of<ILogger<OutputRouter>>(), Mock.Of<IEmailDispositionSender>());
            var pending = new PendingPlanManager(
                new InMemoryTenantCache(), Sessions, Mock.Of<ILogger<PendingPlanManager>>());
            var registry = new Mock<ICodedWorkflowRegistry>();

            Orchestrator = new SessionDispatchOrchestrator(
                Sessions, Routing.Object, Scope.Object,
                ActionRunnerMock.Object, ContextBinder.Object,
                registry.Object, Mock.Of<ISessionFileTextSource>(), router, pending,
                Options.Create(new EventRulesOptions { ReadinessProbeAttempts = 1, ReadinessProbeDelayMs = 0 }),
                new Sprk.Bff.Api.Telemetry.AiTelemetry(),
                Mock.Of<ILogger<SessionDispatchOrchestrator>>());
        }

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
            UploadedFiles: Array.Empty<ChatSessionFile>()));

        public void GivenBinding(AiModelTier? bindingModelTierOverride, AiModelTier? actionModelTier)
        {
            Routing
                .Setup(c => c.GetBindingByIdAsync(BindingId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Binding
                {
                    BindingId = BindingId,
                    ConsumerType = "model-tier-seam-test",
                    ActionId = ActionId,
                    ActionKind = ActionKind.Prompted,
                    Disposition = BindingDisposition.Informational,
                    Risk = BindingRisk.None,
                    ModelTierOverride = bindingModelTierOverride,
                    ActionModelTier = actionModelTier,
                });
            Scope
                .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Model Tier Seam Action",
                    SystemPrompt = "n/a",
                    OutputSchemaJson = "{}",
                    ModelTier = actionModelTier,
                });
        }

        public async Task DispatchAsync(AiModelTier? requestModelTierOverride)
        {
            var argsElement = JsonSerializer.SerializeToElement(new { });
            await foreach (var _ in Orchestrator.DispatchAsync(
                new SessionDispatchRequest(
                    TenantId, SessionId, BindingId, argsElement, ModelTierOverride: requestModelTierOverride)))
            {
            }
        }
    }
}
