using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Workspace;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.LinearConsumers;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Workspace;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Unit tests for <see cref="AssistantSuggestionService"/> (spaarkeai-assistant-enhancements-r2 task 022,
/// FR-B3/B5). These protect the parse/validate contract that makes the proactive-suggestion turn
/// ADR-039-safe and FR-B5-compliant: the model's proposals are constrained to the CLOSED candidate set
/// (off-catalog / hallucinated <c>targetBindingId</c>s are DROPPED — no uncataloged capability can be
/// proposed), capped at 3, and de-duplicated; and the facade is best-effort (returns an empty list
/// rather than throwing when the AI feature is disabled or an upstream fails). Assertions are on the
/// returned chips — the behavior the endpoint (and ultimately the user) observes.
/// </summary>
public sealed class AssistantSuggestionServiceTests
{
    private readonly Mock<IActionResolver> _actionResolver = new();
    private readonly Mock<IActionRunner> _actionRunner = new();
    private readonly Mock<IConsumerRoutingService> _consumerRouting = new();
    private readonly Mock<IWorkspaceStateService> _workspaceState = new();

    private const string Doc = "document";

    private AssistantSuggestionService CreateService() => new(
        _actionResolver.Object,
        _actionRunner.Object,
        _consumerRouting.Object,
        _workspaceState.Object,
        NullLogger<AssistantSuggestionService>.Instance);

    private static Binding Candidate(Guid id) => new()
    {
        BindingId = id,
        ConsumerType = "chat-summarize",
        ToolDescription = "summarize the document",
        ContextTypeTags = new[] { Doc },
    };

    private void SetupCandidates(params Binding[] candidates) =>
        _consumerRouting
            .Setup(s => s.ListTextProjectableBindingsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

    private void SetupActionResolves() =>
        _actionResolver
            .Setup(r => r.ResolveAsync(ConsumerTypes.AssistantSuggest, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisAction { Name = "Suggest Followups" });

    private void SetupModelReturns(JsonElement output) =>
        _actionRunner
            .Setup(r => r.RunAsync(
                It.IsAny<AnalysisAction>(),
                It.IsAny<BoundInputs>(),
                It.IsAny<LinearRunContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(output);

    private static JsonElement Suggestions(params (string id, string label)[] items)
    {
        var payload = new
        {
            suggestions = items.Select(i => new { targetBindingId = i.id, label = i.label, reason = "why" }).ToArray(),
        };
        using var doc = JsonSerializer.SerializeToDocument(payload);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task SuggestAsync_DropsProposalsWhoseBindingIdIsNotAnOfferedCandidate()
    {
        var onCatalog = Guid.NewGuid();
        SetupCandidates(Candidate(onCatalog));
        SetupActionResolves();
        SetupModelReturns(Suggestions(
            (onCatalog.ToString(), "Summarize this NDA"),
            (Guid.NewGuid().ToString(), "Hallucinated off-catalog capability")));

        var chips = await CreateService().SuggestAsync("s1", "t1", Doc, activeTabId: null);

        chips.Should().ContainSingle().Which.TargetBindingId.Should().Be(onCatalog.ToString());
    }

    [Fact]
    public async Task SuggestAsync_CapsResultAtThreeChips()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        SetupCandidates(ids.Select(Candidate).ToArray());
        SetupActionResolves();
        SetupModelReturns(Suggestions(ids.Select(id => (id.ToString(), $"Do {id}")).ToArray()));

        var chips = await CreateService().SuggestAsync("s1", "t1", Doc, activeTabId: null);

        chips.Should().HaveCount(3);
    }

    [Fact]
    public async Task SuggestAsync_DeduplicatesByBindingId()
    {
        var id = Guid.NewGuid();
        SetupCandidates(Candidate(id));
        SetupActionResolves();
        SetupModelReturns(Suggestions(
            (id.ToString(), "First phrasing"),
            (id.ToString(), "Duplicate phrasing")));

        var chips = await CreateService().SuggestAsync("s1", "t1", Doc, activeTabId: null);

        chips.Should().ContainSingle().Which.Label.Should().Be("First phrasing");
    }

    [Fact]
    public async Task SuggestAsync_WithBlankContextType_ReturnsEmptyWithoutRunningTheModel()
    {
        var chips = await CreateService().SuggestAsync("s1", "t1", contextType: "  ", activeTabId: null);

        chips.Should().BeEmpty();
        _actionRunner.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SuggestAsync_WhenNoCandidateMatchesTheContextType_ReturnsEmptyWithoutRunningTheModel()
    {
        // Candidate is tagged email-only; the focused tab is a document.
        SetupCandidates(new Binding
        {
            BindingId = Guid.NewGuid(),
            ConsumerType = "chat-summarize",
            ToolDescription = "x",
            ContextTypeTags = new[] { "email" },
        });

        var chips = await CreateService().SuggestAsync("s1", "t1", Doc, activeTabId: null);

        chips.Should().BeEmpty();
        _actionRunner.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SuggestAsync_WhenActionResolverThrows_ReturnsEmpty_BestEffort()
    {
        // Simulates the AI feature being disabled (NullActionResolver throws).
        SetupCandidates(Candidate(Guid.NewGuid()));
        _actionResolver
            .Setup(r => r.ResolveAsync(ConsumerTypes.AssistantSuggest, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI disabled"));

        var chips = await CreateService().SuggestAsync("s1", "t1", Doc, activeTabId: null);

        chips.Should().BeEmpty();
    }

    [Fact]
    public async Task SuggestAsync_WhenModelRunThrows_ReturnsEmpty_BestEffort()
    {
        SetupCandidates(Candidate(Guid.NewGuid()));
        SetupActionResolves();
        _actionRunner
            .Setup(r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("completion failed"));

        var chips = await CreateService().SuggestAsync("s1", "t1", Doc, activeTabId: null);

        chips.Should().BeEmpty();
    }

    // =====================================================================================
    // spaarkeai-assistant-enhancements-r4 task 021a (FR-04) — the CONVERSATIONAL grounded
    // proposer + the typed two-kind output. These protect: (1) the closed-catalog guard still
    // holds for capabilities in the conversational path; (2) the two kinds parse into the typed
    // structure (capability carries a selected id; question carries none); (3) a question kind
    // round-trips as a safe conversational follow-on; (4) cadence is STRUCTURAL — the method has
    // no response-length gate, so a short post-capability ack still yields followups; (5) the
    // candidate scope is the UNION of the open-tab context-types; (6) the proactive contract
    // stays capability-only (questions dropped there).
    // =====================================================================================

    /// <summary>Builds a typed two-kind SUGGEST-FOLLOWUPS output: kind='capability' items carry an id, kind='question' items carry an empty id.</summary>
    private static JsonElement Followups(params (string kind, string id, string label)[] items)
    {
        var payload = new
        {
            suggestions = items
                .Select(i => new { kind = i.kind, targetBindingId = i.id, label = i.label, reason = "why" })
                .ToArray(),
        };
        using var doc = JsonSerializer.SerializeToDocument(payload);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task SuggestForConversationAsync_ParsesBothKinds_CapabilityCarriesSelectedId_QuestionCarriesNoId()
    {
        var onCatalog = Guid.NewGuid();
        SetupCandidates(Candidate(onCatalog));
        SetupActionResolves();
        SetupModelReturns(Followups(
            ("capability", onCatalog.ToString(), "Extract the NDA's parties"),
            ("question", "", "What are the confidentiality exceptions?")));

        var followups = await CreateService().SuggestForConversationAsync(
            "s1", "t1", "what does this say about confidentiality?", "It binds both parties for 3 years.",
            activeContextType: Doc, activeTabId: null, openTabContextTypes: new[] { Doc });

        followups.Should().HaveCount(2);
        var capability = followups.Should().ContainSingle(f => f.Kind == SuggestedFollowupKind.Capability).Subject;
        capability.TargetBindingId.Should().Be(onCatalog.ToString());
        var question = followups.Should().ContainSingle(f => f.Kind == SuggestedFollowupKind.Question).Subject;
        question.TargetBindingId.Should().BeNull();
        question.Label.Should().Be("What are the confidentiality exceptions?");
    }

    [Fact]
    public async Task SuggestForConversationAsync_DropsCapabilityWhoseBindingIdIsOffCatalog_KeepsQuestion()
    {
        var onCatalog = Guid.NewGuid();
        SetupCandidates(Candidate(onCatalog));
        SetupActionResolves();
        // The model names a capability id we NEVER offered (the P2 dead-end) + a safe question.
        SetupModelReturns(Followups(
            ("capability", Guid.NewGuid().ToString(), "Do an unwired thing"),
            ("question", "", "How does this compare to a standard NDA?")));

        var followups = await CreateService().SuggestForConversationAsync(
            "s1", "t1", "u", "a", activeContextType: Doc, activeTabId: null, openTabContextTypes: new[] { Doc });

        // The off-catalog capability is dropped (no dead-end); the question survives.
        followups.Should().ContainSingle()
            .Which.Kind.Should().Be(SuggestedFollowupKind.Question);
    }

    [Fact]
    public async Task SuggestForConversationAsync_TreatsBlankTargetBindingIdAsQuestion_EvenWithoutExplicitKind()
    {
        SetupCandidates(Candidate(Guid.NewGuid()));
        SetupActionResolves();
        // No 'kind', blank id ⇒ inferred as a question (tolerant parse).
        SetupModelReturns(Followups(("", "", "What happens if we breach this?")));

        var followups = await CreateService().SuggestForConversationAsync(
            "s1", "t1", "u", "a", activeContextType: Doc, activeTabId: null, openTabContextTypes: new[] { Doc });

        followups.Should().ContainSingle()
            .Which.Kind.Should().Be(SuggestedFollowupKind.Question);
    }

    [Fact]
    public async Task SuggestForConversationAsync_RunsRegardlessOfResponseLength_CadenceIsStructural()
    {
        var onCatalog = Guid.NewGuid();
        SetupCandidates(Candidate(onCatalog));
        SetupActionResolves();
        SetupModelReturns(Followups(("capability", onCatalog.ToString(), "Summarize this")));

        // A post-capability SHORT ack (the exact moment the retired <150-char gate went silent).
        var followups = await CreateService().SuggestForConversationAsync(
            "s1", "t1", "prioritize my tasks", assistantResponse: "Done.",
            activeContextType: Doc, activeTabId: null, openTabContextTypes: new[] { Doc });

        followups.Should().ContainSingle().Which.TargetBindingId.Should().Be(onCatalog.ToString());
        // The model DID run — no length gate suppressed the pass.
        _actionRunner.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SuggestForConversationAsync_ScopesCandidatesByUnionOfOpenTabContextTypes()
    {
        // The only candidate is tagged email-only; the active context is a document, but an EMAIL tab
        // is open ⇒ the union scope includes 'email' ⇒ the candidate is in scope and can be proposed.
        var emailOnly = new Binding
        {
            BindingId = Guid.NewGuid(),
            ConsumerType = "chat-summarize",
            ToolDescription = "triage this email",
            ContextTypeTags = new[] { "email" },
        };
        SetupCandidates(emailOnly);
        SetupActionResolves();
        SetupModelReturns(Followups(("capability", emailOnly.BindingId.ToString(), "Triage this email")));

        var followups = await CreateService().SuggestForConversationAsync(
            "s1", "t1", "u", "a", activeContextType: Doc, activeTabId: null,
            openTabContextTypes: new[] { "email" });

        followups.Should().ContainSingle().Which.TargetBindingId.Should().Be(emailOnly.BindingId.ToString());
    }

    [Fact]
    public async Task SuggestForConversationAsync_WhenNoCandidateInScope_ReturnsEmptyWithoutRunningTheModel()
    {
        // Candidate is email-only; nothing open, active context is a document ⇒ out of scope.
        SetupCandidates(new Binding
        {
            BindingId = Guid.NewGuid(),
            ConsumerType = "chat-summarize",
            ToolDescription = "x",
            ContextTypeTags = new[] { "email" },
        });

        var followups = await CreateService().SuggestForConversationAsync(
            "s1", "t1", "u", "a", activeContextType: Doc, activeTabId: null, openTabContextTypes: new[] { Doc });

        followups.Should().BeEmpty();
        _actionRunner.Verify(
            r => r.RunAsync(It.IsAny<AnalysisAction>(), It.IsAny<BoundInputs>(), It.IsAny<LinearRunContext>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SuggestAsync_Proactive_DropsQuestionKind_ReturnsCapabilityChipsOnly()
    {
        var onCatalog = Guid.NewGuid();
        SetupCandidates(Candidate(onCatalog));
        SetupActionResolves();
        // Even if the model emits a question in the proactive moment, the /suggest contract is capability-only.
        SetupModelReturns(Followups(
            ("capability", onCatalog.ToString(), "Summarize this document"),
            ("question", "", "What is this about?")));

        var chips = await CreateService().SuggestAsync("s1", "t1", Doc, activeTabId: null);

        chips.Should().ContainSingle().Which.TargetBindingId.Should().Be(onCatalog.ToString());
    }
}
