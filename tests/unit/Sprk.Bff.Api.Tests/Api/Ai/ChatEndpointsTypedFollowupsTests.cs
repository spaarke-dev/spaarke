using FluentAssertions;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// spaarkeai-assistant-enhancements-r4 task 024 (FR-04 / FR-10). Guards the typed-followups assembly the
/// <c>/api/ai/chat/sessions/{id}/messages</c> path emits as the ONE SSE <c>suggestions</c> event
/// (<see cref="ChatEndpoints.BuildTypedFollowups"/>, reached via <c>InternalsVisibleTo</c> — the same
/// precedent as this file's other ChatEndpoints internals; not reflection into a private member).
///
/// This is the endpoint-wiring regression guard that closes the D-024-01 residual: the wire payload is the
/// typed two-kind <see cref="ChatSseFollowupItem"/> shape (never the retired untyped free-string), assembled
/// in the §9a order (action → capability → question), with a capability that carries no binding id DROPPED
/// (a dead-end can never reach the wire). A revert to the ungrounded free-string generator — or dropping the
/// ordering / null-binding guard — fails these.
/// </summary>
public sealed class ChatEndpointsTypedFollowupsTests
{
    private static SuggestedFollowup Capability(string label, string? bindingId) =>
        new(SuggestedFollowupKind.Capability, bindingId, label, Reason: "why");

    private static SuggestedFollowup Question(string label) =>
        new(SuggestedFollowupKind.Question, TargetBindingId: null, label, Reason: "why");

    private static ChatSseFollowupItem Action(string actionId, string label) =>
        new("action", label, TargetBindingId: null, ActionId: actionId);

    [Fact]
    public void BuildTypedFollowups_OrdersActionThenCapabilityThenQuestion()
    {
        var actions = new[] { Action("upload", "Upload a document") };
        // Deliberately supply the grounded items question-first to prove the method re-orders (not passthrough).
        var grounded = new[]
        {
            Question("What are the risks?"),
            Capability("Summarize this document", "b-cap-1"),
        };

        var result = ChatEndpoints.BuildTypedFollowups(actions, grounded);

        // §9a wire order is action → capability → question, regardless of the grounded input order.
        result.Select(f => f.Kind).Should().Equal(new[] { "action", "capability", "question" });
    }

    [Fact]
    public void BuildTypedFollowups_DropsCapabilityWhoseBindingIdIsNull_NoDeadEndReachesTheWire()
    {
        var grounded = new[]
        {
            Capability("A wired action", "b-real"),
            Capability("An unwired dead-end", null), // a capability with no binding — must never render
        };

        var result = ChatEndpoints.BuildTypedFollowups(System.Array.Empty<ChatSseFollowupItem>(), grounded);

        result.Should().ContainSingle().Which.TargetBindingId.Should().Be("b-real");
    }

    [Fact]
    public void BuildTypedFollowups_MapsKinds_CapabilityCarriesBindingId_QuestionCarriesNone()
    {
        var grounded = new[]
        {
            Capability("Do the thing", "b-cap-2"),
            Question("How does this compare?"),
        };

        var result = ChatEndpoints.BuildTypedFollowups(System.Array.Empty<ChatSseFollowupItem>(), grounded);

        var capability = result.Should().ContainSingle(f => f.Kind == "capability").Subject;
        capability.TargetBindingId.Should().Be("b-cap-2");
        capability.Label.Should().Be("Do the thing");

        var question = result.Should().ContainSingle(f => f.Kind == "question").Subject;
        question.TargetBindingId.Should().BeNull("a question carries only its label — it re-enters the grounded loop");
        question.Label.Should().Be("How does this compare?");
    }

    [Fact]
    public void BuildTypedFollowups_WithNoSources_ReturnsEmpty_SoAbsenceIsMeaningful()
    {
        var result = ChatEndpoints.BuildTypedFollowups(
            System.Array.Empty<ChatSseFollowupItem>(),
            System.Array.Empty<SuggestedFollowup>());

        result.Should().BeEmpty("an empty followups list means the endpoint emits NO suggestions event (meaningful absence, never a padded/dead-end chip)");
    }
}
