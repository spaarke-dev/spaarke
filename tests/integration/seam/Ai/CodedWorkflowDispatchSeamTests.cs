using System.Text.Json;
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
using Sprk.Bff.Api.Services.Ai.Workflows;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Ai;

/// <summary>
/// E-30 vertical-slice seam tests — the definition-of-done for the coded-workflow DISPATCH SEAM
/// (ADR-043 §4). Proves the chat dispatch loop can launch a <see cref="ActionKind.Coded"/> Action
/// end-to-end: a Binding targeting a registered <see cref="ICodedWorkflow"/> dispatches through the
/// PRODUCTION <see cref="SessionDispatchOrchestrator"/> → the workflow runs → its output is
/// ledger-written BEFORE render (ADR-040) → the Email disposition delivers the notification. The
/// reserved front door the future Action Engine plugs into.
/// </summary>
/// <remarks>
/// Real path, not mocked: <see cref="SessionDispatchOrchestrator"/>, <see cref="OutputRouter"/>,
/// <see cref="ChatSessionManager"/> (in-memory cache + production serialization), and the real
/// <see cref="SendUploadNotificationWorkflow"/> are exercised. Only the catalog boundaries (routing +
/// action), the coded-workflow registry lookup, and the email TRANSPORT
/// (<see cref="IEmailDispositionSender"/>) are doubled — a contract-shape test would not prove the
/// slice (that is how the compose 422 shipped "done").
/// </remarks>
public sealed class CodedWorkflowDispatchSeamTests
{
    private const string TenantId = "00000000-0000-0000-0000-0000000000cc";
    private const string SessionId = "77777777-7777-7777-7777-777777777777";
    private static readonly Guid BindingId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid ActionId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private const string WorkflowClassRef = nameof(SendUploadNotificationWorkflow);

    [Fact]
    public async Task CodedDispatch_UploadNotification_RunsWorkflow_StoresLedger_AndDeliversNotificationEmail()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenCodedBinding(WorkflowClassRef, BindingDisposition.Email);

        await h.DispatchAsync(
            args: new { fileName = "nda-acme.pdf" },
            actingUserEmail: "lawyer@contoso.com");

        // The Email disposition delivered exactly one notification, server-addressed to the acting user.
        h.SentEmails.Should().HaveCount(1);
        var sent = h.SentEmails.Single();
        sent.To.Should().ContainSingle().Which.Should().Be("lawyer@contoso.com");
        sent.Subject.Should().Be("Here's the file you just uploaded");
        sent.HtmlBody.Should().Contain("nda-acme.pdf", "the workflow acknowledges the operand file name");

        // Store precedes delivery (ADR-040): the email-disposition SessionOutput is in the ledger.
        var stored = await h.GetStoredOutputAsync();
        stored.Should().NotBeNull();
        stored!.Disposition.Should().Be("email");
    }

    [Fact]
    public async Task CodedDispatch_UnknownActionKind_Rejects422_NoWorkflowRun()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        // A kind outside the closed { Prompted, Coded } vocabulary — the deterministic gate rejects it.
        h.GivenBinding(new Binding
        {
            BindingId = BindingId,
            ConsumerType = "coded-seam-test",
            ActionId = ActionId,
            ActionKind = (ActionKind)999,
            Disposition = BindingDisposition.Email,
            WorkflowClass = WorkflowClassRef,
        });

        var act = () => h.DispatchToCompletionAsync(new { fileName = "x.pdf" });

        (await act.Should().ThrowAsync<DispatchRejectedException>())
            .Which.ErrorCode.Should().Be(DispatchRejectedException.ActionKindUnsupported);
        h.SentEmails.Should().BeEmpty();
    }

    [Fact]
    public async Task CodedDispatch_MissingWorkflowClass_Rejects422()
    {
        var h = new Harness();
        await h.SeedSessionAsync();
        h.GivenBinding(new Binding
        {
            BindingId = BindingId,
            ConsumerType = "coded-seam-test",
            ActionId = ActionId,
            ActionKind = ActionKind.Coded,
            Disposition = BindingDisposition.Email,
            WorkflowClass = null, // coded Action with no registered workflow ref — a catalog error
        });

        var act = () => h.DispatchToCompletionAsync(new { fileName = "x.pdf" });

        (await act.Should().ThrowAsync<DispatchRejectedException>())
            .Which.ErrorCode.Should().Be(DispatchRejectedException.ActionKindUnsupported);
        h.SentEmails.Should().BeEmpty();
    }

    private sealed class Harness
    {
        public ChatSessionManager Sessions { get; }
        public Mock<IConsumerRoutingService> Routing { get; } = new();
        public Mock<IScopeResolverService> Scope { get; } = new();
        public Mock<IEmailDispositionSender> EmailSender { get; } = new();
        public List<EmailDispositionEnvelope> SentEmails { get; } = new();
        public SessionDispatchOrchestrator Orchestrator { get; }

        public Harness()
        {
            Sessions = new ChatSessionManager(
                new InMemoryTenantCache(),
                Mock.Of<IChatDataverseRepository>(),
                Mock.Of<ILogger<ChatSessionManager>>());

            EmailSender
                .Setup(s => s.SendAsync(It.IsAny<EmailDispositionEnvelope>(), It.IsAny<CancellationToken>()))
                .Callback<EmailDispositionEnvelope, CancellationToken>((env, _) => SentEmails.Add(env))
                .Returns(Task.CompletedTask);

            var router = new OutputRouter(Sessions, Mock.Of<ILogger<OutputRouter>>(), EmailSender.Object);
            var pending = new PendingPlanManager(
                new InMemoryTenantCache(), Sessions, Mock.Of<ILogger<PendingPlanManager>>());

            // The real coded workflow behind a registry double (assembly-scan registration is an app
            // concern; the seam under test is the orchestrator resolving + executing it).
            var registry = new Mock<ICodedWorkflowRegistry>();
            registry.Setup(r => r.GetWorkflow(WorkflowClassRef)).Returns(new SendUploadNotificationWorkflow());
            registry.Setup(r => r.GetRegisteredWorkflowClassRefs()).Returns(new[] { WorkflowClassRef });

            Orchestrator = new SessionDispatchOrchestrator(
                Sessions, Routing.Object, Scope.Object,
                Mock.Of<IActionRunner>(), Mock.Of<IContextBinder>(),
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

        public void GivenCodedBinding(string workflowClassRef, BindingDisposition disposition) =>
            GivenBinding(new Binding
            {
                BindingId = BindingId,
                ConsumerType = "coded-seam-test",
                ActionId = ActionId,
                ActionKind = ActionKind.Coded,
                Disposition = disposition,
                WorkflowClass = workflowClassRef,
            });

        public void GivenBinding(Binding binding)
        {
            Routing
                .Setup(c => c.GetBindingByIdAsync(BindingId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(binding);
            // The Action is resolved (for logging/identity) before the kind branch — any row suffices.
            Scope
                .Setup(s => s.GetActionAsync(ActionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new AnalysisAction
                {
                    Id = ActionId,
                    Name = "Coded Seam Action",
                    SystemPrompt = "n/a",
                    OutputSchemaJson = "{}",
                    Temperature = 0.2m,
                });
        }

        public async Task DispatchAsync(object args, string? actingUserEmail)
        {
            var argsElement = JsonSerializer.SerializeToElement(args);
            await foreach (var _ in Orchestrator.DispatchAsync(
                new SessionDispatchRequest(TenantId, SessionId, BindingId, argsElement, ActingUserEmail: actingUserEmail)))
            {
            }
        }

        public async Task DispatchToCompletionAsync(object args)
        {
            var argsElement = JsonSerializer.SerializeToElement(args);
            await foreach (var _ in Orchestrator.DispatchAsync(
                new SessionDispatchRequest(TenantId, SessionId, BindingId, argsElement, ActingUserEmail: "seam@contoso.com")))
            {
            }
        }

        public async Task<SessionOutput?> GetStoredOutputAsync()
        {
            var session = await Sessions.GetSessionAsync(TenantId, SessionId, CancellationToken.None);
            return session?.Outputs is { Count: > 0 } ? session.Outputs[^1] : null;
        }
    }
}
