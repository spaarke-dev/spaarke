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
}
